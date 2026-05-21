using System;
using System.Collections.Generic;
using Windows.UI;

namespace BrowserCore.Engine
{
    /// <summary>
    /// Backward-compatible shim for legacy CssMini API. Original implementation corrupted; replaced with
    /// thin delegating layer to current minimal parser logic in <see cref="CssParser"/>.
    /// </summary>
    public static class CssMini
    {
        /// <summary>
        /// Delegate color parsing to the maintained CssParser.
        /// </summary>
        public static Color? ParseColor(string value) => CssParser.ParseColor(value);

        /// <summary>
        /// Legacy Parse entry point. Returns an empty stylesheet placeholder while full selector/rule
        /// parsing is reintroduced incrementally.
        /// </summary>
        public static CssStylesheet Parse(string css, int sourceIndex)
        {
            return new CssStylesheet { SourceIndex = sourceIndex };
        }
    }

    /// <summary>
    /// Placeholder stylesheet type for compatibility.
    /// </summary>
    public class CssStylesheet
    {
        public int SourceIndex { get; set; }
        public List<CssRule> Rules { get; set; } = new List<CssRule>();
    }

    public class CssRule
    {
        public List<CssSelector> Selectors { get; set; } = new List<CssSelector>();
        public List<CssDecl> Decls { get; set; } = new List<CssDecl>();
        public int SourceOrder { get; set; }
    }

    public class CssSelector
    {
        public string Raw { get; set; }
        public List<Part> Parts { get; set; } = new List<Part>();

        public class Part
        {
            public string Tag { get; set; }
            public string Id { get; set; }
            public List<string> Classes { get; set; } = new List<string>();
            public List<AttrSelector> Attrs { get; set; } = new List<AttrSelector>();
            public List<Pseudo> Pseudos { get; set; } = new List<Pseudo>();
            public char? CombinatorToPrev { get; set; }

            public class AttrSelector
            {
                public string Name { get; set; }
                public string Op { get; set; } = "";
                public string Value { get; set; }
            }

            public class Pseudo
            {
                public string Name { get; set; }
                public string Arg { get; set; }
                public List<CssSelector> AnyInner { get; set; } = null;
            }
        }

        // Helpers for :nth-*
        private static bool ParseNthArgument(string arg, out int a, out int b)
        {
            a = 0; b = 0;
            if (string.IsNullOrWhiteSpace(arg)) return false;
            var s = arg.Replace(" ", "").ToLowerInvariant();
            if (s == "odd") { a = 2; b = 1; return true; }
            if (s == "even") { a = 2; b = 0; return true; }
            if (s == "n") { a = 1; b = 0; return true; }
            int posN = s.IndexOf('n');
            if (posN >= 0)
            {
                var aPart = s.Substring(0, posN);
                var bPart = s.Substring(posN + 1);
                if (aPart == "+" || aPart == "" ) a = 1;
                else if (aPart == "-") a = -1;
                else if (!int.TryParse(aPart, out a)) return false;
                if (string.IsNullOrEmpty(bPart)) { b = 0; return true; }
                if (!int.TryParse(bPart, out b)) return false;
                return true;
            }
            return int.TryParse(s, out b) ? (a = 0) == 0 : false;
        }

        private static bool MatchesAnPlusB(int index1Based, int a, int b)
        {
            if (a == 0) return index1Based == b;
            var diff = index1Based - b;
            if (a > 0) return diff >= 0 && diff % a == 0;
            return diff <= 0 && diff % a == 0;
        }

        public bool Matches(LiteElement el)
        {
            if (el == null || el.IsText) return false;
            // If we failed to parse and only have Raw, fall back to previous conservative heuristic
            if ((Parts == null || Parts.Count == 0) && !string.IsNullOrEmpty(Raw))
            {
                try
                {
                    // special-case :root to match the document root (html)
                    if (string.Equals(Raw.Trim(), ":root", StringComparison.OrdinalIgnoreCase))
                    {
                        return string.Equals(el.Tag, "html", StringComparison.OrdinalIgnoreCase);
                    }
                    // try id
                    var m = System.Text.RegularExpressions.Regex.Match(Raw, "#([A-Za-z0-9_-]+)");
                    if (m.Success)
                    {
                        string vid;
                        if (el.Attr != null && el.Attr.TryGetValue("id", out vid) && string.Equals(vid ?? "", m.Groups[1].Value, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }

                    // try class presence (any one of many classes)
                    var clsMatches = System.Text.RegularExpressions.Regex.Matches(Raw, "\\.([A-Za-z0-9_-]+)");
                    if (clsMatches.Count > 0 && el.Attr != null)
                    {
                        string cls;
                        if (el.Attr.TryGetValue("class", out cls) && !string.IsNullOrWhiteSpace(cls))
                        {
                            var set = new HashSet<string>(cls.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
                            for (int i = 0; i < clsMatches.Count; i++)
                            {
                                var name = clsMatches[i].Groups[1].Value;
                                if (set.Contains(name)) { return true; }
                            }
                        }
                    }

                    // try tag token at start
                    var mt = System.Text.RegularExpressions.Regex.Match(Raw.TrimStart(), "^([a-zA-Z0-9_-]+)");
                    if (mt.Success)
                    {
                        if (string.Equals(mt.Groups[1].Value, el.Tag, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
                catch { System.Diagnostics.Debug.WriteLine(" [Engine/CssMini.cs] empty catch empty catch"); }
                return false;
            }

            // Matching algorithm: start from the right-most part and walk the DOM according to combinators
            int idx = Parts.Count - 1;
            LiteElement current = el;
            if (!MatchSimplePart(Parts[idx], current)) return false;

            for (int p = idx; p > 0; p--)
            {
                var comb = Parts[p].CombinatorToPrev ?? ' ';
                var prevPart = Parts[p - 1];

                if (comb == ' ')
                {
                    // descendant: find an ancestor that matches
                    var anc = current.Parent;
                    bool found = false;
                    while (anc != null)
                    {
                        if (MatchSimplePart(prevPart, anc)) { found = true; current = anc; break; }
                        anc = anc.Parent;
                    }
                    if (!found) return false;
                }
                else if (comb == '>')
                {
                    var par = current.Parent;
                    if (par == null || !MatchSimplePart(prevPart, par)) return false;
                    current = par;
                }
                else if (comb == '+')
                {
                    var par = current.Parent;
                    if (par == null) return false;
                    int idxChild = par.Children.IndexOf(current);
                    if (idxChild <= 0) return false;
                    var sibling = par.Children[idxChild - 1];
                    if (!MatchSimplePart(prevPart, sibling)) return false;
                    current = sibling;
                }
                else if (comb == '~')
                {
                    var par = current.Parent;
                    if (par == null) return false;
                    int idxChild = par.Children.IndexOf(current);
                    bool found = false;
                    for (int s = idxChild - 1; s >= 0; s--)
                    {
                        var sib = par.Children[s];
                        if (MatchSimplePart(prevPart, sib)) { found = true; current = sib; break; }
                    }
                    if (!found) return false;
                }
                else
                {
                    // unknown combinator
                    return false;
                }
            }

            return true;
        }

        private bool MatchSimplePart(Part part, LiteElement el)
        {
            if (el == null) return false;
            // Tag
            if (!string.IsNullOrEmpty(part.Tag) && !string.Equals(part.Tag, "*", StringComparison.Ordinal) && !string.Equals(part.Tag, el.Tag, StringComparison.OrdinalIgnoreCase))
                return false;
            // ID
            if (!string.IsNullOrEmpty(part.Id))
            {
                string elId;
                if (el.Attr == null || !el.Attr.TryGetValue("id", out elId) || !string.Equals(elId, part.Id, StringComparison.Ordinal))
                    return false;
            }
            // Classes
            if (part.Classes.Count > 0 && (el.Attr == null || !el.Attr.ContainsKey("class")))
                return false;
            if (part.Classes.Count > 0)
            {
                string elClasses;
                if (el.Attr != null && el.Attr.TryGetValue("class", out elClasses))
                {
                    var elClassSet = new HashSet<string>(elClasses.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
                    foreach (var cls in part.Classes)
                    {
                        if (!elClassSet.Contains(cls)) return false;
                    }
                }
            }
            // Attributes
            foreach (var attr in part.Attrs)
            {
                if (attr.Op == "")
                {
                    if (el.Attr == null || !el.Attr.ContainsKey(attr.Name)) return false;
                }
                else
                {
                    string elAttrVal;
                    if (el.Attr == null || !el.Attr.TryGetValue(attr.Name, out elAttrVal)) return false;
                    switch (attr.Op)
                    {
                        case "=": if (!string.Equals(elAttrVal, attr.Value, StringComparison.Ordinal)) return false; break;
                        case "~=": // one of space-separated values
                            var parts = elAttrVal.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            bool oneMatch = false;
                            foreach (var p in parts) if (string.Equals(p, attr.Value, StringComparison.Ordinal)) { oneMatch = true; break; }
                            if (!oneMatch) return false;
                            break;
                        case "|=": // starts with or equals with dash
                            if (!string.Equals(elAttrVal, attr.Value, StringComparison.Ordinal) && !elAttrVal.StartsWith(attr.Value + "-", StringComparison.Ordinal))
                                return false;
                            break;
                        case "^=": if (!elAttrVal.StartsWith(attr.Value, StringComparison.Ordinal)) return false; break;
                        case "$=": if (!elAttrVal.EndsWith(attr.Value, StringComparison.Ordinal)) return false; break;
                        case "*=": if (elAttrVal.IndexOf(attr.Value, StringComparison.Ordinal) < 0) return false; break;
                    }
                }
            }
            // Pseudos
            foreach (var pseudo in part.Pseudos)
            {
                var name = (pseudo.Name ?? "").ToLowerInvariant();
                if (name == "first-child") { if (el.Parent == null || el.Parent.Children.IndexOf(el) != 0) return false; }
                else if (name == "last-child") { if (el.Parent == null || el.Parent.Children.IndexOf(el) != el.Parent.Children.Count - 1) return false; }
                else if (name == "nth-child") { if (el.Parent == null || !MatchesAnPlusB(el.Parent.Children.IndexOf(el) + 1, ParseNthArgument(pseudo.Arg, out int a, out int b) ? a : 0, b)) return false; }
                else if (name == "nth-of-type") { if (el.Parent == null) return false; /* TODO */ }
                else if (name == "not") { if (pseudo.AnyInner != null) foreach (var sel in pseudo.AnyInner) if (sel.Matches(el)) return false; }
                else if (name == "root") { if (el.Parent != null) return false; } // very basic implementation
            }

            return true;
        }
    }

    public sealed class CssDecl
    {
        public string Name;
        public string Value;
        public bool Important;
    }

}