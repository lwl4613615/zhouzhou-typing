using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using newgdq.Models;
using newgdq.Services;
using newgdq.Views;

namespace newgdq
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑。
    /// 业务状态全部交给 <see cref="TypingSession"/>，本类只负责 WPF 控件交互。
    /// </summary>
    public partial class MainWindow : HandyControl.Controls.Window
    {
        private readonly TypingSession _session = new TypingSession();
        private readonly List<Run>     _charRuns = new List<Run>();
        /// <summary>每个 Run 的当前染色状态缓存：0=默认 1=正确 2=错误。
        /// TextChanged 中只对状态变化的 Run 真正赋 Foreground/Background，省 95%+ 重绘。</summary>
        private byte[] _runStatus = new byte[0];
        private int _historyIndex;

        // 服务 / 计时器
        private readonly KeyHook _keyHook = new KeyHook();
        private readonly ImeWatcher _ime = new ImeWatcher();
        private readonly DispatcherTimer _timerTime  = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly DispatcherTimer _timerStats = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };

        // 编码提示
        private readonly DictionaryService _dict = new DictionaryService();

        // 发文
        private readonly SendingService _sending = new SendingService();

        // 速度曲线窗口
        // 嵌入式速度曲线（TogChart 切换可见性）
        private OxyPlot.PlotModel _chartModel;
        private OxyPlot.Series.AreaSeries _chartSpeed;
        private OxyPlot.Series.ScatterSeries _chartFinishMark;

        // 重数色 (与原版 Glob.BmColors 一致)
        private static readonly Brush[] RankBrushes =
        {
            new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xE2)),  // 1重
            new SolidColorBrush(Color.FromRgb(0xE2, 0x4A, 0x4A)),  // 2重
            new SolidColorBrush(Color.FromRgb(0x9C, 0x4A, 0xE2)),  // 3重
            new SolidColorBrush(Color.FromRgb(0xE2, 0x4A, 0x9C)),  // 4重+
        };

        public ObservableCollection<HistoryRow> History { get; } = new ObservableCollection<HistoryRow>();

        // 颜色（可通过设置窗实时更新）
        private Brush _brushDefault = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        private Brush _brushRight   = new SolidColorBrush(Color.FromRgb(0x16, 0x6F, 0x16));
        private Brush _brushRightBg = new SolidColorBrush(Color.FromRgb(0xCC, 0xF2, 0xCC));
        private Brush _brushWrong   = new SolidColorBrush(Color.FromRgb(0xCC, 0x33, 0x33));
        private Brush _brushWrongBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0xD8));

        // 回改地点高亮（对齐老版 Show_Hg_Place）：触发回改后，被删除的那段字给 0.8s 短暂淡黄背景
        private static readonly Brush HgFlashBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0x3B));
        private readonly DispatcherTimer _hgFlashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        private int _hgFlashFrom, _hgFlashTo;
        private bool _hgFlashActive;

        // 上屏偏慢标记 + 回改位置标记（仅在跟打中收集，FinishTyping 时统一染色）
        private static readonly Brush SlowBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xE8, 0x9A));  // 浅绿 = 慢
        private static readonly Brush HgBrush   = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x8A));  // 浅黄 = 回改
        private const double SlowCharThresholdSec = 1.2;
        private readonly HashSet<int> _slowMarks = new HashSet<int>();
        private readonly HashSet<int> _hgMarks   = new HashSet<int>();

        // 当前段号：发文模式下记录"刚发出的段号"，结算时写入历史。非发文置 0。
        private int _currentSegNo;

        /// <summary>把对照区滚到当前光标位置，保证最后一行不被卡在视窗下方。</summary>
        private void ScrollCompareToCursor(int len)
        {
            if (_charRuns.Count == 0) return;
            int idx = Math.Min(Math.Max(len, 0), _charRuns.Count - 1);
            try { _charRuns[idx].BringIntoView(); } catch { }
        }

        /// <summary>结算时统一染色：先涂回改(浅黄)，再涂慢字(浅绿)，错字红色已由 TextChanged 维护。
        /// 慢字背景优先级高于回改（最近一次输入的"慢"事件覆盖之前的"回改"标记）。</summary>
        private void ApplyResultMarks()
        {
            foreach (var i in _hgMarks)
            {
                if (i < 0 || i >= _charRuns.Count) continue;
                if (i < _runStatus.Length && _runStatus[i] == 2) continue;  // 错字不覆盖
                _charRuns[i].Background = HgBrush;
            }
            foreach (var i in _slowMarks)
            {
                if (i < 0 || i >= _charRuns.Count) continue;
                if (i < _runStatus.Length && _runStatus[i] == 2) continue;
                _charRuns[i].Background = SlowBrush;
            }
        }

        // 跟打地图：嵌入式 Canvas + Polyline，横轴=用时，纵轴=已打字数占比
        private readonly System.Collections.Generic.List<System.Windows.Point> _mapPoints = new System.Collections.Generic.List<System.Windows.Point>();
        private System.Windows.Shapes.Polyline _mapLine;
        private double _mapW, _mapH;

        // 长时间未跟打自动重打（对齐老版 timer5）
        private readonly DispatcherTimer _autoRepeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private DateTime _lastInputAt;

        // 本段重打次数（每次 Repeat() +1；换新文段时清零）
        private int _repeatCount;
        private bool _isRepeating;
        // 词组下划线颜色（按词长）
        private static readonly Brush[] WordUnderlineBrushes =
        {
            new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xE2)),  // 2 字（蓝）
            new SolidColorBrush(Color.FromRgb(0x9C, 0x4A, 0xE2)),  // 3 字（紫）
            new SolidColorBrush(Color.FromRgb(0xE2, 0x4A, 0x4A)),  // 4 字+（红）
        };
        public MainWindow()
        {
            InitializeComponent();
            TbxInput.TextChanged += TbxInput_TextChanged;
            TbxInput.PreviewKeyDown += TbxInput_PreviewKeyDown;
            _ime.Attach(TbxInput);
            TbxInput.LostFocus += (s, e) => PauseType();
            _flashTimer.Tick += FlashTimer_Tick;
            _hgFlashTimer.Tick += HgFlashTimer_Tick;
            _autoRepeatTimer.Tick += AutoRepeatTimer_Tick;
            _autoRepeatTimer.Start();

            DgvHistory.ItemsSource = History;

            _timerTime.Tick  += TimerTime_Tick;
            _timerStats.Tick += TimerStats_Tick;

            _keyHook.KeyDown += KeyHook_KeyDown;
            try { _keyHook.Start(); } catch { /* 钩子安装失败，不影响 UI */ }

            // 异步加载词典（76145 行，主线程加载会卡 ~200ms，可以接受但提示一下）
            try { _dict.LoadFromResource(); } catch { /* 词典加载失败不致命 */ }

            // 从 %AppData%\newgdq\settings.json 恢复设置（窗口几何 + 标记栏开关）
            SettingsService.Load();
            // SQLite 历史持久化初始化 + 装载最近 200 条
            HistoryRepository.Init();
            foreach (var row in HistoryRepository.LoadRecent(200))
                History.Add(row);
            _historyIndex = HistoryRepository.TotalCount();
            RefreshSummaryCache();
            this.Loaded += MainWindow_Loaded;
            this.Closing += MainWindow_Closing;
            this.StateChanged += MainWindow_StateChanged;

            this.Closed += (s, e) =>
            {
                _timerTime.Stop();
                _timerStats.Stop();
                _keyHook.Dispose();
                try { TrayIcon?.Dispose(); } catch { }
            };
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var s = SettingsService.Instance;

            // 窗口几何
            if (s.WindowWidth  is double w && w > 100) this.Width  = w;
            if (s.WindowHeight is double h && h > 100) this.Height = h;
            if (s.WindowLeft is double l && s.WindowTop is double t)
            {
                // 简单防越界（屏幕变小 / 多屏切换后位置可能落在屏外）
                var work = SystemParameters.WorkArea;
                if (l < work.Right - 50 && t < work.Bottom - 50 && l > work.Left - 100 && t > work.Top - 50)
                {
                    this.Left = l;
                    this.Top  = t;
                }
            }
            if (s.WindowMaximized == true) this.WindowState = WindowState.Maximized;

            // 标记栏开关
            if (s.TogBmTips  is bool b1) TogBmTips.IsChecked = b1;
            if (s.TogChart   is bool b2) TogChart.IsChecked  = b2;
            if (s.TogMark    is bool b3) TogMark.IsChecked   = b3;
            if (s.TogSimple  is bool b4) TogSimple.IsChecked = b4;
            if (s.TogDetail  is bool b5) TogDetail.IsChecked = b5;
            // SegRulerBox 已废弃 - 用户反馈无用，永远 Collapsed
            if (s.TogMap     is bool b8) TogMap.IsChecked    = b8;
            if (s.SmartCi    is bool b7) MnuSmartCi.IsChecked = b7;

            // 字体/颜色/个签
            ApplyAppearance();
        }

        /// <summary>把 SettingsService.Instance 的字体/颜色/个签应用到 UI。
        /// 设置窗每次"应用"后由设置窗调用一次。</summary>
        public void ApplyAppearance()
        {
            var s = SettingsService.Instance;

            // 字体
            if (!string.IsNullOrEmpty(s.CompareFontFamily))
                RtbCompare.FontFamily = new FontFamily(s.CompareFontFamily);
            if (s.CompareFontSize is double cfs && cfs >= 8 && cfs <= 96)
                RtbCompare.FontSize = cfs;
            if (!string.IsNullOrEmpty(s.InputFontFamily))
                TbxInput.FontFamily = new FontFamily(s.InputFontFamily);
            if (s.InputFontSize is double ifs && ifs >= 8 && ifs <= 96)
                TbxInput.FontSize = ifs;

            // 颜色：更新 brushes 并对已渲染字符重新染色
            if (TryParseColor(s.ColorRight,    out var c1)) _brushRight   = new SolidColorBrush(c1);
            if (TryParseColor(s.ColorRightBg,  out var c2)) _brushRightBg = new SolidColorBrush(c2);
            if (TryParseColor(s.ColorWrong,    out var c3)) _brushWrong   = new SolidColorBrush(c3);
            if (TryParseColor(s.ColorWrongBg,  out var c4)) _brushWrongBg = new SolidColorBrush(c4);
            if (TryParseColor(s.ColorCompareBg,out var c5)) RtbCompare.Background = new SolidColorBrush(c5);
            if (TryParseColor(s.ColorInputBg,  out var c6)) TbxInput.Background   = new SolidColorBrush(c6);

            // 重刷已染色字符
            RecolorRenderedChars();

            // 个签 / 联系州州：启用个签则显示个签文本，否则显示默认微信号
            if (s.SignEnabled == true && !string.IsNullOrEmpty(s.SignText))
                TxtSign.Text = s.SignText;
            else
                TxtSign.Text = "微信 " + WECHAT_ID;
        }

        // ===== 联系州州（信息条最右格点击）=====
        private void TxtSign_LeftClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetText(WECHAT_ID);
                HandyControl.Controls.Growl.Success("已复制微信号：" + WECHAT_ID);
            }
            catch (Exception ex) { HandyControl.Controls.Growl.Error(ex.Message); }
        }

        private static bool TryParseColor(string hex, out Color c)
        {
            c = default(Color);
            if (string.IsNullOrEmpty(hex)) return false;
            try
            {
                var obj = ColorConverter.ConvertFromString(hex);
                if (obj is Color cc) { c = cc; return true; }
            }
            catch { }
            return false;
        }

        /// <summary>颜色变化后，对已渲染的对照区每个字按当前输入比对状态重新染色。</summary>
        private void RecolorRenderedChars()
        {
            if (_charRuns.Count == 0) return;
            // 颜色变了，作废所有缓存状态，让下次 TextChanged 重新涂。
            // 这里同步把可见区也立即重涂一次（不靠下次 TextChanged 等待用户按键）。
            string input = TbxInput.Text ?? string.Empty;
            int len = Math.Min(input.Length, _charRuns.Count);
            for (int i = 0; i < len; i++)
            {
                if (input[i] == _session.TypeText[i])
                {
                    _charRuns[i].Foreground = _brushRight;
                    _charRuns[i].Background = _brushRightBg;
                    if (i < _runStatus.Length) _runStatus[i] = 1;
                }
                else
                {
                    _charRuns[i].Foreground = _brushWrong;
                    _charRuns[i].Background = _brushWrongBg;
                    if (i < _runStatus.Length) _runStatus[i] = 2;
                }
            }
            for (int i = len; i < _charRuns.Count; i++)
            {
                _charRuns[i].Foreground = _brushDefault;
                _charRuns[i].Background = null;
                if (i < _runStatus.Length) _runStatus[i] = 0;
            }
        }

        /// <summary>回改地点高亮（对齐老版 Show_Hg_Place）：把 [from, to) 那段字短暂置为淡黄背景。
        /// 0.8s 后由 <see cref="HgFlashTimer_Tick"/> 还原。</summary>
        private void TriggerHgFlash(int from, int to)
        {
            // 若已有高亮在闪，先恢复上次的，避免叠加颜色错乱
            if (_hgFlashActive) HgFlashTimer_Tick(null, null);

            from = Math.Max(0, from);
            to   = Math.Min(_charRuns.Count, to);
            if (from >= to) return;

            for (int i = from; i < to; i++)
                _charRuns[i].Background = HgFlashBrush;

            _hgFlashFrom = from;
            _hgFlashTo   = to;
            _hgFlashActive = true;
            _hgFlashTimer.Stop();
            _hgFlashTimer.Start();
        }

        private void HgFlashTimer_Tick(object sender, EventArgs e)
        {
            _hgFlashTimer.Stop();
            if (!_hgFlashActive) return;
            _hgFlashActive = false;
            // 还原：被高亮的那段字按当前输入对比状态重新染色
            string input = TbxInput.Text ?? string.Empty;
            int end = Math.Min(_hgFlashTo, _charRuns.Count);
            for (int i = _hgFlashFrom; i < end; i++)
            {
                if (i < input.Length && i < _session.TypeText.Length)
                {
                    if (input[i] == _session.TypeText[i])
                    {
                        _charRuns[i].Foreground = _brushRight;
                        _charRuns[i].Background = _brushRightBg;
                        if (i < _runStatus.Length) _runStatus[i] = 1;
                    }
                    else
                    {
                        _charRuns[i].Foreground = _brushWrong;
                        _charRuns[i].Background = _brushWrongBg;
                        if (i < _runStatus.Length) _runStatus[i] = 2;
                    }
                }
                else
                {
                    _charRuns[i].Foreground = _brushDefault;
                    _charRuns[i].Background = null;
                    if (i < _runStatus.Length) _runStatus[i] = 0;
                }
            }
        }

        private bool _exitingFromTray;

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            // 最小化到托盘：仅当 settings 启用 + 当前处于 Minimized + 不是从托盘"退出"触发
            if (_exitingFromTray) return;
            if (this.WindowState == WindowState.Minimized
                && SettingsService.Instance.MinimizeToTray == true)
            {
                this.Hide();
                try
                {
                    TrayIcon?.ShowBalloonTip("州州跟打器", "已最小化到托盘，单击图标恢复",
                        Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
                }
                catch { /* 某些系统通知被禁用，忽略 */ }
            }
        }

        private void TrayIcon_LeftClick(object sender, RoutedEventArgs e) => RestoreFromTray();
        private void TrayMenu_Show_Click(object sender, RoutedEventArgs e) => RestoreFromTray();

        private void TrayMenu_Exit_Click(object sender, RoutedEventArgs e)
        {
            _exitingFromTray = true;
            this.Close();
        }

        private void RestoreFromTray()
        {
            if (!this.IsVisible) this.Show();
            if (this.WindowState == WindowState.Minimized) this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var s = SettingsService.Instance;
            // 最大化时记 RestoreBounds，以便下次打开还原成"还原态"位置
            bool isMax = this.WindowState == WindowState.Maximized;
            var rect = isMax ? this.RestoreBounds : new Rect(this.Left, this.Top, this.Width, this.Height);
            s.WindowLeft       = rect.Left;
            s.WindowTop        = rect.Top;
            s.WindowWidth      = rect.Width;
            s.WindowHeight     = rect.Height;
            s.WindowMaximized  = isMax;

            s.TogBmTips  = TogBmTips.IsChecked  == true;
            s.TogChart   = TogChart.IsChecked   == true;
            s.TogMark    = TogMark.IsChecked    == true;
            s.TogSimple  = TogSimple.IsChecked  == true;
            s.TogDetail  = TogDetail.IsChecked  == true;
            // SegRulerBox 已废弃
            // s.TogSegRuler= SegRulerBox.Visibility == Visibility.Visible;
            s.TogMap     = TogMap.IsChecked    == true;
            s.SmartCi    = MnuSmartCi.IsChecked == true;

            SettingsService.Save();
        }

        // ===== 速度曲线 =====

        private void TogChart_Toggled(object sender, RoutedEventArgs e)
        {
            bool on = TogChart.IsChecked == true;
            if (on && _chartModel == null) BuildInlineChart();
            ChartCol.Width          = on ? new GridLength(300) : new GridLength(0);
            ChartSplitterCol.Width  = on ? new GridLength(4)   : new GridLength(0);
        }

        private void BuildInlineChart()
        {
            // 从主题取色，浅色下白底深字、暗色下深底亮字
            var bgBrush   = this.TryFindResource("PanelBG")  as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(0x1E,0x1E,0x1E));
            var fgBrush   = this.TryFindResource("LabelFG")  as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(0x88,0x88,0x88));
            var gridLine  = this.TryFindResource("GridLine") as SolidColorBrush ?? new SolidColorBrush(Color.FromArgb(0x22,0xFF,0xFF,0xFF));
            var bgColor   = OxyPlot.OxyColor.FromArgb(0xFF, bgBrush.Color.R,  bgBrush.Color.G,  bgBrush.Color.B);
            var fgColor   = OxyPlot.OxyColor.FromArgb(0xFF, fgBrush.Color.R,  fgBrush.Color.G,  fgBrush.Color.B);
            var gridColor = OxyPlot.OxyColor.FromArgb(0x55, gridLine.Color.R, gridLine.Color.G, gridLine.Color.B);

            _chartModel = new OxyPlot.PlotModel
            {
                Background          = bgColor,
                PlotAreaBorderColor = OxyPlot.OxyColors.Transparent,
                TextColor           = fgColor,
                DefaultFont         = "微软雅黑",
                DefaultFontSize     = 11,
                PlotMargins         = new OxyPlot.OxyThickness(42, 8, 10, 24),
                Padding             = new OxyPlot.OxyThickness(0),
                Title               = null,
            };
            _chartModel.Axes.Add(new OxyPlot.Axes.LinearAxis
            {
                Position           = OxyPlot.Axes.AxisPosition.Bottom,
                Title              = "用时(s)",
                TitleFontSize      = 10,
                AxislineColor      = gridColor,
                AxislineThickness  = 1,
                MajorGridlineStyle = OxyPlot.LineStyle.Solid,
                MajorGridlineColor = gridColor,
                MinorGridlineStyle = OxyPlot.LineStyle.Dot,
                MinorGridlineColor = OxyPlot.OxyColor.FromArgb(0x33, gridLine.Color.R, gridLine.Color.G, gridLine.Color.B),
                Minimum            = 0,
                IntervalLength     = 60,
                TickStyle          = OxyPlot.Axes.TickStyle.Outside,
                MajorTickSize      = 4,
                MinorTickSize      = 2,
                FontSize           = 10,
            });
            _chartModel.Axes.Add(new OxyPlot.Axes.LinearAxis
            {
                Position           = OxyPlot.Axes.AxisPosition.Left,
                Title              = "速度(字/分)",
                TitleFontSize      = 10,
                AxislineColor      = gridColor,
                AxislineThickness  = 1,
                MajorGridlineStyle = OxyPlot.LineStyle.Solid,
                MajorGridlineColor = gridColor,
                MinorGridlineStyle = OxyPlot.LineStyle.None,
                Minimum            = 0,
                TickStyle          = OxyPlot.Axes.TickStyle.Outside,
                MajorTickSize      = 4,
                FontSize           = 10,
                StringFormat       = "0",
            });
            _chartSpeed = new OxyPlot.Series.AreaSeries
            {
                Title           = "速度",
                Color           = OxyPlot.OxyColor.FromRgb(0xFF, 0xB0, 0x2E),
                StrokeThickness = 1.6,
                MarkerType      = OxyPlot.MarkerType.None,
                Fill            = OxyPlot.OxyColor.FromArgb(0x55, 0xFF, 0xB0, 0x2E),
                InterpolationAlgorithm = OxyPlot.InterpolationAlgorithms.CanonicalSpline,
                LineJoin        = OxyPlot.LineJoin.Round,
            };
            _chartModel.Series.Add(_chartSpeed);
            InlineChart.Model = _chartModel;
        }

        private void InlineChartReset()
        {
            if (_chartSpeed == null) return;
            _chartSpeed.Points.Clear();
            if (_chartFinishMark != null) { _chartModel.Series.Remove(_chartFinishMark); _chartFinishMark = null; }
            _chartModel.InvalidatePlot(true);
        }

        private int _chartAddCounter;
        private void InlineChartAddPoint(double sec, double speed)
        {
            if (_chartSpeed == null) return;
            _chartSpeed.Points.Add(new OxyPlot.DataPoint(sec, speed));
            // 限点：最多 600 点 + 每 5 点才刷一次避免 GC 压力
            if (_chartSpeed.Points.Count > 600) _chartSpeed.Points.RemoveAt(0);
            if ((++_chartAddCounter % 5) == 0) _chartModel.InvalidatePlot(true);
        }

        private void InlineChartMarkFinish()
        {
            if (_chartSpeed == null || _chartSpeed.Points.Count == 0) return;
            var last = _chartSpeed.Points[_chartSpeed.Points.Count - 1];
            _chartFinishMark = new OxyPlot.Series.ScatterSeries
            {
                MarkerType   = OxyPlot.MarkerType.Circle,
                MarkerSize   = 6,
                MarkerFill   = OxyPlot.OxyColor.FromRgb(0xFF, 0xD2, 0x4C),
                MarkerStroke = OxyPlot.OxyColors.White,
            };
            _chartFinishMark.Points.Add(new OxyPlot.Series.ScatterPoint(last.X, last.Y));
            _chartModel.Series.Add(_chartFinishMark);
            _chartModel.InvalidatePlot(true);
        }

        // ===== 跟打地图（嵌入式 Canvas + Polyline）=====

        private void TogMap_Toggled(object sender, RoutedEventArgs e)
        {
            MapPanel.Visibility = TogMap.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (TogMap.IsChecked == true) RedrawMap();
        }

        private void MapCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _mapW = e.NewSize.Width;
            _mapH = e.NewSize.Height;
            RedrawMap();
        }

        private void MapAddSample()
        {
            // 节奏热力条：完全由 Report 事件驱动，TextChanged 里已自动重画
            // 这里只在采样定时器里触发一次重画，确保即时更新
            if (MapPanel.Visibility != Visibility.Visible) return;
            RedrawMap();
        }
        private int _mapDrawCounter;

        private void MapReset()
        {
            _charMs = null;
            RedrawMap();
        }

        // 长时间未跟打自动重打：每秒检查一次，若跟打中且距上次输入超过阈值分钟 → 触发 F3
        private void AutoRepeatTimer_Tick(object sender, EventArgs e)
        {
            int? th = SettingsService.Instance.AutoRepeatMinutes;
            if (!th.HasValue || th.Value <= 0) return;
            if (!_session.Started || _session.Finished) return;
            if (_isPaused) return;
            if (_lastInputAt == default) return;

            if ((DateTime.Now - _lastInputAt).TotalMinutes >= th.Value)
            {
                Repeat();
                HandyControl.Controls.Growl.Info($"已超过 {th.Value} 分钟无输入，自动重打");
            }
        }

        // 节奏热力条：每个字一格，按打这个字花的毫秒数上色
        private double[] _charMs;

        private void RedrawMap()
        {
            if (MapCanvas == null) return;
            MapCanvas.Children.Clear();
            if (_mapW <= 1 || _mapH <= 1) return;
            int total = _session.TypeText.Length;
            if (total <= 0) return;

            // 从 Report 事件汇总每个字的耗时（事件 perChar 平均分摊到这次输入的字）
            if (_charMs == null || _charMs.Length != total) _charMs = new double[total];
            else Array.Clear(_charMs, 0, _charMs.Length);
            int prevLen = 0;
            foreach (var ev in _session.Report)
            {
                if (ev.Length <= 0) { prevLen = ev.End; continue; }
                double perCharMs = ev.TotalTime * 1000.0 / ev.Length;
                int to = Math.Min(ev.End, total);
                for (int i = prevLen; i < to; i++) _charMs[i] = perCharMs;
                prevLen = ev.End;
            }

            // 找最慢字索引（用于发光）
            double maxMs = 0; int maxIdx = -1;
            for (int i = 0; i < total; i++)
                if (_charMs[i] > maxMs) { maxMs = _charMs[i]; maxIdx = i; }

            double cellW = _mapW / total;
            // 每格至少 2px 宽，避免几百字时出现亚像素错位
            double drawCellW = Math.Max(cellW, 1.5);

            for (int i = 0; i < total; i++)
            {
                var fill = ColorForMs(_charMs[i]);
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = drawCellW,
                    Height = _mapH,
                    Fill = fill,
                };
                System.Windows.Controls.Canvas.SetLeft(rect, i * cellW);
                System.Windows.Controls.Canvas.SetTop(rect, 0);
                if (i == maxIdx && maxMs > 800)
                {
                    rect.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Colors.White,
                        BlurRadius = 8,
                        ShadowDepth = 0,
                        Opacity = 0.9,
                    };
                }
                MapCanvas.Children.Add(rect);
            }

            // 顶部 1px 高光线 + 底部 1px 阴影线，立体感
            MapCanvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = 0, Y1 = 0, X2 = _mapW, Y2 = 0,
                Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1,
            });
        }

        /// <summary>毫秒 → 颜色（绿→黄→橙→红→深红）。空格未打的字用透明灰。</summary>
        private static Brush ColorForMs(double ms)
        {
            if (ms <= 0) return new SolidColorBrush(Color.FromArgb(0x30, 0x55, 0x55, 0x60));
            // 渐变锚点（ms, ARGB）：300 绿 / 600 黄 / 1200 橙 / 2500+ 深红
            Color c;
            if (ms <= 300) c = Lerp(Color.FromRgb(0x4C, 0xC9, 0x6A), Color.FromRgb(0xA8, 0xE0, 0x5F), Norm(ms, 0, 300));
            else if (ms <= 600) c = Lerp(Color.FromRgb(0xA8, 0xE0, 0x5F), Color.FromRgb(0xFF, 0xD7, 0x2E), Norm(ms, 300, 600));
            else if (ms <= 1200) c = Lerp(Color.FromRgb(0xFF, 0xD7, 0x2E), Color.FromRgb(0xFF, 0x8C, 0x1A), Norm(ms, 600, 1200));
            else if (ms <= 2500) c = Lerp(Color.FromRgb(0xFF, 0x8C, 0x1A), Color.FromRgb(0xE7, 0x3E, 0x3E), Norm(ms, 1200, 2500));
            else c = Color.FromRgb(0xB0, 0x1F, 0x1F);
            return new SolidColorBrush(c);
        }
        private static double Norm(double v, double a, double b) { double t = (v - a) / (b - a); return t < 0 ? 0 : (t > 1 ? 1 : t); }
        private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));

        // ===== 编码提示（当前字 1 个）=====

        /// <summary>测试用户自定义的 bm.txt 是否合法（不替换当前词典）。
        /// 老版 FormBMTips 的目的，新版以临时 DictionaryService 加载并显示统计。</summary>
        private void MenuItem_TestBmFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 bm.txt 文件验证",
                Filter = "文本词典|*.txt|所有文件|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog() != true) return;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var tmp = new Services.DictionaryService();
                tmp.LoadFromFile(dlg.FileName);
                sw.Stop();
                var fi = new System.IO.FileInfo(dlg.FileName);
                string msg =
                    $"✓ 文件合法可加载\n\n" +
                    $"路径：{dlg.FileName}\n" +
                    $"大小：{fi.Length / 1024.0:0.0} KB\n" +
                    $"用时：{sw.ElapsedMilliseconds} ms\n\n" +
                    $"总条目：{tmp.TotalEntries}\n" +
                    $"独立单字：{tmp.SingleCount}\n" +
                    $"词组条目：{tmp.PhraseCount}\n\n" +
                    $"格式要求：每行 \"编码 字1 字2 ...\"（空格/Tab 分隔，UTF-8 编码）。\n" +
                    $"本次测试不会替换内置词典，仅为校验。";
                HandyControl.Controls.MessageBox.Show(msg, "bm.txt 校验结果");
            }
            catch (Exception ex)
            {
                sw.Stop();
                HandyControl.Controls.MessageBox.Show(
                    $"✗ 加载失败\n\n{ex.Message}\n\n请检查文件是否为 UTF-8 编码、格式是否正确（每行 \"编码 字1 字2 ...\"）。",
                    "bm.txt 校验失败");
            }
        }

        private void TogBmTips_Toggled(object sender, RoutedEventArgs e)
        {
            BmTipBox.Visibility = TogBmTips.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            RefreshBmTips();
        }

        /// <summary>刷新主窗口右上编码提示标签：显示当前光标位置那个字的码。</summary>
        private void RefreshBmTips()
        {
            if (BmTipBox.Visibility != Visibility.Visible) return;
            int len = _session.LastInputLen;
            if (_session.TypeText.Length == 0 || len >= _session.TypeText.Length)
            {
                BmChar.Text = "-";
                BmCode.Text = "-";
                BmRankBox.Background = Brushes.Transparent;
                return;
            }
            // 智能测词开启 → 词组优先（MatchAt 最长匹配）；关闭 → 只查单字
            BmEntry entry;
            if (MnuSmartCi != null && MnuSmartCi.IsChecked == true)
                entry = _dict.MatchAt(_session.TypeText, len);
            else
                entry = _dict.LookupChar(_session.TypeText[len]);
            if (entry == null)
            {
                BmChar.Text = _session.TypeText[len].ToString();
                BmCode.Text = "无";
                BmRankBox.Background = Brushes.Gray;
                return;
            }
            BmChar.Text = entry.Word;
            BmCode.Text = entry.Code;
            BmRankBox.Background = RankBrushes[Math.Max(0, Math.Min(entry.Rank - 1, RankBrushes.Length - 1))];
        }

        // ===== 字数进度条 =====

        private double _progHostWidth;

        private void ProgHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _progHostWidth = e.NewSize.Width;
            UpdateProgress();
        }

        private void UpdateProgress()
        {
            int total = _session.TypeText.Length;
            int len   = _session.LastInputLen;
            double pct = total > 0 ? Math.Min(1.0, (double)len / total) : 0;
            ProgFill.Width = _progHostWidth * pct;

            // 进度条文本：已完成字数,已完成%
            int donePct = (int)(pct * 100);
            TxtProgress.Text = total > 0 ? $"{len},{donePct}%" : "-";

            // 状态条第 2 格：已打字数 / 总字数、下行进度%
            TxtCount1.Text = $"{len}/{total}";
            TxtCount1Pct.Text = (pct * 100).ToString("0.0") + "%";

            // 末尾汇总：今日字数 / 累计字数 / 训练天数 / 累计段数
            var sm = _summaryCache;
            // 当前正在跟打的字数也叠加到今日（结算后会刷新缓存）
            int todayW = sm.todayWords + len;
            TxtTotalInfo.Text = $"{todayW}/{sm.totalWords + len}/{Math.Max(1, sm.days)}天/{sm.totalSegs}";
        }

        // 历史汇总缓存（开机+每次结算后刷新，避免每帧查 SQLite）
        private (int todayWords, double todaySec, int todaySegs, int totalWords, int totalSegs, int days) _summaryCache;
        private void RefreshSummaryCache()
        {
            try
            {
                _summaryCache = HistoryRepository.LoadSummary();
                var avg = HistoryRepository.LoadAverages();
                if (TxtFootTime != null)
                {
                    var ts = TimeSpan.FromSeconds(_summaryCache.todaySec);
                    TxtFootTime.Text  = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
                    TxtFootSegs.Text  = _summaryCache.todaySegs + "#";
                    TxtFootSpeed.Text = avg.todaySpeed.ToString("0.00");
                    TxtFootJj.Text    = avg.todayJj.ToString("0.00");
                    TxtFootMc.Text    = avg.todayMc.ToString("0.00");
                    TxtFootWords.Text = _summaryCache.todayWords.ToString();
                    TxtFootAllAvg.Text = "累计 " + avg.totalSpeed.ToString("0.00");
                    TxtFootAllJj.Text  = avg.totalJj.ToString("0.00");
                }
            }
            catch { }
        }

        // ===== 加载文章 =====

        private void MenuItem_LoadInternal_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is MenuItem mi)) return;
            var fileName = mi.Tag as string;
            if (string.IsNullOrEmpty(fileName)) return;

            try
            {
                var text  = ArticleLoader.LoadInternal(fileName);
                var title = mi.Header?.ToString() ?? string.Empty;
                _currentSegNo = 0;
                LoadArticle(text, title);
            }
            catch (Exception ex)
            {
                HandyControl.Controls.Growl.Error("载入失败：" + ex.Message);
            }
        }

        private void LoadArticle(string text, string title)
        {
            // 如果上一段已字数打满但因末字错未 finish，载新文时强制以当前成绩入历史
            TryForceFinalizeLastSegment();

            // 替换：底部标记栏"替换"开启时，载入时自动英文标点转中文标点
            if (TogReplace != null && TogReplace.IsChecked == true && !string.IsNullOrEmpty(text))
                text = Services.TextProcessor.En2Cn(text);

            _session.Load(text, title);

            // 重建对照区
            RtbCompare.Document.Blocks.Clear();
            RtbCompare.Document.PagePadding = new Thickness(0);
            _charRuns.Clear();
            _runStatus = new byte[_session.TypeText.Length];
            _slowMarks.Clear();
            _hgMarks.Clear();

            var para = new Paragraph { Margin = new Thickness(0), Padding = new Thickness(0) };
            foreach (var ch in _session.TypeText)
            {
                var run = new Run(ch.ToString()) { Foreground = _brushDefault };
                _charRuns.Add(run);
                para.Inlines.Add(run);
            }
            RtbCompare.Document.Blocks.Add(para);

            // 词组下划线（按词长上色）
            ApplyPhraseUnderlines();

            // 信息条
            this.Title = string.IsNullOrEmpty(title) ? "州州跟打器" : "州州跟打器 - " + title;
            TxtTitle.Text     = string.IsNullOrEmpty(title) ? "-" : title;
            TxtWordCount.Text = "0/" + _session.TypeText.Length + "字";

            ResetUi();
            TbxInput.IsReadOnly = false;  // 新段恢复可输入
            // 限制输入长度 = 文段长度，防止用户超打（IME 提交时 WPF 会自动截断）
            TbxInput.MaxLength = _session.TypeText.Length;
            TbxInput.Clear();
            TbxInput.Focus();

            // 新段载入后把对照区滚回顶部 (BringIntoView 在 Loaded 之前可能没生效，用 Dispatcher 延后一帧)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { RtbCompare.ScrollToHome(); } catch { }
                try { TbxInput.ScrollToHome(); } catch { }
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            UpdateProgress();
            RefreshBmTips();
            ComputeAndShowTheoryMc();
            InlineChartReset();
            MapReset();

            // 重打次数：Repeat() 触发的此次 LoadArticle → +1；换新文段 → 0
            if (_isRepeating) _repeatCount++;
            else _repeatCount = 0;
            RefreshExtraStatus();
        }

        private void ResetUi()
        {
            _timerTime.Stop();
            _timerStats.Stop();
            _flashTimer.Stop();
            _isPaused = false;
            TxtTime.Foreground = NormalTimeBrush;
            TxtTime.Text  = "00:00.00";
            TxtSpeed.Text = "0.00";
            TxtJj.Text    = "0.00";
            TxtMc.Text    = "0.00";
            TxtKeysCount.Text = "0";
            TxtHg.Text    = "0";
            TxtCz.Text    = "0";
            TxtRightLast.Text = "0:0";
            TxtGroup.Text = $"重{_repeatCount} 呆0s 准100%";
        }

        /// <summary>刷新信息条"状态"格：重打次数 / 发呆秒数 / 键准百分比。
        /// 由 TimerStats_Tick 每 200ms 调一次。</summary>
        private void RefreshExtraStatus()
        {
            if (TxtGroup == null) return;
            double idleSec = (_session.Started && !_session.Finished && !_isPaused && _lastInputAt != default)
                ? (DateTime.Now - _lastInputAt).TotalSeconds : 0;
            int keys = _session.Keys;
            double acc = keys > 0 ? (keys - _session.Hg) * 100.0 / keys : 100;
            if (acc < 0) acc = 0;
            TxtGroup.Text = $"重{_repeatCount} 呆{idleSec:0}s 准{acc:0}%";
        }

        // ===== 词组下划线 =====

        private void ApplyPhraseUnderlines()
        {
            if (TogMark == null || TogMark.IsChecked != true) return;
            if (MnuSmartCi == null || MnuSmartCi.IsChecked != true) return;
            if (!_dict.Loaded || _charRuns.Count == 0) return;

            var hits = _dict.SegmentPhrases(_session.TypeText);
            foreach (var hit in hits)
            {
                int idx = hit.Length <= 2 ? 0 : (hit.Length == 3 ? 1 : 2);
                var brush = WordUnderlineBrushes[idx];

                // 查字典该词是否全码（4位码－与原版一致）
                var entry = _dict.MatchAt(_session.TypeText, hit.Start);
                bool fullCode = entry != null && entry.Code.Length >= 4;
                var pen = new Pen(brush, hit.Length >= 4 ? 1.5 : 1.0);
                if (!fullCode)
                {
                    // 非全码：虚线
                    pen.DashStyle = new System.Windows.Media.DashStyle(new double[] { 2, 2 }, 0);
                }

                var dec = new TextDecorationCollection();
                dec.Add(new TextDecoration
                {
                    Location = TextDecorationLocation.Underline,
                    Pen = pen,
                    PenOffset = 1,
                    PenOffsetUnit = TextDecorationUnit.Pixel,
                });

                int end = Math.Min(hit.Start + hit.Length, _charRuns.Count);
                for (int i = hit.Start; i < end; i++)
                {
                    _charRuns[i].TextDecorations = dec;
                }
            }
        }

        private void ClearPhraseUnderlines()
        {
            foreach (var run in _charRuns)
                run.TextDecorations = null;
        }

        private void TogMark_Toggled(object sender, RoutedEventArgs e)
        {
            if (TogMark.IsChecked == true) ApplyPhraseUnderlines();
            else ClearPhraseUnderlines();
        }

        // ===== 极简模式 =====
        // 当前阶段只同步 _session.SimpleMode（供 P4 发送代码读取）。
        private void TogSimple_Toggled(object sender, RoutedEventArgs e)
        {
            // P4 发送时读取 TogSimple.IsChecked 即可，这里占位。
        }

        // ===== 详细模式 =====
        // 控制底部历史 Grid + 曲线窗口的显示
        private void TogDetail_Toggled(object sender, RoutedEventArgs e)
        {
            bool show = TogDetail.IsChecked == true;
            if (HistoryBox != null)
                HistoryBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        // ===== 重打 (F3) =====
        private void MenuItem_Repeat_Click(object sender, RoutedEventArgs e) => Repeat();

        private void Repeat()
        {
            if (_session.TypeText.Length == 0) return;
            _isRepeating = true;
            try { LoadArticle(_session.TypeText, _session.Title); }
            finally { _isRepeating = false; }
        }

        /// <summary>强制结算末段（与老版 TryForceFinalizeLastSegment 对齐）：
        /// 若已开始但未完成，且字数已经达到全文长度 → 立刻按当前成绩入历史。
        /// 避免末字错时按 F3/F5/换文导致整段成绩丢失。</summary>
        private void TryForceFinalizeLastSegment()
        {
            if (!_session.Started || _session.Finished) return;
            if (_session.TypeText.Length == 0) return;
            int inputLen = TbxInput.Text?.Length ?? 0;
            if (inputLen < _session.TypeText.Length) return;
            _session.Finished = true;
            FinishTyping();
        }

        // ===== 复位 =====清空当前文章、输入区、历史不动
        // ===== 发文 =====
        private void MenuItem_OpenSendText_Click(object sender, RoutedEventArgs e) => OpenSendTextWindowWithConfirm();

        private void MenuItem_LoadClipboard_Click(object sender, RoutedEventArgs e) => LoadFromClipboard();

        /// <summary>F4 载文：从剪贴板拉一段文字直接载入对照区（不走发文窗口）。
        /// - 跟打中（已开始且未完成）→ 改为重打当前段，不覆盖（与原版一致）
        /// - 识别原版 "-----第N段 标题" 发文格式：自动剥发文头取正文 + 段号 + 标题
        /// - 否则整段作为正文载入</summary>
        private void LoadFromClipboard()
        {
            // 跟打中按 F4：重打当前段，不覆盖
            if (_session.Started && !_session.Finished && _session.TypeText.Length > 0)
            {
                Repeat();
                HandyControl.Controls.Growl.Info("跟打中，已重打当前段（如需载入新文请先按 F5 复位）");
                return;
            }
            _currentSegNo = 0;
            try
            {
                string raw = System.Windows.Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    HandyControl.Controls.Growl.Warning("剪贴板为空");
                    return;
                }

                // 尝试识别原版"-----第N段 标题\n正文\n-----"格式
                string title = "来自剪切板";
                string body  = raw;
                var m = System.Text.RegularExpressions.Regex.Match(
                    raw,
                    @"-{3,}\s*第(\d+)段\s*([^\r\n]*)\r?\n([\s\S]+?)(?:\r?\n-{3,}|$)");
                if (m.Success)
                {
                    title = $"第{m.Groups[1].Value}段 {m.Groups[2].Value.Trim()}".TrimEnd();
                    body  = m.Groups[3].Value;
                }

                string text = TextProcessor.TickBlock(body);
                if (text.Length == 0)
                {
                    HandyControl.Controls.Growl.Warning("剪贴板内容剔除空格后为空");
                    return;
                }
                LoadArticle(text, title);
                HandyControl.Controls.Growl.Info($"已载入 {text.Length} 字 · {title}");
            }
            catch (Exception ex)
            {
                HandyControl.Controls.Growl.Error("载文失败：" + ex.Message);
            }
        }

        /// <summary>F2 入口：已在发文中则先弹确认。</summary>
        private void OpenSendTextWindowWithConfirm()
        {
            if (_sending.State.Active)
            {
                var r = HandyControl.Controls.MessageBox.Show(
                    "已经在发文中（" + (_sending.State.Title ?? "-") + "）。\n是否重新开始一段新的发文？",
                    "发文确认", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                if (r != System.Windows.MessageBoxResult.Yes) return;
                _sending.Stop();
                _sendStatusWin?.Refresh();
            }
            OpenSendTextWindow();
        }

        private void OpenSendTextWindow()
        {
            var win = new SendTextWindow { Owner = this };
            win.OnStartSending = state =>
            {
                _sending.State.Active         = true;
                _sending.State.FullText       = state.FullText;
                _sending.State.PoolText       = state.PoolText;
                _sending.State.Title          = state.Title;
                _sending.State.Type           = state.Type;
                _sending.State.IsRandom       = state.IsRandom;
                _sending.State.RandomNoRepeat = state.RandomNoRepeat;
                _sending.State.OneSentenceEnd = state.OneSentenceEnd;
                _sending.State.CountPerSeg    = state.CountPerSeg;
                _sending.State.Mark           = state.Mark;
                _sending.State.StartSeg       = state.StartSeg;
                _sending.State.SentSeg        = 0;
                // 来源（SendTextWindow 当前 Tab 名）
                _sending.State.SourceName     = state.SourceName ?? "-";
                SendNext();   // 立即发第一段
                ShowSendStatusWindow();  // 自动弹发文状态窗
            };
            win.ShowDialog();
        }

        private void MenuItem_SendNext_Click(object sender, RoutedEventArgs e) => SendNext();

        /// <summary>乱序重抽当前发文会话：强制 IsRandom=true 后发下一段。</summary>
        private void MenuItem_SendShuffle_Click(object sender, RoutedEventArgs e) => SendShuffle();

        private void SendShuffle()
        {
            if (!_sending.State.Active)
            {
                HandyControl.Controls.Growl.Info("尚未开启发文，请先菜单 → 发文 → 发文...");
                return;
            }
            _sending.State.IsRandom = true;
            // 如果之前是顺序表刷文章，乱序只对 Single 生效。处理：强制按 Single 模式抽。
            var origType = _sending.State.Type;
            _sending.State.Type = SendingTextType.Single;
            SendNext();
            _sending.State.Type = origType;
        }

        // 供 SendStatusWindow 调用的公共入口
        public Models.SendingState GetSendingState() => _sending.State;
        public void StopSending() { _sending.Stop(); _sendStatusWin?.Refresh(); }
        public void SendNextSegment() => SendNext();

        /// <summary>Ctrl+← / Ctrl+→ 相对跳段：仅顺序 / 一句结束模式支持。</summary>
        private void JumpSegRelative(int delta)
        {
            if (!_sending.State.Active) return;
            int cur = _sending.State.CurSeg - 1;
            int target = cur + delta;
            if (target < 1) target = 1;
            string seg = _sending.JumpToSeg(target);
            if (seg == null)
            {
                HandyControl.Controls.Growl.Info("当前模式不支持跳段（乱序）");
                return;
            }
            LoadArticle(seg, $"{_sending.State.Title} · 第 {target} 段");
            _currentSegNo = target;
            _sendStatusWin?.Refresh();
        }

        private Views.SendStatusWindow _sendStatusWin;
        private void ShowSendStatusWindow()
        {
            if (_sendStatusWin == null)
            {
                _sendStatusWin = new Views.SendStatusWindow(this);
                _sendStatusWin.Closed += (s, e) => _sendStatusWin = null;
            }
            _sendStatusWin.Show();
            _sendStatusWin.Activate();
            _sendStatusWin.Refresh();
        }

        /// <summary>发下一段：从 SendingService.NextSegment() 取文本，加载到对照区。</summary>
        private void SendNext()
        {
            if (!_sending.State.Active)
            {
                HandyControl.Controls.Growl.Info("尚未开启发文，请先菜单 → 发文 → 发文...");
                return;
            }
            string seg = _sending.NextSegment();
            if (seg == null)
            {
                HandyControl.Controls.Growl.Success("全部发送完毕");
                _sending.Stop();
                return;
            }
            int curSeg = _sending.State.CurSeg - 1; // SentSeg 已 ++，当前段号 = StartSeg + (SentSeg - 1)
            _currentSegNo = curSeg;
            string title = $"{_sending.State.Title} · 第 {curSeg} 段";
            LoadArticle(seg, title);
            _sendStatusWin?.Refresh();
        }
        private void MnuSmartCi_Click(object sender, RoutedEventArgs e)
        {
            if (MnuSmartCi.IsChecked != true)
            {
                TxtTheoryMc.Text = "-";
                ClearPhraseUnderlines();
                RefreshBmTips();
                HandyControl.Controls.Growl.Info("单字模式");
                return;
            }
            ApplyPhraseUnderlines();
            RefreshBmTips();
            ComputeAndShowTheoryMc();
        }

        private void ComputeAndShowTheoryMc()
        {
            if (MnuSmartCi == null || MnuSmartCi.IsChecked != true)
            {
                if (TxtTheoryMc != null) TxtTheoryMc.Text = "-";
                return;
            }
            if (!_dict.Loaded)
            {
                TxtTheoryMc.Text = "?";
                HandyControl.Controls.Growl.Warning("词典未加载");
                return;
            }
            if (_session.TypeText.Length == 0)
            {
                TxtTheoryMc.Text = "-";
                return;
            }

            double mc = _dict.ComputeTheoryMc(_session.TypeText);
            TxtTheoryMc.Text = mc.ToString("0.00");
        }

        // ===== 帮助菜单 =====
        private const string QQ_GROUP_URL = "https://qm.qq.com/q/eb2iF433q2";
        private const string QQ_GROUP_ID  = "17079867";
        private const string WECHAT_ID    = "synhxb";
        private const string PROJECT_URL  = "https://github.com/lwl4613615/zhouzhou-typing";

        private void MenuItem_Hotkeys_Click(object sender, RoutedEventArgs e)
        {
            HandyControl.Controls.MessageBox.Show(
                "—— 全局热键（任何窗口都生效）——\n" +
                "F2   打开发文窗口\n" +
                "F3   重打当前段\n" +
                "F4   载文（剪贴板 → 对照区）\n" +
                "F6   发下一段\n" +
                "F8   暂停 / 继续\n\n" +
                "—— 主窗激活时的快捷键 ——\n" +
                "F9         复制最新一段成绩\n" +
                "Ctrl+R     发下一段（同 F6）\n" +
                "Ctrl+F2    打开发文状态窗\n" +
                "Ctrl+←/→   上一段 / 下一段（跳段）\n" +
                "Ctrl+T     发送图片成绩\n" +
                "Ctrl+B     击键评定\n" +
                "Ctrl+E     速度分析\n" +
                "Ctrl+G     跟打报告\n" +
                "Ctrl+Q     将目前文章乱序\n" +
                "Ctrl+W     英文标点换中文\n\n" +
                "提示：输入框失焦会自动暂停；回到输入框敲任意键自动继续。",
                "快捷键列表");
        }

        private void MenuItem_Homepage_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(PROJECT_URL); }
            catch (Exception ex) { HandyControl.Controls.Growl.Error(ex.Message); }
        }

        private void MenuItem_JoinQQ_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(QQ_GROUP_URL); }
            catch (Exception ex) { HandyControl.Controls.Growl.Error(ex.Message); }
        }

        // ===== 文章处理（菜单 → 功能 → 文章处理）=====

        private void MenuItem_ShuffleArticle_Click(object sender, RoutedEventArgs e)
        {
            if (_session.TypeText.Length == 0)
            { HandyControl.Controls.Growl.Info("当前无文段"); return; }
            string shuffled = Services.TextProcessor.Shuffle(_session.TypeText);
            LoadArticle(shuffled, _session.Title + "（已乱序）");
        }

        private void MenuItem_En2CnPunct_Click(object sender, RoutedEventArgs e)
        {
            if (_session.TypeText.Length == 0)
            { HandyControl.Controls.Growl.Info("当前无文段"); return; }
            string converted = Services.TextProcessor.En2Cn(_session.TypeText);
            LoadArticle(converted, _session.Title);
        }

        private void MenuItem_StripSpace_Click(object sender, RoutedEventArgs e)
        {
            if (_session.TypeText.Length == 0)
            { HandyControl.Controls.Growl.Info("当前无文段"); return; }
            string stripped = Services.TextProcessor.TickBlock(_session.TypeText);
            LoadArticle(stripped, _session.Title);
        }

        // ===== 复制成绩 / 退出 / 发文状态 =====

        private void MenuItem_CopyResult_Click(object sender, RoutedEventArgs e)
        {
            if (History.Count == 0)
            { HandyControl.Controls.Growl.Info("没有可复制的成绩，先打一段"); return; }
            var r = History[0];
            string s = $"第{r.Seg}段 速度{r.Speed:0.00} 罚五{r.Speed2:0.00} 击键{r.Jj:0.00} 码长{r.Mc:0.00} " +
                       $"回改{r.Hg} 错字{r.Cz} 键数{r.Js} 打词{r.DaCi} 用时{r.UseTime:0.00}s · {r.Title}";
            try { System.Windows.Clipboard.SetText(s); HandyControl.Controls.Growl.Success("最新成绩已复制"); }
            catch (Exception ex) { HandyControl.Controls.Growl.Error(ex.Message); }
        }

        private void MenuItem_Exit_Click(object sender, RoutedEventArgs e) => this.Close();

        private void MenuItem_OpenAverage_Click(object sender, RoutedEventArgs e)
        {
            new Views.AverageWindow(this).Show();
        }

        private void MenuItem_SendImageScore_Click(object sender, RoutedEventArgs e)
        {
            if (History.Count == 0)
            { HandyControl.Controls.Growl.Info("没有可发送的成绩，先打一段"); return; }
            AutoCopyResultImage();
        }

        private void MenuItem_SendStatus_Click(object sender, RoutedEventArgs e)
        {
            ShowSendStatusWindow();
        }

        private void MenuItem_About_Click(object sender, RoutedEventArgs e)
        {
            new Views.AboutWindow(this).ShowDialog();
        }

        private void MenuItem_OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new Views.SettingsWindow(this);
            win.ShowDialog();
        }

        private void MenuItem_OpenReport_Click(object sender, RoutedEventArgs e)
        {
            if (_session.TypeText.Length == 0 && _session.Report.Count == 0)
            {
                HandyControl.Controls.Growl.Info("当前没有可分析的跟打数据，先打一段试试");
                return;
            }
            var win = new Views.ReportWindow(_session, this);
            win.Show();
        }

        private void MenuItem_OpenJjCheck_Click(object sender, RoutedEventArgs e)
        {
            new Views.JjCheckWindow(this).Show();
        }

        private void MenuItem_OpenSpeedAnalysis_Click(object sender, RoutedEventArgs e)
        {
            if (_session.TypeText.Length == 0 && _session.Report.Count == 0)
            {
                HandyControl.Controls.Growl.Info("当前没有可分析的跟打数据，先打一段试试");
                return;
            }
            new Views.SpeedAnalysisWindow(_session, this).Show();
        }

        // ===== 信息条段号点击 → 弹列表跳段 =====
        private void TxtCurSegInfo_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_sending.State.Active)
            {
                HandyControl.Controls.Growl.Info("尚未开启发文，请先菜单 → 发文 → 发文...");
                return;
            }
            var segs = _sending.EnumerateSegments(previewLen: 14, maxCount: 300);
            if (segs.Count == 0)
            {
                HandyControl.Controls.Growl.Info("当前模式不支持段号跳转（乱序/词组模式按性质无法预先确定段号）");
                return;
            }

            var menu = new System.Windows.Controls.ContextMenu { MaxHeight = 480 };
            int curSeg = _sending.State.CurSeg - 1; // 当前已发段
            foreach (var (segNo, preview) in segs)
            {
                var mi = new System.Windows.Controls.MenuItem
                {
                    Header = $"第 {segNo} 段  ·  {preview}",
                    IsChecked = (segNo == curSeg),
                };
                int captured = segNo;
                mi.Click += (s2, e2) =>
                {
                    string seg = _sending.JumpToSeg(captured);
                    if (seg == null) { HandyControl.Controls.Growl.Warning("跳转失败"); return; }
                    LoadArticle(seg, $"{_sending.State.Title} · 第 {captured} 段");
                    _currentSegNo = captured;
                };
                menu.Items.Add(mi);
            }
            menu.PlacementTarget = TxtCurSegInfo;
            menu.IsOpen = true;
        }

        private void MenuItem_Reset_Click(object sender, RoutedEventArgs e)
        {
            // 与老版一致：复位前若末段已打满但因末字错未结算，强制入历史，避免数据丢失
            TryForceFinalizeLastSegment();
            _session.Load(string.Empty, string.Empty);
            RtbCompare.Document.Blocks.Clear();
            _charRuns.Clear();
            _runStatus = new byte[0];
            _slowMarks.Clear();
            _hgMarks.Clear();
            this.Title = "州州跟打器";
            TxtTitle.Text = "-";
            TxtWordCount.Text = "0/0字";
            ResetUi();
            TbxInput.IsReadOnly = true;
            TbxInput.Clear();
            UpdateProgress();
            RefreshBmTips();
            if (TxtTheoryMc != null) TxtTheoryMc.Text = "-";
            // 曲线重置
            InlineChartReset();
            MapReset();
            _repeatCount = 0;
            _currentSegNo = 0;
            RefreshExtraStatus();
            HandyControl.Controls.Growl.Info("已复位");
        }

        // ===== 暂停 / 继续（与原版一致：菜单点 或 输入框失焦则暂停；敢一个键自动继续）=====
        private DateTime _pauseStart;
        private bool _isPaused;
        private readonly DispatcherTimer _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private bool _flashOn;
        private static readonly Brush PauseFlashBrush = new SolidColorBrush(Color.FromRgb(0xCD, 0x5C, 0x5C));
        private static readonly Brush NormalTimeBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));

        private void MenuItem_Pause_Click(object sender, RoutedEventArgs e) => TogglePause();

        /// <summary>F8 行为：跟打中按一下暂停；暂停时按一下继续。</summary>
        private void TogglePause()
        {
            if (_isPaused) { EndPause(); TbxInput.Focus(); return; }
            PauseType();
        }

        /// <summary>暂停（幂等）。返回是否真的暂停了。</summary>
        private bool PauseType()
        {
            if (_isPaused) return false;
            if (!_session.Started || _session.Finished) return false;
            int len = _session.LastInputLen;
            if (len <= 0 || len >= _session.TypeText.Length) return false;

            _isPaused = true;
            _pauseStart = DateTime.Now;
            _timerTime.Stop();
            _timerStats.Stop();
            _session.PauseTimes++;
            this.Title += " [已暂停]";
            _flashOn = false;
            _flashTimer.Start();
            return true;
        }

        private void EndPause()
        {
            if (!_isPaused) return;
            // 纯上原版逻辑：暂停期间不计时间 → 重启时把 StartTime 后推过去的量
            var paused = DateTime.Now - _pauseStart;
            _session.StartTime = _session.StartTime.Add(paused);
            _isPaused = false;
            _flashTimer.Stop();
            TxtTime.Foreground = NormalTimeBrush;
            // 窗口标题去掉 [已暂停] 后缀
            if (this.Title.EndsWith(" [已暂停]"))
                this.Title = this.Title.Substring(0, this.Title.Length - " [已暂停]".Length);
            _timerTime.Start();
            _timerStats.Start();
        }

        private void FlashTimer_Tick(object sender, EventArgs e)
        {
            _flashOn = !_flashOn;
            TxtTime.Foreground = _flashOn ? PauseFlashBrush : NormalTimeBrush;
        }

        // ===== 键盘钩子 =====

        // GetAsyncKeyState 用于检测 Ctrl 是否按下（KeyHook 只回报单键 vk，不含修饰）
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private static bool IsCtrlDown() => (GetAsyncKeyState(0x11) & 0x8000) != 0;

        private void KeyHook_KeyDown(object sender, int vk)
        {
            // 所有热键都要求主窗激活才生效，避免在其他程序里误触
            if (!this.IsActive) return;

            switch (vk)
            {
                case 0x71: // F2 打开发文窗口；Ctrl+F2 改为打开发文状态窗
                    if (IsCtrlDown())
                        Dispatcher.BeginInvoke(new Action(ShowSendStatusWindow));
                    else
                        Dispatcher.BeginInvoke(new Action(OpenSendTextWindowWithConfirm));
                    return;
                case 0x72: // F3 重打
                    Dispatcher.BeginInvoke(new Action(Repeat));
                    return;
                case 0x73: // F4 载文（拉剪贴板直接进对照区）
                    Dispatcher.BeginInvoke(new Action(LoadFromClipboard));
                    return;
                case 0x75: // F6 发下一段
                    Dispatcher.BeginInvoke(new Action(SendNext));
                    return;
                case 0x77: // F8 暂停 / 继续
                    Dispatcher.BeginInvoke(new Action(TogglePause));
                    return;
                case 0x78: // F9 复制最新成绩
                    Dispatcher.BeginInvoke(new Action(() => MenuItem_CopyResult_Click(null, null)));
                    return;
            }

            // Ctrl+字母 组合键
            if (IsCtrlDown())
            {
                switch (vk)
                {
                    case 0x54: // Ctrl+T 发送图片成绩
                        Dispatcher.BeginInvoke(new Action(() => MenuItem_SendImageScore_Click(null, null)));
                        return;
                    case 0x42: // Ctrl+B 击键评定
                        Dispatcher.BeginInvoke(new Action(() => MenuItem_OpenJjCheck_Click(null, null)));
                        return;
                    case 0x45: // Ctrl+E 速度分析
                        Dispatcher.BeginInvoke(new Action(() => MenuItem_OpenSpeedAnalysis_Click(null, null)));
                        return;
                    case 0x47: // Ctrl+G 跟打报告
                        Dispatcher.BeginInvoke(new Action(() => MenuItem_OpenReport_Click(null, null)));
                        return;
                    case 0x51: // Ctrl+Q 文章乱序
                        Dispatcher.BeginInvoke(new Action(() => MenuItem_ShuffleArticle_Click(null, null)));
                        return;
                    case 0x57: // Ctrl+W 英文标点换中文
                        Dispatcher.BeginInvoke(new Action(() => MenuItem_En2CnPunct_Click(null, null)));
                        return;
                    case 0x52: // Ctrl+R 发下一段（对齐老版菜单快捷键）
                        Dispatcher.BeginInvoke(new Action(SendNext));
                        return;
                    case 0x25: // Ctrl+← 上一段
                        Dispatcher.BeginInvoke(new Action(() => JumpSegRelative(-1)));
                        return;
                    case 0x27: // Ctrl+→ 下一段（跳段，不是发送）
                        Dispatcher.BeginInvoke(new Action(() => JumpSegRelative(+1)));
                        return;
                }
            }

            if (!_session.Started) return;
            if (!TbxInput.IsKeyboardFocused) return;

            // 击键只计字母 / 数字 / 标点 / 回车 / 退格 / 空格（与原版一致，排除修饰键、功能键、方向键、Tab、Esc、Win 等）
            bool isAlpha     = (vk >= 0x41 && vk <= 0x5A);                 // A-Z
            bool isDigit     = (vk >= 0x30 && vk <= 0x39);                 // 0-9 主键盘
            bool isNumpad    = (vk >= 0x60 && vk <= 0x69);                 // 小键盘0-9
            bool isPunct     = (vk >= 0xBA && vk <= 0xC0) || (vk >= 0xDB && vk <= 0xDE); // 标点
            bool isEnter     = vk == 0x0D;
            bool isBackspace = vk == 0x08;
            bool isSpace     = vk == 0x20;
            if (!(isAlpha || isDigit || isNumpad || isPunct || isEnter || isBackspace || isSpace))
                return;

            _session.Keys++;

            // IME 退格计数（替代老版的"回车"列）：物理 Backspace + 此时 IME 在合成中
            // = 用户在拼音候选框里删拼音，不是删跟打区已上屏的字（后者计入 Hg 回改）
            if (isBackspace && _ime.IsComposing) _session.Enter++;

            // 选重计数（对齐老版）：按 ; (0xBA) / ' (0xDE) / 0-9 数字主键 时，
            // 若原文当前位置的字符不是这些"选重键字符"，认为用户在挑候选 → +1
            if (vk == 0xBA || vk == 0xDE || (vk >= 0x30 && vk <= 0x39))
            {
                int pos = _session.LastInputLen;
                if (pos < _session.TypeText.Length)
                {
                    char src = _session.TypeText[pos];
                    bool srcIsSelectKey = src == ';' || src == '\'' || (src >= '0' && src <= '9');
                    if (!srcIsSelectKey) _session.Reselect++;
                }
            }

            // 左右手字母区（与原版一致）。
            if ((vk >= 65 && vk <= 71) || (vk >= 81 && vk <= 84) || vk == 88 || vk == 90)
                _session.LeftHand++;
            else if ((vk >= 72 && vk <= 80) || vk == 85 || vk == 89)
                _session.RightHand++;
        }

        private void TbxInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!_session.Started) return;
            if (e.Key == System.Windows.Input.Key.Back)
            {
                _session.Hg++;
                TxtHg.Text = _session.Hg.ToString();
            }
        }

        // ===== 输入比对染色 =====

        private void TbxInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_session.TypeText.Length == 0) return;
            if (_session.Finished) return;

            // \u6682\u505c\u4e2d\u6562\u952e \u2192 \u81ea\u52a8\u7ee7\u7eed\uff08\u539f\u7248\u903b\u8f91\uff09
            if (_isPaused) EndPause();

            // IME 合成中（拼音未提交）跳过染色，避免字母被判成"错字"
            if (_ime.IsComposing) return;

            var input = TbxInput.Text ?? string.Empty;

            // 双保险：如果输入长度等于上次染色长度且未回退，说明只是 IME 切换/光标移动，跳过
            if (input.Length == _session.LastInputLen && _session.Started) return;

            // 记录回改范围（输入变短）→ 主染色后再触发黄色闪烁，避免被主循环清回默认色
            int hgFrom = -1, hgTo = -1;
            if (_session.Started && input.Length < _session.LastInputLen)
            {
                hgFrom = input.Length;
                hgTo   = _session.LastInputLen;
            }

            // 第一次有字符 -> 启动计时
            if (!_session.Started && input.Length > 0)
            {
                _session.Started = true;
                _session.StartTime = DateTime.Now;
                _timerTime.Start();
                _timerStats.Start();
            }

            // 刷新"最后一次输入时间"（长时间未跟打自动重打用）
            _lastInputAt = DateTime.Now;

            int len = Math.Min(input.Length, _charRuns.Count);

            // 调试：把 TextChanged 时的真实输入打到 VS 输出窗口
            System.Diagnostics.Debug.WriteLine(
                $"[TextChanged] InputLen={input.Length} Composing={_ime.IsComposing} Text=[{input}]");

            // 通用 IME 漏字符防护：
            // 拼音输入法（搜狗/QQ/微软）选字时常把空格 / 字母 / 数字漏进 TextBox。
            // 当文章原文是中文（CJK）但输入是 ASCII 控制字符时，截断到该位置（视为未输入）。
            int realLen = len;
            for (int i = 0; i < len; i++)
            {
                char src = _session.TypeText[i];
                char inp = input[i];
                bool srcIsCjk = src > 127;                    // 原文中文
                bool inpIsAscii = inp < 128;                  // 输入是 ASCII
                bool inpIsImeJunk = inp == ' ' || inp == '\u3000'
                                    || (inp >= 'a' && inp <= 'z')
                                    || (inp >= 'A' && inp <= 'Z')
                                    || (inp >= '0' && inp <= '9');
                if (srcIsCjk && inpIsAscii && inpIsImeJunk)
                {
                    realLen = i;
                    break;
                }
            }
            len = realLen;

            int cz = 0;
            // 差异染色：每次都对已输入区域整段重写背景，避免 RichTextBox 内部布局变化时
            // 缓存状态匹配但 Brush 已丢失导致灰底消失（偶发渲染异常）。性能开销极小（属性赋值）。
            for (int i = 0; i < len; i++)
            {
                byte newSt = input[i] == _session.TypeText[i] ? (byte)1 : (byte)2;
                if (newSt == 2) cz++;
                if (i >= _runStatus.Length) continue;
                var run = _charRuns[i];
                if (newSt == 1) { run.Foreground = _brushRight; run.Background = _brushRightBg; }
                else            { run.Foreground = _brushWrong; run.Background = _brushWrongBg; }
                _runStatus[i] = newSt;
            }
            for (int i = len; i < _charRuns.Count; i++)
            {
                if (i < _runStatus.Length && _runStatus[i] == 0) continue;
                _charRuns[i].Foreground = _brushDefault;
                _charRuns[i].Background = null;
                if (i < _runStatus.Length) _runStatus[i] = 0;
            }
            _session.Cz = cz;
            TxtCz.Text = cz.ToString();
            TxtWordCount.Text = $"{len}/{_session.TypeText.Length}字";

            // 回改地点高亮（用户反馈干扰，已禁用；保留 TriggerHgFlash/HgFlashTimer 代码以备将来切回）
            // if (hgFrom >= 0) TriggerHgFlash(hgFrom, hgTo);

            // 段内事件 + 慢/回改位置记录（仅记录，不在跟打中染色，等 FinishTyping 时统一染）
            if (_session.Started && len != _session.LastInputLen)
            {
                int prevLen = _session.LastInputLen;
                // 回改：输入变短的范围 [len, prevLen) 标记为回改位置（同位置可被多次记录但 HashSet 去重）
                if (len < prevLen)
                {
                    for (int i = len; i < prevLen && i < _charRuns.Count; i++)
                        _hgMarks.Add(i);
                }
                _session.AppendEvent(len);
                // 慢字：刚追加的事件如果是正向输入且单字耗时 > 阈值 → 记录到 _slowMarks（不染色）
                if (len > prevLen && _session.Report.Count > 0)
                {
                    var ev = _session.Report[_session.Report.Count - 1];
                    if (ev.Length > 0 && ev.TotalTime > 0)
                    {
                        double perChar = ev.TotalTime / ev.Length;
                        if (perChar >= SlowCharThresholdSec)
                        {
                            int end = Math.Min(len, _charRuns.Count);
                            for (int i = prevLen; i < end; i++) _slowMarks.Add(i);
                        }
                    }
                }
            }

            // 编码提示 + 进度条刷新
            UpdateProgress();
            RefreshBmTips();

            // 自动滚屏：让当前光标位置的字保持在对照区可见区域内
            ScrollCompareToCursor(len);

            // 节奏热力条实时跟随（仅当开启）
            if (MapPanel.Visibility == Visibility.Visible) RedrawMap();

            // 字数打满才考虑结束
            if (len >= _session.TypeText.Length)
            {
                // 检查最后一段输入是否有错字（取上次输入位置到当前位置）
                bool tailWrong = false;
                int from = Math.Max(0, _session.LastInputLen);
                int to   = Math.Min(_session.TypeText.Length, len);
                for (int i = from; i < to; i++)
                {
                    if (i >= input.Length || input[i] != _session.TypeText[i])
                    {
                        tailWrong = true;
                        break;
                    }
                }

                if (!tailWrong)
                {
                    _session.Finished = true;
                    FinishTyping();
                }
                // 末字有错 → 不结束，允许用户回改纠正
                // 用户也可以选择不回改，直接载入新文（载入时 _session.Reset()）
            }
        }

        // ===== 计时器 =====

        private void TimerTime_Tick(object sender, EventArgs e)
        {
            var span = DateTime.Now - _session.StartTime;
            // 超过 60 分钟后显示 HH:MM:SS，否则 MM:SS.ff
            if (span.TotalHours >= 1)
                TxtTime.Text = $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}";
            else
                TxtTime.Text = $"{span.Minutes:D2}:{span.Seconds:D2}.{span.Milliseconds / 10:D2}";
        }

        private void TimerStats_Tick(object sender, EventArgs e)
        {
            UpdateStatsDisplay();
            TxtHg.Text = _session.Hg.ToString();
            TxtCz.Text = _session.Cz.ToString();
            TxtKeysCount.Text = _session.Keys.ToString();
            TxtRightLast.Text = _session.LeftHand + ":" + _session.RightHand;
            RefreshExtraStatus();

            // 速度曲线采样（仅当窗口打开 + 已开始）
            // 内嵌曲线采样（切换开启且已开始）
            if (TogChart.IsChecked == true && _chartSpeed != null && _session.Started)
            {
                int len = TbxInput.Text?.Length ?? 0;
                var (speed, _, _, _, sec) = _session.ComputeStats(len);
                if (sec > 0) InlineChartAddPoint(sec, speed);
            }

            // 跟打地图采样
            MapAddSample();
        }

        private void UpdateStatsDisplay()
        {
            int len = TbxInput.Text?.Length ?? 0;
            var (speed, _, jj, mc, _) = _session.ComputeStats(len);
            TxtSpeed.Text = speed.ToString("0.00");
            TxtJj.Text    = jj.ToString("0.00");
            TxtMc.Text    = mc.ToString("0.00");
        }

        // ===== 完成 =====

        private void FinishTyping()
        {
            _timerTime.Stop();
            _timerStats.Stop();
            TbxInput.IsReadOnly = true;   // 完成后输入框只读，防止走跟打路径

            // 完成时按"全文长度"算，不再被 IME junk 干扰
            int total = _session.TypeText.Length;
            _session.LastInputLen = total;

            // 出成绩时统一染色：回改位置浅黄，慢字位置浅绿（错字仍是红色，由 TextChanged 持续维护）
            ApplyResultMarks();

            UpdateStatsDisplay();
            UpdateProgress();
            RefreshBmTips();

            var (speed, speed2, jj, mc, sec) = _session.ComputeStats(total);

            // 速度门槛：底部"限制"按钮启用 + 设置中阈值 > 0 + 当前速度低于阈值 → 不入历史
            bool blockedByLimit = false;
            if (TogLimit != null && TogLimit.IsChecked == true)
            {
                double limit = SettingsService.Instance.SpeedLimit ?? 0;
                if (limit > 0 && speed < limit) blockedByLimit = true;
            }

            if (!blockedByLimit)
            {
                _historyIndex++;
                var row = new HistoryRow
            {
                Index   = _historyIndex,
                When    = DateTime.Now,
                Time    = DateTime.Now.ToString("HH:mm:ss"),
                Title   = _session.Title,
                Seg     = _currentSegNo > 0 ? _currentSegNo.ToString() : "1",
                Speed   = Math.Round(speed, 2),
                Speed2  = Math.Round(speed2, 2),
                Jj      = Math.Round(jj, 2),
                Mc      = Math.Round(mc, 2),
                Hg      = _session.Hg,
                Cz      = _session.Cz,
                Js      = _session.Keys,
                Words   = total,
                DaCi    = _session.Words,
                UseTime = Math.Round(sec, 2),
                Reselect= _session.Reselect,
                Enter   = _session.Enter,
                LeftHand = _session.LeftHand,
                RightHand= _session.RightHand,
            };
            History.Insert(0, row);
            HistoryRepository.Insert(row);
            RefreshSummaryCache();
            }   // end if (!blockedByLimit)

            if (blockedByLimit)
            {
                HandyControl.Controls.Growl.Warning($"速度 {speed:0.00} 低于阈值，未入历史（菜单 → 外观 → 个签 Tab 改阈值）");
            }
            else
            {
                HandyControl.Controls.Growl.Success(new HandyControl.Data.GrowlInfo
                {
                    Message =
                        $"完成！速度 {speed:0.00}（错一罚五 {speed2:0.00}）| 击键 {jj:0.00} | 码长 {mc:0.00} | 用时 {sec:0.00}s\n" +
                        $"错字 {_session.Cz} | 回改 {_session.Hg} | 键数 {_session.Keys} | 打词 {_session.Words} | 选重 {_session.Reselect} | 拼回 {_session.Enter} | 左:右 {_session.LeftHand}:{_session.RightHand}",
                    WaitTime = 2,   // 默认 5 秒太长，缩到 2 秒
                    ShowDateTime = false,
                });

                // 图片成绩：完成自动截 ReportWindow 复制到剪贴板
                if (TogImage != null && TogImage.IsChecked == true)
                    AutoCopyResultImage();
            }

            InlineChartMarkFinish();
        }

        /// <summary>完成时若"图片"开启，弹一个隐藏的 ReportWindow 截图复制到剪贴板。</summary>
        private void AutoCopyResultImage()
        {
            try
            {
                var rw = new Views.ReportWindow(_session, this)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000, Top = -10000,   // 屏外预渲染避免闪烁
                    ShowInTaskbar = false,
                };
                rw.Show();
                // 等 WPF 完成布局再截图
                rw.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var visual = (System.Windows.Media.Visual)rw.Content;
                        var bounds = System.Windows.Media.VisualTreeHelper.GetDescendantBounds(visual);
                        int w = (int)Math.Ceiling(bounds.Width);
                        int h = (int)Math.Ceiling(bounds.Height);
                        if (w > 0 && h > 0)
                        {
                            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96,
                                System.Windows.Media.PixelFormats.Pbgra32);
                            rtb.Render(visual);
                            System.Windows.Clipboard.SetImage(rtb);
                            HandyControl.Controls.Growl.Success("成绩图已自动复制到剪贴板");
                        }
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine("AutoCopyResultImage: " + ex); }
                    finally { rw.Close(); }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("AutoCopyResultImage outer: " + ex); }
        }
    }
}
