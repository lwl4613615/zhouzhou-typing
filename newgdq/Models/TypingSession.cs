using System;
using System.Collections.Generic;

namespace newgdq.Models
{
    /// <summary>
    /// 一次跟打的运行时状态。
    /// 取代原版 Glob 中的散落 static 字段，便于"重新开始/换篇"时整体重置。
    /// </summary>
    public class TypingSession
    {
        public string TypeText { get; private set; } = string.Empty;
        public string Title    { get; private set; } = string.Empty;
        /// <summary>文章是否含中文字符（含 → 用 IME 拦截规则）。</summary>
        public bool   IsCjk    { get; private set; }

        public bool     Started  { get; set; }
        public bool     Finished { get; set; }
        public DateTime StartTime;
        public DateTime? EndTime;   // 完成时刻；非 null 表示已冻结用时

        public int Keys;        // 击键数（来自键钩子）
        public int Hg;          // 回改：退格累计
        public int Cz;          // 错字（已打部分中错的字数）
        public int LeftHand;    // 左手键击键次数
        public int RightHand;   // 右手键击键次数
        public int PauseTimes;  // 暂停次数
        public int ImeBackspace;  // 拼回：删拼音/IME候选退格（committed 长度不变时的退格）
        public int Words;       // 打词数：一次输入 ≥ 2 个字符记 1 词（对齐老版 Glob.aTypeWords）
        public int Reselect;    // 选重次数（对齐老版 Glob.选重）

        public int LastInputLen;
        public int EventIndex;
        public readonly List<TypeDate> Report = new List<TypeDate>();

        /// <summary>载入新文章并重置所有状态。</summary>
        public void Load(string text, string title)
        {
            TypeText = (text ?? string.Empty)
                .Replace("\r", "")
                .Replace("\n", "")
                .Replace(" ", "")
                .Replace("\t", "");
            Title = title ?? string.Empty;
            Reset();
        }

        public void Reset()
        {
            Started = false;
            Finished = false;
            EndTime = null;
            Keys = 0;
            Hg = 0;
            Cz = 0;
            LeftHand = 0;
            RightHand = 0;
            PauseTimes = 0;
            ImeBackspace = 0;
            Words = 0;
            Reselect = 0;
            LastInputLen = 0;
            EventIndex = 0;
            Report.Clear();
        }

        /// <summary>统计：(速度字/分, 错一罚五速度, 击键键/秒, 码长键/字, 用时秒)
        /// 与老版 tygdq 对齐：
        ///   speed  = (inputLen − Cz) × 60 / sec
        ///   speed2 = max(0, (inputLen − Cz × 5)) × 60 / sec   ← 错一罚五
        ///   mc     = Keys / (inputLen − Cz)        ← 实际敲键比，不剥任何键
        ///   jj     = Keys / sec
        /// </summary>
        public (double speed, double speed2, double jj, double mc, double sec) ComputeStats(int inputLen)
        {
            double sec = ((EndTime ?? DateTime.Now) - StartTime).TotalSeconds;
            if (sec <= 0.001 || inputLen <= 0) return (0, 0, 0, 0, 0);

            int validLen = inputLen - Cz;  // 已打 - 错字 = 有效字数
            if (validLen < 0) validLen = 0;

            double speed = validLen * 60.0 / sec;
            if (speed > 999) speed = 999;

            int penalized = inputLen - Cz * 5;
            double speed2 = penalized > 0 ? penalized * 60.0 / sec : 0;
            if (speed2 > 999) speed2 = 999;

            double jj = Keys / sec;
            double mc = validLen > 0 ? (double)Keys / validLen : 0;
            return (speed, speed2, jj, mc, sec);
        }

        /// <summary>
        /// 回改前浪费键（老版 TextMcc 思想的新版适配）：真实回改删掉的已上屏内容，
        /// 当初打出它们消耗的键数即"浪费键"。从 Report 事件台账无状态推算：
        /// 正向事件按 [Start,End) 记其 TotalTick 键入台账；回改事件删掉 [End,Start)，
        /// 把覆盖被删位置的正向段键数计入浪费（跨界的多字提交段按字符比例分摊，不强行拆到单字）。
        /// </summary>
        public int ComputeWasteKeys()
        {
            // ledger：连续覆盖已上屏位置 [0, committedLen) 的正向提交段（start,end,keys）
            var ledger = new List<(int start, int end, int keys)>();
            int waste = 0;
            foreach (var ev in Report)
            {
                if (ev.End > ev.Start)
                {
                    ledger.Add((ev.Start, ev.End, ev.TotalTick));
                }
                else if (ev.End < ev.Start)
                {
                    int target = ev.End;                       // 回改后剩余长度
                    while (ledger.Count > 0 && ledger[ledger.Count - 1].end > target)
                    {
                        var seg = ledger[ledger.Count - 1];
                        ledger.RemoveAt(ledger.Count - 1);
                        if (seg.start >= target)
                        {
                            waste += seg.keys;                 // 整段被删
                        }
                        else
                        {
                            // 跨界：保留 [start,target)，浪费 [target,end)，按字符比例分摊键
                            int totalChars = seg.end - seg.start;
                            int delChars   = seg.end - target;
                            int wKeys = totalChars > 0
                                ? (int)System.Math.Round((double)seg.keys * delChars / totalChars)
                                : seg.keys;
                            if (wKeys > seg.keys) wKeys = seg.keys;
                            if (wKeys < 0) wKeys = 0;
                            waste += wKeys;
                            ledger.Add((seg.start, target, seg.keys - wKeys));
                            break;
                        }
                    }
                }
                // ev.End == ev.Start：AppendEvent 仅在 len 变化时调用，不会出现，忽略
            }
            return waste;
        }

        /// <summary>追加一条段内事件（输入长度变化时调用）。</summary>
        public void AppendEvent(int newLen)
        {
            EventIndex++;
            double now = (DateTime.Now - StartTime).TotalSeconds;
            int prevLen = LastInputLen;
            int delta = newLen - prevLen;
            // 打词：一次正向输入 ≥ 2 个字符视为打了一个词（与老版 aTypeWords 一致）
            if (delta >= 2) Words++;
            var prev = Report.Count > 0 ? Report[Report.Count - 1] : null;
            Report.Add(new TypeDate
            {
                Index     = EventIndex,
                Start     = prevLen,
                End       = newLen,
                Length    = delta,
                NowTime   = now,
                TotalTime = prev == null ? now : now - prev.NowTime,
                Tick      = Keys,
                TotalTick = prev == null ? Keys : Keys - prev.Tick,
            });
            LastInputLen = newLen;
        }
    }
}
