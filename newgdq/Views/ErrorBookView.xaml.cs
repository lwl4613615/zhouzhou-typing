using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using newgdq.Services;

namespace newgdq.Views
{
    /// <summary>
    /// 错字本内嵌视图 —— 用于在主窗口内容区内嵌展示（E：分析页内嵌）。
    /// 「生成练习」闭环：调用主窗 LoadPracticeText 后退出分析页回到跟打区。
    /// </summary>
    public partial class ErrorBookView : UserControl
    {
        private sealed class Row
        {
            public string Correct { get; set; }
            public string TypedShow { get; set; }
            public int Count { get; set; }
            public string StreakShow { get; set; }
            public string RateShow { get; set; }
            public string LastShow { get; set; }
        }

        public ErrorBookView()
        {
            InitializeComponent();
            Loaded += (s, e) => Refresh();
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
            if (Grid == null) return;
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

        private void BuildChart(IReadOnlyList<ErrorStat> stats)
        {
            if (PlotErr == null) return;
            var top = stats.OrderByDescending(s => s.Count).Take(10).ToList();
            if (top.Count == 0)
            {
                PlotErr.Model = null;
                PlotErr.Visibility = Visibility.Collapsed;
                if (TxtChartEmpty != null) TxtChartEmpty.Visibility = Visibility.Visible;
                return;
            }
            PlotErr.Visibility = Visibility.Visible;
            if (TxtChartEmpty != null) TxtChartEmpty.Visibility = Visibility.Collapsed;

            var fg = (this.FindResource("ValueFG") as System.Windows.Media.SolidColorBrush)?.Color;
            var axisColor = fg.HasValue
                ? OxyColor.FromArgb(0x99, fg.Value.R, fg.Value.G, fg.Value.B)
                : OxyColors.Gray;
            var textColor = fg.HasValue
                ? OxyColor.FromRgb(fg.Value.R, fg.Value.G, fg.Value.B)
                : OxyColors.Gray;

            var model = new PlotModel
            {
                PlotAreaBorderColor = OxyColors.Transparent,
                TextColor    = textColor,
                TitleColor   = textColor,
                Background   = OxyColors.Transparent,
                PlotMargins  = new OxyThickness(0),
                Padding      = new OxyThickness(0),
            };
            var bar = new BarSeries
            {
                StrokeColor = OxyColors.Transparent,
                LabelPlacement = LabelPlacement.Inside,
                LabelFormatString = "{0}",
                TextColor = OxyColors.White,
                TrackerFormatString = "{1}：错 {2} 次",
            };
            int max = top.Max(s => s.Count);
            var ordered = top.AsEnumerable().Reverse().ToList();
            foreach (var s in ordered)
            {
                double t = max > 0 ? (double)s.Count / max : 0;
                // 低→高：柔和青绿 #5BBF8A → 暖珊瑚 #E8825A（避免纯饱和红刺眼）
                var col = OxyColor.FromRgb(
                    (byte)(0x5B + (0xE8 - 0x5B) * t),
                    (byte)(0xBF + (0x82 - 0xBF) * t),
                    (byte)(0x8A + (0x5A - 0x8A) * t));
                bar.Items.Add(new BarItem { Value = s.Count, Color = col });
            }

            var categoryAxis = new CategoryAxis
            {
                Position = AxisPosition.Left,
                TextColor = textColor,
                AxislineColor = axisColor,
                TicklineColor = axisColor,
                FontSize = 15,
                FontWeight = OxyPlot.FontWeights.Bold,
            };
            foreach (var s in ordered)
                categoryAxis.Labels.Add(s.Correct + "→" + ShowCharShort(s.Typed));

            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = 0,
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = axisColor,
                MinorTickSize = 0,
                TextColor = textColor,
                AxislineColor = axisColor,
                TicklineColor = axisColor,
            };

            model.Axes.Add(categoryAxis);
            model.Axes.Add(valueAxis);
            model.Series.Add(bar);
            PlotErr.Model = model;
        }

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

        private List<string> SelectedErrorChars()
        {
            return Grid.SelectedItems
                .OfType<Row>()
                .Select(r => r.Correct)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
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

        private void BtnGenPractice_Click(object sender, RoutedEventArgs e)
        {
            var selected = SelectedErrorChars();
            bool useSelected = selected.Count > 0;
            var chars = useSelected ? selected : CurrentErrorChars();
            if (chars.Count == 0)
            {
                Services.Toast.Info("当前范围没有错字可练");
                return;
            }
            var main = System.Windows.Window.GetWindow(this) as newgdq.MainWindow;
            if (main == null)
            {
                Services.Toast.Warning("无法定位主窗口，请从主窗菜单打开错字本");
                return;
            }
            int maxChars = useSelected ? 100 : 30;
            var pick = chars.Take(maxChars).ToList();
            int repeat = Math.Max(2, Math.Min(6, (int)Math.Ceiling(80.0 / pick.Count)));
            var sb = new StringBuilder();
            for (int r = 0; r < repeat; r++)
                foreach (var c in pick) sb.Append(c);
            string text = sb.ToString();
            string title = $"错字针对练习 · {pick.Count}字×{repeat}遍";
            if (!main.LoadPracticeText(text, title)) return;   // 用户在“覆盖确认”里取消 → 不退出分析页
            string scope = useSelected ? $"选中 {pick.Count} 个错字" : "全部错字";
            Services.Toast.Success($"已用{scope}生成 {text.Length} 字练习，去主窗开打");
            main.CloseAnalysis();   // 退出分析页，回到跟打区
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
    }
}
