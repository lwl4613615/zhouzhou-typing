using System.Security.Cryptography;
using System.Text;
using newgdq.Models;

namespace newgdq.Services
{
    /// <summary>
    /// 自定义文章"续打进度"辅助：算正文指纹、由会话状态构造身份、判定续打是否有效。
    /// 纯函数、无 UI 依赖，便于单测。仅服务 文章(Article) + 顺序(非乱序) 场景。
    /// </summary>
    public static class ResumeProgressService
    {
        /// <summary>对最终进入发文的正文（已含去空格等处理）算 SHA-256（UTF8），返回小写 hex。</summary>
        public static string ComputeTextHash(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>由当前发文会话状态构造续打身份记录（ResumeSegNo / UpdatedAt 由调用方补）。</summary>
        public static SendResumeProgress BuildIdentity(SendingState s)
        {
            string textHash = ComputeTextHash(s.FullText);
            string kind = string.IsNullOrEmpty(s.ArticleKind) ? "" : s.ArticleKind;
            string id;
            switch (kind)
            {
                case "CustomFile":
                    id = NormalizePath(s.ArticleId);
                    break;
                case "Clipboard":
                    id = textHash;          // 剪贴板无路径，以正文指纹作为身份
                    break;
                default:                    // Builtin / 未知
                    id = s.ArticleId ?? "";
                    break;
            }
            return new SendResumeProgress
            {
                ArticleKind    = kind,
                ArticleId      = id,
                TextHash       = textHash,
                Title          = s.Title ?? "",
                Type           = (int)s.Type,
                IsRandom       = s.IsRandom,
                CountPerSeg    = s.CountPerSeg,
                OneSentenceEnd = s.OneSentenceEnd,
                StartSeg       = s.StartSeg,
                InitialMark    = s.InitialMark,
                TickOut        = s.TickOut,
            };
        }

        /// <summary>判定记录 rec 对当前会话身份 current 是否构成有效续打。
        /// totalSegments = 当前会话可枚举的总段数（SendingService.EnumerateSegments().Count）。
        /// 任一身份/切法不符、段号越界 → false。</summary>
        public static bool IsResumeValid(SendResumeProgress rec, SendResumeProgress current, int totalSegments)
        {
            if (rec == null || current == null) return false;
            if (current.Type != (int)SendingTextType.Article) return false;
            if (rec.Type != (int)SendingTextType.Article) return false;
            if (rec.IsRandom || current.IsRandom) return false;
            if (!StrEqIgnoreCase(rec.ArticleKind, current.ArticleKind)) return false;
            if (!StrEqIgnoreCase(rec.ArticleId, current.ArticleId)) return false;
            if (!StrEqOrdinal(rec.TextHash, current.TextHash)) return false;
            if (rec.CountPerSeg != current.CountPerSeg) return false;
            if (rec.OneSentenceEnd != current.OneSentenceEnd) return false;
            if (rec.StartSeg != current.StartSeg) return false;
            if (rec.InitialMark != current.InitialMark) return false;
            if (rec.TickOut != current.TickOut) return false;
            int first = rec.StartSeg;
            int last  = rec.StartSeg + totalSegments - 1;
            if (rec.ResumeSegNo < first) return false;
            if (rec.ResumeSegNo > last)  return false;
            return true;
        }

        private static string NormalizePath(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            try { return System.IO.Path.GetFullPath(p); }
            catch { return p; }
        }

        private static bool StrEqIgnoreCase(string a, string b)
            => string.Equals(a ?? "", b ?? "", System.StringComparison.OrdinalIgnoreCase);

        private static bool StrEqOrdinal(string a, string b)
            => string.Equals(a ?? "", b ?? "", System.StringComparison.Ordinal);
    }
}
