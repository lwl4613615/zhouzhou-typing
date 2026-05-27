using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using newgdq.Models;

namespace newgdq.Views
{
    /// <summary>
    /// 跟打报告窗口 —— 摘要 + 段内事件 DataGrid。
    /// 行染色规则（LoadingRow 事件）：
    ///   - 长度 &lt; 0（回改）→ 黄底
    ///   - 本次用时 &gt; 平均 × 3 且为正向输入 → 红底（停留 / 卡顿点）
    /// </summary>
    public partial class ReportWindow : HandyControl.Controls.Window
    {
        private readonly TypingSession _session;
        private double _avgTotalTime;

        public ReportWindow(TypingSession session, Window owner)
        {
            InitializeComponent();
            _session = session;
            if (owner != null) Owner = owner;

            BuildSummary();
            BuildEvents();
        }

        private void BuildSummary()
        {
            TxtTitle.Text = string.IsNullOrEmpty(_session.Title) ? "（未命名文段）" : _session.Title;

            int total = _session.TypeText.Length;
            int len   = _session.LastInputLen;
            int useLen = total > 0 ? total : len;
            var (speed, speed2, jj, mc, sec) = _session.ComputeStats(useLen);

            TxtSpeed.Text  = speed.ToString("0.00");
            TxtSpeed2.Text = speed2.ToString("0.00");
            TxtJj.Text     = jj.ToString("0.00");
            TxtMc.Text     = mc.ToString("0.00");
            TxtSec.Text    = sec.ToString("0.00");
            TxtCounts.Text = $"键 {_session.Keys} / 字 {useLen} / 错 {_session.Cz} / 回改 {_session.Hg} / 打词 {_session.Words} / 选重 {_session.Reselect} / 回车 {_session.Enter}";
        }

        private void BuildEvents()
        {
            var events = _session.Report;
            DgvEvents.ItemsSource = events;

            // 计算正向输入的平均本次用时（用于红底阈值）
            var positives = events.Where(e => e.Length > 0).Select(e => e.TotalTime).ToList();
            _avgTotalTime = positives.Count > 0 ? positives.Average() : 0;
        }

        private void DgvEvents_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (!(e.Row.Item is TypeDate td)) return;
            if (td.Length < 0)
            {
                e.Row.Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x38, 0x26)); // 回改黄底
                e.Row.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x82));
            }
            else if (_avgTotalTime > 0 && td.TotalTime > _avgTotalTime * 3)
            {
                e.Row.Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x26, 0x26)); // 长用时红底
                e.Row.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xAB, 0xAB));
            }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            string s =
                $"【跟打报告】{TxtTitle.Text}\n" +
                $"速度 {TxtSpeed.Text} | 错一罚五 {TxtSpeed2.Text} | 击键 {TxtJj.Text} | 码长 {TxtMc.Text}\n" +
                $"用时 {TxtSec.Text}s | {TxtCounts.Text}";
            try { Clipboard.SetText(s); HandyControl.Controls.Growl.Success("已复制到剪贴板"); }
            catch (Exception ex) { HandyControl.Controls.Growl.Error(ex.Message); }
        }

        private System.Windows.Media.Imaging.BitmapSource RenderWindowImage()
        {
            // 截取当前窗口内容（DPI 自适应：按 96 渲染，避免被高分屏放大太多）
            var visual = (System.Windows.Media.Visual)this.Content;
            var bounds = System.Windows.Media.VisualTreeHelper.GetDescendantBounds(visual);
            int w = (int)Math.Ceiling(bounds.Width);
            int h = (int)Math.Ceiling(bounds.Height);
            if (w <= 0 || h <= 0) return null;
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(visual);
            return rtb;
        }

        private void BtnCopyImage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var img = RenderWindowImage();
                if (img == null) { HandyControl.Controls.Growl.Warning("没有可截取的内容"); return; }
                Clipboard.SetImage(img);
                HandyControl.Controls.Growl.Success("成绩图已复制到剪贴板");
            }
            catch (Exception ex) { HandyControl.Controls.Growl.Error(ex.Message); }
        }

        private void BtnSaveImage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var img = RenderWindowImage();
                if (img == null) { HandyControl.Controls.Growl.Warning("没有可截取的内容"); return; }
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "PNG 图片|*.png",
                    FileName = $"成绩_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                };
                if (dlg.ShowDialog() != true) return;
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
                using (var fs = System.IO.File.Create(dlg.FileName)) encoder.Save(fs);
                HandyControl.Controls.Growl.Success("已保存：" + dlg.FileName);
            }
            catch (Exception ex) { HandyControl.Controls.Growl.Error(ex.Message); }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
