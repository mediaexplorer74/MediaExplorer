using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;

namespace BrowserCore.Engine
{
    public static class OpenRouterClient
    {
        private static readonly HttpClient _http = new HttpClient();

        public static async Task<string> SummarizeAsync(string apiKey, string pageText)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return "Error: No API key. Add your OpenRouter key in Settings.";

            if (string.IsNullOrWhiteSpace(pageText))
                return "Error: No page content to summarize.";

            var escaped = JsonValue.CreateStringValue(pageText).Stringify();
            var json = $@"{{""model"":""deepseek/deepseek-chat"",""messages"":[{{""role"":""system"",""content"":""Summarize the following web page content in 3-5 concise bullet points. Focus on key facts and main topic.""}},{{""role"":""user"",""content"":{escaped}}}],""max_tokens"":500,""temperature"":0.3}}";

            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
                req.Headers.Add("Authorization", "Bearer " + apiKey);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                    return $"Error: API returned {(int)resp.StatusCode} {resp.ReasonPhrase}";

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
