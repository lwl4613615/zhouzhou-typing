namespace newgdq.Models
{
    /// <summary>
    /// 历史成绩行（绑定到主窗口底部 DataGrid + 持久化到 SQLite）
    /// </summary>
    public class HistoryRow
    {
        public int    Index    { get; set; }
        public string Time     { get; set; }   // 显示用 "HH:mm:ss"
        public string Seg      { get; set; }
        public double Speed    { get; set; }
        public double Speed2   { get; set; }   // 错一罚五速度
        public double Jj       { get; set; }
        public double Mc       { get; set; }
        public int    Hg       { get; set; }
        public int    Cz       { get; set; }
        public int    Js       { get; set; }
        public int    Words    { get; set; }
        public int    DaCi     { get; set; }
        public double UseTime  { get; set; }

        // 扩展字段（v2 后新增）
        public int    Reselect { get; set; }   // 选重次数
        public int    Enter    { get; set; }   // 回车次数
        public int    LeftHand { get; set; }   // 左手击键
        public int    RightHand{ get; set; }   // 右手击键

        // 派生：DataGrid 绑定的"左:右"字符串
        public string LeftRight => LeftHand + ":" + RightHand;

        // 持久化用（不展示）
        public System.DateTime When  { get; set; }   // 完整时间戳（含日期）
        public string          Title { get; set; }   // 文段标题
    }
}
