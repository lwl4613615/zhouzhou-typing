using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Reflection;
using newgdq.Models;

namespace newgdq.Services
{
    /// <summary>
    /// 历史成绩持久化（SQLite）—— 便携模式：DB 文件固定在 exe 同目录。
    /// 表 type_record 字段与 <see cref="HistoryRow"/> 字段一一对应。
    /// 启动调 <see cref="Init"/>；完成一段调 <see cref="Insert"/>；
    /// 启动恢复历史用 <see cref="LoadRecent"/>。失败仅 Debug.WriteLine，不阻塞 UI。
    /// </summary>
    public static class HistoryRepository
    {
        private static readonly string ExeDir =
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";

        /// <summary>SQLite 数据库文件绝对路径（exe 同目录\history.db）。</summary>
        public static string FilePath { get; } = Path.Combine(ExeDir, "history.db");

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
CREATE TABLE IF NOT EXISTS type_record (
  id        INTEGER PRIMARY KEY AUTOINCREMENT,
  when_utc  TEXT    NOT NULL,
  title     TEXT,
  seg       TEXT,
  speed     REAL,
  speed2    REAL,
  jj        REAL,
  mc        REAL,
  hg        INTEGER,
  cz        INTEGER,
  js        INTEGER,
  words     INTEGER,
  daci      INTEGER,
  use_time  REAL
);
CREATE INDEX IF NOT EXISTS idx_type_record_when ON type_record(when_utc);";
                        cmd.ExecuteNonQuery();
                    }
                    // 旧库迁移：把历史 enter 列改名为 imebs，保住旧数据（新库无 enter 列则吞异常）
                    try { using (var c2 = conn.CreateCommand()) { c2.CommandText = "ALTER TABLE type_record RENAME COLUMN enter TO imebs;"; c2.ExecuteNonQuery(); } }
                    catch { /* 旧库无 enter 或新库已是 imebs */ }
                    // 兼容旧库：ALTER 加扩展列，已存在则吞异常
                    foreach (var sql in new[] {
                        "ALTER TABLE type_record ADD COLUMN reselect INTEGER DEFAULT 0;",
                        "ALTER TABLE type_record ADD COLUMN imebs    INTEGER DEFAULT 0;",
                        "ALTER TABLE type_record ADD COLUMN lhand    INTEGER DEFAULT 0;",
                        "ALTER TABLE type_record ADD COLUMN rhand    INTEGER DEFAULT 0;",
                    })
                    {
                        try { using (var c2 = conn.CreateCommand()) { c2.CommandText = sql; c2.ExecuteNonQuery(); } }
                        catch { /* 已存在 */ }
                    }
                }
                _initialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[HistoryRepository.Init] " + ex.Message);
            }
        }

        public static void Insert(HistoryRow r)
        {
            if (!_initialized) return;
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
INSERT INTO type_record (when_utc, title, seg, speed, speed2, jj, mc, hg, cz, js, words, daci, use_time, reselect, imebs, lhand, rhand)
VALUES (@w, @t, @s, @sp, @sp2, @jj, @mc, @hg, @cz, @js, @wd, @dc, @ut, @re, @imebs, @lh, @rh);";
                        cmd.Parameters.AddWithValue("@w",   (r.When == default ? DateTime.Now : r.When).ToString("o"));
                        cmd.Parameters.AddWithValue("@t",   (object)r.Title ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@s",   (object)r.Seg   ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@sp",  r.Speed);
                        cmd.Parameters.AddWithValue("@sp2", r.Speed2);
                        cmd.Parameters.AddWithValue("@jj",  r.Jj);
                        cmd.Parameters.AddWithValue("@mc",  r.Mc);
                        cmd.Parameters.AddWithValue("@hg",  r.Hg);
                        cmd.Parameters.AddWithValue("@cz",  r.Cz);
                        cmd.Parameters.AddWithValue("@js",  r.Js);
                        cmd.Parameters.AddWithValue("@wd",  r.Words);
                        cmd.Parameters.AddWithValue("@dc",  r.DaCi);
                        cmd.Parameters.AddWithValue("@ut",  r.UseTime);
                        cmd.Parameters.AddWithValue("@re",  r.Reselect);
                        cmd.Parameters.AddWithValue("@imebs",  r.ImeBackspace);
                        cmd.Parameters.AddWithValue("@lh",  r.LeftHand);
                        cmd.Parameters.AddWithValue("@rh",  r.RightHand);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[HistoryRepository.Insert] " + ex.Message);
            }
        }

        /// <summary>取最近 N 条记录，按时间倒序（最新在前）。Index 字段重排为 1 = 最早的。</summary>
        public static List<HistoryRow> LoadRecent(int limit = 200)
        {
            var list = new List<HistoryRow>();
            if (!_initialized) return list;
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
SELECT id, when_utc, title, seg, speed, speed2, jj, mc, hg, cz, js, words, daci, use_time, reselect, imebs, lhand, rhand
FROM type_record ORDER BY id DESC LIMIT @lim;";
                        cmd.Parameters.AddWithValue("@lim", limit);
                        using (var rd = cmd.ExecuteReader())
                        {
                            while (rd.Read())
                            {
                                DateTime when;
                                if (!DateTime.TryParse(rd.GetString(1), out when)) when = DateTime.Now;
                                list.Add(new HistoryRow
                                {
                                    Index   = (int)rd.GetInt64(0),
                                    When    = when,
                                    Time    = when.ToString("HH:mm:ss"),
                                    Title   = rd.IsDBNull(2) ? null : rd.GetString(2),
                                    Seg     = rd.IsDBNull(3) ? null : rd.GetString(3),
                                    Speed   = rd.GetDouble(4),
                                    Speed2  = rd.GetDouble(5),
                                    Jj      = rd.GetDouble(6),
                                    Mc      = rd.GetDouble(7),
                                    Hg      = (int)rd.GetInt64(8),
                                    Cz      = (int)rd.GetInt64(9),
                                    Js      = (int)rd.GetInt64(10),
                                    Words   = (int)rd.GetInt64(11),
                                    DaCi    = (int)rd.GetInt64(12),
                                    UseTime = rd.GetDouble(13),
                                    Reselect = rd.IsDBNull(14) ? 0 : (int)rd.GetInt64(14),
                                    ImeBackspace = rd.IsDBNull(15) ? 0 : (int)rd.GetInt64(15),
                                    LeftHand = rd.IsDBNull(16) ? 0 : (int)rd.GetInt64(16),
                                    RightHand= rd.IsDBNull(17) ? 0 : (int)rd.GetInt64(17),
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[HistoryRepository.LoadRecent] " + ex.Message);
            }
            return list;
        }

        /// <summary>返回总记录数（用于继续递增 _historyIndex）。</summary>
        public static int TotalCount()
        {
            if (!_initialized) return 0;
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM type_record;";
                        var o = cmd.ExecuteScalar();
                        return Convert.ToInt32(o);
                    }
                }
            }
            catch { return 0; }
        }

        /// <summary>汇总统计：(今日字数, 今日用时秒, 今日段数, 累计字数, 累计段数, 训练天数)。</summary>
        public static (int todayWords, double todaySec, int todaySegs, int totalWords, int totalSegs, int days) LoadSummary()
        {
            if (!_initialized) return (0, 0, 0, 0, 0, 0);
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    // 今日：按本地日期分组 (when_utc 是 ISO UTC)
                    int tw = 0, ts = 0; double tsec = 0;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COALESCE(SUM(words),0), COALESCE(SUM(use_time),0), COUNT(*) FROM type_record WHERE date(when_utc,'localtime')=date('now','localtime');";
                        using (var rd = cmd.ExecuteReader())
                        {
                            if (rd.Read()) { tw = rd.GetInt32(0); tsec = rd.GetDouble(1); ts = rd.GetInt32(2); }
                        }
                    }
                    int totalW = 0, totalS = 0, days = 0;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COALESCE(SUM(words),0), COUNT(*), COUNT(DISTINCT date(when_utc,'localtime')) FROM type_record;";
                        using (var rd = cmd.ExecuteReader())
                        {
                            if (rd.Read()) { totalW = rd.GetInt32(0); totalS = rd.GetInt32(1); days = rd.GetInt32(2); }
                        }
                    }
                    return (tw, tsec, ts, totalW, totalS, days);
                }
            }
            catch { return (0, 0, 0, 0, 0, 0); }
        }

        /// <summary>历史均值：(今日 avg speed/jj/mc, 累计 avg speed/jj)。</summary>
        public static (double todaySpeed, double todayJj, double todayMc, double totalSpeed, double totalJj) LoadAverages()
        {
            if (!_initialized) return (0, 0, 0, 0, 0);
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    double tSp = 0, tJj = 0, tMc = 0, allSp = 0, allJj = 0;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COALESCE(AVG(speed),0), COALESCE(AVG(jj),0), COALESCE(AVG(mc),0) FROM type_record WHERE date(when_utc,'localtime')=date('now','localtime');";
                        using (var rd = cmd.ExecuteReader())
                        {
                            if (rd.Read()) { tSp = rd.GetDouble(0); tJj = rd.GetDouble(1); tMc = rd.GetDouble(2); }
                        }
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COALESCE(AVG(speed),0), COALESCE(AVG(jj),0) FROM type_record;";
                        using (var rd = cmd.ExecuteReader())
                        {
                            if (rd.Read()) { allSp = rd.GetDouble(0); allJj = rd.GetDouble(1); }
                        }
                    }
                    return (tSp, tJj, tMc, allSp, allJj);
                }
            }
            catch { return (0, 0, 0, 0, 0); }
        }

        /// <summary>取所有历史段的击键速度（键/秒）—— 击键评定窗用。</summary>
        public static List<double> LoadAllJj()
        {
            var list = new List<double>();
            if (!_initialized) return list;
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT jj FROM type_record WHERE jj > 0;";
                        using (var rd = cmd.ExecuteReader())
                            while (rd.Read()) list.Add(rd.GetDouble(0));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[HistoryRepository.LoadAllJj] " + ex.Message);
            }
            return list;
        }

        /// <summary>聚合统计：返回 (段数, 平均速度, 平均罚五, 平均击键, 平均码长, 最高速度, 总用时秒)。</summary>
        public static (int Count, double AvgSpeed, double AvgSpeed2, double AvgJj, double AvgMc, double MaxSpeed, double TotalUseTime) LoadAggregate()
        {
            if (!_initialized) return (0, 0, 0, 0, 0, 0, 0);
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*), AVG(speed), AVG(speed2), AVG(jj), AVG(mc), MAX(speed), SUM(use_time) FROM type_record;";
                        using (var rd = cmd.ExecuteReader())
                        {
                            if (rd.Read())
                            {
                                int cnt        = rd.IsDBNull(0) ? 0 : (int)rd.GetInt64(0);
                                double aSpeed  = rd.IsDBNull(1) ? 0 : rd.GetDouble(1);
                                double aSpeed2 = rd.IsDBNull(2) ? 0 : rd.GetDouble(2);
                                double aJj     = rd.IsDBNull(3) ? 0 : rd.GetDouble(3);
                                double aMc     = rd.IsDBNull(4) ? 0 : rd.GetDouble(4);
                                double mSpeed  = rd.IsDBNull(5) ? 0 : rd.GetDouble(5);
                                double tUse    = rd.IsDBNull(6) ? 0 : rd.GetDouble(6);
                                return (cnt, aSpeed, aSpeed2, aJj, aMc, mSpeed, tUse);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[HistoryRepository.LoadAggregate] " + ex.Message); }
            return (0, 0, 0, 0, 0, 0, 0);
        }

        /// <summary>趋势周期粒度。</summary>
        public enum TrendGranularity { Day, Week, Month }

        /// <summary>趋势里一个周期桶（一天/一周/一月）的聚合成绩。</summary>
        public sealed class TrendBucket
        {
            public string Key { get; set; }       // 分组键（排序用）
            public string Label { get; set; }     // 显示标签（如 05-31 / 第22周 / 2026-05）
            public int Segs { get; set; }          // 段数
            public int Words { get; set; }         // 总字数
            public double AvgSpeed { get; set; }   // 平均速度（字/分）
            public double AvgSpeed2 { get; set; }  // 平均错一罚五
            public double MaxSpeed { get; set; }   // 最高速度
            public double AvgJj { get; set; }      // 平均击键
            public double AvgMc { get; set; }      // 平均码长
            public double ErrRate { get; set; }    // 错字率 = 错字 / 字数（0~1）
        }

        /// <summary>
        /// 按周期粒度聚合成绩趋势，返回最近 <paramref name="limitBuckets"/> 个非空周期（时间升序，最新在末尾）。
        /// Day=按本地日期；Week=按 ISO 周（周一起）；Month=按本地年月。空库返回空列表。
        /// </summary>
        public static List<TrendBucket> LoadTrend(TrendGranularity g, int limitBuckets = 12)
        {
            var list = new List<TrendBucket>();
            if (!_initialized) return list;

            // 分组键 / 标签的 SQL 表达式（均按本地时区换算）
            string keyExpr, labelExpr;
            switch (g)
            {
                case TrendGranularity.Week:
                    // %Y-%W：以周一为一周起点（SQLite %W 周一为 0..53）
                    keyExpr   = "strftime('%Y-%W', when_utc, 'localtime')";
                    labelExpr = "MIN(date(when_utc,'localtime'))"; // 标签用该周最早日期
                    break;
                case TrendGranularity.Month:
                    keyExpr   = "strftime('%Y-%m', when_utc, 'localtime')";
                    labelExpr = "strftime('%Y-%m', when_utc, 'localtime')";
                    break;
                default: // Day
                    keyExpr   = "date(when_utc,'localtime')";
                    labelExpr = "date(when_utc,'localtime')";
                    break;
            }

            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText =
$@"SELECT {keyExpr} AS k, {labelExpr} AS lbl,
       COUNT(*) AS segs, COALESCE(SUM(words),0) AS wds,
       AVG(speed) AS asp, AVG(speed2) AS asp2, MAX(speed) AS msp,
       AVG(jj) AS ajj, AVG(mc) AS amc, COALESCE(SUM(cz),0) AS czs
FROM type_record
GROUP BY k
ORDER BY k ASC;";
                        using (var rd = cmd.ExecuteReader())
                        {
                            while (rd.Read())
                            {
                                int wds = (int)rd.GetInt64(3);
                                int czs = (int)rd.GetInt64(9);
                                list.Add(new TrendBucket
                                {
                                    Key       = rd.IsDBNull(0) ? "" : rd.GetString(0),
                                    Label     = FormatTrendLabel(g, rd.IsDBNull(1) ? "" : rd.GetString(1)),
                                    Segs      = (int)rd.GetInt64(2),
                                    Words     = wds,
                                    AvgSpeed  = rd.IsDBNull(4) ? 0 : rd.GetDouble(4),
                                    AvgSpeed2 = rd.IsDBNull(5) ? 0 : rd.GetDouble(5),
                                    MaxSpeed  = rd.IsDBNull(6) ? 0 : rd.GetDouble(6),
                                    AvgJj     = rd.IsDBNull(7) ? 0 : rd.GetDouble(7),
                                    AvgMc     = rd.IsDBNull(8) ? 0 : rd.GetDouble(8),
                                    ErrRate   = wds > 0 ? (double)czs / wds : 0,
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[HistoryRepository.LoadTrend] " + ex.Message); }

            // 只保留最近 N 个周期
            if (limitBuckets > 0 && list.Count > limitBuckets)
                list = list.GetRange(list.Count - limitBuckets, limitBuckets);
            return list;
        }

        /// <summary>把原始标签格式化成友好显示（按天→MM-dd，按周→MM-dd 那周，按月→YYYY-MM）。</summary>
        private static string FormatTrendLabel(TrendGranularity g, string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            DateTime d;
            switch (g)
            {
                case TrendGranularity.Day:
                    return DateTime.TryParse(raw, out d) ? d.ToString("MM-dd") : raw;
                case TrendGranularity.Week:
                    return DateTime.TryParse(raw, out d) ? d.ToString("MM-dd") + " 周" : raw;
                default:
                    return raw; // YYYY-MM
            }
        }
    }
}
