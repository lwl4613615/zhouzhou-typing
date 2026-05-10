using System;
using System.Linq;
using System.Text.RegularExpressions;
using newgdq.Models;

namespace newgdq.Services
{
    /// <summary>
    /// 发文核心：根据 SendingState 算出"下一段"文本。
    /// 与原版 FormType.SendAOnce 等价，去掉所有 QQ 发送相关分支（始终独练）。
    /// </summary>
    public class SendingService
    {
        // 标点判断（与原版一致）
        private static readonly Regex IsDot = new Regex(@"[\u4e00-\u9fa50-9a-zA-Z]");

        public SendingState State { get; } = new SendingState();
        private readonly Random _rnd = new Random();

        /// <summary>
        /// 算出"下一段"文本。如果文已发空返回 null。
        /// </summary>
        public string NextSegment()
        {
            var s = State;
            if (!s.Active || string.IsNullOrEmpty(s.FullText)) return null;

            switch (s.Type)
            {
                case SendingTextType.Word:
                    return NextWord();
                case SendingTextType.Single:
                    return s.IsRandom ? NextSingleRandom() : NextSequential();
                case SendingTextType.Article:
                    return s.OneSentenceEnd ? NextOneSentence() : NextSequential();
                default:
                    return NextSequential();
            }
        }

        // ===== 词组模式 =====
        private string NextWord()
        {
            var s = State;
            if (s.Words == null || s.Words.Length == 0) return null;
            int n = Math.Min(s.CountPerSeg, s.Words.Length);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(s.WordSep);
                sb.Append(s.Words[_rnd.Next(s.Words.Length)]);
            }
            s.SentSeg++;
            return sb.ToString();
        }

        // ===== 单字 - 乱序 =====
        private string NextSingleRandom()
        {
            var s = State;
            string pool = s.RandomNoRepeat ? s.PoolText : s.FullText;
            int len = pool.Length;
            if (len == 0)
            {
                // 重置池
                if (s.RandomNoRepeat) { s.PoolText = s.FullText; pool = s.PoolText; len = pool.Length; }
                if (len == 0) return null;
            }

            int take = Math.Min(s.CountPerSeg, len);
            var idxs = TextProcessor.GetRandomUnrepeatArray(len, take, _rnd);
            var sb = new System.Text.StringBuilder(take);
            foreach (int idx in idxs) sb.Append(pool[idx]);

            if (s.RandomNoRepeat)
            {
                // 移除已用字符
                var taken = new System.Collections.Generic.HashSet<int>(idxs);
                var sb2 = new System.Text.StringBuilder(len - take);
                for (int i = 0; i < len; i++) if (!taken.Contains(i)) sb2.Append(pool[i]);
                s.PoolText = sb2.ToString();
            }
            s.SentSeg++;
            return sb.ToString();
        }

        // ===== 顺序模式（单字 + 文章共用） =====
        private string NextSequential()
        {
            var s = State;
            int total = s.FullText.Length;
            if (s.Mark >= total) return null;
            int take = Math.Min(s.CountPerSeg, total - s.Mark);
            string seg = s.FullText.Substring(s.Mark, take);
            s.Mark += take;
            s.SentSeg++;
            return seg;
        }

        // ===== 文章 - 一句结束 =====
        private string NextOneSentence()
        {
            var s = State;
            int total = s.FullText.Length;
            if (s.Mark >= total) return null;

            int now = s.Mark + s.CountPerSeg;
            if (now >= total)
            {
                string seg = s.FullText.Substring(s.Mark, total - s.Mark);
                s.Mark = total;
                s.SentSeg++;
                return seg;
            }

            // 末字不是汉字/数字/字母时，往后找标点结尾
            int textlen = s.CountPerSeg;
            string lastChar = s.FullText.Substring(now - 1, 1);
            if (IsDot.IsMatch(lastChar))
            {
                // 末字是字符 → 往后扩到非字符的标点为止
                int searchEnd = Math.Min(now + 50, total);
                for (int i = now; i < searchEnd; i++)
                {
                    string ch = s.FullText.Substring(i, 1);
                    if (!IsDot.IsMatch(ch))
                    {
                        // 处理连体标点（与原版一致）
                        if (i + 1 < total)
                        {
                            string nxt = s.FullText.Substring(i + 1, 1);
                            if ((ch == "。" && nxt == "”") ||
                                (ch == "”" && nxt == "。") ||
                                (ch == "—" && nxt == "—") ||
                                (ch == "…" && nxt == "…") ||
                                (ch == "：" && nxt == "“"))
                                i++;
                        }
                        textlen = i - s.Mark + 1;
                        break;
                    }
                }
            }
            string segment = s.FullText.Substring(s.Mark, textlen);
            s.Mark += textlen;
            s.SentSeg++;
            return segment;
        }

        /// <summary>初始化新文段。</summary>
        public void Begin(string fullText, string title, SendingTextType type)
        {
            var s = State;
            s.Active = true;
            s.FullText = fullText ?? "";
            s.PoolText = s.FullText;
            s.Title = string.IsNullOrEmpty(title) ? "-" : title;
            s.Type = type;
            s.SentSeg = 0;
            s.Mark = 0;
            // 词组模式下分词
            if (type == SendingTextType.Word)
            {
                s.Words = s.SplitMode == WordSplitMode.ByLine
                    ? fullText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    : fullText.Split(new[] { s.OtherSplit }, StringSplitOptions.RemoveEmptyEntries);
            }
        }

        /// <summary>停止发文。</summary>
        public void Stop()
        {
            State.Active = false;
        }
    }
}
