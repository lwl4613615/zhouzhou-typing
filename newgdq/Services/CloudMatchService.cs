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
        public static string PersonalCode { get; set; }

        // 抓来的发文内容上限：防云端被攻破/中间人返回超大正文撑爆内存。
        private const int MaxArticleContentLength = 100_000;

        // 当前 F4 抓到的比赛文对应的本场口令（非空 = 当前正在跟打云比赛文）。
        public static string CurrentArticleToken { get; private set; }
        // 当前比赛文标题（仅展示用）。
        public static string CurrentArticleTitle { get; private set; }

        // 本机本次运行已成功交卷的口令集合（防重复上传）。
        private static readonly HashSet<string> _submittedTokens = new HashSet<string>(StringComparer.Ordinal);

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
        }

        /// <summary>把当前段标记为比赛文（F4 抓文并 LoadArticle 之后调用，置位后才锁键/自动上传）。</summary>
        public static void SetCurrentArticle(string token, string title)
        {
            CurrentArticleToken = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
            CurrentArticleTitle = title;
        }

        /// <summary>该口令本机是否已交过卷。</summary>
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
        /// 注意：本方法不置"当前比赛文"标记，调用方在 LoadArticle 之后再调 SetCurrentArticle 置位。</summary>
        public static async Task<(string title, string content, string token)> FetchArticleAsync(string token)
        {
            string baseUrl = BaseUrl();
            if (string.IsNullOrEmpty(baseUrl))
                throw new InvalidOperationException("还没配置云地址（左侧『群比赛』里填）");
            EnsureSecureBaseUrl(baseUrl);
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("还没填本场口令（群里发文后机器人会公布 5 位口令）");

            string reqUrl = baseUrl + "/article?token=" + Uri.EscapeDataString(token.Trim());
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

            using (var doc = JsonDocument.Parse(body))
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

                return (title, content, token.Trim());
            }
        }

        /// <summary>上传成绩：POST {url}/score。成功返回服务器登记的显示名；失败抛异常带原因。
        /// 调用方须保证当前段是比赛文（IsMatchArticleLoaded）。</summary>
        public static async Task<string> UploadScoreAsync(
            double speed, double jj, double mc, int cz, double useTime)
        {
            string baseUrl = BaseUrl();
            if (string.IsNullOrEmpty(baseUrl))
                throw new InvalidOperationException("还没配置云地址");
            EnsureSecureBaseUrl(baseUrl);
            string token = CurrentArticleToken;
            if (string.IsNullOrEmpty(token))
                throw new InvalidOperationException("当前不是比赛文，无需上传");
            if (string.IsNullOrWhiteSpace(PersonalCode))
                throw new InvalidOperationException("还没填个人码（群里发『领码』，机器人私聊发你）");
            if (_submittedTokens.Contains(token))
                throw new InvalidOperationException("本场你已交过卷了（一场只能交一次）");

            var payload = new
            {
                code    = PersonalCode.Trim(),
                token   = token,
                speed   = Math.Round(speed, 2),
                jj      = Math.Round(jj, 2),
                mc      = Math.Round(mc, 2),
                cz      = cz,
                useTime = Math.Round(useTime, 2),
            };
            string json = JsonSerializer.Serialize(payload);

            string respBody;
            try
            {
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (var resp = await _http.PostAsync(baseUrl + "/score", content).ConfigureAwait(false))
                {
                    respBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("连不上云服务器：" + ex.Message);
            }

            using (var doc = JsonDocument.Parse(respBody))
            {
                var root = doc.RootElement;
                bool ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
                if (!ok)
                {
                    string err = root.TryGetProperty("err", out var e) ? e.GetString() : "上传失败";
                    throw new InvalidOperationException(err ?? "上传失败");
                }
                _submittedTokens.Add(token);   // 记下已交卷，防本机重复上传
                return root.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
            }
        }
    }
}
