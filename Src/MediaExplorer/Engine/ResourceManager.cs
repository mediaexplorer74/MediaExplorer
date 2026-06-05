using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Web.Http;

namespace BrowserCore.Engine
{
    public sealed class ResourceManager
    {
        // Optional diagnostic sink; host can assign to surface timings in UI.
        public static System.Action<string> LogSink;
        private readonly HttpClient _http;

        private sealed class TextEntry { public string Body; public string ContentType; }
        private readonly Dictionary<string, LinkedListNode<Tuple<string, TextEntry>>> _textMap = new Dictionary<string, LinkedListNode<Tuple<string, TextEntry>>>(StringComparer.Ordinal);
        private readonly LinkedList<Tuple<string, TextEntry>> _textLru = new LinkedList<Tuple<string, TextEntry>>();
        private readonly int _textCap = 32;
        private readonly object _textLock = new object();

        private sealed class ImgEntry { public IBuffer Buffer; public string ContentType; }
        private readonly Dictionary<string, LinkedListNode<Tuple<string, ImgEntry>>> _imgMap = new Dictionary<string, LinkedListNode<Tuple<string, ImgEntry>>>(StringComparer.Ordinal);
        private readonly LinkedList<Tuple<string, ImgEntry>> _imgLru = new LinkedList<Tuple<string, ImgEntry>>();
        private readonly int _imgCap = 16;
        private readonly object _imgLock = new object();

        public System.TimeSpan DiskCacheTtl = System.TimeSpan.FromMinutes(5);

        private static Type _skiaTypeSvg;
        private static Type _skiaTypeImage;
        private static Type _skiaTypeData;
        private static Type _skiaTypeEncFmt;
        private static readonly Dictionary<string, MethodInfo> _skiaMethods = new Dictionary<string, MethodInfo>();
        private static readonly Dictionary<string, PropertyInfo> _skiaProperties = new Dictionary<string, PropertyInfo>();
        private static bool _skiaTypesResolved = false;
        private static readonly object _skiaLock = new object();

        private static readonly SemaphoreSlim _lowPriorityGate = new SemaphoreSlim(4, 4);

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

        public enum ResourcePriority { Critical, Normal, Low }

    public Uri LastTextResponseUri { get; private set; }

        // Network log for DevTools (static - shared across all ResourceManager instances)
        private static readonly List<string> _networkLog = new List<string>();
        private static readonly object _networkLogLock = new object();
        private const int NetworkLogMax = 200;

        public string GetNetworkLog()
        {
            lock (_networkLogLock)
            {
                if (_networkLog.Count == 0) return "(no network requests yet)";
                return string.Join("\n", _networkLog);
            }
        }

        private void LogNetwork(string message)
        {
            lock (_networkLogLock)
            {
                _networkLog.Add(message);
                if (_networkLog.Count > NetworkLogMax)
                    _networkLog.RemoveAt(0);
            }
        }

        private sealed class HstsEntry { public DateTimeOffset Expiry; public bool IncludeSub; }
        private readonly Dictionary<string, HstsEntry> _hsts = new Dictionary<string, HstsEntry>(StringComparer.OrdinalIgnoreCase);
        private const string HstsStoreKey = "hsts_store_v1";

        public ResourceManager(HttpClient http)
        {
            if (http != null)
            {
                _http = http;
            }
            else
            {
                // Configure HttpClient to NOT auto-redirect so we can handle HTTPS->HTTP manually
                var filter = new Windows.Web.Http.Filters.HttpBaseProtocolFilter();
                filter.AllowAutoRedirect = false; // We handle redirects manually
                filter.AllowUI = false; // Prevent UI prompts
                _http = new HttpClient(filter);
            }
            LoadHsts();
        }

        private void LoadHsts()
        {
            try
            {
                object raw;
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(HstsStoreKey, out raw))
                {
                    var s = raw as string; if (string.IsNullOrWhiteSpace(s)) return;
                    var lines = s.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var parts = lines[i].Split('|');
                        if (parts.Length >= 3)
                        {
                            DateTimeOffset exp; bool inc;
                            if (DateTimeOffset.TryParse(parts[1], out exp) && bool.TryParse(parts[2], out inc))
                                _hsts[parts[0]] = new HstsEntry { Expiry = exp, IncludeSub = inc };
                        }
                    }
                }
            }
            catch { /* swallow */ }
        }

        private void SaveHsts()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var kv in _hsts)
                    if (kv.Value != null)
                        sb.Append(kv.Key).Append('|').Append(kv.Value.Expiry.ToString("o")).Append('|').Append(kv.Value.IncludeSub ? "true" : "false").Append('\n');
                ApplicationData.Current.LocalSettings.Values[HstsStoreKey] = sb.ToString();
            }
            catch { /* swallow */ }
        }

        private static bool LooksTextual(string contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType)) return false;
            var ct = contentType.ToLowerInvariant();
            return ct.StartsWith("text/") || ct.Contains("javascript") || ct.Contains("json");
        }

        private static void AddHeaderSafe(HttpRequestMessage req, string name, string value)
        { try { if (!string.IsNullOrWhiteSpace(value)) req.Headers.TryAppendWithoutValidation(name, value); } catch { /* swallow */ } }

        private static string SafePartition(string origin)
        {
            return string.IsNullOrWhiteSpace(origin) ? "default" : origin.ToLowerInvariant();
        }

        private static string DetermineSecFetchSite(Uri referer, Uri request)
        {
            try
            {
                if (request == null) return "none";
                if (referer == null) return "none";
                var refHost = referer.Host ?? string.Empty;
                var reqHost = request.Host ?? string.Empty;
                if (string.Equals(refHost, reqHost, StringComparison.OrdinalIgnoreCase))
                    return "same-origin";
                if (IsSameSite(refHost, reqHost))
                    return "same-site";
                return "cross-site";
            }
            catch { return "none"; }
        }

        private static bool IsSameSite(string hostA, string hostB)
        {
            if (string.IsNullOrEmpty(hostA) || string.IsNullOrEmpty(hostB)) return false;
            if (hostA.EndsWith("." + hostB, StringComparison.OrdinalIgnoreCase)) return true;
            if (hostB.EndsWith("." + hostA, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string HashForFile(string key)
        {
            try
            {
                // Short, stable 64-bit FNV-1a hex digest to avoid PathTooLong on WP paths
                var text = key ?? string.Empty;
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                unchecked
                {
                    ulong h = 1469598103934665603UL; // FNV-1a 64 offset basis
                    for (int i = 0; i < bytes.Length; i++) { h ^= bytes[i]; h *= 1099511628211UL; }
                    return h.ToString("x16");
                }
            }
            catch { return "0"; }
        }

        private static bool IsHttps(Uri u) => u != null && string.Equals(u.Scheme, "https", StringComparison.OrdinalIgnoreCase);

        private Uri UpgradeIfHsts(Uri u)
        {
            try
            {
                if (u == null || IsHttps(u)) return u;
                var host = u.Host ?? string.Empty;
                foreach (var kv in _hsts)
                {
                    var d = kv.Value; if (d == null || d.Expiry <= DateTimeOffset.UtcNow) continue;
                    if (string.Equals(host, kv.Key, StringComparison.OrdinalIgnoreCase) || (d.IncludeSub && host.EndsWith("." + kv.Key, StringComparison.OrdinalIgnoreCase)))
                    {
                        var b = new UriBuilder(u) { Scheme = "https", Port = -1 };
                        return b.Uri;
                    }
                }
            }
            catch { /* swallow */ }
            return u;
        }

        private void NoteHsts(HttpResponseMessage resp, Uri finalUri)
        {
            try
            {
                if (!IsHttps(finalUri) || resp == null) return;
                var headers = resp.Headers.ToString();
                var m = Regex.Match(headers ?? string.Empty, @"Strict-Transport-Security\s*:\s*(?<v>[^\r\n]+)", RegexOptions.IgnoreCase);
                if (!m.Success) return;
                var v = m.Groups["v"].Value;
                var max = Regex.Match(v, @"max-age\s*=\s*(?<s>\d+)", RegexOptions.IgnoreCase);
                long sec = 0; if (!max.Success || !long.TryParse(max.Groups["s"].Value, out sec) || sec <= 0) return;
                bool include = v.IndexOf("includesubdomains", StringComparison.OrdinalIgnoreCase) >= 0;
                _hsts[finalUri.Host ?? ""] = new HstsEntry { Expiry = DateTimeOffset.UtcNow.AddSeconds(sec), IncludeSub = include };
                SaveHsts();
            }
            catch { /* swallow */ }
        }

        // Text with redirect + small disk cache (5m TTL)
        public async Task<string> FetchTextAsync(Uri url, Uri referer = null, string accept = null, string secFetchDest = null)
        {
            if (url == null) return null;
            
            var startTime = DateTimeOffset.UtcNow;
            LogNetwork($"[GET] {url}");
            
            // Handle ms-appx scheme locally
            if (string.Equals(url.Scheme, "ms-appx", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var file = await StorageFile.GetFileFromApplicationUriAsync(url);
                    var result = await FileIO.ReadTextAsync(file);
                    var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                    LogNetwork($"[OK] {url} ({elapsed:F0}ms) [local]");
                    return result;
                }
                catch (Exception ex)
                {
                    var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                    LogNetwork($"[ERR] {url} ({elapsed:F0}ms) {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[FetchText] ms-appx failed: {url} {ex.Message}");
                    return null;
                }
            }

            url = UpgradeIfHsts(url);
            LastTextResponseUri = null;
            string partition = SafePartition(referer != null ? (referer.Host ?? "") : "");
            var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("cache_" + partition, CreationCollisionOption.OpenIfExists);
            var key = url.AbsoluteUri; var fname = HashForFile(key) + ".txt"; var meta = HashForFile(key) + ".meta";

            try
            {
                var item = await folder.GetItemAsync(fname) as StorageFile;
                var metaItem = await folder.GetItemAsync(meta) as StorageFile;
                if (item != null && metaItem != null)
                {
                    try
                    {
                        var metaPayload = await FileIO.ReadTextAsync(metaItem);
                        var timestampPart = metaPayload;
                        var finalPart = string.Empty;
                        if (!string.IsNullOrEmpty(metaPayload))
                        {
                            var split = metaPayload.IndexOf('|');
                            if (split >= 0)
                            {
                                timestampPart = metaPayload.Substring(0, split);
                                finalPart = metaPayload.Substring(split + 1);
                            }
                        }

                        DateTimeOffset ts;
                        if (DateTimeOffset.TryParse(timestampPart, out ts) && (DateTimeOffset.UtcNow - ts) < DiskCacheTtl)
                        {
                            Uri cachedFinal = null;
                            if (!string.IsNullOrWhiteSpace(finalPart))
                            {
                                try { if (!Uri.TryCreate(finalPart, UriKind.Absolute, out cachedFinal)) cachedFinal = null; } catch { cachedFinal = null; }
                            }
                            LastTextResponseUri = cachedFinal ?? url;
                            return await FileIO.ReadTextAsync(item);
                        }
                    }
                    catch { /* swallow */ }
                }
            }
            catch { /* swallow */ }

            var refererOriginal = referer;
            Uri previousRequest = null;

            try
            {
                var _startFetch = DateTimeOffset.UtcNow;
                Uri current = url; HttpResponseMessage resp = null; int hops = 0; HttpRequestMessage req = null;
                while (hops < 5)
                {
                    req = new HttpRequestMessage(HttpMethod.Get, current);
                    AddHeaderSafe(req, "Accept", string.IsNullOrWhiteSpace(accept) ? "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7" : accept);
                    // Update to Chrome 131
                    var ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
                    AddHeaderSafe(req, "User-Agent", ua);
                    AddHeaderSafe(req, "Accept-Language", "en-US,en;q=0.9");
                    // Let HttpClient handle encoding
                    // AddHeaderSafe(req, "Accept-Encoding", "gzip, deflate, br");
                    
                    // Remove Sec-Fetch headers as they might be incorrect and trigger WAF
                    /*
                    AddHeaderSafe(req, "Sec-Fetch-Dest", string.IsNullOrWhiteSpace(secFetchDest) ? "empty" : secFetchDest);
                    var fetchMode = "cors";
                    if (destLower == "document" || destLower == "iframe") fetchMode = "navigate";
                    else if (destLower == "style" || destLower == "script" || destLower == "image" || destLower == "font") fetchMode = "no-cors";
                    AddHeaderSafe(req, "Sec-Fetch-Mode", fetchMode);
                    var effectiveReferer = refererOriginal ?? previousRequest;
                    if (effectiveReferer != null) AddHeaderSafe(req, "Referer", effectiveReferer.AbsoluteUri);
                    AddHeaderSafe(req, "Sec-Fetch-Site", DetermineSecFetchSite(effectiveReferer, current));
                    */
                    var effectiveReferer = refererOriginal ?? previousRequest;
                    if (effectiveReferer != null) AddHeaderSafe(req, "Referer", effectiveReferer.AbsoluteUri);
                    // Per-type timeout: styles/scripts faster than docs
                    var cts = new System.Threading.CancellationTokenSource();
                    try
                    {
                        int sec = 8;
                        var d = (secFetchDest ?? "").ToLowerInvariant();
                        if (d == "document" || d == "iframe") sec = 12;
                        cts.CancelAfter(System.TimeSpan.FromSeconds(sec));
                    }
                    catch { /* swallow */ }
                    try { resp = await _http.SendRequestAsync(req).AsTask(cts.Token); }
                    catch (Exception sendEx)
                    {
                        var msg = sendEx.Message;
                        // UWP blocks HTTPS->HTTP redirects at protocol level
                        // Also handle generic connection errors that may be caused by HTTPS->HTTP redirect blocking
                        if (current.Scheme == "https" && hops == 0)
                        {
                            // First attempt on HTTPS failed - try HTTP fallback
                            // This handles both explicit redirect errors and connection failures caused by redirect blocking
                            var httpUri = new UriBuilder(current) { Scheme = "http", Port = -1 }.Uri;
                            System.Diagnostics.Debug.WriteLine($"[FetchText] HTTPS failed ({sendEx.Message.Substring(0, Math.Min(50, sendEx.Message.Length))}), retrying HTTP: {httpUri}");
                            current = httpUri;
                            continue;
                        }
                        try { System.Diagnostics.Debug.WriteLine("[FetchTextError] send failed " + current + " ex=" + sendEx.Message); } catch { /* swallow */ }
                        resp = null;
                    }
                    if (resp == null) break;
                    var code = (int)resp.StatusCode;
                    if (code >= 300 && code < 400 && resp.Headers.Location != null)
                    {
                        var loc = resp.Headers.Location; 
                        if (!loc.IsAbsoluteUri) loc = new Uri(current, loc);
                        
                        // Log redirect
                        System.Diagnostics.Debug.WriteLine($"[FetchText] Redirect {code}: {current} -> {loc}");
                        
                        previousRequest = current;
                        current = UpgradeIfHsts(loc);
                        hops++;
                        continue;
                    }
                    break;
                }
                if (resp == null || !resp.IsSuccessStatusCode)
                {
                    var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                    LogNetwork($"[FAIL] {url} ({elapsed:F0}ms) status={(resp != null ? (int)resp.StatusCode : 0)}");
                    try { System.Diagnostics.Debug.WriteLine("[FetchTextFail] url=" + url + " hops=" + hops + " status=" + (resp!=null?(int)resp.StatusCode:0)); } catch { /* swallow */ }
                    return null;
                }
                var finalUri = resp?.RequestMessage?.RequestUri ?? current ?? url;
                LastTextResponseUri = finalUri;
                NoteHsts(resp, finalUri ?? url);

                var ct = resp.Content != null && resp.Content.Headers != null && resp.Content.Headers.ContentType != null ? resp.Content.Headers.ContentType.MediaType : null;
                string text = null;
                try { text = await resp.Content.ReadAsStringAsync(); }
                catch (Exception bodyEx)
                {
                    try { System.Diagnostics.Debug.WriteLine("[FetchTextError] body read failed url=" + url + " ex=" + bodyEx.Message); } catch { /* swallow */ }
                    text = null;
                }
                try { var _elapsed = DateTimeOffset.UtcNow - _startFetch; var _msg = "[FetchText] " + url + " in " + (int)_elapsed.TotalMilliseconds + "ms"; System.Diagnostics.Debug.WriteLine(_msg); if (LogSink != null) LogSink(_msg); } catch { /* swallow */ }
                LogNetwork($"[OK] {url} ({(DateTimeOffset.UtcNow - startTime).TotalMilliseconds:F0}ms) {(text?.Length ?? 0)} bytes");

                if (LooksTextual(ct))
                {
                    // memory cache
                    var entry = new TextEntry { Body = text ?? string.Empty, ContentType = ct ?? string.Empty };
                    var pair = Tuple.Create(key, entry);
                    var node = new LinkedListNode<Tuple<string, TextEntry>>(pair);
                    lock (_textLock) {
                        LinkedListNode<Tuple<string, TextEntry>> old;
                        if (_textMap.TryGetValue(key, out old) && old != null) { try { _textLru.Remove(old); } catch { } }
                        _textLru.AddFirst(node); _textMap[key] = node;
                        if (_textLru.Count > _textCap) { var last = _textLru.Last; if (last != null) { _textMap.Remove(last.Value.Item1); _textLru.RemoveLast(); } }
                    }

                    // disk cache
                    try
                    {
                        var file = await folder.CreateFileAsync(fname, CreationCollisionOption.ReplaceExisting);
                        await FileIO.WriteTextAsync(file, entry.Body);
                        var mfile = await folder.CreateFileAsync(meta, CreationCollisionOption.ReplaceExisting);
                        var metaPayload = DateTimeOffset.UtcNow.ToString("o") + "|" + (finalUri != null ? finalUri.AbsoluteUri : string.Empty);
                        await FileIO.WriteTextAsync(mfile, metaPayload);
                    }
                    catch { /* swallow */ }
                }
                if (string.IsNullOrEmpty(text))
                {
                    try { System.Diagnostics.Debug.WriteLine("[FetchTextEmpty] url=" + url); } catch { /* swallow */ }
                }
                return text;
            }
            catch (Exception ex) {
                try {
                    var msg = $"[FetchTextException] url={url} ex={ex.Message}";
                    System.Diagnostics.Debug.WriteLine(msg);
                    LogSink?.Invoke(msg);
                } catch { /* swallow */ }
                // Return a styled error page
                var eUrl = System.Net.WebUtility.HtmlEncode(url?.ToString() ?? "(null)");
                var eMsg = System.Net.WebUtility.HtmlEncode(ex.Message);
                return $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"/><style>body{{font-family:Segoe UI,sans-serif;background:#1e1e1e;color:#ccc;display:flex;justify-content:center;align-items:center;height:100vh;margin:0;padding:20px;box-sizing:border-box}}div{{max-width:500px;text-align:center}}h1{{color:#ff5555;font-size:24px;margin:0 0 12px}}p{{color:#999;font-size:14px;margin:0 0 20px;word-break:break-all}}.url{{color:#777;font-size:12px}}.icon{{font-size:48px;margin-bottom:16px}}</style></head><body><div><div class=icon>⚠</div><h1>Page Load Failed</h1><p>{eMsg}</p><p class=url>{eUrl}</p></div></body></html>";
            }
        }

        // Extended variant with explicit UA and Accept-Encoding overrides for multi-strategy fallback
        public async Task<string> FetchTextWithOptionsAsync(Uri url, Uri referer, string accept, string secFetchDest, string userAgentOverride, string acceptEncodingOverride)
        {
            if (url == null) return null;
            LastTextResponseUri = null;
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                AddHeaderSafe(req, "Accept", string.IsNullOrWhiteSpace(accept) ? "*/*" : accept);
                AddHeaderSafe(req, "User-Agent", string.IsNullOrWhiteSpace(userAgentOverride) ? "Mozilla/5.0" : userAgentOverride);
                AddHeaderSafe(req, "Accept-Language", "en-US,en;q=0.9");
                AddHeaderSafe(req, "Accept-Encoding", string.IsNullOrWhiteSpace(acceptEncodingOverride) ? "gzip, deflate" : acceptEncodingOverride);
                AddHeaderSafe(req, "Sec-Fetch-Dest", string.IsNullOrWhiteSpace(secFetchDest) ? "empty" : secFetchDest);
                var fetchMode = "cors";
                var destLower = (secFetchDest ?? string.Empty).ToLowerInvariant();
                if (destLower == "document" || destLower == "iframe") fetchMode = "navigate";
                else if (destLower == "style" || destLower == "script" || destLower == "image" || destLower == "font") fetchMode = "no-cors";
                AddHeaderSafe(req, "Sec-Fetch-Mode", fetchMode);
                if (referer != null) AddHeaderSafe(req, "Referer", referer.AbsoluteUri);
                AddHeaderSafe(req, "Sec-Fetch-Site", DetermineSecFetchSite(referer, url));
                var cts = new System.Threading.CancellationTokenSource();
                try { cts.CancelAfter(TimeSpan.FromSeconds(12)); } catch { /* swallow */ }
                HttpResponseMessage resp = null;
                try { resp = await _http.SendRequestAsync(req).AsTask(cts.Token); } catch (Exception sendEx) { try { System.Diagnostics.Debug.WriteLine("[FetchTextOptError] send " + url + " ex=" + sendEx.Message); } catch { /* swallow */ } }
                if (resp == null || !resp.IsSuccessStatusCode)
                { try { System.Diagnostics.Debug.WriteLine("[FetchTextOptFail] url=" + url + " status=" + (resp!=null?(int)resp.StatusCode:0)); } catch { /* swallow */ } return null; }
                LastTextResponseUri = resp.RequestMessage != null ? resp.RequestMessage.RequestUri : url;
                string text = null; try { text = await resp.Content.ReadAsStringAsync(); } catch (Exception bodyEx) { try { System.Diagnostics.Debug.WriteLine("[FetchTextOptError] body " + url + " ex=" + bodyEx.Message); } catch { /* swallow */ } }
                if (string.IsNullOrEmpty(text)) { try { System.Diagnostics.Debug.WriteLine("[FetchTextOptEmpty] url=" + url); } catch { /* swallow */ } }
                return text;
            }
            catch { return null; }
        }

        // Image with redirect, memory cache, and disk cache (configurable TTL)
        public async Task<IRandomAccessStream> FetchImageAsync(Uri url, Uri referer = null, ResourcePriority priority = ResourcePriority.Normal)
        {
            if (url == null) return null;

            var startTime = DateTimeOffset.UtcNow;
            LogNetwork($"[IMG] {url}");

            // Handle ms-appx scheme locally
            if (string.Equals(url.Scheme, "ms-appx", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var file = await StorageFile.GetFileFromApplicationUriAsync(url);
                    var result = await file.OpenReadAsync();
                    var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                    LogNetwork($"[OK] {url} ({elapsed:F0}ms) [local]");
                    return result;
                }
                catch (Exception ex)
                {
                    var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                    LogNetwork($"[ERR] {url} ({elapsed:F0}ms) {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[FetchImage] ms-appx failed: {url} {ex.Message}");
                    return null;
                }
            }

            // Handle data URIs
            if (string.Equals(url.Scheme, "data", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var dataUri = url.OriginalString;
                    var commaIndex = dataUri.IndexOf(',');
                    if (commaIndex > 5)
                    {
                        var header = dataUri.Substring(5, commaIndex - 5);
                        var data = dataUri.Substring(commaIndex + 1);
                        bool isBase64 = header.IndexOf("base64", StringComparison.OrdinalIgnoreCase) >= 0;
                        byte[] bytes;
                        if (isBase64)
                        {
                            try { bytes = Convert.FromBase64String(data); }
                            catch { System.Diagnostics.Debug.WriteLine($"[IMG FAIL] {url}: Invalid base64 encoding"); return null; }
                        }
                        else
                        {
                            bytes = Encoding.UTF8.GetBytes(Uri.UnescapeDataString(data));
                        }
                        System.Diagnostics.Debug.WriteLine($"[FetchImage] data URI loaded: {bytes.Length} bytes");
                        return bytes.AsBuffer().AsStream().AsRandomAccessStream();
                    }
                    else { System.Diagnostics.Debug.WriteLine($"[IMG FAIL] {url}: Malformed data URI"); return null; }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[IMG FAIL] {url}: {ex.Message}"); return null; }
            }

            url = UpgradeIfHsts(url);
            var key = url.AbsoluteUri;

            // --- Memory cache check ---
            LinkedListNode<Tuple<string, ImgEntry>> node;
            lock (_imgLock)
            {
                if (_imgMap.TryGetValue(key, out node) && node != null && node.Value != null && node.Value.Item2 != null && node.Value.Item2.Buffer != null)
                {
                    return node.Value.Item2.Buffer.AsStream().AsRandomAccessStream();
                }
            }

            // --- Disk cache check ---
            string partition = SafePartition(referer != null ? referer.Host : "");
            var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("cache_" + partition, CreationCollisionOption.OpenIfExists);
            var fname = HashForFile(key) + ".img";
            var mname = HashForFile(key) + ".meta";
            var fileLockKey = partition + ":" + fname;
            var fileLock = _fileLocks.GetOrAdd(fileLockKey, _ => new SemaphoreSlim(1, 1));
            await fileLock.WaitAsync();
            try
            {
                try
                {
                    var imgItem = await folder.GetItemAsync(fname) as StorageFile;
                    var metaItem = await folder.GetItemAsync(mname) as StorageFile;
                    if (imgItem != null && metaItem != null)
                    {
                        var metaPayload = await FileIO.ReadTextAsync(metaItem);
                        DateTimeOffset ts;
                        if (!string.IsNullOrEmpty(metaPayload) && DateTimeOffset.TryParse(metaPayload, out ts) && (DateTimeOffset.UtcNow - ts) < DiskCacheTtl)
                        {
                            var buf = await FileIO.ReadBufferAsync(imgItem);
                        var entry = new ImgEntry { Buffer = buf, ContentType = null };
                        var pair = Tuple.Create(key, entry);
                        var n = new LinkedListNode<Tuple<string, ImgEntry>>(pair);
                        lock (_imgLock) {
                            LinkedListNode<Tuple<string, ImgEntry>> old;
                            if (_imgMap.TryGetValue(key, out old) && old != null) { try { _imgLru.Remove(old); } catch { } }
                            _imgLru.AddFirst(n); _imgMap[key] = n;
                        }
                            System.Diagnostics.Debug.WriteLine($"[FetchImage] disk cache hit: {key}");
                            return buf.AsStream().AsRandomAccessStream();
                        }
                    }
                }
                catch { /* swallow */ }

                // --- Low-priority gate ---
                if (priority == ResourcePriority.Low)
                    await _lowPriorityGate.WaitAsync();
                try
                {
                    var _startImg = DateTimeOffset.UtcNow;
                    Uri current = url; HttpResponseMessage resp = null; int hops = 0; HttpRequestMessage req = null;
                    while (hops < 5)
                    {
                        req = new HttpRequestMessage(HttpMethod.Get, current);
                        AddHeaderSafe(req, "Accept", "image/apng,image/png,image/jpeg,image/*,*/*;q=0.8");
                        AddHeaderSafe(req, "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
                        AddHeaderSafe(req, "Accept-Language", "en-US,en;q=0.9");
                        AddHeaderSafe(req, "Accept-Encoding", "gzip, deflate");
                        AddHeaderSafe(req, "Sec-Fetch-Dest", "image");
                        AddHeaderSafe(req, "Sec-Fetch-Mode", "no-cors");
                        if (referer != null) AddHeaderSafe(req, "Referer", referer.AbsoluteUri);
                        AddHeaderSafe(req, "Sec-Fetch-Site", DetermineSecFetchSite(referer, current));
                        var cts = new System.Threading.CancellationTokenSource();
                        try { cts.CancelAfter(System.TimeSpan.FromSeconds(8)); } catch { /* swallow */ }
                        try { resp = await _http.SendRequestAsync(req).AsTask(cts.Token); }
                        catch { resp = null; }
                        if (resp == null) break;
                        var code = (int)resp.StatusCode;
                        if (code >= 300 && code < 400 && resp.Headers.Location != null)
                        {
                            var loc = resp.Headers.Location; if (!loc.IsAbsoluteUri) loc = new Uri(current, loc);
                            current = UpgradeIfHsts(loc);
                            hops++;
                            continue;
                        }
                        break;
                    }
                    if (resp == null || !resp.IsSuccessStatusCode)
                    {
                        try { System.Diagnostics.Debug.WriteLine("[FetchImageFail] status=" + (resp != null ? ((int)resp.StatusCode).ToString() : "0") + " url=" + url); } catch { /* swallow */ }
                        try { var fallback = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Logo.scale-240.png")); return await fallback.OpenReadAsync(); } catch { /* swallow */ }
                        return null;
                    }
                    NoteHsts(resp, url);

                    var buf = await resp.Content.ReadAsBufferAsync();

                    // Memory cache
                    var entry = new ImgEntry { Buffer = buf, ContentType = resp.Content?.Headers?.ContentType?.MediaType };
                    var pair = Tuple.Create(key, entry);
                    var n = new LinkedListNode<Tuple<string, ImgEntry>>(pair);
                    lock (_imgLock) {
                        LinkedListNode<Tuple<string, ImgEntry>> old;
                        if (_imgMap.TryGetValue(key, out old) && old != null) { try { _imgLru.Remove(old); } catch { } }
                        _imgLru.AddFirst(n); _imgMap[key] = n;
                        if (_imgLru.Count > _imgCap) { var last = _imgLru.Last; if (last != null) { _imgMap.Remove(last.Value.Item1); _imgLru.RemoveLast(); } }
                    }

                    // Disk cache write (protected by fileLock)
                    try
                    {
                        var imgFile = await folder.CreateFileAsync(fname, CreationCollisionOption.ReplaceExisting);
                        await FileIO.WriteBufferAsync(imgFile, buf);
                        var metaFile = await folder.CreateFileAsync(mname, CreationCollisionOption.ReplaceExisting);
                        await FileIO.WriteTextAsync(metaFile, DateTimeOffset.UtcNow.ToString("o"));
                    }
                    catch { /* swallow */ }

                    // Try Skia for modern formats and SVGs
                    try
                    {
                        var bytes = buf.ToArray();
                        var sk = await DecodeWithSkiaAsync(bytes, entry.ContentType, url);
                        if (sk != null) return sk;
                    }
                    catch { /* swallow */ }

                    try { System.Diagnostics.Debug.WriteLine("[FetchImage] " + url + " in " + (int)(DateTimeOffset.UtcNow - _startImg).TotalMilliseconds + "ms"); } catch { /* swallow */ }
                    return buf.AsStream().AsRandomAccessStream();
                }
                finally
                {
                    if (priority == ResourcePriority.Low)
                        _lowPriorityGate.Release();
                }
            }
            catch (Exception ex)
            {
                try { System.Diagnostics.Debug.WriteLine("[FetchImageException] url=" + url + " ex=" + ex.Message); } catch { /* swallow */ }
                try { var fallback = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Logo.scale-240.png")); return await fallback.OpenReadAsync(); } catch { /* swallow */ }
                var transparentPng = new byte[] { 137,80,78,71,13,10,26,10,0,0,0,13,73,72,68,82,0,0,0,1,0,0,0,1,8,6,0,0,0,31,21,196,137,0,0,0,13,73,68,65,84,120,156,99,0,1,0,0,5,0,1,13,10,26,10,0,0,0,0,73,69,78,68,174,66,96,130 };
                return transparentPng.AsBuffer().AsStream().AsRandomAccessStream();
            }
            finally
            {
                fileLock.Release();
                // Cleanup: remove from dictionary if uncontested (safe to leak a few entries)
                if (fileLock.CurrentCount == 1)
                {
                    SemaphoreSlim existing;
                    if (_fileLocks.TryGetValue(fileLockKey, out existing) && existing == fileLock)
                    {
                        _fileLocks.TryRemove(fileLockKey, out existing);
                    }
                }
            }
        }

        private static bool LooksSvg(Uri u)
        {
            try { var p = u != null ? (u.AbsolutePath ?? "") : ""; return p.EndsWith(".svg", StringComparison.OrdinalIgnoreCase); } catch { return false; }
        }

        private static bool IsLikelyModernFormat(string contentType, Uri url)
        {
            var ct = (contentType ?? "").ToLowerInvariant();
            if (ct.Contains("svg") || ct.Contains("webp") || ct.Contains("avif")) return true;
            if (LooksSvg(url)) return true;
            var p = url != null ? (url.AbsolutePath ?? "") : "";
            return p.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".avif", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureSkiaTypes()
        {
            if (_skiaTypesResolved) return;
            lock (_skiaLock)
            {
                if (_skiaTypesResolved) return;
                _skiaTypeSvg = Type.GetType("Svg.Skia.SKSvg") ?? Type.GetType("Svg.Skia.SKSvg, Svg.Skia");
                _skiaTypeImage = Type.GetType("SkiaSharp.SKImage") ?? Type.GetType("SkiaSharp.SKImage, SkiaSharp");
                _skiaTypeData = Type.GetType("SkiaSharp.SKData") ?? Type.GetType("SkiaSharp.SKData, SkiaSharp");
                _skiaTypeEncFmt = Type.GetType("SkiaSharp.SKEncodedImageFormat") ?? Type.GetType("SkiaSharp.SKEncodedImageFormat, SkiaSharp");
                _skiaTypesResolved = true;
            }
        }

        private static MethodInfo GetCachedMethod(Type t, string name, Type[] args)
        {
            if (t == null || string.IsNullOrEmpty(name)) return null;
            var key = t.FullName + "::" + name + "(" + (args != null ? string.Join(",", args.Select(a => a.Name).ToArray()) : "") + ")";
            MethodInfo mi;
            if (_skiaMethods.TryGetValue(key, out mi)) return mi;
            mi = GetMethodBestEffort(t, name, args);
            _skiaMethods[key] = mi;
            return mi;
        }

        private static PropertyInfo GetCachedProperty(Type t, string name)
        {
            if (t == null || string.IsNullOrEmpty(name)) return null;
            var key = t.FullName + "::" + name;
            PropertyInfo pi;
            if (_skiaProperties.TryGetValue(key, out pi)) return pi;
            pi = GetPropertyBestEffort(t, name);
            _skiaProperties[key] = pi;
            return pi;
        }

        private async Task<IRandomAccessStream> DecodeWithSkiaAsync(byte[] data, string contentType, Uri url)
        {
            try
            {
                if (data == null || data.Length == 0) return null;
                bool trySvg = (contentType ?? "").IndexOf("svg", StringComparison.OrdinalIgnoreCase) >= 0 || LooksSvg(url);

                EnsureSkiaTypes();

                if (trySvg && _skiaTypeSvg != null && _skiaTypeImage != null && _skiaTypeData != null)
                {
                    try
                    {
                        var mCreateCopy = GetCachedMethod(_skiaTypeData, "CreateCopy", new[] { typeof(byte[]) });
                        var d = mCreateCopy != null ? mCreateCopy.Invoke(null, new object[] { data }) : null;
                        var svg = Activator.CreateInstance(_skiaTypeSvg);
                        var mLoad = GetCachedMethod(_skiaTypeSvg, "Load", new[] { _skiaTypeData });
                        if (svg != null && d != null && mLoad != null)
                        {
                            mLoad.Invoke(svg, new object[] { d });
                            var pImg = GetCachedProperty(_skiaTypeSvg, "Image");
                            if (pImg != null)
                            {
                                var imgObj = pImg.GetValue(svg);
                                if (imgObj != null && _skiaTypeEncFmt != null)
                                {
                                    var mEncode = GetCachedMethod(imgObj.GetType(), "Encode", new[] { _skiaTypeEncFmt, typeof(int) });
                                    if (mEncode != null)
                                    {
                                        var fmtPng = Enum.Parse(_skiaTypeEncFmt, "Png");
                                        var skDataOut = mEncode.Invoke(imgObj, new object[] { fmtPng, 90 });
                                        if (skDataOut != null)
                                        {
                                            var mToArray = GetCachedMethod(skDataOut.GetType(), "ToArray", new Type[0]);
                                            var bytes = mToArray != null ? (byte[])mToArray.Invoke(skDataOut, null) : null;
                                            if (bytes != null) return bytes.AsBuffer().AsStream().AsRandomAccessStream();
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { /* swallow */ }
                }

                if (IsLikelyModernFormat(contentType, url) && _skiaTypeData != null && _skiaTypeImage != null && _skiaTypeEncFmt != null)
                {
                    try
                    {
                        var mCreateCopy = GetCachedMethod(_skiaTypeData, "CreateCopy", new[] { typeof(byte[]) });
                        var d = mCreateCopy != null ? mCreateCopy.Invoke(null, new object[] { data }) : null;
                        var mFrom = GetCachedMethod(_skiaTypeImage, "FromEncodedData", new[] { _skiaTypeData });
                        var img = (mFrom != null && d != null) ? mFrom.Invoke(null, new object[] { d }) : null;
                        if (img != null)
                        {
                            var fmtPng = Enum.Parse(_skiaTypeEncFmt, "Png");
                            var mEncode = GetCachedMethod(img.GetType(), "Encode", new[] { _skiaTypeEncFmt, typeof(int) });
                            if (mEncode != null)
                            {
                                var skDataOut = mEncode.Invoke(img, new object[] { fmtPng, 90 });
                                if (skDataOut != null)
                                {
                                    var mToArray = GetCachedMethod(skDataOut.GetType(), "ToArray", new Type[0]);
                                    var bytes = mToArray != null ? (byte[])mToArray.Invoke(skDataOut, null) : null;
                                    if (bytes != null) return bytes.AsBuffer().AsStream().AsRandomAccessStream();
                                }
                            }
                        }
                    }
                    catch { /* swallow */ }
                }
            }
            catch { /* swallow */ }
            return null;
        }

        private static PropertyInfo GetPropertyBestEffort(Type t, string name)
        {
            if (t == null || string.IsNullOrEmpty(name)) return null;
            try
            {
                var ti = t.GetTypeInfo();
                var p = ti.GetDeclaredProperty(name);
                if (p != null) return p;
                try { return t.GetRuntimeProperty(name); } catch { /* swallow */ }
            }
            catch { /* swallow */ }
            return null;
        }

        private static MethodInfo GetMethodBestEffort(Type t, string name, Type[] args)
        {
            if (t == null || string.IsNullOrEmpty(name)) return null;
            try
            {
                var ti = t.GetTypeInfo();
                var m = ti.GetDeclaredMethod(name);
                if (m != null) return m;
                try { return t.GetRuntimeMethod(name, args); } catch { /* swallow */ }
            }
            catch { /* swallow */ }
            return null;
        }

        // Generic binary fetcher for fonts and other non-text assets
        public async Task<byte[]> FetchBytesAsync(Uri url, Uri referer = null, string accept = null, string secFetchDest = null, ResourcePriority priority = ResourcePriority.Normal)
        {
            if (url == null) return null;
            url = UpgradeIfHsts(url);
            try
            {
                Uri current = url; HttpResponseMessage resp = null; int hops = 0; HttpRequestMessage req = null;
                while (hops < 10)
                {
                    req = new HttpRequestMessage(HttpMethod.Get, current);
                    AddHeaderSafe(req, "Accept", string.IsNullOrWhiteSpace(accept) ? "*/*" : accept);
                    if (!string.IsNullOrWhiteSpace(secFetchDest)) AddHeaderSafe(req, "Sec-Fetch-Dest", secFetchDest);
                    AddHeaderSafe(req, "Sec-Fetch-Mode", "no-cors");
                    if (referer != null) AddHeaderSafe(req, "Referer", referer.AbsoluteUri);
                    var cts = new System.Threading.CancellationTokenSource();
                    try
                    {
                        int sec = 8;
                        var d = (secFetchDest ?? "").ToLowerInvariant();
                        if (d == "font") sec = 12; // fonts can be larger
                        cts.CancelAfter(System.TimeSpan.FromSeconds(sec));
                    }
                    catch { /* swallow */ }
                    try { resp = await _http.SendRequestAsync(req).AsTask(cts.Token); }
                    catch { resp = null; }
                    if (resp == null) break;
                    var code = (int)resp.StatusCode;
                    if (code >= 300 && code < 400 && resp.Headers.Location != null)
                    {
                        var loc = resp.Headers.Location; if (!loc.IsAbsoluteUri) loc = new Uri(current, loc);
                        var prev = current;
                        current = UpgradeIfHsts(loc);
                        referer = prev;
                        hops++;
                        continue;
                    }
                    break;
                }
                if (resp == null || !resp.IsSuccessStatusCode) return null;
                NoteHsts(resp, url);

                var buf = await resp.Content.ReadAsBufferAsync();
                return buf?.ToArray();
            }
            catch { return null; }
        }

        public async void ClearCache()
        {
            lock (_textLock) { try { _textMap.Clear(); _textLru.Clear(); } catch { } }
            lock (_imgLock) { try { _imgMap.Clear(); _imgLru.Clear(); } catch { } }
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var items = await folder.GetItemsAsync();
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Name != null && items[i].Name.StartsWith("cache_"))
                        await items[i].DeleteAsync();
                }
            }
            catch { }
        }
    }
}
