namespace newgdq.Models
{
    /// <summary>
    /// 段内一次输入事件（对应原版 Glob.TypeDate）。
    /// 每次输入框 TextChanged 触发追加一条；回改时 Length 为负。
    /// 给"跟打报告"窗口（P5）用。
    /// </summary>
    public class TypeDate
    {
        public int    Index     { get; set; }   // 第几次输入事件
        public int    Start     { get; set; }   // 文章中的起点字符索引
        public int    End       { get; set; }   // 文章中的终点字符索引
        public int    Length    { get; set; }   // End - Start（回改时为负）
        public double NowTime   { get; set; }   // 累计用时（秒）
        public double TotalTime { get; set; }   // 本次输入耗时
        public int    Tick      { get; set; }   // 累计键数
        public int    TotalTick { get; set; }   // 本次输入键数
    }
}
