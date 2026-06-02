using System;
using System.IO;
using System.Text;
using System.Windows;

namespace newgdq.Services
{
    /// <summary>
    /// 文章加载：内置嵌入资源 + 本地 TXT 文件（自动识别编码、容错格式问题）。
    /// </summary>
    public static class ArticleLoader
    {
        static ArticleLoader()
        {
            // 注册 GBK/GB18030 等传统中文编码（.NET Core 默认只带 UTF 系列）
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
        }

        public static string LoadInternal(string fileName)
        {
            var uri = new Uri($"pack://application:,,,/Resources/TXT/{fileName}", UriKind.Absolute);
            var info = Application.GetResourceStream(uri);
            if (info?.Stream == null) throw new FileNotFoundException(fileName);
            using (var sr = new StreamReader(info.Stream, Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        /// <summary>
        /// 从本地 TXT 文件读取正文，自动识别编码（UTF-8 BOM / UTF-16 / 无 BOM 的 UTF-8 / GBK-GB18030）。
        /// 遇到空文件、超大文件、二进制文件等格式问题抛出带中文说明的异常，便于上层提示。
        /// </summary>
        public static string LoadFromFile(string path)
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) throw new FileNotFoundException("文件不存在", path);
            if (fi.Length == 0) throw new InvalidDataException("文件是空的");
            if (fi.Length > 8L * 1024 * 1024)
                throw new InvalidDataException("文件过大（超过 8MB），可能不是普通文本");

            byte[] bytes = File.ReadAllBytes(path);

            string text;
            Encoding bomEnc = DetectBom(bytes, out int bomLen);
            if (bomEnc != null)
            {
                text = bomEnc.GetString(bytes, bomLen, bytes.Length - bomLen);
            }
            else if (TryDecodeStrictUtf8(bytes, out text))
            {
                // 无 BOM 但是合法 UTF-8
            }
            else
            {
                // 回退到 GB18030（兼容 GBK/GB2312），再不行用系统默认
                var gb = GetGbEncoding();
                text = gb != null ? gb.GetString(bytes) : Encoding.Default.GetString(bytes);
            }

            if (LooksBinary(text))
                throw new InvalidDataException("这看起来不是文本文件（含大量不可见字符）");

            // 规整换行，去掉可能残留的 BOM 字符
            text = text.Replace("\uFEFF", "").Replace("\r\n", "\n").Replace("\r", "\n");
            return text;
        }

        private static Encoding DetectBom(byte[] b, out int bomLen)
        {
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            { bomLen = 3; return new UTF8Encoding(false); }
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
            { bomLen = 2; return Encoding.Unicode; }        // UTF-16 LE
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
            { bomLen = 2; return Encoding.BigEndianUnicode; } // UTF-16 BE
            bomLen = 0; return null;
        }

        private static bool TryDecodeStrictUtf8(byte[] b, out string text)
        {
            try
            {
                var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
                text = strict.GetString(b);
                return true;
            }
            catch
            {
                text = null;
                return false;
            }
        }

        private static Encoding GetGbEncoding()
        {
            try { return Encoding.GetEncoding(54936); }      // GB18030
            catch { }
            try { return Encoding.GetEncoding(936); }        // GBK
            catch { }
            return null;
        }

        private static bool LooksBinary(string text)
        {
            int scan = Math.Min(text.Length, 4096);
            int nul = 0;
            for (int i = 0; i < scan; i++)
                if (text[i] == '\0') nul++;
            // 取样区出现超过 1% 的 NUL 视为二进制
            return scan > 0 && nul * 100 > scan;
        }
    }
}
