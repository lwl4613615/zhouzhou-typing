using System.Collections.Generic;
using System.Linq;

namespace newgdq.Services
{
    /// <summary>五笔86版一个字母键承载的字根信息。</summary>
    public sealed class WubiKey
    {
        public char Key { get; }           // 'g'
        public string Code { get; }        // "11" 区位
        public string Name { get; }        // 键名字根（如 "王"），排在首位、与字键同形
        public IReadOnlyList<string> Radicals { get; }  // 该键全部练习字根（含键名）
        public IReadOnlyList<string> Special { get; }   // 特殊/易忘字根（高亮标记）
        public string Mnemonic { get; }    // 助记口诀

        public WubiKey(char key, string code, string name, string[] radicals, string[] special, string mnemonic)
        {
            Key = key; Code = code; Name = name;
            Radicals = radicals;
            Special = special ?? new string[0];
            Mnemonic = mnemonic;
        }

        public bool IsSpecial(string radical) => Special.Contains(radical);
    }

    /// <summary>
    /// 五笔字型 86 版 字根 → 键位映射（键位练习用）。
    /// 数据按公开的 86 版字根总表与助记口诀整理。每键首位为"键名字根"，
    /// Special 标出形态特殊 / 初学易忘的字根（如 草字头、提手旁、宝盖、走之等）。
    /// </summary>
    public static class WubiRadicalTable
    {
        public static IReadOnlyList<WubiKey> Keys { get; }
        private static readonly Dictionary<char, WubiKey> _byKey = new Dictionary<char, WubiKey>();

        static WubiRadicalTable()
        {
            Keys = new List<WubiKey>
            {
                // 第一区 横起（GFDSA）
                new WubiKey('g', "11", "王", new[]{"王","一","五","戋","圭"}, new[]{"戋","圭"}, "王旁青头戋五一"),
                new WubiKey('f', "12", "土", new[]{"土","士","二","干","十","寸","雨","革"}, new[]{"雨","革"}, "土士二干十寸雨"),
                new WubiKey('d', "13", "大", new[]{"大","犬","三","古","石","厂","羊","镸"}, new[]{"厂","羊","镸"}, "大犬三羊古石厂"),
                new WubiKey('s', "14", "木", new[]{"木","丁","西","覀"}, new[]{"西","覀"}, "木丁西"),
                new WubiKey('a', "15", "工", new[]{"工","戈","弋","艹","廿","廾","匚","七"}, new[]{"弋","艹","廿","廾","匚"}, "工戈草头右框七"),

                // 第二区 竖起（HJKLM）
                new WubiKey('h', "21", "目", new[]{"目","丨","上","止","卜","卢","皮"}, new[]{"丨","卜","止","卢"}, "目具上止卜虎皮"),
                new WubiKey('j', "22", "日", new[]{"日","曰","早","虫","刂","川"}, new[]{"曰","虫","刂"}, "日早两竖与虫依"),
                new WubiKey('k', "23", "口", new[]{"口","川"}, new[]{"川"}, "口与川，字根稀"),
                new WubiKey('l', "24", "田", new[]{"田","甲","四","罒","车","力","皿","囗"}, new[]{"四","罒","皿","囗"}, "田甲方框四车力"),
                new WubiKey('m', "25", "山", new[]{"山","由","贝","几","冂","冎"}, new[]{"几","冂","冎"}, "山由贝，下框几"),

                // 第三区 撇起（TREWQ）
                new WubiKey('t', "31", "禾", new[]{"禾","竹","彳","攵","夂","丿"}, new[]{"彳","攵","夂","丿"}, "禾竹一撇双人立，反文条头共三一"),
                new WubiKey('r', "32", "白", new[]{"白","手","扌","斤","丿"}, new[]{"扌","斤"}, "白手看头三二斤"),
                new WubiKey('e', "33", "月", new[]{"月","彡","乃","用","豕","衣","爫"}, new[]{"彡","乃","豕","衣","爫"}, "月彡乃用家衣底"),
                new WubiKey('w', "34", "人", new[]{"人","亻","八","癶"}, new[]{"癶"}, "人和八，三四里"),
                new WubiKey('q', "35", "金", new[]{"金","钅","勹","鱼","儿","夕","犭","乂"}, new[]{"钅","勹","犭","夕","乂"}, "金勺缺点无尾鱼，犬旁留儿一点夕"),

                // 第四区 捡起（YUIOP）
                new WubiKey('y', "41", "言", new[]{"言","讠","文","方","广","亠","丶"}, new[]{"广","亠","丶"}, "言文方广在四一"),
                new WubiKey('u', "42", "立", new[]{"立","辛","冫","丷","六","门","痒","丬"}, new[]{"冫","丷","痒","丬"}, "立辛两点六门病"),
                new WubiKey('i', "43", "水", new[]{"水","氵","小","⺌"}, new[]{"氵","⺌"}, "水旁兴头小倒立"),
                new WubiKey('o', "44", "火", new[]{"火","业","灬","米"}, new[]{"灬","业"}, "火业头，四点米"),
                new WubiKey('p', "45", "之", new[]{"之","辶","廴","宀","冖","礻","衤"}, new[]{"辶","廴","宀","冖","礻","衤"}, "之宝盖，摘礻衤"),

                // 第五区 折起（NBVCX）
                new WubiKey('n', "51", "已", new[]{"已","巳","己","尸","心","忼","羽","乙","乚"}, new[]{"尸","忼","羽","乙","乚"}, "已半巳满不出己，左框折尸心和羽"),
                new WubiKey('b', "52", "子", new[]{"子","孑","了","也","卩","陁","耳","巜"}, new[]{"孑","卩","陁","耳","巜"}, "子耳了也框向上"),
                new WubiKey('v', "53", "女", new[]{"女","刀","九","臼","彐","巛"}, new[]{"臼","彐","巛"}, "女刀九臼山朝西"),
                new WubiKey('c', "54", "又", new[]{"又","巴","马","厶"}, new[]{"厶"}, "又巴马，丢矢矣"),
                new WubiKey('x', "55", "纟", new[]{"纟","幺","母","弓","匕"}, new[]{"幺","母","匕"}, "慈母无心弓和匕，幼无力"),
            };
            foreach (var k in Keys) _byKey[k.Key] = k;
        }

        public static WubiKey Get(char key)
            => _byKey.TryGetValue(key, out var k) ? k : null;

        /// <summary>键盘渲染用：该键字根串（空格分隔）。Z 等无字根键返回空串。</summary>
        public static string LabelFor(char key)
            => _byKey.TryGetValue(key, out var k) ? string.Join(" ", k.Radicals) : string.Empty;

        /// <summary>某字根在该键上是否为特殊/易忘字根。</summary>
        public static bool IsSpecial(char key, string radical)
            => _byKey.TryGetValue(key, out var k) && k.IsSpecial(radical);

        /// <summary>部件类字根的释义（出题时附注，避免把部件误读成整字）。</summary>
        private static readonly Dictionary<string, string> _notes = new Dictionary<string, string>
        {
            { "革", "革字底（革的下部）" },
            { "冎", "骨字头（骨的上部）" },
            { "爫", "爪字头（爱的上部）" },
            { "镸", "长字旁（長）" },
            { "覀", "西字头（覆的上部）" },
            { "廾", "弄字底" },
            { "丨", "竖" },
            { "罒", "四字头 / 网字头" },
            { "乂", "叉（义字根）" },
            { "丷", "倒八头（两点）" },
            { "⺌", "小字头（兴字头）" },
            { "孑", "独体子旁" },
            { "巜", "川的变体" },
            { "巛", "川（巡字根）" },
            { "乚", "竖弯钩" },
            { "卢", "虎字头" },
            { "亠", "六字头 / 点横头" },
            { "丬", "将字旁" },
            { "卩", "单耳旁" },
            { "阝", "双耳旁" },
            { "艹", "草字头" },
            { "扌", "提手旁" },
            { "辶", "走之" },
            { "氵", "三点水" },
            { "钅", "金字旁" },
            { "犭", "反犬旁" },
            { "礻", "示字旁" },
            { "衤", "衣字旁" },
        };

        /// <summary>取字根释义；无则返回 null。</summary>
        public static string NoteFor(string radical)
            => _notes.TryGetValue(radical, out var n) ? n : null;

        /// <summary>全部练习项：每个字根 → 其所在键。</summary>
        public static IReadOnlyList<DrillItem> BuildDrills()
        {
            var list = new List<DrillItem>();
            foreach (var k in Keys)
                foreach (var r in k.Radicals)
                    list.Add(new DrillItem(r, k.Key, false));
            return list;
        }
    }
}
