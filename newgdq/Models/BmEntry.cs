namespace newgdq.Models
{
    /// <summary>词典条目：一个字/词 对应一条编码。</summary>
    public class BmEntry
    {
        public string Word { get; set; }   // 单字或词组
        public string Code { get; set; }   // 编码
        public int    Rank { get; set; }   // 重数（同码中的位置，1 = 首选）
    }
}
