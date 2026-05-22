using System.Net;
using System.Text;
using System.Text.Json;

namespace Olympus.Services
{
    public class AiService
    {
        private readonly IConfiguration _config;
        private readonly HttpClient _httpClient;

        public AiService(IConfiguration config, HttpClient httpClient)
        {
            _config = config;
            _httpClient = httpClient;
        }

        private string[] Keys => _config.GetSection("Ai:KEYS").Get<string[]>() ?? [];
        private string Model => _config["Ai:MODEL"] ?? "";
        private string Url   => _config["Ai:URL"]   ?? "";

        // Pide respuesta en JSON estricto — para recomendaciones, insights, alertas
        public async Task<string> AskJsonAsync(string systemPrompt, string userPrompt)
            => await SendAsync(systemPrompt, userPrompt, jsonMode: true);

        // Pide respuesta en texto libre — para el chat
        public async Task<string> AskTextAsync(string systemPrompt, string userPrompt)
            => await SendAsync(systemPrompt, userPrompt, jsonMode: false);

        private async Task<string> SendAsync(string systemPrompt, string userPrompt, bool jsonMode)
        {
            var keys = Keys;

            if (keys.Length == 0 || string.IsNullOrEmpty(Model) || string.IsNullOrEmpty(Url))
                throw new InvalidOperationException("Configuración de IA incompleta (KEYS, MODEL o URL).");

            var messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   }
            };

            object body = jsonMode
                ? (object)new { model = Model, messages, temperature = 0.25, response_format = new { type = "json_object" } }
                : (object)new { model = Model, messages, temperature = 0.7 };

            var json = JsonSerializer.Serialize(body);

            HttpRequestException? lastError = null;

            foreach (var key in keys)
            {
                try
                {
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var request = new HttpRequestMessage(HttpMethod.Post, Url);
                    request.Headers.Add("Authorization", $"Bearer {key}");
                    request.Content = content;

                    var response = await _httpClient.SendAsync(request);
                    var result   = await response.Content.ReadAsStringAsync();

                    // Cuota agotada o rate limit — rotar a la siguiente key
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        lastError = new HttpRequestException($"Key agotada (429) — rotando: {key[..16]}...");
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException($"Groq devolvió {(int)response.StatusCode}: {result}");

                    using var doc = JsonDocument.Parse(result);
                    return doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString() ?? "";
                }
                catch (HttpRequestException ex) when (
                    ex.Message.Contains("429") ||
                    ex.Message.Contains("quota") ||
                    ex.Message.Contains("rate"))
                {
                    lastError = ex;
                    // Continúa con la siguiente key
                }
            }

            throw lastError
                ?? new HttpRequestException("Todas las API keys agotaron su cuota.");
        }
    }
}
