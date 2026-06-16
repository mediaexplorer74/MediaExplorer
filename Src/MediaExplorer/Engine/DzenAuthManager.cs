using System;
using System.Threading.Tasks;
using Windows.Security.Credentials;
using Windows.Storage;

namespace BrowserCore.Engine
{
    public class DzenAuthManager
    {
        private const string ClientId = "MEDIAEXPLORER_DZEN_CLIENT_ID";
        private const string ResourceName = "MediaExplorer_DzenAuth";
        private const string TokenKey = "DzenAccessToken";
        private const string RefreshKey = "DzenRefreshToken";
        private const string UserKey = "DzenUsername";
        private const string ExpiryKey = "DzenTokenExpiry";

        public static bool IsLoggedIn
        {
            get
            {
                try
                {
                    var s = ApplicationData.Current.LocalSettings;
                    return s.Values.ContainsKey(TokenKey) && !string.IsNullOrEmpty(s.Values[TokenKey] as string);
                }
                catch { return false; }
            }
        }

        public static string Username
        {
            get
            {
                try
                {
                    var s = ApplicationData.Current.LocalSettings;
                    if (s.Values.TryGetValue(UserKey, out var v) && v is string name) return name;
                }
                catch { }
                return null;
            }
        }

        public static bool IsTokenExpired
        {
            get
            {
                try
                {
                    var s = ApplicationData.Current.LocalSettings;
                    if (s.Values.TryGetValue(ExpiryKey, out var v) && v is long ticks)
                    {
                        return DateTime.UtcNow > new DateTime(ticks, DateTimeKind.Utc);
                    }
                }
                catch { }
                return true;
            }
        }

        public static async Task<bool> TryRefreshTokenAsync()
        {
            try
            {
                var s = ApplicationData.Current.LocalSettings;
                if (!s.Values.TryGetValue(RefreshKey, out var refreshVar) || !(refreshVar is string refreshToken) || string.IsNullOrEmpty(refreshToken))
                    return false;

                var http = new System.Net.Http.HttpClient();
                var content = new System.Net.Http.FormUrlEncodedContent(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "refresh_token"),
                    new System.Collections.Generic.KeyValuePair<string, string>("client_id", ClientId),
                    new System.Collections.Generic.KeyValuePair<string, string>("refresh_token", refreshToken)
                });

                var resp = await http.PostAsync("https://oauth.yandex.ru/token", content);
                if (!resp.IsSuccessStatusCode) return false;

                var body = await resp.Content.ReadAsStringAsync();
                var json = Windows.Data.Json.JsonValue.Parse(body)?.GetObject();
                if (json == null) return false;

                string accessToken = GetJsonStr(json, "access_token");
                string newRefresh = GetJsonStr(json, "refresh_token");
                int expiresIn = (int)json.GetNamedNumber("expires_in", 3600);

                if (!string.IsNullOrEmpty(accessToken))
                {
                    SaveTokens(accessToken, newRefresh ?? refreshToken, expiresIn);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static void SaveTokens(string accessToken, string refreshToken, int expiresInSec)
        {
            try
            {
                var s = ApplicationData.Current.LocalSettings;
                s.Values[TokenKey] = accessToken ?? "";
                s.Values[RefreshKey] = refreshToken ?? "";
                s.Values[ExpiryKey] = DateTime.UtcNow.AddSeconds(expiresInSec).Ticks;
            }
            catch { }
        }

        public static void SaveUsername(string username)
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[UserKey] = username ?? "";
            }
            catch { }
        }

        public static string GetAccessToken()
        {
            try
            {
                var s = ApplicationData.Current.LocalSettings;
                if (s.Values.TryGetValue(TokenKey, out var v) && v is string token) return token;
            }
            catch { }
            return null;
        }

        public static void Logout()
        {
            try
            {
                var s = ApplicationData.Current.LocalSettings;
                s.Values.Remove(TokenKey);
                s.Values.Remove(RefreshKey);
                s.Values.Remove(UserKey);
                s.Values.Remove(ExpiryKey);
            }
            catch { }
        }

        public static string GetOAuthUrl()
        {
            return $"https://oauth.yandex.ru/authorize?response_type=code&client_id={ClientId}&login_hint=any&force_confirm=yes";
        }

        public static async Task<bool> ExchangeCodeAsync(string authCode)
        {
            try
            {
                var http = new System.Net.Http.HttpClient();
                var content = new System.Net.Http.FormUrlEncodedContent(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "authorization_code"),
                    new System.Collections.Generic.KeyValuePair<string, string>("client_id", ClientId),
                    new System.Collections.Generic.KeyValuePair<string, string>("code", authCode)
                });

                var resp = await http.PostAsync("https://oauth.yandex.ru/token", content);
                if (!resp.IsSuccessStatusCode) return false;

                var body = await resp.Content.ReadAsStringAsync();
                var json = Windows.Data.Json.JsonValue.Parse(body)?.GetObject();
                if (json == null) return false;

                string accessToken = GetJsonStr(json, "access_token");
                string refreshToken = GetJsonStr(json, "refresh_token");
                int expiresIn = (int)json.GetNamedNumber("expires_in", 3600);

                if (!string.IsNullOrEmpty(accessToken))
                {
                    SaveTokens(accessToken, refreshToken, expiresIn);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static string GetJsonStr(Windows.Data.Json.JsonObject obj, string key)
        {
            if (!obj.ContainsKey(key)) return null;
            var v = obj.GetNamedValue(key);
            if (v == null || v.ValueType == Windows.Data.Json.JsonValueType.Null) return null;
            return v.GetString();
        }
    }
}
