using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using newgdq.Models;

namespace newgdq.Views
{
    /// <summary>
    /// 速度分析内嵌视图 —— 用于在主窗口内容区内嵌展示（E：分析页内嵌）。
    /// </summary>
    public partial class SpeedAnalysisView : UserControl
    {
        private readonly TypingSession _session;

        public SpeedAnalysisView(TypingSession session)
        {
            InitializeComponent();
            _session = session;
            Build();
        }

        private void Build()
        {
            TxtTitle.Text = string.IsNullOrEmpty(_session.Title) ? "（未命名文段）" : _session.Title;

            int total  = _session.TypeText.Length;
            int useLen = total > 0 ? total : _session.LastInputLen;
            var (speed, speed2, _, _, sec) = _session.ComputeStats(useLen);

            var events = _session.Report;
            var positives = events.Where(e => e.Length > 0).ToList();
            double avgPos = positives.Count > 0 ? positives.Average(e => e.TotalTime) : 0;

            double tNormal = positives
                .Where(e => avgPos <= 0 || e.TotalTime <= avgPos * 3)
                .Sum(e => e.TotalTime);
            double tStall = positives
                .Where(e => avgPos > 0 && e.TotalTime > avgPos * 3)
                .Sum(e => e.TotalTime);
            double tHg = events.Where(e => e.Length < 0).Sum(e => e.TotalTime);

            double penPerChar = avgPos > 0 ? avgPos * 4 : 0;
            double tErr = _session.Cz * penPerChar;

            int validLen = useLen - _session.Cz;
            if (validLen < 0) validLen = 0;
            double ideal = (tNormal > 0.01 && validLen > 0)
                ? validLen * 60.0 / tNormal
                : 0;
            if (ideal > 999) ideal = 999;

            TxtSpeed.Text  = speed.ToString("0.00");
            TxtSpeed2.Text = speed2.ToString("0.00");
            TxtIdeal.Text  = ideal.ToString("0.00");
            TxtSec.Text    = sec.ToString("0.00");

            BuildPlot(tNormal, tStall, tHg, tErr);
        }

        private void BuildPlot(double tNormal, double tStall, double tHg, double tErr)
        {
            var model = new PlotModel
            {
                PlotAreaBorderColor = OxyColors.Transparent,
                TextColor    = OxyColors.LightGray,
                Background   = OxyColors.Transparent,
            };

            var bar = new BarSeries
            {
                StrokeColor = OxyColors.Transparent,
                LabelPlacement = LabelPlacement.Outside,
                LabelFormatString = "{0:0.00}s",
                TextColor = OxyColors.LightGray,
            };
            bar.Items.Add(new BarItem { Value = tNormal, Color = OxyColor.FromRgb(0x66, 0xBB, 0x6A) });
            bar.Items.Add(new BarItem { Value = tStall,  Color = OxyColor.FromRgb(0xEF, 0x53, 0x50) });
            bar.Items.Add(new BarItem { Value = tHg,     Color = OxyColor.FromRgb(0xFF, 0xCA, 0x28) });
            bar.Items.Add(new BarItem { Value = tErr,    Color = OxyColor.FromRgb(0xAB, 0x47, 0xBC) });

            var category = new CategoryAxis
            {
                Position = AxisPosition.Left,
                TextColor = OxyColors.LightGray,
                AxislineColor = OxyColors.Gray,
                TicklineColor = OxyColors.Gray,
            };
            category.Labels.Add("正常打字");
            category.Labels.Add("卡顿/停留");
            category.Labels.Add("回改用时");
            category.Labels.Add("错字罚时 (推算)");

            var value = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = 0,
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColors.DimGray,
                StringFormat = "0.0s",
                TextColor = OxyColors.LightGray,
                AxislineColor = OxyColors.Gray,
                TicklineColor = OxyColors.Gray,
            };

            model.Axes.Add(category);
            model.Axes.Add(value);
            model.Series.Add(bar);
            Plot.Model = model;
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            string s =
                $"【速度分析】{TxtTitle.Text}\n" +
                $"实际 {TxtSpeed.Text} | 错一罚五 {TxtSpeed2.Text} | 理想 {TxtIdeal.Text} | 用时 {TxtSec.Text}s";
            if (newgdq.Services.ClipboardHelper.TrySetText(s))
                Services.Toast.Success("已复制");
            else
                Services.Toast.Warning("剪贴板被其他程序占用，请稍后再试");
        }
    }
}
