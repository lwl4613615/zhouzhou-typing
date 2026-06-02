using System;
using System.Linq;
using System.Windows;
using newgdq.Services;

namespace newgdq.Views
{
    /// <summary>
    /// 击键评定窗口（对齐老版 JjCheck）：
    ///   - 从 SQLite 取所有历史段的击键速度（jj，键/秒）
    ///   - 按 4..12+ 共 9 个等级分桶，jj &lt; 4 计入桶 0
    ///   - jjC = 首个占比 ≥ 10% 的等级（+4，封顶 12），即"基准等级"
    ///   - jjC_ = 等级 ≥ jjC 的累计占比
    ///   - 评定值 = jjC + jjC_，形如 "8.235"，表示"在 8 键/秒及以上稳定占 23.5%"
    /// </summary>
    public partial class JjCheckWindow : HandyControl.Controls.Window
    {
        public JjCheckWindow(Window owner)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            Build();
        }

        private void Build()
        {
            var all = HistoryRepository.LoadAllJj();
            int total = all.Count;
            if (total == 0)
            {
                TxtTitle.Text = "暂无历史数据。先打几段试试 ~";
                Plot.Plot.Clear();
                Plot.Refresh();
                return;
            }

            // 分桶：等级 0..8 对应 4..12，jj<4 也并入桶 0（避免遗漏）
            int[] cnt = new int[9];
            foreach (var jj in all)
            {
                int lv = (int)Math.Floor(jj) - 4;
                if (lv < 0) lv = 0;
                if (lv > 8) lv = 8;
                cnt[lv]++;
            }

            // 算 jjC / jjC_
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

            // ScottPlot 横向柱图
            var plot = Plot.Plot;
            plot.Clear();

            var fore = ScottPlot.Colors.LightGray;
            var axisCol = ScottPlot.Colors.Gray;
            var fill = new ScottPlot.Color(0x4F, 0xC3, 0xF7);

            var bars = new System.Collections.Generic.List<ScottPlot.Bar>();
            // 表格从上到下为 4..12+，柱图反转位置使 4 在顶部
            for (int i = 0; i < 9; i++)
            {
                bars.Add(new ScottPlot.Bar
                {
                    Position = 8 - i,
                    Value = pcts[i],
                    FillColor = fill,
                    Orientation = ScottPlot.Orientation.Horizontal,
                });
            }
            plot.Add.Bars(bars);

            var ticks = new ScottPlot.TickGenerators.NumericManual();
            for (int i = 0; i < 9; i++)
                ticks.AddMajor(8 - i, (i + 4 == 12 ? "12+" : (i + 4).ToString()) + " 键/秒");
            plot.Axes.Left.TickGenerator = ticks;

            double maxPct = pcts.Max();
            plot.FigureBackground.Color = ScottPlot.Colors.Transparent;
            plot.DataBackground.Color = ScottPlot.Colors.Transparent;
            plot.Axes.Color(axisCol);
            plot.Axes.Left.TickLabelStyle.ForeColor = fore;
            plot.Axes.Left.TickLabelStyle.FontName = "Microsoft YaHei";
            plot.Axes.Bottom.TickLabelStyle.ForeColor = fore;
            plot.HideGrid();
            plot.Axes.SetLimits(0, Math.Max(maxPct * 1.15, 0.05), -0.6, 8.4);

            Plot.Refresh();
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (newgdq.Services.ClipboardHelper.TrySetText(TxtTitle.Text))
                HandyControl.Controls.Growl.Success("已复制");
            else
                HandyControl.Controls.Growl.Warning("剪贴板被其他程序占用，请稍后再试");
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
