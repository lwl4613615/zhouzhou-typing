using System.Runtime.Serialization;

namespace newgdq.Models
{
    /// <summary>
    /// 自定义文章"续打进度"持久化记录（只存最近 1 篇）。
    /// 仅服务 文章(Article) + 顺序(非乱序) 场景；乱序 / 词组 / 单字 / 群比赛云文不写。
    /// 旧 settings.json 无此字段 → 反序列化为 null = 无记录。
    /// </summary>
    [DataContract]
    public class SendResumeProgress
    {
        [DataMember] public string ArticleKind { get; set; } = "";   // "Builtin" / "CustomFile" / "Clipboard"
        [DataMember] public string ArticleId   { get; set; } = "";   // 自带=资源名；本地=绝对路径；剪贴板=正文指纹
        [DataMember] public string TextHash    { get; set; } = "";   // 最终正文 SHA-256（UTF8，小写 hex）
        [DataMember] public string Title       { get; set; } = "";
        [DataMember] public int    Type        { get; set; }          // SendingTextType（Article=2）
        [DataMember] public bool   IsRandom    { get; set; }
        [DataMember] public int    CountPerSeg { get; set; }
        [DataMember] public bool   OneSentenceEnd { get; set; }
        [DataMember] public int    StartSeg    { get; set; }
        [DataMember] public int    InitialMark { get; set; }          // 会话起始 Mark（用户"起始位置"）
        [DataMember] public bool   TickOut     { get; set; }          // 是否自动去空格
        [DataMember] public int    ResumeSegNo { get; set; }          // 上次停在的段号
        [DataMember] public string UpdatedAt   { get; set; } = "";    // ISO 8601
    }
}
