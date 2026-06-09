using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using newgdq.Models;

namespace newgdq.Views
{
    /// <summary>现代化成绩卡：左 KPI / 右 速度曲线+节奏热力 / 下 详细统计。
    /// 独立 UserControl，用于"复制成绩图 / 保存成绩图"截图。</summary>
    public partial class ScoreCard : UserControl
    {
        public ScoreCard(TypingSession s)
        {
            InitializeComponent();
            Render(s);
        }

        private void Render(TypingSession s)
        {
            int total = s.TypeText.Length;
            var (speed, speed2, jj, mc, sec) = s.ComputeStats(total);

            TxtTitle.Text    = string.IsNullOrEmpty(s.Title) ? "跟打成绩" : s.Title;
            TxtSubtitle.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            TxtSpeed.Text    = speed.ToString("0.00");
            TxtSpeed2.Text   = "罚五 " + speed2.ToString("0.00");
            TxtSec.Text      = sec.ToString("0.0") + "s";
            TxtJj.Text       = jj.ToString("0.00");
            TxtMc.Text       = mc.ToString("0.00");
            TxtCz.Text       = s.Cz.ToString();
            TxtWords.Text    = total.ToString();
            TxtKeys.Text     = s.Keys.ToString();
            TxtHg.Text       = s.Hg.ToString();
            TxtDaci.Text     = s.Words.ToString();
            TxtResel.Text    = s.Reselect.ToString();
            TxtImeBs.Text    = s.Enter.ToString();
            TxtLeftRight.Text = s.LeftHand + ":" + s.RightHand;
            double acc = s.Keys > 0 ? (s.Keys - s.Hg * 2) * 100.0 / s.Keys : 100;
            if (acc < 0) acc = 0;
            if (acc > 100) acc = 100;
            TxtAcc.Text      = acc.ToString("0.0") + "%";

            BuildMiniChart(s, sec);
            // 等控件布局完成后画热力条（异步避免 ActualWidth=0）
            HeatCanvas.Loaded += (_, __) => BuildHeat(s, total);
            HeatCanvas.SizeChanged += (_, __) => BuildHeat(s, total);
        }

        private void BuildMiniChart(TypingSession s, double totalSec)
        {
            var model = new OxyPlot.PlotModel
            {
                Background          = OxyPlot.OxyColors.Transparent,
                PlotAreaBorderColor = OxyPlot.OxyColors.Transparent,
                TextColor           = OxyPlot.OxyColor.FromRgb(0x94, 0xA3, 0xB8),
                DefaultFont         = "微软雅黑",
                DefaultFontSize     = 10,
                PlotMargins         = new OxyPlot.OxyThickness(36, 6, 8, 22),
                Padding             = new OxyPlot.OxyThickness(0),
            };
            var grid = OxyPlot.OxyColor.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
            model.Axes.Add(new OxyPlot.Axes.LinearAxis
            {
                Position = OxyPlot.Axes.AxisPosition.Bottom,
                Minimum = 0,
                AxislineColor = grid, AxislineThickness = 1,
                MajorGridlineStyle = OxyPlot.LineStyle.Solid, MajorGridlineColor = grid,
                TickStyle = OxyPlot.Axes.TickStyle.Outside, MajorTickSize = 3, FontSize = 9,
                StringFormat = "0",
            });
            model.Axes.Add(new OxyPlot.Axes.LinearAxis
            {
                Position = OxyPlot.Axes.AxisPosition.Left,
                Minimum = 0,
                AxislineColor = grid, AxislineThickness = 1,
                MajorGridlineStyle = OxyPlot.LineStyle.Solid, MajorGridlineColor = grid,
                TickStyle = OxyPlot.Axes.TickStyle.Outside, MajorTickSize = 3, FontSize = 9,
                StringFormat = "0",
            });
            // 速度走势：从 Report 每个事件累计计算 instant speed = (累计字数 / 累计时间) * 60
            var area = new OxyPlot.Series.AreaSeries
            {
                Color           = OxyPlot.OxyColor.FromRgb(0xFF, 0xD5, 0x4F),
                StrokeThickness = 2,
                Fill            = OxyPlot.OxyColor.FromArgb(0x66, 0xFF, 0xD5, 0x4F),
                InterpolationAlgorithm = OxyPlot.InterpolationAlgorithms.CanonicalSpline,
                LineJoin = OxyPlot.LineJoin.Round,
                MarkerType = OxyPlot.MarkerType.None,
            };
            foreach (var ev in s.Report)
            {
                if (ev.NowTime <= 0) continue;
                double sp = ev.End * 60.0 / ev.NowTime;
                if (sp > 999) sp = 999;
                area.Points.Add(new OxyPlot.DataPoint(ev.NowTime, sp));
            }
            if (area.Points.Count == 0)
            {
                // 至少一个点防 OxyPlot 异常
                area.Points.Add(new OxyPlot.DataPoint(0, 0));
                area.Points.Add(new OxyPlot.DataPoint(totalSec > 0 ? totalSec : 1, 0));
            }
            model.Series.Add(area);
            MiniChart.Model = model;
        }

        private void BuildHeat(TypingSession s, int total)
        {
            HeatCanvas.Children.Clear();
            double w = HeatCanvas.ActualWidth;
            double h = HeatCanvas.ActualHeight;
            if (w <= 1 || h <= 1 || total <= 0) return;

            var charMs = new double[total];
            int prev = 0;
            foreach (var ev in s.Report)
            {
                if (ev.Length <= 0) { prev = ev.End; continue; }
                double per = ev.TotalTime * 1000.0 / ev.Length;
                int to = Math.Min(ev.End, total);
                for (int i = prev; i < to; i++) charMs[i] = per;
                prev = ev.End;
            }
            double cellW = w / total;
            double drawW = Math.Max(cellW, 1.5);
            for (int i = 0; i < total; i++)
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = drawW,
                    Height = h,
                    Fill = ColorForMs(charMs[i]),
                };
                Canvas.SetLeft(rect, i * cellW);
                Canvas.SetTop(rect, 0);
                HeatCanvas.Children.Add(rect);
            }
        }

        private static Brush ColorForMs(double ms)
        {
            if (ms <= 0) return new SolidColorBrush(Color.FromArgb(0x30, 0x55, 0x55, 0x60));
            Color c;
            if (ms <= 300) c = Lerp(Color.FromRgb(0x4C, 0xC9, 0x6A), Color.FromRgb(0xA8, 0xE0, 0x5F), N(ms, 0, 300));
            else if (ms <= 600) c = Lerp(Color.FromRgb(0xA8, 0xE0, 0x5F), Color.FromRgb(0xFF, 0xD7, 0x2E), N(ms, 300, 600));
            else if (ms <= 1200) c = Lerp(Color.FromRgb(0xFF, 0xD7, 0x2E), Color.FromRgb(0xFF, 0x8C, 0x1A), N(ms, 600, 1200));
            else if (ms <= 2500) c = Lerp(Color.FromRgb(0xFF, 0x8C, 0x1A), Color.FromRgb(0xE7, 0x3E, 0x3E), N(ms, 1200, 2500));
            else c = Color.FromRgb(0xB0, 0x1F, 0x1F);
            return new SolidColorBrush(c);
        }
        private static double N(double v, double a, double b) { double t = (v - a) / (b - a); return t < 0 ? 0 : (t > 1 ? 1 : t); }
        private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }
}
