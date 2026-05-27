using System;
using System.Windows;
using System.Windows.Threading;
using newgdq.Models;

namespace newgdq.Views
{
    /// <summary>发文状态浮窗（仿老版 SendTextStatic 风格）。
    /// 主窗启动发文时弹出，按 DispatcherTimer 1s 刷新；点 停止发文 → 关闭发文会话。</summary>
    public partial class SendStatusWindow : HandyControl.Controls.Window
    {
        private readonly MainWindow _owner;
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        public SendStatusWindow(MainWindow owner)
        {
            InitializeComponent();
            _owner = owner;
            if (owner != null)
            {
                Owner = owner;
                // 紧贴主窗左上角外侧；若左侧空间不够（会跑屏外）则贴在右侧
                double left = owner.Left - this.Width - 4;
                if (left < 0) left = owner.Left + owner.Width + 4;
                Left = left;
                Top  = owner.Top;
            }
            _timer.Tick += (s, e) => Refresh();
            this.Loaded += (s, e) => { Refresh(); _timer.Start(); };
            this.Closed += (s, e) => _timer.Stop();
        }

        public void Refresh()
        {
            var st = _owner?.GetSendingState();
            if (st == null || !st.Active)
            {
                TxtCurSerial.Text = "未开启";
                TxtTotalSerial.Text = "0";
                TxtTitle.Text = TxtSource.Text = TxtType.Text = "-";
                TxtMark.Text = TxtSentSeg.Text = TxtTotal.Text = TxtRemain.Text = "-";
                TxtCountPerSeg.Text = TxtMode.Text = TxtStartSeg.Text = TxtCurSeg.Text = "-";
                TxtOneEnd.Text = TxtNoRepeat.Text = "-";
                PrgSent.Value = 0;
                TxtPct.Text = "0%";
                BtnStop.IsEnabled = false;
                BtnSendNext.IsEnabled = false;
                return;
            }
            int totalLen = st.FullText?.Length ?? 0;
            int totalSeg = st.CountPerSeg > 0 ? (totalLen + st.CountPerSeg - 1) / st.CountPerSeg : 0;
            TxtCurSerial.Text   = (st.SentSeg + 1).ToString();
            TxtTotalSerial.Text = totalSeg.ToString();
            TxtTitle.Text       = st.Title ?? "-";
            TxtSource.Text      = st.SourceName ?? "-";
            TxtType.Text        = st.Type.ToString() + (st.IsRandom ? " / 乱序" : " / 顺序");
            TxtMark.Text        = st.Mark.ToString();
            TxtSentSeg.Text     = st.SentSeg.ToString();
            TxtTotal.Text       = totalLen.ToString();
            TxtRemain.Text      = (st.IsRandom && !st.RandomNoRepeat) ? "乱序无限" : (totalLen - st.Mark).ToString();
            TxtCountPerSeg.Text = st.CountPerSeg.ToString();
            TxtMode.Text        = st.OneSentenceEnd ? "一句结束" : (st.IsRandom ? (st.RandomNoRepeat ? "乱序不重复" : "乱序") : "顺序");
            TxtStartSeg.Text    = st.StartSeg.ToString();
            TxtCurSeg.Text      = (st.CurSeg - 1).ToString();
            TxtOneEnd.Text      = st.OneSentenceEnd ? "✓" : "✗";
            TxtNoRepeat.Text    = st.RandomNoRepeat ? "✓" : "✗";
            double pct = totalLen > 0 ? (double)st.Mark * 100.0 / totalLen : 0;
            if (pct < 0) pct = 0; if (pct > 100) pct = 100;
            PrgSent.Value = pct;
            TxtPct.Text = pct.ToString("0.0") + "%";
            BtnStop.IsEnabled = true;
            BtnSendNext.IsEnabled = true;
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _owner?.StopSending();
            Refresh();
        }

        private void BtnSendNext_Click(object sender, RoutedEventArgs e)
        {
            _owner?.SendNextSegment();
            Refresh();
        }
    }
}
