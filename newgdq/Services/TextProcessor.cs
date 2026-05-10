using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace newgdq.Services
{
    /// <summary>
    /// 文段处理工具：识别类型、剔除空格、英标转中标、随机抽取等。
    /// </summary>
    public static class TextProcessor
    {
        private static readonly Regex CnPunct = new Regex(@"，|。|！|…|：|""|""|？");

        /// <summary>识别文段类型：含中文标点视为"文章"，否则视为"单字"。</summary>
        public static bool IsArticle(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return CnPunct.IsMatch(text);
        }

        /// <summary>剔除全角/半角空格、回车、Tab。</summary>
        public static string TickBlock(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace(" ", "")
                       .Replace("\u3000", "")
                       .Replace("\r", "")
                       .Replace("\n", "")
                       .Replace("\t", "");
        }

        /// <summary>用 sep 替换空格/换行（"标点填充"模式）。</summary>
        public static string FillWith(string text, string sep)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace(" ", sep)
                       .Replace("\u3000", sep)
                       .Replace("\r\n", sep)
                       .Replace("\r", sep)
                       .Replace("\n", sep);
        }

        /// <summary>英文标点换中文标点。</summary>
        public static string En2Cn(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace(",", "，")
                       .Replace(".", "。")
                       .Replace("!", "！")
                       .Replace("?", "？")
                       .Replace(":", "：")
                       .Replace(";", "；")
                       .Replace("(", "（").Replace(")", "）");
        }

        /// <summary>从 [0, max) 随机抽 count 个不重复整数。</summary>
        public static int[] GetRandomUnrepeatArray(int max, int count, Random rnd = null)
        {
            if (count > max) count = max;
            rnd = rnd ?? new Random((int)DateTime.Now.Ticks);
            var pool = new List<int>(max);
            for (int i = 0; i < max; i++) pool.Add(i);
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                int idx = rnd.Next(pool.Count);
                result[i] = pool[idx];
                pool.RemoveAt(idx);
            }
            return result;
        }
    }
}
