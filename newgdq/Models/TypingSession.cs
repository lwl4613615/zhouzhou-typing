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
        public int PauseTimes;  // 暂停次数
        public int Enter;       // 回车次数（对齐老版 Glob.回车）
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
            Keys = 0;
            Hg = 0;
            Cz = 0;
            LeftHand = 0;
            RightHand = 0;
            PauseTimes = 0;
            Enter = 0;
            Words = 0;
            Reselect = 0;
            LastInputLen = 0;
            EventIndex = 0;
            Report.Clear();
        }

        /// <summary>统计：(速度字/分, 错一罚五速度, 击键键/秒, 码长键/字, 用时秒)
        /// 与老版 tygdq 对齐：分母用"已打字数 − 当前错字数"（实际正确字数）。
        ///   speed  = (inputLen − Cz) × 60 / sec
        ///   speed2 = max(0, (inputLen − Cz × 5)) × 60 / sec   ← 错一罚五
        ///   mc     = (Keys − Hg − Enter) / (inputLen − Cz)   ← 退格/回车不算编码键
        ///   jj     = Keys / sec
        /// </summary>
        public (double speed, double speed2, double jj, double mc, double sec) ComputeStats(int inputLen)
        {
            double sec = (DateTime.Now - StartTime).TotalSeconds;
            if (sec <= 0.001 || inputLen <= 0) return (0, 0, 0, 0, 0);

            int validLen = inputLen - Cz;  // 已打 - 错字 = 有效字数
            if (validLen < 0) validLen = 0;

            double speed = validLen * 60.0 / sec;
            if (speed > 999) speed = 999;

            int penalized = inputLen - Cz * 5;
            double speed2 = penalized > 0 ? penalized * 60.0 / sec : 0;
            if (speed2 > 999) speed2 = 999;

            double jj = Keys / sec;
            // 码长分子要剥掉非编码键：退格(Hg) + 回车(Enter)
            int codeKeys = Keys - Hg - Enter;
            if (codeKeys < 0) codeKeys = 0;
            double mc = validLen > 0 ? (double)codeKeys / validLen : 0;
            return (speed, speed2, jj, mc, sec);
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
