using System.Collections.Generic;
using System.Linq;

namespace newgdq.Services
{
    /// <summary>双拼方案种类。</summary>
    public enum ShuangPinKind
    {
        Xiaohe,   // 小鹤双拼
        Ziranma   // 自然码
    }

    /// <summary>键位练习用的单个练习项：要显示的 拼音音节片段 + 应按下的键。</summary>
    public sealed class DrillItem
    {
        public string Token { get; }   // 显示给用户的内容，如 "zh"、"uang"
        public char Key { get; }       // 对应的键（小写字母）
        public bool IsInitial { get; } // true=声母，false=韵母

        public DrillItem(string token, char key, bool isInitial)
        {
            Token = token; Key = key; IsInitial = isInitial;
        }
    }

    /// <summary>
    /// 双拼方案：声母 / 韵母 在键盘上的位置映射。
    /// 数据按各方案公开的标准键位表独立整理，不复制任何第三方实现代码。
    /// 仅做"键位练习"，不处理零声母整句拆分（避免歧义）。
    /// </summary>
    public sealed class ShuangPinScheme
    {
        public ShuangPinKind Kind { get; }
        public string DisplayName { get; }

        /// <summary>特殊声母 → 键（zh/ch/sh）。单字母声母键位即其本身，无需列出。</summary>
        public IReadOnlyDictionary<char, string> KeyInitial { get; }

        /// <summary>键 → 该键承载的韵母列表。</summary>
        public IReadOnlyDictionary<char, IReadOnlyList<string>> KeyFinals { get; }

        /// <summary>全部练习项（声母 zh/ch/sh + 所有韵母）。</summary>
        public IReadOnlyList<DrillItem> Drills { get; }

        private ShuangPinScheme(ShuangPinKind kind, string name,
            Dictionary<char, string> initials,
            Dictionary<char, string[]> finals)
        {
            Kind = kind;
            DisplayName = name;
            KeyInitial = initials;
            KeyFinals = finals.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<string>)kv.Value.ToList());

            var drills = new List<DrillItem>();
            foreach (var kv in initials)             // zh/ch/sh
                drills.Add(new DrillItem(kv.Value, kv.Key, true));
            foreach (var kv in finals)
                foreach (var f in kv.Value)
                    drills.Add(new DrillItem(f, kv.Key, false));
            Drills = drills;
        }

        /// <summary>取某键上的完整标签（声母 + 韵母），用于键盘渲染。</summary>
        public string LabelFor(char key)
        {
            var parts = new List<string>();
            if (KeyInitial.TryGetValue(key, out var ini)) parts.Add(ini);
            if (KeyFinals.TryGetValue(key, out var fs)) parts.AddRange(fs);
            return string.Join(" ", parts);
        }

        // ===== 方案表 =====

        public static ShuangPinScheme Create(ShuangPinKind kind)
            => kind == ShuangPinKind.Ziranma ? Ziranma() : Xiaohe();

        private static ShuangPinScheme Xiaohe()
        {
            var initials = new Dictionary<char, string>
            {
                ['v'] = "zh", ['i'] = "ch", ['u'] = "sh",
            };
            var finals = new Dictionary<char, string[]>
            {
                ['q'] = new[] { "iu" },
                ['w'] = new[] { "ei" },
                ['e'] = new[] { "e" },
                ['r'] = new[] { "uan" },
                ['t'] = new[] { "ue" },
                ['y'] = new[] { "un" },
                ['u'] = new[] { "u" },
                ['i'] = new[] { "i" },
                ['o'] = new[] { "uo", "o" },
                ['p'] = new[] { "ie" },
                ['a'] = new[] { "a" },
                ['s'] = new[] { "ong", "iong" },
                ['d'] = new[] { "ai" },
                ['f'] = new[] { "en" },
                ['g'] = new[] { "eng" },
                ['h'] = new[] { "ang" },
                ['j'] = new[] { "an" },
                ['k'] = new[] { "uai", "ing" },
                ['l'] = new[] { "uang", "iang" },
                ['z'] = new[] { "ou" },
                ['x'] = new[] { "ua", "ia" },
                ['c'] = new[] { "ao" },
                ['v'] = new[] { "ui", "ü" },
                ['b'] = new[] { "in" },
                ['n'] = new[] { "iao" },
                ['m'] = new[] { "ian" },
            };
            return new ShuangPinScheme(ShuangPinKind.Xiaohe, "小鹤双拼", initials, finals);
        }

        private static ShuangPinScheme Ziranma()
        {
            var initials = new Dictionary<char, string>
            {
                ['v'] = "zh", ['i'] = "ch", ['u'] = "sh",
            };
            var finals = new Dictionary<char, string[]>
            {
                ['q'] = new[] { "iu" },
                ['w'] = new[] { "ia", "ua" },
                ['e'] = new[] { "e" },
                ['r'] = new[] { "uan" },
                ['t'] = new[] { "ue" },
                ['y'] = new[] { "ing", "uai" },
                ['u'] = new[] { "u" },
                ['i'] = new[] { "i" },
                ['o'] = new[] { "uo", "o" },
                ['p'] = new[] { "un" },
                ['a'] = new[] { "a" },
                ['s'] = new[] { "ong", "iong" },
                ['d'] = new[] { "iang", "uang" },
                ['f'] = new[] { "en" },
                ['g'] = new[] { "eng" },
                ['h'] = new[] { "ang" },
                ['j'] = new[] { "an" },
                ['k'] = new[] { "ao" },
                ['l'] = new[] { "ai" },
                ['z'] = new[] { "ei" },
                ['x'] = new[] { "ie" },
                ['c'] = new[] { "iao" },
                ['v'] = new[] { "ui", "ü" },
                ['b'] = new[] { "ou" },
                ['n'] = new[] { "in" },
                ['m'] = new[] { "ian" },
            };
            return new ShuangPinScheme(ShuangPinKind.Ziranma, "自然码", initials, finals);
        }
    }
}
