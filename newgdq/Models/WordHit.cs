using System.Collections.Generic;

namespace newgdq.Models
{
    /// <summary>
    /// 分词命中记录：在文章中 Start 起，长度 Length 的子串是个词。
    /// </summary>
    public class WordHit
    {
        public int Start  { get; set; }
        public int Length { get; set; }
    }
}
