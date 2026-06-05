using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Globalization;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using BrowserCore.Engine;

namespace WEBVIEW.Engine
{
    /// <summary>
    /// Loads CSS from inline &lt;style&gt; and external &lt;link rel="stylesheet"&gt;, parses rules,
    /// and produces a computed-style map for LiteElement nodes.
    ///
    /// Design goals:
    /// - Backward-compatible with DomBasicRenderer + RendererStyles (fills both typed props and Map).
    /// - Works with cookie-aware fetchers via delegate.
    /// - Handles basic selectors: tag, .class, #id, descendant/child, commas.
    /// - Handles !important, specificity, and source order.
    /// - Handles @import (bounded, cycle-safe).
    /// - Avoids throwing; logs instead.
    /// </summary>
    public static class CssLoader
    {
        // Phase S.4: hard cap on CSS rules per ParseRules call.
        public const int MaxCssRules = 5000;
        // Per-call log dedup flag (resets each ParseRules invocation).
        // Implemented as a static because ParseRules is static; on multithreaded
        // use this would need to be a [ThreadStatic], but CssLoader isn't called
        // concurrently today (caller is CustomHtmlEngine under a UI lock).
        private static bool ruleCapLogged = false;

        // Cached parsed rules from last ComputeAsync call (for incremental re-cascade)
        private static List<CssRule> _cachedRules = new List<CssRule>();

        // Compiled regex for hot patterns
        private static readonly Regex _importUrlRx = new Regex(@"@import\s+(url\((['""]?)(?<u>[^)'""]+)\2\)|(['""])(?<u2>[^'""]+)\4)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _minWidthRx = new Regex(@"min-width\s*:\s*(?<v>[0-9]+)px", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _maxWidthRx = new Regex(@"max-width\s*:\s*(?<v>[0-9]+)px", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _minHeightRx = new Regex(@"min-height\s*:\s*(?<v>[0-9]+)px", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _maxHeightRx = new Regex(@"max-height\s*:\s*(?<v>[0-9]+)px", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _urlFuncRx = new Regex(@"url\(['""]?(?<u>[^)'""]+)['""]?\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _hexColorRx = new Regex(@"(#[0-9a-fA-F]{3,8}|rgba?\([^)]+\)|[a-zA-Z]+)", RegexOptions.Compiled);
        private static readonly Regex _borderPxRx = new Regex(@"([0-9]+)px", RegexOptions.Compiled);
        private static readonly Regex _varRefRx = new Regex(@"var\((--[^)]+)\)", RegexOptions.Compiled);
        private static readonly Regex _minDppxRx = new Regex(@"min(-webkit-)?(device-pixel-ratio|resolution)\s*:\s*(?<v>[0-9.]+)(dppx|x)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _maxDppxRx = new Regex(@"max(-webkit-)?(device-pixel-ratio|resolution)\s*:\s*(?<v>[0-9.]+)(dppx|x)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ===========================
        // Public API
        // ===========================

        /// <summary>
        /// Main entry point: fetches external styles, parses all CSS, and computes styles for the document.
        /// </summary>
        /// <param name="root">Document root (html or the parsed tree root).</param>
        /// <param name="baseUri">Base URI for resolving &lt;link&gt;, @import, url(...).</param>
        /// <param name="fetchExternalCssAsync">
        /// Delegate to fetch external CSS text for a given absolute URL (cookie-aware if needed).
        /// If null, external styles are skipped gracefully.
        /// </param>
        /// <param name="viewportWidth">Optional viewport width for simple media checks.</param>
        /// <param name="log">Optional logger for warnings/notes.</param>
        public static async Task<Dictionary<LiteElement, CssComputed>> ComputeAsync(
            LiteElement root,
            Uri baseUri,
            Func<Uri, Task<string>> fetchExternalCssAsync,
            double? viewportWidth = null,
            Action<string> log = null)
        {
            if (root == null)
                return new Dictionary<LiteElement, CssComputed>();

            var cssBlobs = new List<CssSource>(); // collected CSS texts with source ordering
            int sourceIndex = 0;

            // 0) UA stylesheet (very small normalize) — lowest precedence
            try
            {
                var uaCss = @"
                    /* Basic UA defaults for readability on phone */
                    html,body{background:#fff;color:#222;margin:0;padding:0;}
                    body{font:16px/1.5 'Segoe UI',Roboto,Arial,sans-serif;}
                    a{color:#1a0dab;text-decoration:underline;}
                    /* Footer/nav polish: compact inline links with spacing */
                    nav a, footer a, .footer a, #footer a, .nav a, .menu a{ display:inline-block; margin:0 8px 6px 0; }
                    nav ul, footer ul, .footer ul, #footer ul, .nav ul, .menu ul, .uiList{ list-style:none; padding:0; margin:0.5em 0; }
                    nav li, footer li, .footer li, #footer li, .nav li, .menu li, .uiList li{ display:inline-block; margin:0 10px 6px 0; }
                    a:focus,a:hover{ text-decoration:underline; }
                    img{border:0;max-width:100%;height:auto;}
                    input,button,select,textarea{font:inherit;}
                    /* Block semantics */
                    article,aside,nav,section,header,footer,main,figure{display:block;}
                    h1{font-size:2em;margin:0.67em 0;font-weight:600;}
                    h2{font-size:1.5em;margin:0.83em 0;font-weight:600;}
                    h3{font-size:1.17em;margin:1em 0;font-weight:600;}
                    h4{font-size:1em;margin:1.33em 0;font-weight:600;}
                    h5{font-size:0.83em;margin:1.67em 0;font-weight:600;}
                    h6{font-size:0.67em;margin:2.33em 0;font-weight:600;}
                    p{margin:1em 0;}
                    ul,ol{margin:1em 0;padding-left:2em;}
                    li{margin:0.25em 0;}
                    small{font-size:0.875em;}
                    strong,b{font-weight:600;}
                    em,i{font-style:italic;}
                    figure{margin:1em 0;}
                    figcaption{font-size:0.875em;color:#555;}
                    table{border-collapse:collapse;}
                    th{font-weight:600;text-align:left;}
                    td,th{padding:0.4em 0.6em;}
                ";
                cssBlobs.Add(new CssSource { CssText = uaCss, Origin = CssOrigin.UserAgent, SourceOrder = sourceIndex++, BaseUri = baseUri });
            }
            catch { /* Ignore UA style errors */ }

            // 1) Inline <style> tags first (DOM order)
            foreach (var n in root.Descendants().Where(n => !n.IsText && n.Tag == "style"))
            {
                var text = SafeGatherText(n);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    // System.Diagnostics.Debug.WriteLine($"[CssLoader] Found style block: {text.Length} chars");
                    // System.Diagnostics.Debug.WriteLine($"[CssLoader] Style content: {text.Substring(0, Math.Min(text.Length, 100))}...");
                    cssBlobs.Add(new CssSource
                    {
                        CssText = text,
                        Origin = CssOrigin.Inline,
                        SourceOrder = sourceIndex++,
                        BaseUri = baseUri
                    });
                }
                else
                {
                    // System.Diagnostics.Debug.WriteLine("[CssLoader] Found empty style block");
                }
            }

            // 2) External <link rel="stylesheet"> (DOM order)
            // Fetch in parallel with a small concurrency limit to avoid blocking UI
            var linkNodes = root.Descendants().Where(n => !n.IsText && n.Tag == "link").ToList();
            var extTasks = new List<Task>();
            var gate = new System.Threading.SemaphoreSlim(8); // Shared gate for all CSS fetches (links + imports)
            foreach (var link in linkNodes)
            {
                if (link.Attr == null) continue;
                string rel; if (!link.Attr.TryGetValue("rel", out rel)) continue; if (!ContainsToken(rel, "stylesheet")) continue;
                string href; if (!link.Attr.TryGetValue("href", out href) || string.IsNullOrWhiteSpace(href)) continue;
                // Respect media attribute — support media types + parenthesized features
                string media;
                if (link.Attr.TryGetValue("media", out media) && !string.IsNullOrWhiteSpace(media))
                {
                    var m = media.ToLowerInvariant();
                    if (!EvaluateMediaQuery(m)) continue;
                }
                var abs = ResolveUri(baseUri, href);
                if (abs == null || fetchExternalCssAsync == null) continue;

                await gate.WaitAsync();
                var order = sourceIndex++;
                var t = Task.Run(async () =>
                {
                    try
                    {
                        var css = await fetchExternalCssAsync(abs).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(css))
                        {
                            lock (cssBlobs)
                            {
                                cssBlobs.Add(new CssSource
                                {
                                    CssText = css,
                                    Origin = CssOrigin.External,
                                    SourceOrder = order,
                                    BaseUri = abs
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(log, "[CssLoader] Failed to fetch CSS: " + abs + " :: " + ex.Message);
                    }
                    finally { try { gate.Release(); } catch { /* Ignore release errors */ } }
                });
                extTasks.Add(t);
            }
            if (extTasks.Count > 0) { try { await Task.WhenAll(extTasks).ConfigureAwait(false); } catch { /* Ignore fetch errors */ } }

            // 3) Expand @import (depth-bounded)
            var expanded = await ExpandImportsAsync(cssBlobs, fetchExternalCssAsync, viewportWidth, log, gate);

            // 4) Parse rules from all sources (parallel, bounded)
            var allRules = new List<CssRule>();
            var parseGate = new System.Threading.SemaphoreSlim(4);
            var parseTasks = new List<Task>();
            foreach (var blob in expanded)
            {
                await parseGate.WaitAsync();
                parseTasks.Add(Task.Run(() =>
                {
                    try
                    {
                        var parsed = ParseRules(blob.CssText, blob.SourceOrder, blob.BaseUri, viewportWidth, log);
                        lock (allRules) allRules.AddRange(parsed);
                    }
                    catch (Exception ex)
                    {
                        Log(log, "[CssLoader] ParseRules failed: " + ex.Message);
                    }
                    finally { try { parseGate.Release(); } catch { /* Ignore release errors */ } }
                }));
            }
            if (parseTasks.Count > 0) { try { await Task.WhenAll(parseTasks).ConfigureAwait(false); } catch { /* Ignore parse errors */ } }

            // Cache rules for incremental re-cascade
            // Note: CSS variable resolution (var(--name)) is handled per-element
            // in CascadeIntoComputedStyles via ResolveCustomPropertyReferences,
            // which walks the inherited CustomProperties chain. Global pre-resolution
            // via ResolveVariables was removed because it uses only :root values,
            // breaking per-element overrides.
            lock (_cachedRules)
            {
                _cachedRules.Clear();
                _cachedRules.AddRange(allRules);
            }

            // 5) Compute per-element cascaded styles
            try
            {
                var computed = CascadeIntoComputedStyles(root, allRules, log);
                return computed;
            }
            catch (Exception ex)
            {
                try { DevToolsLogger.Log("[CSS] Cascade failed: " + ex.Message); } catch { }
                return new Dictionary<LiteElement, CssComputed>();
            }
        }

        /// <summary>
        /// Overload without viewport/log parameters.
        /// </summary>
        public static Task<Dictionary<LiteElement, CssComputed>> ComputeAsync(
            LiteElement root,
            Uri baseUri,
            Func<Uri, Task<string>> fetchExternalCssAsync)
        {
            return ComputeAsync(root, baseUri, fetchExternalCssAsync, null, null);
        }

        // ===========================
        // Model & helpers
        // ===========================

        private enum CssOrigin { Inline, External, Imported,
            UserAgent
        }

        private sealed class CssSource
        {
            public string CssText;
            public CssOrigin Origin;
            public int SourceOrder;
            public Uri BaseUri;
        }

        internal sealed class CssRule
        {
            public List<SelectorChain> Selectors = new List<SelectorChain>(); // comma-separated selectors
            public Dictionary<string, CssDecl> Declarations = new Dictionary<string, CssDecl>(StringComparer.OrdinalIgnoreCase);
            public int SourceOrder;    // to break ties
            public Uri BaseUri;        // for url() resolving
        }

        internal sealed class CssDecl
        {
            public string Name;
            public string Value;
            public bool Important;
            public int Specificity;
        }

        internal sealed class SelectorChain
        {
            public List<SelectorSegment> Segments = new List<SelectorSegment>(); // left-to-right parsed
            public int Specificity; // computed from segments
        }

        internal enum Combinator { Descendant, Child }

        internal sealed class SelectorSegment
        {
            public string Tag;                    // e.g. "div"
            public string Id;                     // e.g. "main"
            public List<string> Classes;          // e.g. ["foo","bar"]
            public List<string> PseudoClasses;    // e.g. [":first-child"]
            public List<Tuple<string, string, string>> Attributes; // e.g. [("type", "=", "text")]
            public Combinator? Next;              // relation to the NEXT segment (left-to-right)
        }

        // ===========================
        // Stage 1: Import expansion
        // ===========================

        private static async Task<List<CssSource>> ExpandImportsAsync(
            List<CssSource> sources,
            Func<Uri, Task<string>> fetchExternal,
            double? viewportWidth,
            Action<string> log,
            System.Threading.SemaphoreSlim gate)
        {
            var output = new List<CssSource>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var guard = new RecursionGuard();

            foreach (var s in sources)
            {
                await ExpandOneAsync(s, fetchExternal, seen, output, viewportWidth, log, guard, gate);

            }
            return output;
        }

        // Keeps track of @import recursion without needing ref/out on async methods
        private sealed class RecursionGuard
        {
            public int Count;
        }

        private static async Task ExpandOneAsync(
            CssSource source,
            Func<Uri, Task<string>> fetchExternal,
            HashSet<string> seenUrls,
            List<CssSource> output,
            double? viewportWidth,
            Action<string> log,
            RecursionGuard guard,
            System.Threading.SemaphoreSlim gate)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.CssText))
                return;

            // Hard cap to avoid runaway recursion
            if ((guard != null ? guard.Count : 0) > 2048)
            {
                Log(log, "[CssLoader] Import expansion limit reached.");
                return;
            }

            string text = source.CssText;
            text = StripComments(text);

            var imports = new List<string>();
            var sb = new StringBuilder();

            int idx = 0;
            while (idx < text.Length)
            {
                if (StartsWithAt(text, idx, "@import"))
                {
                    int semi = text.IndexOf(';', idx);
                    if (semi < 0) { break; }
                    var importLine = text.Substring(idx, semi - idx + 1);
                    idx = semi + 1;

                    var url = ExtractImportUrl(importLine);
                    if (!string.IsNullOrWhiteSpace(url))
                        imports.Add(url);
                }
                else
                {
                    sb.Append(text[idx]);
                    idx++;
                }
            }

            // Resolve + fetch imports (parallel, bounded)
            // var gate = new System.Threading.SemaphoreSlim(4); // REMOVED: use shared gate
            var tasks = new List<System.Threading.Tasks.Task>();
            foreach (var imp in imports)
            {
                var abs = ResolveUri(source.BaseUri, imp);
                if (abs == null) continue;
                var key = abs.AbsoluteUri;
                if (seenUrls.Contains(key)) continue; // cycle
                seenUrls.Add(key);
                if (fetchExternal == null) continue;

                await gate.WaitAsync().ConfigureAwait(false);
                tasks.Add(System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var css = await fetchExternal(abs).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(css))
                        {
                            if (guard != null) guard.Count++;
                            await ExpandOneAsync(
                                new CssSource
                                {
                                    CssText = css,
                                    Origin = CssOrigin.Imported,
                                    SourceOrder = source.SourceOrder,
                                    BaseUri = abs
                                },
                                fetchExternal,
                                seenUrls,
                                output,
                                viewportWidth,
                                log,
                                guard,
                                gate).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex) { Log(log, "[CssLoader] @import fetch failed: " + abs + " :: " + ex.Message); }
                    finally { try { gate.Release(); } catch { /* Ignore release errors */ } }
                }));
            }
            if (tasks.Count > 0) { try { await System.Threading.Tasks.Task.WhenAll(tasks).ConfigureAwait(false); } catch { /* Ignore task errors */ } }

            // Keep the remainder (with @imports stripped)
            output.Add(new CssSource
            {
                CssText = sb.ToString(),
                Origin = source.Origin,
                SourceOrder = source.SourceOrder,
                BaseUri = source.BaseUri
            });
        }


        private static bool StartsWithAt(string s, int idx, string token)
        {
            if (idx + token.Length > s.Length) return false;
            return string.Compare(s, idx, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static string ExtractImportUrl(string importLine)
        {
            // Handles: @import "x.css";  @import url('x.css');
            var m = _importUrlRx.Match(importLine ?? "");
            if (m.Success)
            {
                var u = !string.IsNullOrEmpty(m.Groups["u"].Value) ? m.Groups["u"].Value : m.Groups["u2"].Value;
                return u.Trim();
            }
            return null;
        }

        // ===========================
        // Stage 2: Parsing rules
        // ===========================

        private static List<CssRule> ParseRules(string css, int sourceOrder, Uri baseForUrls, double? viewportWidth, Action<string> log)
        {
            ruleCapLogged = false; // reset per call
            var rules = new List<CssRule>();
            if (string.IsNullOrWhiteSpace(css)) return rules;

            var text = StripComments(css);

            // (Very) basic @media handling: keep simple "screen" blocks; ignore others.
            // We flatten recognized @media blocks by inlining their contents.
            text = FlattenBasicMedia(text, viewportWidth, _viewportHeight, log);

            // Split by braces into selector/declarations pairs.
            // This is a naive parser but resistant to most content without nested braces in values.
            int i = 0;
            while (i < text.Length)
            {
                // find '{'
                int open = text.IndexOf('{', i);
                if (open < 0) break;
                var selectorText = text.Substring(i, open - i).Trim();
                int close = FindMatchingBrace(text, open);
                if (close < 0) break;

                var declText = text.Substring(open + 1, close - open - 1);
                i = close + 1;

                if (string.IsNullOrWhiteSpace(selectorText) || string.IsNullOrWhiteSpace(declText))
                    continue;

                var chains = ParseSelectors(selectorText);
                if (chains.Count == 0) continue;

                var decls = ParseDeclarations(declText);

                var rule = new CssRule { Selectors = chains, SourceOrder = sourceOrder, BaseUri = baseForUrls };
                foreach (var d in decls)
                {
                    // last declaration wins inside the same block
                    rule.Declarations[d.Name] = d;
                }
                // Phase S.4: hard cap on CSS rules per stylesheet. A single
                // page with 50 000 rules would dominate the cascade time.
                if (rules.Count >= MaxCssRules)
                {
                    if (!ruleCapLogged)
                    {
                        try { DevToolsLogger.Log("[WARN] CSS rule cap hit at " + MaxCssRules + " — further rules ignored"); } catch { }
                        ruleCapLogged = true;
                    }
                    continue;
                }
                rules.Add(rule);
            }

            return rules;
        }

        private static string FlattenBasicMedia(string text, double? viewportWidth, double? viewportHeight, Action<string> log)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.IndexOf("@media", StringComparison.OrdinalIgnoreCase) < 0) return text;

            var sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                if (StartsWithAt(text, i, "@media"))
                {
                    int open = text.IndexOf('{', i);
                    if (open < 0) break;
                    int close = FindMatchingBrace(text, open);
                    if (close < 0) break;

                    var header = text.Substring(i, open - i).ToLowerInvariant();
                    var body = text.Substring(open + 1, close - open - 1);

                    bool keep = EvaluateMediaQuery(header);
                    if (keep) sb.Append(body);
                    i = close + 1;
                }
                else
                {
                    sb.Append(text[i]);
                    i++;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Evaluate a CSS media query string against the current device environment.
        /// Supports: screen/all media types, min/max-width, min/max-height,
        /// orientation, min/max-resolution/dppx/device-pixel-ratio,
        /// prefers-color-scheme, scripting, and the 'not' operator.
        /// </summary>
        private static bool EvaluateMediaQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            var q = query.ToLowerInvariant().Trim();

            // Handle 'not' prefix
            bool negate = false;
            if (q.StartsWith("not ", StringComparison.Ordinal))
            {
                negate = true;
                q = q.Substring(4).Trim();
            }

            // Strip 'only' and 'all'/'screen' media type prefixes
            if (q.StartsWith("only ", StringComparison.Ordinal)) q = q.Substring(5).Trim();
            if (q.StartsWith("screen", StringComparison.Ordinal)) q = q.Substring(6).Trim();
            else if (q.StartsWith("all", StringComparison.Ordinal)) q = q.Substring(3).Trim();

            // If nothing left after media type, match
            if (string.IsNullOrWhiteSpace(q)) return !negate;

            // Split on 'and' to get individual conditions
            var parts = q.Split(new[] { " and " }, StringSplitOptions.RemoveEmptyEntries);
            bool match = true;
            foreach (var part in parts)
            {
                var p = part.Trim().Trim('(', ')').Trim();
                if (string.IsNullOrWhiteSpace(p)) continue;

                if (p.StartsWith("min-width", StringComparison.Ordinal))
                {
                    double? expected = ExtractPxValue(p);
                    if (expected.HasValue && (!CssParser.MediaViewportWidth.HasValue || CssParser.MediaViewportWidth.Value < expected.Value)) { match = false; break; }
                }
                else if (p.StartsWith("max-width", StringComparison.Ordinal))
                {
                    double? expected = ExtractPxValue(p);
                    if (expected.HasValue && (!CssParser.MediaViewportWidth.HasValue || CssParser.MediaViewportWidth.Value > expected.Value)) { match = false; break; }
                }
                else if (p.StartsWith("min-height", StringComparison.Ordinal))
                {
                    double? expected = ExtractPxValue(p);
                    if (expected.HasValue && (!CssParser.MediaViewportHeight.HasValue || CssParser.MediaViewportHeight.Value < expected.Value)) { match = false; break; }
                }
                else if (p.StartsWith("max-height", StringComparison.Ordinal))
                {
                    double? expected = ExtractPxValue(p);
                    if (expected.HasValue && (!CssParser.MediaViewportHeight.HasValue || CssParser.MediaViewportHeight.Value > expected.Value)) { match = false; break; }
                }
                else if (p.Contains("orientation"))
                {
                    var isLandscape = CssParser.MediaViewportWidth.HasValue && CssParser.MediaViewportHeight.HasValue && CssParser.MediaViewportWidth.Value > CssParser.MediaViewportHeight.Value;
                    if (p.Contains("landscape") && !isLandscape) { match = false; break; }
                    if (p.Contains("portrait") && isLandscape) { match = false; break; }
                }
                else if (p.Contains("prefers-color-scheme"))
                {
                    var scheme = CssParser.MediaPrefersColorScheme ?? "light";
                    if ((p.Contains("dark") && scheme != "dark") || (p.Contains("light") && scheme != "light")) { match = false; break; }
                }
                else if (p.Contains("scripting"))
                {
                    var scripting = CssParser.MediaScripting ?? "enabled";
                    if (p.Contains("none") && scripting != "none") { match = false; break; }
                    if (p.Contains("initial-only") && scripting != "initial-only") { match = false; break; }
                    if (p.Contains("enabled") && scripting != "enabled") { match = false; break; }
                }
                else if (p.Contains("resolution") || p.Contains("device-pixel-ratio"))
                {
                    double? expected = ExtractDppxValue(p);
                    if (expected.HasValue)
                    {
                        double dpr = CssParser.MediaDppx ?? 1.0;
                        if (p.StartsWith("min", StringComparison.Ordinal) && dpr < expected.Value) { match = false; break; }
                        else if (p.StartsWith("max", StringComparison.Ordinal) && dpr > expected.Value) { match = false; break; }
                        else if (!p.StartsWith("min", StringComparison.Ordinal) && !p.StartsWith("max", StringComparison.Ordinal))
                        {
                            // Exact match: resolution: X
                            if (Math.Abs(dpr - expected.Value) > 0.01) { match = false; break; }
                        }
                    }
                }
                // Unknown features — be permissive, assume match
            }

            return negate ? !match : match;
        }

        /// <summary>
        /// Extract a pixel value from a media feature like "min-width: 768px"
        /// </summary>
        private static double? ExtractPxValue(string text)
        {
            var m = Regex.Match(text, @"(?:\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var num = Regex.Match(m.Value, @"\d+(?:\.\d+)?");
                if (num.Success)
                {
                    double v;
                    if (TryDouble(num.Value, out v)) return v;
                }
            }
            return null;
        }

        /// <summary>
        /// Extract a dppx value from a media feature like "min-resolution: 2dppx" or "min--webkit-device-pixel-ratio: 2"
        /// Handles dppx, x, dpi (converts dpi to dppx), and unitless (device-pixel-ratio convention)
        /// </summary>
        private static double? ExtractDppxValue(string text)
        {
            // Match: number followed by dppx, x, dpi, or bare number
            var m = Regex.Match(text, @"(?<v>\d+(?:\.\d+)?)\s*(?<u>dppx|x|dpi|dpcm)?", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                double v;
                if (!TryDouble(m.Groups["v"].Value, out v)) return null;
                var unit = m.Groups["u"].Value.ToLowerInvariant();
                if (unit == "dpi") return v / 96.0;   // 1dppx = 96dpi
                if (unit == "dpcm") return v / 37.8;  // 1dppx ≈ 37.8dpcm
                return v; // dppx, x, or unitless (device-pixel-ratio convention)
            }
            return null;
        }

        private static double? ExtractPx(string text, string prop)
        {
            Regex rx;
            if (string.Equals(prop, "min-width", StringComparison.OrdinalIgnoreCase)) rx = _minWidthRx;
            else if (string.Equals(prop, "max-width", StringComparison.OrdinalIgnoreCase)) rx = _maxWidthRx;
            else if (string.Equals(prop, "min-height", StringComparison.OrdinalIgnoreCase)) rx = _minHeightRx;
            else if (string.Equals(prop, "max-height", StringComparison.OrdinalIgnoreCase)) rx = _maxHeightRx;
            else return null;
            
            var m = rx.Match(text ?? "");
            if (m.Success)
            {
                double v;
                if (TryDouble(m.Groups["v"].Value, out v)) return v;
            }
            return null;
        }

        private static int FindMatchingBrace(string s, int openIdx)
        {
            int depth = 0;
            for (int i = openIdx; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static List<SelectorChain> ParseSelectors(string selectorText)
        {
            // Split on commas at top level (not inside anything else — here we assume plain selectors).
            var parts = selectorText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<SelectorChain>();
            foreach (var p in parts)
            {
                var chain = ParseSelectorChain(p.Trim());
                if (chain != null) list.Add(chain);
            }
            return list;
        }

        private static SelectorChain ParseSelectorChain(string s)
        {
            // Supports sequences separated by space (descendant) or '>' (child)
            // Each segment supports: tag (optional), #id, .class(.class2...)
            var chain = new SelectorChain();
            var tokens = TokenizeSelector(s);
            if (tokens.Count == 0) return null;

            var seg = new SelectorSegment { Classes = new List<string>() };
            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t == " ")
                {
                    seg.Next = Combinator.Descendant;
                    chain.Segments.Add(seg);
                    seg = new SelectorSegment { Classes = new List<string>() };
                }
                else if (t == ">")
                {
                    seg.Next = Combinator.Child;
                    chain.Segments.Add(seg);
                    seg = new SelectorSegment { Classes = new List<string>() };
                }
                else if (t.StartsWith("."))
                {
                    seg.Classes.Add(t.Substring(1));
                }
                else if (t.StartsWith("#"))
                {
                    seg.Id = t.Substring(1);
                }
                else if (t.StartsWith(":"))
                {
                    if (seg.PseudoClasses == null) seg.PseudoClasses = new List<string>();
                    seg.PseudoClasses.Add(t.Substring(1));
                }
                else if (t.StartsWith("["))
                {
                    // Basic attribute parsing: [attr] or [attr=val]
                    // Tokenizer likely returns "[attr=val]" or "[attr" ...
                    // We assume simple "[content]" format from tokenizer for now
                    var content = t.TrimStart('[').TrimEnd(']');
                    var eq = content.IndexOf('=');
                    if (seg.Attributes == null) seg.Attributes = new List<Tuple<string, string, string>>();
                    
                    if (eq < 0)
                    {
                        seg.Attributes.Add(Tuple.Create(content.Trim(), "", ""));
                    }
                    else
                    {
                        var name = content.Substring(0, eq).Trim();
                        var val = content.Substring(eq + 1).Trim().Trim('"', '\'');
                        seg.Attributes.Add(Tuple.Create(name, "=", val));
                    }
                }
                else
                {
                    seg.Tag = t;
                }
            }
            chain.Segments.Add(seg);

            // compute specificity: ids*100 + classes*10 + tags*1
            int ids = 0, cl = 0, tg = 0;
            foreach (var s2 in chain.Segments)
            {
                if (!string.IsNullOrEmpty(s2.Id)) ids++;
                if (!string.IsNullOrEmpty(s2.Tag)) tg++;
                if (s2.Classes != null) cl += s2.Classes.Count;
            }
            chain.Specificity = ids * 100 + cl * 10 + tg;

            return chain;
        }

        private static List<string> TokenizeSelector(string s)
        {
            // turns "div#main .x > span.y" into ["div","#main"," ",".x",">","span",".y"]
            var r = new List<string>();
            var sb = new StringBuilder();
            Action flush = () => { if (sb.Length > 0) { r.Add(sb.ToString()); sb.Clear(); } };

            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (char.IsWhiteSpace(c))
                {
                    flush();
                    // coalesce spaces
                    if (r.Count == 0 || r[r.Count - 1] != " ") r.Add(" ");
                }
                else if (c == '>')
                {
                    flush(); r.Add(">");
                }
                else if (c == '.' || c == '#' || c == ':' || c == '[')
                {
                    flush(); sb.Append(c);
                }
                else
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }
            flush();
            // trim leading/trailing spaces tokens
            if (r.Count > 0 && r[0] == " ") r.RemoveAt(0);
            if (r.Count > 0 && r[r.Count - 1] == " ") r.RemoveAt(r.Count - 1);
            return r;
        }

        private static List<CssDecl> ParseDeclarations(string declText)
        {
            var list = new List<CssDecl>();
            if (string.IsNullOrWhiteSpace(declText)) return list;
            // char-by-char to avoid Split allocations
            int i = 0;
            while (i < declText.Length)
            {
                // skip whitespace and leading semicolons
                while (i < declText.Length && (declText[i] == ';' || char.IsWhiteSpace(declText[i])))
                    i++;
                if (i >= declText.Length) break;

                // find ':'
                int colon = -1;
                int start = i;
                while (i < declText.Length && declText[i] != ';')
                {
                    if (declText[i] == ':' && colon < 0)
                        colon = i;
                    i++;
                }
                int end = i; // position of ';' or end

                if (colon < 0 || colon >= end) continue;

                var name = declText.Substring(start, colon - start).Trim().ToLowerInvariant();
                var valRaw = declText.Substring(colon + 1, end - colon - 1).Trim();

                bool important = false;
                var val = valRaw;
                var idx = valRaw.LastIndexOf("!important", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    important = true;
                    val = valRaw.Substring(0, idx).Trim();
                }

                list.Add(new CssDecl { Name = name, Value = val, Important = important });

                if (i < declText.Length && declText[i] == ';')
                    i++; // skip ';'
            }
            return list;
        }

        private static void AddToSelIndex(Dictionary<string, List<CssRule>> index, string key, CssRule rule)
        {
            List<CssRule> list;
            if (!index.TryGetValue(key, out list))
            {
                list = new List<CssRule>();
                index[key] = list;
            }
            list.Add(rule);
        }

        private static void TryAddFromIndex(Dictionary<string, List<CssRule>> index, HashSet<CssRule> set, List<CssRule> list, string key)
        {
            if (key == null) return;
            List<CssRule> candidates;
            if (index.TryGetValue(key, out candidates))
            {
                for (int i = 0; i < candidates.Count; i++)
                    if (set.Add(candidates[i]))
                        list.Add(candidates[i]);
            }
        }

        // ===========================
        // Stage 3: Cascade
        // ===========================

        /// <summary>
        /// Cascade styles for a single node and its subtree (incremental re-style).
        /// Uses cached rules from the last ComputeAsync call.
        /// </summary>
        public static Dictionary<LiteElement, CssComputed> CascadeSingle(
            LiteElement root,
            Dictionary<LiteElement, CssComputed> existingStyles)
        {
            var result = new Dictionary<LiteElement, CssComputed>();
            if (root == null) return result;

            List<CssRule> rules;
            lock (_cachedRules)
            {
                if (_cachedRules.Count == 0) return result;
                rules = _cachedRules;
            }

            // Build selector index (same as full cascade, but reused for subtree)
            var selIndex = new Dictionary<string, List<CssRule>>(StringComparer.OrdinalIgnoreCase);
            for (int ri = 0; ri < rules.Count; ri++)
            {
                var rule = rules[ri];
                if (rule.Selectors == null || rule.Selectors.Count == 0) continue;
                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int ci = 0; ci < rule.Selectors.Count; ci++)
                {
                    var chain = rule.Selectors[ci];
                    if (chain.Segments == null || chain.Segments.Count == 0) continue;
                    var lastSeg = chain.Segments[chain.Segments.Count - 1];

                    if (!string.IsNullOrEmpty(lastSeg.Tag))
                        if (seenKeys.Add(lastSeg.Tag))
                            AddToSelIndex(selIndex, lastSeg.Tag, rule);

                    if (!string.IsNullOrEmpty(lastSeg.Id))
                        if (seenKeys.Add("#" + lastSeg.Id))
                            AddToSelIndex(selIndex, "#" + lastSeg.Id, rule);

                    if (lastSeg.Classes != null)
                    {
                        for (int cl = 0; cl < lastSeg.Classes.Count; cl++)
                        {
                            var k = "." + lastSeg.Classes[cl];
                            if (seenKeys.Add(k))
                                AddToSelIndex(selIndex, k, rule);
                        }
                    }

                    if (string.IsNullOrEmpty(lastSeg.Tag) && string.IsNullOrEmpty(lastSeg.Id) &&
                        (lastSeg.Classes == null || lastSeg.Classes.Count == 0))
                    {
                        if (seenKeys.Add("*"))
                            AddToSelIndex(selIndex, "*", rule);
                    }
                }
            }

            // Walk subtree and cascade
            CascadeSubtreeWalk(root, rules, selIndex, existingStyles, result);
            return result;
        }

        private static void CascadeSubtreeWalk(
            LiteElement node,
            List<CssRule> rules,
            Dictionary<string, List<CssRule>> selIndex,
            Dictionary<LiteElement, CssComputed> existingStyles,
            Dictionary<LiteElement, CssComputed> result)
        {
            if (node == null || node.IsText)
            {
                if (node != null && !node.IsText)
                    CascadeSingleNode(node, rules, selIndex, existingStyles, result);
                return;
            }

            CascadeSingleNode(node, rules, selIndex, existingStyles, result);

            if (node.Children != null)
            {
                for (int i = 0; i < node.Children.Count; i++)
                    CascadeSubtreeWalk(node.Children[i], rules, selIndex, existingStyles, result);
            }
        }

        private static void CascadeSingleNode(
            LiteElement n,
            List<CssRule> rules,
            Dictionary<string, List<CssRule>> selIndex,
            Dictionary<LiteElement, CssComputed> existingStyles,
            Dictionary<LiteElement, CssComputed> result)
        {
            if (n.IsText) return;

            var candidates = new List<CssRule>();
            var seenRules = new HashSet<CssRule>();

            TryAddFromIndex(selIndex, seenRules, candidates, n.Tag);
            string nid;
            if (n.Attr != null && n.Attr.TryGetValue("id", out nid) && !string.IsNullOrEmpty(nid))
                TryAddFromIndex(selIndex, seenRules, candidates, "#" + nid);
            string ncls;
            if (n.Attr != null && n.Attr.TryGetValue("class", out ncls) && !string.IsNullOrEmpty(ncls))
            {
                var classParts = ncls.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int cp = 0; cp < classParts.Length; cp++)
                    TryAddFromIndex(selIndex, seenRules, candidates, "." + classParts[cp]);
            }
            TryAddFromIndex(selIndex, seenRules, candidates, "*");

            var perNode = new List<Tuple<CssDecl, SelectorChain, int>>();

            for (int ci = 0; ci < candidates.Count; ci++)
            {
                var rule = candidates[ci];
                for (int si = 0; si < rule.Selectors.Count; si++)
                {
                    var chain = rule.Selectors[si];
                    if (Matches(n, chain))
                    {
                        foreach (var kv in rule.Declarations)
                        {
                            var decl = kv.Value;
                            var d = new CssDecl
                            {
                                Name = decl.Name,
                                Value = ResolveUrlIfNeeded(decl.Value, rule.BaseUri),
                                Important = decl.Important,
                                Specificity = chain.Specificity
                            };
                            perNode.Add(Tuple.Create(d, chain, rule.SourceOrder));
                        }
                    }
                }
            }

            // Inline style
            string style;
            if (n.Attr != null && n.Attr.TryGetValue("style", out style) && !string.IsNullOrWhiteSpace(style))
            {
                var decls = ParseDeclarations(style);
                foreach (var d in decls)
                {
                    d.Specificity = 1000;
                    perNode.Add(Tuple.Create(d, (SelectorChain)null, int.MaxValue));
                }
            }

            if (perNode.Count == 0)
            {
                // Inherit from parent if no rules match
                CssComputed parentCss = null;
                if (n.Parent != null)
                    existingStyles.TryGetValue(n.Parent, out parentCss);
                if (parentCss == null && result.Count > 0)
                {
                    // Try result dict (parent may have been computed in this batch)
                    result.TryGetValue(n.Parent, out parentCss);
                }
                if (parentCss != null)
                {
                    var inherited = new CssComputed();
                    InheritFrom(parentCss, inherited);
                    result[n] = inherited;
                }
                return;
            }

            // Group by property and resolve cascade
            var byProp = new Dictionary<string, List<Tuple<CssDecl, SelectorChain, int>>>(StringComparer.OrdinalIgnoreCase);
            for (int ii = 0; ii < perNode.Count; ii++)
            {
                var t = perNode[ii];
                List<Tuple<CssDecl, SelectorChain, int>> grp;
                if (!byProp.TryGetValue(t.Item1.Name, out grp))
                {
                    grp = new List<Tuple<CssDecl, SelectorChain, int>>();
                    byProp[t.Item1.Name] = grp;
                }
                grp.Add(t);
            }

            var chosen = new Dictionary<string, CssDecl>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in byProp)
            {
                var grp = kv.Value;
                grp.Sort((a, b) =>
                {
                    int c = b.Item1.Important.CompareTo(a.Item1.Important);
                    if (c != 0) return c;
                    c = b.Item1.Specificity.CompareTo(a.Item1.Specificity);
                    if (c != 0) return c;
                    return b.Item3.CompareTo(a.Item3);
                });
                chosen[kv.Key] = grp[0].Item1;
            }

            // Parent context
            CssComputed parentCss2 = null;
            if (n.Parent != null)
                existingStyles.TryGetValue(n.Parent, out parentCss2);
            if (parentCss2 == null && result.Count > 0)
                result.TryGetValue(n.Parent, out parentCss2);

            var css = new CssComputed();
            if (parentCss2 != null && parentCss2.CustomProperties != null)
            {
                foreach (var kv in parentCss2.CustomProperties)
                {
                    css.CustomProperties[kv.Key] = kv.Value;
                    css.Map[kv.Key] = kv.Value;
                }
            }

            var rawCustom = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var d in chosen.Values)
            {
                if (IsCustomPropertyName(d.Name))
                    rawCustom[d.Name] = d.Value ?? string.Empty;
            }

            foreach (var key in rawCustom.Keys.ToList())
            {
                var resolvedCustom = ResolveCustomPropertyReferences(rawCustom[key], css, rawCustom, new HashSet<string>(StringComparer.Ordinal) { key });
                rawCustom[key] = resolvedCustom;
                css.CustomProperties[key] = resolvedCustom;
                css.Map[key] = resolvedCustom;
            }

            foreach (var d in chosen.Values)
            {
                if (IsCustomPropertyName(d.Name)) continue;
                var val = ResolveCustomPropertyReferences(d.Value, css, rawCustom, new HashSet<string>());
                css.Map[d.Name] = val;
            }

            // Resolve typed properties (same logic as full cascade, abbreviated for common props)
            double posVal;
            if (TryPx(DictGet(css.Map, "left"), out posVal)) css.Left = posVal;
            if (TryPx(DictGet(css.Map, "top"), out posVal)) css.Top = posVal;

            double sizeVal;
            if (TryPx(DictGet(css.Map, "width"), out sizeVal)) css.Width = sizeVal;
            else if (TryPercent(DictGet(css.Map, "width"), out sizeVal)) css.WidthPercent = sizeVal;
            if (TryPx(DictGet(css.Map, "height"), out sizeVal)) css.Height = sizeVal;
            else if (TryPercent(DictGet(css.Map, "height"), out sizeVal)) css.HeightPercent = sizeVal;

            var fgColor = TryColor(DictGet(css.Map, "color"));
            if (fgColor.HasValue) css.ForegroundColor = fgColor;
            else if (parentCss2 != null) css.ForegroundColor = parentCss2.ForegroundColor;

            var bgColor = TryColor(ExtractBackgroundColor(css.Map));
            if (bgColor.HasValue) css.BackgroundColor = bgColor;

            var ffRaw = DictGet(css.Map, "font-family");
            var resolved = SelectFontFamily(ffRaw);
            if (!string.IsNullOrEmpty(resolved)) css.FontFamilyName = resolved;
            else if (parentCss2 != null) css.FontFamilyName = parentCss2.FontFamilyName;

            double px;
            var fsRaw = DictGet(css.Map, "font-size");
            if (!string.IsNullOrEmpty(fsRaw))
            {
                if (fsRaw == "xx-small") css.FontSize = 9;
                else if (fsRaw == "x-small") css.FontSize = 10;
                else if (fsRaw == "small") css.FontSize = 13;
                else if (fsRaw == "medium") css.FontSize = 16;
                else if (fsRaw == "large") css.FontSize = 18;
                else if (fsRaw == "x-large") css.FontSize = 24;
                else if (fsRaw == "xx-large") css.FontSize = 32;
                else if (TryPx(fsRaw, out px)) css.FontSize = px;
            }
            if (!css.FontSize.HasValue && parentCss2 != null) css.FontSize = parentCss2.FontSize;
            else if (!css.FontSize.HasValue) css.FontSize = 16;

            var fwRaw = DictGet(css.Map, "font-weight");
            if (!string.IsNullOrEmpty(fwRaw))
            {
                if (fwRaw == "bold" || fwRaw == "700") css.FontWeight = FontWeights.Bold;
                else if (fwRaw == "normal" || fwRaw == "400") css.FontWeight = FontWeights.Normal;
                else { int w; if (int.TryParse(fwRaw, out w)) css.FontWeight = MakeFontWeight(w); }
            }
            else if (parentCss2 != null) css.FontWeight = parentCss2.FontWeight;

            var fsStyle = DictGet(css.Map, "font-style");
            if (!string.IsNullOrEmpty(fsStyle))
            {
                if (fsStyle == "italic") css.FontStyle = Windows.UI.Text.FontStyle.Italic;
                else if (fsStyle == "normal") css.FontStyle = Windows.UI.Text.FontStyle.Normal;
            }
            else if (parentCss2 != null) css.FontStyle = parentCss2.FontStyle;

            css.Display = Safe(DictGet(css.Map, "display"));
            if (string.IsNullOrEmpty(css.Display) && parentCss2 != null) css.Display = parentCss2.Display;

            css.Position = Safe(DictGet(css.Map, "position"));
            css.Float = Safe(DictGet(css.Map, "float"));
            css.Clear = Safe(DictGet(css.Map, "clear"));
            css.Visibility = Safe(DictGet(css.Map, "visibility"));
            if (string.IsNullOrEmpty(css.Visibility)) css.Visibility = "visible";

            css.Overflow = Safe(DictGet(css.Map, "overflow"));
            if (string.IsNullOrEmpty(css.Overflow)) css.Overflow = "visible";

            css.TextDecoration = Safe(DictGet(css.Map, "text-decoration"));
            css.WhiteSpace = Safe(DictGet(css.Map, "white-space"));
            css.ListStyleType = Safe(DictGet(css.Map, "list-style-type"));

            // text-align (enum conversion)
            var taRaw = Safe(DictGet(css.Map, "text-align"));
            if (!string.IsNullOrEmpty(taRaw))
            {
                var taLow = taRaw.ToLowerInvariant();
                if (taLow == "left") css.TextAlign = Windows.UI.Xaml.TextAlignment.Left;
                else if (taLow == "center") css.TextAlign = Windows.UI.Xaml.TextAlignment.Center;
                else if (taLow == "right") css.TextAlign = Windows.UI.Xaml.TextAlignment.Right;
                else if (taLow == "justify") css.TextAlign = Windows.UI.Xaml.TextAlignment.Justify;
            }

            Thickness th;
            if (TryThickness(DictGet(css.Map, "margin"), out th)) css.Margin = th;
            if (TryThickness(DictGet(css.Map, "padding"), out th)) css.Padding = th;

            double bVal;
            if (TryPx(DictGet(css.Map, "border-width"), out bVal)) css.BorderThickness = new Thickness(bVal);
            var bColor = TryColor(DictGet(css.Map, "border-color"));
            if (bColor.HasValue) css.BorderBrushColor = bColor;

            css.BorderRadius = TryCornerRadius(DictGet(css.Map, "border-radius"));

            double gRow, gCol;
            if (TryGapShorthand(DictGet(css.Map, "gap"), out gRow, out gCol))
            {
                css.Gap = gRow; css.RowGap = gRow; css.ColumnGap = gCol;
            }

            // CSS Grid
            css.GridTemplateColumns = Safe(DictGet(css.Map, "grid-template-columns"));
            css.GridTemplateRows = Safe(DictGet(css.Map, "grid-template-rows"));
            css.GridTemplateAreas = Safe(DictGet(css.Map, "grid-template-areas"));
            css.GridAutoColumns = Safe(DictGet(css.Map, "grid-auto-columns"));
            css.GridAutoRows = Safe(DictGet(css.Map, "grid-auto-rows"));
            css.GridAutoFlow = Safe(DictGet(css.Map, "grid-auto-flow"));
            css.GridColumn = Safe(DictGet(css.Map, "grid-column"));
            css.GridRow = Safe(DictGet(css.Map, "grid-row"));
            css.GridArea = Safe(DictGet(css.Map, "grid-area"));

            int zIdx;
            if (TryInt(DictGet(css.Map, "z-index"), out zIdx)) css.ZIndex = zIdx;

            var transform = DictGet(css.Map, "transform");
            if (!string.IsNullOrEmpty(transform)) css.Transform = transform;

            var transformOrigin = DictGet(css.Map, "transform-origin");
            if (!string.IsNullOrEmpty(transformOrigin)) css.TransformOrigin = transformOrigin;

            double opacity;
            if (TryDouble(DictGet(css.Map, "opacity"), out opacity)) css.Opacity = opacity;

            var boxShadow = DictGet(css.Map, "box-shadow");
            if (!string.IsNullOrEmpty(boxShadow)) css.BoxShadow = boxShadow;

            result[n] = css;
        }

        private static void InheritFrom(CssComputed parent, CssComputed child)
        {
            if (parent.ForegroundColor.HasValue) child.ForegroundColor = parent.ForegroundColor;
            if (!string.IsNullOrEmpty(parent.FontFamilyName)) child.FontFamilyName = parent.FontFamilyName;
            if (parent.FontSize.HasValue) child.FontSize = parent.FontSize;
            if (parent.FontWeight.HasValue) child.FontWeight = parent.FontWeight;
            if (parent.FontStyle.HasValue) child.FontStyle = parent.FontStyle;
            if (!string.IsNullOrEmpty(parent.Visibility)) child.Visibility = parent.Visibility;
            if (!string.IsNullOrEmpty(parent.ListStyleType)) child.ListStyleType = parent.ListStyleType;
            if (parent.CustomProperties != null)
            {
                foreach (var kv in parent.CustomProperties)
                {
                    child.CustomProperties[kv.Key] = kv.Value;
                    child.Map[kv.Key] = kv.Value;
                }
            }
        }

        private static Dictionary<LiteElement, CssComputed> CascadeIntoComputedStyles(LiteElement root, List<CssRule> rules, Action<string> log)
        {
            var result = new Dictionary<LiteElement, CssComputed>();
            if (root == null) return result;

            // Pre-flatten the DOM into a list to avoid repeated Descendants() enumerations
            var nodes = new List<LiteElement>();
            var stack = new Stack<LiteElement>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                nodes.Add(n);
                for (int i = n.Children.Count - 1; i >= 0; i--)
                    stack.Push(n.Children[i]);
            }

            // Pre-compute sibling/type indices for :nth-child etc
            for (int pi = 0; pi < nodes.Count; pi++)
            {
                var n = nodes[pi];
                if (n.Parent == null || n.Parent.Children == null || n.IsText) continue;
                var parent = n.Parent;
                if (n != parent.Children[0]) continue;
                int ci = 0;
                var tiMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int s = 0; s < parent.Children.Count; s++)
                {
                    var sib = parent.Children[s];
                    ci++;
                    sib._cachedChildIndex = ci;
                    if (!sib.IsText)
                    {
                        int ti;
                        tiMap.TryGetValue(sib.Tag, out ti);
                        ti++;
                        tiMap[sib.Tag] = ti;
                        sib._cachedTypeIndex = ti;
                    }
                }
            }

            // Build selector index keyed by rightmost segment properties
            var selIndex = new Dictionary<string, List<CssRule>>(StringComparer.OrdinalIgnoreCase);
            for (int ri = 0; ri < rules.Count; ri++)
            {
                var rule = rules[ri];
                if (rule.Selectors == null || rule.Selectors.Count == 0) continue;
                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int ci = 0; ci < rule.Selectors.Count; ci++)
                {
                    var chain = rule.Selectors[ci];
                    if (chain.Segments == null || chain.Segments.Count == 0) continue;
                    var lastSeg = chain.Segments[chain.Segments.Count - 1];
                    bool hasKey = false;

                    if (!string.IsNullOrEmpty(lastSeg.Tag))
                        if (seenKeys.Add(lastSeg.Tag))
                            AddToSelIndex(selIndex, lastSeg.Tag, rule);

                    if (!string.IsNullOrEmpty(lastSeg.Id))
                        if (seenKeys.Add("#" + lastSeg.Id))
                            AddToSelIndex(selIndex, "#" + lastSeg.Id, rule);

                    if (lastSeg.Classes != null)
                    {
                        for (int cl = 0; cl < lastSeg.Classes.Count; cl++)
                        {
                            var k = "." + lastSeg.Classes[cl];
                            if (seenKeys.Add(k))
                                AddToSelIndex(selIndex, k, rule);
                        }
                    }

                    if (!hasKey && string.IsNullOrEmpty(lastSeg.Tag) && string.IsNullOrEmpty(lastSeg.Id) &&
                        (lastSeg.Classes == null || lastSeg.Classes.Count == 0))
                    {
                        if (seenKeys.Add("*"))
                            AddToSelIndex(selIndex, "*", rule);
                    }
                }
            }

            // Match using selector index instead of iterating all rules per node
            var perNode = new Dictionary<LiteElement, List<Tuple<CssDecl, SelectorChain, int>>>();
            var seenRules = new HashSet<CssRule>();
            var candidates = new List<CssRule>();

            for (int ni = 0; ni < nodes.Count; ni++)
            {
                var n = nodes[ni];
                if (n.IsText) continue;

                // Gather candidate rules from selector index
                seenRules.Clear();
                candidates.Clear();
                TryAddFromIndex(selIndex, seenRules, candidates, n.Tag);
                string nid;
                if (n.Attr != null && n.Attr.TryGetValue("id", out nid) && !string.IsNullOrEmpty(nid))
                    TryAddFromIndex(selIndex, seenRules, candidates, "#" + nid);
                string ncls;
                if (n.Attr != null && n.Attr.TryGetValue("class", out ncls) && !string.IsNullOrEmpty(ncls))
                {
                    var classParts = ncls.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int cp = 0; cp < classParts.Length; cp++)
                        TryAddFromIndex(selIndex, seenRules, candidates, "." + classParts[cp]);
                }
                TryAddFromIndex(selIndex, seenRules, candidates, "*");

                // Match only candidate rules
                for (int ci = 0; ci < candidates.Count; ci++)
                {
                    var rule = candidates[ci];
                    for (int si = 0; si < rule.Selectors.Count; si++)
                    {
                        var chain = rule.Selectors[si];
                        if (Matches(n, chain))
                        {
                            foreach (var kv in rule.Declarations)
                            {
                                var decl = kv.Value;
                                var d = new CssDecl
                                {
                                    Name = decl.Name,
                                    Value = ResolveUrlIfNeeded(decl.Value, rule.BaseUri),
                                    Important = decl.Important,
                                    Specificity = chain.Specificity
                                };
                                List<Tuple<CssDecl, SelectorChain, int>> list;
                                if (!perNode.TryGetValue(n, out list))
                                {
                                    list = new List<Tuple<CssDecl, SelectorChain, int>>();
                                    perNode[n] = list;
                                }
                                list.Add(Tuple.Create(d, chain, rule.SourceOrder));
                            }
                        }
                    }
                }
            }

            // Inline style beats author rules; include as highest priority “rule”
            foreach (var n in nodes)
            {
                if (n.IsText) continue;
                string style;
                if (n.Attr != null && n.Attr.TryGetValue("style", out style) && !string.IsNullOrWhiteSpace(style))
                {
                    var decls = ParseDeclarations(style);
                    List<Tuple<CssDecl, SelectorChain, int>> list;
                    if (!perNode.TryGetValue(n, out list))
                    {
                        list = new List<Tuple<CssDecl, SelectorChain, int>>();
                        perNode[n] = list;
                    }
                    // inline specificity: max out (acts like 1000)
                    foreach (var d in decls)
                    {
                        d.Specificity = 1000;
                        list.Add(Tuple.Create(d, (SelectorChain)null, int.MaxValue));
                    }
                }
            }

            // Now resolve final property values with cascade ordering
            foreach (var n in nodes)
            {
                if (n == null || n.IsText) continue;

                List<Tuple<CssDecl, SelectorChain, int>> items;
                if (!perNode.TryGetValue(n, out items) || items == null || items.Count == 0)
                {
                    // No rules match — still inherit from parent so children can find us in result
                    CssComputed skipParent = null;
                    if (n.Parent != null)
                        result.TryGetValue(n.Parent, out skipParent);
                    if (skipParent != null)
                    {
                        var inherited = new CssComputed();
                        InheritFrom(skipParent, inherited);
                        result[n] = inherited;
                    }
                    continue;
                }

                // group by property name (manual to avoid LINQ overhead)
                var byProp = new Dictionary<string, List<Tuple<CssDecl, SelectorChain, int>>>(StringComparer.OrdinalIgnoreCase);
                for (int ii = 0; ii < items.Count; ii++)
                {
                    var t = items[ii];
                    List<Tuple<CssDecl, SelectorChain, int>> grp;
                    if (!byProp.TryGetValue(t.Item1.Name, out grp))
                    {
                        grp = new List<Tuple<CssDecl, SelectorChain, int>>();
                        byProp[t.Item1.Name] = grp;
                    }
                    grp.Add(t);
                }
                var chosen = new Dictionary<string, CssDecl>(StringComparer.OrdinalIgnoreCase);

                foreach (var kv in byProp)
                {
                    var grp = kv.Value;
                    // Manual cascade sort by important desc, specificity desc, sourceOrder desc
                    grp.Sort((a, b) =>
                    {
                        int c = b.Item1.Important.CompareTo(a.Item1.Important);
                        if (c != 0) return c;
                        c = b.Item1.Specificity.CompareTo(a.Item1.Specificity);
                        if (c != 0) return c;
                        return b.Item3.CompareTo(a.Item3);
                    });
                    chosen[kv.Key] = grp[0].Item1;
                }

                CssComputed parentCss = null;
                if (n.Parent != null)
                    result.TryGetValue(n.Parent, out parentCss);

                var css = new CssComputed();
                if (parentCss != null && parentCss.CustomProperties != null)
                {
                    foreach (var kv in parentCss.CustomProperties)
                    {
                        css.CustomProperties[kv.Key] = kv.Value;
                        css.Map[kv.Key] = kv.Value;
                    }
                }

                var rawCustom = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var d in chosen.Values)
                {
                    if (IsCustomPropertyName(d.Name))
                        rawCustom[d.Name] = d.Value ?? string.Empty;
                }

                foreach (var key in rawCustom.Keys.ToList())
                {
                    var resolvedCustom = ResolveCustomPropertyReferences(rawCustom[key], css, rawCustom, new HashSet<string>(StringComparer.Ordinal) { key });
                    rawCustom[key] = resolvedCustom;
                    css.CustomProperties[key] = resolvedCustom;
                    css.Map[key] = resolvedCustom;
                }

                foreach (var d in chosen.Values)
                {
                    if (IsCustomPropertyName(d.Name)) continue;
                    var val = ResolveCustomPropertyReferences(d.Value, css, rawCustom, new HashSet<string>());
                    css.Map[d.Name] = val;
                }

                // Phase C.6: parse the transition shorthand (e.g. "opacity 0.3s ease")
                // and stash the parsed parts on the computed style. Renderer uses
                // these to drive Storyboard animations on hover/tap state changes.
                ParseTransition(css);

                double posVal;
                if (TryPx(DictGet(css.Map, "left"), out posVal)) css.Left = posVal;
                if (TryPx(DictGet(css.Map, "top"), out posVal)) css.Top = posVal;
                if (TryPx(DictGet(css.Map, "right"), out posVal)) css.Right = posVal;
                if (TryPx(DictGet(css.Map, "bottom"), out posVal)) css.Bottom = posVal;

                // dimensions
                double sizeVal;
                if (TryPx(DictGet(css.Map, "width"), out sizeVal)) css.Width = sizeVal;
                else if (TryPercent(DictGet(css.Map, "width"), out sizeVal)) css.WidthPercent = sizeVal;

                if (TryPx(DictGet(css.Map, "height"), out sizeVal)) css.Height = sizeVal;
                else if (TryPercent(DictGet(css.Map, "height"), out sizeVal)) css.HeightPercent = sizeVal;
                if (TryPx(DictGet(css.Map, "min-width"), out sizeVal)) css.MinWidth = sizeVal;
                if (TryPx(DictGet(css.Map, "min-height"), out sizeVal)) css.MinHeight = sizeVal;
                if (TryPx(DictGet(css.Map, "max-width"), out sizeVal)) css.MaxWidth = sizeVal;
                if (TryPx(DictGet(css.Map, "max-height"), out sizeVal)) css.MaxHeight = sizeVal;

                // aspect-ratio
                var aspectRatioRaw = Safe(DictGet(css.Map, "aspect-ratio"));
                if (!string.IsNullOrEmpty(aspectRatioRaw) && !aspectRatioRaw.Contains("auto"))
                {
                    // Parse "16/9" or "1.777"
                    if (aspectRatioRaw.Contains("/"))
                    {
                        var parts = aspectRatioRaw.Split('/');
                        if (parts.Length == 2)
                        {
                            double w, h;
                            if (TryDouble(parts[0].Trim(), out w) && TryDouble(parts[1].Trim(), out h) && h > 0)
                                css.AspectRatio = w / h;
                        }
                    }
                    else
                    {
                        double ratio;
                        if (TryDouble(aspectRatioRaw, out ratio) && ratio > 0)
                            css.AspectRatio = ratio;
                    }
                }

                // gaps (gap shorthand + explicit row/column overrides)
                double gapRow, gapCol;
                if (TryGapShorthand(DictGet(css.Map, "gap"), out gapRow, out gapCol))
                {
                    css.Gap = gapRow;
                    css.RowGap = gapRow;
                    css.ColumnGap = gapCol;
                }
                double gapExplicit;
                if (TryPx(DictGet(css.Map, "row-gap"), out gapExplicit)) css.RowGap = gapExplicit;
                if (TryPx(DictGet(css.Map, "column-gap"), out gapExplicit)) css.ColumnGap = gapExplicit;
                if (!css.RowGap.HasValue && css.Gap.HasValue) css.RowGap = css.Gap;
                if (!css.ColumnGap.HasValue)
                {
                    if (css.Gap.HasValue) css.ColumnGap = css.Gap;
                    else if (css.RowGap.HasValue) css.ColumnGap = css.RowGap;
                }

                // color
                var fgColor = TryColor(DictGet(css.Map, "color"));
                if (fgColor.HasValue) css.ForegroundColor = fgColor;

                // background-color / background
                var bgColorRaw = ExtractBackgroundColor(css.Map);
                var bgColor = TryColor(bgColorRaw);
                if (bgColor.HasValue) css.BackgroundColor = bgColor;

                // font-family (first concrete family)
                try
                {
                    var ffRaw = DictGet(css.Map, "font-family");
                    var resolved = SelectFontFamily(ffRaw);
                    if (!string.IsNullOrEmpty(resolved))
                        css.FontFamilyName = resolved;
                }
                catch { }

                // font-size
                double px;
                if (TryPx(DictGet(css.Map, "font-size"), out px)) css.FontSize = px;

                // font-weight (keywords and numeric 100..900)
                var fwRaw = Safe(DictGet(css.Map, "font-weight"));
                if (!string.IsNullOrEmpty(fwRaw))
                {
                    var fw = fwRaw.Trim().ToLowerInvariant();
                    if (fw == "normal") css.FontWeight = MakeFontWeight(400);
                    else if (fw == "bold") css.FontWeight = MakeFontWeight(700);
                    else
                    {
                        int numeric;
                        if (int.TryParse(fw, NumberStyles.Integer, CultureInfo.InvariantCulture, out numeric))
                        {
                            css.FontWeight = MakeFontWeight(numeric);
                        }
                    }
                }

                // font-style
                var fsRaw = Safe(DictGet(css.Map, "font-style"));
                if (!string.IsNullOrEmpty(fsRaw))
                {
                    if (string.Equals(fsRaw, "italic", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(fsRaw, "oblique", StringComparison.OrdinalIgnoreCase))
                        css.FontStyle = Windows.UI.Text.FontStyle.Italic;
                    else if (string.Equals(fsRaw, "normal", StringComparison.OrdinalIgnoreCase))
                        css.FontStyle = Windows.UI.Text.FontStyle.Normal;
                }

                // text-align
                var ta = Safe(DictGet(css.Map, "text-align"));
                if (ta == "center") css.TextAlign = Windows.UI.Xaml.TextAlignment.Center;
                else if (ta == "right") css.TextAlign = Windows.UI.Xaml.TextAlignment.Right;
                else if (ta == "justify") css.TextAlign = Windows.UI.Xaml.TextAlignment.Justify;

                // text-decoration
                css.TextDecoration = Safe(DictGet(css.Map, "text-decoration"));

                // white-space
                css.WhiteSpace = Safe(DictGet(css.Map, "white-space"));

                // text-overflow
                css.TextOverflow = Safe(DictGet(css.Map, "text-overflow"));

                // Visual effects - opacity, text-shadow, box-shadow
                double opacityVal;
                if (TryDouble(DictGet(css.Map, "opacity"), out opacityVal))
                    css.Opacity = Math.Max(0.0, Math.Min(1.0, opacityVal));
                
                css.TextShadow = Safe(DictGet(css.Map, "text-shadow"));
                css.BoxShadow = Safe(DictGet(css.Map, "box-shadow"));

                // transform
                css.Transform = Safe(DictGet(css.Map, "transform"));

                // Background image properties
                var bgImageRaw = Safe(DictGet(css.Map, "background-image"));
                if (!string.IsNullOrEmpty(bgImageRaw))
                {
                    var urlMatch = _urlFuncRx.Match(bgImageRaw);
                    if (urlMatch.Success)
                    {
                        var url = urlMatch.Groups["u"].Value.Trim();
                        // URL is already resolved by ResolveUrlIfNeeded at declaration level
                        if (!string.IsNullOrEmpty(url))
                            css.BackgroundImageUrl = url;
                    }
                }
                // Also check shorthand "background" for url()
                if (string.IsNullOrEmpty(css.BackgroundImageUrl))
                {
                    var bgShorthand = Safe(DictGet(css.Map, "background"));
                    if (!string.IsNullOrEmpty(bgShorthand))
                    {
                        var urlMatch2 = _urlFuncRx.Match(bgShorthand);
                        if (urlMatch2.Success)
                        {
                            var url = urlMatch2.Groups["u"].Value.Trim();
                            if (!string.IsNullOrEmpty(url))
                                css.BackgroundImageUrl = url;
                        }
                    }
                }

                css.BackgroundRepeat = Safe(DictGet(css.Map, "background-repeat"));
                css.BackgroundPosition = Safe(DictGet(css.Map, "background-position"));
                css.BackgroundSize = Safe(DictGet(css.Map, "background-size"));

                // object-fit
                var objectFitRaw = Safe(DictGet(css.Map, "object-fit"));
                if (!string.IsNullOrEmpty(objectFitRaw))
                {
                    var of = objectFitRaw.Trim().ToLowerInvariant();
                    if (of == "fill" || of == "contain" || of == "cover" || of == "none" || of == "scale-down")
                        css.ObjectFit = of;
                }

                // border-spacing (table)
                string bsRaw = Safe(DictGet(css.Map, "border-spacing"));
                if (!string.IsNullOrWhiteSpace(bsRaw))
                {
                    var parts = bsRaw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    double bsH;
                    if (parts.Length > 0 && TryPx(parts[0], out bsH))
                    {
                        css.BorderSpacing = Math.Max(0, bsH);
                        if (parts.Length >= 2)
                        {
                            double bsV;
                            if (TryPx(parts[1], out bsV))
                                css.BorderSpacingVertical = Math.Max(0, bsV);
                        }
                    }
                }

                // outline (shorthand + longhands)
                string outlineRaw = Safe(DictGet(css.Map, "outline"));
                if (!string.IsNullOrWhiteSpace(outlineRaw) && !string.Equals(outlineRaw.Trim(), "none", StringComparison.OrdinalIgnoreCase))
                {
                    var outlineParts = outlineRaw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var op in outlineParts)
                    {
                        string lower = op.Trim().ToLowerInvariant();
                        if (lower == "solid" || lower == "dotted" || lower == "dashed" || lower == "double" || lower == "groove" || lower == "ridge" || lower == "inset" || lower == "outset")
                            css.OutlineStyle = lower;
                        else if (lower == "thin") { if (!css.OutlineWidth.HasValue) css.OutlineWidth = 1; }
                        else if (lower == "medium") { if (!css.OutlineWidth.HasValue) css.OutlineWidth = 3; }
                        else if (lower == "thick") { if (!css.OutlineWidth.HasValue) css.OutlineWidth = 5; }
                        else
                        {
                            double ow;
                            if (TryPx(op, out ow))
                            {
                                if (!css.OutlineWidth.HasValue) css.OutlineWidth = Math.Max(0, ow);
                            }
                            else
                            {
                                var col = TryColor(op);
                                if (col.HasValue) css.OutlineColor = col.Value;
                            }
                        }
                    }
                }
                string outlineStyle = Safe(DictGet(css.Map, "outline-style"));
                if (!string.IsNullOrWhiteSpace(outlineStyle)) css.OutlineStyle = outlineStyle;
                string outlineWidthRaw = Safe(DictGet(css.Map, "outline-width"));
                if (!string.IsNullOrWhiteSpace(outlineWidthRaw))
                {
                    double ow;
                    if (TryPx(outlineWidthRaw, out ow)) css.OutlineWidth = Math.Max(0, ow);
                    else
                    {
                        var owLow = outlineWidthRaw.Trim().ToLowerInvariant();
                        if (owLow == "thin") css.OutlineWidth = 1;
                        else if (owLow == "medium") css.OutlineWidth = 3;
                        else if (owLow == "thick") css.OutlineWidth = 5;
                    }
                }
                string outlineColorRaw = Safe(DictGet(css.Map, "outline-color"));
                if (!string.IsNullOrWhiteSpace(outlineColorRaw))
                {
                    var col = TryColor(outlineColorRaw.Trim());
                    if (col.HasValue) css.OutlineColor = col.Value;
                }
                if (css.OutlineWidth.HasValue && css.OutlineColor == null) css.OutlineColor = Windows.UI.Colors.Black;
                if (string.IsNullOrEmpty(css.OutlineStyle)) css.OutlineStyle = "solid";

                // box-sizing
                var boxSizingRaw = Safe(DictGet(css.Map, "box-sizing"));
                if (!string.IsNullOrEmpty(boxSizingRaw))
                {
                    var bs = boxSizingRaw.Trim().ToLowerInvariant();
                    if (bs == "border-box" || bs == "padding-box" || bs == "content-box")
                        css.BoxSizing = bs;
                }

                // margins/padding/border (kept in Map for RendererStyles, but also set typed if your CssComputed supports them)
                Thickness th;
                if (TryThickness(DictGet(css.Map, "margin"), out th)) css.Margin = th;
                
                // Override individual margins
                double mVal;
                var m = css.Margin;
                if (TryPx(DictGet(css.Map, "margin-left"), out mVal)) m.Left = mVal;
                if (TryPx(DictGet(css.Map, "margin-top"), out mVal)) m.Top = mVal;
                if (TryPx(DictGet(css.Map, "margin-right"), out mVal)) m.Right = mVal;
                if (TryPx(DictGet(css.Map, "margin-bottom"), out mVal)) m.Bottom = mVal;
                css.Margin = m;

                if (TryThickness(DictGet(css.Map, "padding"), out th)) css.Padding = th;

                // Override individual paddings
                var p = css.Padding;
                if (TryPx(DictGet(css.Map, "padding-left"), out mVal)) p.Left = mVal;
                if (TryPx(DictGet(css.Map, "padding-top"), out mVal)) p.Top = mVal;
                if (TryPx(DictGet(css.Map, "padding-right"), out mVal)) p.Right = mVal;
                if (TryPx(DictGet(css.Map, "padding-bottom"), out mVal)) p.Bottom = mVal;
                css.Padding = p;

                // border shorthand
                var borderColor = TryColor(ExtractBorderColor(css.Map));
                if (borderColor.HasValue) css.BorderBrushColor = borderColor;
                if (TryThickness(ExtractBorderThickness(css.Map), out th)) css.BorderThickness = th;
                
                // border-style from shorthand or longhands
                var bsFromShorthand = ExtractBorderStyle(css.Map);
                if (!string.IsNullOrEmpty(bsFromShorthand))
                    css.BorderStyle = bsFromShorthand.Trim().ToLowerInvariant();
                
                CornerRadius cr;
                if (TryCornerRadius(DictGet(css.Map, "border-radius"), out cr)) css.BorderRadius = cr;

                // Flexbox
                // Display and position
                css.Display = Safe(DictGet(css.Map, "display"));
                css.Position = Safe(DictGet(css.Map, "position"));
                css.Overflow = Safe(DictGet(css.Map, "overflow"));
                css.Visibility = Safe(DictGet(css.Map, "visibility"));
                css.Float = Safe(DictGet(css.Map, "float"));
                css.Clear = Safe(DictGet(css.Map, "clear"));
                
                // z-index
                var zIndexRaw = Safe(DictGet(css.Map, "z-index"));
                if (!string.IsNullOrEmpty(zIndexRaw))
                {
                    int zi;
                    if (int.TryParse(zIndexRaw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out zi))
                        css.ZIndex = zi;
                }

                // overflow-x / overflow-y
                css.OverflowX = Safe(DictGet(css.Map, "overflow-x"));
                css.OverflowY = Safe(DictGet(css.Map, "overflow-y"));
                // If overflow-x/y not set, inherit from overflow shorthand
                if (string.IsNullOrEmpty(css.OverflowX) && !string.IsNullOrEmpty(css.Overflow))
                    css.OverflowX = css.Overflow;
                if (string.IsNullOrEmpty(css.OverflowY) && !string.IsNullOrEmpty(css.Overflow))
                    css.OverflowY = css.Overflow;
                
                css.FlexDirection = Safe(DictGet(css.Map, "flex-direction"));
                css.FlexWrap = Safe(DictGet(css.Map, "flex-wrap"));
                css.JustifyContent = Safe(DictGet(css.Map, "justify-content"));
                css.AlignItems = Safe(DictGet(css.Map, "align-items"));
                css.AlignContent = Safe(DictGet(css.Map, "align-content"));
                css.ListStyleType = Safe(DictGet(css.Map, "list-style-type"));
                css.ListStylePosition = Safe(DictGet(css.Map, "list-style-position"));
                
                // list-style-image with URL resolution
                var listStyleImageRaw = Safe(DictGet(css.Map, "list-style-image"));
                if (!string.IsNullOrEmpty(listStyleImageRaw))
                {
                    var urlMatch = _urlFuncRx.Match(listStyleImageRaw);
                    if (urlMatch.Success)
                    {
                        var url = urlMatch.Groups["u"].Value.Trim();
                        if (!string.IsNullOrEmpty(url))
                            css.ListStyleImage = url;
                    }
                    else if (!listStyleImageRaw.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        css.ListStyleImage = listStyleImageRaw;
                    }
                }

                // letter-spacing
                double letterSpacing;
                if (TryPx(DictGet(css.Map, "letter-spacing"), out letterSpacing))
                    css.LetterSpacing = letterSpacing;

                // word-spacing
                double wordSpacing;
                if (TryPx(DictGet(css.Map, "word-spacing"), out wordSpacing))
                    css.WordSpacing = wordSpacing;

                // line-height (px or "normal")
                var lineHeightRaw = Safe(DictGet(css.Map, "line-height"));
                if (!string.IsNullOrEmpty(lineHeightRaw))
                {
                    if (lineHeightRaw.Trim().ToLowerInvariant() == "normal")
                    {
                        // "normal" is handled by XAML default
                    }
                    else
                    {
                        double lh;
                        if (TryPx(lineHeightRaw, out lh) && lh > 0)
                            css.LineHeight = lh;
                        else
                        {
                            // Unitless multiplier (e.g., line-height: 1.5)
                            double multiplier;
                            if (TryDouble(lineHeightRaw, out multiplier) && multiplier > 0)
                            {
                                // Will be resolved relative to font-size in renderer
                                css.LineHeight = multiplier * (css.FontSize ?? 16.0);
                            }
                        }
                    }
                }

                // text-transform
                css.TextTransform = Safe(DictGet(css.Map, "text-transform"));

                // text-indent
                double textIndent;
                if (TryPx(DictGet(css.Map, "text-indent"), out textIndent))
                    css.TextIndent = textIndent;

                // vertical-align
                css.VerticalAlign = Safe(DictGet(css.Map, "vertical-align"));

                // pointer-events
                css.PointerEvents = Safe(DictGet(css.Map, "pointer-events"));

                // cursor
                css.Cursor = Safe(DictGet(css.Map, "cursor"));

                // transform-origin
                css.TransformOrigin = Safe(DictGet(css.Map, "transform-origin"));

                // border-style (from shorthand or longhand)
                var borderStyleRaw = Safe(DictGet(css.Map, "border-style"));
                if (!string.IsNullOrEmpty(borderStyleRaw))
                {
                    var bs = borderStyleRaw.Trim().ToLowerInvariant();
                    // Take first keyword if multiple (border-style: solid dashed dotted double)
                    var bsParts = bs.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (bsParts.Length > 0)
                        css.BorderStyle = bsParts[0];
                }
                // Longhands override
                var borderTopStyle = Safe(DictGet(css.Map, "border-top-style"));
                if (!string.IsNullOrEmpty(borderTopStyle)) css.BorderStyle = borderTopStyle.Trim().ToLowerInvariant();
                else
                {
                    var borderRightStyle = Safe(DictGet(css.Map, "border-right-style"));
                    if (!string.IsNullOrEmpty(borderRightStyle)) css.BorderStyle = borderRightStyle.Trim().ToLowerInvariant();
                    else
                    {
                        var borderBottomStyle = Safe(DictGet(css.Map, "border-bottom-style"));
                        if (!string.IsNullOrEmpty(borderBottomStyle)) css.BorderStyle = borderBottomStyle.Trim().ToLowerInvariant();
                        else
                        {
                            var borderLeftStyle = Safe(DictGet(css.Map, "border-left-style"));
                            if (!string.IsNullOrEmpty(borderLeftStyle)) css.BorderStyle = borderLeftStyle.Trim().ToLowerInvariant();
                        }
                    }
                }

                double fG, fS, fB;
                if (TryFlexShorthand(DictGet(css.Map, "flex"), out fG, out fS, out fB))
                {
                    css.FlexGrow = fG;
                    css.FlexShrink = fS;
                    css.FlexBasis = fB;
                }

                double flexVal;
                if (TryDouble(DictGet(css.Map, "flex-grow"), out flexVal)) css.FlexGrow = flexVal;
                if (TryDouble(DictGet(css.Map, "flex-shrink"), out flexVal)) css.FlexShrink = flexVal;
                if (TryPx(DictGet(css.Map, "flex-basis"), out flexVal)) css.FlexBasis = flexVal;

                result[n] = css;
            }

            // Phase C.6: build :hover overrides (only for elements with hover rules
            // in the stylesheet). Skipped for elements without transitions since
            // there's no animation to play and no observable behavior change.
            ComputeHoverOverrides(root, rules, result);

            return result;
        }

        // ===========================
        // Phase C.6: CSS Transitions
        // ===========================

        // Parse the "transition" shorthand into typed fields on CssComputed.
        // Supports the common forms:
        //   "<property> <duration> [timing-function] [delay]"
        //   "all 0.3s"
        //   "opacity 200ms ease-in 0.1s"
        // For comma-separated lists ("a 0.3s, b 0.5s") only the first entry
        // is consumed — the "good enough" approximation from Plan_04 §C.6.
        private static void ParseTransition(CssComputed css)
        {
            if (css == null || css.Map == null) return;
            var raw = DictGet(css.Map, "transition");
            if (string.IsNullOrWhiteSpace(raw)) return;
            css.Transition = raw.Trim();

            // Take only the first comma-separated entry
            var first = raw;
            int comma = first.IndexOf(',');
            if (comma >= 0) first = first.Substring(0, comma);
            first = first.Trim();
            if (string.IsNullOrEmpty(first)) return;

            var tokens = first.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return;

            string property = null;
            string timing = null;

            foreach (var tk in tokens)
            {
                if (property == null && !IsTimingFunctionToken(tk) && !IsDurationToken(tk, out _))
                {
                    property = tk.ToLowerInvariant();
                    continue;
                }
                double d;
                if (IsDurationToken(tk, out d))
                {
                    // First duration → duration, second → delay
                    if (css.TransitionDurationMs == 0)
                        css.TransitionDurationMs = d;
                    else
                        css.TransitionDelayMs = d;
                    continue;
                }
                if (IsTimingFunctionToken(tk))
                {
                    timing = tk.ToLowerInvariant();
                    continue;
                }
            }

            if (property != null) css.TransitionProperty = property;
            if (timing != null) css.TransitionTimingFunction = timing;
        }

        private static bool IsDurationToken(string s, out double ms)
        {
            ms = 0;
            if (string.IsNullOrEmpty(s)) return false;
            // "ms" → milliseconds
            if (s.Length > 2 && s.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
            {
                double v;
                if (double.TryParse(s.Substring(0, s.Length - 2),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v))
                { ms = v; return true; }
            }
            // "s" → seconds (but watch for "ms" above, handled first)
            if (s.Length > 1 && s[s.Length - 1] == 's' && (s.Length < 3 || s[s.Length - 2] != 'm'))
            {
                double v;
                if (double.TryParse(s.Substring(0, s.Length - 1),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v))
                { ms = v * 1000.0; return true; }
            }
            return false;
        }

        private static bool IsTimingFunctionToken(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            var t = s.ToLowerInvariant();
            return t == "ease" || t == "linear" || t == "ease-in" || t == "ease-out"
                || t == "ease-in-out" || t == "step-start" || t == "step-end";
        }

        // Walk all rules; for each :hover rule, strip the :hover pseudo and
        // try to match the (now plain) selector against every element. If it
        // matches, build a Hover CssComputed by re-cascading just the hover
        // declarations on top of the element's base CssComputed.
        //
        // Only sets css.Hover when a transition is configured — there's no
        // observable behavior change without an animation.
        private static void ComputeHoverOverrides(LiteElement root, List<CssRule> rules, Dictionary<LiteElement, CssComputed> result)
        {
            if (root == null || rules == null || rules.Count == 0 || result == null || result.Count == 0)
                return;

            // Flatten DOM for matching
            var nodes = new List<LiteElement>();
            var stack = new Stack<LiteElement>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                nodes.Add(n);
                for (int i = n.Children.Count - 1; i >= 0; i--)
                    stack.Push(n.Children[i]);
            }

                // Collect rules that have at least one :hover selector variant
                // (we treat :focus and :active as hover for the museum browser —
                // exact focus styling would need keyboard-input plumbing).
                var hoverRules = new List<Tuple<CssRule, SelectorChain>>();
                for (int ri = 0; ri < rules.Count; ri++)
                {
                    var rule = rules[ri];
                    if (rule.Selectors == null) continue;
                    for (int si = 0; si < rule.Selectors.Count; si++)
                    {
                        var chain = rule.Selectors[si];
                        if (ChainHasInteractivePseudo(chain))
                            hoverRules.Add(Tuple.Create(rule, chain));
                    }
                }
                if (hoverRules.Count == 0) return;

                // For each element, collect matching hover declarations and
                // build a hover override CssComputed.
                for (int ni = 0; ni < nodes.Count; ni++)
                {
                    var n = nodes[ni];
                    if (n == null || n.IsText) continue;
                    CssComputed baseCss;
                    if (!result.TryGetValue(n, out baseCss) || baseCss == null) continue;

                    // Only build hover override if a transition is configured.
                    // (Hover without transition = no observable change.)
                    if (baseCss.TransitionDurationMs <= 0) continue;

                    var hoverByProp = new Dictionary<string, List<Tuple<CssDecl, Uri, int>>>(StringComparer.OrdinalIgnoreCase);
                    int matchedCount = 0;
                    for (int hi = 0; hi < hoverRules.Count; hi++)
                    {
                        var rule = hoverRules[hi].Item1;
                        var chain = hoverRules[hi].Item2;
                        // Re-match the chain with interactive pseudos stripped.
                        // The match would have failed in the base cascade because
                        // MatchesSingle returns false for :hover/:focus/:active.
                        if (MatchesIgnoringInteractivePseudos(n, chain))
                        {
                            matchedCount++;
                            foreach (var kv in rule.Declarations)
                            {
                                var decl = kv.Value;
                                // Skip "transition" itself — it's a base concept
                                if (string.Equals(decl.Name, "transition", StringComparison.OrdinalIgnoreCase))
                                    continue;
                                List<Tuple<CssDecl, Uri, int>> grp;
                                if (!hoverByProp.TryGetValue(decl.Name, out grp))
                                {
                                    grp = new List<Tuple<CssDecl, Uri, int>>();
                                    hoverByProp[decl.Name] = grp;
                                }
                                grp.Add(Tuple.Create(decl, rule.BaseUri, rule.SourceOrder));
                            }
                        }
                    }
                    if (matchedCount == 0) continue;

                    // Pick winning hover decl per property (cascade sort:
                    // sourceOrder desc, specificity desc — but hover rules rarely
                    // collide, so sourceOrder alone is fine for v1).
                    var hoverMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in hoverByProp)
                    {
                        var grp = kv.Value;
                        grp.Sort((a, b) =>
                        {
                            int c = b.Item1.Important.CompareTo(a.Item1.Important);
                            if (c != 0) return c;
                            c = b.Item1.Specificity.CompareTo(a.Item1.Specificity);
                            if (c != 0) return c;
                            return b.Item3.CompareTo(a.Item3);
                        });
                        hoverMap[kv.Key] = ResolveUrlIfNeeded(grp[0].Item1.Value, grp[0].Item2);
                    }

                    // Build a CssComputed copy that shadows only the overridden
                    // properties. We keep it lightweight: only the property values
                    // the renderer needs to swap (Opacity, BackgroundColor,
                    // transform-related). For everything else we fall back to base.
                    var hover = new CssComputed
                    {
                        Transition = baseCss.Transition,
                        TransitionDurationMs = baseCss.TransitionDurationMs,
                        TransitionProperty = baseCss.TransitionProperty,
                        TransitionTimingFunction = baseCss.TransitionTimingFunction,
                        TransitionDelayMs = baseCss.TransitionDelayMs,
                    };
                    // Seed the hover Map with base values, then override.
                    if (baseCss.Map != null)
                    {
                        foreach (var bk in baseCss.Map)
                            hover.Map[bk.Key] = bk.Value;
                    }
                    foreach (var kv in hoverMap)
                        hover.Map[kv.Key] = kv.Value;

                    baseCss.Hover = hover;
                }
            }

        // Does the chain have any of :hover / :focus / :active?
        private static bool ChainHasInteractivePseudo(SelectorChain chain)
        {
            if (chain == null || chain.Segments == null) return false;
            for (int i = 0; i < chain.Segments.Count; i++)
            {
                var seg = chain.Segments[i];
                if (seg.PseudoClasses == null) continue;
                for (int j = 0; j < seg.PseudoClasses.Count; j++)
                {
                    var p = seg.PseudoClasses[j];
                    if (string.Equals(p, "hover", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(p, "focus", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(p, "active", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        // Like MatchesSingle, but treats :hover/:focus/:active as non-existent
        // (i.e. ignores them). Lets us evaluate a .box:hover chain against an
        // element as if it were just .box.
        private static bool MatchesIgnoringInteractivePseudos(LiteElement n, SelectorChain chain)
        {
            if (chain == null || chain.Segments == null || chain.Segments.Count == 0) return false;

            // Walk the chain right-to-left, tracking the current node
            LiteElement cur = n;
            // Rightmost segment must match `n` itself
            var right = chain.Segments[chain.Segments.Count - 1];
            if (!SegmentMatchesNoInteractive(cur, right)) return false;

            // Walk left through the rest
            for (int i = chain.Segments.Count - 2; i >= 0; i--)
            {
                var seg = chain.Segments[i];
                var nextCombinator = chain.Segments[i + 1].Next;
                if (nextCombinator == Combinator.Child)
                {
                    if (cur.Parent == null) return false;
                    if (!SegmentMatchesNoInteractive(cur.Parent, seg)) return false;
                    cur = cur.Parent;
                }
                else // Descendant
                {
                    bool found = false;
                    var p = cur.Parent;
                    while (p != null)
                    {
                        if (SegmentMatchesNoInteractive(p, seg)) { found = true; cur = p; break; }
                        p = p.Parent;
                    }
                    if (!found) return false;
                }
            }
            return true;
        }

        // Mirror of MatchesSingle's per-segment check, with interactive
        // pseudo-classes treated as "no constraint" (skipped). All other
        // checks (tag, id, class, attribute, structural pseudos) are reused.
        private static bool SegmentMatchesNoInteractive(LiteElement n, SelectorSegment seg)
        {
            if (n == null || seg == null) return false;
            if (!string.IsNullOrEmpty(seg.Tag)
                && !string.Equals(n.Tag, seg.Tag, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(seg.Id))
            {
                string nid = null; n.Attr?.TryGetValue("id", out nid);
                if (!string.Equals(nid, seg.Id, StringComparison.OrdinalIgnoreCase)) return false;
            }
            if (seg.Classes != null && seg.Classes.Count > 0)
            {
                string ncls = null; n.Attr?.TryGetValue("class", out ncls);
                if (string.IsNullOrEmpty(ncls)) return false;
                var haveParts = ncls.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < seg.Classes.Count; i++)
                {
                    bool ok = false;
                    for (int j = 0; j < haveParts.Length; j++)
                        if (string.Equals(haveParts[j], seg.Classes[i], StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
                    if (!ok) return false;
                }
            }
            if (seg.PseudoClasses != null)
            {
                for (int pi = 0; pi < seg.PseudoClasses.Count; pi++)
                {
                    var ps = seg.PseudoClasses[pi];
                    if (string.Equals(ps, "hover", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(ps, "focus", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(ps, "active", StringComparison.OrdinalIgnoreCase))
                    {
                        // Skip — treat as no constraint
                        continue;
                    }
                    // For other pseudos, defer to the same logic as MatchesSingle
                    // by reusing the public Matches() entry (slightly wasteful
                    // but correct; v2 can refactor).
                    if (!SegmentMatchesIncludingAllPseudos(n, seg, ps)) return false;
                }
            }
            if (seg.Attributes != null)
            {
                for (int ai = 0; ai < seg.Attributes.Count; ai++)
                {
                    var attr = seg.Attributes[ai];
                    string v = null; n.Attr?.TryGetValue(attr.Item1, out v);
                    if (v == null) return false;
                    if (!string.Equals(v ?? "", attr.Item3, StringComparison.OrdinalIgnoreCase)) return false;
                }
            }
            return true;
        }

        // Re-dispatches a single non-interactive pseudo check through
        // MatchesSingle's logic by constructing a synthetic segment with the
        // single pseudo. Kept tiny so we don't duplicate all the nth-* code.
        private static bool SegmentMatchesIncludingAllPseudos(LiteElement n, SelectorSegment original, string pseudo)
        {
            var probe = new SelectorSegment
            {
                Tag = original.Tag,
                Id = original.Id,
                Classes = original.Classes,
                PseudoClasses = new List<string> { pseudo },
                Attributes = original.Attributes,
            };
            var chain = new SelectorChain();
            chain.Segments.Add(probe);
            return Matches(n, chain);
        }

        private static FontWeight MakeFontWeight(int openTypeWeight)
        {
            if (openTypeWeight < 1) openTypeWeight = 1;
            if (openTypeWeight > 999) openTypeWeight = 999;
            return new FontWeight { Weight = (ushort)openTypeWeight };
        }

        // ===========================
        // Matching
        // ===========================

        private static bool Matches(LiteElement n, SelectorChain chain)
        {
            if (n == null || chain == null || chain.Segments.Count == 0) return false;

            // We match from the last segment back to the first, walking up the DOM for ancestor/parent checks.
            int segIndex = chain.Segments.Count - 1;
            LiteElement cur = n;

            // Match the right-most segment first
            if (!MatchesSingle(cur, chain.Segments[segIndex])) return false;

            // Walk up the chain
            while (segIndex > 0)
            {
                // The combinator connecting (segIndex-1) -> (segIndex) is stored on (segIndex-1)
                var prevSeg = chain.Segments[segIndex - 1];
                var comb = prevSeg.Next;

                segIndex--; // Move to the previous segment (the one we want to find now)

                if (cur == null) return false;

                if (comb == Combinator.Child)
                {
                    cur = cur.Parent;
                    if (!MatchesSingle(cur, prevSeg)) return false;
                }
                else // Descendant
                {
                    // Find an ancestor that matches prevSeg
                    cur = FindAncestorMatching(cur.Parent, prevSeg);
                    if (cur == null) return false;
                }
            }

            return true;
        }

        private static LiteElement FindAncestorMatching(LiteElement start, SelectorSegment seg)
        {
            var cur = start;
            while (cur != null)
            {
                if (MatchesSingle(cur, seg)) return cur;
                cur = cur.Parent;
            }
            return null;
        }

        /// <summary>
        /// Parse an+b notation for :nth-child and :nth-of-type.
        /// Supports: "odd", "even", "3", "2n", "2n+1", "2n-1", "-n+3", etc.
        /// </summary>
        private static bool ParseNthExpression(string expr, out int a, out int b)
        {
            a = 0; b = 0;
            if (string.IsNullOrWhiteSpace(expr)) return false;
            
            var s = expr.Replace(" ", "").ToLowerInvariant();
            
            // Handle keywords
            if (s == "odd") { a = 2; b = 1; return true; }
            if (s == "even") { a = 2; b = 0; return true; }
            
            // Find 'n'
            int posN = s.IndexOf('n');
            if (posN < 0)
            {
                // Just a number (e.g., "3")
                int num;
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out num))
                {
                    a = 0;
                    b = num;
                    return true;
                }
                return false;
            }
            
            // Parse 'an' part
            var aPart = s.Substring(0, posN);
            if (aPart == "" || aPart == "+") a = 1;
            else if (aPart == "-") a = -1;
            else if (!int.TryParse(aPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out a))
                return false;
            
            // Parse '+b' or '-b' part
            var bPart = s.Substring(posN + 1);
            if (string.IsNullOrEmpty(bPart))
            {
                b = 0;
                return true;
            }
            
            if (!int.TryParse(bPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out b))
                return false;
            
            return true;
        }

        /// <summary>
        /// Check if a 1-based index matches the an+b pattern.
        /// </summary>
        private static bool MatchesNth(int index1Based, int a, int b)
        {
            if (a == 0)
                return index1Based == b;
            
            var diff = index1Based - b;
            if (a > 0)
                return diff >= 0 && diff % a == 0;
            else
                return diff <= 0 && diff % a == 0;
        }

        /// <summary>
        /// Get the 1-based child index of element n within its parent's children.
        /// </summary>
        private static int GetChildIndex(LiteElement n)
        {
            if (n == null || n.Parent == null || n.Parent.Children == null)
                return 0;
            if (n._cachedChildIndex >= 0)
                return n._cachedChildIndex;
            for (int i = 0; i < n.Parent.Children.Count; i++)
            {
                if (n.Parent.Children[i] == n)
                    return i + 1; // 1-based
            }
            return 0;
        }

        /// <summary>
        /// Get the 1-based index of element n among siblings of the same tag type.
        /// </summary>
        private static int GetTypeIndex(LiteElement n)
        {
            if (n == null || n.Parent == null || n.Parent.Children == null || string.IsNullOrEmpty(n.Tag))
                return 0;
            if (n._cachedTypeIndex >= 0)
                return n._cachedTypeIndex;
            int index = 0;
            foreach (var child in n.Parent.Children)
            {
                if (child.IsText) continue;
                if (string.Equals(child.Tag, n.Tag, StringComparison.OrdinalIgnoreCase))
                {
                    index++;
                    if (child == n) return index; // 1-based
                }
            }
            return 0;
        }

        /// <summary>
        /// Get the 1-based index from the end (last child = 1) within parent's children.
        /// </summary>
        private static int GetLastChildIndex(LiteElement n)
        {
            if (n == null || n.Parent == null || n.Parent.Children == null)
                return 0;
            
            int count = n.Parent.Children.Count;
            for (int i = 0; i < count; i++)
            {
                if (n.Parent.Children[i] == n)
                    return count - i; // Distance from end (1-based)
            }
            return 0;
        }

        /// <summary>
        /// Get the 1-based index from the end among siblings of the same tag type.
        /// </summary>
        private static int GetLastTypeIndex(LiteElement n)
        {
            if (n == null || n.Parent == null || n.Parent.Children == null || string.IsNullOrEmpty(n.Tag))
                return 0;
            
            var sameTypeElements = new System.Collections.Generic.List<LiteElement>();
            foreach (var child in n.Parent.Children)
            {
                if (!child.IsText && string.Equals(child.Tag, n.Tag, StringComparison.OrdinalIgnoreCase))
                    sameTypeElements.Add(child);
            }
            
            for (int i = 0; i < sameTypeElements.Count; i++)
            {
                if (sameTypeElements[i] == n)
                    return sameTypeElements.Count - i; // Distance from end (1-based)
            }
            return 0;
        }

        /// <summary>
        /// Extract the argument from a pseudo-class like ":nth-child(2n+1)" -> "2n+1"
        /// </summary>
        private static string ExtractPseudoArg(string pseudoClass)
        {
            if (string.IsNullOrEmpty(pseudoClass)) return "";
            
            int start = pseudoClass.IndexOf('(');
            int end = pseudoClass.LastIndexOf(')');
            
            if (start >= 0 && end > start)
                return pseudoClass.Substring(start + 1, end - start - 1).Trim();
            
            return "";
        }

        private static bool MatchesSingle(LiteElement n, SelectorSegment seg)
        {
            if (n == null || seg == null) return false;
            if (n.IsText) return false;

            if (!string.IsNullOrEmpty(seg.Tag))
            {
                if (!string.Equals(n.Tag, seg.Tag, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (!string.IsNullOrEmpty(seg.Id))
            {
                string id;
                if (n.Attr == null || !n.Attr.TryGetValue("id", out id) || !string.Equals(id ?? "", seg.Id, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (seg.Classes != null && seg.Classes.Count > 0)
            {
                string cls;
                if (n.Attr == null || !n.Attr.TryGetValue("class", out cls) || string.IsNullOrWhiteSpace(cls))
                    return false;

                var have = SplitTokens(cls);
                foreach (var c in seg.Classes)
                    if (!have.Contains(c, StringComparer.OrdinalIgnoreCase)) return false;
            }

            if (seg.Attributes != null)
            {
                if (n.Attr == null) return false;
                foreach (var attr in seg.Attributes)
                {
                    string val;
                    if (!n.Attr.TryGetValue(attr.Item1, out val)) return false;
                    
                    if (attr.Item2 == "=")
                    {
                        if (!string.Equals(val ?? "", attr.Item3, StringComparison.OrdinalIgnoreCase)) return false;
                    }
                    // Add other operators (~=, |=, ^=, $=, *=) if needed
                }
            }

            if (seg.PseudoClasses != null)
            {
                foreach (var ps in seg.PseudoClasses)
                {
                    if (string.Equals(ps, "first-child", StringComparison.OrdinalIgnoreCase))
                    {
                        if (n.Parent == null || n.Parent.Children == null || n.Parent.Children.Count == 0 || n.Parent.Children[0] != n) return false;
                    }
                    else if (string.Equals(ps, "last-child", StringComparison.OrdinalIgnoreCase))
                    {
                        if (n.Parent == null || n.Parent.Children == null || n.Parent.Children.Count == 0 || n.Parent.Children[n.Parent.Children.Count - 1] != n) return false;
                    }
                    else if (string.Equals(ps, "root", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.Equals(n.Tag, "html", StringComparison.OrdinalIgnoreCase)) return false;
                    }
                    else if (string.Equals(ps, "only-child", StringComparison.OrdinalIgnoreCase))
                    {
                        if (n.Parent == null || n.Parent.Children == null || n.Parent.Children.Count != 1 || n.Parent.Children[0] != n) return false;
                    }
                    else if (ps.StartsWith("nth-child(", StringComparison.OrdinalIgnoreCase))
                    {
                        var arg = ExtractPseudoArg(ps);
                        int a, b;
                        if (ParseNthExpression(arg, out a, out b))
                        {
                            int index = GetChildIndex(n);
                            if (index == 0 || !MatchesNth(index, a, b)) return false;
                        }
                        else
                        {
                            return false; // Invalid nth-expression
                        }
                    }
                    else if (ps.StartsWith("nth-of-type(", StringComparison.OrdinalIgnoreCase))
                    {
                        var arg = ExtractPseudoArg(ps);
                        int a, b;
                        if (ParseNthExpression(arg, out a, out b))
                        {
                            int index = GetTypeIndex(n);
                            if (index == 0 || !MatchesNth(index, a, b)) return false;
                        }
                        else
                        {
                            return false; // Invalid nth-expression
                        }
                    }
                    else if (ps.StartsWith("nth-last-child(", StringComparison.OrdinalIgnoreCase))
                    {
                        var arg = ExtractPseudoArg(ps);
                        int a, b;
                        if (ParseNthExpression(arg, out a, out b))
                        {
                            int index = GetLastChildIndex(n);
                            if (index == 0 || !MatchesNth(index, a, b)) return false;
                        }
                        else
                        {
                            return false; // Invalid nth-expression
                        }
                    }
                    else if (ps.StartsWith("nth-last-of-type(", StringComparison.OrdinalIgnoreCase))
                    {
                        var arg = ExtractPseudoArg(ps);
                        int a, b;
                        if (ParseNthExpression(arg, out a, out b))
                        {
                            int index = GetLastTypeIndex(n);
                            if (index == 0 || !MatchesNth(index, a, b)) return false;
                        }
                        else
                        {
                            return false; // Invalid nth-expression
                        }
                    }
                    else if (string.Equals(ps, "first-of-type", StringComparison.OrdinalIgnoreCase))
                    {
                        int index = GetTypeIndex(n);
                        if (index != 1) return false;
                    }
                    else if (string.Equals(ps, "last-of-type", StringComparison.OrdinalIgnoreCase))
                    {
                        if (n == null || n.Parent == null || n.Parent.Children == null || string.IsNullOrEmpty(n.Tag)) return false;
                        
                        // Find the last element of this type among siblings
                        LiteElement lastOfType = null;
                        foreach (var child in n.Parent.Children)
                        {
                            if (!child.IsText && string.Equals(child.Tag, n.Tag, StringComparison.OrdinalIgnoreCase))
                                lastOfType = child;
                        }
                        if (lastOfType != n) return false;
                    }
                    else if (string.Equals(ps, "only-of-type", StringComparison.OrdinalIgnoreCase))
                    {
                        if (n == null || n.Parent == null || n.Parent.Children == null || string.IsNullOrEmpty(n.Tag)) return false;

                        int typeCount = 0;
                        foreach (var child in n.Parent.Children)
                        {
                            if (!child.IsText && string.Equals(child.Tag, n.Tag, StringComparison.OrdinalIgnoreCase))
                                typeCount++;
                        }
                        if (typeCount != 1) return false;
                    }
                    // Interactive pseudo-classes (Phase C.6, Session 3.26):
                    // never match the base cascade — handled separately in
                    // CssLoader.ComputeHoverOverrides() to build CssComputed.Hover.
                    else if (string.Equals(ps, "hover", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(ps, "focus", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(ps, "active", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // ===========================
        // Utility helpers
        // ===========================

        private static bool IsCustomPropertyName(string name)
        {
            return !string.IsNullOrEmpty(name) && name.StartsWith("--", StringComparison.Ordinal);
        }

        private static string ResolveCustomPropertyReferences(string value, CssComputed current, Dictionary<string, string> rawCurrent, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            if (value.IndexOf("var(", StringComparison.OrdinalIgnoreCase) < 0) return value;

            if (seen == null)
                seen = new HashSet<string>(StringComparer.Ordinal);

            var sb = new StringBuilder(value.Length);
            int idx = 0;
            while (idx < value.Length)
            {
                var pos = value.IndexOf("var(", idx, StringComparison.OrdinalIgnoreCase);
                if (pos < 0)
                {
                    sb.Append(value.Substring(idx));
                    break;
                }

                sb.Append(value.Substring(idx, pos - idx));
                int argsStart = pos + 4;
                int depth = 1;
                int i = argsStart;
                while (i < value.Length && depth > 0)
                {
                    char ch = value[i];
                    if (ch == '(') depth++;
                    else if (ch == ')') depth--;
                    if (depth == 0) break;
                    i++;
                }

                if (depth != 0)
                {
                    sb.Append(value.Substring(pos));
                    break;
                }

                var inner = value.Substring(argsStart, i - argsStart);
                var resolved = EvaluateVarExpression(inner, current, rawCurrent, seen);
                sb.Append(resolved);
                idx = i + 1;
            }

            return sb.ToString();
        }

        private static string EvaluateVarExpression(string rawArgs, CssComputed current, Dictionary<string, string> rawCurrent, HashSet<string> seen)
        {
            var trimmed = (rawArgs ?? string.Empty).Trim();
            if (trimmed.Length == 0) return string.Empty;

            int comma = FindTopLevelComma(trimmed);
            string name = comma >= 0 ? trimmed.Substring(0, comma).Trim() : trimmed;
            string fallback = comma >= 0 ? trimmed.Substring(comma + 1) : null;

            if (string.IsNullOrEmpty(name) || !name.StartsWith("--", StringComparison.Ordinal))
                return ResolveFallback(fallback, current, rawCurrent, seen);

            string resolved;
            string rawValue;
            if (rawCurrent != null && rawCurrent.TryGetValue(name, out rawValue))
            {
                if (seen == null) seen = new HashSet<string>(StringComparer.Ordinal);
                if (seen.Contains(name))
                    return ResolveFallback(fallback, current, rawCurrent, seen);

                seen.Add(name);
                resolved = ResolveCustomPropertyReferences(rawValue, current, rawCurrent, seen);
                seen.Remove(name);
                current.CustomProperties[name] = resolved;
                return resolved;
            }

            if (current != null && current.CustomProperties != null && current.CustomProperties.TryGetValue(name, out resolved))
            {
                return resolved;
            }

            return ResolveFallback(fallback, current, rawCurrent, seen);
        }

        private static string ResolveFallback(string fallback, CssComputed current, Dictionary<string, string> rawCurrent, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(fallback)) return string.Empty;
            return ResolveCustomPropertyReferences(fallback, current, rawCurrent, seen);
        }

        private static int FindTopLevelComma(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return -1;
            int depth = 0;
            bool inString = false;
            char stringChar = '\0';

            for (int i = 0; i < raw.Length; i++)
            {
                var ch = raw[i];
                if (inString)
                {
                    if (ch == stringChar) inString = false;
                    continue;
                }

                if (ch == '\"' || ch == '\'')
                {
                    inString = true; stringChar = ch; continue;
                }
                if (ch == '(') { depth++; continue; }
                if (ch == ')') { depth = Math.Max(0, depth - 1); continue; }
                if (ch == ',' && depth == 0) return i;
            }
            return -1;
        }

        private static List<string> SplitCssValues(string raw)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return result;

            var sb = new StringBuilder();
            int depth = 0;
            bool inString = false;
            char stringChar = '\0';

            foreach (var ch in raw)
            {
                if (inString)
                {
                    sb.Append(ch);
                    if (ch == stringChar) inString = false;
                    continue;
                }

                if (ch == '\"' || ch == '\'')
                {
                    inString = true; stringChar = ch; sb.Append(ch); continue;
                }

                if (ch == '(') { depth++; sb.Append(ch); continue; }
                if (ch == ')') { depth = Math.Max(0, depth - 1); sb.Append(ch); continue; }

                if (char.IsWhiteSpace(ch) && depth == 0)
                {
                    if (sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); }
                }
                else
                {
                    sb.Append(ch);
                }
            }

            if (sb.Length > 0) result.Add(sb.ToString());
            return result;
        }

        private static bool TryGapShorthand(string raw, out double row, out double column)
        {
            row = column = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var parts = SplitCssValues(raw);
            if (parts.Count == 0) return false;

            double first;
            if (!TryPx(parts[0], out first)) return false;
            row = first;
            column = first;

            if (parts.Count > 1)
            {
                double second;
                if (TryPx(parts[1], out second)) column = second;
            }

            return true;
        }

        private static bool TryFlexShorthand(string raw, out double grow, out double shrink, out double basis)
        {
            grow = 0; shrink = 1; basis = double.NaN; // Default initial values
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var parts = SplitCssValues(raw);
            if (parts.Count == 0) return false;

            // Handle keywords
            if (parts.Count == 1)
            {
                var p = parts[0].ToLowerInvariant();
                if (p == "none") { grow = 0; shrink = 0; basis = double.NaN; return true; }
                if (p == "auto") { grow = 1; shrink = 1; basis = double.NaN; return true; }
                if (p == "initial") { grow = 0; shrink = 1; basis = double.NaN; return true; }
            }

            // Helper to check if string is a length
            Func<string, bool> isLength = (s) => 
            {
                s = s.ToLowerInvariant();
                return s.EndsWith("px") || s.EndsWith("%") || s.EndsWith("em") || s.EndsWith("rem") || s == "auto" || s == "content";
            };

            // Parse parts
            if (parts.Count == 1)
            {
                // <number> (grow) OR <length> (basis)
                double val;
                if (isLength(parts[0]))
                {
                    if (TryPx(parts[0], out val) || parts[0].ToLowerInvariant() == "auto")
                    {
                        grow = 1; shrink = 1; basis = (parts[0].ToLowerInvariant() == "auto" ? double.NaN : val);
                    }
                }
                else if (TryDouble(parts[0], out val))
                {
                    grow = val; shrink = 1; basis = 0;
                }
            }
            else if (parts.Count == 2)
            {
                // first is grow
                double val1;
                if (TryDouble(parts[0], out val1)) grow = val1;

                // second: <number> (shrink) OR <length> (basis)
                double val2;
                if (isLength(parts[1]))
                {
                    shrink = 1;
                    if (TryPx(parts[1], out val2) || parts[1].ToLowerInvariant() == "auto")
                        basis = (parts[1].ToLowerInvariant() == "auto" ? double.NaN : val2);
                }
                else if (TryDouble(parts[1], out val2))
                {
                    shrink = val2; basis = 0;
                }
            }
            else if (parts.Count >= 3)
            {
                // grow shrink basis
                double v;
                if (TryDouble(parts[0], out v)) grow = v;
                if (TryDouble(parts[1], out v)) shrink = v;
                if (TryPx(parts[2], out v) || parts[2].ToLowerInvariant() == "auto")
                    basis = (parts[2].ToLowerInvariant() == "auto" ? double.NaN : v);
            }

            return true;
        }

        private static string StripComments(string css)
        {
            if (string.IsNullOrEmpty(css)) return css ?? "";
            var sb = new StringBuilder(css.Length);
            int i = 0;
            while (i < css.Length)
            {
                if (i + 1 < css.Length && css[i] == '/' && css[i + 1] == '*')
                {
                    i += 2; // skip /*
                    while (i + 1 < css.Length && !(css[i] == '*' && css[i + 1] == '/'))
                        i++;
                    i += 2; // skip */
                }
                else
                {
                    sb.Append(css[i]);
                    i++;
                }
            }
            return sb.ToString();
        }

        private static IEnumerable<string> SplitTokens(string s)
        {
            if (string.IsNullOrWhiteSpace(s) || s.Length > 65536) yield break;
            var parts = s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 8192) yield break;
            for (int i = 0; i < parts.Length; i++)
                yield return parts[i].Trim();
        }

        private static bool ContainsToken(string list, string token)
        {
            foreach (var t in SplitTokens(list))
                if (string.Equals(t, token, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string Safe(string s) { return string.IsNullOrWhiteSpace(s) ? null : s.Trim(); }

        private static bool TryCornerRadius(string raw, out CornerRadius radius)
        {
            radius = new CornerRadius(0);
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var main = raw.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? raw;
            var parts = main.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            double tl, tr, br, bl;
            switch (parts.Length)
            {
                case 1:
                    if (TryCornerComponent(parts[0], out tl))
                    {
                        radius = new CornerRadius(tl);
                        return true;
                    }
                    return false;
                case 2:
                    if (TryCornerComponent(parts[0], out tl) && TryCornerComponent(parts[1], out tr))
                    {
                        radius = new CornerRadius(tl, tr, tl, tr);
                        return true;
                    }
                    return false;
                case 3:
                    if (TryCornerComponent(parts[0], out tl) && TryCornerComponent(parts[1], out tr) && TryCornerComponent(parts[2], out br))
                    {
                        radius = new CornerRadius(tl, tr, br, tr);
                        return true;
                    }
                    return false;
                default:
                    if (TryCornerComponent(parts[0], out tl) &&
                        TryCornerComponent(parts[1], out tr) &&
                        TryCornerComponent(parts[2], out br) &&
                        TryCornerComponent(parts[3], out bl))
                    {
                        radius = new CornerRadius(tl, tr, br, bl);
                        return true;
                    }
                    return false;
            }
        }

        private static bool TryCornerComponent(string raw, out double value)
        {
            value = 0;
            if (TryPx(raw, out value)) return true;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            raw = raw.Trim();
            if (raw.EndsWith("%", StringComparison.Ordinal))
            {
                double pct;
                if (TryDouble(raw.TrimEnd('%'), out pct))
                {
                    value = Math.Max(0, pct);
                    return true;
                }
            }
            return false;
        }

        private static string SelectFontFamily(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var parts = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var candidate = part.Trim().Trim('\"', '\'');
                if (candidate.Length == 0) continue;
                if (IsGenericFamily(candidate)) continue;
                return candidate;
            }

            if (parts.Length == 0) return null;
            var fallback = parts[0].Trim().Trim('"', '\'');
            if (string.IsNullOrEmpty(fallback)) return null;
            return MapGenericFamily(fallback) ?? fallback;
        }

        private static bool IsGenericFamily(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            switch (name.Trim().ToLowerInvariant())
            {
                case "sans-serif":
                case "serif":
                case "monospace":
                case "cursive":
                case "fantasy":
                case "system-ui":
                case "ui-sans-serif":
                case "ui-serif":
                case "ui-monospace":
                case "ui-rounded":
                    return true;
                default:
                    return false;
            }
        }

        private static string MapGenericFamily(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            switch (name.Trim().ToLowerInvariant())
            {
                case "sans-serif":
                case "system-ui":
                case "ui-sans-serif":
                    return "Segoe UI";
                case "serif":
                case "ui-serif":
                    return "Times New Roman";
                case "monospace":
                case "ui-monospace":
                    return "Consolas";
                case "cursive":
                    return "Comic Sans MS";
                case "fantasy":
                case "ui-rounded":
                    return "Segoe UI";
                default:
                    return null;
            }
        }

        private static string SafeGatherText(LiteElement n)
        {
            if (n == null) return null;
            if (n.IsText) return n.Text ?? "";
            var sb = new StringBuilder();
            foreach (var ch in n.Children)
            {
                sb.Append(SafeGatherText(ch));
            }
            return sb.ToString();
        }

        private static string DictGet(IDictionary<string, string> map, string key)
        {
            if (map == null || key == null) return null;
            string v;
            return map.TryGetValue(key, out v) ? v : null;
        }

        private static Uri ResolveUri(Uri baseUri, string href)
        {
            if (string.IsNullOrWhiteSpace(href)) return null;
            href = href.Trim();

            if (href.StartsWith("//"))
            {
                try { return new Uri((baseUri != null ? baseUri.Scheme : "https") + ":" + href); } catch { return null; }
            }

            Uri abs;
            if (Uri.TryCreate(href, UriKind.Absolute, out abs)) return abs;
            if (baseUri != null && Uri.TryCreate(baseUri, href, out abs)) return abs;
            return null;
        }

        private static string ResolveUrlIfNeeded(string value, Uri baseUri)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            var m = _urlFuncRx.Match(value);
            if (m.Success)
            {
                var u = m.Groups["u"].Value.Trim();
                var abs = ResolveUri(baseUri, u);
                if (abs != null)
                    return "url(" + abs.AbsoluteUri + ")";
            }
            return value;
        }

        // ---- CSS value parsing used for typed properties ----

        private static bool TryDouble(string s, out double v)
        {
            return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
        }

        private static bool TryInt(string s, out int v)
        {
            return int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out v);
        }

        private static CornerRadius TryCornerRadius(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return default(CornerRadius);
            var parts = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return default(CornerRadius);
            double v0;
            if (parts.Length == 1 && TryPx(parts[0], out v0)) return new CornerRadius(v0);
            if (parts.Length >= 2)
            {
                double v1;
                TryPx(parts[0], out v0);
                TryPx(parts[1], out v1);
                double v2 = v0, v3 = v1;
                if (parts.Length >= 3) { double tmp; TryPx(parts[2], out tmp); v2 = tmp; }
                if (parts.Length >= 4) { double tmp; TryPx(parts[3], out tmp); v3 = tmp; }
                return new CornerRadius(v0, v1, v2, v3);
            }
            return default(CornerRadius);
        }

        private static double _viewportWidth = 360;
        private static double _viewportHeight = 640;

        public static void SetViewportDimensions(double width, double height)
        {
            if (width > 0) _viewportWidth = width;
            if (height > 0) _viewportHeight = height;
        }

        private static bool TryPx(string s, out double px)
        {
            px = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            var sl = s.ToLowerInvariant();

            // Handle clamp() expressions: clamp(MIN, PREFERRED, MAX)
            // Must be tried before calc() so nested calc() inside clamp() works
            // (TryPxClamp itself calls TryPx on each operand, which handles calc()).
            if (sl.StartsWith("clamp("))
            {
                return TryPxClamp(s, out px);
            }

            // Handle calc() expressions
            if (sl.StartsWith("calc("))
            {
                double result = EvaluateCalc(s);
                if (double.IsNaN(result)) return false; // Parsing failed, ignore property
                px = result;
                return true;
            }

            // px
            if (sl.EndsWith("px"))
            {
                var num = s.Substring(0, s.Length - 2).Trim();
                double v;
                if (TryDouble(num, out v)) { px = v; return true; }
                return false;
            }

            // dvw (dynamic viewport width)
            if (sl.EndsWith("dvw"))
            {
                var num = s.Substring(0, s.Length - 3).Trim();
                double v;
                if (TryDouble(num, out v)) { px = v * _viewportWidth / 100.0; return true; }
                return false;
            }

            // dvh (dynamic viewport height)
            if (sl.EndsWith("dvh"))
            {
                var num = s.Substring(0, s.Length - 3).Trim();
                double v;
                if (TryDouble(num, out v)) { px = v * _viewportHeight / 100.0; return true; }
                return false;
            }

            // vw (viewport width)
            if (sl.EndsWith("vw"))
            {
                var num = s.Substring(0, s.Length - 2).Trim();
                double v;
                if (TryDouble(num, out v)) { px = v * _viewportWidth / 100.0; return true; }
                return false;
            }

            // vh (viewport height)
            if (sl.EndsWith("vh"))
            {
                var num = s.Substring(0, s.Length - 2).Trim();
                double v;
                if (TryDouble(num, out v)) { px = v * _viewportHeight / 100.0; return true; }
                return false;
            }

            // rem (baseline 16px)
            if (sl.EndsWith("rem"))
            {
                var num = s.Substring(0, s.Length - 3).Trim();
                double v;
                if (TryDouble(num, out v)) { px = v * 16.0; return true; }
                return false;
            }

            // em (approximate 16px without parent context)
            if (sl.EndsWith("em"))
            {
                var num = s.Substring(0, s.Length - 2).Trim();
                double v;
                if (TryDouble(num, out v)) { px = v * 16.0; return true; }
                return false;
            }

            // raw number -> px
            {
                double v;
                if (TryDouble(sl, out v)) { px = v; return true; }
            }
            return false;
        }

        // Resolve a clamp(MIN, PREFERRED, MAX) expression.
        // Each operand is itself any value TryPx can resolve (px, vw, vh, rem, em, %, calc()).
        // Returns max(MIN, min(PREFERRED, MAX)).
        private static bool TryPxClamp(string s, out double px)
        {
            px = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            // Outer "clamp(" and ")" must both be present
            if (s.Length < 8) return false;
            if (!s.StartsWith("clamp(", StringComparison.OrdinalIgnoreCase)) return false;
            if (s[s.Length - 1] != ')') return false;

            var inner = s.Substring(6, s.Length - 7).Trim();
            var parts = SplitTopLevelCommas(inner);
            if (parts.Count != 3) return false;

            double mn, pref, mx;
            // Try each operand. If a single operand can't resolve (e.g. exotic unit),
            // fall back to viewport-relative evaluation via EvaluateCalc so the test
            // cases like clamp(14px, 2vw, 20px) succeed.
            if (!TryPx(parts[0].Trim(), out mn))
            {
                var r = EvaluateCalc("calc(" + parts[0].Trim() + ")");
                if (double.IsNaN(r)) return false;
                mn = r;
            }
            if (!TryPx(parts[1].Trim(), out pref))
            {
                var r = EvaluateCalc("calc(" + parts[1].Trim() + ")");
                if (double.IsNaN(r)) return false;
                pref = r;
            }
            if (!TryPx(parts[2].Trim(), out mx))
            {
                var r = EvaluateCalc("calc(" + parts[2].Trim() + ")");
                if (double.IsNaN(r)) return false;
                mx = r;
            }

            if (double.IsNaN(mn) || double.IsNaN(pref) || double.IsNaN(mx)) return false;

            // Sanitize non-finite results (defensive — shouldn't happen, but safer than NaN)
            if (double.IsInfinity(mn) || double.IsInfinity(pref) || double.IsInfinity(mx)) return false;

            px = Math.Max(mn, Math.Min(mx, pref));
            return true;
        }

        // Split a string on top-level commas (commas inside nested parens don't count).
        // Used by clamp() and any other multi-arg CSS function.
        private static List<string> SplitTopLevelCommas(string s)
        {
            var result = new List<string>(3);
            if (string.IsNullOrEmpty(s)) return result;

            int depth = 0;
            int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '(') depth++;
                else if (c == ')') { if (depth > 0) depth--; }
                else if (c == ',' && depth == 0)
                {
                    result.Add(s.Substring(start, i - start));
                    start = i + 1;
                }
            }
            result.Add(s.Substring(start));
            return result;
        }

        // Find top-level clamp(MIN, PREF, MAX) expressions in a calc() body and
        // replace each with its evaluated px value. Operates only on top-level
        // matches (parens-aware) so nested clamp() inside an outer clamp() or
        // calc() is handled correctly. Idempotent — once all clamps are resolved
        // the second pass is a no-op.
        private static string ResolveNestedClamps(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf("clamp(", StringComparison.OrdinalIgnoreCase) < 0)
                return s;

            var sb = new System.Text.StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                // Match "clamp(" case-insensitively
                if (i + 6 <= s.Length &&
                    s.Substring(i, 6).Equals("clamp(", StringComparison.OrdinalIgnoreCase))
                {
                    // Find matching closing paren with depth counting
                    int depth = 1;
                    int j = i + 6;
                    while (j < s.Length && depth > 0)
                    {
                        char c = s[j];
                        if (c == '(') depth++;
                        else if (c == ')') depth--;
                        if (depth == 0) break;
                        j++;
                    }
                    if (depth != 0)
                    {
                        // Unbalanced — copy the rest verbatim and stop
                        sb.Append(s.Substring(i));
                        return sb.ToString();
                    }
                    // i..j inclusive is "clamp(... )"
                    string clampExpr = s.Substring(i, j - i + 1);
                    double resolved;
                    if (TryPxClamp(clampExpr, out resolved))
                    {
                        sb.Append(resolved.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        // Couldn't resolve — keep the original expression; EvaluateMathExpression
                        // will fail and the caller will get NaN, which TryPx translates to "skip".
                        sb.Append(clampExpr);
                    }
                    i = j + 1;
                }
                else
                {
                    sb.Append(s[i]);
                    i++;
                }
            }
            return sb.ToString();
        }

        private static double EvaluateCalc(string expr)
        {
            // Strip calc( and )
            var inner = expr.Trim();
            if (inner.StartsWith("calc(", StringComparison.OrdinalIgnoreCase))
                inner = inner.Substring(5);
            if (inner.EndsWith(")"))
                inner = inner.Substring(0, inner.Length - 1);
            inner = inner.Trim();

            // Pre-resolve any top-level clamp(MIN, PREF, MAX) expressions to plain px numbers.
            // This lets nested clamp() inside calc() (e.g. `calc(2 * clamp(1rem, 5vw, 3rem))`)
            // be reduced to a number before the math evaluator runs.
            inner = ResolveNestedClamps(inner);

            // Replace CSS units with pixel values
            inner = System.Text.RegularExpressions.Regex.Replace(inner, @"([\d.]+)\s*dvw", m =>
            {
                double v;
                TryDouble(m.Groups[1].Value, out v);
                return (v * _viewportWidth / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            });
            inner = System.Text.RegularExpressions.Regex.Replace(inner, @"([\d.]+)\s*dvh", m =>
            {
                double v;
                TryDouble(m.Groups[1].Value, out v);
                return (v * _viewportHeight / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            });
            inner = System.Text.RegularExpressions.Regex.Replace(inner, @"([\d.]+)\s*vw", m =>
            {
                double v;
                TryDouble(m.Groups[1].Value, out v);
                return (v * _viewportWidth / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            });
            inner = System.Text.RegularExpressions.Regex.Replace(inner, @"([\d.]+)\s*vh", m =>
            {
                double v;
                TryDouble(m.Groups[1].Value, out v);
                return (v * _viewportHeight / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            });
            inner = System.Text.RegularExpressions.Regex.Replace(inner, @"([\d.]+)\s*rem", m =>
            {
                double v;
                TryDouble(m.Groups[1].Value, out v);
                return (v * 16.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            });
            inner = System.Text.RegularExpressions.Regex.Replace(inner, @"([\d.]+)\s*em", m =>
            {
                double v;
                TryDouble(m.Groups[1].Value, out v);
                return (v * 16.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            });
            inner = System.Text.RegularExpressions.Regex.Replace(inner, @"([\d.]+)\s*px", m =>
            {
                double v;
                TryDouble(m.Groups[1].Value, out v);
                return v.ToString(System.Globalization.CultureInfo.InvariantCulture);
            });
            inner = System.Text.RegularExpressions.Regex.Replace(inner, @"([\d.]+)\s*%", m =>
            {
                double v;
                TryDouble(m.Groups[1].Value, out v);
                return (v * _viewportWidth / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            });

            // Simple math evaluator: handles +, -, *, / with parentheses
            try
            {
                return EvaluateMathExpression(inner);
            }
            catch
            {
                return double.NaN;
            }
        }

        private static double EvaluateMathExpression(string expr)
        {
            int pos = 0;
            return EvalAddSub(expr, ref pos);
        }

        private static double EvalAddSub(string expr, ref int pos)
        {
            double left = EvalMulDiv(expr, ref pos);
            while (pos < expr.Length)
            {
                SkipSpaces(expr, ref pos);
                if (pos >= expr.Length) break;
                char op = expr[pos];
                if (op == '+' || op == '-')
                {
                    pos++;
                    double right = EvalMulDiv(expr, ref pos);
                    left = op == '+' ? left + right : left - right;
                }
                else break;
            }
            return left;
        }

        private static double EvalMulDiv(string expr, ref int pos)
        {
            double left = EvalPrimary(expr, ref pos);
            while (pos < expr.Length)
            {
                SkipSpaces(expr, ref pos);
                if (pos >= expr.Length) break;
                char op = expr[pos];
                if (op == '*' || op == '/')
                {
                    pos++;
                    double right = EvalPrimary(expr, ref pos);
                    left = op == '*' ? left * right : (right != 0 ? left / right : 0);
                }
                else break;
            }
            return left;
        }

        private static double EvalPrimary(string expr, ref int pos)
        {
            SkipSpaces(expr, ref pos);
            if (pos >= expr.Length) return 0;

            if (expr[pos] == '(')
            {
                pos++;
                double val = EvalAddSub(expr, ref pos);
                SkipSpaces(expr, ref pos);
                if (pos < expr.Length && expr[pos] == ')') pos++;
                return val;
            }

            // Parse number (possibly negative)
            int start = pos;
            if (expr[pos] == '+' || expr[pos] == '-') pos++;
            while (pos < expr.Length && (char.IsDigit(expr[pos]) || expr[pos] == '.')) pos++;
            if (pos > start && (expr[start] == '+' || expr[start] == '-' || char.IsDigit(expr[start])))
            {
                string numStr = expr.Substring(start, pos - start);
                double val;
                if (TryDouble(numStr, out val)) return val;
            }
            return 0;
        }

        private static void SkipSpaces(string expr, ref int pos)
        {
            while (pos < expr.Length && char.IsWhiteSpace(expr[pos])) pos++;
        }


        private static bool TryThickness(string s, out Thickness th)
        {
            th = new Thickness(0);
            if (string.IsNullOrWhiteSpace(s)) return false;

            var parts = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            double a, b, c, d;

            if (parts.Length == 1) { if (!TryPx(parts[0], out a)) a = 0; th = new Thickness(a); return true; }
            if (parts.Length == 2)
            {
                if (!TryPx(parts[0], out a)) a = 0;
                if (!TryPx(parts[1], out b)) b = 0;
                th = new Thickness(b, a, b, a); return true;
            }
            if (parts.Length == 3)
            {
                if (!TryPx(parts[0], out a)) a = 0;
                if (!TryPx(parts[1], out b)) b = 0;
                if (!TryPx(parts[2], out c)) c = 0;
                th = new Thickness(b, a, b, c); return true;
            }
            // 4+
            if (!TryPx(parts[0], out a)) a = 0;
            if (!TryPx(parts[1], out b)) b = 0;
            if (!TryPx(parts[2], out c)) c = 0;
            if (!TryPx(parts[3], out d)) d = 0;
            th = new Thickness(d, a, b, c); return true;
        }

        private static Windows.UI.Color FromHex(string hex)
        {
            hex = (hex ?? "").Trim().TrimStart('#');
            if (hex.Length == 3) // #abc -> #aabbcc
            {
                var r = Convert.ToByte(new string(hex[0], 2), 16);
                var g = Convert.ToByte(new string(hex[1], 2), 16);
                var b = Convert.ToByte(new string(hex[2], 2), 16);
                return Windows.UI.Color.FromArgb(255, r, g, b);
            }
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return Windows.UI.Color.FromArgb(255, r, g, b);
            }
            if (hex.Length == 8)
            {
                byte a = Convert.ToByte(hex.Substring(0, 2), 16);
                byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                byte b = Convert.ToByte(hex.Substring(6, 2), 16);
                return Windows.UI.Color.FromArgb(a, r, g, b);
            }
            return Windows.UI.Colors.Black;
        }

        private static Windows.UI.Color? TryColor(string css)
        {
            try
            {
                return CssParser.ParseColor(css);
            }
            catch { return null; }
        }



        private static string ExtractBackgroundColor(Dictionary<string, string> map)
        {
            string v;
            if (map.TryGetValue("background-color", out v) && !string.IsNullOrWhiteSpace(v)) return v;
            if (map.TryGetValue("background", out v) && !string.IsNullOrWhiteSpace(v))
            {
                // Find all potential color matches
                // Matches hex, rgb/rgba, or named colors (letters only)
                var matches = _hexColorRx.Matches(v);
                foreach (Match m in matches)
                {
                    var val = m.Value;
                    // Ignore common keywords that might look like colors but aren't
                    if (val.Equals("none", StringComparison.OrdinalIgnoreCase) || 
                        val.Equals("url", StringComparison.OrdinalIgnoreCase) ||
                        val.Equals("repeat", StringComparison.OrdinalIgnoreCase) ||
                        val.Equals("scroll", StringComparison.OrdinalIgnoreCase) ||
                        val.Equals("fixed", StringComparison.OrdinalIgnoreCase) ||
                        val.Equals("center", StringComparison.OrdinalIgnoreCase) ||
                        val.Equals("top", StringComparison.OrdinalIgnoreCase) ||
                        val.Equals("bottom", StringComparison.OrdinalIgnoreCase) ||
                        val.Equals("left", StringComparison.OrdinalIgnoreCase) ||
                        val.Equals("right", StringComparison.OrdinalIgnoreCase))
                        continue;

                    return val;
                }
            }
            return null;
        }

        private static string ExtractBorderColor(Dictionary<string, string> map)
        {
            string v;
            if (map.TryGetValue("border-color", out v) && !string.IsNullOrWhiteSpace(v)) return v;
            if (map.TryGetValue("border", out v) && !string.IsNullOrWhiteSpace(v))
            {
                // Find all potential color matches
                var matches = _hexColorRx.Matches(v);
                foreach (Match m in matches)
                {
                    var val = m.Value;
                    if (IsBorderStyle(val)) continue;
                    // Ignore width keywords
                    if (val.Equals("thin", StringComparison.OrdinalIgnoreCase) || 
                        val.Equals("medium", StringComparison.OrdinalIgnoreCase) || 
                        val.Equals("thick", StringComparison.OrdinalIgnoreCase) ||
                        val.EndsWith("px", StringComparison.OrdinalIgnoreCase)) 
                        continue;
                    
                    return val;
                }
            }
            return null;
        }

        private static bool IsBorderStyle(string s)
        {
            s = s.ToLowerInvariant();
            return s == "none" || s == "hidden" || s == "dotted" || s == "dashed" || 
                   s == "solid" || s == "double" || s == "groove" || s == "ridge" || 
                   s == "inset" || s == "outset";
        }

        private static bool TryPercent(string s, out double pct)
        {
            pct = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s.EndsWith("%"))
            {
                var num = s.Substring(0, s.Length - 1).Trim();
                return TryDouble(num, out pct);
            }
            return false;
        }

        private static string ExtractBorderThickness(Dictionary<string, string> map)
        {
            string v;
            if (map.TryGetValue("border-width", out v) && !string.IsNullOrWhiteSpace(v)) return v;
            if (map.TryGetValue("border", out v) && !string.IsNullOrWhiteSpace(v))
            {
                // naive: pick first length
                var m = _borderPxRx.Match(v);
                if (m.Success) return m.Groups[0].Value;
            }
            return null;
        }

        private static string ExtractBorderStyle(Dictionary<string, string> map)
        {
            string v;
            if (map.TryGetValue("border-style", out v) && !string.IsNullOrWhiteSpace(v)) return v;
            if (map.TryGetValue("border", out v) && !string.IsNullOrWhiteSpace(v))
            {
                var parts = v.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (IsBorderStyle(part)) return part;
                }
            }
            return null;
        }

        private static void Log(Action<string> log, string msg)
        {
            try { if (log != null) log(msg); } catch { }
        }

        private static void ResolveVariables(List<CssRule> rules)
        {
            if (rules == null) return;
            var vars = new Dictionary<string, string>(StringComparer.Ordinal);

            // 1. Collect global variables from :root
            foreach (var rule in rules)
            {
                if (rule.Selectors == null) continue;
                foreach (var sel in rule.Selectors)
                {
                    bool isRoot = false;
                    if (sel.Segments != null && sel.Segments.Count == 1)
                    {
                        var seg = sel.Segments[0];
                        if (string.Equals(seg.Tag, ":root", StringComparison.OrdinalIgnoreCase)) isRoot = true;
                    }
                    
                    if (isRoot && rule.Declarations != null)
                    {
                        foreach (var kvp in rule.Declarations)
                        {
                            if (kvp.Key.StartsWith("--"))
                            {
                                vars[kvp.Key] = kvp.Value.Value;
                            }
                        }
                    }
                }
            }

            if (vars.Count == 0) return;

            // 2. Substitute in all rules
            foreach (var rule in rules)
            {
                if (rule.Declarations == null) continue;
                foreach (var kvp in rule.Declarations.ToList())
                {
                    var val = kvp.Value.Value;
                    if (val != null && val.IndexOf("var(--", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var newVal = _varRefRx.Replace(val, m =>
                        {
                            var name = m.Groups[1].Value.Trim();
                            string sub;
                            if (vars.TryGetValue(name, out sub)) return sub;
                            return m.Value;
                        });
                        
                        if (newVal != val)
                        {
                            kvp.Value.Value = newVal;
                        }
                    }
                }
            }
        }
    }
}

