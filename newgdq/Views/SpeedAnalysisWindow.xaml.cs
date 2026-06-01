using System;
using System.Linq;
using System.Windows;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using newgdq.Models;

namespace newgdq.Views
{
    /// <summary>
    /// 速度分析窗口 —— 把一段总用时按事件类型拆解：
    ///   - 正常打字：Length &gt; 0 且本次用时 ≤ 平均 × 3 的累计时间
    ///   - 卡顿/停留：Length &gt; 0 且本次用时 &gt; 平均 × 3 的累计时间（异常长输入）
    ///   - 回改：Length &lt; 0 的累计时间（退格事件）
    ///   - 错字罚时（推算）：错字数 × 平均单字时间 × 4，表示"错一罚五"中比正确字多付的 4 倍代价
    ///
    /// 老版 SpeedAn 用 8 项条纹图，新版精简为 4 项更易读；总速度对比同时显示。
    /// </summary>
    public partial class SpeedAnalysisWindow : HandyControl.Controls.Window
    {
        private readonly TypingSession _session;

        public SpeedAnalysisWindow(TypingSession session, Window owner)
        {
            InitializeComponent();
            _session = session;
            if (owner != null) Owner = owner;
            Build();
        }

        private void Build()
        {
            TxtTitle.Text = string.IsNullOrEmpty(_session.Title) ? "（未命名文段）" : _session.Title;

            int total  = _session.TypeText.Length;
            int useLen = total > 0 ? total : _session.LastInputLen;
            var (speed, speed2, _, _, sec) = _session.ComputeStats(useLen);

            // 拆解事件
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

            // 错字罚时推算：每个错字多付 4 倍正常单字时间（"错一罚五" → 损失 4）
            double penPerChar = avgPos > 0 ? avgPos * 4 : 0;
            double tErr = _session.Cz * penPerChar;

            // 理想速度 = 用"正常打字"时间打完所有有效字数的速度
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
            // 颜色与含义对应
            bar.Items.Add(new BarItem { Value = tNormal, Color = OxyColor.FromRgb(0x66, 0xBB, 0x6A) }); // 正常 绿
            bar.Items.Add(new BarItem { Value = tStall,  Color = OxyColor.FromRgb(0xEF, 0x53, 0x50) }); // 卡顿 红
            bar.Items.Add(new BarItem { Value = tHg,     Color = OxyColor.FromRgb(0xFF, 0xCA, 0x28) }); // 回改 黄
            bar.Items.Add(new BarItem { Value = tErr,    Color = OxyColor.FromRgb(0xAB, 0x47, 0xBC) }); // 错罚 紫

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
                HandyControl.Controls.Growl.Success("已复制");
            else
                HandyControl.Controls.Growl.Warning("剪贴板被其他程序占用，请稍后再试");
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
