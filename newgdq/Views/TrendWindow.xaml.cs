using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using newgdq.Services;

namespace newgdq.Views
{
    /// <summary>
    /// 成绩趋势窗 —— 按天/周/月聚合历史均速，画折线 + 明细表。
    /// 设计原则：主打"和过去的自己比"，目标速度只作参考虚线 + 完成度百分比，
    /// 达不到不做任何"未达标"红叉，避免挫败感。
    /// </summary>
    public partial class TrendWindow : HandyControl.Controls.Window
    {
        /// <summary>DataGrid 行视图模型。</summary>
        private sealed class Row
        {
            public string Label { get; set; }
            public int Segs { get; set; }
            public int Words { get; set; }
            public string SpeedShow { get; set; }
            public string Speed2Show { get; set; }
            public string JjShow { get; set; }
            public string ErrShow { get; set; }
            public string DeltaShow { get; set; }
            public Brush DeltaBrush { get; set; }
            public string GoalShow { get; set; }
        }

        private static readonly Brush UpBrush   = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A)); // 进步：绿
        private static readonly Brush FlatBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0xB7, 0xE3)); // 持平：蓝灰
        private static readonly Brush DownBrush = new SolidColorBrush(Color.FromRgb(0xBD, 0xB0, 0x76)); // 回落：暗黄（不用刺眼红）

        public TrendWindow(Window owner)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            double goal = SettingsService.Instance.GoalSpeed ?? 0;
            NumGoal.Value = goal;
            // 构造里 CmbGranularity 默认选中"按周"，SelectionChanged 触发首次刷新
        }

        private HistoryRepository.TrendGranularity CurrentGranularity()
        {
            switch (CmbGranularity.SelectedIndex)
            {
                case 0: return HistoryRepository.TrendGranularity.Day;
                case 2: return HistoryRepository.TrendGranularity.Month;
                default: return HistoryRepository.TrendGranularity.Week;
            }
        }

        private int CurrentLimit()
        {
            return CmbGranularity.SelectedIndex == 0 ? 14 : 12;
        }

        private void Refresh()
        {
            if (Grid == null || Chart == null) return;

            var buckets = HistoryRepository.LoadTrend(CurrentGranularity(), CurrentLimit());
            double goal = NumGoal != null ? NumGoal.Value : 0;

            BuildHeadline(buckets, goal);
            BuildRows(buckets, goal);
            BuildChart(buckets, goal);
        }

        /// <summary>顶部激励文案：只讲进步与达成度，不做未达标责备。</summary>
        private void BuildHeadline(List<HistoryRepository.TrendBucket> buckets, double goal)
        {
            string unit = GranularityWord();
            if (buckets.Count == 0)
            {
                TxtHeadline.Text = "还没有足够的成绩，先打几段就能看到趋势啦 ~";
                TxtSub.Text = "";
                return;
            }

            var last = buckets[buckets.Count - 1];
            string head = $"最近一{unit}均速 {last.AvgSpeed:0.0} 字/分";

            if (buckets.Count >= 2)
            {
                var prev = buckets[buckets.Count - 2];
                double delta = last.AvgSpeed - prev.AvgSpeed;
                if (delta > 0.05)
                    head += $"，比上一{unit}快了 {delta:0.0} ↑";
                else if (delta < -0.05)
                    head += $"，比上一{unit}慢了 {Math.Abs(delta):0.0}（状态有起伏很正常）";
                else
                    head += $"，和上一{unit}基本持平";
            }
            TxtHeadline.Text = head;

            // 历史最高那一期（破纪录激励）
            var best = buckets.OrderByDescending(b => b.AvgSpeed).First();
            var sub = new StringBuilder();
            if (best == last && buckets.Count >= 2)
                sub.Append($"这是近 {buckets.Count} {unit}里的最佳均速，保持住！  ");
            else
                sub.Append($"近 {buckets.Count} {unit}最佳：{best.Label} 的 {best.AvgSpeed:0.0} 字/分。  ");

            if (goal > 0)
            {
                double pct = goal > 0 ? last.AvgSpeed / goal * 100 : 0;
                if (last.AvgSpeed >= goal)
                    sub.Append($"已达到目标 {goal:0} 字/分 🎉");
                else
                    sub.Append($"已完成目标的 {pct:0}%（目标 {goal:0} 字/分，还差 {goal - last.AvgSpeed:0.0}）");
            }
            else
            {
                sub.Append("未设目标——可在右上角填一个目标速度作为远方的灯塔。");
            }
            TxtSub.Text = sub.ToString();
        }

        private void BuildRows(List<HistoryRepository.TrendBucket> buckets, double goal)
        {
            var rows = new List<Row>(buckets.Count);
            for (int i = 0; i < buckets.Count; i++)
            {
                var b = buckets[i];
                string deltaShow; Brush deltaBrush;
                if (i == 0)
                {
                    deltaShow = "—"; deltaBrush = FlatBrush;
                }
                else
                {
                    double d = b.AvgSpeed - buckets[i - 1].AvgSpeed;
                    if (d > 0.05)      { deltaShow = $"+{d:0.0} ↑"; deltaBrush = UpBrush; }
                    else if (d < -0.05){ deltaShow = $"{d:0.0} ↓";  deltaBrush = DownBrush; }
                    else               { deltaShow = "持平";        deltaBrush = FlatBrush; }
                }

                string goalShow = "—";
                if (goal > 0)
                    goalShow = b.AvgSpeed >= goal ? "✓ 达标" : $"{b.AvgSpeed / goal * 100:0}%";

                rows.Add(new Row
                {
                    Label      = b.Label,
                    Segs       = b.Segs,
                    Words      = b.Words,
                    SpeedShow  = b.AvgSpeed.ToString("0.0"),
                    Speed2Show = b.AvgSpeed2.ToString("0.0"),
                    JjShow     = b.AvgJj.ToString("0.00"),
                    ErrShow    = (b.ErrRate * 100).ToString("0.0") + "%",
                    DeltaShow  = deltaShow,
                    DeltaBrush = deltaBrush,
                    GoalShow   = goalShow,
                });
            }
            // 最新在最上面
            rows.Reverse();
            Grid.ItemsSource = rows;
        }

        private void BuildChart(List<HistoryRepository.TrendBucket> buckets, double goal)
        {
            var plot = Chart.Plot;
            plot.Clear();

            var textCol = new ScottPlot.Color(0x94, 0xA3, 0xB8);
            var gridCol = new ScottPlot.Color(0xFF, 0xFF, 0xFF).WithAlpha(0x33);
            var lineCol = new ScottPlot.Color(0xFF, 0xD5, 0x4F);

            int n = buckets.Count;
            var xs = new double[Math.Max(n, 1)];
            var ys = new double[Math.Max(n, 1)];
            for (int i = 0; i < n; i++) { xs[i] = i; ys[i] = buckets[i].AvgSpeed; }
            if (n == 0) { xs[0] = 0; ys[0] = 0; }

            var scatter = plot.Add.Scatter(xs, ys);
            scatter.Color = lineCol;
            scatter.LineWidth = 2.5f;
            scatter.MarkerSize = 7;
            scatter.MarkerColor = lineCol;

            // X 轴类别标签
            var ticks = new ScottPlot.TickGenerators.NumericManual();
            for (int i = 0; i < n; i++)
                ticks.AddMajor(i, buckets[i].Label);
            plot.Axes.Bottom.TickGenerator = ticks;

            // 目标参考线（虚线，柔和绿）
            if (goal > 0)
            {
                var goalCol = new ScottPlot.Color(0x66, 0xBB, 0x6A);
                var hl = plot.Add.HorizontalLine(goal);
                hl.Color = goalCol;
                hl.LineWidth = 1.5f;
                hl.LinePattern = ScottPlot.LinePattern.Dashed;
                hl.LabelText = $"目标 {goal:0}";
                hl.LabelOppositeAxis = false;
            }

            plot.FigureBackground.Color = ScottPlot.Colors.Transparent;
            plot.DataBackground.Color = ScottPlot.Colors.Transparent;
            plot.Axes.Color(textCol);
            plot.Axes.Bottom.TickLabelStyle.ForeColor = textCol;
            plot.Axes.Bottom.TickLabelStyle.FontName = "Microsoft YaHei";
            plot.Axes.Bottom.TickLabelStyle.FontSize = 10;
            plot.Axes.Left.TickLabelStyle.ForeColor = textCol;
            plot.Axes.Left.TickLabelStyle.FontSize = 10;
            plot.Grid.MajorLineColor = gridCol;
            plot.Axes.SetLimitsY(0, Math.Max(ys.Max(), goal) * 1.12 + 1);

            Chart.Refresh();
        }

        private string GranularityWord()
        {
            switch (CurrentGranularity())
            {
                case HistoryRepository.TrendGranularity.Day:   return "天";
                case HistoryRepository.TrendGranularity.Month: return "月";
                default: return "周";
            }
        }

        private void CmbGranularity_SelectionChanged(object sender, SelectionChangedEventArgs e) => Refresh();

        private void NumGoal_ValueChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e)
        {
            double g = NumGoal.Value;
            SettingsService.Instance.GoalSpeed = g > 0 ? g : (double?)null;
            try { SettingsService.Save(); } catch { }
            Refresh();
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            var buckets = HistoryRepository.LoadTrend(CurrentGranularity(), CurrentLimit());
            if (buckets.Count == 0) { HandyControl.Controls.Growl.Info("暂无可复制的趋势数据"); return; }
            var sb = new StringBuilder();
            sb.AppendLine($"【成绩趋势 · {GranularityWord()}】");
            foreach (var b in buckets)
                sb.AppendLine($"{b.Label}  均速 {b.AvgSpeed:0.0}  罚五 {b.AvgSpeed2:0.0}  击键 {b.AvgJj:0.00}  错字率 {b.ErrRate * 100:0.0}%  ({b.Segs}段)");
            if (newgdq.Services.ClipboardHelper.TrySetText(sb.ToString()))
                HandyControl.Controls.Growl.Success("趋势已复制");
            else
                HandyControl.Controls.Growl.Warning("剪贴板被其他程序占用，请稍后再试");
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
