using System;
using System.Text;

namespace BrowserCore.Engine
{
    /// <summary>
    /// Detects character encoding from byte content via BOM or &lt;meta charset&gt; tags.
    /// </summary>
    public static class CharsetDetector
    {
        /// <summary>
        /// Last successfully detected charset name (e.g., "WINDOWS-1251"). Set by DetectEncoding.
        /// </summary>
        public static string LastDetectedCharset { get; private set; } = "UTF-8";

        /// <summary>
        /// Detect charset from raw bytes. Checks BOM first, then scans first 2KB for meta charset.
        /// Returns detected Encoding or null if UTF-8 default should be used.
        /// </summary>
        public static Encoding DetectEncoding(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;

            // 1. Check BOM (Byte Order Mark)
            var bomEncoding = DetectBom(bytes);
            if (bomEncoding != null) { LastDetectedCharset = bomEncoding.WebName.ToUpperInvariant(); return bomEncoding; }

            // 2. Scan first 2KB for <meta charset="..."> or <meta http-equiv="Content-Type" content="...;charset=...">
            int scanLen = Math.Min(bytes.Length, 2048);
            string head = TryDecodeAscii(bytes, scanLen);
            if (string.IsNullOrEmpty(head)) return null;

            var enc = DetectMetaCharset(head);
            if (enc != null) LastDetectedCharset = enc.WebName.ToUpperInvariant();
            return enc;
        }

        /// <summary>
        /// Detect charset from raw bytes, with fallback to UTF-8.
        /// </summary>
        public static Encoding DetectEncodingOrDefault(byte[] bytes)
        {
            return DetectEncoding(bytes) ?? Encoding.UTF8;
        }

        /// <summary>
        /// Get charset name from raw bytes (for document.charset property).
        /// </summary>
        public static string DetectCharsetName(byte[] bytes)
        {
            DetectEncoding(bytes);
            return LastDetectedCharset;
        }

        static Encoding DetectBom(byte[] bytes)
        {
            // UTF-8 BOM: EF BB BF
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8;

            // UTF-16 LE BOM: FF FE
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode;

            // UTF-16 BE BOM: FE FF
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode;

            // UTF-32 LE BOM: FF FE 00 00
            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
                return Encoding.UTF32;

            return null;
        }

        static Encoding DetectMetaCharset(string head)
        {
            string lower = head.ToLowerInvariant();
            string charset = null;

            // Pattern 1: <meta charset="windows-1251">
            int metaCharsetIdx = lower.IndexOf("<meta charset=");
            if (metaCharsetIdx >= 0)
            {
                int quoteStart = lower.IndexOf('"', metaCharsetIdx + 14);
                if (quoteStart < 0) quoteStart = lower.IndexOf('\'', metaCharsetIdx + 14);
                if (quoteStart >= 0)
                {
                    char quote = head[quoteStart];
                    int quoteEnd = head.IndexOf(quote, quoteStart + 1);
                    if (quoteEnd > quoteStart)
                        charset = head.Substring(quoteStart + 1, quoteEnd - quoteStart - 1).Trim();
                }
            }

            // Pattern 2: <meta http-equiv="Content-Type" content="text/html; charset=windows-1251">
            if (string.IsNullOrEmpty(charset))
            {
                int httpEquivIdx = lower.IndexOf("http-equiv=");
                if (httpEquivIdx >= 0 && lower.IndexOf("content-type", httpEquivIdx) >= 0)
                {
                    int contentIdx = lower.IndexOf("content=", httpEquivIdx);
                    if (contentIdx >= 0)
                    {
                        int qStart = lower.IndexOf('"', contentIdx + 8);
                        if (qStart < 0) qStart = lower.IndexOf('\'', contentIdx + 8);
                        if (qStart >= 0)
                        {
                            char q = head[qStart];
                            int qEnd = head.IndexOf(q, qStart + 1);
                            if (qEnd > qStart)
                            {
                                string contentVal = head.Substring(qStart + 1, qEnd - qStart - 1).ToLowerInvariant();
                                int csIdx = contentVal.IndexOf("charset=");
                                if (csIdx >= 0)
                                    charset = contentVal.Substring(csIdx + 8).Trim().TrimEnd(';');
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(charset)) return null;
            return ResolveEncoding(charset);
        }

        static Encoding ResolveEncoding(string charset)
        {
            charset = charset.Trim().ToLowerInvariant().Replace("\"", "").Replace("'", "");

            // Normalize common aliases
            if (charset == "iso-8859-1" || charset == "latin1" || charset == "latin-1")
                return Encoding.GetEncoding("iso-8859-1");

            if (charset == "koi8-r")
                return GetEncodingSafe("koi8-r");

            if (charset == "windows-1251" || charset == "cp1251" || charset == "win-1251")
                return GetEncodingSafe("windows-1251");

            if (charset == "windows-1252" || charset == "cp1252")
                return GetEncodingSafe("windows-1252");

            if (charset == "gb2312" || charset == "gbk" || charset == "gb18030")
                return GetEncodingSafe("gb18030");

            if (charset == "shift_jis" || charset == "shift-jis" || charset == "sjis" || charset == "x-sjis")
                return GetEncodingSafe("shift_jis");

            if (charset == "euc-kr" || charset == "euckr" || charset == "ks_c_5601-1987")
                return GetEncodingSafe("euc-kr");

            // Try direct name
            return GetEncodingSafe(charset);
        }

        static Encoding GetEncodingSafe(string name)
        {
            try
            {
                // Register CodePages provider for non-UTF8 encodings on UWP
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            }
            catch { /* already registered or not available */ }

            try
            {
                return Encoding.GetEncoding(name);
            }
            catch
            {
                return null;
            }
        }

        static string TryDecodeAscii(byte[] bytes, int length)
        {
            try
            {
                // Decode as ASCII for meta tag scanning (ASCII is subset of most encodings)
                var ascii = Encoding.ASCII.GetString(bytes, 0, length);
                return ascii;
            }
            catch { return null; }
        }
    }
}
