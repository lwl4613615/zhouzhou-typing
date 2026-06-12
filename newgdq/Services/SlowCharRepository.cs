using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Reflection;

namespace newgdq.Services
{
    /// <summary>慢字本时间范围：近 7 天 / 近 30 天 / 全部。</summary>
    public enum TimeRange { Last7, Last30, All }

    /// <summary>慢字本一条采集明细（由跟打结束时的采集层构造，交给 <see cref="SlowCharRepository.InsertBatch"/> 落库）。</summary>
    public sealed class SlowEntry
    {
        public string Ch { get; set; }            // 该字
        public string Context { get; set; }       // 该字所在上下文片段（围绕该字的窗口）
        public int Pos { get; set; }              // 在原文中的位置（下标）
        public double PerSec { get; set; }         // 打该字耗时（秒）
        public double ThresholdSec { get; set; }   // 判"慢"的阈值（秒）
        public double PerTick { get; set; }        // 打该字耗时（计时单位/原始计数）
        public bool Slow { get; set; }             // 是否判为"慢"
        public bool Hg { get; set; }               // 是否回改（退格重打）
        public bool HighKey { get; set; }          // 是否高键位/高码长
        public string SourceSnippet { get; set; }  // 原文取样片段
    }

    /// <summary>慢字本弱项排行一行（供 UI / 训练生成对齐）。</summary>
    public sealed class SlowRankRow
    {
        public string Ch { get; set; }            // 该字
        public int SlowCount { get; set; }        // 范围内"慢"次数
        public double AvgOverSec { get; set; }     // 范围内"慢"时平均超阈秒数
        public int HgCount { get; set; }          // 范围内回改次数
        public int HighKeyCount { get; set; }     // 范围内高键位次数
        public int ErrorCount { get; set; }       // 范围内错字次数（只读 errorbook.db，仅用于打分）
        public double WeakScore { get; set; }      // 弱项分（倒序排序键）
        public bool Mastered { get; set; }         // 是否被手动标记为已掌握
        public DateTime LastSeen { get; set; }     // 范围内最近一次出现时间
    }

    /// <summary>慢字本溯源一行：来源文章标题 + 次数 + 最近时间 + 样例上下文 / 位置。</summary>
    public sealed class SlowSourceRow
    {
        public string Title { get; set; }
        public int Count { get; set; }
        public DateTime LastTime { get; set; }
        public string SampleContext { get; set; }
        public int SamplePos { get; set; }
    }

    /// <summary>
    /// 慢字本持久化（独立 SQLite 库，便携模式：slowchar.db 固定在 exe 同目录）。
    /// 明细 slow_log（每个采集字一条，带时间戳）+ 单字累计 slow_char_stat + 上下文累计 slow_context_stat。
    /// 排行 / 溯源按 slow_log 时间范围聚合；弱项分另只读 errorbook.db 取同字错次加权，绝不写入错字数据。
    /// 与 history.db / errorbook.db 完全分离，互不影响。失败仅 Debug.WriteLine，不阻塞 UI。
    /// </summary>
    public static class SlowCharRepository
    {
        private static readonly string ExeDir =
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";

        /// <summary>慢字本数据库文件绝对路径（exe 同目录\slowchar.db）。</summary>
        public static string FilePath { get; } = Path.Combine(ExeDir, "slowchar.db");

        private static string ConnStr => $"Data Source={FilePath};Version=3;Journal Mode=WAL;Busy Timeout=5000;";

        private static bool _initialized;

        /// <summary>启动调一次：建库 + 建表。</summary>
        public static void Init()
        {
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS slow_log (
  id             INTEGER PRIMARY KEY AUTOINCREMENT,
  when_utc       TEXT    NOT NULL,
  title          TEXT,
  seg            TEXT,
  pos            INTEGER,
  ch             TEXT    NOT NULL,
  context        TEXT,
  per_sec        REAL,
  threshold_sec  REAL,
  per_tick       REAL,
  slow_flag      INTEGER,
  hg_flag        INTEGER,
  high_key_flag  INTEGER,
  source_snippet TEXT
);
CREATE INDEX IF NOT EXISTS idx_slow_log_when    ON slow_log(when_utc);
CREATE INDEX IF NOT EXISTS idx_slow_log_when_ch ON slow_log(when_utc, ch);
CREATE INDEX IF NOT EXISTS idx_slow_log_ch_when ON slow_log(ch, when_utc);

CREATE TABLE IF NOT EXISTS slow_char_stat (
  ch              TEXT    PRIMARY KEY,
  typed_total     INTEGER NOT NULL DEFAULT 0,
  slow_total      INTEGER NOT NULL DEFAULT 0,
  hg_total        INTEGER NOT NULL DEFAULT 0,
  high_key_total  INTEGER NOT NULL DEFAULT 0,
  sec_sum         REAL    NOT NULL DEFAULT 0,
  over_sec_sum    REAL    NOT NULL DEFAULT 0,
  max_sec         REAL    NOT NULL DEFAULT 0,
  last_seen_utc   TEXT,
  last_slow_utc   TEXT,
  mastered        INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS slow_context_stat (
  context      TEXT    PRIMARY KEY,
  center_ch    TEXT,
  slow_total   INTEGER NOT NULL DEFAULT 0,
  sec_sum      REAL    NOT NULL DEFAULT 0,
  max_sec      REAL    NOT NULL DEFAULT 0,
  last_utc     TEXT,
  sample_title TEXT,
  sample_pos   INTEGER
);";
                        cmd.ExecuteNonQuery();
                    }
                }
                _initialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SlowCharRepository.Init] " + ex.Message);
            }
        }

        /// <summary>
        /// 批量写入一段跟打采集到的慢字明细：每条写 slow_log，并 upsert slow_char_stat（typed_total++、
        /// slow/hg/high_key 计数累加、sec/over_sec/max_sec/last_* 累计）与 slow_context_stat（仅"慢"且有上下文者）。
        /// over_sec = max(0, per_sec - threshold_sec)。空集合直接返回。失败仅 Debug，不抛。
        /// </summary>
        public static void InsertBatch(IEnumerable<SlowEntry> entries, string title, string seg)
        {
            if (!_initialized || entries == null) return;
            if (entries is ICollection<SlowEntry> col && col.Count == 0) return;
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    using (var logCmd = conn.CreateCommand())
                    using (var charCmd = conn.CreateCommand())
                    using (var ctxCmd = conn.CreateCommand())
                    {
                        logCmd.CommandText = @"
INSERT INTO slow_log
  (when_utc, title, seg, pos, ch, context, per_sec, threshold_sec, per_tick, slow_flag, hg_flag, high_key_flag, source_snippet)
VALUES
  (@w, @ti, @sg, @pos, @ch, @ctx, @persec, @thr, @ptick, @slow, @hg, @hk, @snip);";
                        var lW     = logCmd.Parameters.Add("@w",     System.Data.DbType.String);
                        var lTi    = logCmd.Parameters.Add("@ti",    System.Data.DbType.String);
                        var lSg    = logCmd.Parameters.Add("@sg",    System.Data.DbType.String);
                        var lPos   = logCmd.Parameters.Add("@pos",   System.Data.DbType.Int32);
                        var lCh    = logCmd.Parameters.Add("@ch",    System.Data.DbType.String);
                        var lCtx   = logCmd.Parameters.Add("@ctx",   System.Data.DbType.String);
                        var lSec   = logCmd.Parameters.Add("@persec", System.Data.DbType.Double);
                        var lThr   = logCmd.Parameters.Add("@thr",   System.Data.DbType.Double);
                        var lTick  = logCmd.Parameters.Add("@ptick", System.Data.DbType.Double);
                        var lSlow  = logCmd.Parameters.Add("@slow",  System.Data.DbType.Int32);
                        var lHg    = logCmd.Parameters.Add("@hg",    System.Data.DbType.Int32);
                        var lHk    = logCmd.Parameters.Add("@hk",    System.Data.DbType.Int32);
                        var lSnip  = logCmd.Parameters.Add("@snip",  System.Data.DbType.String);

                        charCmd.CommandText = @"
INSERT INTO slow_char_stat
  (ch, typed_total, slow_total, hg_total, high_key_total, sec_sum, over_sec_sum, max_sec, last_seen_utc, last_slow_utc, mastered)
VALUES
  (@ch, 1, @slow, @hg, @hk, @persec, @over, @persec, @now, CASE WHEN @slow = 1 THEN @now ELSE NULL END, 0)
ON CONFLICT(ch) DO UPDATE SET
  typed_total    = typed_total + 1,
  slow_total     = slow_total + @slow,
  hg_total       = hg_total + @hg,
  high_key_total = high_key_total + @hk,
  sec_sum        = sec_sum + @persec,
  over_sec_sum   = over_sec_sum + @over,
  max_sec        = CASE WHEN @persec > max_sec THEN @persec ELSE max_sec END,
  last_seen_utc  = @now,
  last_slow_utc  = CASE WHEN @slow = 1 THEN @now ELSE last_slow_utc END;";
                        var cCh   = charCmd.Parameters.Add("@ch",    System.Data.DbType.String);
                        var cSlow = charCmd.Parameters.Add("@slow",  System.Data.DbType.Int32);
                        var cHg   = charCmd.Parameters.Add("@hg",    System.Data.DbType.Int32);
                        var cHk   = charCmd.Parameters.Add("@hk",    System.Data.DbType.Int32);
                        var cSec  = charCmd.Parameters.Add("@persec", System.Data.DbType.Double);
                        var cOver = charCmd.Parameters.Add("@over",  System.Data.DbType.Double);
                        var cNow  = charCmd.Parameters.Add("@now",   System.Data.DbType.String);

                        ctxCmd.CommandText = @"
INSERT INTO slow_context_stat
  (context, center_ch, slow_total, sec_sum, max_sec, last_utc, sample_title, sample_pos)
VALUES
  (@ctx, @ch, 1, @persec, @persec, @now, @ti, @pos)
ON CONFLICT(context) DO UPDATE SET
  slow_total   = slow_total + 1,
  sec_sum      = sec_sum + @persec,
  max_sec      = CASE WHEN @persec > max_sec THEN @persec ELSE max_sec END,
  last_utc     = @now,
  center_ch    = @ch,
  sample_title = @ti,
  sample_pos   = @pos;";
                        var xCtx  = ctxCmd.Parameters.Add("@ctx",   System.Data.DbType.String);
                        var xCh   = ctxCmd.Parameters.Add("@ch",    System.Data.DbType.String);
                        var xSec  = ctxCmd.Parameters.Add("@persec", System.Data.DbType.Double);
                        var xNow  = ctxCmd.Parameters.Add("@now",   System.Data.DbType.String);
                        var xTi   = ctxCmd.Parameters.Add("@ti",    System.Data.DbType.String);
                        var xPos  = ctxCmd.Parameters.Add("@pos",   System.Data.DbType.Int32);

                        string now = DateTime.Now.ToString("o");
                        object titleVal = (object)title ?? DBNull.Value;
                        object segVal   = (object)seg ?? DBNull.Value;

                        lW.Value = now; lTi.Value = titleVal; lSg.Value = segVal;
                        cNow.Value = now;
                        xNow.Value = now; xTi.Value = titleVal;

                        foreach (var e in entries)
                        {
                            if (e == null || string.IsNullOrEmpty(e.Ch)) continue;
                            int slow = e.Slow ? 1 : 0;
                            int hg   = e.Hg ? 1 : 0;
                            int hk   = e.HighKey ? 1 : 0;
                            double over = Math.Max(0.0, e.PerSec - e.ThresholdSec);

                            lPos.Value  = e.Pos;
                            lCh.Value   = e.Ch;
                            lCtx.Value  = (object)e.Context ?? DBNull.Value;
                            lSec.Value  = e.PerSec;
                            lThr.Value  = e.ThresholdSec;
                            lTick.Value = e.PerTick;
                            lSlow.Value = slow;
                            lHg.Value   = hg;
                            lHk.Value   = hk;
                            lSnip.Value = (object)e.SourceSnippet ?? DBNull.Value;
                            logCmd.ExecuteNonQuery();

                            cCh.Value   = e.Ch;
                            cSlow.Value = slow;
                            cHg.Value   = hg;
                            cHk.Value   = hk;
                            cSec.Value  = e.PerSec;
                            cOver.Value = over;
                            charCmd.ExecuteNonQuery();

                            if (e.Slow && !string.IsNullOrEmpty(e.Context))
                            {
                                xCtx.Value = e.Context;
                                xCh.Value  = e.Ch;
                                xSec.Value = e.PerSec;
                                xPos.Value = e.Pos;
                                ctxCmd.ExecuteNonQuery();
                            }
                        }
                        tx.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SlowCharRepository.InsertBatch] " + ex.Message);
            }
        }

        /// <summary>把时间范围换算成下界（本地时间，与 when_utc 写入格式一致）；All 返回 DateTime.MinValue。</summary>
        private static DateTime RangeLowerBound(TimeRange range)
        {
            switch (range)
            {
                case TimeRange.Last7:  return DateTime.Now.AddDays(-7);
                case TimeRange.Last30: return DateTime.Now.AddDays(-30);
                default:               return DateTime.MinValue;
            }
        }

        /// <summary>
        /// 弱项排行：按 slow_log 时间范围聚合每个字的 slow/hg/high_key 次数与"慢"时平均超阈秒，
        /// join slow_char_stat.mastered，并只读 errorbook.db 取同字同范围错次，按 weak_score 倒序。
        /// weak_score = slow_count*3.0 + avg_over_sec*2.0 + hg_count*1.5 + high_key_count*0.8 + error_count*1.0。
        /// </summary>
        public static List<SlowRankRow> LoadRanking(TimeRange range, bool hideMastered)
        {
            var list = new List<SlowRankRow>();
            if (!_initialized) return list;
            string lb = RangeLowerBound(range).ToString("o");
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
SELECT l.ch,
       COALESCE(SUM(l.slow_flag), 0)     AS slow_count,
       COALESCE(AVG(CASE WHEN l.slow_flag = 1 THEN MAX(0.0, l.per_sec - l.threshold_sec) END), 0) AS avg_over,
       COALESCE(SUM(l.hg_flag), 0)       AS hg_count,
       COALESCE(SUM(l.high_key_flag), 0) AS hk_count,
       MAX(l.when_utc)                   AS last_seen,
       COALESCE(s.mastered, 0)           AS mastered
FROM slow_log l
LEFT JOIN slow_char_stat s ON s.ch = l.ch
WHERE l.when_utc >= @lb" + (hideMastered ? " AND COALESCE(s.mastered, 0) = 0" : "") + @"
GROUP BY l.ch;";
                        cmd.Parameters.AddWithValue("@lb", lb);
                        using (var rd = cmd.ExecuteReader())
                        {
                            while (rd.Read())
                            {
                                if (rd.IsDBNull(0)) continue;
                                DateTime last;
                                if (rd.IsDBNull(5) || !DateTime.TryParse(rd.GetString(5), out last)) last = DateTime.Now;
                                list.Add(new SlowRankRow
                                {
                                    Ch           = rd.GetString(0),
                                    SlowCount    = (int)rd.GetInt64(1),
                                    AvgOverSec   = rd.GetDouble(2),
                                    HgCount      = (int)rd.GetInt64(3),
                                    HighKeyCount = (int)rd.GetInt64(4),
                                    LastSeen     = last,
                                    Mastered     = rd.GetInt64(6) != 0,
                                });
                            }
                        }
                    }
                }

                var errCounts = LoadErrorCounts(lb);
                foreach (var row in list)
                {
                    int ec;
                    row.ErrorCount = errCounts.TryGetValue(row.Ch, out ec) ? ec : 0;
                    row.WeakScore = row.SlowCount * 3.0
                                  + row.AvgOverSec * 2.0
                                  + row.HgCount * 1.5
                                  + row.HighKeyCount * 0.8
                                  + row.ErrorCount * 1.0;
                }
                list.Sort((a, b) => b.WeakScore.CompareTo(a.WeakScore));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SlowCharRepository.LoadRanking] " + ex.Message);
            }
            return list;
        }

        /// <summary>
        /// 只读 errorbook.db，按时间范围统计每个原文字的错次（correct -> count）。
        /// 仅用于 weak_score 排序加权，绝不写入慢字本库；errorbook.db 不存在时返回空表。
        /// </summary>
        private static Dictionary<string, int> LoadErrorCounts(string lowerBoundIso)
        {
            var map = new Dictionary<string, int>();
            try
            {
                string path = ErrorBookRepository.FilePath;
                if (!File.Exists(path)) return map;
                string cs = $"Data Source={path};Version=3;Journal Mode=WAL;Busy Timeout=5000;";
                using (var conn = new SQLiteConnection(cs))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT correct, COUNT(*) FROM error_log WHERE when_utc >= @lb GROUP BY correct;";
                        cmd.Parameters.AddWithValue("@lb", lowerBoundIso);
                        using (var rd = cmd.ExecuteReader())
                        {
                            while (rd.Read())
                            {
                                if (rd.IsDBNull(0)) continue;
                                map[rd.GetString(0)] = (int)rd.GetInt64(1);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SlowCharRepository.LoadErrorCounts] " + ex.Message);
            }
            return map;
        }

        /// <summary>查某个字在指定时间范围内的来源文章分布（按次数倒序），样例上下文/位置取该来源最近一次出现。</summary>
        public static List<SlowSourceRow> LoadSources(string ch, TimeRange range)
        {
            var list = new List<SlowSourceRow>();
            if (!_initialized || string.IsNullOrEmpty(ch)) return list;
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        // 仅一个 MAX(when_utc) 聚合：SQLite 保证裸列 context/pos 取自该 MAX 所在行（最近一次）。
                        cmd.CommandText = @"
SELECT COALESCE(NULLIF(title, ''), '(未命名)') AS t,
       COUNT(*)        AS cnt,
       MAX(when_utc)   AS last_t,
       context         AS sample_ctx,
       pos             AS sample_pos
FROM slow_log
WHERE ch = @c AND when_utc >= @lb
GROUP BY t
ORDER BY cnt DESC, last_t DESC;";
                        cmd.Parameters.AddWithValue("@c", ch);
                        cmd.Parameters.AddWithValue("@lb", RangeLowerBound(range).ToString("o"));
                        using (var rd = cmd.ExecuteReader())
                        {
                            while (rd.Read())
                            {
                                DateTime lt;
                                if (rd.IsDBNull(2) || !DateTime.TryParse(rd.GetString(2), out lt)) lt = DateTime.Now;
                                list.Add(new SlowSourceRow
                                {
                                    Title         = rd.IsDBNull(0) ? "(未命名)" : rd.GetString(0),
                                    Count         = (int)rd.GetInt64(1),
                                    LastTime      = lt,
                                    SampleContext = rd.IsDBNull(3) ? "" : rd.GetString(3),
                                    SamplePos     = rd.IsDBNull(4) ? 0 : (int)rd.GetInt64(4),
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SlowCharRepository.LoadSources] " + ex.Message);
            }
            return list;
        }

        /// <summary>读取指定时间范围内"最卡"的若干上下文短语（按 slow_total、sec_sum 倒序），供慢字专项练习串入。只读 slow_context_stat，不写任何表。</summary>
        public static List<string> LoadTopContexts(TimeRange range, int limit)
        {
            var list = new List<string>();
            if (!_initialized || limit <= 0) return list;
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
SELECT context
FROM slow_context_stat
WHERE last_utc >= @lb AND context IS NOT NULL AND context <> ''
ORDER BY slow_total DESC, sec_sum DESC
LIMIT @lim;";
                        cmd.Parameters.AddWithValue("@lb", RangeLowerBound(range).ToString("o"));
                        cmd.Parameters.AddWithValue("@lim", limit);
                        using (var rd = cmd.ExecuteReader())
                        {
                            while (rd.Read())
                                if (!rd.IsDBNull(0)) list.Add(rd.GetString(0));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SlowCharRepository.LoadTopContexts] " + ex.Message);
            }
            return list;
        }

        /// <summary>清空指定时间范围内的 slow_log（All 清全部）；随后从剩余明细整表重算两张累计表，并保留用户手动"已掌握"标记。仅动 slowchar.db。返回删除明细条数。</summary>
        public static int ClearRange(TimeRange range)
        {
            if (!_initialized) return 0;
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        int deleted;
                        string lb = RangeLowerBound(range).ToString("o");

                        // 保留用户手动标记的"已掌握"字（不随重算丢失）
                        var mastered = new List<string>();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT ch FROM slow_char_stat WHERE mastered = 1;";
                            using (var rd = cmd.ExecuteReader())
                                while (rd.Read())
                                    if (!rd.IsDBNull(0)) mastered.Add(rd.GetString(0));
                        }

                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "DELETE FROM slow_log WHERE when_utc >= @lb;";
                            cmd.Parameters.AddWithValue("@lb", lb);
                            deleted = cmd.ExecuteNonQuery();
                        }

                        // 从剩余明细整表重算两张累计表
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = @"
DELETE FROM slow_char_stat;
INSERT INTO slow_char_stat
  (ch, typed_total, slow_total, hg_total, high_key_total, sec_sum, over_sec_sum, max_sec, last_seen_utc, last_slow_utc, mastered)
SELECT ch,
       COUNT(*),
       COALESCE(SUM(slow_flag), 0),
       COALESCE(SUM(hg_flag), 0),
       COALESCE(SUM(high_key_flag), 0),
       COALESCE(SUM(per_sec), 0),
       COALESCE(SUM(MAX(0.0, per_sec - threshold_sec)), 0),
       COALESCE(MAX(per_sec), 0),
       MAX(when_utc),
       MAX(CASE WHEN slow_flag = 1 THEN when_utc END),
       0
FROM slow_log
GROUP BY ch;

DELETE FROM slow_context_stat;
INSERT INTO slow_context_stat
  (context, center_ch, slow_total, sec_sum, max_sec, last_utc, sample_title, sample_pos)
SELECT context, ch, COUNT(*), COALESCE(SUM(per_sec), 0), COALESCE(MAX(per_sec), 0), MAX(when_utc), title, pos
FROM slow_log
WHERE slow_flag = 1 AND context IS NOT NULL AND context <> ''
GROUP BY context;";
                            cmd.ExecuteNonQuery();
                        }

                        // 重新套用"已掌握"标记
                        if (mastered.Count > 0)
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = @"
INSERT INTO slow_char_stat
  (ch, typed_total, slow_total, hg_total, high_key_total, sec_sum, over_sec_sum, max_sec, last_seen_utc, last_slow_utc, mastered)
VALUES (@c, 0, 0, 0, 0, 0, 0, 0, NULL, NULL, 1)
ON CONFLICT(ch) DO UPDATE SET mastered = 1;";
                                var pc = cmd.Parameters.Add("@c", System.Data.DbType.String);
                                foreach (var m in mastered) { pc.Value = m; cmd.ExecuteNonQuery(); }
                            }
                        }

                        tx.Commit();
                        return deleted;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SlowCharRepository.ClearRange] " + ex.Message);
                return 0;
            }
        }

        /// <summary>手动标记/取消某个字"已掌握"（从默认榜淡出）。slow_char_stat 无该字时自动补建。</summary>
        public static void SetMastered(string ch, bool mastered)
        {
            if (!_initialized || string.IsNullOrEmpty(ch)) return;
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
INSERT INTO slow_char_stat
  (ch, typed_total, slow_total, hg_total, high_key_total, sec_sum, over_sec_sum, max_sec, last_seen_utc, last_slow_utc, mastered)
VALUES (@c, 0, 0, 0, 0, 0, 0, 0, NULL, NULL, @m)
ON CONFLICT(ch) DO UPDATE SET mastered = @m;";
                        cmd.Parameters.AddWithValue("@c", ch);
                        cmd.Parameters.AddWithValue("@m", mastered ? 1 : 0);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SlowCharRepository.SetMastered] " + ex.Message);
            }
        }
    }
}
