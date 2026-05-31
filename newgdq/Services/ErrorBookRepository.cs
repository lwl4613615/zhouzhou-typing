using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Reflection;

namespace newgdq.Services
{
    /// <summary>错字本一条排行：正确字 + 被打成的字 + 次数 + 最近一次时间。</summary>
    public sealed class ErrorStat
    {
        public string Correct { get; set; }   // 原文应打的字
        public string Typed { get; set; }     // 实际打成的字
        public int Count { get; set; }         // 出现次数
        public DateTime LastTime { get; set; } // 最近一次错的时间
        public int Streak { get; set; }        // 该字当前连续打对次数（错一次归零）
        public int WrongTotal { get; set; }    // 该字累计打错次数
        public int TypedTotal { get; set; }    // 该字累计被打次数（对+错）
        public bool Mastered { get; set; }     // 是否被手动标记为已掌握

        /// <summary>累计错误率（0~1）；从未打过返回 0。</summary>
        public double WrongRate => TypedTotal > 0 ? (double)WrongTotal / TypedTotal : 0;
    }

    /// <summary>错字本时间范围。</summary>
    public enum ErrorRange { Session, Day, Week, Month, Year, All }

    /// <summary>
    /// 错字本持久化（独立 SQLite 库，便携模式：errorbook.db 固定在 exe 同目录）。
    /// 详细粒度（B）：每出现一次"正确字→打成字"就记一条，带时间戳，支持按时间范围聚合排行。
    /// 与 history.db 完全分离，互不影响。失败仅 Debug.WriteLine，不阻塞 UI。
    /// </summary>
    public static class ErrorBookRepository
    {
        private static readonly string ExeDir =
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";

        /// <summary>错字本数据库文件绝对路径（exe 同目录\errorbook.db）。</summary>
        public static string FilePath { get; } = Path.Combine(ExeDir, "errorbook.db");

        private static string ConnStr => $"Data Source={FilePath};Version=3;Journal Mode=WAL;Busy Timeout=5000;";

        private static bool _initialized;

        /// <summary>连续打对达到此次数即视为"已掌握"，默认从榜单自动淡出。</summary>
        public const int MasterStreak = 5;

        /// <summary>本进程启动时刻（"本次"范围以此为下界）。</summary>
        public static readonly DateTime ProcessStart = DateTime.Now;

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
CREATE TABLE IF NOT EXISTS error_log (
  id        INTEGER PRIMARY KEY AUTOINCREMENT,
  when_utc  TEXT    NOT NULL,
  correct   TEXT    NOT NULL,
  typed     TEXT,
  title     TEXT
);
CREATE INDEX IF NOT EXISTS idx_error_log_when ON error_log(when_utc);

CREATE TABLE IF NOT EXISTS char_stat (
  correct        TEXT    PRIMARY KEY,
  typed_total    INTEGER NOT NULL DEFAULT 0,
  wrong_total    INTEGER NOT NULL DEFAULT 0,
  streak         INTEGER NOT NULL DEFAULT 0,
  mastered       INTEGER NOT NULL DEFAULT 0,
  last_wrong_utc TEXT
);";
                        cmd.ExecuteNonQuery();
                    }
                }
                _initialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ErrorBookRepository.Init] " + ex.Message);
            }
        }

        /// <summary>批量写入一段跟打里采集到的错字（正确字, 打成字）。空集合直接返回。</summary>
        public static void InsertBatch(IList<(string correct, string typed)> errors, string title)
        {
            if (!_initialized || errors == null || errors.Count == 0) return;
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
INSERT INTO error_log (when_utc, correct, typed, title) VALUES (@w, @c, @t, @ti);";
                        var pW  = cmd.Parameters.Add("@w",  System.Data.DbType.String);
                        var pC  = cmd.Parameters.Add("@c",  System.Data.DbType.String);
                        var pT  = cmd.Parameters.Add("@t",  System.Data.DbType.String);
                        var pTi = cmd.Parameters.Add("@ti", System.Data.DbType.String);
                        string now = DateTime.Now.ToString("o");
                        foreach (var e in errors)
                        {
                            pW.Value  = now;
                            pC.Value  = e.correct ?? string.Empty;
                            pT.Value  = (object)e.typed ?? DBNull.Value;
                            pTi.Value = (object)title ?? DBNull.Value;
                            cmd.ExecuteNonQuery();
                        }
                        tx.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ErrorBookRepository.InsertBatch] " + ex.Message);
            }
        }

        /// <summary>
        /// 批量更新逐字"对/错"统计（char_stat）：对每个原文字累加被打次数，
        /// 错则错数 +1、连对清零并记录时间；对则连对 +1。用于错误率排序与"连对自动毕业"淡出。
        /// </summary>
        public static void UpsertBatch(IList<(string correct, bool wrong)> chars)
        {
            if (!_initialized || chars == null || chars.Count == 0) return;
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
INSERT INTO char_stat (correct, typed_total, wrong_total, streak, mastered, last_wrong_utc)
VALUES (@c, 1, @w, @s0, 0, @lw)
ON CONFLICT(correct) DO UPDATE SET
  typed_total    = typed_total + 1,
  wrong_total    = wrong_total + @w,
  streak         = CASE WHEN @w = 1 THEN 0 ELSE streak + 1 END,
  last_wrong_utc = CASE WHEN @w = 1 THEN @lw ELSE last_wrong_utc END;";
                        var pC  = cmd.Parameters.Add("@c",  System.Data.DbType.String);
                        var pW  = cmd.Parameters.Add("@w",  System.Data.DbType.Int32);
                        var pS0 = cmd.Parameters.Add("@s0", System.Data.DbType.Int32);
                        var pLw = cmd.Parameters.Add("@lw", System.Data.DbType.String);
                        string now = DateTime.Now.ToString("o");
                        foreach (var e in chars)
                        {
                            if (string.IsNullOrEmpty(e.correct)) continue;
                            pC.Value  = e.correct;
                            pW.Value  = e.wrong ? 1 : 0;
                            pS0.Value = e.wrong ? 0 : 1;   // 首次插入时的连对初值
                            pLw.Value = now;
                            cmd.ExecuteNonQuery();
                        }
                        tx.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ErrorBookRepository.UpsertBatch] " + ex.Message);
            }
        }

        /// <summary>把时间范围换算成下界（本地时间）；All 返回 DateTime.MinValue。</summary>
        private static DateTime RangeLowerBound(ErrorRange range)
        {
            var now = DateTime.Now;
            switch (range)
            {
                case ErrorRange.Session: return ProcessStart;
                case ErrorRange.Day:     return now.Date;
                case ErrorRange.Week:
                    // 周一为一周起点
                    int diff = ((int)now.DayOfWeek + 6) % 7;
                    return now.Date.AddDays(-diff);
                case ErrorRange.Month:   return new DateTime(now.Year, now.Month, 1);
                case ErrorRange.Year:    return new DateTime(now.Year, 1, 1);
                default:                 return DateTime.MinValue;
            }
        }

        /// <summary>按时间范围临时聚合错字排行（正确字+打成字分组计数，次数倒序）。</summary>
        public static List<ErrorStat> QueryRanking(ErrorRange range, int limit = 200)
        {
            var list = new List<ErrorStat>();
            if (!_initialized) return list;
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
SELECT e.correct, e.typed, COUNT(*) AS cnt, MAX(e.when_utc) AS last_t,
       COALESCE(s.streak, 0)      AS streak,
       COALESCE(s.wrong_total, 0) AS wtot,
       COALESCE(s.typed_total, 0) AS ttot,
       COALESCE(s.mastered, 0)    AS mast
FROM error_log e
LEFT JOIN char_stat s ON s.correct = e.correct
WHERE e.when_utc >= @lb
GROUP BY e.correct, e.typed
ORDER BY cnt DESC, last_t DESC
LIMIT @lim;";
                        cmd.Parameters.AddWithValue("@lb", RangeLowerBound(range).ToString("o"));
                        cmd.Parameters.AddWithValue("@lim", limit);
                        using (var rd = cmd.ExecuteReader())
                        {
                            while (rd.Read())
                            {
                                DateTime lt;
                                if (rd.IsDBNull(3) || !DateTime.TryParse(rd.GetString(3), out lt)) lt = DateTime.Now;
                                list.Add(new ErrorStat
                                {
                                    Correct    = rd.IsDBNull(0) ? "" : rd.GetString(0),
                                    Typed      = rd.IsDBNull(1) ? "" : rd.GetString(1),
                                    Count      = (int)rd.GetInt64(2),
                                    LastTime   = lt,
                                    Streak     = (int)rd.GetInt64(4),
                                    WrongTotal = (int)rd.GetInt64(5),
                                    TypedTotal = (int)rd.GetInt64(6),
                                    Mastered   = rd.GetInt64(7) != 0,
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ErrorBookRepository.QueryRanking] " + ex.Message);
            }
            return list;
        }

        /// <summary>手动标记/取消某个正确字"已掌握"（从默认榜淡出）。char_stat 无该字时自动补建。</summary>
        public static void MarkMastered(string correct, bool mastered)
        {
            if (!_initialized || string.IsNullOrEmpty(correct)) return;
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
INSERT INTO char_stat (correct, typed_total, wrong_total, streak, mastered, last_wrong_utc)
VALUES (@c, 0, 0, 0, @m, NULL)
ON CONFLICT(correct) DO UPDATE SET mastered = @m;";
                        cmd.Parameters.AddWithValue("@c", correct);
                        cmd.Parameters.AddWithValue("@m", mastered ? 1 : 0);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ErrorBookRepository.MarkMastered] " + ex.Message);
            }
        }

        /// <summary>彻底删除某个正确字的全部记录（明细 + 统计）。返回删除的明细条数。</summary>
        public static int DeleteChar(string correct)
        {
            if (!_initialized || string.IsNullOrEmpty(correct)) return 0;
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    int n;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM error_log WHERE correct = @c;";
                        cmd.Parameters.AddWithValue("@c", correct);
                        n = cmd.ExecuteNonQuery();
                    }
                    using (var cmd2 = conn.CreateCommand())
                    {
                        cmd2.CommandText = "DELETE FROM char_stat WHERE correct = @c;";
                        cmd2.Parameters.AddWithValue("@c", correct);
                        cmd2.ExecuteNonQuery();
                    }
                    return n;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ErrorBookRepository.DeleteChar] " + ex.Message);
                return 0;
            }
        }

        /// <summary>清空指定时间范围内的错字记录；All 清空全部。返回删除条数。</summary>
        public static int Clear(ErrorRange range)
        {
            if (!_initialized) return 0;
            try
            {
                using (var conn = new SQLiteConnection(ConnStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        if (range == ErrorRange.All)
                        {
                            cmd.CommandText = "DELETE FROM error_log; DELETE FROM char_stat;";
                        }
                        else
                        {
                            cmd.CommandText = "DELETE FROM error_log WHERE when_utc >= @lb;";
                            cmd.Parameters.AddWithValue("@lb", RangeLowerBound(range).ToString("o"));
                        }
                        return cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ErrorBookRepository.Clear] " + ex.Message);
                return 0;
            }
        }
    }
}
