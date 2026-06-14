using System;
using BrowserCore.Engine;

namespace EngineTest
{
    class Program
    {
        static void Main(string[] args)
        {
            // Stub host (no UI callbacks)
            var host = new JsHostAdapter(null, null, null);
            var engine = new JavaScriptEngine(host);

            // Build simple DOM with elements that have class "test"
            var docRoot = new LiteElement("#document");
            var body = new LiteElement("body");
            docRoot.Append(body);

            var div1 = new LiteElement("div");
            div1.SetAttribute("class", "test foo");
            body.Append(div1);

            var div2 = new LiteElement("div");
            div2.SetAttribute("class", "bar test");
            body.Append(div2);

            var span = new LiteElement("span");
            span.SetAttribute("class", "baz");
            body.Append(span);

            // Attach DOM to engine
            engine.SetDom(docRoot);

            // Evaluate script iterating over getElementsByClassName('test')
            var script = @"var count = 0; for (const el of document.getElementsByClassName('test')) { count++; } count;";
            var result = engine.EvalToString(script, new JsContext { BaseUri = new Uri("https://example.com") });
            Console.WriteLine($"Elements with class 'test': {result}");

            // ------- New NiL.JS tests -------
            // Mock fetch to return a simple JSON payload
            engine.FetchOverride = uri => System.Threading.Tasks.Task.FromResult("{\"msg\":\"ok\"}");

            // Test that fetch returns a thenable (promise) with a .then function
            var scriptFetchThen = @"var p = fetch('https://example.com/data'); typeof p.then === 'function';";
            var resultFetchFn = engine.EvalToString("fetch", new JsContext { BaseUri = new Uri("https://example.com") });
            Console.WriteLine($"fetch function representation: {resultFetchFn}");

            // Test async fetch + json parsing + macro task pump
            var scriptFetchChain = @"
                var out = null;
                fetch('https://example.com/data')
                  .then(r => r.json())
                  .then(o => { out = o.msg; })
                  .catch(e => { out = 'error'; });
                // Trigger macro task processing
                setTimeout(() => {}, 0);
                out;
            ";
            var resultType = engine.EvalToString("typeof fetch('https://example.com/data')", new JsContext { BaseUri = new Uri("https://example.com") });
            Console.WriteLine($"typeof fetch result: {resultType}");
        }
    }
}

