using System;

namespace newgdq.Models
{
    /// <summary>
    /// 发文模式（替代原版"类型"字符串）。
    /// </summary>
    public enum SendingTextType
    {
        Auto    = 0,  // 自动识别
        Single  = 1,  // 单字
        Article = 2,  // 文章（含标点）
        Word    = 3,  // 词组（每行/每个词作为单位）
    }

    /// <summary>
    /// 词组分隔策略。
    /// </summary>
    public enum WordSplitMode
    {
        ByLine   = 0,  // 每行一词
        BySplit  = 1,  // 按指定字符切分
    }

    /// <summary>
    /// 一次发文的状态（替代原版 NewSendText 静态字段）。
    /// 只在内存中保留，不持久化（持久化进度由 P5 SQLite 接管）。
    /// </summary>
    public class SendingState
    {
        public bool   Active;        // 是否正在发文流程中
        public string SourceName = "-";  // 文章来源（自带文章 / 自定义文章 / 来自剪切板）
        public string Title = "-";
        public string FullText = ""; // 文章全文
        public string PoolText = ""; // 当前剩余池（乱序全段不重复时会消减）

        public SendingTextType Type = SendingTextType.Article;

        // 词组模式
        public string[]    Words;
        public string      WordSep = "，";
        public WordSplitMode SplitMode = WordSplitMode.ByLine;
        public string      OtherSplit = " ";

        // 模式选项
        public bool IsRandom;            // 是否乱序
        public bool RandomNoRepeat;      // 乱序全段不重复
        public bool OneSentenceEnd;      // 文章模式：以一句结束

        // 进度
        public int StartSeg = 1;         // 起始段号
        public int SentSeg;              // 已发段数
        public int Mark;                 // 标记（顺序模式下的当前位置）
        public int CountPerSeg = 25;     // 每段字数

        public int CurSeg => StartSeg + SentSeg;
    }
}
