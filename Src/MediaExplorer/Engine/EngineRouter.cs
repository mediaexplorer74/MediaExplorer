using System;
using System.Collections.Generic;
using Windows.Storage;

namespace BrowserCore.Engine
{
    public enum EngineType
    {
        NiLJS,
        EdgeHTML,
        AI,
        Remote,
        Auto
    }

    public class EngineDecision
    {
        public EngineType Engine { get; set; }
        public string Reason { get; set; }
        public bool IsFallback { get; set; }
    }

    public enum RescuePreference
    {
        None,
        AI,
        Poor,
        Edge,
        Remote
    }

    public static class EngineRouter
    {
        private static Dictionary<string, EngineType> _siteOverrides = new Dictionary<string, EngineType>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, RescuePreference> _rescuePreferences = new Dictionary<string, RescuePreference>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;

        private static readonly HashSet<string> _knownSpaHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "duckduckgo.com",
            "www.duckduckgo.com",
            "developer.mozilla.org",
            "mdn.dev",
            "dev.to",
            "medium.com",
            "www.medium.com",
            "towardsdatascience.com",
            "substack.com",
            "docs.github.com",
            "github.com",
            "stackoverflow.com",
            "reddit.com",
            "www.reddit.com",
            "old.reddit.com"
        };

        private static readonly HashSet<string> _knownNiLJsHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "news.ycombinator.com",
            "nokiadesignarchive.aalto.fi",
            "example.com",
            "jquery.com",
            "getbootstrap.com",
            "4pda.to",
            "www.4pda.to"
        };

        static EngineRouter()
        {
            LoadConfig();
        }

        public static EngineDecision SelectEngine(string url, int canvasChildCount, string textContent)
        {
            if (string.IsNullOrEmpty(url))
                return new EngineDecision { Engine = EngineType.NiLJS, Reason = "default" };

            string host = ExtractHost(url);

            // 1. Per-site override
            EngineType overrideEngine;
            if (_siteOverrides.TryGetValue(host, out overrideEngine) && overrideEngine != EngineType.Auto)
            {
                return new EngineDecision { Engine = overrideEngine, Reason = "per-site override" };
            }

            // 2. Known SPA hosts → EdgeHTML
            if (_knownSpaHosts.Contains(host))
            {
                return new EngineDecision { Engine = EngineType.EdgeHTML, Reason = "known SPA" };
            }

            // 3. Known NiL.JS-friendly hosts → NiLJS
            if (_knownNiLJsHosts.Contains(host))
            {
                return new EngineDecision { Engine = EngineType.NiLJS, Reason = "known simple site" };
            }

            // 4. Default: try NiLJS first (will fallback via SmartFallbackRenderer)
            return new EngineDecision { Engine = EngineType.NiLJS, Reason = "default — will fallback if render fails" };
        }

    private static string ExtractHost(string url)
        {
            try
            {
                var uri = new Uri(url);
                return uri.Host.ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }

        private static void LoadConfig()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                var s = ApplicationData.Current.LocalSettings;
                if (s.Values.TryGetValue("SiteEngines", out var v) && v is string json)
                {
                    var lines = json.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('=');
                        if (parts.Length == 2 && Enum.TryParse<EngineType>(parts[1], true, out var eng))
                        {
                            _siteOverrides[parts[0].Trim()] = eng;
                        }
                    }
                }
                if (s.Values.TryGetValue("SiteRescuePrefs", out var rv) && rv is string rescueJson)
                {
                    var lines = rescueJson.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('=');
                        if (parts.Length == 2 && Enum.TryParse<RescuePreference>(parts[1], true, out var pref))
                            _rescuePreferences[parts[0].Trim()] = pref;
                    }
                }
            }
            catch { }
        }

        public static void SaveConfig()
        {
            try
            {
                var lines = new List<string>();
                foreach (var kv in _siteOverrides)
                    lines.Add(kv.Key + "=" + kv.Value);
                ApplicationData.Current.LocalSettings.Values["SiteEngines"] = string.Join("\n", lines);

                var rescueLines = new List<string>();
                foreach (var kv in _rescuePreferences)
                    rescueLines.Add(kv.Key + "=" + kv.Value);
                ApplicationData.Current.LocalSettings.Values["SiteRescuePrefs"] = string.Join("\n", rescueLines);
            }
            catch { }
        }

        public static void SetSiteEngine(string host, EngineType engine)
        {
            if (string.IsNullOrWhiteSpace(host)) return;
            host = host.ToLowerInvariant().Trim();
            if (engine == EngineType.Auto)
                _siteOverrides.Remove(host);
            else
                _siteOverrides[host] = engine;
            SaveConfig();
        }

        public static EngineType GetSiteEngine(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return EngineType.Auto;
            host = host.ToLowerInvariant().Trim();
            if (_siteOverrides.TryGetValue(host, out var eng))
                return eng;
            return EngineType.Auto;
        }

        public static void SetRescuePreference(string host, RescuePreference pref)
        {
            if (string.IsNullOrWhiteSpace(host)) return;
            host = host.ToLowerInvariant().Trim();
            if (pref == RescuePreference.None)
                _rescuePreferences.Remove(host);
            else
                _rescuePreferences[host] = pref;
            SaveConfig();
        }

        public static RescuePreference GetRescuePreference(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return RescuePreference.None;
            host = host.ToLowerInvariant().Trim();
            if (_rescuePreferences.TryGetValue(host, out var pref))
                return pref;
            return RescuePreference.None;
        }

        public static bool IsKnownSpaHost(string url)
        {
            var host = ExtractHost(url);
            return !string.IsNullOrWhiteSpace(host) && _knownSpaHosts.Contains(host);
        }

        public static string GetHostFromUrl(string url)
        {
            return ExtractHost(url);
        }
    }
}
