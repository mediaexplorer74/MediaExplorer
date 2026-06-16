using System;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;

namespace BrowserCore.Engine
{
    public class SmartFallbackRenderer
    {
        private readonly Func<string, Task<string>> _fetchPageText;
        private readonly Action<string, bool> _showAiResult;
        private readonly Action _startReadingMode;
        private readonly Action<string> _showToast;
        private bool _fallbackTriggered;
        private string _lastUrl;

        public SmartFallbackRenderer(
            Func<string, Task<string>> fetchPageText,
            Action<string, bool> showAiResult,
            Action startReadingMode,
            Action<string> showToast)
        {
            _fetchPageText = fetchPageText;
            _showAiResult = showAiResult;
            _startReadingMode = startReadingMode;
            _showToast = showToast;
        }

        public bool IsPageEmpty(int canvasChildrenCount, string textContent, string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (url == _lastUrl && _fallbackTriggered) return false;

            bool tooFewElements = canvasChildrenCount < 5;
            bool tooLittleText = string.IsNullOrWhiteSpace(textContent) || textContent.Trim().Length < 50;

            bool isBlockPage = false;
            if (!string.IsNullOrWhiteSpace(textContent))
            {
                var t = textContent.ToLowerInvariant();
                if (t.Contains("network error") || t.Contains("could not load") ||
                    t.Contains("access denied") || t.Contains("blocked") ||
                    t.Contains("connection refused") || t.Contains("server not found") ||
                    t.Contains("err_connection") || t.Contains("this site can't") ||
                    t.Contains("please wait for verification") ||
                    t.Contains("checking your browser") ||
                    t.Contains("verify you are human") ||
                    t.Contains("challenge-platform") ||
                    t.Contains("cf-challenge") ||
                    t.Contains("just a moment"))
                    isBlockPage = true;
            }

            bool isCodeJunk = false;
            if (!string.IsNullOrWhiteSpace(textContent) && textContent.Length > 100)
            {
                int codeChars = 0;
                int totalChars = textContent.Length;
                foreach (char c in textContent)
                {
                    if (c == '{' || c == '}' || c == ';' || c == ':' ||
                        c == '(' || c == ')' || c == '=' || c == '>' || c == '<' ||
                        c == '!' || c == '/' || c == '\\' || c == '*' || c == '&')
                        codeChars++;
                }
                double codeRatio = (double)codeChars / totalChars;
                var t = textContent.ToLowerInvariant();
                int cssSelectors = 0;
                if (t.Contains("font-family")) cssSelectors++;
                if (t.Contains("border-radius")) cssSelectors++;
                if (t.Contains("!important")) cssSelectors++;
                if (t.Contains("display:")) cssSelectors++;
                if (t.Contains("margin-bottom")) cssSelectors++;
                if (t.Contains("text-transform")) cssSelectors++;
                if (t.Contains("function(") || t.Contains("function (")) cssSelectors++;
                if (t.Contains("var ") && t.Contains("=")) cssSelectors++;
                if (t.Contains(".prototype")) cssSelectors++;
                if (t.Contains("document.")) cssSelectors++;
                if (t.Contains("window.")) cssSelectors++;
                if (t.Contains("addEventListener")) cssSelectors++;

                if (codeRatio > 0.30 || cssSelectors >= 3)
                    isCodeJunk = true;
            }

            return (tooFewElements && tooLittleText) || isBlockPage || isCodeJunk;
        }

        public async Task TryFallbackAsync(string url)
        {
            if (_fallbackTriggered && _lastUrl == url) return;
            _fallbackTriggered = true;
            _lastUrl = url;

            var defaults = AiConnectorConfig.GetDefaults();
            var activeType = ConnectorStorage.LoadActiveConnector();

            // Smart downshift: if Smart mode, try cheapest first
            if (activeType == ConnectorType.Smart)
            {
                await TrySmartDownshift(url);
                return;
            }

            var cfg = ConnectorStorage.Load(activeType, defaults[(int)activeType]);
            await TrySingleConnector(url, cfg);
        }

        private async Task TrySmartDownshift(string url)
        {
            var defaults = AiConnectorConfig.GetDefaults();
            ConnectorType[] cascade = { ConnectorType.Asceti, ConnectorType.Poor, ConnectorType.Rich, ConnectorType.Ultra };

            double dailyBudget = 0.5;
            try
            {
                var s = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (s.Values.TryGetValue("SmartDailyBudget", out var v) && v is double d) dailyBudget = d;
            }
            catch { }

            double dailyUsage = ConnectorStorage.LoadDailyUsage();

            _showAiResult?.Invoke("Smart mode: trying best free option first...", true);
            string pageText = await _fetchPageText(url);

            if (string.IsNullOrWhiteSpace(pageText))
            {
                _showAiResult?.Invoke("Could not fetch page content from: " + url, false);
                return;
            }

            if (pageText.Length > 12000)
                pageText = pageText.Substring(0, 12000) + "\n[truncated]";

            // Detect skill for prompt selection
            var skill = SkillRouter.DetectSkill(url, pageText);

            foreach (var connectorType in cascade)
            {
                var cfg = ConnectorStorage.Load(connectorType, defaults[(int)connectorType]);
                if (!cfg.Enabled) continue;
                if (string.IsNullOrWhiteSpace(cfg.ApiKey)) continue;
                if (cfg.DailyBudgetUsd > 0 && dailyUsage >= dailyBudget)
                {
                    DevToolsLogger.Log("[DIAG:SMART] Skipping " + cfg.Name + " — daily budget exhausted (" + dailyUsage + "/" + dailyBudget + ")");
                    continue;
                }

                DevToolsLogger.Log("[DIAG:SMART] Trying " + cfg.Name + " (" + cfg.ModelFamily + "/" + cfg.ModelId + ") skill=" + skill);
                _showAiResult?.Invoke("Trying " + cfg.Name + " (" + cfg.ModelId + ")...", true);

                string prompt = SkillRouter.GetSkillPrompt(skill, pageText);
                var result = await ApiClient.SummarizeAsync(cfg.ApiKey, cfg.ModelFamily, cfg.ModelId, pageText, prompt);

                if (!string.IsNullOrEmpty(result) && !result.StartsWith("Error:"))
                {
                    double estimatedCost = connectorType == ConnectorType.Asceti ? 0.0 : 0.01;
                    ConnectorStorage.SaveDailyUsage(estimatedCost);

                    string footer = "\n\n---\nPowered by " + cfg.Name + " (" + cfg.ModelId + ")" +
                                    (skill != ContentSkill.Auto ? " • Skill: " + skill : "");
                    _showAiResult?.Invoke(result + footer, false);
                    DevToolsLogger.Log("[DIAG:SMART] Success with " + cfg.Name);
                    return;
                }

                DevToolsLogger.Log("[DIAG:SMART] " + cfg.Name + " failed: " + (result ?? "null"));
            }

            _showAiResult?.Invoke("All connectors failed. Check API keys in Settings → AI Connectors.", false);
        }

        private async Task TrySingleConnector(string url, AiConnectorConfig cfg)
        {
            DevToolsLogger.Log("[DIAG:FALLBACK] Triggered for " + url + " connector=" + cfg.Name + " model=" + cfg.ModelId);

            if (!cfg.Enabled)
            {
                DevToolsLogger.Log("[DIAG:FALLBACK] Connector " + cfg.Name + " disabled");
                _showAiResult?.Invoke("AI fallback disabled for " + cfg.Name + " connector.", false);
                return;
            }

            string apiKey = cfg.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                DevToolsLogger.Log("[DIAG:FALLBACK] No API key for " + cfg.Name);
                _showToast?.Invoke("No API key for " + cfg.Name + " connector. Configure in Settings → AI Connectors.");
                return;
            }

            _showAiResult?.Invoke("Fetching page content...", true);

            try
            {
                string pageText = await _fetchPageText(url);
                DevToolsLogger.Log("[DIAG:FALLBACK] Fetched text length=" + (pageText?.Length ?? 0));

                if (string.IsNullOrWhiteSpace(pageText))
                {
                    DevToolsLogger.Log("[DIAG:FALLBACK] No text fetched");
                    _showAiResult?.Invoke("Could not fetch page content from: " + url, false);
                    return;
                }

                if (pageText.Length > 12000)
                    pageText = pageText.Substring(0, 12000) + "\n[truncated]";

                var skill = SkillRouter.DetectSkill(url, pageText);
                string prompt = SkillRouter.GetSkillPrompt(skill, pageText);

                _showAiResult?.Invoke("Analyzing with " + cfg.Name + " (" + cfg.ModelId + ") • " + skill + "...", true);
                DevToolsLogger.Log("[DIAG:FALLBACK] Calling API model=" + cfg.ModelFamily + "/" + cfg.ModelId + " skill=" + skill);
                var summary = await ApiClient.SummarizeAsync(apiKey, cfg.ModelFamily, cfg.ModelId, pageText, prompt);
                DevToolsLogger.Log("[DIAG:FALLBACK] API response length=" + (summary?.Length ?? 0));

                string footer = "\n\n---\nPowered by " + cfg.Name + " (" + cfg.ModelId + ")" +
                                (skill != ContentSkill.Auto ? " • Skill: " + skill : "");
                _showAiResult?.Invoke(summary + footer, false);
            }
            catch (Exception ex)
            {
                DevToolsLogger.Log("[DIAG:FALLBACK] Error: " + ex.Message);
                _showAiResult?.Invoke("AI fallback error: " + ex.Message, false);
            }
        }

        public void Reset()
        {
            _fallbackTriggered = false;
            _lastUrl = null;
        }
    }
}
