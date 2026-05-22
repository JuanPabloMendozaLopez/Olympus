using Microsoft.AspNetCore.Mvc;
using Olympus.Models;
using Olympus.Services;
using System.Text.Json;

namespace Olympus.Controllers
{
    [Route("api/ai")]
    [ApiController]
    public class AiController : ControllerBase
    {
        private readonly AiService _groq;
        private readonly SolarService _solar;

        public AiController(AiService groq, SolarService solar)
        {
            _groq = groq;
            _solar = solar;
        }

        // ============================================================
        // POST /api/ai/recommendations
        // Genera 3 recomendaciones — modo empresa o comunidad
        // ============================================================
        [HttpPost("recommendations")]
        public async Task<IActionResult> GetRecommendations([FromBody] RecommendationRequest req)
        {
            try
            {
                // Validación
                if (string.IsNullOrWhiteSpace(req.Name))
                    return BadRequest(new { success = false, error = "name es obligatorio" });

                var targetType = NormalizeTarget(req.TargetType);
                if (targetType == "company" && string.IsNullOrWhiteSpace(req.CompanyType))
                    return BadRequest(new { success = false, error = "companyType es obligatorio en modo company" });

                // 1. Datos solares reales — en paralelo para reducir latencia
                var todayTask = _solar.GetTodayAsync();
                var avgTask = _solar.GetHistoricalAverageAsync(90);
                var forecastTask = _solar.GetForecastAsync(3);
                await Task.WhenAll(todayTask, avgTask, forecastTask);

                var today = await todayTask;
                if (today == null)
                    return StatusCode(503, new { success = false, error = "No se pudieron obtener datos solares" });

                var historicalAvg = await avgTask;
                var forecast = await forecastTask;

                // 2. Bloque de datos solares (común a ambos modos)
                var comparacion = historicalAvg > 0
                    ? $"El promedio histórico de los últimos 90 días es {historicalAvg} kWh/m². " +
                      $"Hoy está {(today.RadiationKwhM2 > historicalAvg ? "POR ENCIMA" : "POR DEBAJO")} del promedio."
                    : "";

                // Hora actual — para recomendaciones inmediatas vs. planificación
                var horaActual = DateTime.Now;
                var horarioSolar = horaActual.Hour >= 9 && horaActual.Hour <= 17
                    ? "DENTRO del horario solar (9:00-17:00)"
                    : "FUERA del horario solar — planifica para mañana";

                // Pronóstico próximos 2 días
                var pronostico = "";
                if (forecast.Count > 1)
                {
                    var m = forecast[1];
                    pronostico = $"\nPRONÓSTICO:\n- Mañana ({m.Date:dd/MM}): {m.RadiationKwhM2} kWh/m², {m.SolarIndexLabel}, {m.TemperatureC}°C";
                    if (forecast.Count > 2)
                    {
                        var p = forecast[2];
                        pronostico += $"\n- Pasado ({p.Date:dd/MM}): {p.RadiationKwhM2} kWh/m², {p.SolarIndexLabel}, {p.TemperatureC}°C";
                    }
                }

                var datosSolares = $@"DATOS SOLARES DE HOY ({today.Date:yyyy-MM-dd}):
                - Hora actual en Riohacha: {horaActual:HH:mm} ({horarioSolar})
                - Radiación: {today.RadiationKwhM2} kWh/m²/día
                - Índice Solar: {today.SolarIndex}/100 ({today.SolarIndexLabel})
                - Temperatura máxima: {today.TemperatureC}°C
                - Viento máximo: {today.WindSpeedKmh} km/h
                - Horas óptimas: {string.Join(", ", today.OptimalHours)}
                - Horas pico tarifario: {string.Join(", ", today.PeakCostHours)}
                {comparacion}{pronostico}";

                // 3. User prompt según el modo
                var systemPrompt = BuildSystemPrompt();
                var userPrompt = targetType == "community"
                    ? BuildCommunityPrompt(req, datosSolares)
                    : BuildCompanyPrompt(req, datosSolares);

                // 4. Llamar a Groq
                var raw = await _groq.AskJsonAsync(systemPrompt, userPrompt);

                // 5. Parsear
                var parsed = ParseRecommendations(raw);
                if (parsed == null)
                    return StatusCode(502, new { success = false, error = "La IA devolvió un formato inválido" });

                // 6. Completar con datos conocidos
                parsed.TargetType = targetType;
                parsed.Name = req.Name;
                parsed.Date = today.Date;
                parsed.RadiationToday = today.RadiationKwhM2;
                parsed.SolarIndex = today.SolarIndex;
                parsed.TotalSavingsCopDay = parsed.Recommendations.Sum(r => r.SavingsCopDay);
                parsed.TotalSavingsCopMonth = parsed.TotalSavingsCopDay * 30L;

                return Ok(new { success = true, data = parsed });
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(503, new { success = false, error = "Servicio de IA no disponible", detail = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = "Error generando recomendaciones", detail = ex.Message });
            }
        }

        // ============================================================
        // POST /api/ai/chat
        // ============================================================
        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] ChatRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.Message))
                    return BadRequest(new { success = false, error = "El mensaje no puede estar vacío" });

                var targetType = NormalizeTarget(req.TargetType);
                var today = await _solar.GetTodayAsync();

                var contexto = today != null
                    ? $"Radiación hoy: {today.RadiationKwhM2} kWh/m², Índice Solar: {today.SolarIndex}/100, " +
                      $"Temperatura: {today.TemperatureC}°C."
                    : "Datos solares no disponibles en este momento.";

                // Contexto del objetivo según el modo
                string contextoObjetivo;
                if (targetType == "community")
                {
                    contextoObjetivo = $@"El usuario consulta a nivel COMUNITARIO.
                                        - Comunidad: {req.Name}
                                        - Población estimada: {req.PopulationEstimate?.ToString() ?? "no especificada"}";
                }
                else
                {
                    var perfilConsumo = BuildConsumoContext(req.MonthlyConsumptionKwh ?? 0, req.CompanySize, req.TariffCopKwh ?? 943, req.OperatingHoursPerDay);
                    var cargas = req.MainLoads != null && req.MainLoads.Any()
                        ? $"Cargas principales: {string.Join(", ", req.MainLoads)}."
                        : "";
                    contextoObjetivo = $@"El usuario consulta para una EMPRESA.
                                        - Nombre: {req.Name}
                                        - Tipo: {req.CompanyType ?? "no especificado"}
                                        {perfilConsumo}
                                        {cargas}";
                }

                var systemPrompt = BuildSystemPrompt();
                var userPrompt = $@"CONTEXTO SOLAR ACTUAL: {contexto}

                                {contextoObjetivo}

                                Pregunta del usuario: {req.Message}

                                Responde en lenguaje natural, máximo 3 oraciones, con horarios y cifras en pesos colombianos cuando sea relevante.";

                var raw = await _groq.AskTextAsync(systemPrompt, userPrompt);

                return Ok(new
                {
                    success = true,
                    data = new ChatResult { Reply = raw.Trim(), Timestamp = DateTime.Now }
                });
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(503, new { success = false, error = "Servicio de IA no disponible", detail = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = "Error en el chat", detail = ex.Message });
            }
        }

        // ============================================================
        // POST /api/ai/simulate
        // Simulación de ROI solar — cálculo puro, sin LLM
        // ============================================================
        [HttpPost("simulate")]
        public IActionResult Simulate([FromBody] SimulateRequest req)
        {
            if (req.KwpInstalled <= 0 || req.KwpInstalled > 500)
                return BadRequest(new { success = false, error = "kWp debe estar entre 0.5 y 500" });
            if (req.TariffCopKwh <= 0)
                return BadRequest(new { success = false, error = "tariffCopKwh debe ser mayor a 0" });

            // Constantes Riohacha
            const double PeakSunHours   = 5.0;       // HSP promedio anual
            const double Efficiency     = 0.80;       // pérdidas inversor/temperatura/cables
            const double CostPerKwp     = 3_500_000;  // COP/kWp instalado (promedio Colombia 2025)
            const double Co2FactorKgKwh = 0.214;      // kg CO₂ por kWh evitado — factor red Colombia

            var annualGen   = req.KwpInstalled * PeakSunHours * Efficiency * 365;
            var investment  = req.KwpInstalled * CostPerKwp;
            var annualSav   = annualGen * req.TariffCopKwh;
            var payback     = annualSav > 0 ? investment / annualSav : 999;
            var co2Year     = annualGen * Co2FactorKgKwh;
            var annualKwh   = req.MonthlyConsumptionKwh * 12;
            var coverage    = annualKwh > 0 ? Math.Min(100, (annualGen / annualKwh) * 100) : 0;
            // ROI a 25 años (vida útil del sistema)
            var roi25       = investment > 0 ? ((annualSav * 25 - investment) / investment) * 100 : 0;

            var result = new SimulateResult
            {
                KwpInstalled        = Math.Round(req.KwpInstalled, 1),
                AnnualGenerationKwh = Math.Round(annualGen),
                AnnualSavingsCop    = Math.Round(annualSav),
                MonthlySavingsCop   = Math.Round(annualSav / 12),
                DailySavingsCop     = Math.Round(annualSav / 365),
                InvestmentCop       = Math.Round(investment),
                PaybackYears        = Math.Round(payback, 1),
                Co2ReductionKgYear  = Math.Round(co2Year),
                CoveragePercent     = Math.Round(coverage, 1),
                Roi25Years          = Math.Round(roi25, 1),
                ProfileName         = req.ProfileName,
                Summary             = $"{req.KwpInstalled:F1} kWp cubriría el {coverage:F0}% del consumo de {req.ProfileName}. " +
                                      $"Payback estimado: {payback:F1} años con radiación promedio de Riohacha.",
            };

            return Ok(new { success = true, data = result });
        }

        // ============================================================
        // POST /api/ai/insights
        // Resumen ejecutivo diario generado por IA
        // ============================================================
        [HttpPost("insights")]
        public async Task<IActionResult> GetInsights([FromBody] InsightsRequest req)
        {
            try
            {
                var today = await _solar.GetTodayAsync();
                if (today == null)
                    return StatusCode(503, new { success = false, error = "Datos solares no disponibles" });

                var avgTask = _solar.GetHistoricalAverageAsync(90);
                var forecastTask = _solar.GetForecastAsync(3);
                await Task.WhenAll(avgTask, forecastTask);

                var avg = await avgTask;
                var forecast = await forecastTask;

                var trend = avg > 0
                    ? (today.RadiationKwhM2 > avg ? $"SUPERIOR al promedio 90d ({avg} kWh/m²)" : $"INFERIOR al promedio 90d ({avg} kWh/m²)")
                    : "";

                var forecastStr = forecast.Count > 1
                    ? $"Mañana: {forecast[1].RadiationKwhM2} kWh/m² ({forecast[1].SolarIndexLabel}). "
                    : "";

                var contextoPerfil = req.TargetType == "community"
                    ? $"Comunidad: {req.Name}, ~{req.PopulationEstimate?.ToString("N0") ?? "246.000"} habitantes."
                    : $"Empresa: {req.Name} ({req.CompanyType}), consumo {req.MonthlyConsumptionKwh?.ToString("N0") ?? "?"} kWh/mes, tarifa {req.TariffCopKwh?.ToString("N0") ?? "943"} COP/kWh.";

                var systemPrompt = BuildSystemPrompt();
                var userPrompt = $@"Genera un resumen ejecutivo energético en formato JSON para:
{contextoPerfil}

DATOS SOLARES HOY ({today.Date:yyyy-MM-dd}):
- Radiación: {today.RadiationKwhM2} kWh/m² ({trend})
- Índice Solar: {today.SolarIndex}/100 ({today.SolarIndexLabel})
- Temperatura: {today.TemperatureC}°C
- Horas óptimas: {string.Join(", ", today.OptimalHours)}
- {forecastStr}

Genera 3 insights accionables y un titular ejecutivo.

FORMATO JSON ESTRICTO:
{{
  ""headline"": ""Titular ejecutivo de 1 línea con cifra concreta"",
  ""executive_summary"": ""Párrafo de 2-3 oraciones con el análisis del día y oportunidad principal"",
  ""insights"": [
    {{
      ""icon"": ""emoji relevante"",
      ""title"": ""Título corto del insight"",
      ""body"": ""Explicación 1-2 oraciones con cifras"",
      ""impact"": ""alto|medio|bajo""
    }}
  ],
  ""solar_context"": ""Una frase sobre el potencial solar de Riohacha hoy""
}}
Genera exactamente 3 insights. Solo JSON, sin texto adicional.";

                var raw = await _groq.AskJsonAsync(systemPrompt, userPrompt);
                var result = ParseInsights(raw, today);

                if (result == null)
                    return StatusCode(502, new { success = false, error = "Formato de respuesta IA inválido" });

                return Ok(new { success = true, data = result });
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(503, new { success = false, error = "Servicio IA no disponible", detail = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = "Error generando insights", detail = ex.Message });
            }
        }

        // ============================================================
        // POST /api/ai/alerts
        // Alertas predictivas energéticas generadas por IA
        // ============================================================
        [HttpPost("alerts")]
        public async Task<IActionResult> GetAlerts([FromBody] AlertsRequest req)
        {
            try
            {
                var todayTask   = _solar.GetTodayAsync();
                var forecastTask = _solar.GetForecastAsync(3);
                await Task.WhenAll(todayTask, forecastTask);

                var today    = await todayTask;
                var forecast = await forecastTask;

                if (today == null)
                    return StatusCode(503, new { success = false, error = "Datos solares no disponibles" });

                var contextoPerfil = req.TargetType == "company"
                    ? $"Empresa {req.Name} ({req.CompanyType}), tarifa {req.TariffCopKwh?.ToString("N0") ?? "1050"} COP/kWh, consumo {req.MonthlyConsumptionKwh?.ToString("N0") ?? "?"} kWh/mes."
                    : $"Comunidad {req.Name}.";

                var forecastStr = forecast.Count > 1
                    ? string.Join("; ", forecast.Skip(1).Take(2).Select(f => $"{f.Date:dd/MM}: {f.RadiationKwhM2} kWh/m² ({f.SolarIndexLabel})"))
                    : "";

                var horaActual = DateTime.Now.Hour;
                var horaSolar  = horaActual >= 9 && horaActual <= 17;

                var systemPrompt = BuildSystemPrompt();
                var userPrompt = $@"Genera alertas energéticas predictivas en JSON para:
{contextoPerfil}

HOY: Radiación {today.RadiationKwhM2} kWh/m², Índice {today.SolarIndex}/100, Temp {today.TemperatureC}°C, {(horaSolar ? "DENTRO" : "FUERA")} del horario solar.
PRONÓSTICO PRÓXIMOS DÍAS: {forecastStr}
CONTEXTO RIOHACHA: promedio apagones 60h/año, tarifa Air-e fluctúa entre 780-1050 COP/kWh.

Genera exactamente 4 alertas (mínimo 1 crítica, 2 advertencias, 1 informativa).
Las alertas deben ser predictivas y accionables.

FORMATO JSON:
{{
  ""alerts"": [
    {{
      ""type"": ""critical|warning|info"",
      ""icon"": ""emoji"",
      ""title"": ""Título corto"",
      ""message"": ""Explicación con cifras y horarios"",
      ""action"": ""Acción concreta a tomar"",
      ""time_window"": ""HH:MM - HH:MM o descripción temporal""
    }}
  ],
  ""solar_summary"": ""Resumen de 1 línea del contexto solar del día""
}}
Solo JSON, sin texto adicional.";

                var raw = await _groq.AskJsonAsync(systemPrompt, userPrompt);
                var result = ParseAlerts(raw, today);

                if (result == null)
                    return StatusCode(502, new { success = false, error = "Formato de respuesta IA inválido" });

                return Ok(new { success = true, data = result });
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(503, new { success = false, error = "Servicio IA no disponible", detail = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = "Error generando alertas", detail = ex.Message });
            }
        }

        // ============================================================
        // Helpers privados
        // ============================================================
        private static string NormalizeTarget(string? t)
            => string.Equals(t, "community", StringComparison.OrdinalIgnoreCase) ? "community" : "company";

        private static string BuildCompanyPrompt(RecommendationRequest req, string datosSolares)
        {
            var tariff = req.TariffCopKwh ?? 943;
            var perfilConsumo = BuildConsumoContext(req.MonthlyConsumptionKwh ?? 0, req.CompanySize, tariff, req.OperatingHoursPerDay);
            var cargas = req.MainLoads != null && req.MainLoads.Any()
                ? $"Cargas principales del negocio: {string.Join(", ", req.MainLoads)}."
                : "";
            var horasPico = !string.IsNullOrWhiteSpace(req.PeakUsageHours)
                ? $"Franja de mayor consumo declarada: {req.PeakUsageHours}."
                : "";

            return $@"Genera recomendaciones de ahorro energético para esta EMPRESA:
                    - Nombre: {req.Name}
                    - Tipo: {req.CompanyType}

                    {perfilConsumo}
                    {cargas}
                    {horasPico}

                    {datosSolares}

                    Las recomendaciones deben ser acciones concretas aplicables al negocio.
                    USA los rangos de ahorro calculados arriba como ancla — no los ignores.
                    Responde SOLO con el JSON en el formato indicado.";
        }

        private static string BuildCommunityPrompt(RecommendationRequest req, string datosSolares)
        {
            var problemas = req.MainProblems != null && req.MainProblems.Any()
                ? $"Problemas energéticos principales: {string.Join(", ", req.MainProblems)}."
                : "";

            return $@"Genera recomendaciones de ahorro energético a nivel COMUNITARIO para:
                    - Comunidad: {req.Name}
                    - Población estimada: {req.PopulationEstimate?.ToString() ?? "no especificada"}
                    {problemas}

                    {datosSolares}

                    Las recomendaciones deben ser políticas públicas, campañas o acciones colectivas
                    aplicables a la comunidad, no a un negocio individual.
                    Los ahorros deben estimarse a escala poblacional.
                    Responde SOLO con el JSON en el formato indicado.";
        }

        private static string BuildConsumoContext(double monthlyKwh, int? size, double tariff = 943, double? operatingHours = null)
        {
            if (monthlyKwh <= 0)
                return "PERFIL DE CONSUMO: no proporcionado. Estima según un negocio típico del tipo indicado e indícalo en el reasoning.";

            var dailyKwh       = Math.Round(monthlyKwh / 30, 1);
            var dailyCostCop   = Math.Round(dailyKwh * tariff);
            var monthlyCostCop = Math.Round(monthlyKwh * tariff);
            // Anclas para el modelo: rango por recomendación (2% – 15%) y tope del conjunto (40%)
            var minSavingsPerRec  = (long)Math.Round(dailyCostCop * 0.02);
            var maxSavingsPerRec  = (long)Math.Round(dailyCostCop * 0.15);
            var maxTotalSavings   = (long)Math.Round(dailyCostCop * 0.40);

            var horasStr = operatingHours.HasValue
                ? $"\n                    - Horas de operación diarias: {operatingHours.Value} h/día"
                : "";

            return $@"PERFIL DE CONSUMO DE LA EMPRESA:
                    - Consumo mensual declarado: {monthlyKwh:N0} kWh
                    - Consumo diario promedio: {dailyKwh} kWh/día
                    - Tarifa eléctrica real: {tariff:N0} COP/kWh
                    - Costo diario base: {dailyCostCop:N0} COP/día
                    - Costo mensual estimado: {monthlyCostCop:N0} COP/mes
                    - Tamaño / empleados: {size?.ToString() ?? "no especificado"}{horasStr}

                    ANCLAS DE AHORRO (OBLIGATORIAS — úsalas para calcular savings_cop_day):
                    - Mínimo por recomendación: {minSavingsPerRec:N0} COP/día (2% del costo diario)
                    - Máximo por recomendación: {maxSavingsPerRec:N0} COP/día (15% del costo diario)
                    - Máximo total 3 recomendaciones: {maxTotalSavings:N0} COP/día (40% del costo diario)
                    - Fórmula: ahorro_kwh × {tariff:N0} COP/kWh = savings_cop_day";
        }

        private static string BuildSystemPrompt()
        {
            return @"Eres el Agente Solar, un copiloto energético inteligente especializado en
                    optimización del consumo eléctrico en Riohacha, La Guajira, Colombia.

                    CONTEXTO DEL TERRITORIO:
                    - Riohacha tiene hasta 7.0 kWh/m²/día de radiación, el mayor potencial de Colombia.
                    - Las tarifas eléctricas de Air-e La Guajira oscilan entre 780 (residencial) y
                      1.050 COP/kWh (comercial). Siempre usa la tarifa declarada en el perfil.
                    - Hay apagones promedio de 60 horas/año.
                    - La energía representa hasta el 33% del OpEx de las PYMES locales.

                    MODOS DE OPERACIÓN:
                    - Modo EMPRESA: recomendaciones para un negocio concreto (hotel, hielera, retail, restaurante).
                      Las acciones son operativas y los ahorros se calculan sobre el consumo del negocio.
                    - Modo COMUNIDAD: recomendaciones a nivel poblacional (políticas, campañas, acciones
                      colectivas). Los ahorros se estiman a escala de la comunidad.

                    REGLAS DE RAZONAMIENTO:
                    1. Si radiation > 6.5 → recomendar mover cargas pesadas al mediodía solar.
                    2. Si radiation < 5.0 → advertir sobre menor generación, priorizar ahorro nocturno.
                    3. Si temperatura > 34°C → el A/C consume hasta 25% más; gestionar horarios.
                    4. Usa SIEMPRE la tarifa declarada en el perfil (campo ""tarifa eléctrica real"") para
                       calcular ahorros. NUNCA uses 943 si la tarifa declarada es distinta.
                    5. Usa la hora actual para decidir si las acciones son inmediatas (""hazlo ahora"")
                       o de planificación (""para mañana""). Si la hora está fuera del horario solar,
                       orienta las recomendaciones a preparar el día siguiente.
                    6. Si mañana tiene menos radiación que hoy, úsalo en el alert: recomienda concentrar
                       el consumo pesado hoy. Si mañana tiene más radiación, indícalo como oportunidad.
                    7. RAZONAMIENTO OBLIGATORIO: en el campo ""reasoning"" muestra el cálculo paso a paso:
                       (a) radiación vs. promedio histórico, (b) carga principal del negocio y su kWh/día,
                       (c) kWh que ahorra cada acción × tarifa = COP. Cita cifras concretas.

                    REGLA DE CÁLCULO DE AHORRO:
                    El perfil te entrega: costo_diario_base, mínimo y máximo por recomendación, y tope total.
                    DEBES usar esas anclas. La fórmula es: ahorro_kwh_acción × tarifa_declarada = savings_cop_day.
                    Si NO recibes consumo, indícalo en el reasoning y aclara que son estimaciones aproximadas.
                    NUNCA devuelvas savings_cop_day menores a 1.000 para una empresa con consumo declarado.

                    RANGOS DE AHORRO (de referencia — prevalecen las anclas calculadas en el perfil):
                    - Hotel (~400 kWh/día, 1050 COP/kWh): 8.400–63.000 COP/día por recomendación.
                    - Hielera (~933 kWh/día, 850 COP/kWh): 15.900–118.000 COP/día por recomendación.
                    - Restaurante (~283 kWh/día, 1050 COP/kWh): 5.900–44.600 COP/día por recomendación.
                    - Comunidad (escala poblacional): 100.000–1.500.000 COP/día por recomendación.

                    ─────────────────────────────────────────────────
                    EJEMPLO 1 — RESTAURANTE (283 kWh/día, tarifa 1050, costo_diario 297.150 COP, hora 14:30):
                    Anclas: min 5.943 COP, max 44.573 COP por rec, tope total 118.860 COP.
                    {
                      ""reasoning"": ""Radiación 6.4 kWh/m² (por encima del promedio 90d de 5.9). Hora 14:30 — ventana solar activa 90 min más. Cocina industrial = ~40% consumo (113 kWh × 1050 = 118.650 COP/día). Moverla fuera del pico 18-21h ahorra ~25 kWh × 1050 = 26.250 COP. A/C (25% = 70 kWh): subir termostato 2°C ahorra 8 kWh × 1050 = 8.400 COP. Pre-enfriar cámaras ahorra 6.5 kWh de ciclos nocturnos × 1050 = 6.825 COP."",
                      ""recommendations"": [
                        {""title"": ""Adelantar mise en place al pico solar"", ""description"": ""Termina cortes, salsas y preparaciones base antes de las 16:00. Evita encender la plancha industrial entre 18:00-21:00 cuando la tarifa es máxima."", ""priority"": ""alta"", ""time_window"": ""14:30 - 16:00"", ""savings_cop_day"": 26250},
                        {""title"": ""Pre-enfriar cámaras antes del pico"", ""description"": ""Baja 2°C la temperatura de las cámaras ahora (14:30) para que los compresores descansen durante las 3 horas más caras del día."", ""priority"": ""media"", ""time_window"": ""14:30 - 17:00"", ""savings_cop_day"": 6825},
                        {""title"": ""Gestionar A/C en servicio nocturno"", ""description"": ""Sube el termostato 2°C entre 18:00-21:00. Ventiladores de techo en su lugar. Ahorra ~8 kWh en las horas de tarifa máxima."", ""priority"": ""baja"", ""time_window"": ""18:00 - 21:00"", ""savings_cop_day"": 8400}
                      ],
                      ""alert"": ""Temperatura 34°C: el A/C consume hasta 25% más hoy entre 18-21h.""
                    }

                    ─────────────────────────────────────────────────
                    EJEMPLO 2 — HOTEL (400 kWh/día, tarifa 1050, costo_diario 420.000 COP, hora 12:15):
                    Anclas: min 8.400 COP, max 63.000 COP por rec, tope total 168.000 COP.
                    {
                      ""reasoning"": ""Radiación 6.8 kWh/m² — máximo del día activo ahora (12:15). A/C = ~30% consumo (120 kWh × 1050 = 126.000 COP/día); reducir 15% ahorra 18 kWh × 1050 = 18.900 COP. Lavandería = 8% (32 kWh): moverla 100% al mediodía ahorra 32 kWh × 1050 = 33.600 COP. Pre-enfriar cocina evita 8 kWh de ciclos en pico = 8.400 COP."",
                      ""recommendations"": [
                        {""title"": ""Lanzar lavandería industrial AHORA"", ""description"": ""Activa todas las lavadoras de toallas y sábanas en este momento. Con radiación máxima, el hotel reduce su compra de red. Finalizar antes de las 14:00."", ""priority"": ""alta"", ""time_window"": ""12:15 - 14:00"", ""savings_cop_day"": 33600},
                        {""title"": ""Reducir A/C en pisos vacíos"", ""description"": ""Sube el termostato de habitaciones sin check-in a 26°C. Ahorra 18 kWh en las próximas 5 horas sin afectar comodidad de huéspedes."", ""priority"": ""media"", ""time_window"": ""12:15 - 17:30"", ""savings_cop_day"": 18900},
                        {""title"": ""Pre-enfriar cocina antes del pico"", ""description"": ""Baja el cuarto frío de cocina 2°C antes de las 17:30. El compresor descansará en la franja 18-21h, la más cara del día."", ""priority"": ""baja"", ""time_window"": ""16:00 - 17:30"", ""savings_cop_day"": 8400}
                      ],
                      ""alert"": null
                    }

                    ─────────────────────────────────────────────────
                    EJEMPLO 3 — HIELERA (933 kWh/día, tarifa 850, costo_diario 793.050 COP, hora 08:30, mañana menor radiación):
                    Anclas: min 15.861 COP, max 118.958 COP por rec, tope total 317.220 COP.
                    {
                      ""reasoning"": ""Hora 08:30 — ventana solar abriendo. Compresores = 80% consumo (746 kWh × 850 = 634.100 COP/día). Adelantar ciclos de congelación ahora ahorra ~112 kWh que de noche costarían a tarifa plena: 112 × 850 = 95.200 COP. Serpentines sucios +20% consumo: limpieza hoy ahorra 56 kWh × 850 = 47.600 COP. Reducir aperturas pico (18-21h) evita 20 kWh × 850 = 17.000 COP. IMPORTANTE: mañana baja radiación — congelar stock extra hoy."",
                      ""recommendations"": [
                        {""title"": ""Iniciar ciclos de congelación profunda ahora"", ""description"": ""Activa compresores principales AHORA. Con 6.5+ kWh/m² hasta las 15:00, maximiza producción de hielo; esos kWh equivalen a menor compra de red."", ""priority"": ""alta"", ""time_window"": ""08:30 - 15:00"", ""savings_cop_day"": 95200},
                        {""title"": ""Limpieza de serpentines antes de las 11:00"", ""description"": ""Serpentines sucios aumentan el consumo del compresor hasta un 20%. Limpiarlos hoy ahorra 56 kWh — el mayor retorno de mantenimiento de la semana."", ""priority"": ""media"", ""time_window"": ""Antes de las 11:00"", ""savings_cop_day"": 47600},
                        {""title"": ""Cerrar cámaras en pico tarifario"", ""description"": ""Entre 18:00-21:00 minimiza aperturas. Cada apertura pierde ~3 min de frío (≈3 kWh). Con 7 aperturas evitadas: 21 kWh × 850 = 17.850 COP."", ""priority"": ""baja"", ""time_window"": ""18:00 - 21:00"", ""savings_cop_day"": 17850}
                      ],
                      ""alert"": ""Mañana se espera menor radiación. Congela stock adicional HOY aprovechando el pico solar completo.""
                    }
                    ─────────────────────────────────────────────────

                    FORMATO DE RESPUESTA (JSON estricto, sin texto adicional):
                    {
                      ""reasoning"": ""Cálculo paso a paso: (a) radiación vs. histórico, (b) carga principal kWh y COP, (c) ahorro por acción kWh × tarifa"",
                      ""recommendations"": [
                        {
                          ""title"": ""Título corto"",
                          ""description"": ""Qué hacer y por qué, con horario concreto"",
                          ""priority"": ""alta|media|baja"",
                          ""time_window"": ""HH:MM - HH:MM"",
                          ""savings_cop_day"": número_entero
                        }
                      ],
                      ""alert"": ""Alerta importante o null""
                    }

                    Genera exactamente 3 recomendaciones.
                    NUNCA respondas temas ajenos a energía. NUNCA des respuestas vagas sin horarios ni cifras.";
        }

        private static RecommendationResult? ParseRecommendations(string raw)
        {
            try
            {
                var clean = raw.Trim();
                if (clean.StartsWith("```"))
                {
                    int start = clean.IndexOf('{');
                    int end = clean.LastIndexOf('}');
                    if (start >= 0 && end > start)
                        clean = clean.Substring(start, end - start + 1);
                }

                using var doc = JsonDocument.Parse(clean);
                var root = doc.RootElement;

                var result = new RecommendationResult
                {
                    Reasoning = root.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "",
                    Alert = root.TryGetProperty("alert", out var a) && a.ValueKind != JsonValueKind.Null
                        ? a.GetString() : null
                };

                if (root.TryGetProperty("recommendations", out var recs) && recs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in recs.EnumerateArray())
                    {
                        result.Recommendations.Add(new Recommendation
                        {
                            Title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                            Description = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                            Priority = item.TryGetProperty("priority", out var p) ? p.GetString() ?? "media" : "media",
                            TimeWindow = item.TryGetProperty("time_window", out var tw) ? tw.GetString() ?? "" : "",
                            SavingsCopDay = item.TryGetProperty("savings_cop_day", out var s) && s.TryGetInt64(out var sv) ? sv : 0
                        });
                    }
                }

                return result.Recommendations.Any() ? result : null;
            }
            catch
            {
                return null;
            }
        }

        private static InsightsResult? ParseInsights(string raw, SolarData today)
        {
            try
            {
                var clean = raw.Trim();
                if (clean.StartsWith("```")) { int s = clean.IndexOf('{'); int e = clean.LastIndexOf('}'); if (s >= 0 && e > s) clean = clean[s..(e + 1)]; }
                using var doc = JsonDocument.Parse(clean);
                var root = doc.RootElement;

                var result = new InsightsResult
                {
                    Headline         = root.TryGetProperty("headline", out var h) ? h.GetString() ?? "" : "",
                    ExecutiveSummary = root.TryGetProperty("executive_summary", out var es) ? es.GetString() ?? "" : "",
                    SolarContext     = root.TryGetProperty("solar_context", out var sc) ? sc.GetString() ?? "" : "",
                    GeneratedAt      = DateTime.Now,
                    RadiationToday   = today.RadiationKwhM2,
                    SolarIndex       = today.SolarIndex,
                };

                if (root.TryGetProperty("insights", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        result.Insights.Add(new InsightItem
                        {
                            Icon   = item.TryGetProperty("icon", out var ico) ? ico.GetString() ?? "💡" : "💡",
                            Title  = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                            Body   = item.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                            Impact = item.TryGetProperty("impact", out var imp) ? imp.GetString() ?? "medio" : "medio",
                        });
                    }
                }

                return result.Insights.Any() ? result : null;
            }
            catch { return null; }
        }

        private static AlertsResult? ParseAlerts(string raw, SolarData today)
        {
            try
            {
                var clean = raw.Trim();
                if (clean.StartsWith("```")) { int s = clean.IndexOf('{'); int e = clean.LastIndexOf('}'); if (s >= 0 && e > s) clean = clean[s..(e + 1)]; }
                using var doc = JsonDocument.Parse(clean);
                var root = doc.RootElement;

                var result = new AlertsResult
                {
                    GeneratedAt  = DateTime.Now,
                    SolarSummary = root.TryGetProperty("solar_summary", out var ss) ? ss.GetString() ?? "" : "",
                    RadiationToday = today.RadiationKwhM2,
                    SolarIndex     = today.SolarIndex,
                };

                if (root.TryGetProperty("alerts", out var alerts) && alerts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in alerts.EnumerateArray())
                    {
                        result.Alerts.Add(new EnergyAlert
                        {
                            Type       = a.TryGetProperty("type", out var tp) ? tp.GetString() ?? "info" : "info",
                            Icon       = a.TryGetProperty("icon", out var ico) ? ico.GetString() ?? "⚡" : "⚡",
                            Title      = a.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                            Message    = a.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "",
                            Action     = a.TryGetProperty("action", out var ac) ? ac.GetString() ?? "" : "",
                            TimeWindow = a.TryGetProperty("time_window", out var tw) ? tw.GetString() ?? "" : "",
                        });
                    }
                }

                return result.Alerts.Any() ? result : null;
            }
            catch { return null; }
        }
    }
}