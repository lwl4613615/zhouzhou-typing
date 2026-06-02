using System;
using System.Windows;
using System.Windows.Controls;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using newgdq.Services;

namespace newgdq.Views
{
    /// <summary>
    /// 击键评定内嵌视图 —— 与 JjCheckWindow 同源逻辑，用于在主窗口内容区内嵌展示（E：分析页内嵌）。
    /// </summary>
    public partial class JjCheckView : UserControl
    {
        public JjCheckView()
        {
            InitializeComponent();
            Build();
        }

        private void Build()
        {
            var all = HistoryRepository.LoadAllJj();
            int total = all.Count;
            if (total == 0)
            {
                TxtTitle.Text = "暂无历史数据。先打几段试试 ~";
                Plot.Model = null;
                return;
            }

            int[] cnt = new int[9];
            foreach (var jj in all)
            {
                int lv = (int)Math.Floor(jj) - 4;
                if (lv < 0) lv = 0;
                if (lv > 8) lv = 8;
                cnt[lv]++;
            }

            int    jjC  = 0;
            double jjC_ = 0;
            var pcts = new double[9];
            for (int i = 0; i < 9; i++)
            {
                pcts[i] = (double)cnt[i] / total;
                if (pcts[i] >= 0.1 && jjC == 0)
                    jjC = Math.Min(i + 4, 12);
                if (jjC != 0 && (i + 4) >= jjC)
                    jjC_ += pcts[i];
            }

            string scoreText = jjC == 0
                ? "数据不足以评定（任一等级都未达到 10% 占比）"
                : $"击键评定：{(jjC + jjC_):0.000}（基准 {jjC} 键/秒，达标占比 {jjC_:P1}）";
            TxtTitle.Text = $"总共跟打 {total} 段 · {scoreText}";

            var model = new PlotModel
            {
                PlotAreaBorderColor = OxyColors.Transparent,
                TextColor    = OxyColors.LightGray,
                TitleColor   = OxyColors.LightGray,
                Background   = OxyColors.Transparent,
            };
            var bar = new BarSeries
            {
                FillColor = OxyColor.FromRgb(0x4F, 0xC3, 0xF7),
                StrokeColor = OxyColors.Transparent,
                LabelPlacement = LabelPlacement.Outside,
                LabelFormatString = "{0:P1}",
                TextColor = OxyColors.LightGray,
            };
            for (int i = 0; i < 9; i++)
                bar.Items.Add(new BarItem { Value = pcts[i] });

            var categoryAxis = new CategoryAxis
            {
                Position = AxisPosition.Left,
                TextColor = OxyColors.LightGray,
                AxislineColor = OxyColors.Gray,
                TicklineColor = OxyColors.Gray,
            };
            for (int i = 0; i < 9; i++)
                categoryAxis.Labels.Add((i + 4 == 12 ? "12+" : (i + 4).ToString()) + " 键/秒");

            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = 0,
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColors.DimGray,
                StringFormat = "P0",
                TextColor = OxyColors.LightGray,
                AxislineColor = OxyColors.Gray,
                TicklineColor = OxyColors.Gray,
            };

            model.Axes.Add(categoryAxis);
            model.Axes.Add(valueAxis);
            model.Series.Add(bar);
            Plot.Model = model;
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (ClipboardHelper.TrySetText(TxtTitle.Text))
                Toast.Success("已复制");
            else
                Toast.Warning("剪贴板被其他程序占用，请稍后再试");
        }
    }
}
