using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using newgdq.Models;

namespace newgdq.Services
{
    /// <summary>
    /// 编码词典服务（读 bm.txt 格式：每行 "编码 字1 字2 ..."）。
    ///
    /// 原版用 List&lt;List&lt;string&gt;&gt; 全量扫描，O(n) 查询。
    /// 这里建反向索引：字/词 → BmEntry 列表（按重数升序），首选最常用。
    /// 同时为词组按首字符分桶，最长匹配。
    /// </summary>
    public sealed class DictionaryService
    {
        // 单字 → 首选 BmEntry（重数最小的）
        private readonly Dictionary<string, BmEntry> _single = new Dictionary<string, BmEntry>(StringComparer.Ordinal);

        // 词组按首字符分桶：首字 → 该字开头的词列表（按词长降序，便于最长匹配）
        private readonly Dictionary<char, List<BmEntry>> _phraseByHead = new Dictionary<char, List<BmEntry>>();

        public bool Loaded { get; private set; }
        public int  TotalEntries { get; private set; }

        /// <summary>从嵌入资源加载（Resources/bm.txt，UTF-8）。</summary>
        public void LoadFromResource()
        {
            var uri = new Uri("pack://application:,,,/Resources/bm.txt", UriKind.Absolute);
            var info = Application.GetResourceStream(uri);
            if (info == null) throw new FileNotFoundException("bm.txt");
            using (var sr = new StreamReader(info.Stream, Encoding.UTF8))
            {
                LoadFromReader(sr);
            }
        }

        /// <summary>从外部文件加载（用户自定义词典）。</summary>
        public void LoadFromFile(string path)
        {
            using (var sr = new StreamReader(path, Encoding.UTF8))
            {
                LoadFromReader(sr);
            }
        }

        private void LoadFromReader(TextReader reader)
        {
            _single.Clear();
            _phraseByHead.Clear();
            TotalEntries = 0;

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                string code = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string word = parts[i];
                    if (word.Length == 0) continue;
                    var entry = new BmEntry { Word = word, Code = code, Rank = i };
                    TotalEntries++;

                    if (word.Length == 1)
                    {
                        // 单字：保留重数最小（首选）
                        if (!_single.TryGetValue(word, out var cur) || entry.Rank < cur.Rank)
                            _single[word] = entry;
                    }
                    else
                    {
                        // 词组：按首字符分桶
                        char head = word[0];
                        if (!_phraseByHead.TryGetValue(head, out var list))
                        {
                            list = new List<BmEntry>();
                            _phraseByHead[head] = list;
                        }
                        list.Add(entry);
                    }
                }
            }

            // 词组列表按词长降序，方便最长匹配
            foreach (var list in _phraseByHead.Values)
                list.Sort((a, b) => b.Word.Length.CompareTo(a.Word.Length));

            Loaded = true;
        }

        /// <summary>
        /// 在文章 text 的 startIndex 处尝试最长匹配一个词；
        /// 找不到则返回单字 BmEntry；都找不到返回 null。
        /// </summary>
        public BmEntry MatchAt(string text, int startIndex)
        {
            if (!Loaded || string.IsNullOrEmpty(text) || startIndex >= text.Length) return null;

            char c = text[startIndex];
            // 先尝试词组（最长匹配）
            if (_phraseByHead.TryGetValue(c, out var phrases))
            {
                foreach (var p in phrases)
                {
                    if (startIndex + p.Word.Length <= text.Length &&
                        text.Substring(startIndex, p.Word.Length) == p.Word)
                        return p;
                }
            }
            // 单字兜底
            return _single.TryGetValue(c.ToString(), out var single) ? single : null;
        }

        /// <summary>查指定字（不查词组）。</summary>
        public BmEntry LookupChar(char c)
        {
            return _single.TryGetValue(c.ToString(), out var e) ? e : null;
        }

        /// <summary>
        /// 对整段文章做最长匹配分词，只返回长度 ≥ 2 的词组命中。
        /// </summary>
        public List<WordHit> SegmentPhrases(string text)
        {
            var hits = new List<WordHit>();
            if (!Loaded || string.IsNullOrEmpty(text)) return hits;

            int i = 0;
            while (i < text.Length)
            {
                var entry = MatchAt(text, i);
                if (entry != null && entry.Word.Length >= 2)
                {
                    hits.Add(new WordHit { Start = i, Length = entry.Word.Length });
                    i += entry.Word.Length;
                }
                else
                {
                    i++;
                }
            }
            return hits;
        }

        /// <summary>
        /// 计算文段理论码长 = 总编码长度 / 总字数。
        /// 词组按最长匹配，单字按首选编码。
        /// </summary>
        public double ComputeTheoryMc(string text)
        {
            if (!Loaded || string.IsNullOrEmpty(text)) return 0;
            int totalCode = 0;
            int totalChar = 0;
            int i = 0;
            while (i < text.Length)
            {
                var entry = MatchAt(text, i);
                if (entry == null)
                {
                    // 字典外字符（标点等）按 1 码长 1 字计
                    totalCode += 1;
                    totalChar += 1;
                    i++;
                }
                else
                {
                    totalCode += entry.Code.Length;
                    totalChar += entry.Word.Length;
                    i += entry.Word.Length;
                }
            }
            return totalChar > 0 ? (double)totalCode / totalChar : 0;
        }
    }
}
