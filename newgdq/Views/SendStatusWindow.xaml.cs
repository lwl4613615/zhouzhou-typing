using System;
using System.Windows;
using System.Windows.Media;
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
            // 跟打状态浮窗比主窗口小一圈（实际缩放 = 全局倍数 × 0.8）
            Services.UiScaleManager.SetScaleFactor(this, 0.8);
            if (owner != null)
            {
                Owner = owner;
                // 磁吸跟随：记录初始相对偏移，主窗移动时按差量同步
                _ownerLastLeft = owner.Left;
                _ownerLastTop  = owner.Top;
                owner.LocationChanged += Owner_LocationChanged;
            }
            _timer.Tick += (s, e) => Refresh();
            this.Loaded += (s, e) =>
            {
                // 贴边定位放到 Loaded 后：此时缩放已由 UiScaleManager 应用，再延后一拍等布局完成，
                // ActualWidth 才是缩放后的真实宽度，否则放大倍数下会与主窗重叠。
                Dispatcher.BeginInvoke(new Action(PositionBesideOwner),
                    System.Windows.Threading.DispatcherPriority.Loaded);
                Refresh();
                _timer.Start();
            };
            // 运行时切换缩放后，浮窗宽度变化 → 重新贴边定位（否则会与主窗重叠）
            Services.UiScaleManager.ScaleChanged += OnScaleChanged;
            this.Closed += (s, e) =>
            {
                _timer.Stop();
                Services.UiScaleManager.ScaleChanged -= OnScaleChanged;
                if (_owner != null) _owner.LocationChanged -= Owner_LocationChanged;
            };
        }

        /// <summary>缩放变化后，等布局重算完成再重新贴边（ActualWidth 此时才是新倍数下的宽度）。</summary>
        private void OnScaleChanged(double scale)
        {
            Dispatcher.BeginInvoke(new Action(PositionBesideOwner),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>把浮窗紧贴主窗左侧外缘；左侧空间不够则贴右侧。用缩放后的实际宽度计算。</summary>
        private void PositionBesideOwner()
        {
            if (_owner == null) return;
            double w = ActualWidth > 0 ? ActualWidth : Width;
            double left = _owner.Left - w - 4;
            if (left < 0) left = _owner.Left + _owner.Width + 4;
            Left = left;
            Top  = _owner.Top;
            _ownerLastLeft = _owner.Left;
            _ownerLastTop  = _owner.Top;
        }

        private double _ownerLastLeft, _ownerLastTop;
        private void Owner_LocationChanged(object sender, EventArgs e)
        {
            if (_owner == null) return;
            double dx = _owner.Left - _ownerLastLeft;
            double dy = _owner.Top  - _ownerLastTop;
            _ownerLastLeft = _owner.Left;
            _ownerLastTop  = _owner.Top;
            // 用户拖过子窗后保持相对位置：直接把同样的位移加到子窗上
            Left += dx;
            Top  += dy;
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
                ChkAutoAdvance.IsEnabled = false;
                // 停止后：若仍有未发内容，把"停止发文"按钮变身为绿色"继续发文"
                ApplyStopResumeButton(active: false);
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
            ChkAutoAdvance.IsEnabled = true;
            // 发文中：按钮为红色"停止发文"
            ApplyStopResumeButton(active: true);
            // 反向同步开关状态（避免 Click 处理在赋值时被重入触发）
            if (ChkAutoAdvance.IsChecked != st.AutoAdvance)
            {
                _suppressAutoChk = true;
                ChkAutoAdvance.IsChecked = st.AutoAdvance;
                _suppressAutoChk = false;
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            var st = _owner?.GetSendingState();
            if (st != null && st.Active) _owner.StopSending();   // 发文中 → 停止
            else _owner?.ResumeSending();                        // 已停止 → 继续
            Refresh();
        }

        /// <summary>根据是否发文中，切换底部主按钮的"停止/继续"外观与可用态（保持布局不变）。</summary>
        private void ApplyStopResumeButton(bool active)
        {
            if (active)
            {
                BtnStop.Content     = "■ 停止发文";
                BtnStop.Background   = (Brush)FindResource("StopBg");
                BtnStop.BorderBrush = new SolidColorBrush(Color.FromRgb(0x7E, 0x28, 0x28));
                BtnStop.IsEnabled   = true;
            }
            else
            {
                bool canResume = _owner?.CanResumeSending() ?? false;
                BtnStop.Content     = canResume ? "▶ 继续发文" : "■ 停止发文";
                BtnStop.Background   = canResume ? (Brush)FindResource("ResumeBg")
                                                : (Brush)FindResource("StopBg");
                BtnStop.BorderBrush = new SolidColorBrush(canResume
                    ? Color.FromRgb(0x1C, 0x7C, 0x49) : Color.FromRgb(0x7E, 0x28, 0x28));
                BtnStop.IsEnabled   = canResume; // 不能继续（已发完）则禁用
            }
        }

        private void BtnSendNext_Click(object sender, RoutedEventArgs e)
        {
            _owner?.SendNextSegment();
            Refresh();
        }

        private bool _suppressAutoChk;
        private void ChkAutoAdvance_Click(object sender, RoutedEventArgs e)
        {
            if (_suppressAutoChk) return;
            _owner?.SetAutoAdvance(ChkAutoAdvance.IsChecked == true);
        }
    }
}
