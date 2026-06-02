using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using newgdq.Services;

namespace newgdq.Views
{
    /// <summary>
    /// 错字本窗口 —— 从独立 errorbook.db 按时间范围（本次/本日/本周/本月/本年/全部）
    /// 临时聚合"正确字→打成字"错误排行，帮用户定位高频错字。
    /// </summary>
    public partial class ErrorBookWindow : HandyControl.Controls.Window
    {
        /// <summary>DataGrid 行视图模型。</summary>
        private sealed class Row
        {
            public string Correct { get; set; }
            public string TypedShow { get; set; }   // 打成的字（空格/空显示占位）
            public int Count { get; set; }
            public string StreakShow { get; set; }   // 连对次数（达阈加✓）
            public string RateShow { get; set; }      // 累计错误率
            public string LastShow { get; set; }
        }

        public ErrorBookWindow(Window owner)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            // 构造里 CmbRange 默认选中"本周"，SelectionChanged 会触发首次刷新
        }

        private ErrorRange CurrentRange()
        {
            switch (CmbRange.SelectedIndex)
            {
                case 0: return ErrorRange.Session;
                case 1: return ErrorRange.Day;
                case 2: return ErrorRange.Week;
                case 3: return ErrorRange.Month;
                case 4: return ErrorRange.Year;
                default: return ErrorRange.All;
            }
        }

        private void Refresh()
        {
            if (!IsLoaded && Grid == null) return;
            var stats = ErrorBookRepository.QueryRanking(CurrentRange());
            bool hide = ChkHideMastered == null || ChkHideMastered.IsChecked == true;
            if (hide)
                stats = stats.Where(s => !s.Mastered && s.Streak < ErrorBookRepository.MasterStreak).ToList();
            var rows = stats.Select(s => new Row
            {
                Correct    = s.Correct,
                TypedShow  = ShowChar(s.Typed),
                Count      = s.Count,
                StreakShow = s.Mastered ? "✓掌握" : (s.Streak >= ErrorBookRepository.MasterStreak ? "✓" + s.Streak : s.Streak.ToString()),
                RateShow   = s.TypedTotal > 0 ? (s.WrongRate * 100).ToString("0") + "%" : "-",
                LastShow   = s.LastTime.ToString("MM-dd HH:mm"),
            }).ToList();
            Grid.ItemsSource = rows;

            int totalErr = stats.Sum(s => s.Count);
            int distinct = stats.Select(s => s.Correct).Distinct().Count();
            TxtTitle.Text = totalErr == 0
                ? "这个范围内没有错字，很稳 ~"
                : $"共 {totalErr} 次错误 · 涉及 {distinct} 个不同的字";

            BuildChart(stats);
        }

        /// <summary>右侧 TOP 10 错字横向柱状图（按错次倒序）。</summary>
        private void BuildChart(IReadOnlyList<ErrorStat> stats)
        {
            if (PlotErr == null) return;
            var top = stats.OrderByDescending(s => s.Count).Take(10).ToList();
            if (top.Count == 0)
            {
                PlotErr.Visibility = Visibility.Collapsed;
                if (TxtChartEmpty != null) TxtChartEmpty.Visibility = Visibility.Visible;
                return;
            }
            PlotErr.Visibility = Visibility.Visible;
            if (TxtChartEmpty != null) TxtChartEmpty.Visibility = Visibility.Collapsed;

            var fg = (this.FindResource("ValueFG") as System.Windows.Media.SolidColorBrush)?.Color;
            var textCol = fg.HasValue
                ? new ScottPlot.Color(fg.Value.R, fg.Value.G, fg.Value.B)
                : ScottPlot.Colors.Gray;
            var axisCol = textCol.WithAlpha(0x99);

            var plot = PlotErr.Plot;
            plot.Clear();

            int max = top.Max(s => s.Count);
            // 表格倒序，柱图从下往上，反转使最高项在顶部
            var ordered = top.AsEnumerable().Reverse().ToList();

            var bars = new List<ScottPlot.Bar>();
            for (int i = 0; i < ordered.Count; i++)
            {
                var s = ordered[i];
                double t = max > 0 ? (double)s.Count / max : 0;
                var col = new ScottPlot.Color(
                    (byte)(0x42 + (0xEF - 0x42) * t),
                    (byte)(0xC3 + (0x44 - 0xC3) * t),
                    (byte)(0x6E + (0x36 - 0x6E) * t));
                bars.Add(new ScottPlot.Bar
                {
                    Position = i,
                    Value = s.Count,
                    FillColor = col,
                    Orientation = ScottPlot.Orientation.Horizontal,
                });
            }
            plot.Add.Bars(bars);

            // Y 轴类别标签
            var ticks = new ScottPlot.TickGenerators.NumericManual();
            for (int i = 0; i < ordered.Count; i++)
                ticks.AddMajor(i, ordered[i].Correct + "→" + ShowCharShort(ordered[i].Typed));
            plot.Axes.Left.TickGenerator = ticks;

            // 样式：透明背景、隐网格、中文字体、轴色
            plot.FigureBackground.Color = ScottPlot.Colors.Transparent;
            plot.DataBackground.Color = ScottPlot.Colors.Transparent;
            plot.Axes.Color(axisCol);
            plot.Axes.Left.TickLabelStyle.ForeColor = textCol;
            plot.Axes.Left.TickLabelStyle.FontName = "Microsoft YaHei";
            plot.Axes.Left.TickLabelStyle.FontSize = 13;
            plot.Axes.Bottom.TickLabelStyle.ForeColor = textCol;
            plot.HideGrid();
            plot.Axes.SetLimits(0, max * 1.12, -0.6, ordered.Count - 0.4);

            PlotErr.Refresh();
        }

        /// <summary>柱图轴标用的紧凑“打成字”显示。</summary>
        private static string ShowCharShort(string s)
        {
            if (string.IsNullOrEmpty(s)) return "∅";
            if (s == " ") return "␣";
            if (s == "\t") return "⇥";
            return s;
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

        /// <summary>选中某行 → 查该字来源文章分布，显示在右侧"错字溯源"区。</summary>
        private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TxtSrc == null || TxtSrcTitle == null) return;
            var row = Grid.SelectedItem as Row;
            if (row == null || string.IsNullOrEmpty(row.Correct))
            {
                TxtSrcTitle.Text = "错字溯源（点选左侧某字查看）";
                TxtSrc.Text = "—";
                return;
            }
            var sources = ErrorBookRepository.QuerySources(row.Correct, CurrentRange());
            int totalErr = sources.Sum(s => s.Count);
            TxtSrcTitle.Text = $"「{row.Correct}」来源（共 {totalErr} 次，{sources.Count} 篇）";
            if (sources.Count == 0)
            {
                TxtSrc.Text = "本范围内暂无来源记录";
                return;
            }
            var sb = new StringBuilder();
            foreach (var s in sources)
                sb.AppendLine($"· {s.Title} ×{s.Count}（最近 {s.LastTime:MM-dd HH:mm}）");
            TxtSrc.Text = sb.ToString().TrimEnd();
        }

        /// <summary>取选中行的正确字；未选中返回 null 并提示。</summary>
        private string SelectedCorrect()
        {
            var row = Grid.SelectedItem as Row;
            if (row == null || string.IsNullOrEmpty(row.Correct))
            {
                Services.Toast.Info("请先在表格中选中一行");
                return null;
            }
            return row.Correct;
        }

        private void BtnMaster_Click(object sender, RoutedEventArgs e)
        {
            string c = SelectedCorrect();
            if (c == null) return;
            ErrorBookRepository.MarkMastered(c, true);
            Services.Toast.Success($"「{c}」已标为掌握，再错会重新出现");
            Refresh();
        }

        private void BtnDeleteChar_Click(object sender, RoutedEventArgs e)
        {
            string c = SelectedCorrect();
            if (c == null) return;
            var r = System.Windows.MessageBox.Show(
                $"彻底删除「{c}」的全部错字记录？此操作不可撤销。",
                "删除错字", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (r != MessageBoxResult.OK) return;
            int n = ErrorBookRepository.DeleteChar(c);
            Services.Toast.Success($"已删除「{c}」的 {n} 条记录");
            Refresh();
        }

        /// <summary>当前范围内去重错字，按总错次倒序（受“隐藏已掌握”过滤）。</summary>
        private List<string> CurrentErrorChars()
        {
            var stats = ErrorBookRepository.QueryRanking(CurrentRange());
            bool hide = ChkHideMastered == null || ChkHideMastered.IsChecked == true;
            if (hide)
                stats = stats.Where(s => !s.Mastered && s.Streak < ErrorBookRepository.MasterStreak).ToList();
            return stats
                .GroupBy(s => s.Correct)
                .OrderByDescending(g => g.Sum(x => x.Count))
                .Select(g => g.Key)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();
        }

        private void BtnCopyChars_Click(object sender, RoutedEventArgs e)
        {
            string text = string.Concat(CurrentErrorChars());
            if (string.IsNullOrEmpty(text))
            {
                Services.Toast.Info("当前范围没有错字可复制");
                return;
            }
            if (newgdq.Services.ClipboardHelper.TrySetText(text))
                Services.Toast.Success($"已复制 {text.Length} 个错字");
            else
                Services.Toast.Warning("剪贴板被其他程序占用，请稍后再试");
        }

        /// <summary>错字闭环：取高频错字组成重复练习段，直接送进主窗跟打区。</summary>
        private void BtnGenPractice_Click(object sender, RoutedEventArgs e)
        {
            var chars = CurrentErrorChars();
            if (chars.Count == 0)
            {
                Services.Toast.Info("当前范围没有错字可练");
                return;
            }
            var main = Owner as newgdq.MainWindow;
            if (main == null)
            {
                Services.Toast.Warning("无法定位主窗口，请从主窗菜单打开错字本");
                return;
            }
            // 取高频前 N 个，重复凑成约 80 字的练习段（逐遍重打弱点字）
            const int MaxChars = 30;
            var pick = chars.Take(MaxChars).ToList();
            int repeat = Math.Max(2, Math.Min(6, (int)Math.Ceiling(80.0 / pick.Count)));
            var sb = new StringBuilder();
            for (int r = 0; r < repeat; r++)
                foreach (var c in pick) sb.Append(c);
            string text = sb.ToString();
            string title = $"错字针对练习 · {pick.Count}字×{repeat}遍";
            if (!main.LoadPracticeText(text, title)) return;   // 用户在"覆盖确认"里取消 → 不关窗
            Services.Toast.Success($"已生成 {text.Length} 字练习，去主窗开打");
            Close();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            var range = CurrentRange();
            string name = (CmbRange.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "本范围";
            var r = System.Windows.MessageBox.Show(
                $"确定清空「{name}」范围内的错字记录？此操作不可撤销。",
                "清空错字本", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (r != MessageBoxResult.OK) return;
            int n = ErrorBookRepository.Clear(range);
            Services.Toast.Success($"已清空 {n} 条记录");
            Refresh();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
