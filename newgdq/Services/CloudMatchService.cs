using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace newgdq.Services
{
    /// <summary>
    /// 群比赛云中转客户端：F4 抓文 + 打完上传成绩。
    /// 设计要点：
    ///   1. 个人码（PersonalCode）只在内存，关程序即失，绝不写 settings.json（防明文泄漏被冒名）。
    ///   2. 本场口令 / 云地址走 SettingsService（口令群里公开，存本地无妨）。
    ///   3. "一场一交卷"：本机记下已成功上传过的 (口令) ，再交直接拦（防顺手重打刷分）。
    ///      真正的硬保证在云端（同口令同人只收第一条）；这里只是客户端摩擦。
    /// </summary>
    public static class CloudMatchService
    {
        // 个人码：仅本次运行内存缓存，不持久化。
        // 设备路径上传成绩已不再使用个人码（保留字段供 QQ 桥备份场景，界面后续移除入口）。
        public static string PersonalCode { get; set; }

        // 抓来的发文内容上限：防云端被攻破/中间人返回超大正文撑爆内存。
        private const int MaxArticleContentLength = 100_000;

        // 当前 F4 抓到的比赛文对应的本场口令（非空 = 当前正在跟打云比赛文）。
        public static string CurrentArticleToken { get; private set; }
        // 当前比赛文标题（仅展示用）。
        public static string CurrentArticleTitle { get; private set; }
        // 当前比赛文模式：'match'（一场一交卷）| 'daily'（日经文，可反复刷最好成绩）。
        public static string CurrentArticleMode { get; private set; }

        // 最近一次 FetchArticleAsync 解析出的模式（SetCurrentArticle 未显式传 mode 时带入）。
        public static string LastFetchedMode { get; private set; }

        /// <summary>当前是否为 daily（日经）模式。daily 不锁交卷，可反复提交刷新最好成绩。</summary>
        public static bool IsDailyArticle =>
            string.Equals(CurrentArticleMode, "daily", StringComparison.OrdinalIgnoreCase);

        // daily 最小提交间隔（毫秒）：防脚本式/轮询式自动连发，确保是人手动打完才交。
        private const int DailyMinIntervalMs = 5000;
        // 上次成功提交的时间戳（用于 daily 最小间隔护栏）。
        private static DateTime _lastSubmitUtc = DateTime.MinValue;

        // 本机本次运行已成功交卷的口令集合（防重复上传；仅 match 模式记录/拦截）。
        private static readonly HashSet<string> _submittedTokens = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>上传成绩结果（供界面层据此显示反馈）。</summary>
        public struct UploadResult
        {
            public bool Ok;            // 服务端是否登记成功
            public string Name;        // 服务端登记的显示名
            public bool IsDaily;       // 本次是否 daily 模式
            public bool IsDuplicate;   // match 模式：本场是否已交过（409 / 客户端拦截）
            public bool Improved;      // daily 模式：本次是否超越历史最好
            public double? Old;        // daily：旧成绩值
            public double? New;        // daily：本次成绩值
            public double? Best;       // daily：当前最好成绩值
        }

        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        /// <summary>当前段是否为 F4 抓来的云比赛文（用于决定是否锁 F3/自动上传）。</summary>
        public static bool IsMatchArticleLoaded => !string.IsNullOrEmpty(CurrentArticleToken);

        /// <summary>清掉"当前比赛文"标记（换普通文/复位时调用）。</summary>
        public static void ClearCurrentArticle()
        {
            CurrentArticleToken = null;
            CurrentArticleTitle = null;
            CurrentArticleMode = null;
        }

        /// <summary>把当前段标记为比赛文（F4 抓文并 LoadArticle 之后调用，置位后才锁键/自动上传）。
        /// mode 缺省时取最近一次 FetchArticleAsync 解析到的 <see cref="LastFetchedMode"/>。</summary>
        public static void SetCurrentArticle(string token, string title, string mode = null)
        {
            CurrentArticleToken = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
            CurrentArticleTitle = title;
            string m = string.IsNullOrWhiteSpace(mode) ? LastFetchedMode : mode;
            CurrentArticleMode = string.IsNullOrWhiteSpace(m) ? "match" : m.Trim().ToLowerInvariant();
        }

        /// <summary>该口令本机是否已交过卷（仅 match 模式有意义；daily 不锁）。</summary>
        public static bool HasSubmitted(string token)
            => !string.IsNullOrEmpty(token) && _submittedTokens.Contains(token);

        private static string BaseUrl()
        {
            var url = (SettingsService.Instance.CloudUrl ?? string.Empty).Trim();
            return url.TrimEnd('/');
        }

        /// <summary>校验云地址：必须是 https 绝对地址。明文 http 会被中间人窃听/篡改发文与成绩。</summary>
        private static void EnsureSecureBaseUrl(string baseUrl)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                || !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("云地址必须以 https:// 开头（左侧『群比赛』里改）");
        }

        /// <summary>F4 抓文：GET {url}/article?token=本场口令。成功返回 (标题, 正文, 口令)，失败抛异常带原因。
        /// 解析出的模式 'match'|'daily' 暂存到 <see cref="LastFetchedMode"/>，调用方 SetCurrentArticle 时会带入。
        /// 注意：本方法不置"当前比赛文"标记，调用方在 LoadArticle 之后再调 SetCurrentArticle 置位。</summary>
        public static async Task<(string title, string content, string token)> FetchArticleAsync(string token)
        {
            string baseUrl = BaseUrl();
            if (string.IsNullOrEmpty(baseUrl))
                throw new InvalidOperationException("还没配置云地址（左侧『群比赛』里填）");
            EnsureSecureBaseUrl(baseUrl);
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("还没填本场口令（群里发文后机器人会公布 5 位口令）");
            // 云端本场口令字符集全大写、且区分大小写比较（不像个人码会 toUpperCase）。
            // 客户端统一归一为大写，避免群友输入小写口令时被误判为"本场已结束"。
            token = token.Trim().ToUpperInvariant();
            string reqUrl = baseUrl + "/article?token=" + Uri.EscapeDataString(token);
            string body;
            try
            {
                using (var resp = await _http.GetAsync(reqUrl).ConfigureAwait(false))
                {
                    body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("连不上云服务器：" + ex.Message);
            }

            JsonDocument doc;
            try { doc = JsonDocument.Parse(body); }
            catch (JsonException) { throw new InvalidOperationException("服务器返回了无法识别的内容（可能是网络/网关异常），请稍后再试"); }
            using (doc)
            {
                var root = doc.RootElement;
                bool ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
                if (!ok)
                {
                    string err = root.TryGetProperty("err", out var e) ? e.GetString() : "抓文失败";
                    throw new InvalidOperationException(err ?? "抓文失败");
                }
                if (!root.TryGetProperty("article", out var art) || art.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("本场还没有发文");

                string title   = art.TryGetProperty("title", out var t) ? (t.GetString() ?? "") : "";
                string content = art.TryGetProperty("content", out var c) ? (c.GetString() ?? "") : "";
                if (string.IsNullOrEmpty(content))
                    throw new InvalidOperationException("本场发文内容为空");
                if (content.Length > MaxArticleContentLength)
                    throw new InvalidOperationException($"发文内容过长（超过 {MaxArticleContentLength} 字），已拒绝载入");

                // mode 可能在 article 内或根上；缺省按 match。暂存供 SetCurrentArticle 带入。
                string mode = null;
                if (art.TryGetProperty("mode", out var mArt) && mArt.ValueKind == JsonValueKind.String)
                    mode = mArt.GetString();
                else if (root.TryGetProperty("mode", out var mRoot) && mRoot.ValueKind == JsonValueKind.String)
                    mode = mRoot.GetString();
                LastFetchedMode = string.IsNullOrWhiteSpace(mode) ? "match" : mode.Trim().ToLowerInvariant();

                return (title, content, token.Trim());
            }
        }

        /// <summary>群比赛昵称（设备路径上传成绩的显示名）。存 settings（昵称非口令/密钥，落地无妨）。</summary>
        public static string Nickname
        {
            get => SettingsService.Instance.CloudNickname ?? string.Empty;
            set
            {
                SettingsService.Instance.CloudNickname = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                SettingsService.Save();
            }
        }

        /// <summary>上传成绩：POST {url}/score（设备路径，body 带 deviceId+name）。
        /// match：成功登记 / 重复交卷（IsDuplicate）。daily：返回是否超越（Improved）及 old/new/best，不锁可反复刷。
        /// 调用方须保证当前段是比赛文（IsMatchArticleLoaded）。</summary>
        public static async Task<UploadResult> UploadScoreAsync(
            double speed, double jj, double mc, int cz, double useTime)
        {
            string baseUrl = BaseUrl();
            if (string.IsNullOrEmpty(baseUrl))
                throw new InvalidOperationException("还没配置云地址");
            EnsureSecureBaseUrl(baseUrl);
            string token = CurrentArticleToken;
            if (string.IsNullOrEmpty(token))
                throw new InvalidOperationException("当前不是比赛文，无需上传");

            string name = Nickname;
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("还没填昵称（群比赛设置里填一个昵称再交卷）");

            string deviceId = DeviceIdentity.GetDeviceId();
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new InvalidOperationException("拿不到设备标识，无法交卷");

            bool isDaily = IsDailyArticle;

            // match：本机一场一交卷锁。
            if (!isDaily && _submittedTokens.Contains(token))
                throw new InvalidOperationException("本场你已交过卷了（一场只能交一次）");

            // daily 护栏：客户端最小提交间隔，防脚本式/轮询式自动连发。
            if (isDaily)
            {
                double sinceMs = (DateTime.UtcNow - _lastSubmitUtc).TotalMilliseconds;
                if (sinceMs < DailyMinIntervalMs)
                {
                    int waitSec = (int)Math.Ceiling((DailyMinIntervalMs - sinceMs) / 1000.0);
                    throw new InvalidOperationException("交得太快啦，手动打完再交（" + waitSec + " 秒后可再交）");
                }
            }

            var payload = new
            {
                deviceId = deviceId,
                name     = name,
                token    = token,
                speed    = Math.Round(speed, 2),
                jj       = Math.Round(jj, 2),
                mc       = Math.Round(mc, 2),
                cz       = cz,
                useTime  = Math.Round(useTime, 2),
            };
            string json = JsonSerializer.Serialize(payload);

            string respBody;
            int statusCode;
            try
            {
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (var resp = await _http.PostAsync(baseUrl + "/score", content).ConfigureAwait(false))
                {
                    statusCode = (int)resp.StatusCode;
                    respBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("连不上云服务器：" + ex.Message);
            }

            JsonDocument doc;
            try { doc = JsonDocument.Parse(respBody); }
            catch (JsonException) { throw new InvalidOperationException("服务器返回了无法识别的内容（可能是网络/网关异常），请稍后再试"); }
            using (doc)
            {
                var root = doc.RootElement;

                // match 重复交卷：服务端 409。
                if (statusCode == 409)
                {
                    if (!isDaily) _submittedTokens.Add(token);
                    return new UploadResult
                    {
                        Ok = false,
                        IsDaily = isDaily,
                        IsDuplicate = true,
                        Name = root.TryGetProperty("name", out var dn) ? (dn.GetString() ?? "") : "",
                    };
                }

                bool ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
                if (!ok)
                {
                    string err = root.TryGetProperty("err", out var e) ? e.GetString() : "上传失败";
                    throw new InvalidOperationException(err ?? "上传失败");
                }

                var result = new UploadResult
                {
                    Ok = true,
                    IsDaily = isDaily,
                    IsDuplicate = false,
                    Name = root.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "",
                };

                if (isDaily)
                {
                    result.Improved = root.TryGetProperty("improved", out var imp) && imp.ValueKind == JsonValueKind.True;
                    result.Old  = ReadNullableNumber(root, "old");
                    result.New  = ReadNullableNumber(root, "new");
                    result.Best = ReadNullableNumber(root, "best");
                    _lastSubmitUtc = DateTime.UtcNow;   // daily 不锁，仅刷新最小间隔时间戳
                }
                else
                {
                    _submittedTokens.Add(token);        // match：记下已交卷，防本机重复上传
                    _lastSubmitUtc = DateTime.UtcNow;
                }

                return result;
            }
        }

        private static double? ReadNullableNumber(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
                && el.TryGetDouble(out var v))
                return v;
            return null;
        }
    }
}
