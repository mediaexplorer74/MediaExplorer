using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NiL.JS;
using NiL.JS.Core;
using JSModule = NiL.JS.Module;

namespace BrowserCore.Engine
{
    internal sealed class ModuleLoader : IModuleResolver
    {
        private readonly JavaScriptEngine _engine;
        private readonly GlobalContext _nil;
        private readonly ConcurrentDictionary<string, JSModule> _moduleCache = new ConcurrentDictionary<string, JSModule>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, Task<string>> _fetchTasks = new ConcurrentDictionary<string, Task<string>>(StringComparer.Ordinal);
        private readonly Regex _importRegex = new Regex("import\\s+(?:[^\\'\";]+?\\s+from\\s+)?['\"](?<spec>[^'\"]+)['\"]", RegexOptions.CultureInvariant);
        private readonly Regex _sideEffectImportRegex = new Regex("import\\s+['\"](?<spec>[^'\"]+)['\"]", RegexOptions.CultureInvariant);
        private readonly Dictionary<string, string> _importMap = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Uri> _moduleBaseUris = new Dictionary<string, Uri>(StringComparer.Ordinal);
        private int _inlineCounter;

        public ModuleLoader(JavaScriptEngine engine, GlobalContext nil)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _nil = nil ?? throw new ArgumentNullException(nameof(nil));
        }

        public void SetImportMap(Dictionary<string, string> map)
        {
            _importMap.Clear();
            if (map != null)
                foreach (var kv in map)
                    _importMap[kv.Key] = kv.Value;
        }

        public Task ExecuteModuleTagAsync(LiteElement node, Uri sourceUri, Uri baseUri, string inlineContent = null)
        {
            if (node == null) return Task.Delay(0);

            if (sourceUri != null)
                return ExecuteModuleAsync(sourceUri.AbsoluteUri, sourceUri, baseUri);

            _inlineCounter++;
            var inlineKey = "inline:" + _inlineCounter.ToString("x") + ":" + Guid.NewGuid().ToString("n");
            return ExecuteInlineModuleAsync(inlineKey, baseUri, inlineContent ?? string.Empty);
        }

        private async Task ExecuteModuleAsync(string key, Uri moduleUri, Uri baseUri)
        {
            if (_moduleCache.ContainsKey(key)) return;
            if (string.IsNullOrWhiteSpace(key)) return;

            lock (_moduleBaseUris) _moduleBaseUris[key] = moduleUri;

            DevToolsLogger.Log("[Module] Fetching: " + moduleUri);
            string source;
            try { source = await FetchModuleTextAsync(moduleUri, baseUri).ConfigureAwait(false); }
            catch (Exception ex) { DevToolsLogger.Log("[Module] Fetch failed: " + moduleUri + " " + ex.Message); return; }
            if (source == null) { DevToolsLogger.Log("[Module] Fetch returned null: " + moduleUri); return; }

            if (source.TrimStart().StartsWith("<"))
            {
                var preview = source.Length > 100 ? source.Substring(0, 100) : source;
                DevToolsLogger.Log("[Module] WARNING: Got HTML instead of JS from " + moduleUri + " preview: " + preview);
                return;
            }

            DevToolsLogger.Log("[Module] Fetched OK: " + moduleUri + " (" + source.Length + " bytes)");
            await PrefetchDependencies(source, moduleUri).ConfigureAwait(false);

            RunModule(key, source);
        }

        private async Task ExecuteInlineModuleAsync(string key, Uri baseUri, string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return;
            lock (_moduleBaseUris) _moduleBaseUris[key] = baseUri;
            await PrefetchDependencies(source, baseUri).ConfigureAwait(false);
            RunModule(key, source);
        }

        private void RunModule(string key, string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return;
            try
            {
                var module = new JSModule(key, code, _nil);
                module.ModuleResolversChain.Add(this);
                module.Run();
            }
            catch (Exception ex) { DevToolsLogger.Log("[Module] Eval error: " + ex.Message); }
        }

        private async Task PrefetchDependencies(string source, Uri baseUri)
        {
            if (string.IsNullOrWhiteSpace(source) || baseUri == null) return;
            var specs = CollectSpecifiers(source).ToList();
            if (specs.Count > 0)
                DevToolsLogger.Log("[Module] Found " + specs.Count + " import(s) in " + baseUri);

            foreach (var spec in specs)
            {
                var resolved = ResolveUrl(baseUri, spec);
                if (resolved == null) { DevToolsLogger.Log("[Module] Skip unresolved import: " + spec); continue; }
                var depKey = resolved.AbsoluteUri;
                if (_moduleCache.ContainsKey(depKey) || _fetchTasks.ContainsKey(depKey)) continue;

                DevToolsLogger.Log("[Module] Prefetching dependency: " + resolved);
                string depSource;
                try { depSource = await FetchModuleTextAsync(resolved, baseUri).ConfigureAwait(false); }
                catch (Exception ex) { DevToolsLogger.Log("[Module] Prefetch failed: " + resolved + " " + ex.Message); continue; }
                if (depSource == null) { DevToolsLogger.Log("[Module] Prefetch returned null: " + resolved); continue; }

                if (depSource.TrimStart().StartsWith("<"))
                {
                    var preview = depSource.Length > 80 ? depSource.Substring(0, 80) : depSource;
                    DevToolsLogger.Log("[Module] WARNING: Dependency is HTML: " + resolved + " preview: " + preview);
                    continue;
                }

                await PrefetchDependencies(depSource, resolved).ConfigureAwait(false);

                lock (_moduleBaseUris) _moduleBaseUris[depKey] = resolved;
                CacheModule(depKey, depSource);
            }
        }

        private void CacheModule(string key, string source)
        {
            if (_moduleCache.ContainsKey(key)) return;
            var module = new JSModule(key, source, _nil);
            module.ModuleResolversChain.Add(this);
            _moduleCache.TryAdd(key, module);
        }

        private IEnumerable<string> CollectSpecifiers(string source)
        {
            if (string.IsNullOrEmpty(source)) yield break;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in _importRegex.Matches(source))
            {
                var spec = match.Groups["spec"].Value;
                if (string.IsNullOrWhiteSpace(spec)) continue;
                if (seen.Add(spec)) yield return spec;
            }
            foreach (Match match in _sideEffectImportRegex.Matches(source))
            {
                var spec = match.Groups["spec"].Value;
                if (string.IsNullOrWhiteSpace(spec)) continue;
                if (seen.Add(spec)) yield return spec;
            }
        }

        private static Uri ResolveUrl(Uri baseUri, string specifier)
        {
            if (string.IsNullOrWhiteSpace(specifier)) return null;
            try
            {
                Uri abs;
                if (Uri.TryCreate(specifier, UriKind.Absolute, out abs)) return abs;
                if (baseUri != null && Uri.TryCreate(baseUri, specifier, out abs)) return abs;
            }
            catch { return null; }
            return null;
        }

        private string ResolveBareSpecifier(string specifier)
        {
            string mapped;
            if (_importMap.TryGetValue(specifier, out mapped)) return mapped;
            return null;
        }

        bool IModuleResolver.TryGetModule(ModuleRequest request, out Module result)
        {
            result = null;
            if (request == null) return false;

            var spec = request.CmdArgument;
            var absPath = request.AbsolutePath;

            DevToolsLogger.Log("[Module] ResolveModule: spec=" + spec + " absPath=" + absPath);

            JSModule cached;
            if (!string.IsNullOrEmpty(absPath) && _moduleCache.TryGetValue(absPath, out cached))
            {
                DevToolsLogger.Log("[Module] Cache hit: " + absPath);
                result = cached;
                return true;
            }

            if (_moduleCache.TryGetValue(spec, out cached))
            {
                DevToolsLogger.Log("[Module] Cache hit (spec): " + spec);
                result = cached;
                return true;
            }

            string mapped = ResolveBareSpecifier(spec);
            if (!string.IsNullOrEmpty(mapped))
            {
                if (_moduleCache.TryGetValue(mapped, out cached))
                {
                    DevToolsLogger.Log("[Module] Cache hit (mapped): " + mapped);
                    result = cached;
                    return true;
                }
            }

            string initiatorPath = null;
            if (request.Initiator != null)
                initiatorPath = request.Initiator.FilePath;

            Uri resolvedUrl = null;
            if (!string.IsNullOrEmpty(initiatorPath))
            {
                Uri baseUri;
                lock (_moduleBaseUris) _moduleBaseUris.TryGetValue(initiatorPath, out baseUri);
                if (baseUri == null && Uri.TryCreate(initiatorPath, UriKind.Absolute, out baseUri)) { }
                if (baseUri != null)
                    resolvedUrl = ResolveUrl(baseUri, spec);
            }

            if (resolvedUrl == null)
            {
                DevToolsLogger.Log("[Module] ResolveModule could not resolve: spec=" + spec + " initiator=" + initiatorPath);
                return false;
            }

            var cacheKey = resolvedUrl.AbsoluteUri;
            if (_moduleCache.TryGetValue(cacheKey, out cached))
            {
                DevToolsLogger.Log("[Module] Cache hit (resolved): " + cacheKey);
                result = cached;
                return true;
            }

            DevToolsLogger.Log("[Module] ResolveModule failed: spec=" + spec + " resolved=" + cacheKey);
            return false;
        }

        private bool TryResolveUrl(string url, out Uri absUri)
        {
            absUri = null;
            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out absUri)) return true;
                var loc = _nil.GetVariable("location");
                if (loc != null && loc.ValueType != JSValueType.Undefined)
                {
                    var href = loc["href"];
                    if (href != null && href.ValueType != JSValueType.Undefined && href.Value != null)
                    {
                        var hrefStr = href.Value.ToString();
                        Uri baseUri;
                        if (!string.IsNullOrWhiteSpace(hrefStr) && Uri.TryCreate(hrefStr, UriKind.Absolute, out baseUri))
                        {
                            if (Uri.TryCreate(baseUri, url, out absUri)) return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private async Task<string> FetchModuleTextAsync(Uri uri, Uri referer)
        {
            try { return await _engine.FetchModuleTextAsync(uri, referer).ConfigureAwait(false); }
            catch { return null; }
        }
    }
}
