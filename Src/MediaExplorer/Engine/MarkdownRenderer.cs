using System;
using System.Text;

namespace BrowserCore.Engine
{
    /// <summary>
    /// Lightweight markdown-to-HTML converter.
    /// Supports: headers, bold, italic, links, images, code blocks, inline code,
    /// unordered/ordered lists, blockquotes, horizontal rules, paragraphs.
    /// No regex — pure line-by-line + inline parsing for netstandard1.4.
    /// </summary>
    public static class MarkdownRenderer
    {
        public static string RenderToHtml(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
                return "<html><body><p>(empty document)</p></body></html>";

            var lines = markdown.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset=\"utf-8\"/>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:'Segoe UI',sans-serif;max-width:720px;margin:40px auto;padding:0 20px;line-height:1.6;color:#222;}");
            sb.AppendLine("h1{font-size:28px;border-bottom:2px solid #ddd;padding-bottom:8px;}");
            sb.AppendLine("h2{font-size:22px;border-bottom:1px solid #eee;padding-bottom:4px;}");
            sb.AppendLine("h3{font-size:18px;}");
            sb.AppendLine("code{background:#f4f4f4;padding:2px 6px;border-radius:3px;font-family:Consolas,monospace;font-size:14px;}");
            sb.AppendLine("pre{background:#f4f4f4;padding:12px;border-radius:4px;overflow-x:auto;}");
            sb.AppendLine("pre code{background:none;padding:0;}");
            sb.AppendLine("blockquote{border-left:4px solid #ccc;margin:12px 0;padding:8px 16px;color:#555;}");
            sb.AppendLine("a{color:#0366d6;text-decoration:none;}");
            sb.AppendLine("a:hover{text-decoration:underline;}");
            sb.AppendLine("img{max-width:100%;height:auto;}");
            sb.AppendLine("hr{border:none;border-top:1px solid #ddd;margin:24px 0;}");
            sb.AppendLine("ul,ol{padding-left:24px;}");
            sb.AppendLine("li{margin:4px 0;}");
            sb.AppendLine("table{border-collapse:collapse;margin:12px 0;}");
            sb.AppendLine("th,td{border:1px solid #ddd;padding:8px 12px;text-align:left;}");
            sb.AppendLine("th{background:#f8f8f8;}");
            sb.AppendLine("</style></head><body>");

            bool inCodeBlock = false;
            bool inList = false;
            bool inOrderedList = false;
            bool inBlockquote = false;
            int blankLineCount = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // Fenced code block (``` or ~~~)
                if (line.TrimStart().StartsWith("```") || line.TrimStart().StartsWith("~~~"))
                {
                    if (inCodeBlock)
                    {
                        sb.AppendLine("</code></pre>");
                        inCodeBlock = false;
                    }
                    else
                    {
                        CloseLists(sb, ref inList, ref inOrderedList);
                        if (inBlockquote) { sb.AppendLine("</blockquote>"); inBlockquote = false; }
                        sb.Append("<pre><code>");
                        inCodeBlock = true;
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    sb.AppendLine(System.Net.WebUtility.HtmlEncode(line));
                    continue;
                }

                // Blank line — close open blocks
                if (string.IsNullOrWhiteSpace(line))
                {
                    blankLineCount++;
                    CloseLists(sb, ref inList, ref inOrderedList);
                    if (inBlockquote) { sb.AppendLine("</blockquote>"); inBlockquote = false; }
                    continue;
                }

                blankLineCount = 0;

                // Horizontal rule
                if (IsHorizontalRule(line))
                {
                    CloseLists(sb, ref inList, ref inOrderedList);
                    sb.AppendLine("<hr/>");
                    continue;
                }

                // Headers
                int headerLevel = GetHeaderLevel(line);
                if (headerLevel > 0)
                {
                    CloseLists(sb, ref inList, ref inOrderedList);
                    string content = line.Substring(headerLevel).Trim();
                    sb.AppendFormat("<h{0}>{1}</h{0}>", headerLevel, ParseInline(content));
                    sb.AppendLine();
                    continue;
                }

                // Blockquote
                if (line.TrimStart().StartsWith(">"))
                {
                    CloseLists(sb, ref inList, ref inOrderedList);
                    if (!inBlockquote) { sb.AppendLine("<blockquote>"); inBlockquote = true; }
                    string content = line.TrimStart().Substring(1).Trim();
                    sb.AppendLine("<p>" + ParseInline(content) + "</p>");
                    continue;
                }
                else if (inBlockquote)
                {
                    sb.AppendLine("</blockquote>");
                    inBlockquote = false;
                }

                // Unordered list
                if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* ") || line.TrimStart().StartsWith("+ "))
                {
                    if (inOrderedList) { sb.AppendLine("</ol>"); inOrderedList = false; }
                    if (!inList) { sb.AppendLine("<ul>"); inList = true; }
                    string content = line.TrimStart().Substring(2).Trim();
                    sb.AppendLine("<li>" + ParseInline(content) + "</li>");
                    continue;
                }

                // Ordered list
                if (line.TrimStart().Length > 2 && char.IsDigit(line.TrimStart()[0]) && line.TrimStart()[1] == '.' && line.TrimStart()[2] == ' ')
                {
                    if (inList) { sb.AppendLine("</ul>"); inList = false; }
                    if (!inOrderedList) { sb.AppendLine("<ol>"); inOrderedList = true; }
                    int dotIdx = line.TrimStart().IndexOf('.');
                    string content = line.TrimStart().Substring(dotIdx + 1).Trim();
                    sb.AppendLine("<li>" + ParseInline(content) + "</li>");
                    continue;
                }

                // Close lists if not a list item
                CloseLists(sb, ref inList, ref inOrderedList);

                // Table detection (line with |)
                if (line.Contains("|") && i + 1 < lines.Length && lines[i + 1].Contains("---"))
                {
                    // Header row
                    sb.AppendLine("<table>");
                    sb.AppendLine("<tr>");
                    foreach (var cell in line.Split('|'))
                    {
                        var trimmed = cell.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            sb.AppendLine("<th>" + ParseInline(trimmed) + "</th>");
                    }
                    sb.AppendLine("</tr>");
                    // Skip separator line
                    i++;
                    // Data rows
                    while (i + 1 < lines.Length && lines[i + 1].Contains("|"))
                    {
                        i++;
                        sb.AppendLine("<tr>");
                        foreach (var cell in lines[i].Split('|'))
                        {
                            var trimmed = cell.Trim();
                            if (!string.IsNullOrEmpty(trimmed))
                                sb.AppendLine("<td>" + ParseInline(trimmed) + "</td>");
                        }
                        sb.AppendLine("</tr>");
                    }
                    sb.AppendLine("</table>");
                    continue;
                }

                // Regular paragraph
                sb.AppendLine("<p>" + ParseInline(line) + "</p>");
            }

            // Close any open blocks
            CloseLists(sb, ref inList, ref inOrderedList);
            if (inCodeBlock) sb.AppendLine("</code></pre>");
            if (inBlockquote) sb.AppendLine("</blockquote>");

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static void CloseLists(StringBuilder sb, ref bool inList, ref bool inOrderedList)
        {
            if (inList) { sb.AppendLine("</ul>"); inList = false; }
            if (inOrderedList) { sb.AppendLine("</ol>"); inOrderedList = false; }
        }

        private static int GetHeaderLevel(string line)
        {
            int count = 0;
            foreach (char c in line)
            {
                if (c == '#') count++;
                else break;
            }
            if (count >= 1 && count <= 6 && count < line.Length && line[count] == ' ')
                return count;
            return 0;
        }

        private static bool IsHorizontalRule(string line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length < 3) return false;
            char c = trimmed[0];
            if (c != '-' && c != '*' && c != '_') return false;
            int count = 0;
            foreach (char ch in trimmed)
            {
                if (ch == c) count++;
                else if (ch != ' ') return false;
            }
            return count >= 3;
        }

        /// <summary>
        /// Parse inline markdown: **bold**, *italic*, `code`, [link](url), ![img](url)
        /// </summary>
        private static string ParseInline(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                // Inline code `...`
                if (text[i] == '`')
                {
                    int end = text.IndexOf('`', i + 1);
                    if (end > i)
                    {
                        sb.Append("<code>");
                        sb.Append(System.Net.WebUtility.HtmlEncode(text.Substring(i + 1, end - i - 1)));
                        sb.Append("</code>");
                        i = end + 1;
                        continue;
                    }
                }

                // Image ![alt](url)
                if (text[i] == '!' && i + 1 < text.Length && text[i + 1] == '[')
                {
                    int closeBracket = text.IndexOf(']', i + 2);
                    if (closeBracket > i && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                    {
                        int closeParen = text.IndexOf(')', closeBracket + 2);
                        if (closeParen > closeBracket)
                        {
                            string alt = text.Substring(i + 2, closeBracket - i - 2);
                            string url = text.Substring(closeBracket + 2, closeParen - closeBracket - 2);
                            sb.AppendFormat("<img src=\"{0}\" alt=\"{1}\"/>", url, alt);
                            i = closeParen + 1;
                            continue;
                        }
                    }
                }

                // Link [text](url)
                if (text[i] == '[')
                {
                    int closeBracket = text.IndexOf(']', i + 1);
                    if (closeBracket > i && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                    {
                        int closeParen = text.IndexOf(')', closeBracket + 2);
                        if (closeParen > closeBracket)
                        {
                            string linkText = text.Substring(i + 1, closeBracket - i - 1);
                            string url = text.Substring(closeBracket + 2, closeParen - closeBracket - 2);
                            sb.AppendFormat("<a href=\"{0}\">{1}</a>", url, ParseInline(linkText));
                            i = closeParen + 1;
                            continue;
                        }
                    }
                }

                // Bold **...**
                if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int end = text.IndexOf("**", i + 2);
                    if (end > i)
                    {
                        sb.Append("<strong>");
                        sb.Append(ParseInline(text.Substring(i + 2, end - i - 2)));
                        sb.Append("</strong>");
                        i = end + 2;
                        continue;
                    }
                }

                // Italic *...*
                if (text[i] == '*' && (i + 1 < text.Length && text[i + 1] != '*'))
                {
                    int end = text.IndexOf('*', i + 1);
                    if (end > i)
                    {
                        sb.Append("<em>");
                        sb.Append(ParseInline(text.Substring(i + 1, end - i - 1)));
                        sb.Append("</em>");
                        i = end + 1;
                        continue;
                    }
                }

                // Strikethrough ~~...~~
                if (text[i] == '~' && i + 1 < text.Length && text[i + 1] == '~')
                {
                    int end = text.IndexOf("~~", i + 2);
                    if (end > i)
                    {
                        sb.Append("<del>");
                        sb.Append(ParseInline(text.Substring(i + 2, end - i - 2)));
                        sb.Append("</del>");
                        i = end + 2;
                        continue;
                    }
                }

                sb.Append(text[i]);
                i++;
            }

            return sb.ToString();
        }
    }
}
