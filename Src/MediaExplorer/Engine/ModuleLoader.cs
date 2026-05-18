using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NiL.JS;
using NiL.JS.Core;
using JSModule = NiL.JS.Module;

namespace BrowserCore.Engine
{
    internal sealed class ModuleLoader
    {
        private readonly JavaScriptEngine _engine;
        private readonly Context _nil;
        private readonly ConcurrentDictionary<string, JSModule> _moduleCache = new ConcurrentDictionary<string, JSModule>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, Task<string>> _fetchTasks = new ConcurrentDictionary<string, Task<string>>(StringComparer.Ordinal);
        private readonly Regex _importRegex = new Regex("import\\s+(?:[^\\'\";]+?\\s+from\\s+)?['\"](?<spec>[^'\"]+)['\"]", RegexOptions.CultureInvariant);
        private readonly Regex _sideEffectImportRegex = new Regex("import\\s+['\"](?<spec>[^'\"]+)['\"]", RegexOptions.CultureInvariant);
        private readonly object _initLock = new object();
        private readonly Dictionary<string, string> _importMap = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly MethodInfo _moduleContextSetter;
        private bool _subscribed;
        private int _inlineCounter;

        public ModuleLoader(JavaScriptEngine engine, Context nil)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _nil = nil ?? throw new ArgumentNullException(nameof(nil));
            var ctxProp = typeof(JSModule).GetProperty("Context", BindingFlags.Public | BindingFlags.Instance);
            _moduleContextSetter = ctxProp?.GetSetMethod(true);
        }

        private void EnsureSubscribed()
        {
            if (_subscribed) return;
            lock (_initLock)
            {
                if (_subscribed) return;
                JSModule.ResolveModule += OnResolveModule;
                _subscribed = true;
            }
        }

        public void SetImportMap(Dictionary<string, string> map)
        {
            _importMap.Clear();
            if (map != null)
                foreach (var kv in map)
                    _importMap[kv.Key] = kv.Value;
        }

        public Task ExecuteModuleTagAsync(LiteElement node, Uri sourceUri, Uri baseUri)
        {
            EnsureSubscribed();
            if (node == null) return Task.Delay(0);

            if (sourceUri != null)
                return ExecuteModuleAsync(sourceUri.AbsoluteUri, sourceUri, baseUri);

            _inlineCounter++;
            var inlineKey = "inline:" + _inlineCounter.ToString("x") + ":" + Guid.NewGuid().ToString("n");
            return ExecuteInlineModuleAsync(inlineKey, baseUri, node.Text ?? string.Empty);
        }

        private async Task ExecuteModuleAsync(string key, Uri moduleUri, Uri baseUri)
        {
            if (_moduleCache.ContainsKey(key)) return;
            if (string.IsNullOrWhiteSpace(key)) return;

            string source;
            try { source = await FetchModuleTextAsync(moduleUri, baseUri).ConfigureAwait(false); }
            catch { return; }
            if (source == null) return;

            await PrefetchDependencies(source, moduleUri).ConfigureAwait(false);

            ExecuteInNil(source);
        }

        private async Task ExecuteInlineModuleAsync(string key, Uri baseUri, string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return;

            await PrefetchDependencies(source, baseUri).ConfigureAwait(false);

            ExecuteInNil(source);
        }

        private void ExecuteInNil(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return;
            try { _nil.Eval(code); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[Module] Eval error: " + ex.Message); }
        }

        private async Task PrefetchDependencies(string source, Uri baseUri)
        {
            if (string.IsNullOrWhiteSpace(source) || baseUri == null) return;
            foreach (var spec in CollectSpecifiers(source))
            {
                var resolved = ResolveUrl(baseUri, spec);
                if (resolved == null) continue;
                var depKey = resolved.AbsoluteUri;
                if (_moduleCache.ContainsKey(depKey) || _fetchTasks.ContainsKey(depKey)) continue;

                string depSource;
                try { depSource = await FetchModuleTextAsync(resolved, baseUri).ConfigureAwait(false); }
                catch { continue; }
                if (depSource == null) continue;

                await PrefetchDependencies(depSource, resolved).ConfigureAwait(false);

                CacheModule(depKey, depSource);
            }
        }

        private void CacheModule(string key, string source)
        {
            if (_moduleCache.ContainsKey(key)) return;
            var module = new JSModule(key, source);
            if (_moduleContextSetter != null) _moduleContextSetter.Invoke(module, new object[] { _nil });
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

        private void OnResolveModule(object sender, ResolveModuleEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.ModulePath)) return;

            var spec = e.ModulePath;
            var mapped = ResolveBareSpecifier(spec);
            var url = mapped ?? spec;

            JSModule cached;
            if (_moduleCache.TryGetValue(url, out cached))
            {
                e.Module = cached;
                e.AddToCache = true;
                return;
            }

            string source = null;
            string cacheKey = url;

            if (TryResolveUrl(url, out var absUri))
            {
                var fetchTask = _fetchTasks.GetOrAdd(absUri.AbsoluteUri, _ => FetchModuleTextAsync(absUri, null));
                try { source = fetchTask.GetAwaiter().GetResult(); cacheKey = absUri.AbsoluteUri; }
                catch { }
            }

            if (source == null) return;

            if (!_moduleCache.ContainsKey(cacheKey))
            {
                var module = new JSModule(cacheKey, source);
                if (_moduleContextSetter != null) _moduleContextSetter.Invoke(module, new object[] { _nil });
                _moduleCache.TryAdd(cacheKey, module);
            }

            e.Module = _moduleCache[cacheKey];
            e.AddToCache = true;
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
