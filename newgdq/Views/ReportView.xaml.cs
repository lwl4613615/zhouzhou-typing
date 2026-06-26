using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using newgdq.Models;

namespace newgdq.Views
{
    /// <summary>
    /// 跟打报告内嵌视图 —— 与 ReportWindow 同源逻辑，用于在主窗口内容区内嵌展示（E：分析页内嵌）。
    /// 成绩图渲染独立的 ScoreCard，与是否窗口无关。
    /// </summary>
    public partial class ReportView : UserControl
    {
        private readonly TypingSession _session;
        private double _avgTotalTime;

        public ReportView(TypingSession session)
        {
            InitializeComponent();
            _session = session;
            BuildSummary();
            BuildEvents();
        }

        private void BuildSummary()
        {
            TxtTitle.Text = string.IsNullOrEmpty(_session.Title) ? "（未命名文段）" : _session.Title;

            int total = _session.TypeText.Length;
            int len   = _session.LastInputLen;
            int useLen = len > 0 ? len : total;
            var (speed, speed2, jj, mc, sec) = _session.ComputeStats(useLen);

            TxtSpeed.Text  = speed.ToString("0.00");
            TxtSpeed2.Text = speed2.ToString("0.00");
            TxtJj.Text     = jj.ToString("0.00");
            TxtMc.Text     = mc.ToString("0.00");
            TxtSec.Text    = sec.ToString("0.00");
            TxtCounts.Text = $"键 {_session.Keys} / 字 {useLen} / 错 {_session.Cz} / 回改 {_session.Hg} / 打词 {_session.Words} / 选重 {_session.Reselect} / 拼回 {_session.ImeBackspace}";
        }

        private void BuildEvents()
        {
            var events = _session.Report;
            DgvEvents.ItemsSource = events;
            var positives = events.Where(e => e.Length > 0).Select(e => e.TotalTime).ToList();
            _avgTotalTime = positives.Count > 0 ? positives.Average() : 0;
        }

        private void DgvEvents_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (!(e.Row.Item is TypeDate td)) return;
            if (td.Length < 0)
            {
                e.Row.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xC4));
                e.Row.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x4F, 0x00));
            }
            else if (_avgTotalTime > 0 && td.TotalTime > _avgTotalTime * 3)
            {
                e.Row.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0xD6));
                e.Row.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x1F, 0x1F));
            }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            string s =
                $"【跟打报告】{TxtTitle.Text}\n" +
                $"速度 {TxtSpeed.Text} | 错一罚五 {TxtSpeed2.Text} | 击键 {TxtJj.Text} | 码长 {TxtMc.Text}\n" +
                $"用时 {TxtSec.Text}s | {TxtCounts.Text}";
            if (newgdq.Services.ClipboardHelper.TrySetText(s))
                Services.Toast.Success("已复制到剪贴板");
            else
                Services.Toast.Warning("剪贴板被其他程序占用，请稍后再试");
        }

        private System.Windows.Media.Imaging.BitmapSource RenderWindowImage()
        {
            var card = new ScoreCard(_session);
            card.Measure(new Size(card.Width, card.Height));
            card.Arrange(new Rect(0, 0, card.Width, card.Height));
            card.UpdateLayout();
            card.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

            int w = (int)Math.Ceiling(card.Width);
            int h = (int)Math.Ceiling(card.Height);
            if (w <= 0 || h <= 0) return null;
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                w * 2, h * 2, 192, 192,
                System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(card);
            return rtb;
        }

        private void BtnCopyImage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var img = RenderWindowImage();
                if (img == null) { Services.Toast.Warning("没有可截取的内容"); return; }
                bool ok = false;
                for (int retry = 0; retry < 4 && !ok; retry++)
                {
                    try { Clipboard.SetImage(img); ok = true; }
                    catch { System.Threading.Thread.Sleep(80); }
                }
                if (ok) Services.Toast.Success("成绩图已复制到剪贴板");
                else Services.Toast.Warning("剪贴板被占用，复制失败，请重试");
            }
            catch (Exception ex) { Services.Toast.Error(ex.Message); }
        }

        private void BtnSaveImage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var img = RenderWindowImage();
                if (img == null) { Services.Toast.Warning("没有可截取的内容"); return; }
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "PNG 图片|*.png",
                    FileName = $"成绩_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                };
                if (dlg.ShowDialog() != true) return;
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
                using (var fs = System.IO.File.Create(dlg.FileName)) encoder.Save(fs);
                Services.Toast.Success("已保存：" + dlg.FileName);
            }
            catch (Exception ex) { Services.Toast.Error(ex.Message); }
        }
    }
}
