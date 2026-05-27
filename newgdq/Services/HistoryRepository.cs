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
                    // 兼容旧库：ALTER 加扩展列，已存在则吞异常
                    foreach (var sql in new[] {
                        "ALTER TABLE type_record ADD COLUMN reselect INTEGER DEFAULT 0;",
                        "ALTER TABLE type_record ADD COLUMN enter    INTEGER DEFAULT 0;",
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
INSERT INTO type_record (when_utc, title, seg, speed, speed2, jj, mc, hg, cz, js, words, daci, use_time, reselect, enter, lhand, rhand)
VALUES (@w, @t, @s, @sp, @sp2, @jj, @mc, @hg, @cz, @js, @wd, @dc, @ut, @re, @en, @lh, @rh);";
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
                        cmd.Parameters.AddWithValue("@en",  r.Enter);
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
SELECT id, when_utc, title, seg, speed, speed2, jj, mc, hg, cz, js, words, daci, use_time, reselect, enter, lhand, rhand
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
                                    Enter    = rd.IsDBNull(15) ? 0 : (int)rd.GetInt64(15),
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
    }
}
