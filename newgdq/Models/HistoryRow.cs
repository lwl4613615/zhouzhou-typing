namespace newgdq.Models
{
    /// <summary>
    /// 历史成绩行（绑定到主窗口底部 DataGrid）
    /// </summary>
    public class HistoryRow
    {
        public int    Index    { get; set; }
        public string Time     { get; set; }
        public string Seg      { get; set; }
        public double Speed    { get; set; }
        public double Jj       { get; set; }
        public double Mc       { get; set; }
        public int    Hg       { get; set; }
        public int    Cz       { get; set; }
        public int    Js       { get; set; }
        public int    Words    { get; set; }
        public int    DaCi     { get; set; }
        public double UseTime  { get; set; }
    }
}
