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
        }
    }
}

