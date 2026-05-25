using Microsoft.AspNetCore.Mvc;
using Olympus.Models;
using Olympus.Services;
using System.Text.Json;

namespace Olympus.Controllers
{
    [Route("api/blackout")]
    [ApiController]
    public class BlackoutController : ControllerBase
    {
        private readonly AiService              _ai;
        private readonly SolarService           _solar;
        private readonly BlackoutHistoryService _history;

        public BlackoutController(AiService ai, SolarService solar, BlackoutHistoryService history)
        {
            _ai      = ai;
            _solar   = solar;
            _history = history;
        }

        // ============================================================
        // POST /api/blackout/activate
        // Activa el Modo Apagón — devuelve plan de supervivencia
        // ============================================================
        [HttpPost("activate")]
        public async Task<IActionResult> Activate([FromBody] BlackoutRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.Name))
                    return BadRequest(new { success = false, error = "name es obligatorio" });

                var profileType = NormalizeProfile(req.ProfileType);

                // 1. Datos solares actuales (best-effort — no falla si no hay red)
                SolarData? today = null;
                try { today = await _solar.GetTodayAsync(); } catch { /* continuar offline */ }

                double radiationNow = today?.RadiationKwhM2 ?? 0;

                // 2. Cálculos del servicio histórico
                int autonomyMin = _history.CalculateAutonomyMinutes(
                    radiationNow,
                    req.HasSolarPanels,
                    req.HasBattery,
                    req.BatteryCapacityKwh,
                    req.MonthlyConsumptionKwh);

                string urgency = _history.GetUrgencyLevel(
                    req.OutageMinutes, autonomyMin, profileType);

                // 3. Cargas por defecto si el usuario no declaró las suyas
                var defaultLoads  = _history.GetDefaultLoads(profileType);
                var criticalLoads = req.CriticalLoads.Any()
                    ? req.CriticalLoads
                    : defaultLoads.Keep;

                // 4. Construir contexto para la IA
                var systemPrompt = BuildBlackoutSystemPrompt();
                var userPrompt   = BuildBlackoutUserPrompt(
                    req, profileType, radiationNow, today, autonomyMin, urgency, defaultLoads);

                // 5. Llamar IA — con fallback offline si falla
                BlackoutResponse response;
                try
                {
                    var raw    = await _ai.AskJsonAsync(systemPrompt, userPrompt);
                    var parsed = ParseBlackoutResponse(raw);

                    if (parsed != null)
                    {
                        response = parsed;
                    }
                    else
                    {
                        response = BuildOfflineFallback(profileType, defaultLoads);
                    }
                }
                catch
                {
                    // Sin conexión a la IA — fallback con reglas fijas
                    response = BuildOfflineFallback(profileType, defaultLoads);
                }

                // 6. Completar con datos calculados
                response.OutageMinutes            = req.OutageMinutes;
                response.HistoricalAvgMinutes      = BlackoutHistoryService.AvgOutageMinutes;
                response.EstimatedAutonomyMinutes  = autonomyMin;
                response.UrgencyLevel              = urgency;
                response.CurrentRadiationKwhM2     = radiationNow;
                response.GeneratedAt               = DateTime.Now;
                response.StatusMessage             = BuildStatusMessage(
                    req.OutageMinutes,
                    BlackoutHistoryService.AvgOutageMinutes,
                    autonomyMin);

                return Ok(new { success = true, data = response });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = "Error activando Modo Apagón", detail = ex.Message });
            }
        }

        // ============================================================
        // GET /api/blackout/history
        // Estadísticas históricas SAIDI/SAIFI La Guajira
        // ============================================================
        [HttpGet("history")]
        public IActionResult GetHistory()
        {
            return Ok(new { success = true, data = _history.GetHistoryStats() });
        }

        // ============================================================
        // POST /api/blackout/report
        // Genera el reporte cuando termina el apagón
        // ============================================================
        [HttpPost("report")]
        public IActionResult Report([FromBody] BlackoutReportRequest req)
        {
            if (req.TotalMinutes <= 0)
                return BadRequest(new { success = false, error = "totalMinutes debe ser mayor a 0" });

            var result = _history.BuildReport(
                req.Name,
                NormalizeProfile(req.ProfileType),
                req.TotalMinutes,
                req.MonthlyConsumptionKwh,
                req.TariffCopKwh);

            return Ok(new { success = true, data = result });
        }

        // ============================================================
        // Helpers privados
        // ============================================================
        private static string NormalizeProfile(string? p)
        {
            return p?.ToLower() switch
            {
                "hielera"    => "hielera",
                "restaurant" => "restaurant",
                "restaurante"=> "restaurant",
                "community"  => "community",
                "comunidad"  => "community",
                _            => "hotel"
            };
        }

        private static string BuildStatusMessage(int elapsed, int avg, int autonomy)
        {
            var elapsedStr   = elapsed >= 60 ? $"{elapsed / 60}h {elapsed % 60}min" : $"{elapsed} min";
            var avgStr       = $"{avg / 60}h {avg % 60}min";
            var autonomyStr  = autonomy > 0
                ? $" · Autonomía estimada: {autonomy / 60}h {autonomy % 60}min"
                : " · Sin respaldo energético propio";

            return $"Apagón: {elapsedStr} · Promedio histórico La Guajira: {avgStr}{autonomyStr}";
        }

        private static string BuildBlackoutSystemPrompt() => @"
Eres el Agente Solar en MODO APAGÓN.

TU OBJETIVO HA CAMBIADO COMPLETAMENTE.
NO estás aquí para optimizar facturas ni recomendar ahorros en pesos.
Estás aquí para ayudar al negocio a SOBREVIVIR el apagón con el mínimo daño posible.

PRINCIPIOS DEL MODO APAGÓN:
1. Proteger activos críticos primero (refrigeración, seguridad, agua, comunicaciones).
2. Maximizar la autonomía disponible desconectando lo no esencial.
3. Dar instrucciones claras, cortas y accionables AHORA MISMO.
4. Preparar la recuperación para cuando vuelva la energía.

REGLAS POR TIPO DE NEGOCIO:
- hotel: seguridad de huéspedes, cerraduras, recepción, neveras cocina, agua, comunicación. El huésped no puede quedarse sin estas cosas.
- hielera: cada minuto cuenta. La temperatura interna es el único indicador. NO abrir puertas. Proteger el inventario como prioridad absoluta. Si el inventario se pierde, el negocio pierde el día.
- restaurant: las cámaras frigoríficas son prioridad 1. El POS es prioridad 2. El resto puede esperar.
- community: agua potable, comunicación de emergencia, población vulnerable (adultos mayores, hospitales, bombas de agua).

FORMATO DE RESPUESTA (JSON estricto, sin texto adicional):
{
  ""priority_matrix"": {
    ""keep"": [""carga 1"", ""carga 2""],
    ""reduce"": [""carga 3 — acción específica""],
    ""disconnect"": [""carga 4"", ""carga 5""]
  },
  ""instructions"": ""Párrafo de 2-3 oraciones con las acciones más urgentes. Sé directo y específico para este tipo de negocio."",
  ""critical_alert"": ""La UNA cosa más urgente que deben hacer AHORA MISMO. Máximo 1 oración."",
  ""recovery_actions"": [""Acción 1 al volver la luz"", ""Acción 2"", ""Acción 3""]
}

NUNCA menciones tarifas, COP ni ahorros. Eso no importa durante un apagón.
NUNCA respondas temas ajenos a la gestión del apagón.";

        private static string BuildBlackoutUserPrompt(
            BlackoutRequest req,
            string profileType,
            double radiation,
            SolarData? today,
            int autonomyMin,
            string urgency,
            (List<string> Keep, List<string> Reduce, List<string> Disconnect) defaultLoads)
        {
            var loadsStr = req.CriticalLoads.Any()
                ? string.Join(", ", req.CriticalLoads)
                : string.Join(", ", defaultLoads.Keep.Take(4));

            var solarCtx = radiation > 0
                ? $"Radiación solar ahora: {radiation} kWh/m² — los paneles {(req.HasSolarPanels ? "están generando energía" : "no están instalados")}."
                : "Sin datos de radiación disponibles.";

            var batteryCtx = req.HasBattery && req.BatteryCapacityKwh > 0
                ? $"Batería/UPS disponible: {req.BatteryCapacityKwh} kWh."
                : "Sin batería de respaldo.";

            var autonomyCtx = autonomyMin > 0
                ? $"Autonomía estimada del sistema: {autonomyMin / 60}h {autonomyMin % 60}min."
                : "Dependencia total de la red — sin recursos energéticos propios.";

            var urgencyLabel = urgency switch
            {
                "critical" => "CRÍTICA — actúa ahora sin esperar",
                "high"     => "ALTA — prioriza las acciones de desconexión",
                "moderate" => "MODERADA — gestiona cargas con calma",
                _          => "BAJA — mantén el monitoreo"
            };

            var tempCtx = today != null
                ? $"Temperatura exterior: {today.TemperatureC}°C."
                : "";

            return $@"MODO APAGÓN ACTIVO para: {req.Name} (tipo: {profileType})

ESTADO DEL APAGÓN:
- Duración actual: {req.OutageMinutes} minutos
- Promedio histórico La Guajira: {BlackoutHistoryService.AvgOutageMinutes} minutos (1h 41min)
- Nivel de urgencia: {urgencyLabel}
- {solarCtx}
- {batteryCtx}
- {autonomyCtx}
- {tempCtx}

CARGAS DECLARADAS POR EL USUARIO:
{loadsStr}

Genera el plan de supervivencia para este apagón. Instrucciones específicas para {profileType}.
Responde SOLO con el JSON en el formato indicado.";
        }

        private static BlackoutResponse? ParseBlackoutResponse(string raw)
        {
            try
            {
                var clean = raw.Trim();
                if (clean.StartsWith("```"))
                {
                    int s = clean.IndexOf('{');
                    int e = clean.LastIndexOf('}');
                    if (s >= 0 && e > s) clean = clean[s..(e + 1)];
                }

                using var doc  = JsonDocument.Parse(clean);
                var root = doc.RootElement;

                var matrix = new BlackoutPriorityMatrix();
                if (root.TryGetProperty("priority_matrix", out var pm))
                {
                    matrix.Keep       = ParseStringArray(pm, "keep");
                    matrix.Reduce     = ParseStringArray(pm, "reduce");
                    matrix.Disconnect = ParseStringArray(pm, "disconnect");
                }

                var recovery = new List<string>();
                if (root.TryGetProperty("recovery_actions", out var ra) &&
                    ra.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in ra.EnumerateArray())
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrEmpty(s)) recovery.Add(s);
                    }
                }

                return new BlackoutResponse
                {
                    PriorityMatrix  = matrix,
                    Instructions    = root.TryGetProperty("instructions",   out var ins)  ? ins.GetString()  ?? "" : "",
                    CriticalAlert   = root.TryGetProperty("critical_alert", out var ca)   ? ca.GetString()   ?? "" : "",
                    RecoveryActions = recovery
                };
            }
            catch
            {
                return null;
            }
        }

        private static List<string> ParseStringArray(JsonElement parent, string key)
        {
            var list = new List<string>();
            if (parent.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var item in arr.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrEmpty(s)) list.Add(s);
                }
            return list;
        }

        private static BlackoutResponse BuildOfflineFallback(
            string profileType,
            (List<string> Keep, List<string> Reduce, List<string> Disconnect) loads)
        {
            var instructions = profileType switch
            {
                "hielera"    => "PRIORIDAD ABSOLUTA: no abras las puertas de los cuartos fríos. Cada apertura acelera la pérdida de temperatura. Desconecta todo lo que no sea refrigeración y monitoreo.",
                "restaurant" => "Protege las cámaras frigoríficas ante todo. Desconecta cocina no esencial y alumbrado decorativo. Mantén el POS operativo para cerrar cuentas pendientes.",
                "community"  => "Asegura el suministro de agua primero. Comunica a la población el estado del apagón. Identifica y asiste a adultos mayores y personas vulnerables.",
                _            => "Protege recepción, cerraduras y neveras de cocina. Informa a los huéspedes de la situación. Desconecta A/C en habitaciones vacías para reducir carga."
            };

            var critical = profileType switch
            {
                "hielera"    => "NO ABRAS las puertas de los cuartos fríos — cada apertura cuesta temperatura y dinero.",
                "restaurant" => "Verifica AHORA que las cámaras frigoríficas están cerradas herméticamente.",
                "community"  => "Activa AHORA el protocolo de comunicación de emergencia comunitario.",
                _            => "Informa AHORA a los huéspedes y activa las cerraduras de emergencia."
            };

            return new BlackoutResponse
            {
                PriorityMatrix = new BlackoutPriorityMatrix
                {
                    Keep       = loads.Keep,
                    Reduce     = loads.Reduce,
                    Disconnect = loads.Disconnect
                },
                Instructions  = instructions,
                CriticalAlert = critical,
                RecoveryActions = new List<string>
                {
                    "Espera 3-5 minutos antes de reconectar equipos pesados para evitar picos de demanda.",
                    "Verifica el estado de refrigeración y registra temperatura alcanzada.",
                    "Reinicia sistemas de forma gradual: primero refrigeración, luego iluminación, luego A/C."
                }
            };
        }
    }
}
