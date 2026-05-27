using System.Runtime.Serialization;

namespace newgdq.Models
{
    /// <summary>
    /// 发文参数预设：命名保存的常用发文配置。
    /// 用户在 SendTextWindow → 配置与设置 Tab 管理；点 "应用到当前" 把这些值灌到主参数区。
    /// </summary>
    [DataContract]
    public class SendPreset
    {
        [DataMember] public string Name { get; set; } = "未命名";
        [DataMember] public int    CountPerSeg { get; set; } = 25;
        [DataMember] public int    StartSeg    { get; set; } = 1;
        [DataMember] public int    Mark        { get; set; } = 0;
        [DataMember] public bool   IsRandom       { get; set; }
        [DataMember] public bool   RandomNoRepeat { get; set; }
        [DataMember] public bool   OneSentenceEnd { get; set; }
        [DataMember] public bool   TickOut        { get; set; }   // 去空格
    }
}
