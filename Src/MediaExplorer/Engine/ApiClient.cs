using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;

namespace BrowserCore.Engine
{
    public static class ApiClient
    {
        private static readonly HttpClient _http = new HttpClient();

        public static async Task<string> SummarizeAsync(string apiKey, string modelFamily, string modelId, string pageText, string prompt)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return "Error: No API key configured.";

            if (string.IsNullOrWhiteSpace(pageText))
                return "Error: No page content to analyze.";

            // If modelId already contains "/" it's a full ID (e.g. "google/gemma-4-26b-a4b-it:free")
            string model = modelId.Contains("/") ? modelId : modelFamily + "/" + modelId;

            string keyPreview = apiKey.Length > 8 ? apiKey.Substring(0, 4) + "..." + apiKey.Substring(apiKey.Length - 4) : "(short)";
            DevToolsLogger.Log("[DIAG:API] model=" + model + " key=" + keyPreview + " textLen=" + pageText.Length);

            var escapedText = JsonValue.CreateStringValue(pageText).Stringify();
            var escapedPrompt = JsonValue.CreateStringValue(prompt).Stringify();
            var json = $@"{{""model"":""{model}"",""messages"":[{{""role"":""system"",""content"":{escapedPrompt}}},{{""role"":""user"",""content"":{escapedText}}}],""max_tokens"":800,""temperature"":0.3}}";

            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
                req.Headers.Add("Authorization", "Bearer " + apiKey);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var resp = await _http.SendAsync(req);
                DevToolsLogger.Log("[DIAG:API] status=" + (int)resp.StatusCode + " " + resp.ReasonPhrase);

                // Auto-retry on 429 (rate limit)
                if ((int)resp.StatusCode == 429)
                {
                    int retrySec = 23;
                    try
                    {
                        var errJson = await resp.Content.ReadAsStringAsync();
                        var errObj = Windows.Data.Json.JsonValue.Parse(errJson)?.GetObject();
                        if (errObj != null && errObj.ContainsKey("error"))
                        {
                            var errData = errObj["error"].GetObject();
                            if (errData.ContainsKey("metadata"))
                            {
                                var meta = errData["metadata"].GetObject();
                                if (meta.ContainsKey("retry_after_seconds"))
                                    retrySec = (int)meta.GetNamedNumber("retry_after_seconds", 23);
                            }
                        }
                    }
                    catch { }

                    DevToolsLogger.Log("[DIAG:API] Rate limited, retrying in " + retrySec + "s...");
                    await Task.Delay(retrySec * 1000);

                    // Retry once
                    var retryReq = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
                    retryReq.Headers.Add("Authorization", "Bearer " + apiKey);
                    retryReq.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    resp = await _http.SendAsync(retryReq);
                    DevToolsLogger.Log("[DIAG:API] retry status=" + (int)resp.StatusCode);

                    if (!resp.IsSuccessStatusCode)
                    {
                        var errBody = await resp.Content.ReadAsStringAsync();
                        return $"Error: API returned {(int)resp.StatusCode} after retry\n{errBody}";
                    }
                }
                else if (!resp.IsSuccessStatusCode)
                {
                    var errBody = await resp.Content.ReadAsStringAsync();
                    DevToolsLogger.Log("[DIAG:API] errorBody=" + (errBody?.Substring(0, Math.Min(200, errBody?.Length ?? 0)) ?? ""));
                    return $"Error: API returned {(int)resp.StatusCode} {resp.ReasonPhrase}\n{errBody}";
                }

                var body = await resp.Content.ReadAsStringAsync();
                var obj = JsonValue.Parse(body).GetObject();
                var choices = obj["choices"].GetArray();
                if (choices.Count == 0)
                    return "Error: Empty response from API.";

                var msg = choices[0].GetObject()["message"].GetObject();
                var content = msg["content"].GetString();
                return content;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }
}
