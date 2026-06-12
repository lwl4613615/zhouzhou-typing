using System;
using System.Collections.Generic;
using System.Text;

namespace newgdq.Services
{
    /// <summary>
    /// 慢字专项练习文本生成：把弱项排行（或本场聚合的弱项行）做成一段纯文本练习，交给主窗 LoadPracticeText 开打。
    /// 口径仿错字本 BuildDrills：弱项字取 Top≤30，每字重复 2~6 遍（按 WeakScore 高低映射遍数），目标总长约 80 字；
    /// 优先串入高分上下文短语，并混入约 20% 已掌握/低分字符防机械刷（无可混则全弱项）。本类只产文本+标题，不接 UI。
    /// </summary>
    public static class SlowCharDrillBuilder
    {
        private const int TargetLen = 80;        // 目标总长（字符数，含重复）
        private const int MaxWeak = 30;          // 弱项字 Top 上限
        private const int MinRepeat = 2;         // 每字最少遍数
        private const int MaxRepeat = 6;         // 每字最多遍数
        private const double WeakShare = 0.8;    // 弱项内容占比（其余混入易混项）

        /// <summary>从时间范围生成：取弱项排行（已掌握不入练，作为易混项）+ 高分上下文，按统一口径成文。弱项为空返回 ("", "")。</summary>
        public static (string text, string title) BuildFromRange(TimeRange range)
        {
            var all = SlowCharRepository.LoadRanking(range, hideMastered: false);   // 已按 WeakScore 倒序

            var weak = new List<SlowRankRow>();    // 入练的弱项字（排除已掌握）
            var mix = new List<string>();          // 易混项：已掌握字优先，其后接弱项榜溢出的低分字
            foreach (var r in all)
            {
                if (r == null || string.IsNullOrEmpty(r.Ch)) continue;
                if (r.Mastered) mix.Add(r.Ch);
                else weak.Add(r);
            }
            for (int i = MaxWeak; i < weak.Count; i++) mix.Add(weak[i].Ch);

            var contexts = SlowCharRepository.LoadTopContexts(range, 6);
            return Build(weak, mix, contexts, RangeSeg(range));
        }

        /// <summary>从本场已聚合的弱项行生成（不依赖库，便于"打完即练"）：Top 字入练，末尾低分字作易混项。空集返回 ("", "")。</summary>
        public static (string text, string title) BuildFromSession(IReadOnlyList<SlowRankRow> sessionTop)
        {
            if (sessionTop == null) return ("", "");

            var weak = new List<SlowRankRow>();
            foreach (var r in sessionTop)
                if (r != null && !string.IsNullOrEmpty(r.Ch)) weak.Add(r);
            weak.Sort((a, b) => b.WeakScore.CompareTo(a.WeakScore));   // 按 WeakScore 倒序
            if (weak.Count == 0) return ("", "");

            // 本场无外部"已掌握"库可混，保留末尾约 20% 低分字作易混项（字数够才留，避免把仅有的几个弱项也抽走）
            int n = weak.Count;
            int tail = n >= 5 ? Math.Max(1, (int)Math.Round(n * (1 - WeakShare))) : 0;
            var drill = new List<SlowRankRow>();
            var mix = new List<string>();
            for (int i = 0; i < n; i++)
            {
                if (i < n - tail) drill.Add(weak[i]);
                else mix.Add(weak[i].Ch);
            }
            return Build(drill, mix, null, "本场");
        }

        /// <summary>统一成文：弱项字按权重映射 2~6 遍（超核心预算才压缩），串入高分上下文，按 ~20% 比例匀撒易混项。</summary>
        private static (string text, string title) Build(
            IReadOnlyList<SlowRankRow> weak,
            IReadOnlyList<string> mixCandidates,
            IReadOnlyList<string> contexts,
            string seg)
        {
            var pool = new List<SlowRankRow>();
            if (weak != null)
                foreach (var r in weak)
                    if (r != null && !string.IsNullOrEmpty(r.Ch)) pool.Add(r);
            if (pool.Count == 0) return ("", "");

            int drillCount = Math.Min(pool.Count, MaxWeak);
            var drill = pool.GetRange(0, drillCount);

            var featured = new HashSet<string>();
            foreach (var r in drill) featured.Add(r.Ch);

            // 易混项去重、排除已在练的字
            var mix = new List<string>();
            var seen = new HashSet<string>(featured);
            if (mixCandidates != null)
                foreach (var c in mixCandidates)
                    if (!string.IsNullOrEmpty(c) && seen.Add(c)) mix.Add(c);

            bool hasMix = mix.Count > 0;
            int coreBudget = hasMix ? (int)Math.Round(TargetLen * WeakShare) : TargetLen;

            // 高分上下文优先串入：限不超过核心预算的 40%，且不挤占弱项字的最少遍数
            var sb = new StringBuilder();
            int ctxCap = Math.Min((int)(coreBudget * 0.4), Math.Max(0, coreBudget - MinRepeat * drill.Count));
            if (contexts != null)
                foreach (var ctx in contexts)
                {
                    if (string.IsNullOrWhiteSpace(ctx)) continue;
                    if (sb.Length + ctx.Length > ctxCap) continue;
                    sb.Append(ctx);
                }

            // 弱项字按 WeakScore 高低映射 2~6 遍，超出剩余核心预算才整体压缩（同字连排，便于针对性练）
            int charBudget = coreBudget - sb.Length;
            var reps = MapReps(drill, charBudget);
            for (int i = 0; i < drill.Count; i++)
                for (int r = 0; r < reps[i]; r++) sb.Append(drill[i].Ch);
            string core = sb.ToString();

            // 混入约 20% 易混项：按核心长度的 ~1/4 取量（守住 80/20 比例），循环取候选（每字最多 3 个，避免单字刷屏）
            string mixText = "";
            if (hasMix)
            {
                int want = (int)Math.Round(core.Length * (1 - WeakShare) / WeakShare);
                int mixLen = Math.Min(Math.Min(want, mix.Count * 3), Math.Max(0, TargetLen - core.Length));
                var mb = new StringBuilder();
                int idx = 0;
                while (mb.Length < mixLen)
                {
                    mb.Append(mix[idx % mix.Count]);
                    idx++;
                }
                mixText = mb.ToString();
            }

            string text = Interleave(core, mixText);
            string title = $"慢字专项练习 · {seg}Top{drill.Count}";
            return (text, title);
        }

        /// <summary>把每个弱项字映射到 [2,6] 遍：WeakScore 越高遍数越多；总遍数缩放到目标预算。全等权时退化为均匀遍数（仿错字本口径）。</summary>
        private static int[] MapReps(IReadOnlyList<SlowRankRow> rows, int budget)
        {
            int n = rows.Count;
            var reps = new int[n];
            if (n == 0) return reps;
            if (budget < MinRepeat * n) budget = MinRepeat * n;   // 保底：每字至少 MinRepeat 遍

            double maxW = rows[0].WeakScore, minW = rows[0].WeakScore;
            for (int i = 1; i < n; i++)
            {
                double w = rows[i].WeakScore;
                if (w > maxW) maxW = w;
                if (w < minW) minW = w;
            }

            if (maxW <= minW)
            {
                int uni = Clamp((int)Math.Round((double)budget / n), MinRepeat, MaxRepeat);
                for (int i = 0; i < n; i++) reps[i] = uni;
                return reps;
            }

            int sum = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (rows[i].WeakScore - minW) / (maxW - minW);   // 0..1
                reps[i] = (int)Math.Round(MinRepeat + (MaxRepeat - MinRepeat) * t);
                sum += reps[i];
            }
            if (sum > budget)   // 仅在超预算时整体压缩，保住权重梯度；不足预算则保留自然遍数
            {
                double f = (double)budget / sum;
                for (int i = 0; i < n; i++)
                    reps[i] = Clamp((int)Math.Round(reps[i] * f), MinRepeat, MaxRepeat);
            }
            return reps;
        }

        /// <summary>把易混项均匀撒进核心文本（按间隔插入），打散机械重复的节奏。</summary>
        private static string Interleave(string core, string mix)
        {
            if (string.IsNullOrEmpty(mix)) return core;
            if (string.IsNullOrEmpty(core)) return mix;
            var sb = new StringBuilder(core.Length + mix.Length);
            int step = Math.Max(1, core.Length / (mix.Length + 1));
            int mi = 0;
            for (int i = 0; i < core.Length; i++)
            {
                sb.Append(core[i]);
                if (mi < mix.Length && (i + 1) % step == 0) sb.Append(mix[mi++]);
            }
            while (mi < mix.Length) sb.Append(mix[mi++]);
            return sb.ToString();
        }

        private static string RangeSeg(TimeRange range)
        {
            switch (range)
            {
                case TimeRange.Last7:  return "近7天";
                case TimeRange.Last30: return "近30天";
                default:               return "全部";
            }
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
