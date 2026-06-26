using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using newgdq.Services;

namespace newgdq.Views
{
    /// <summary>
    /// 慢字本（弱项中心）内嵌视图 —— 仿 ErrorBookView，从 slowchar.db 按时间范围聚合弱项排行
    /// （慢/回改/高键耗分列，按 WeakScore 倒序），帮用户定位手速弱点并一键生成针对练习。
    /// 「生成练习」闭环：调用主窗 LoadPracticeText 后退出分析页回到跟打区。
    /// </summary>
    public partial class SlowCharBookView : UserControl
    {
        private sealed class Row
        {
            public string Ch { get; set; }
            public int SlowCount { get; set; }
            public string AvgOverShow { get; set; }
            public int HgCount { get; set; }
            public int HighKeyCount { get; set; }
            public int ErrorCount { get; set; }
            public string WeakScoreShow { get; set; }
            public string MasteredShow { get; set; }
            public string LastShow { get; set; }
            public bool Mastered { get; set; }   // 供"标记/取消掌握"切换判断
        }

        public SlowCharBookView()
        {
            InitializeComponent();
            Loaded += (s, e) => Refresh();
        }

        private TimeRange CurrentRange()
        {
            switch (CmbRange.SelectedIndex)
            {
                case 0: return TimeRange.Last7;
                case 1: return TimeRange.Last30;
                default: return TimeRange.All;
            }
        }

        private void Refresh()
        {
            if (Grid == null) return;
            bool hide = ChkHideMastered == null || ChkHideMastered.IsChecked == true;
            var stats = SlowCharRepository.LoadRanking(CurrentRange(), hide);   // 已按 WeakScore 倒序
            var rows = stats.Select(s => new Row
            {
                Ch            = s.Ch,
                SlowCount     = s.SlowCount,
                AvgOverShow   = s.SlowCount > 0 ? s.AvgOverSec.ToString("0.0") + "s" : "-",
                HgCount       = s.HgCount,
                HighKeyCount  = s.HighKeyCount,
                ErrorCount    = s.ErrorCount,
                WeakScoreShow = s.WeakScore.ToString("0.0"),
                MasteredShow  = s.Mastered ? "✓" : "",
                LastShow      = s.LastSeen.ToString("MM-dd HH:mm"),
                Mastered      = s.Mastered,
            }).ToList();
            Grid.ItemsSource = rows;

            int totalSlow = stats.Sum(s => s.SlowCount);
            int totalHg = stats.Sum(s => s.HgCount);
            int totalHk = stats.Sum(s => s.HighKeyCount);
            TxtTitle.Text = rows.Count == 0
                ? "这个范围内没有明显的慢字，手速很稳 ~"
                : $"共 {rows.Count} 个弱项字 · 慢打 {totalSlow} 次 · 回改 {totalHg} · 高键耗 {totalHk}";
        }

        private static string ShowChar(string s)
        {
            if (string.IsNullOrEmpty(s)) return "∅";
            if (s == " ") return "␣（空格）";
            if (s == "\t") return "⇥（Tab）";
            return s;
        }

        private void CmbRange_SelectionChanged(object sender, SelectionChangedEventArgs e) => Refresh();

        private void ChkHideMastered_Changed(object sender, RoutedEventArgs e) => Refresh();

        /// <summary>选中某行 → 查该字来源文章分布，显示标题/次数/最近时间/样例上下文/位置。</summary>
        private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TxtSrc == null || TxtSrcTitle == null) return;
            var row = Grid.SelectedItem as Row;
            if (row == null || string.IsNullOrEmpty(row.Ch))
            {
                TxtSrcTitle.Text = "慢字溯源（点选左侧某字查看）";
                TxtSrc.Text = "—";
                return;
            }
            var sources = SlowCharRepository.LoadSources(row.Ch, CurrentRange());
            int total = sources.Sum(s => s.Count);
            TxtSrcTitle.Text = $"「{ShowChar(row.Ch)}」来源（共 {total} 次，{sources.Count} 篇）";
            if (sources.Count == 0)
            {
                TxtSrc.Text = "本范围内暂无来源记录";
                return;
            }
            var sb = new StringBuilder();
            foreach (var s in sources)
            {
                sb.AppendLine($"· {s.Title} ×{s.Count}（最近 {s.LastTime:MM-dd HH:mm}）");
                if (!string.IsNullOrWhiteSpace(s.SampleContext))
                    sb.AppendLine($"    …{s.SampleContext}…（第 {s.SamplePos + 1} 字）");
            }
            TxtSrc.Text = sb.ToString().TrimEnd();
        }

        /// <summary>标记 / 取消"已掌握"：按选中行当前状态翻转，刷新榜单。</summary>
        private void BtnMaster_Click(object sender, RoutedEventArgs e)
        {
            var row = Grid.SelectedItem as Row;
            if (row == null || string.IsNullOrEmpty(row.Ch))
            {
                Services.Toast.Info("请先在表格中选中一行");
                return;
            }
            bool target = !row.Mastered;
            SlowCharRepository.SetMastered(row.Ch, target);
            Services.Toast.Success(target
                ? $"「{ShowChar(row.Ch)}」已标为掌握，再慢会重新出现"
                : $"「{ShowChar(row.Ch)}」已取消掌握");
            Refresh();
        }

        private System.Collections.Generic.List<string> SelectedChars()
        {
            return Grid.SelectedItems
                .OfType<Row>()
                .Select(r => r.Ch)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .ToList();
        }

        /// <summary>慢字闭环：有选中则练选中的字，否则练当前范围全部弱项，送进主窗跟打区。空集则提示。</summary>
        private void BtnGenPractice_Click(object sender, RoutedEventArgs e)
        {
            var selected = SelectedChars();
            bool useSelected = selected.Count > 0;
            var (text, title) = useSelected
                ? SlowCharDrillBuilder.BuildFromChars(selected)
                : SlowCharDrillBuilder.BuildFromRange(CurrentRange());
            if (string.IsNullOrEmpty(text))
            {
                Services.Toast.Info(useSelected ? "选中的字无法生成练习" : "当前范围暂无弱项可练");
                return;
            }
            var main = System.Windows.Window.GetWindow(this) as newgdq.MainWindow;
            if (main == null)
            {
                Services.Toast.Warning("无法定位主窗口，请从主窗菜单打开慢字本");
                return;
            }
            if (!main.LoadPracticeText(text, title)) return;   // 用户在"覆盖确认"里取消 → 不退出分析页
            Services.Toast.Success($"已用{(useSelected ? $"选中 {selected.Count} 个字" : "全部弱项")}生成 {text.Length} 字练习，去主窗开打");
            main.CloseAnalysis();   // 退出分析页，回到跟打区
        }

        /// <summary>清空当前时间范围的慢字记录（仅动 slowchar.db，不影响错字本）。</summary>
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            var range = CurrentRange();
            string name = (CmbRange.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "本范围";
            var r = System.Windows.MessageBox.Show(
                $"确定清空「{name}」范围内的慢字记录？此操作不可撤销，且只影响慢字本（不动错字本）。",
                "清空慢字本", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (r != MessageBoxResult.OK) return;
            int n = SlowCharRepository.ClearRange(range);
            Services.Toast.Success($"已清空 {n} 条慢字记录");
            Refresh();
        }
    }
}
