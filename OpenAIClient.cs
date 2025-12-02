using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GhostBar
{
    public static class OpenAIClient
    {
        // Read API key from environment variable - never hardcode secrets!
        private static readonly string _apiKey =
            Environment.GetEnvironmentVariable("GHOSTBAR_OPENAI_API_KEY") ?? "";

        private static readonly HttpClient _httpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            Logger.Info("Creating HttpClient with custom handler...");
            
            // WARNING: This disables SSL certificate validation.
            // Only use for testing on a trusted network and machine.
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                {
                    Logger.Info($"SSL Callback: Cert={cert?.Subject ?? "null"}, Errors={errors}");
                    return true;
                }
            };

            Logger.Info("HttpClient created with SSL bypass");
            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        public static async Task<string> AskAsync(string prompt)
        {
            Logger.Action($"AskAsync called with prompt: {prompt.Substring(0, Math.Min(50, prompt.Length))}...");
            
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                Logger.Error("API key not set");
                return "⚠️ Set GHOSTBAR_OPENAI_API_KEY environment variable with your OpenAI API key.";
            }

            // Chat-style request body
            var requestBody = new
            {
                model = "gpt-4o-mini", // or another model you have access to
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var endpoint = "https://api.openai.com/v1/chat/completions";
            
            Logger.APIRequest(endpoint, json);
            
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                Logger.Info("Sending HTTP request...");
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                
                var responseJson = await response.Content.ReadAsStringAsync();
                Logger.APIResponse(endpoint, (int)response.StatusCode, responseJson);
                
                response.EnsureSuccessStatusCode();

                using var doc = JsonDocument.Parse(responseJson);

                var root = doc.RootElement;
                var content = root
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                Logger.Info("API call successful");
                return content?.Trim() ?? "(empty response)";
            }
            catch (Exception ex)
            {
                Logger.Error("API call failed", ex);
                return $"❌ Error calling OpenAI: {ex.Message}";
            }
        }
    }
}
