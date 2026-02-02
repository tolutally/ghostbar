using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GhostBar
{
    public static class OpenAIClient
    {
        // Read API key from environment variable - never hardcode secrets!
        // API Key is now managed by ConfigManager
        // private static readonly string _apiKey = ...


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
            var messages = new[] { new ChatMessage("user", prompt) };
            return await ChatAsync(messages);
        }

        public static async Task<string> ChatAsync(IEnumerable<ChatMessage> messages)
        {
            var apiKey = ConfigManager.OpenAIKey;
            
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Logger.Error("API key not set");
                return "⚠️ OpenAI API key missing. Click the settings (⚙️) button to configure it.";
            }

            // Convert ChatMessage objects to anonymous objects for JSON serialization
            // (or ensure ChatMessage properties are lowercased if serialized directly)
            var apiMessages = new System.Collections.Generic.List<object>();
            foreach (var msg in messages)
            {
                apiMessages.Add(new { role = msg.Role, content = msg.Content });
            }

            var requestBody = new
            {
                model = "gpt-4o-mini", // or another model you have access to
                messages = apiMessages
            };

            var json = JsonSerializer.Serialize(requestBody);
            var endpoint = "https://api.openai.com/v1/chat/completions";
            
            Logger.APIRequest(endpoint, json);
            
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
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
