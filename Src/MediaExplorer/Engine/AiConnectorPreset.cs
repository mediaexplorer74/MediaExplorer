using System;
using Windows.Storage;

namespace BrowserCore.Engine
{
    public enum ConnectorType
    {
        Ultra,
        Rich,
        Poor,
        Asceti,
        Smart
    }

    public enum ContentSkill
    {
        Auto,
        General,
        Code,
        News,
        Translate,
        Quick
    }

    public class AiConnectorConfig
    {
        public ConnectorType Type { get; set; }
        public string Name { get; set; }
        public string ApiKey { get; set; }
        public string ModelFamily { get; set; }
        public string ModelId { get; set; }
        public bool Enabled { get; set; }
        public bool AutoFormat { get; set; }
        public bool IsOnline { get; set; }
        public double QualityThreshold { get; set; }
        public int MaxAttempts { get; set; }
        public double DailyBudgetUsd { get; set; }

        public static AiConnectorConfig[] GetDefaults()
        {
            return new[]
            {
                new AiConnectorConfig
                {
                    Type = ConnectorType.Ultra, Name = "Ultra",
                    ModelFamily = "anthropic", ModelId = "claude-sonnet-4-20250514",
                    Enabled = true, AutoFormat = true, IsOnline = true,
                    QualityThreshold = 1.0, MaxAttempts = 1, DailyBudgetUsd = 1.0
                },
                new AiConnectorConfig
                {
                    Type = ConnectorType.Rich, Name = "Rich",
                    ModelFamily = "openai", ModelId = "gpt-4o",
                    Enabled = true, AutoFormat = true, IsOnline = true,
                    QualityThreshold = 0.8, MaxAttempts = 1, DailyBudgetUsd = 0.5
                },
                new AiConnectorConfig
                {
                    Type = ConnectorType.Poor, Name = "Poor",
                    ModelFamily = "mistralai", ModelId = "ministral-8b-2512",
                    Enabled = true, AutoFormat = true, IsOnline = true,
                    QualityThreshold = 0.5, MaxAttempts = 1, DailyBudgetUsd = 0.1
                },
                new AiConnectorConfig
                {
                    Type = ConnectorType.Asceti, Name = "Asceti",
                    ModelFamily = "google", ModelId = "gemma-4-26b-a4b-it:free",
                    Enabled = true, AutoFormat = true, IsOnline = true,
                    QualityThreshold = 0.3, MaxAttempts = 1, DailyBudgetUsd = 0.0
                },
                new AiConnectorConfig
                {
                    Type = ConnectorType.Smart, Name = "Smart",
                    ModelFamily = "", ModelId = "",
                    Enabled = true, AutoFormat = true, IsOnline = true,
                    QualityThreshold = 0.6, MaxAttempts = 3, DailyBudgetUsd = 0.5
                }
            };
        }
    }

    public static class ConnectorStorage
    {
        private static string Prefix(ConnectorType t) => "Conn_" + t.ToString() + "_";

        public static void Save(AiConnectorConfig cfg)
        {
            try
            {
                var s = ApplicationData.Current.LocalSettings;
                string p = Prefix(cfg.Type);
                s.Values[p + "Enabled"] = cfg.Enabled;
                s.Values[p + "ApiKey"] = cfg.ApiKey ?? "";
                s.Values[p + "Family"] = cfg.ModelFamily ?? "";
                s.Values[p + "Model"] = cfg.ModelId ?? "";
                s.Values[p + "AutoFormat"] = cfg.AutoFormat;
                s.Values[p + "Threshold"] = cfg.QualityThreshold;
                s.Values[p + "MaxAttempts"] = cfg.MaxAttempts;
                s.Values[p + "Budget"] = cfg.DailyBudgetUsd;
            }
            catch { }
        }

        public static AiConnectorConfig Load(ConnectorType type, AiConnectorConfig defaults)
        {
            try
            {
                var s = ApplicationData.Current.LocalSettings;
                string p = Prefix(type);
                var cfg = new AiConnectorConfig
                {
                    Type = type,
                    Name = defaults.Name,
                    IsOnline = defaults.IsOnline
                };
                if (s.Values.TryGetValue(p + "Enabled", out var v1) && v1 is bool b) cfg.Enabled = b; else cfg.Enabled = defaults.Enabled;
                if (s.Values.TryGetValue(p + "ApiKey", out var v2) && v2 is string k) cfg.ApiKey = k; else cfg.ApiKey = "";
                if (s.Values.TryGetValue(p + "Family", out var v3) && v3 is string f) cfg.ModelFamily = f; else cfg.ModelFamily = defaults.ModelFamily;
                if (s.Values.TryGetValue(p + "Model", out var v4) && v4 is string m) cfg.ModelId = m; else cfg.ModelId = defaults.ModelId;
                if (s.Values.TryGetValue(p + "AutoFormat", out var v5) && v5 is bool af) cfg.AutoFormat = af; else cfg.AutoFormat = defaults.AutoFormat;
                if (s.Values.TryGetValue(p + "Threshold", out var v6) && v6 is double th) cfg.QualityThreshold = th; else cfg.QualityThreshold = defaults.QualityThreshold;
                if (s.Values.TryGetValue(p + "MaxAttempts", out var v7) && v7 is int ma) cfg.MaxAttempts = ma; else cfg.MaxAttempts = defaults.MaxAttempts;
                if (s.Values.TryGetValue(p + "Budget", out var v8) && v8 is double bu) cfg.DailyBudgetUsd = bu; else cfg.DailyBudgetUsd = defaults.DailyBudgetUsd;
                return cfg;
            }
            catch { }
            return defaults;
        }

        public static ConnectorType LoadActiveConnector()
        {
            try
            {
                var s = ApplicationData.Current.LocalSettings;
                if (s.Values.TryGetValue("ActiveConnector", out var v) && v is string name)
                {
                    if (System.Enum.TryParse<ConnectorType>(name, out var t))
                        return t;
                }
            }
            catch { }
            return ConnectorType.Smart;
        }

        public static void SaveActiveConnector(ConnectorType type)
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values["ActiveConnector"] = type.ToString();
            }
            catch { }
        }

        public static void SaveDailyUsage(double costUsd)
        {
            try
            {
                var s = ApplicationData.Current.LocalSettings;
                string key = "AI_Usage_" + System.DateTime.UtcNow.ToString("yyyyMMdd");
                double current = 0;
                if (s.Values.TryGetValue(key, out var v) && v is double d) current = d;
                s.Values[key] = current + costUsd;
            }
            catch { }
        }

        public static double LoadDailyUsage()
        {
            try
            {
                var s = ApplicationData.Current.LocalSettings;
                string key = "AI_Usage_" + System.DateTime.UtcNow.ToString("yyyyMMdd");
                if (s.Values.TryGetValue(key, out var v) && v is double d) return d;
            }
            catch { }
            return 0.0;
        }
    }

    public static class SkillRouter
    {
        private static readonly string[] _codeHosts = { "github.com", "stackoverflow.com", "gitlab.com", "codepen.io", "jsfiddle.net" };
        private static readonly string[] _newsHosts = { "news.ycombinator.com", "reddit.com", "lobste.rs", "techcrunch.com", "theverge.com", "arstechnica.com" };
        private static readonly string[] _translateHosts = { "4pda.to", "www.4pda.to", "habr.com", "vc.ru", "dtf.ru" };

        public static ContentSkill DetectSkill(string url, string pageText)
        {
            if (string.IsNullOrEmpty(url)) return ContentSkill.Auto;

            try
            {
                var uri = new Uri(url);
                string host = uri.Host.ToLowerInvariant();

                foreach (var h in _codeHosts)
                    if (host.Contains(h)) return ContentSkill.Code;

                foreach (var h in _newsHosts)
                    if (host.Contains(h)) return ContentSkill.News;

                foreach (var h in _translateHosts)
                    if (host.Contains(h)) return ContentSkill.Translate;
            }
            catch { }

            if (!string.IsNullOrEmpty(pageText) && pageText.Length < 500)
                return ContentSkill.Quick;

            return ContentSkill.General;
        }

        public static string GetSkillPrompt(ContentSkill skill, string pageText)
        {
            switch (skill)
            {
                case ContentSkill.Code:
                    return "You are a code expert. Analyze this page and provide: 1) What the code/project does, 2) Key functions or classes, 3) Usage examples if any, 4) Notable patterns. Be concise and technical.";
                case ContentSkill.News:
                    return "You are a news analyst. Summarize this page: 1) Main headline/topic, 2) Key points (3-5 bullet points), 3) Who is affected, 4) Why it matters. Be concise and factual.";
                case ContentSkill.Translate:
                    return "You are a translator. Translate and summarize this page content. Provide: 1) Brief summary in English, 2) Key points translated, 3) Any technical terms kept in original language. Be accurate.";
                case ContentSkill.Quick:
                    return "Provide a brief, concise answer. One or two sentences maximum.";
                case ContentSkill.General:
                default:
                    return "Provide a clear, structured summary: 1) What this page is about, 2) Key sections and content, 3) Important links or references. Be thorough but concise.";
            }
        }
    }
}
