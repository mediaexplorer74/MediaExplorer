using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using NiL.JS;
using NiL.JS.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using System.Globalization;

class Program
{
    private class BadHashRecord
    {
        public string Hash { get; set; }
        public string Preview { get; set; }
        public int Count { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        // Optional diagnostics: populated when a failure occurs so the store
        // contains useful information for post-mortem analysis.
        public string LastExceptionType { get; set; }
        public string LastExceptionMessage { get; set; }
        public string LastExceptionStack { get; set; }
        public DateTime? LastExceptionTime { get; set; }
        public string LastExceptionContext { get; set; }
    }

    private class TestExpectation
    {
        public bool? shouldFail { get; set; }
        public JToken expectedHash { get; set; }
        public int? minOccurrences { get; set; }
    }

    static int ComputeHash(string code)
    {
        var h = 0;
        try { unchecked { for (int i = 0; i < code.Length; i++) h = h * 31 + code[i]; } } catch { }
        return h;
    }

    static string StoreFilePath()
    {
        // Prefer workspace store to avoid writing into bin/obj which may be cleaned
        return WorkspaceStorePath();
    }

    static string NormalizeStorePath(string overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;
        return StoreFilePath();
    }

    static void RunSuite(string dir, string overrideStorePath, int threshold, bool persistFlag, int repeat, int pageThreshold, bool assertMode)
    {
        if (!Directory.Exists(dir)) { Console.WriteLine("Suite dir not found: " + dir); return; }
        var storeFile = NormalizeStorePath(overrideStorePath);
        Console.WriteLine($"Running suite in {dir}; store={storeFile} threshold={threshold} persist={persistFlag} repeat={repeat} pageThreshold={pageThreshold} assert={assertMode}");

        var store = File.Exists(storeFile) ? LoadStoreFromPath(storeFile) : new List<BadHashRecord>();

        var jsFiles = Directory.GetFiles(dir, "*.js");
        var htmlFiles = Directory.GetFiles(dir, "*.html");
        var tests = jsFiles.Concat(htmlFiles).OrderBy(x => x).ToArray();
        Console.WriteLine($"Found {tests.Length} tests ({jsFiles.Length} .js, {htmlFiles.Length} .html)");

        int failures = 0;
        foreach (var t in tests)
        {
            for (int rep = 0; rep < Math.Max(1, repeat); rep++)
            {
                var ext = Path.GetExtension(t)?.ToLowerInvariant();
                if (ext == ".html")
                {
                    var html = File.ReadAllText(t);
                    var scripts = ExtractInlineScriptsFromHtml(html);
                    Console.WriteLine($"\n=== HTML Test: {Path.GetFileName(t)} (rep={rep+1}/{repeat}) scripts={scripts.Count} ===");
                    int pageFails = 0;
                    int idx = 0;
                    foreach (var s in scripts)
                    {
                        idx++;
                        var h = ComputeHash(s).ToString();
                        var rec = store.FirstOrDefault(x => x.Hash == h);
                        if (rec != null && rec.Count >= threshold)
                        {
                            Console.WriteLine($"SKIP inline #{idx} hash={h} preview=\"{TrimPreview(s)}\"");
                            continue;
                        }
                        try
                        {
                            var ctx = new Context();
                            var wrapped = "(function(){" + s + "})()";
                            ctx.Eval(wrapped);
                            Console.WriteLine($"inline #{idx}: OK");
                        }
                        catch (Exception ex)
                        {
                            pageFails++;
                            failures++;
                            Console.WriteLine($"inline #{idx}: ERROR -> {ex.Message}");
                            if (rec == null)
                            {
                                rec = new BadHashRecord { Hash = h, Preview = TrimPreview(s), Count = 0, FirstSeen = DateTime.UtcNow, LastSeen = DateTime.UtcNow };
                                store.Add(rec);
                            }
                            rec.Count += 1; rec.LastSeen = DateTime.UtcNow;
                            Console.WriteLine($"Observed fail for hash={h} totalCount={rec.Count}");
                            if (rec.Count >= threshold) Console.WriteLine($"Hash {h} reached threshold {threshold} (will be considered bad)");
                            if (pageThreshold > 0 && pageFails >= pageThreshold)
                            {
                                Console.WriteLine($"Page-level threshold reached for {Path.GetFileName(t)}: pageFails={pageFails} >= {pageThreshold}");
                                break;
                            }
                        }
                    }
                }
                else // assume .js
                {
                    var code = File.ReadAllText(t);
                    Console.WriteLine($"\n=== Test: {Path.GetFileName(t)} (rep={rep+1}/{repeat}) ({code.Length} chars) ===");
                    int fails = 0;
                    try
                    {
                        var ctx = new Context();
                        var wrapped = "(function(){" + code + "})()";
                        try { ctx.Eval(wrapped); Console.WriteLine("Run: OK"); }
                        catch (Exception ex) { fails++; Console.WriteLine("Run: ERROR -> " + ex.Message); }
                    }
                    catch (Exception ex) { Console.WriteLine("Engine error: " + ex.Message); }

                    if (fails > 0)
                    {
                        var h = ComputeHash(code).ToString();
                        var rec = store.FirstOrDefault(x => x.Hash == h);
                        if (rec == null) { rec = new BadHashRecord { Hash = h, Preview = TrimPreview(code), Count = 0, FirstSeen = DateTime.UtcNow, LastSeen = DateTime.UtcNow }; store.Add(rec); }
                        rec.Count += fails; rec.LastSeen = DateTime.UtcNow;
                        try
                        {
                            // populate diagnostics from the thrown exception if available via a simple pattern
                            // For NilJsTest we only have the Exception.Message; include that as LastExceptionMessage
                            // and timestamp.
                            rec.LastExceptionMessage = Truncate("Run failed with exception during eval", 1024);
                            rec.LastExceptionTime = DateTime.UtcNow;
                        }
                        catch { }
                        Console.WriteLine($"Observed fail for hash={h} totalCount={rec.Count}");
                        if (rec.Count >= threshold)
                        {
                            Console.WriteLine($"Hash {h} reached threshold {threshold} (will be considered bad)");
                        }
                        failures += fails;
                    }
                }

                // page-level threshold simulation for non-HTML tests (best-effort)
                if (pageThreshold > 0 && Path.GetExtension(t).ToLowerInvariant() != ".html")
                {
                    var distinctFails = store.Where(x => x.Preview != null && x.Preview.Contains(Path.GetFileName(t))).Count();
                    if (distinctFails >= pageThreshold)
                    {
                        Console.WriteLine($"Page-level threshold reached for {Path.GetFileName(t)}: distinctFails={distinctFails} >= {pageThreshold}");
                        break; // stop repeating this test
                    }
                }
            }
        }

        if (persistFlag)
        {
            SaveStoreToPath(store, storeFile);
            Console.WriteLine($"Saved store with {store.Count} entries to {storeFile}");
        }
        else
        {
            Console.WriteLine($"Suite finished; store not persisted. Found {store.Count} candidate bad hashes (use --persist to save)");
        }

        // If assertMode is enabled, evaluate expectations for each test file with .expect.json
        if (assertMode)
        {
            Console.WriteLine("\n--- Running assertions from .expect.json files ---");
            var expectFiles = Directory.GetFiles(dir, "*.expect.json").ToDictionary(x => Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(x)), StringComparer.OrdinalIgnoreCase);
            foreach (var t in tests)
            {
                var name = Path.GetFileNameWithoutExtension(t);
                var expectPath = Path.Combine(dir, name + ".expect.json");
                if (!File.Exists(expectPath)) continue;
                try
                {
                    var text = File.ReadAllText(expectPath);
                    var exp = JsonConvert.DeserializeObject<TestExpectation>(text);
                    bool ok = true; string msg = null;
                    var code = File.ReadAllText(t);
                    var hstr = ComputeHash(code).ToString();
                    if (exp.shouldFail.HasValue)
                    {
                        var actuallyFailed = store.Any(x => x.Hash == hstr && x.Count > 0);
                        if (exp.shouldFail.Value != actuallyFailed) { ok = false; msg = $"shouldFail expected={exp.shouldFail.Value} actual={actuallyFailed}"; }
                    }
                    if (exp.expectedHash != null)
                    {
                        string expectedHashStr;
                        try
                        {
                            if (exp.expectedHash.Type == JTokenType.Integer)
                                expectedHashStr = exp.expectedHash.ToObject<long>().ToString(CultureInfo.InvariantCulture);
                            else
                                expectedHashStr = exp.expectedHash.ToString();
                        }
                        catch { expectedHashStr = exp.expectedHash.ToString(); }
                        if (!string.Equals(expectedHashStr, hstr, StringComparison.OrdinalIgnoreCase)) { ok = false; msg = $"expectedHash={expectedHashStr} actual={hstr}"; }
                    }
                    if (exp.minOccurrences.HasValue)
                    {
                        var rec = store.FirstOrDefault(x => x.Hash == hstr);
                        var count = rec?.Count ?? 0;
                        if (count < exp.minOccurrences.Value) { ok = false; msg = $"minOccurrences expected>={exp.minOccurrences.Value} actual={count}"; }
                    }

                    if (ok) Console.WriteLine($"ASSERT OK: {name}"); else { Console.WriteLine($"ASSERT FAIL: {name} -> {msg}"); failures++; }
                }
                catch (Exception ex) { Console.WriteLine($"ASSERT ERROR reading {expectPath}: {ex.Message}"); failures++; }
            }

            if (failures > 0)
            {
                Console.WriteLine($"Assertions failed: {failures}");
                Environment.Exit(2);
            }
            else Console.WriteLine("All assertions passed");
        }
    }

    static List<BadHashRecord> LoadStoreFromPath(string path)
    {
        try
        {
            if (!File.Exists(path)) return new List<BadHashRecord>();
            var text = File.ReadAllText(path);
            var list = JsonConvert.DeserializeObject<List<BadHashRecord>>(text);
            return list ?? new List<BadHashRecord>();
        }
        catch { return new List<BadHashRecord>(); }
    }

    // Extract inline <script>...</script> contents from HTML (simple, not full parser)
    static List<string> ExtractInlineScriptsFromHtml(string html)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(html)) return list;
        var rx = new Regex("<script(?:(?:\\s|\\S)*?)>([\\s\\S]*?)</script>", RegexOptions.IgnoreCase);
        var mc = rx.Matches(html);
        foreach (Match m in mc)
        {
            try
            {
                var content = m.Groups[1].Value;
                // skip external scripts (src attribute present)
                var tag = m.Value;
                if (tag.IndexOf("src=", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (!string.IsNullOrWhiteSpace(content)) list.Add(content);
            }
            catch { }
        }
        return list;
    }

    static void SaveStoreToPath(List<BadHashRecord> list, string path)
    {
        try
        {
            var json = JsonConvert.SerializeObject(list, Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch (Exception ex) { Console.WriteLine("Failed to save store: " + ex.Message); }
    }

    static string FindLatestStoreInRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
        var files = Directory.GetFiles(root, "js_bad_hashes.json", SearchOption.AllDirectories);
        if (files == null || files.Length == 0) return null;
        return files.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).FirstOrDefault();
    }

    // Helper: prefer not to write into bin/obj; return a safe default store path in workspace
    static string WorkspaceStorePath()
    {
        // Use the project src directory (repo root) as a stable place: ./Src/NilJsTest/js_bad_hashes.json
        var baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
        try
        {
            // If we're running from bin/... move up to project folder if possible
            var di = new DirectoryInfo(baseDir);
            while (di != null && !File.Exists(Path.Combine(di.FullName, "NilJsTest.csproj"))) di = di.Parent;
                if (di != null)
                {
                var candidate = Path.Combine(di.FullName, "js_bad_hashes.json");
                return candidate;
                }
        }
        catch { }
        // Fallback: place next to exe
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "js_bad_hashes.json");
    }

    static List<BadHashRecord> LoadStore()
    {
        try
        {
            var path = StoreFilePath();
            if (!File.Exists(path)) return new List<BadHashRecord>();
            var text = File.ReadAllText(path);
            var list = JsonConvert.DeserializeObject<List<BadHashRecord>>(text);
            return list ?? new List<BadHashRecord>();
        }
        catch { return new List<BadHashRecord>(); }
    }

    static void SaveStore(List<BadHashRecord> list)
    {
        try
        {
            var path = StoreFilePath();
            var json = JsonConvert.SerializeObject(list, Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch (Exception ex) { Console.WriteLine("Failed to save store: " + ex.Message); }
    }

    static void PrintUsage()
    {
        Console.WriteLine("NilJsTest CLI — helpers for testing NiL.JS scripts and bad-hash persistence");
        Console.WriteLine("Usage:");
        Console.WriteLine("  NilJsTest --file <path>                Run a JS file (module) and report errors");
        Console.WriteLine("  NilJsTest --eval <code> [--repeat N] [--persist]   Eval JS code (wraps as function) N times");
        Console.WriteLine("  NilJsTest show                          Show persisted bad-hash store");
        Console.WriteLine("  NilJsTest export                        Print store JSON to stdout");
        Console.WriteLine("  NilJsTest import                        Read JSON from stdin and restore store");
        Console.WriteLine("  NilJsTest clear                         Clear persisted store file");
        Console.WriteLine("  NilJsTest hash <code>                   Compute engine hash (decimal and hex) for a snippet");
        Console.WriteLine("  NilJsTest --suite <dir> [--store <path>] [--threshold N] [--persist]   Run a directory of JS test cases");
        Console.WriteLine("Examples:");
        Console.WriteLine("  NilJsTest --eval \"throw new Error('x')\" --repeat 2 --persist");
    }

    static void Main(string[] args)
    {
        // Global handlers: capture any unhandled exceptions and persist a small JSON for post-mortem.
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            try { var ex = e.ExceptionObject as Exception; SaveUnhandled(ex, args); } catch { }
        };
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            try { SaveUnhandled(e.Exception, args); } catch { }
        };

        if (args == null || args.Length == 0) { PrintUsage(); return; }

        string file = null;
        string code = null;
        int repeat = 1;
        bool persist = false;
        bool show = false;
        bool doExport = false;
        bool doImport = false;
        bool clear = false;
        bool computeHash = false;
        string importJson = null;

        string suiteDir = null;
        string storePath = null;
        int threshold = 2;
        int suiteRepeat = 1;
        string findStoreRoot = null;
        bool copyFoundStore = false;
        bool assertMode = false;
        int pageThreshold = 5;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
                if (a == "--file" || a == "-f") { if (i + 1 < args.Length) { file = args[++i]; } }
            else if (a == "--eval" || a == "-e") { if (i + 1 < args.Length) { code = args[++i]; } }
            else if (a == "--eval-file" || a == "--evalfile" || a == "-F") { if (i + 1 < args.Length) { var p = args[++i]; try { code = File.ReadAllText(p); } catch (Exception ex) { Console.WriteLine("Failed to read eval-file: " + ex.Message); return; } } }
            else if (a == "--repeat" || a == "-r") { if (i + 1 < args.Length) int.TryParse(args[++i], out repeat); }
            else if (a == "--persist" || a == "-p") { persist = true; }
            else if (a == "show") { show = true; }
            else if (a == "export") { doExport = true; }
            else if (a == "import") { doImport = true; }
            else if (a == "clear") { clear = true; }
            else if (a == "hash") { computeHash = true; if (i + 1 < args.Length) code = args[++i]; }
            else if (a == "--suite" || a == "-S") { if (i + 1 < args.Length) suiteDir = args[++i]; }
            else if (a == "--store") { if (i + 1 < args.Length) storePath = args[++i]; }
            else if (a == "--threshold") { if (i + 1 < args.Length) int.TryParse(args[++i], out threshold); }
            else if (a == "--persist") { persist = true; }
            else if (a == "--repeat") { if (i + 1 < args.Length) int.TryParse(args[++i], out suiteRepeat); }
            else if (a == "--find-store") { if (i + 1 < args.Length) findStoreRoot = args[++i]; }
            else if (a == "--copy-store") { copyFoundStore = true; }
            else if (a == "--assert") { assertMode = true; }
            else if (a == "--page-threshold") { if (i + 1 < args.Length) int.TryParse(args[++i], out pageThreshold); }
            else { Console.WriteLine("Unknown arg: " + a); PrintUsage(); return; }
        }

        if (show)
        {
            var list = string.IsNullOrWhiteSpace(storePath) ? LoadStore() : LoadStoreFromPath(storePath);
            Console.WriteLine($"Bad-hash store entries: {list.Count}");
            foreach (var r in list.OrderByDescending(x => x.Count))
            {
                Console.WriteLine($"Hash={r.Hash} Count={r.Count} First={r.FirstSeen:o} Last={r.LastSeen:o} Preview={TrimPreview(r.Preview)}");
            }
            return;
        }

        if (doExport)
        {
            var list = string.IsNullOrWhiteSpace(storePath) ? LoadStore() : LoadStoreFromPath(storePath);
            Console.WriteLine(JsonConvert.SerializeObject(list, Formatting.Indented));
            return;
        }

        if (doImport)
        {
            try
            {
                importJson = Console.In.ReadToEnd();
                if (string.IsNullOrWhiteSpace(importJson)) { Console.WriteLine("No input on stdin"); return; }
                var list = JsonConvert.DeserializeObject<List<BadHashRecord>>(importJson);
                if (list == null) { Console.WriteLine("Failed to parse JSON"); return; }
                if (!string.IsNullOrWhiteSpace(storePath)) SaveStoreToPath(list, storePath);
                else SaveStore(list);
                Console.WriteLine($"Imported {list.Count} records and saved to " + (string.IsNullOrWhiteSpace(storePath) ? StoreFilePath() : storePath));
            }
            catch (Exception ex) { Console.WriteLine("Import failed: " + ex.Message); }
            return;
        }

        if (clear)
        {
            try { var p = string.IsNullOrWhiteSpace(storePath) ? StoreFilePath() : storePath; if (File.Exists(p)) File.Delete(p); Console.WriteLine("Cleared store: " + p); } catch (Exception ex) { Console.WriteLine("Clear failed: " + ex.Message); }
            return;
        }

        if (computeHash && !string.IsNullOrEmpty(code))
        {
            var h = ComputeHash(code);
            Console.WriteLine($"Hash decimal={h} hex=0x{h:X8}");
            return;
        }

        if (!string.IsNullOrEmpty(file))
        {
            try
            {
                var fileContent = File.ReadAllText(file);
                Console.WriteLine($"=== Running module file: {file} ({fileContent.Length} chars) ===");
                try
                {
                    var module = new Module(fileContent);
                    module.Run();
                    Console.WriteLine("OK");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
                }
            }
            catch (Exception ex) { Console.WriteLine("Failed to read file: " + ex.Message); }
            return;
        }

        if (!string.IsNullOrEmpty(code))
        {
            int failCount = 0;
            object lastResult = null;
            for (int i = 0; i < Math.Max(1, repeat); i++)
            {
                try
                {
                    var ctx = new Context();
                    // Wrap as IIFE to mirror how inline handlers are executed
                    var wrapped = "(function(){" + code + "})()";
                    var val = ctx.Eval(wrapped);
                    lastResult = val != null ? val.ToString() : "(undefined)";
                    Console.WriteLine($"Run #{i + 1}: OK -> {lastResult}");
                }
                catch (Exception ex)
                {
                    failCount++;
                    Console.WriteLine($"Run #{i + 1}: ERROR -> {ex.GetType().Name}: {ex.Message}");
                }
            }

            var h = ComputeHash(code);
            Console.WriteLine($"Summary: hash(decimal)={h} hash(hex)=0x{h:X8} runs={Math.Max(1, repeat)} fails={failCount}");

            if (persist && failCount > 0)
            {
                var list = LoadStore();
                var hk = h.ToString();
                var rec = list.FirstOrDefault(x => x.Hash == hk);
                if (rec == null)
                {
                    rec = new BadHashRecord { Hash = hk, Preview = TrimPreview(code), Count = failCount, FirstSeen = DateTime.UtcNow, LastSeen = DateTime.UtcNow };
                    list.Add(rec);
                }
                else
                {
                    rec.Count += failCount;
                    rec.LastSeen = DateTime.UtcNow;
                    if (string.IsNullOrWhiteSpace(rec.Preview)) rec.Preview = TrimPreview(code);
                }
                SaveStore(list);
                Console.WriteLine($"Persisted hash {hk} with total count={rec.Count} to {StoreFilePath()}");
            }

            return;
        }

        if (!string.IsNullOrEmpty(suiteDir))
        {
            // If requested, try to find a js_bad_hashes.json in the workspace/tree
            // If the user didn't specify --store or --find-store, automatically look in Pictures/MediaExplorer
            // because snapshots are saved there by the UWP app and it's accessible from a non-UWP process.
            if (string.IsNullOrWhiteSpace(findStoreRoot) && string.IsNullOrWhiteSpace(storePath))
            {
                try
                {
                    var picsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "MediaExplorer");
                    if (Directory.Exists(picsRoot))
                    {
                        findStoreRoot = picsRoot;
                        copyFoundStore = true; // auto-copy when found in Pictures
                        Console.WriteLine("Auto-detected Pictures MediaExplorer store root: " + findStoreRoot);
                    }
                }
                catch (Exception ex) { Console.WriteLine("Auto-find failed: " + ex.Message); }
            }

            string foundStore = null;
            if (!string.IsNullOrWhiteSpace(findStoreRoot))
            {
                try { foundStore = FindLatestStoreInRoot(findStoreRoot); } catch (Exception ex) { Console.WriteLine("Find-store failed: " + ex.Message); }
                if (foundStore != null) Console.WriteLine("Found store: " + foundStore);
            }

            if (copyFoundStore && !string.IsNullOrWhiteSpace(foundStore))
            {
                try { File.Copy(foundStore, StoreFilePath(), true); Console.WriteLine("Copied store to " + StoreFilePath()); } catch (Exception ex) { Console.WriteLine("Copy failed: " + ex.Message); }
            }

            // Prefer explicit --store, otherwise use foundStore if available
            var effectiveStore = storePath ?? foundStore;
            RunSuite(suiteDir, effectiveStore, threshold, persist, suiteRepeat, pageThreshold, assertMode);
            return;
        }

        PrintUsage();
        return;
    }

    static void SaveUnhandled(Exception ex, string[] args)
    {
        try
        {
            var outObj = new Dictionary<string, object>();
            outObj["Time"] = DateTime.UtcNow;
            try { outObj["Type"] = ex?.GetType().Name ?? "(null)"; } catch { outObj["Type"] = "(unknown)"; }
            try { outObj["Message"] = ex?.Message ?? ""; } catch { outObj["Message"] = "(failed to get message)"; }
            try { outObj["Stack"] = ex?.StackTrace ?? ""; } catch { outObj["Stack"] = "(failed to get stack)"; }
            try { outObj["Inner"] = ex?.InnerException != null ? ex.InnerException.Message : null; } catch { }
            try { outObj["Args"] = args ?? new string[0]; } catch { }
            var json = JsonConvert.SerializeObject(outObj, Formatting.Indented);
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "niljstest_unhandled.json");
            File.WriteAllText(path, json);
        }
        catch { }
    }

    static string TrimPreview(string code)
    {
        if (string.IsNullOrEmpty(code)) return "";
        var p = code.Replace('\r', ' ').Replace('\n', ' ');
        if (p.Length > 160) return p.Substring(0, 160);
        return p;
    }

    static string Truncate(string s, int max)
    {
        if (s == null) return null;
        if (s.Length <= max) return s;
        try { return s.Substring(0, max); } catch { return s; }
    }
}
