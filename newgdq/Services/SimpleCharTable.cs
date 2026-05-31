using System.Collections.Generic;

namespace newgdq.Services
{
    /// <summary>一个简单字练习项：汉字 + 拼音 + 规范化的声母/韵母（用于换算双拼键）。</summary>
    public sealed class SimpleChar
    {
        public string Char { get; }      // 汉字
        public string Pinyin { get; }    // 显示用拼音（无声调）
        public string Shengmu { get; }   // 声母，零声母为空串
        public string Yunmu { get; }     // 双拼规范韵母（与方案韵母表一致，y/w 介音已折算）

        public SimpleChar(string ch, string pinyin, string shengmu, string yunmu)
        {
            Char = ch; Pinyin = pinyin; Shengmu = shengmu; Yunmu = yunmu;
        }
    }

    /// <summary>
    /// 内置常用简单字 → 拼音 表（自行整理，不含任何第三方词库代码）。
    /// 韵母一律存"双拼字面形式"：零声母 y/w 音节按拼音字面去掉首字母取韵母
    /// （如 我 wo→o、也 ye→e、有 you→ou、文 wen→en），与双拼方案对零声母的拆分一致；
    /// 因此与小鹤 / 自然码 两套韵母键表均可直接换算。
    /// 含 ü 的音节（女/鱼/雨等）暂不收录，避免不同方案歧义。
    /// </summary>
    public static class SimpleCharTable
    {
        public static IReadOnlyList<SimpleChar> Items { get; } = new List<SimpleChar>
        {
            // —— 零声母 ——
            new SimpleChar("啊", "a",   "",  "a"),
            new SimpleChar("哦", "o",   "",  "o"),
            new SimpleChar("鹅", "e",   "",  "e"),
            new SimpleChar("爱", "ai",  "",  "ai"),
            new SimpleChar("安", "an",  "",  "an"),
            new SimpleChar("恩", "en",  "",  "en"),
            new SimpleChar("欧", "ou",  "",  "ou"),
            new SimpleChar("昂", "ang", "",  "ang"),

            // —— b p m f ——
            new SimpleChar("白", "bai", "b", "ai"),
            new SimpleChar("不", "bu",  "b", "u"),
            new SimpleChar("笔", "bi",  "b", "i"),
            new SimpleChar("怕", "pa",  "p", "a"),
            new SimpleChar("皮", "pi",  "p", "i"),
            new SimpleChar("朋", "peng","p", "eng"),
            new SimpleChar("马", "ma",  "m", "a"),
            new SimpleChar("米", "mi",  "m", "i"),
            new SimpleChar("门", "men", "m", "en"),
            new SimpleChar("名", "ming","m", "ing"),
            new SimpleChar("飞", "fei", "f", "ei"),
            new SimpleChar("风", "feng","f", "eng"),
            new SimpleChar("发", "fa",  "f", "a"),

            // —— d t n l ——
            new SimpleChar("大", "da",  "d", "a"),
            new SimpleChar("地", "di",  "d", "i"),
            new SimpleChar("电", "dian","d", "ian"),
            new SimpleChar("多", "duo", "d", "uo"),
            new SimpleChar("都", "dou", "d", "ou"),
            new SimpleChar("对", "dui", "d", "ui"),
            new SimpleChar("天", "tian","t", "ian"),
            new SimpleChar("土", "tu",  "t", "u"),
            new SimpleChar("听", "ting","t", "ing"),
            new SimpleChar("他", "ta",  "t", "a"),
            new SimpleChar("你", "ni",  "n", "i"),
            new SimpleChar("牛", "niu", "n", "iu"),
            new SimpleChar("鸟", "niao","n", "iao"),
            new SimpleChar("能", "neng","n", "eng"),
            new SimpleChar("来", "lai", "l", "ai"),
            new SimpleChar("力", "li",  "l", "i"),
            new SimpleChar("了", "le",  "l", "e"),
            new SimpleChar("老", "lao", "l", "ao"),
            new SimpleChar("里", "li",  "l", "i"),

            // —— g k h ——
            new SimpleChar("高", "gao", "g", "ao"),
            new SimpleChar("个", "ge",  "g", "e"),
            new SimpleChar("给", "gei", "g", "ei"),
            new SimpleChar("工", "gong","g", "ong"),
            new SimpleChar("国", "guo", "g", "uo"),
            new SimpleChar("看", "kan", "k", "an"),
            new SimpleChar("开", "kai", "k", "ai"),
            new SimpleChar("口", "kou", "k", "ou"),
            new SimpleChar("好", "hao", "h", "ao"),
            new SimpleChar("和", "he",  "h", "e"),
            new SimpleChar("火", "huo", "h", "uo"),
            new SimpleChar("花", "hua", "h", "ua"),
            new SimpleChar("会", "hui", "h", "ui"),
            new SimpleChar("还", "hai", "h", "ai"),

            // —— j q x ——
            new SimpleChar("家", "jia", "j", "ia"),
            new SimpleChar("就", "jiu", "j", "iu"),
            new SimpleChar("小", "xiao","x", "iao"),
            new SimpleChar("下", "xia", "x", "ia"),
            new SimpleChar("心", "xin", "x", "in"),
            new SimpleChar("写", "xie", "x", "ie"),
            new SimpleChar("学", "xue", "x", "ue"),
            new SimpleChar("习", "xi",  "x", "i"),
            new SimpleChar("想", "xiang","x","iang"),
            new SimpleChar("行", "xing","x", "ing"),
            new SimpleChar("去", "qu",  "q", "u"),

            // —— zh ch sh r ——
            new SimpleChar("中", "zhong","zh","ong"),
            new SimpleChar("这", "zhe", "zh","e"),
            new SimpleChar("是", "shi", "sh","i"),
            new SimpleChar("上", "shang","sh","ang"),
            new SimpleChar("说", "shuo","sh","uo"),
            new SimpleChar("水", "shui","sh","ui"),
            new SimpleChar("山", "shan","sh","an"),
            new SimpleChar("书", "shu", "sh","u"),
            new SimpleChar("少", "shao","sh","ao"),
            new SimpleChar("手", "shou","sh","ou"),
            new SimpleChar("树", "shu", "sh","u"),
            new SimpleChar("车", "che", "ch","e"),
            new SimpleChar("长", "chang","ch","ang"),
            new SimpleChar("出", "chu", "ch","u"),
            new SimpleChar("人", "ren", "r", "en"),
            new SimpleChar("日", "ri",  "r", "i"),
            new SimpleChar("入", "ru",  "r", "u"),

            // —— z c s ——
            new SimpleChar("子", "zi",  "z", "i"),
            new SimpleChar("在", "zai", "z", "ai"),
            new SimpleChar("走", "zou", "z", "ou"),
            new SimpleChar("作", "zuo", "z", "uo"),
            new SimpleChar("字", "zi",  "z", "i"),
            new SimpleChar("草", "cao", "c", "ao"),
            new SimpleChar("从", "cong","c", "ong"),
            new SimpleChar("四", "si",  "s", "i"),

            // —— y w 介音 ——
            new SimpleChar("我", "wo",  "w", "o"),
            new SimpleChar("五", "wu",  "w", "u"),
            new SimpleChar("文", "wen", "w", "en"),
            new SimpleChar("也", "ye",  "y", "e"),
            new SimpleChar("有", "you", "y", "ou"),
            new SimpleChar("要", "yao", "y", "ao"),
            new SimpleChar("用", "yong","y", "ong"),
            new SimpleChar("月", "yue", "y", "ue"),
            new SimpleChar("云", "yun", "y", "un"),
            new SimpleChar("一", "yi",  "y", "i"),

            // —— 扩充：常用高频字（同一约定，已去重、不含 ü/er）——
            new SimpleChar("的", "de",  "d", "e"),
            new SimpleChar("方", "fang","f", "ang"),
            new SimpleChar("道", "dao", "d", "ao"),
            new SimpleChar("后", "hou", "h", "ou"),
            new SimpleChar("候", "hou", "h", "ou"),
            new SimpleChar("经", "jing","j", "ing"),
            new SimpleChar("起", "qi",  "q", "i"),
            new SimpleChar("前", "qian","q", "ian"),
            new SimpleChar("情", "qing","q", "ing"),
            new SimpleChar("全", "quan","q", "uan"),
            new SimpleChar("然", "ran", "r", "an"),
            new SimpleChar("三", "san", "s", "an"),
            new SimpleChar("色", "se",  "s", "e"),
            new SimpleChar("太", "tai", "t", "ai"),
            new SimpleChar("头", "tou", "t", "ou"),
            new SimpleChar("民", "min", "m", "in"),
            new SimpleChar("年", "nian","n", "ian"),
            new SimpleChar("见", "jian","j", "ian"),
            new SimpleChar("间", "jian","j", "ian"),
            new SimpleChar("现", "xian","x", "ian"),
            new SimpleChar("先", "xian","x", "ian"),
            new SimpleChar("信", "xin", "x", "in"),
            new SimpleChar("为", "wei", "w", "ei"),
            new SimpleChar("万", "wan", "w", "an"),
            new SimpleChar("王", "wang","w", "ang"),
            new SimpleChar("言", "yan", "y", "an"),
            new SimpleChar("眼", "yan", "y", "an"),
            new SimpleChar("阳", "yang","y", "ang"),
            new SimpleChar("样", "yang","y", "ang"),
            new SimpleChar("因", "yin", "y", "in"),
            new SimpleChar("应", "ying","y", "ing"),
            new SimpleChar("早", "zao", "z", "ao"),
            new SimpleChar("怎", "zen", "z", "en"),
            new SimpleChar("总", "zong","z", "ong"),
            new SimpleChar("张", "zhang","zh","ang"),
            new SimpleChar("找", "zhao","zh","ao"),
            new SimpleChar("真", "zhen","zh","en"),
            new SimpleChar("正", "zheng","zh","eng"),
            new SimpleChar("主", "zhu", "zh","u"),
            new SimpleChar("准", "zhun","zh","un"),
        };

        /// <summary>表内"常用读音 ≠ 唯一读音"的多音字：练习时按表中给定读音打，
        /// 题面需特别标注，避免用户按另一个读音拆键被误判。</summary>
        private static readonly HashSet<string> PolyChars = new HashSet<string>
        {
            "了", "和", "还", "都", "会", "长", "行", "中",
            "上", "少", "要", "为", "地", "的", "应", "正",
        };

        /// <summary>该字是否为需提醒的多音字。</summary>
        public static bool IsPolyphonic(string ch) => ch != null && PolyChars.Contains(ch);
    }
}
