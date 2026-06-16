using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BrowserCore.Engine
{
    public class DzenPost
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Snippet { get; set; }
        public string ImageUrl { get; set; }
        public string Author { get; set; }
        public string ChannelName { get; set; }
        public string Url { get; set; }
        public long ViewCount { get; set; }
        public long LikeCount { get; set; }
        public long CommentCount { get; set; }
        public string PublishedAt { get; set; }
    }

    public static class DzenApi
    {
        private static readonly System.Net.Http.HttpClient _http = new System.Net.Http.HttpClient();

        public static async Task<List<DzenPost>> FetchFeedAsync(string category = "popular")
        {
            var posts = new List<DzenPost>();
            try
            {
                string token = DzenAuthManager.GetAccessToken();
                if (string.IsNullOrEmpty(token))
                {
                    DevToolsLogger.Log("[DIAG:DZEN] No access token — not logged in");
                    return posts;
                }

                string url = $"https://dzen.ru/api/v3/feed?period=day&category={category}";
                _http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("OAuth", token);
                _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                var resp = await _http.GetAsync(url);
                DevToolsLogger.Log("[DIAG:DZEN] Feed status=" + (int)resp.StatusCode);

                if (!resp.IsSuccessStatusCode) return posts;

                var body = await resp.Content.ReadAsStringAsync();
                var json = Windows.Data.Json.JsonValue.Parse(body)?.GetObject();
                if (json == null || !json.ContainsKey("items")) return posts;

                var items = json.GetNamedArray("items");
                foreach (var item in items)
                {
                    var obj = item.GetObject();
                    var post = new DzenPost
                    {
                        Id = GetStr(obj, "id"),
                        Title = GetStr(obj, "title"),
                        Snippet = GetStr(obj, "snippet"),
                        Author = GetStr(obj, "author"),
                        ChannelName = GetStr(obj, "channel_name"),
                        Url = "https://dzen.ru/a/" + GetStr(obj, "slug"),
                        ViewCount = (long)GetNum(obj, "views_count"),
                        LikeCount = (long)GetNum(obj, "like_count"),
                        CommentCount = (long)GetNum(obj, "comments_count"),
                        PublishedAt = GetStr(obj, "publish_date")
                    };

                    if (obj.ContainsKey("image"))
                    {
                        var img = obj.GetNamedObject("image");
                        post.ImageUrl = GetStr(img, "src");
                    }

                    posts.Add(post);
                }

                DevToolsLogger.Log("[DIAG:DZEN] Loaded " + posts.Count + " posts");
            }
            catch (Exception ex)
            {
                DevToolsLogger.Log("[DIAG:DZEN] Error: " + ex.Message);
            }
            return posts;
        }

        private static string GetStr(Windows.Data.Json.JsonObject obj, string key)
        {
            if (!obj.ContainsKey(key)) return "";
            var v = obj.GetNamedValue(key);
            if (v == null || v.ValueType == Windows.Data.Json.JsonValueType.Null) return "";
            return v.GetString();
        }

        private static double GetNum(Windows.Data.Json.JsonObject obj, string key)
        {
            if (!obj.ContainsKey(key)) return 0;
            var v = obj.GetNamedValue(key);
            if (v == null || v.ValueType == Windows.Data.Json.JsonValueType.Null) return 0;
            return v.GetNumber();
        }
    }
}
