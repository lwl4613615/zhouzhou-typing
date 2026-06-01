using System;
using System.Windows;
using newgdq.Services;

namespace newgdq.Views
{
    /// <summary>
    /// 平均成绩窗口 —— 从 SQLite 聚合所有历史段的平均值/极值/总数。
    /// </summary>
    public partial class AverageWindow : HandyControl.Controls.Window
    {
        public AverageWindow(Window owner)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            Build();
        }

        private void Build()
        {
            var (cnt, avgSpeed, avgSpeed2, avgJj, avgMc, maxSpeed, totalUseTime) = HistoryRepository.LoadAggregate();
            TxtTitle.Text = cnt == 0
                ? "暂无历史数据。先打几段试试 ~"
                : $"基于 {cnt} 段历史成绩的统计";
            TxtCount.Text     = cnt.ToString();
            TxtAvgSpeed.Text  = avgSpeed.ToString("0.00");
            TxtAvgSpeed2.Text = avgSpeed2.ToString("0.00");
            TxtAvgJj.Text     = avgJj.ToString("0.00");
            TxtAvgMc.Text     = avgMc.ToString("0.00");
            TxtMisc.Text      = $"{maxSpeed:0.00} 字/分  ·  {totalUseTime:0.0} 秒（约 {totalUseTime / 60:0.0} 分钟）";
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            string s = $"【平均成绩】{TxtTitle.Text}\n" +
                       $"速度 {TxtAvgSpeed.Text} | 罚五 {TxtAvgSpeed2.Text} | 击键 {TxtAvgJj.Text} | 码长 {TxtAvgMc.Text}\n" +
                       $"最高速度 / 总用时：{TxtMisc.Text}";
            if (newgdq.Services.ClipboardHelper.TrySetText(s))
                HandyControl.Controls.Growl.Success("已复制");
            else
                HandyControl.Controls.Growl.Warning("剪贴板被其他程序占用，请稍后再试");
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
