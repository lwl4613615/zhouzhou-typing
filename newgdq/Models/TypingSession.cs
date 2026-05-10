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

        public int Keys;        // 击键数（来自键钩子）
        public int Hg;          // 回改：退格累计
        public int Cz;          // 错字（已打部分中错的字数）
        public int LeftHand;    // 左手键击键次数
        public int RightHand;   // 右手键击键次数

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
            Keys = 0;
            Hg = 0;
            Cz = 0;
            LeftHand = 0;
            RightHand = 0;
            LastInputLen = 0;
            EventIndex = 0;
            Report.Clear();
        }

        /// <summary>统计：(速度字/分, 击键键/秒, 码长键/字, 用时秒)</summary>
        public (double speed, double jj, double mc, double sec) ComputeStats(int inputLen)
        {
            double sec = (DateTime.Now - StartTime).TotalSeconds;
            if (sec <= 0.001 || inputLen <= 0) return (0, 0, 0, 0);

            double speed = inputLen * 60.0 / sec;
            if (speed > 999) speed = 999;
            double jj = Keys / sec;
            double mc = (double)Keys / inputLen;
            return (speed, jj, mc, sec);
        }

        /// <summary>追加一条段内事件（输入长度变化时调用）。</summary>
        public void AppendEvent(int newLen)
        {
            EventIndex++;
            double now = (DateTime.Now - StartTime).TotalSeconds;
            int prevLen = LastInputLen;
            var prev = Report.Count > 0 ? Report[Report.Count - 1] : null;
            Report.Add(new TypeDate
            {
                Index     = EventIndex,
                Start     = prevLen,
                End       = newLen,
                Length    = newLen - prevLen,
                NowTime   = now,
                TotalTime = prev == null ? now : now - prev.NowTime,
                Tick      = Keys,
                TotalTick = prev == null ? Keys : Keys - prev.Tick,
            });
            LastInputLen = newLen;
        }
    }
}
