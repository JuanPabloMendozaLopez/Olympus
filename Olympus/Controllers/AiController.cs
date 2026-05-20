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
        private readonly GroqService _groq;
        private readonly SolarService _solar;

        public AiController(GroqService groq, SolarService solar)
        {
            _groq = groq;
            _solar = solar;
        }

        // ============================================================
        // POST /api/ai/recommendations
        // Genera 3 recomendaciones diarias con contexto solar real
        // ============================================================
        [HttpPost("recommendations")]
        public async Task<IActionResult> GetRecommendations([FromBody] RecommendationRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.CompanyType))
                    return BadRequest(new { success = false, error = "companyType es obligatorio" });

                // 1. Obtener datos solares reales
                var today = await _solar.GetTodayAsync();
                if (today == null)
                    return StatusCode(503, new { success = false, error = "No se pudieron obtener datos solares" });

                var historicalAvg = await _solar.GetHistoricalAverageAsync(90);

                // 2. Armar el contexto para el LLM
                var comparacion = historicalAvg > 0
                    ? $"El promedio histórico de los últimos 90 días es {historicalAvg} kWh/m². " +
                      $"Hoy está {(today.RadiationKwhM2 > historicalAvg ? "POR ENCIMA" : "POR DEBAJO")} del promedio."
                    : "";

                var systemPrompt = BuildSystemPrompt();

                var perfilConsumo = BuildConsumoContext(req.MonthlyConsumptionKwh, req.CompanySize);

                var userPrompt = $@"
                                Genera recomendaciones de ahorro energético para esta empresa:
                                - Nombre: {(string.IsNullOrWhiteSpace(req.CompanyName) ? "empresa local" : req.CompanyName)}
                                - Tipo: {req.CompanyType}

                                {perfilConsumo}

                                DATOS SOLARES DE HOY ({today.Date:yyyy-MM-dd}):
                                - Radiación: {today.RadiationKwhM2} kWh/m²/día
                                - Índice Solar: {today.SolarIndex}/100 ({today.SolarIndexLabel})
                                - Temperatura máxima: {today.TemperatureC}°C
                                - Viento máximo: {today.WindSpeedKmh} km/h
                                - Horas óptimas: {string.Join(", ", today.OptimalHours)}
                                - Horas pico tarifario: {string.Join(", ", today.PeakCostHours)}
                                {comparacion}

                                Responde SOLO con el JSON en el formato indicado.";

                // 3. Llamar a Groq  -- AJUSTA esta línea según la firma de tu GroqService
                var raw = await _groq.AskJsonAsync(systemPrompt, userPrompt);

                // 4. Parsear la respuesta del LLM
                var parsed = ParseRecommendations(raw);
                if (parsed == null)
                    return StatusCode(502, new { success = false, error = "La IA devolvió un formato inválido" });

                // 5. Completar con datos que ya conocemos
                parsed.CompanyName = req.CompanyName;
                parsed.CompanyType = req.CompanyType;
                parsed.Date = today.Date;
                parsed.RadiationToday = today.RadiationKwhM2;
                parsed.SolarIndex = today.SolarIndex;
                parsed.TotalSavingsCopDay = parsed.Recommendations.Sum(r => r.SavingsCopDay);
                parsed.TotalSavingsCopMonth = parsed.TotalSavingsCopDay * 30;

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
        // Chat libre con el Agente Solar
        // ============================================================
        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] ChatRequest req)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.Message))
                    return BadRequest(new { success = false, error = "El mensaje no puede estar vacío" });

                var today = await _solar.GetTodayAsync();

                var contexto = today != null
                    ? $"Radiación hoy: {today.RadiationKwhM2} kWh/m², Índice Solar: {today.SolarIndex}/100, " +
                      $"Temperatura: {today.TemperatureC}°C."
                    : "Datos solares no disponibles en este momento.";

                var systemPrompt = BuildSystemPrompt();

                var perfilConsumo = BuildConsumoContext(req.MonthlyConsumptionKwh, req.CompanySize);

                var userPrompt = $@"
                                    CONTEXTO ACTUAL: {contexto}
                                    Tipo de empresa del usuario: {req.CompanyType}

                                    {perfilConsumo}

                                    Pregunta del usuario: {req.Message}

                                    Responde en lenguaje natural, máximo 3 oraciones, con horarios y cifras en pesos colombianos cuando sea relevante.";

                var raw = await _groq.AskTextAsync(systemPrompt, userPrompt);

                return Ok(new
                {
                    success = true,
                    data = new ChatResult
                    {
                        Reply = raw.Trim(),
                        Timestamp = DateTime.Now
                    }
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
        // GET /api/ai/recommendations/demo
        // FALLBACK hardcoded — nunca falla. Seguro para la demo.
        // ============================================================
        [HttpGet("recommendations/demo")]
        public IActionResult GetDemoRecommendations()
        {
            var demo = new RecommendationResult
            {
                CompanyName = "Hotel Majayura",
                CompanyType = "hotel",
                Date = DateTime.Today,
                RadiationToday = 6.8,
                SolarIndex = 89,
                Reasoning = "Detecté radiación alta (6.8 kWh/m²) y horas de costo crítico entre 6pm y 9pm. Prioricé mover cargas pesadas al pico solar del mediodía.",
                Recommendations = new List<Recommendation>
                {
                    new() {
                        Title = "Mover lavandería al pico solar",
                        Description = "Programa el lavado de toallas y sábanas entre 10:00 y 14:00, cuando la radiación es máxima.",
                        Priority = "alta",
                        TimeWindow = "10:00 - 14:00",
                        SavingsCopDay = 48500
                    },
                    new() {
                        Title = "Apagar A/C en habitaciones vacías",
                        Description = "Con ocupación al 60%, apaga el aire de las habitaciones sin huéspedes durante el día.",
                        Priority = "media",
                        TimeWindow = "Inmediato",
                        SavingsCopDay = 22000
                    },
                    new() {
                        Title = "Pre-enfriar la cocina antes de las 6pm",
                        Description = "Aprovecha la última hora de sol para bajar temperatura antes del pico tarifario.",
                        Priority = "baja",
                        TimeWindow = "16:00 - 17:30",
                        SavingsCopDay = 12000
                    }
                },
                Alert = "Mañana se espera menor radiación. Concentra hoy el consumo pesado.",
                TotalSavingsCopDay = 82500,
                TotalSavingsCopMonth = 2475000
            };

            return Ok(new { success = true, data = demo });
        }

        // ============================================================
        // Helpers privados
        // ============================================================
        private static string BuildSystemPrompt()
        {
            return @"Eres el Agente Solar, un copiloto energético inteligente especializado en
                    optimización del consumo eléctrico para empresas en Riohacha, La Guajira, Colombia.

                    CONTEXTO DEL TERRITORIO:
                    - Riohacha tiene hasta 7.0 kWh/m²/día de radiación, el mayor potencial de Colombia.
                    - La tarifa eléctrica local es de 943 COP/kWh (una de las más altas del país).
                    - Las empresas tienen apagones promedio de 60 horas/año.
                    - La energía representa hasta el 33% del OpEx de las PYMES locales.

                    REGLAS DE RAZONAMIENTO:
                    1. Si radiation > 6.5 → recomendar mover cargas pesadas al mediodía.
                    2. Si radiation < 5.0 → advertir sobre menor generación, priorizar ahorro nocturno.
                    3. Si temperatura > 34°C → el A/C consume hasta 25% más; gestionar horarios.
                    4. Siempre calcular ahorro en COP usando tarifa de 943 COP/kWh.
                    5. Las recomendaciones deben ser CONCRETAS, con horario y ahorro en pesos.

                    FORMATO DE RESPUESTA PARA RECOMENDACIONES:
                    Responde SIEMPRE en JSON válido, sin texto adicional antes ni después:
                    {
                      ""reasoning"": ""Análisis breve en 1-2 oraciones"",
                      ""recommendations"": [
                        {
                          ""title"": ""Título corto"",
                          ""description"": ""Qué hacer y por qué"",
                          ""priority"": ""alta|media|baja"",
                          ""time_window"": ""HH:MM - HH:MM"",
                          ""savings_cop_day"": número_entero
                        }
                      ],
                      ""alert"": ""Alerta importante o null""
                    }

                    Genera exactamente 3 recomendaciones.

                    NUNCA digas que eres una IA genérica. NUNCA des respuestas vagas sin horarios ni cifras.";
        }

        private static string BuildConsumoContext(double monthlyKwh, int? size)
        {
            if (monthlyKwh <= 0)
                return "PERFIL DE CONSUMO: no proporcionado. Estima según un negocio típico del tipo indicado e indícalo en el reasoning.";

            return $@"PERFIL DE CONSUMO DE LA EMPRESA:
                    - Consumo mensual declarado: {monthlyKwh} kWh
                    - Consumo diario promedio: {Math.Round(monthlyKwh / 30, 1)} kWh
                    - Costo mensual actual estimado: {Math.Round(monthlyKwh * 943):N0} COP
                    - Tamaño: {size?.ToString() ?? "no especificado"}";
        }

        private static RecommendationResult? ParseRecommendations(string raw)
        {
            try
            {
                // El LLM a veces envuelve el JSON en ```json ... ``` — lo limpiamos
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
                            SavingsCopDay = item.TryGetProperty("savings_cop_day", out var s) && s.TryGetInt32(out var sv) ? sv : 0
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
    }
}