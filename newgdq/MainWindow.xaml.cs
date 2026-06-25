using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly TypingSession _session = new TypingSession();
        private readonly List<Run>     _charRuns = new List<Run>();
        /// <summary>每个 Run 的当前染色状态缓存：0=默认 1=正确 2=错误。
        /// TextChanged 中只对状态变化的 Run 真正赋 Foreground/Background，省 95%+ 重绘。</summary>
        private byte[] _runStatus = new byte[0];
        private int _historyIndex;

        // 服务 / 计时器
        private readonly KeyHook _keyHook = new KeyHook();
        private readonly DispatcherTimer _timerTime  = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly DispatcherTimer _timerStats = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };

        // 编码提示
        private readonly DictionaryService _dict = new DictionaryService();

        // 发文
        private readonly SendingService _sending = new SendingService();
        // 自动续发：打完一段后延迟一小段再发下一段，给用户看一眼成绩；可被任何手动操作取消
        private readonly System.Windows.Threading.DispatcherTimer _autoAdvanceTimer =
            new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(700) };

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

        // 成绩区视图模式：本次（默认）= 只显示本进程启动后完成的段；全部 = 显示历史 + 本次
        // 锚点 _sessionStartAt 进程启动取一次，跨日/换文/F3 都不重置
        private readonly DateTime _sessionStartAt = DateTime.Now;
        private bool _showCurrentOnly = true;
        // "本次/全部"切换列的原生列头引用（Loaded 时捕获），用于更新文字
        private System.Windows.Controls.Primitives.DataGridColumnHeader _scoreFilterHeader;
        // 双拼键位练习面板是否激活：激活期间屏蔽全局键钩/自动重打/自动进段
        private bool _practiceMode;

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
        private static readonly Brush HgBrush   = new SolidColorBrush(Color.FromRgb(0xF5, 0xB7, 0xD8));  // 粉红 = 回改
        private const double SlowCharThresholdSec = 1.2;
        // 慢字本采集阈值：单字耗时上限（超过视作停顿/离开噪声，丢弃）+ 高码长阈值（键/字）
        private const double MaxSlowCharSec = 6.0;
        private const double HighKeyPerChar = 4.0;
        private const int SessionSlowTopN = 10;   // 结算摘要 / 生成练习取本场弱项 Top N
        private readonly HashSet<int> _slowMarks = new HashSet<int>();
        private readonly HashSet<int> _hgMarks   = new HashSet<int>();
        // 本场采集到的慢字/弱项明细（每次结算前清空再填）：供结算摘要 UI 聚合"本场最卡 N 字"
        private List<Services.SlowEntry> _lastSessionSlowEntries = new();

        // 当前段号：发文模式下记录"刚发出的段号"，结算时写入历史。非发文置 0。
        private int _currentSegNo;

        /// <summary>把对照区滚到当前光标位置前一行（预读），保证下一行始终可见。</summary>
        private void ScrollCompareToCursor(int len)
        {
            if (_charRuns.Count == 0) return;
            if (_imeComposing) return;   // 拼音合成期不滚动，避免合成窗口内触发布局变更；上屏后下一次 TextChanged 会补滚
            int idx = Math.Min(Math.Max(len, 0), _charRuns.Count - 1);
            // 延迟到 Background 优先级 → 等 WPF 完成本轮 Measure/Arrange 再取 rect，
            // 否则 IME 一次性上屏多字时 GetCharacterRect 会返回 IsEmpty，导致只能 BringIntoView
            // 不能按 30% 锚点定位，表现为"不跟随"。
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (idx >= _charRuns.Count) return;
                    var run  = _charRuns[idx];
                    var rect = run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                    if (rect.IsEmpty)
                    {
                        run.BringIntoView();
                        return;
                    }
                    double targetTopRatio = 0.30;
                    double anchorY = RtbCompare.ActualHeight * targetTopRatio;
                    double delta = rect.Top - anchorY;
                    if (Math.Abs(delta) < 4) return;
                    var sv = GetCompareScrollViewer();
                    if (sv == null) return;
                    double newOffset = sv.VerticalOffset + delta;
                    if (newOffset < 0) newOffset = 0;
                    sv.ScrollToVerticalOffset(newOffset);
                }
                catch { }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // FlowDocumentScrollViewer 不直接暴露滚动 API，取模板内部 ScrollViewer 部件 PART_ContentHost 操作（bug18 改 FDSV 后的等价滚动入口）。
        private ScrollViewer GetCompareScrollViewer()
            => RtbCompare.Template?.FindName("PART_ContentHost", RtbCompare) as ScrollViewer;

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
            // bug19：跟打/比赛态全程禁止粘贴 / 剪切 / 撤销 / 重做 / 拖放（保留复制），防止整段粘贴绕过逐键污染统计。
            // DataObject.Pasting 拦所有粘贴入口（Ctrl+V / Shift+Insert / 右键 / 编程）；命令钩子拦剪切/撤销/重做（及粘贴冗余）；拖放单独拦。
            System.Windows.DataObject.AddPastingHandler(TbxInput, TbxInput_Pasting);
            System.Windows.Input.CommandManager.AddPreviewExecutedHandler(TbxInput, TbxInput_PreviewExecuted);
            TbxInput.PreviewDragOver += TbxInput_PreviewDragOverOrDrop;
            TbxInput.PreviewDrop += TbxInput_PreviewDragOverOrDrop;
            // IME 合成态侦测（方案 A'）：用 WPF TSF 合成事件维护 _imeComposing，
            // 合成中（拼音未上屏）才保护尾部占位串；非合成（英文直打/已上屏）则逐字判错。
            System.Windows.Input.TextCompositionManager.AddPreviewTextInputStartHandler(TbxInput, TbxInput_TextInputStart);
            System.Windows.Input.TextCompositionManager.AddPreviewTextInputHandler(TbxInput, TbxInput_TextInputDone);
            TbxInput.LostFocus += (s, e) => { ResetImeCompose(); PauseType(); };
            _flashTimer.Tick += FlashTimer_Tick;
            _hgFlashTimer.Tick += HgFlashTimer_Tick;
            _autoRepeatTimer.Tick += AutoRepeatTimer_Tick;
            _autoRepeatTimer.Start();
            _autoAdvanceTimer.Tick += AutoAdvanceTimer_Tick;
            DgvHistory.ItemsSource = History;
            // 列宽可拖拽：让汇总行（FootGrid）跟随 DataGrid 各列实际宽度同步
            DgvHistory.LayoutUpdated += (s, e) => SyncFooterColumns();

            _timerTime.Tick  += TimerTime_Tick;
            _timerStats.Tick += TimerStats_Tick;

            // 界面缩放由全局 UiScaleManager 统一处理（Ctrl+滚轮 / Ctrl+加减号 / 菜单）
            UiScaleManager.SetManageSize(this, false); // 主窗口自己持久化几何，不让管理器二次缩放尺寸
            UiScaleManager.ScaleChanged += _ => UpdateScaleMenuChecks();

            _keyHook.KeyDown += KeyHook_KeyDown;
            try { _keyHook.Start(); } catch { /* 钩子安装失败，不影响 UI */ }

            // 从 %AppData%\newgdq\settings.json 恢复设置（窗口几何 + 标记栏开关）
            SettingsService.Load();
            // 按优先级加载词典：exe 同目录 bm.txt > 设置 BmFilePath > 内置嵌入资源
            try { _dict.LoadAuto(SettingsService.Instance.BmFilePath); } catch { /* 词典加载失败不致命 */ }
            _showCurrentOnly = SettingsService.Instance.ShowCurrentOnly ?? true;
            UpdateScoreFilterLabel();
            // 成绩区视图过滤：本次模式 = 只显示 When >= _sessionStartAt 的行
            var view = CollectionViewSource.GetDefaultView(History);
            view.Filter = o => !_showCurrentOnly || (o is HistoryRow r && r.When >= _sessionStartAt);
            // SQLite 历史持久化初始化 + 装载最近 200 条
            HistoryRepository.Init();
            ErrorBookRepository.Init();
            SlowCharRepository.Init();
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
                // 关窗后这些定时器若仍在跑，其 Tick 回调会访问已释放的窗口资源（TbxInput/_session 等）导致崩溃
                _autoAdvanceTimer.Stop();
                _hgFlashTimer.Stop();
                _autoRepeatTimer.Stop();
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

            // 同步界面缩放菜单勾选（缩放本身由 UiScaleManager 在窗口加载时已应用）
            UpdateScaleMenuChecks();
        }

        // 左侧导航：点击带下拉的导航行，在按钮下方弹出其 ContextMenu
        private void NavDropdown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b && b.ContextMenu != null)
            {
                b.ContextMenu.PlacementTarget = b;
                b.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                b.ContextMenu.IsOpen = true;
            }
        }

        // 左侧导航：悬浮滚动条拖动 → 同步滚动内容
        private void NavScrollBar_Scroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
        {
            NavScroll?.ScrollToVerticalOffset(e.NewValue);
        }

        // 左侧导航：收起 / 展开
        private void NavToggle_Click(object sender, RoutedEventArgs e)
        {
            bool collapsed = NavRail.Visibility != Visibility.Visible;
            SetNavCollapsed(!collapsed);
        }

        /// <summary>设置左侧导航折叠状态（true=收起）。比赛态自动收起、退出自动展开都走这里。</summary>
        private void SetNavCollapsed(bool collapsed)
        {
            NavRail.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            NavExpandStrip.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        }

        // ===== 界面缩放菜单（实际缩放逻辑在 Services.UiScaleManager） =====

        private void UpdateScaleMenuChecks()
        {
            if (MnuUiScale == null) return;
            double cur = UiScaleManager.Scale;
            foreach (var obj in MnuUiScale.Items)
            {
                if (obj is MenuItem mi && mi.IsCheckable
                    && mi.Tag is string tag && double.TryParse(tag,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double v))
                {
                    mi.IsChecked = Math.Abs(v - cur) < 0.001;
                }
            }
        }

        private void MenuItem_ScaleUp_Click(object sender, RoutedEventArgs e) => UiScaleManager.StepUp();

        private void MenuItem_ScaleDown_Click(object sender, RoutedEventArgs e) => UiScaleManager.StepDown();

        private void MenuItem_ScaleSet_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string tag && double.TryParse(tag,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double v))
                UiScaleManager.SetScale(v);
        }

        /// <summary>把 SettingsService.Instance 的字体/颜色/个签应用到 UI。
        /// 设置窗每次"应用"后由设置窗调用一次。</summary>
        public void ApplyAppearance()
        {
            var s = SettingsService.Instance;

            // 字体
            if (!string.IsNullOrEmpty(s.CompareFontFamily))
                RtbCompare.Document.FontFamily = new FontFamily(s.CompareFontFamily);
            if (s.CompareFontSize is double cfs && cfs >= 8 && cfs <= 96)
                RtbCompare.Document.FontSize = cfs;
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
                if (newgdq.Services.ClipboardHelper.TrySetText(WECHAT_ID))
                    Services.Toast.Success("已复制微信号：" + WECHAT_ID);
                else
                    Services.Toast.Warning("剪贴板被其他程序占用，请稍后再试");
            }
            catch (Exception ex) { Services.Toast.Error(ex.Message); }
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
                if (Services.TextProcessor.NormalizeForCompare(input[i]) == Services.TextProcessor.NormalizeForCompare(_session.TypeText[i]))
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
                    if (Services.TextProcessor.NormalizeForCompare(input[i]) == Services.TextProcessor.NormalizeForCompare(_session.TypeText[i]))
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
            ChartCol.Width          = on ? new GridLength(180) : new GridLength(0);
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

        private void MapReset()
        {
            _charMs = null;
            RedrawMap();
        }

        // 长时间未跟打自动重打：每秒检查一次，若跟打中且距上次输入超过阈值分钟 → 触发 F3
        private void AutoRepeatTimer_Tick(object sender, EventArgs e)
        {
            if (_practiceMode) return;
            int? th = SettingsService.Instance.AutoRepeatMinutes;
            if (!th.HasValue || th.Value <= 0) return;
            if (!_session.Started || _session.Finished) return;
            if (_isPaused) return;
            if (_lastInputAt == default) return;

            if ((DateTime.Now - _lastInputAt).TotalMinutes >= th.Value)
            {
                Repeat();
                Services.Toast.Info($"已超过 {th.Value} 分钟无输入，自动重打");
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

            // 从 Report 事件汇总每个字的耗时
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

            // 找最慢的几个字（前 3 名，用于上方标记）
            var slowTop = new List<(int idx, double ms)>();
            for (int i = 0; i < total; i++)
                if (_charMs[i] > 800) slowTop.Add((i, _charMs[i]));
            slowTop.Sort((a, b) => b.ms.CompareTo(a.ms));
            if (slowTop.Count > 3) slowTop.RemoveRange(3, slowTop.Count - 3);
            int maxIdx = slowTop.Count > 0 ? slowTop[0].idx : -1;
            double maxMs = slowTop.Count > 0 ? slowTop[0].ms : 0;

            double cellW = _mapW / total;
            double drawCellW = Math.Max(cellW, 1.5);

            // 三层高度按容器实际高度(_mapH)自适应分配，不写死 60：上=刻度、中=慢字标记堆叠、下=色块。
            // 字号固定(10)，故上层刻度/下层色块取固定高度保证可读，中层取余下空间随容器伸缩；
            // 标签行数多时由下方 needH 撑高容器，自适应同样生效（40px 下三层不重叠）。
            const double TopH = 12, BotH = 8;
            double topY = 0, midY = TopH, botY = _mapH - BotH;
            double MidH = botY - midY;

            // ===== 第 1 层：顶部刻度（0%/25%/50%/75%/100%）=====
            var tickColor = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
            for (int pct = 0; pct <= 100; pct += 25)
            {
                double x = _mapW * pct / 100.0;
                MapCanvas.Children.Add(new System.Windows.Shapes.Line
                {
                    X1 = x, Y1 = topY, X2 = x, Y2 = topY + TopH,
                    Stroke = tickColor, StrokeThickness = 1,
                });
                var lbl = new TextBlock
                {
                    Text = pct + "%",
                    Foreground = Brushes.White,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Colors.Black, BlurRadius = 3, ShadowDepth = 0, Opacity = 1.0,
                    },
                };
                System.Windows.Controls.Canvas.SetLeft(lbl, x + 2);
                System.Windows.Controls.Canvas.SetTop(lbl, topY);
                if (pct == 100)
                {
                    lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    System.Windows.Controls.Canvas.SetLeft(lbl, x - lbl.DesiredSize.Width - 2);
                }
                MapCanvas.Children.Add(lbl);
            }

            // ===== 第 2 层：慢字标记（红色倒三角 + ms 数字，纵向堆叠避免重叠）=====
            // 先把所有标签 measure 一次，再贪心分层（多个标签共用 y 时按 X 排序左→右占位）
            var labelInfos = new List<(System.Windows.Shapes.Polygon tri, TextBlock lbl, double labelX, double width)>();
            slowTop.Sort((a, b) => a.idx.CompareTo(b.idx));
            foreach (var item in slowTop)
            {
                int idx = item.idx; double ms = item.ms;
                double x = (idx + 0.5) * cellW;
                // 三角紧贴色块上方（指向具体字）
                double triTop = midY + MidH - 6;
                var tri = new System.Windows.Shapes.Polygon
                {
                    Points = new System.Windows.Media.PointCollection
                    {
                        new System.Windows.Point(x - 4, triTop),
                        new System.Windows.Point(x + 4, triTop),
                        new System.Windows.Point(x,     triTop + 6),
                    },
                    Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55)),
                };
                var lbl = new TextBlock
                {
                    Text = ((int)ms) + "ms",
                    Foreground = Brushes.White,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Colors.Black, BlurRadius = 3, ShadowDepth = 0, Opacity = 1.0,
                    },
                };
                lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double w = lbl.DesiredSize.Width;
                double labelX = x - w / 2;
                if (labelX < 0) labelX = 0;
                if (labelX + w > _mapW) labelX = _mapW - w;
                labelInfos.Add((tri, lbl, labelX, w));
            }

            // 贪心多行布局：每层一个 list 记录已占区间，新标签找第一个不冲突的层
            var rows = new List<List<(double left, double right)>>();
            const double LineH = 12;            // 每层高度
            const double PadX = 3;              // 横向间距
            foreach (var info in labelInfos)
            {
                MapCanvas.Children.Add(info.tri);
                int row = 0;
                while (true)
                {
                    if (row >= rows.Count) rows.Add(new List<(double, double)>());
                    bool conflict = false;
                    foreach (var (l, r) in rows[row])
                        if (!(info.labelX + info.width + PadX < l || info.labelX > r + PadX)) { conflict = true; break; }
                    if (!conflict) { rows[row].Add((info.labelX, info.labelX + info.width)); break; }
                    row++;
                }
                // 标签从中部最上排起，往下堆叠
                double y = midY + row * LineH;
                System.Windows.Controls.Canvas.SetLeft(info.lbl, info.labelX);
                System.Windows.Controls.Canvas.SetTop(info.lbl, y);
                MapCanvas.Children.Add(info.lbl);
            }

            // 如果标签层数超过 1，动态加高容器（让标签不被裁）：刻度 + N 行标签 + 三角(6) + 色块 + 边距
            int neededRows = rows.Count;
            double needH = TopH + neededRows * LineH + 6 + BotH + 2;
            if (MapPanel.Height < needH) MapPanel.Height = needH;

            // ===== 第 3 层：色块条 =====
            for (int i = 0; i < total; i++)
            {
                var fill = ColorForMs(_charMs[i]);
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = drawCellW,
                    Height = BotH,
                    Fill = fill,
                };
                // 悬停提示：显示该格对应的原文字符 + 耗时（中文/英文/标点皆原样显示）
                rect.ToolTip = BuildMapTooltip(i, _charMs[i]);
                System.Windows.Controls.Canvas.SetLeft(rect, i * cellW);
                System.Windows.Controls.Canvas.SetTop(rect, botY);
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
        }

        /// <summary>节奏色块悬停提示：第 idx 个字符（原文）+ 耗时。</summary>
        private string BuildMapTooltip(int idx, double ms)
        {
            string ch;
            if (idx >= 0 && idx < _session.TypeText.Length)
            {
                char c = _session.TypeText[idx];
                switch (c)
                {
                    case ' ':  ch = "空格"; break;
                    case '\u3000': ch = "全角空格"; break;
                    case '\t': ch = "Tab"; break;
                    case '\r':
                    case '\n': ch = "换行"; break;
                    default:   ch = c.ToString(); break;
                }
            }
            else ch = "?";

            return ms > 0
                ? $"第{idx + 1}字：{ch}    {(int)ms}ms"
                : $"第{idx + 1}字：{ch}    （未打）";
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
                System.Windows.MessageBox.Show(msg, "bm.txt 校验结果");
            }
            catch (Exception ex)
            {
                sw.Stop();
                System.Windows.MessageBox.Show(
                    $"✗ 加载失败\n\n{ex.Message}\n\n请检查文件是否为 UTF-8 编码、格式是否正确（每行 \"编码 字1 字2 ...\"）。",
                    "bm.txt 校验失败");
            }
        }

        /// <summary>选一个 bm.txt 作为编码提示词表：先临时实例校验，成功才替换当前词典并持久化路径。</summary>
        private void MenuItem_UseBmFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 bm.txt 文件作为编码提示词表",
                Filter = "文本词典|*.txt|所有文件|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var tmp = new Services.DictionaryService();
                tmp.LoadFromFile(dlg.FileName);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"✗ 加载失败，当前词典未改变\n\n{ex.Message}\n\n请检查文件是否为 UTF-8 编码、格式是否正确（每行 \"编码 字1 字2 ...\"）。",
                    "使用自定义 bm.txt 失败");
                return;
            }

            _dict.LoadFromFile(dlg.FileName);
            SettingsService.Instance.BmFilePath = dlg.FileName;
            SettingsService.Save();
            ReloadBmRefresh();
            System.Windows.MessageBox.Show(
                $"✓ 已切换到自定义词表\n\n路径：{dlg.FileName}\n\n" +
                $"总条目：{_dict.TotalEntries}\n独立单字：{_dict.SingleCount}\n词组条目：{_dict.PhraseCount}",
                "已使用自定义 bm.txt");
        }

        /// <summary>清除自定义码表路径，切回内置词表。</summary>
        private void MenuItem_ResetBmFile_Click(object sender, RoutedEventArgs e)
        {
            SettingsService.Instance.BmFilePath = null;
            SettingsService.Save();
            _dict.LoadFromResource();
            ReloadBmRefresh();
            System.Windows.MessageBox.Show(
                "✓ 已恢复内置词表。\n\n注意：若 exe 同目录存在 bm.txt，下次启动仍会优先使用同目录的文件。",
                "已恢复内置词表");
        }

        /// <summary>词典重载后刷新编码提示与词组下划线（若功能开启）。</summary>
        private void ReloadBmRefresh()
        {
            RefreshBmTips();
            ClearPhraseUnderlines();
            ApplyPhraseUnderlines();
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
                if (TxtFootTime == null) return;

                // 本次模式：footer 数字按"本进程启动后的段"现算（不查 DB）
                if (_showCurrentOnly)
                {
                    var rows = History.Where(r => r.When >= _sessionStartAt).ToList();
                    int segs = rows.Count;
                    double totSec = rows.Sum(r => r.UseTime);
                    int    totWds = rows.Sum(r => r.Words);
                    double avgSp  = segs > 0 ? rows.Average(r => r.Speed) : 0;
                    double avgJj  = segs > 0 ? rows.Average(r => r.Jj)    : 0;
                    double avgMc  = segs > 0 ? rows.Average(r => r.Mc)    : 0;
                    var ts = TimeSpan.FromSeconds(totSec);
                    TxtFootLabel.Text = "本次";
                    TxtFootTime.Text  = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
                    TxtFootSegs.Text  = segs + "#";
                    TxtFootSpeed.Text = avgSp.ToString("0.00");
                    TxtFootJj.Text    = avgJj.ToString("0.00");
                    TxtFootMc.Text    = avgMc.ToString("0.00");
                    TxtFootWords.Text = totWds.ToString();
                    var avg = HistoryRepository.LoadAverages();
                    TxtFootAllAvg.Text = "累计 " + avg.totalSpeed.ToString("0.00");
                    TxtFootAllJj.Text  = avg.totalJj.ToString("0.00");
                    ApplyGoalHintToFooter(avgSp);
                }
                else
                {
                    var avg = HistoryRepository.LoadAverages();
                    var ts = TimeSpan.FromSeconds(_summaryCache.todaySec);
                    TxtFootLabel.Text = "今日";
                    TxtFootTime.Text  = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
                    TxtFootSegs.Text  = _summaryCache.todaySegs + "#";
                    TxtFootSpeed.Text = avg.todaySpeed.ToString("0.00");
                    TxtFootJj.Text    = avg.todayJj.ToString("0.00");
                    TxtFootMc.Text    = avg.todayMc.ToString("0.00");
                    TxtFootWords.Text = _summaryCache.todayWords.ToString();
                    TxtFootAllAvg.Text = "累计 " + avg.totalSpeed.ToString("0.00");
                    TxtFootAllJj.Text  = avg.totalJj.ToString("0.00");
                    ApplyGoalHintToFooter(avg.todaySpeed);
                }
            }
            catch { }
        }

        /// <summary>
        /// 把目标速度的达成情况体现在 footer 均速上：达标变绿，未达标只在 tooltip 里
        /// 显示"已完成 X%"（鼓励式，不做红叉责备）。未设目标则恢复默认样式。
        /// </summary>
        private void ApplyGoalHintToFooter(double currentSpeed)
        {
            if (TxtFootSpeed == null) return;
            double goal = SettingsService.Instance.GoalSpeed ?? 0;
            if (goal <= 0)
            {
                TxtFootSpeed.SetResourceReference(TextBlock.ForegroundProperty, "ValueFG");
                TxtFootSpeed.ToolTip = null;
                return;
            }
            if (currentSpeed >= goal)
            {
                TxtFootSpeed.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A));
                TxtFootSpeed.ToolTip = $"已达到目标 {goal:0} 字/分 🎉";
            }
            else
            {
                TxtFootSpeed.SetResourceReference(TextBlock.ForegroundProperty, "ValueFG");
                double pct = currentSpeed / goal * 100;
                TxtFootSpeed.ToolTip = $"目标 {goal:0} 字/分 · 已完成 {pct:0}%（还差 {goal - currentSpeed:0.0}）";
            }
        }

        /// <summary>切换"本次/全部"视图模式</summary>
        private void ToggleScoreFilter()
        {
            _showCurrentOnly = !_showCurrentOnly;
            SettingsService.Instance.ShowCurrentOnly = _showCurrentOnly;
            try { SettingsService.Save(); } catch { }
            CollectionViewSource.GetDefaultView(History)?.Refresh();
            UpdateScoreFilterLabel();
            RefreshSummaryCache();
        }

        private void UpdateScoreFilterLabel()
        {
            if (_scoreFilterHeader == null) return;
            int curCnt = History.Count(r => r.When >= _sessionStartAt);
            _scoreFilterHeader.Content = _showCurrentOnly
                ? $"本次 {curCnt}"
                : $"全部 {History.Count}";
            _scoreFilterHeader.ToolTip = _showCurrentOnly
                ? "当前显示：本进程启动后完成的段（点击切换为 全部）"
                : "当前显示：所有历史段（点击切换为 本次）";
        }

        /// <summary>列头加载时捕获引用并立即刷新文字</summary>
        private void ScoreFilterHeader_Loaded(object sender, RoutedEventArgs e)
        {
            _scoreFilterHeader = sender as System.Windows.Controls.Primitives.DataGridColumnHeader;
            UpdateScoreFilterLabel();
        }

        private void BtnScoreFilter_Click(object sender, RoutedEventArgs e) => ToggleScoreFilter();

        /// <summary>
        /// 让底部汇总行的列宽实时跟随 DataGrid 各列的实际宽度，
        /// 这样用户拖拽任意一列时表头(DataGrid原生列头)、数据、汇总行三者始终对齐。
        /// </summary>
        private void SyncFooterColumns()
        {
            if (FootGrid == null || DgvHistory == null) return;
            var cols = DgvHistory.Columns;
            var defs = FootGrid.ColumnDefinitions;
            if (cols.Count == 0 || defs.Count != cols.Count) return;
            for (int i = 0; i < cols.Count; i++)
            {
                double w = cols[i].ActualWidth;
                if (double.IsNaN(w) || w <= 0) continue;
                // 仅在宽度确有变化时更新，避免 LayoutUpdated 反复触发布局
                if (System.Math.Abs(defs[i].Width.Value - w) > 0.5 || defs[i].Width.GridUnitType != GridUnitType.Pixel)
                    defs[i].Width = new GridLength(w, GridUnitType.Pixel);
            }
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
                Services.Toast.Error("载入失败：" + ex.Message);
            }
        }

        private void LoadArticle(string text, string title)
        {
            // 载入新内容前取消任何挂起的自动续发（F5/重打/跳段/手动发段都经过这里）
            CancelAutoAdvance();
            // 如果上一段已字数打满但因末字错未 finish，载新文时强制以当前成绩入历史。
            // 必须在清除"群比赛文"标记之前结算：否则上一段是比赛文时，IsMatchArticleLoaded
            // 已被清掉，强制结算走 FinishTyping 时不会上传云端，导致末字打错的比赛段静默丢分。
            TryForceFinalizeLastSegment();
            // 载入任何新内容默认解除"群比赛文"标记（F4 抓文会在本方法之后重新置位）。
            // 这样发文/内部文/乱序/转换/复位等普通载文都会自动解锁比赛态快捷键。
            bool wasMatch = Services.CloudMatchService.IsMatchArticleLoaded;
            Services.CloudMatchService.ClearCurrentArticle();
            if (wasMatch) SetNavCollapsed(false);   // 退出比赛态：左侧导航自动展开回来

            // 替换：底部标记栏"替换"开启时，载入时自动英文标点转中文标点
            if (TogReplace != null && TogReplace.IsChecked == true && !string.IsNullOrEmpty(text))
                text = Services.TextProcessor.En2Cn(text);

            _session.Load(text, title);
            App.Diag("LOAD", $"seg=[{title}] chars={_session.TypeText.Length}");

            // 重建对照区
            RtbCompare.Document.Blocks.Clear();
            RtbCompare.Document.PagePadding = new Thickness(0);
            _charRuns.Clear();
            _runStatus = new byte[_session.TypeText.Length];
            _slowMarks.Clear();
            _hgMarks.Clear();
            _lastSessionSlowEntries.Clear();
            HideSessionSlowSummary();

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
            ResetImeCompose();
            _prevRawLen = 0;
            TbxInput.Clear();
            TbxInput.Focus();

            // 新段载入后把对照区滚回顶部 (BringIntoView 在 Loaded 之前可能没生效，用 Dispatcher 延后一帧)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { GetCompareScrollViewer()?.ScrollToHome(); } catch { }
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
            TxtGroup.Text = $"重{_repeatCount} 呆0s";
            TxtAccBar.Text = "100%";
            TxtAccBar.Foreground = AccColor(100);
        }

        /// <summary>刷新信息条"状态"格（重打次数 / 发呆秒数）与"键准"列（百分比 + 三档配色）。
        /// 由 TimerStats_Tick 每 200ms 调一次。</summary>
        private void RefreshExtraStatus()
        {
            if (TxtGroup == null) return;
            double idleSec = (_session.Started && !_session.Finished && !_isPaused && _lastInputAt != default)
                ? (DateTime.Now - _lastInputAt).TotalSeconds : 0;
            int keys = _session.Keys;
            int waste = _session.ComputeWasteKeys();
            double acc = keys > 0 ? (keys - _session.ImeBackspace * 2 - waste) * 100.0 / keys : 100;
            if (acc < 0) acc = 0;
            if (acc > 100) acc = 100;
            TxtGroup.Text = $"重{_repeatCount} 呆{idleSec:0}s";
            TxtAccBar.Text = $"{acc:0}%";
            TxtAccBar.Foreground = AccColor(acc);
        }

        /// <summary>键准三档配色：>=95 绿 / 85~95 黄 / <85 红。优先主题资源，缺失回退硬编码。</summary>
        private Brush AccColor(double acc)
        {
            if (acc >= 95) return TryFindResource("SuccessFG") as Brush ?? new SolidColorBrush(Color.FromRgb(0x4C, 0xC9, 0x6A));
            if (acc >= 85) return TryFindResource("AccentFG")  as Brush ?? new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x4C));
            return                TryFindResource("ErrorFG")   as Brush ?? new SolidColorBrush(Color.FromRgb(0xE7, 0x3E, 0x3E));
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
            FinishTyping(false);
        }

        // ===== 复位 =====清空当前文章、输入区、历史不动
        // ===== 发文 =====
        private void MenuItem_OpenSendText_Click(object sender, RoutedEventArgs e) => OpenSendTextWindowWithConfirm();

        /// <summary>F2 入口：已在发文中则先弹确认。</summary>
        private void OpenSendTextWindowWithConfirm()
        {
            if (_sending.State.Active)
            {
                var r = System.Windows.MessageBox.Show(
                    "已经在发文中（" + (_sending.State.Title ?? "-") + "）。\n是否重新开始一段新的发文？\n\n[ Esc 取消 ]",
                    "发文确认", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Question);
                if (r != System.Windows.MessageBoxResult.OK) return;
                CancelAutoAdvance();
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
                _sending.State.AutoAdvance    = state.AutoAdvance;
                _sending.State.CountPerSeg    = state.CountPerSeg;
                _sending.State.Mark           = state.Mark;
                _sending.State.StartSeg       = state.StartSeg;
                _sending.State.SentSeg        = 0;
                // 来源（SendTextWindow 当前 Tab 名）
                _sending.State.SourceName     = state.SourceName ?? "-";
                // 续打身份（resume-core）：随会话保留，供 RecordResumeProgress 重建身份
                _sending.State.ArticleKind    = state.ArticleKind ?? "";
                _sending.State.ArticleId      = state.ArticleId ?? "";
                _sending.State.TickOut        = state.TickOut;
                _sending.State.InitialMark    = state.InitialMark;
                CancelAutoAdvance();     // 新会话先清掉任何挂起的自动续发
                // 有有效续打记录则弹窗询问；用户选"继续"会跳到第 N 段并 return true，
                // 这里就不再发默认首段（避免续打跳段后又被首段覆盖的双发）。
                if (TryResumeSending())
                {
                    ShowSendStatusWindow();
                    return;
                }
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
                Services.Toast.Info("尚未开启发文，请先菜单 → 发文 → 发文...");
                return;
            }
            var savedRandom = _sending.State.IsRandom;
            _sending.State.IsRandom = true;
            // 如果之前是顺序表刷文章，乱序只对 Single 生效。处理：强制按 Single 模式抽。
            var origType = _sending.State.Type;
            _sending.State.Type = SendingTextType.Single;
            try
            {
                SendNext();
            }
            finally
            {
                _sending.State.Type = origType;
                _sending.State.IsRandom = savedRandom;
            }
        }

        // 供 SendStatusWindow 调用的公共入口
        public Models.SendingState GetSendingState() => _sending.State;
        public void StopSending() { CancelAutoAdvance(); _sending.Stop(); _sendStatusWin?.Refresh(); }
        public void SendNextSegment() => SendNext();

        /// <summary>停止后是否还能"继续发文"：有文本且仍有未发内容（乱序无限池视为始终可继续）。</summary>
        public bool CanResumeSending()
        {
            var s = _sending.State;
            if (s.Active) return false;                       // 仍在发文中，无需继续
            if (string.IsNullOrEmpty(s.FullText)) return false; // 从未开过发文
            // 乱序（含不重复但池非空）始终可继续；顺序 / 一句结束按 Mark 是否到底判断
            if (s.IsRandom) return true;
            return s.Mark < s.FullText.Length;
        }

        /// <summary>继续发文：仅把会话重新激活，不自动发段、不安排自动续发（避免与自动发文冲突）。
        /// 恢复后由用户继续打当前段或手动点"发下一段"。</summary>
        public void ResumeSending()
        {
            if (!CanResumeSending())
            {
                Services.Toast.Info("没有可继续的发文（已发完或尚未开始）");
                return;
            }
            CancelAutoAdvance();          // 双保险：清掉任何残留的挂起续发
            _sending.State.Active = true; // 进度（Mark/SentSeg/PoolText 等）原样保留，天然接续
            _sendStatusWin?.Refresh();
        }
        /// <summary>发文状态窗里切换"打完自动发下一段"。关闭时取消任何挂起的续发。</summary>
        public void SetAutoAdvance(bool on)
        {
            _sending.State.AutoAdvance = on;
            if (!on) CancelAutoAdvance();
        }
        /// <summary>Ctrl+← / Ctrl+→ 相对跳段：仅顺序 / 一句结束模式支持。</summary>
        private void JumpSegRelative(int delta)
        {
            var s = _sending.State;
            if (!s.Active) return;

            // 模式守卫：仅 文章 + 顺序（含一句结束）支持段号跳转
            if (Services.CloudMatchService.IsMatchArticleLoaded)
            {
                Services.Toast.Info("群比赛文不支持跳段");
                return;
            }
            if (s.Type != SendingTextType.Article)
            {
                Services.Toast.Info("当前模式不支持段号跳转");
                return;
            }
            if (s.IsRandom)
            {
                Services.Toast.Info("当前模式不支持跳段（乱序）");
                return;
            }
            if (s.InitialMark != 0)
            {
                Services.Toast.Info("自定义起始位置下不支持跳段");
                return;
            }

            int total = _sending.EnumerateSegments().Count;   // 总段数（仅顺序/一句结束可枚举）
            if (total <= 0)
            {
                Services.Toast.Info("当前模式不支持跳段");
                return;
            }

            int first = s.StartSeg;
            int last  = s.StartSeg + total - 1;
            int cur = s.CurSeg - 1;            // 当前已载入段号（StartSeg + SentSeg - 1）
            int target = cur + delta;
            if (target < first)
            {
                Services.Toast.Info("已经是第一段");
                return;
            }
            if (target > last)
            {
                Services.Toast.Info("已经是最后一段");
                return;
            }

            string seg = _sending.JumpToSeg(target);
            if (seg == null)
            {
                Services.Toast.Info("当前模式不支持跳段");
                return;
            }
            // 保成绩：LoadArticle 内 TryForceFinalizeLastSegment 会先结算上一段
            LoadArticle(seg, $"{s.Title} · 第 {target} 段");
            _currentSegNo = target;
            _sendStatusWin?.Refresh();
            RecordResumeProgress(target);   // 复用 resume-core 单一入口（内部自带范围守卫）
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
                Services.Toast.Info("尚未开启发文，请先菜单 → 发文 → 发文...");
                return;
            }
            string seg = _sending.NextSegment();
            if (seg == null)
            {
                Services.Toast.Success("全部发送完毕");
                _sending.Stop();
                ClearResumeProgress();   // 全部发完：清除续打记录，避免"继续到不存在的段"
                return;
            }
            int curSeg = _sending.State.CurSeg - 1; // SentSeg 已 ++，当前段号 = StartSeg + (SentSeg - 1)
            _currentSegNo = curSeg;
            string title = $"{_sending.State.Title} · 第 {curSeg} 段";
            LoadArticle(seg, title);
            _sendStatusWin?.Refresh();
            RecordResumeProgress(curSeg);   // 进段即记录（手动发段 / 自动续发都经此处）
        }

        // ===== 自定义文章续打进度（resume-core）=====
        /// <summary>记录续打进度的单一入口（resume-jump-help 跳段后也调本方法复用）。
        /// 仅当当前会话在可续打范围（文章 + 顺序，非群比赛文）才写盘；否则忽略。</summary>
        internal void RecordResumeProgress(int segNo)
        {
            var s = _sending.State;
            if (s.Type != SendingTextType.Article || s.IsRandom) return;     // 乱序/词组/单字不写
            if (s.ArticleKind != "CustomFile") return;                       // 续打仅限自定义文章；自带/剪切板不记录
            if (s.InitialMark != 0) return;                                  // 仅起始位置=0 才能经 JumpToSeg(从0重切)精确复现，自定义起始位置不记
            if (Services.CloudMatchService.IsMatchArticleLoaded) return;     // 群比赛云文不写
            var rec = Services.ResumeProgressService.BuildIdentity(s);
            rec.ResumeSegNo = segNo;
            rec.UpdatedAt   = System.DateTime.Now.ToString("o");
            Services.SettingsService.Instance.SendResumeProgress = rec;
            try { Services.SettingsService.Save(); } catch { }
        }

        /// <summary>清除续打进度（当前这篇全部发完时调用）。仅清除属于当前会话这篇的记录，避免发送其它来源/文章时误删自定义文章续打记忆。</summary>
        private void ClearResumeProgress()
        {
            var rec = Services.SettingsService.Instance.SendResumeProgress;
            if (rec == null) return;
            var cur = Services.ResumeProgressService.BuildIdentity(_sending.State);
            if (!Services.ResumeProgressService.SameIdentity(rec, cur)) return;
            Services.SettingsService.Instance.SendResumeProgress = null;
            try { Services.SettingsService.Save(); } catch { }
        }

        /// <summary>开启发文时尝试续打：有有效记录则弹窗询问。
        /// 返回 true 表示已按"续打第 N 段"载入该段（调用方不要再发默认首段，避免双发）。</summary>
        private bool TryResumeSending()
        {
            var s = _sending.State;
            // 范围守卫：仅 文章 + 顺序（发文流程本身即非群比赛文，故不查 IsMatchArticleLoaded —
            // 该标记此刻反映的是上一篇载入态，尚未被本会话首段 LoadArticle 清除）
            if (s.Type != SendingTextType.Article || s.IsRandom) return false;
            if (s.ArticleKind != "CustomFile") return false;                 // 续打仅限自定义文章
            var rec = Services.SettingsService.Instance.SendResumeProgress;
            if (rec == null) return false;
            var cur = Services.ResumeProgressService.BuildIdentity(s);
            // 续打校验要真实总段数，须与写入端(FinishTyping 的 Mark 判定)一致：去掉默认 500 上限，
            // 否则 >500 段时 total 被截断、写入端记录的第 501 段会被 IsResumeValid 判越界而读不出。
            // previewLen:1 省去无用的预览 substring 开销（此处只取 .Count）。续打仅开发文时触发一次，低频。
            int total = _sending.EnumerateSegments(previewLen: 1, maxCount: int.MaxValue).Count;
            if (!Services.ResumeProgressService.IsResumeValid(rec, cur, total)) return false;

            int n = rec.ResumeSegNo;
            var r = System.Windows.MessageBox.Show(
                $"上次停在第 {n} 段，是否继续？",
                "续打", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (r != System.Windows.MessageBoxResult.Yes) return false;   // 选"否" → 走默认首段（记录不删）

            string seg = _sending.JumpToSeg(n);   // 与 EnumerateSegments 同样从 Mark=0 重切，确定性复现第 N 段
            if (seg == null) return false;         // 兜底：取不到则回默认流程
            _currentSegNo = n;
            LoadArticle(seg, $"{s.Title} · 第 {n} 段");
            _sendStatusWin?.Refresh();
            RecordResumeProgress(n);
            return true;
        }

        // ===== 自动续发（打完一段自动发下一段）=====
        /// <summary>由"正常打完"分支调用：若发文会话开启了自动续发，安排延迟发下一段。</summary>
        private void ScheduleAutoAdvance()
        {
            if (!_sending.State.Active || !_sending.State.AutoAdvance) return;
            // 重启计时器，单次触发
            _autoAdvanceTimer.Stop();
            _autoAdvanceTimer.Start();
        }

        /// <summary>取消任何挂起的自动续发（手动发段 / 复位 / 重开发文 / 停发文时调用）。</summary>
        private void CancelAutoAdvance() => _autoAdvanceTimer.Stop();

        private void AutoAdvanceTimer_Tick(object sender, EventArgs e)
        {
            _autoAdvanceTimer.Stop();
            if (_practiceMode) return;
            // 二次校验：计时器排队期间状态可能已变（用户手动跳段/停发文/重开）
            if (!_sending.State.Active || !_sending.State.AutoAdvance) return;
            SendNext();
        }

        private void MnuSmartCi_Click(object sender, RoutedEventArgs e)
        {
            if (MnuSmartCi.IsChecked != true)
            {
                TxtTheoryMc.Text = "-";
                ClearPhraseUnderlines();
                RefreshBmTips();
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
                Services.Toast.Warning("词典未加载");
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
        // 问题反馈群（老群）：用于报 bug、提需求
        private const string QQ_GROUP_URL = "https://qm.qq.com/q/eb2iF433q2";
        // 跟打练习群（新群）：纯跟打练习交流
        private const string QQ_PRACTICE_GROUP_URL = "https://qm.qq.com/q/mti78dCTCg";
        private const string QQ_GROUP_ID  = "17079867";
        private const string WECHAT_ID    = "synhxb";
        private const string PROJECT_URL  = "https://github.com/lwl4613615/zhouzhou-typing";

        private void MenuItem_Hotkeys_Click(object sender, RoutedEventArgs e)
        {
            new Views.HotkeysWindow(this).ShowDialog();
        }

        private void MenuItem_Homepage_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(PROJECT_URL);
        }

        private void MenuItem_JoinQQ_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(QQ_GROUP_URL);
        }

        private void MenuItem_JoinPracticeQQ_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(QQ_PRACTICE_GROUP_URL);
        }

        /// <summary>用系统默认浏览器打开 URL。.NET Core+ 必须 UseShellExecute=true，否则直接传 URL 会报"找不到文件"。</summary>
        private static void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) { Services.Toast.Error(ex.Message); }
        }

        // ===== 文章处理（菜单 → 功能 → 文章处理）=====

        private void MenuItem_ShuffleArticle_Click(object sender, RoutedEventArgs e)
        {
            if (_session.TypeText.Length == 0)
            { Services.Toast.Info("当前无文段"); return; }
            string shuffled = Services.TextProcessor.Shuffle(_session.TypeText);
            LoadArticle(shuffled, _session.Title + "（已乱序）");
        }

        private void MenuItem_En2CnPunct_Click(object sender, RoutedEventArgs e)
        {
            if (_session.TypeText.Length == 0)
            { Services.Toast.Info("当前无文段"); return; }
            string converted = Services.TextProcessor.En2Cn(_session.TypeText);
            LoadArticle(converted, _session.Title);
        }

        private void MenuItem_StripSpace_Click(object sender, RoutedEventArgs e)
        {
            if (_session.TypeText.Length == 0)
            { Services.Toast.Info("当前无文段"); return; }
            string stripped = Services.TextProcessor.TickBlock(_session.TypeText);
            LoadArticle(stripped, _session.Title);
        }

        // ===== 复制成绩 / 退出 / 发文状态 =====

        private void MenuItem_CopyResult_Click(object sender, RoutedEventArgs e)
        {
            if (History.Count == 0)
            { Services.Toast.Info("没有可复制的成绩，先打一段"); return; }
            var r = History[0];
            string s = $"第{r.Seg}段 速度{r.Speed:0.00} 罚五{r.Speed2:0.00} 击键{r.Jj:0.00} 码长{r.Mc:0.00} " +
                       $"回改{r.Hg} 错字{r.Cz} 键数{r.Js} 打词{r.DaCi} 用时{r.UseTime:0.00}s · {r.Title}";
            try
            {
                if (newgdq.Services.ClipboardHelper.TrySetText(s))
                    Services.Toast.Success("最新成绩已复制");
                else
                    Services.Toast.Warning("剪贴板被其他程序占用，请稍后再试");
            }
            catch (Exception ex) { Services.Toast.Error(ex.Message); }
        }

        private void MenuItem_Exit_Click(object sender, RoutedEventArgs e) => this.Close();

        private void MenuItem_OpenAverage_Click(object sender, RoutedEventArgs e)
        {
            ShowAnalysis(new Views.AverageView(), "平均成绩");
        }

        // ===== E：分析页内嵌承载（覆盖主跟打区，与双拼练习面板同模式） =====
        /// <summary>在主内容区内嵌显示一个分析视图，隐藏跟打区/信息条/标记栏。</summary>
        private void ShowAnalysis(System.Windows.UIElement view, string title)
        {
            // 键位练习与分析页同为覆盖层。若正在键位练习，先退出，避免两层叠在一起互相抢焦点。
            if (_practiceMode) ExitPracticeMode();
            PauseType();   // 查看分析期间暂停跟打计时
            AnalysisTitle.Text       = title;
            AnalysisContent.Content  = view;
            MainContentRoot.Visibility = Visibility.Collapsed;
            InfoBar.Visibility         = Visibility.Collapsed;
            MarkerBar.Visibility       = Visibility.Collapsed;
            MapPanel.Visibility        = Visibility.Collapsed;
            AnalysisHost.Visibility    = Visibility.Visible;
            FadeIn(AnalysisHost);
        }

        /// <summary>关闭内嵌分析页，恢复跟打区。</summary>
        public void CloseAnalysis()
        {
            if (AnalysisHost == null || AnalysisHost.Visibility != Visibility.Visible) return;
            AnalysisHost.Visibility    = Visibility.Collapsed;
            AnalysisContent.Content    = null;
            MainContentRoot.Visibility = Visibility.Visible;
            FadeIn(MainContentRoot);
            InfoBar.Visibility         = Visibility.Visible;
            MarkerBar.Visibility       = Visibility.Visible;
            MapPanel.Visibility = (TogMap != null && TogMap.IsChecked == true)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AnalysisBack_Click(object sender, RoutedEventArgs e) => CloseAnalysis();

        private void MenuItem_OpenErrorBook_Click(object sender, RoutedEventArgs e)
        {
            ShowAnalysis(new Views.ErrorBookView(), "错字本");
        }

        private void MenuItem_OpenSlowCharBook_Click(object sender, RoutedEventArgs e)
        {
            ShowAnalysis(new Views.SlowCharBookView(), "慢字本");
        }

        /// <summary>错字本闭环：把给定文本作为针对练习载入跟打区并激活主窗。</summary>
        /// <returns>用户是否确认载入（正打到一半时取消则返回 false，原内容保留）。</returns>
        public bool LoadPracticeText(string text, string title)
        {
            if (string.IsNullOrEmpty(text)) return false;
            // 正打到一半（已开始、未结束、有输入但没打满）时先确认，避免覆盖丢失当前进度
            bool inProgress = _session.Started && !_session.Finished
                              && _session.LastInputLen > 0
                              && _session.LastInputLen < _session.TypeText.Length;
            if (inProgress)
            {
                var r = System.Windows.MessageBox.Show(
                    "当前正在跟打「" + (string.IsNullOrEmpty(_session.Title) ? "未命名" : _session.Title) +
                    "」，还没打完。\n载入错字练习会覆盖当前内容，未结算的进度将丢失。\n\n确定要切换吗？\n\n[ Esc 取消 ]",
                    "切换到错字练习", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning);
                if (r != System.Windows.MessageBoxResult.OK) return false;
            }
            _currentSegNo = 0;   // 独立练习，不归属任何发文段
            LoadArticle(text, title);
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            TbxInput.Focus();
            return true;
        }

        private void MenuItem_OpenTrend_Click(object sender, RoutedEventArgs e)
        {
            ShowAnalysis(new Views.TrendView(), "成绩趋势");
        }

        private void TxtFootLabel_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ShowAnalysis(new Views.TrendView(), "成绩趋势");
        }
        private void MenuItem_SendImageScore_Click(object sender, RoutedEventArgs e)
        {
            if (History.Count == 0)
            { Services.Toast.Info("没有可发送的成绩，先打一段"); return; }
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
                Services.Toast.Info("当前没有可分析的跟打数据，先打一段试试");
                return;
            }
            ShowAnalysis(new Views.ReportView(_session), "跟打报告");
        }

        private void MenuItem_OpenJjCheck_Click(object sender, RoutedEventArgs e)
        {
            ShowAnalysis(new Views.JjCheckView(), "击键评定");
        }

        // ===== 双拼键位练习（内嵌面板） =====
        private void MenuItem_OpenShuangPin_Click(object sender, RoutedEventArgs e) => EnterPracticeMode();

        private void MenuItem_AuxCodeHelp_Click(object sender, RoutedEventArgs e)
            => new Views.AuxCodeHelpWindow(this).ShowDialog();

        private void MenuItem_XhAuxCodeHelp_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://flypy.cc/help/#/xh");
        }

        private void EnterPracticeMode()
        {
            if (_practiceMode) return;
            // 分析页(AnalysisHost) 与键位练习同为覆盖 MainContentRoot 的层，且分析页 z 序在最上。
            // 若分析页正显示，必须先关掉，否则它盖在键位练习面板上抢走焦点 → 键位练习按键无效。
            CloseAnalysis();
            _practiceMode = true;
            PauseType();   // 停掉跟打计时，避免后台计数
            PracticePanel.BackRequested -= PracticePanel_BackRequested;
            PracticePanel.BackRequested += PracticePanel_BackRequested;
            InfoBar.Visibility        = Visibility.Collapsed;
            MarkerBar.Visibility      = Visibility.Collapsed;
            MapPanel.Visibility       = Visibility.Collapsed;
            MainContentRoot.Visibility = Visibility.Collapsed;
            PracticePanel.Visibility  = Visibility.Visible;
            FadeIn(PracticePanel);
            PracticePanel.FocusForInput();
        }

        /// <summary>给元素做一次 0→1 透明度淡入过渡。</summary>
        private static void FadeIn(System.Windows.UIElement el, double seconds = 0.18)
        {
            if (el == null) return;
            var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                new Duration(System.TimeSpan.FromSeconds(seconds)));
            el.BeginAnimation(System.Windows.UIElement.OpacityProperty, anim);
        }

        private void PracticePanel_BackRequested(object sender, EventArgs e) => ExitPracticeMode();

        private void ExitPracticeMode()
        {
            if (!_practiceMode) return;
            _practiceMode = false;
            PracticePanel.Visibility   = Visibility.Collapsed;
            MainContentRoot.Visibility = Visibility.Visible;
            FadeIn(MainContentRoot);
            InfoBar.Visibility         = Visibility.Visible;
            MarkerBar.Visibility       = Visibility.Visible;
            // 节奏热力条（MapPanel）恢复到“节奏”开关的原状态
            MapPanel.Visibility = (TogMap != null && TogMap.IsChecked == true)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MenuItem_OpenSpeedAnalysis_Click(object sender, RoutedEventArgs e)
        {
            if (_session.TypeText.Length == 0 && _session.Report.Count == 0)
            {
                Services.Toast.Info("当前没有可分析的跟打数据，先打一段试试");
                return;
            }
            ShowAnalysis(new Views.SpeedAnalysisView(_session), "速度分析");
        }

        // ===== 信息条段号点击 → 弹列表跳段 =====
        private void TxtCurSegInfo_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_sending.State.Active)
            {
                Services.Toast.Info("尚未开启发文，请先菜单 → 发文 → 发文...");
                return;
            }
            if (_sending.State.InitialMark != 0)
            {
                Services.Toast.Info("自定义起始位置下不支持跳段");
                return;
            }
            var segs = _sending.EnumerateSegments(previewLen: 14, maxCount: 300);
            if (segs.Count == 0)
            {
                Services.Toast.Info("当前模式不支持段号跳转（乱序/词组模式按性质无法预先确定段号）");
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
                    if (seg == null) { Services.Toast.Warning("跳转失败"); return; }
                    LoadArticle(seg, $"{_sending.State.Title} · 第 {captured} 段");
                    _currentSegNo = captured;
                    RecordResumeProgress(captured);   // 与 Ctrl+←/→ 跳段一致：补记续打（内部自带范围守卫）
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
            // 退出比赛态（若在）：解除标记并展开左侧导航
            if (Services.CloudMatchService.IsMatchArticleLoaded)
            {
                Services.CloudMatchService.ClearCurrentArticle();
                SetNavCollapsed(false);
            }
            _session.Load(string.Empty, string.Empty);
            _prevRawLen = 0;
            RtbCompare.Document.Blocks.Clear();
            _charRuns.Clear();
            _runStatus = new byte[0];
            _slowMarks.Clear();
            _hgMarks.Clear();
            _lastSessionSlowEntries.Clear();
            HideSessionSlowSummary();
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
            Services.Toast.Info("已复位");
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
            // 双拼练习模式：面板自己通过 WPF 焦点接收按键，全局钩/热键/计数全部停用
            if (_practiceMode) return;

            // Ctrl+M 全局切换最小化 / 还原：
            // 故意放在「主窗激活」守卫之前 —— 窗口最小化后已不是激活窗口，
            // 若受 IsActive 限制就永远还原不回来。所以这是真正的全局热键（任何前台程序下都能弹出/收起）。
            if (IsCtrlDown() && vk == 0x4D)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (WindowState == WindowState.Minimized)
                    {
                        WindowState = WindowState.Normal;
                        Activate();
                    }
                    else
                    {
                        WindowState = WindowState.Minimized;
                    }
                }));
                return;
            }

            // 所有热键都要求主窗激活才生效，避免在其他程序里误触
            if (!this.IsActive) return;

            // 内嵌分析页（AnalysisHost）显示时：底层跟打区被盖住、用户看不见，
            // 此时若误触会改动底层内容/进度的功能键（F2 发文 / F3 重打 / F4 抓文 / F6 发下一段），
            // 会在背后悄悄清掉当前进度或换文 → 关掉分析页回去时数据已丢。一律拦掉并提示。
            if (AnalysisHost != null && AnalysisHost.Visibility == Visibility.Visible)
            {
                if (vk == 0x71 || vk == 0x72 || vk == 0x73 || vk == 0x75)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                        Services.Toast.Info("正在看分析页，先返回跟打再用功能键", 2)));
                    return;
                }
            }

            // 群比赛模式（F4 抓来的云比赛文）：锁掉除 F8 暂停以外的所有功能键 / Ctrl 组合键，
            // 防止 F3 重打 / F6 发下一段 / Ctrl+Q 乱序等"重打刷分"，比赛只许打一遍。
            // 注意：只拦功能键与 Ctrl 组合，下方的逐字击键计数不受影响（正常打字照常）。
            if (Services.CloudMatchService.IsMatchArticleLoaded && !Services.CloudMatchService.IsDailyArticle)
            {
                if (vk == 0x77) // F8 暂停 / 继续——比赛中唯一可用功能键
                {
                    Dispatcher.BeginInvoke(new Action(TogglePause));
                    return;
                }
                // F2/F3/F6/F9 或任意 Ctrl 组合键 → 比赛中一律失效并提示
                bool isFnHotkey = (vk == 0x71 || vk == 0x72 || vk == 0x75 || vk == 0x78);
                if (isFnHotkey || IsCtrlDown())
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                        Services.Toast.Warning("比赛中已锁定，只能用 F8 暂停", 2)));
                    return;
                }
            }

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
                case 0x73: // F4 群比赛抓文（凭本场口令从云端拉取比赛文）
                    Dispatcher.BeginInvoke(new Action(FetchMatchArticle));
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
                    case 0x55: // Ctrl+U 重打当前段
                        Dispatcher.BeginInvoke(new Action(Repeat));
                        return;
                    case 0x4A: // Ctrl+J 乱序重抽（发下一段）
                        Dispatcher.BeginInvoke(new Action(SendShuffle));
                        return;
                    case 0x25: // Ctrl+← 上一段
                        Dispatcher.BeginInvoke(new Action(() => JumpSegRelative(-1)));
                        return;
                    case 0x27: // Ctrl+→ 下一段（跳段，不是发送）
                        Dispatcher.BeginInvoke(new Action(() => JumpSegRelative(+1)));
                        return;
                }
            }

            // bug20: 段首首键/首个 IME 音节漏计修复。Started 要等 TextChanged 见到首个 committed
            // 字符才置 true，触发首字的物理键此刻 Started==false，原"未 Started 即 return"会整串跳过。
            // 放行到下方"计 Keys"，但补齐 Started 隐含前置：已载入文本且非只读（Started==true 行为不变）。
            if (!_session.Started && (_session.TypeText.Length == 0 || TbxInput.IsReadOnly)) return;
            if (!TbxInput.IsKeyboardFocused) return;

            // 击键只计字母 / 数字 / 标点 / 回车 / 退格 / 空格（与原版一致，排除修饰键、功能键、方向键、Tab、Esc、Win 等）
            bool isAlpha     = (vk >= 0x41 && vk <= 0x5A);                 // A-Z
            bool isDigit     = (vk >= 0x30 && vk <= 0x39);                 // 0-9 主键盘
            bool isNumpad    = (vk >= 0x60 && vk <= 0x69);                 // 小键盘0-9
            bool isPunct     = (vk >= 0xBA && vk <= 0xC0) || (vk >= 0xDB && vk <= 0xDE); // 标点
            bool isEnter     = vk == 0x0D;
            bool isBackspace = vk == 0x08;
            bool isSpace     = vk == 0x20;
            // bug26: Ctrl 组合键（未被上面快捷键 switch 命中的，如 Ctrl+A/C/S）不计入击键，否则键数虚高
            if (IsCtrlDown()) return;
            if (!(isAlpha || isDigit || isNumpad || isPunct || isEnter || isBackspace || isSpace))
                return;

            // 计数模式（双口径，开关 = SettingsService.MergeChord）：
            //   并击模式（true,默认）：键数在 TbxInput.PreviewKeyDown 里累加（即 IME 没吃掉的键）。
            //     原因：并击键盘单次按 N 键 IME 只识别 1 个候选；钩子按 N 算就大于实际"动作"。
            //   串行模式（false）：键数在这里（KeyHook）累加，每个物理 down 都算 1 击。
            //     原因：单键用户每按一次都该计入，IME 候选/翻页都是真实按键。
            bool mergeChord = Services.SettingsService.Instance.MergeChord ?? true;
            if (!mergeChord)
            {
                _session.Keys++;
                App.Diag("COUNT", $"hook vk=0x{vk:X2} Keys={_session.Keys}");
            }

            // bug20: 选重 / 左右手等其余统计仍按原约定等到 Started 之后再计——本单只补回首键 Keys，
            // 不动其它统计项（未 Started 时 LastInputLen=0，提前算选重会误判）。
            if (!_session.Started) return;

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
            // Esc 取消合成 → 清状态机，避免残留 _imeComposing=true（不依赖 _session.Started）
            if (e.Key == System.Windows.Input.Key.Escape) ResetImeCompose();

            // bug20: 并击口径在此累加 Keys；未 Started（首字未提交）时也放行，否则段首首键/首音节漏计。
            // 补齐 Started 隐含前置：已载入文本且非只读（Started==true 行为不变）。
            if (!_session.Started && (_session.TypeText.Length == 0 || TbxInput.IsReadOnly)) return;

            // 回改 Hg 由 TextChanged 的 back-range 分支统一计（committed 文本变短才算真回改，避免钩子时序竞争）。
            // 这里只负责并击模式下的 Keys 累加；串行模式不在这里计。
            bool mergeChord = Services.SettingsService.Instance.MergeChord ?? true;
            if (!mergeChord) return;

            // 排除修饰键 / 功能键 / 方向键 / Tab / Esc / Win / IME ProcessKey 等
            // 只统计字母 / 数字 / 标点 / 回车 / 退格 / 空格（与老版 TextBox.KeyDown 口径一致）
            var k = e.Key == System.Windows.Input.Key.System ? e.SystemKey
                  : e.Key == System.Windows.Input.Key.ImeProcessed ? e.ImeProcessedKey
                  : e.Key;
            int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(k);

            bool isAlpha     = (vk >= 0x41 && vk <= 0x5A);
            bool isDigit     = (vk >= 0x30 && vk <= 0x39);
            bool isNumpad    = (vk >= 0x60 && vk <= 0x69);
            bool isPunct     = (vk >= 0xBA && vk <= 0xC0) || (vk >= 0xDB && vk <= 0xDE);
            bool isEnter     = vk == 0x0D;
            bool isBackspace = vk == 0x08;
            bool isSpace     = vk == 0x20;
            // bug26: Ctrl 组合键不计入击键（与 KeyHook 串行路径口径一致）
            if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0) return;
            if (!(isAlpha || isDigit || isNumpad || isPunct || isEnter || isBackspace || isSpace)) return;

            _session.Keys++;
            App.Diag("TBOX", $"vk=0x{vk:X2} key={k} Keys={_session.Keys} Last={_session.LastInputLen} rawLen={(TbxInput.Text?.Length ?? 0)}");
        }

        // ===== bug19：跟打输入框禁止粘贴 / 剪切 / 撤销 / 重做 / 拖放（保留复制）=====
        // 禁止状态 = 输入框处于"可跟打态"：已载入文本且非只读。D1=B：所有跟打态全程禁（比赛态同样满足故一并禁），
        // 不依赖只在比赛态为真的标志。正常逐键输入 / 退格 / IME 合成上屏均不触发以下任何 handler，故不受影响。
        private bool IsTypingEditLocked() => _session.TypeText.Length > 0 && !TbxInput.IsReadOnly;

        // 提示文案按是否比赛态区分；用现有右上角 Toast。
        private void WarnTypingEditBlocked(string action)
        {
            bool isMatch = Services.CloudMatchService.IsMatchArticleLoaded
                        && !Services.CloudMatchService.IsDailyArticle;
            Services.Toast.Warning((isMatch ? "比赛中禁止" : "跟打输入框禁止") + action, 2);
        }

        // 最关键一道：DataObject.Pasting 覆盖所有粘贴入口（Ctrl+V / Shift+Insert / 右键粘贴 / 编程 Paste），禁止态一律取消。
        private void TbxInput_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!IsTypingEditLocked()) return;
            e.CancelCommand();
            WarnTypingEditBlocked("粘贴");
        }

        // 编辑命令拦截：禁止态下拦掉 粘贴 / 剪切 / 撤销 / 重做（含右键菜单与组合键）；复制（Copy）及其它命令放行。
        private void TbxInput_PreviewExecuted(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
        {
            if (!IsTypingEditLocked()) return;
            var cmd = e.Command;
            string action;
            if (cmd == System.Windows.Input.ApplicationCommands.Paste) action = "粘贴";
            else if (cmd == System.Windows.Input.ApplicationCommands.Cut) action = "剪切";
            else if (cmd == System.Windows.Input.ApplicationCommands.Undo) action = "撤销";
            else if (cmd == System.Windows.Input.ApplicationCommands.Redo) action = "重做";
            else return;
            e.Handled = true;
            WarnTypingEditBlocked(action);
        }

        // 拖入文本：禁止态下取消接收（Effects=None + Handled），不让拖一段文本绕过逐键。
        private void TbxInput_PreviewDragOverOrDrop(object sender, DragEventArgs e)
        {
            if (!IsTypingEditLocked()) return;
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        // ===== IME 合成态侦测（方案 A'）=====
        // TSF 合成期间（输入拼音、未上屏），TbxInput.Text 会被实时塞入合成中间态（拼音字母/占位空格）。
        // TextInputStart → 合成开始；PreviewTextInput → 合成提交（上屏）/结束。靠这对事件维护标志，
        // 比"猜尾部 ASCII 占位符"可靠：英文直打或已上屏时 _imeComposing=false → 一律逐字判错。
        private bool _imeComposing;
        private int _imeComposeStartLen = -1;
        // 上一次 TextChanged 的 rawInput.Length（含 IME 合成串）。raw 变短而 committed 不变 → 删拼音（拼回）
        private int _prevRawLen;

        private void ResetImeCompose()
        {
            _imeComposing = false;
            _imeComposeStartLen = -1;
            App.Diag("IME", "reset");
        }

        private static bool ChangeTouchesBeforeComposeStart(TextChangedEventArgs e, int composeStart)
        {
            foreach (var ch in e.Changes)
            {
                if (ch.Offset < composeStart) return true;
            }
            return false;
        }

        private static string SafeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string s = text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
            return s.Length <= 24 ? s : s.Substring(0, 24) + "...";
        }

        private static string FormatChanges(TextChangedEventArgs e)
        {
            if (e == null) return string.Empty;
            return string.Join(",", e.Changes.Select(c => $"{c.Offset}:{c.RemovedLength}->{c.AddedLength}"));
        }

        private void TbxInput_TextInputStart(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // 仅当存在真实合成（输入法在组字）时才置位；普通字符的 TextInput 不会进入此事件。
            _imeComposing = true;
            _imeComposeStartLen = TbxInput.Text?.Length ?? 0;
            App.Diag("IME", $"start composeStart={_imeComposeStartLen} rawLen={(TbxInput.Text?.Length ?? 0)} text=[{SafeText(e.Text)}]");
        }

        private void TbxInput_TextInputDone(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // 合成提交/结束（汉字上屏或英文直接输入完成）→ 解除保护，让 TextChanged 走纯逐字比对。
            App.Diag("IME", $"done composeStart={_imeComposeStartLen} rawLen={(TbxInput.Text?.Length ?? 0)} text=[{SafeText(e.Text)}]");
            ResetImeCompose();
        }

        // ===== 输入比对染色 =====

        private void TbxInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_session.TypeText.Length == 0) return;
            if (_session.Finished) return;

            // 暂停中敲键 → 自动继续（原版逻辑）
            if (_isPaused) EndPause();

            // 取文本框全文。
            // IME 合成防护（实测根因）：WPF 走 TSF 而非 IMM，合成期间无法可靠取到合成串长度；
            // 且拼音合成时输入法会先往 TextBox.Text 塞入占位符（半角空格 U+0020 / 字母），
            // 它被当成已上屏字符与中文原文比对 → 把"对的字"染成红色。
            //
            // 规则：只在 IME 合成进行中（_imeComposing）才剥离"尾部"处于中文原文位置上的 ASCII 占位串。
            // 合成中间态只会出现在文本末尾，且很快会被上屏的汉字替换。非合成态（英文直打、已上屏）
            // 完全不剥离 → 真打错的空格/字母/数字一律逐字判错（修复"空格后字母也不判错"）。
            var rawInput = TbxInput.Text ?? string.Empty;
            int curRawLen = rawInput.Length;

            if (_imeComposing)
            {
                if (_imeComposeStartLen < 0 || rawInput.Length < _imeComposeStartLen)
                    ResetImeCompose();

                if (_imeComposing && ChangeTouchesBeforeComposeStart(e, _imeComposeStartLen))
                    ResetImeCompose();

                // 上屏后的尾部已非 ASCII，说明不再是拼音占位串；只 clear，不重入。
                if (_imeComposing && rawInput.Length > _imeComposeStartLen)
                {
                    char tail = rawInput[rawInput.Length - 1];
                    if (tail >= 128) ResetImeCompose();
                }
            }

            int composeStart = _imeComposing && _imeComposeStartLen >= 0
                ? Math.Min(_imeComposeStartLen, rawInput.Length)
                : rawInput.Length;

            int realLen = rawInput.Length;
            while (_imeComposing && realLen > composeStart)
            {
                int i = realLen - 1;
                char inp = rawInput[i];
                // 拼音合成串恒为 ASCII：字母 / 数字 / 空格 / 分隔符（微软拼音用单引号 ' 分隔音节）。
                // 剥离条件：尾部是 ASCII，且 (原文该位是中文) 或 (位置已超出原文长度——多出来的 ASCII
                // 只能是未上屏拼音)。后者关键：合成串把输入撑过原文长度时，尾部越界会让旧逻辑立即
                // break，残留整串逐字染红（微软拼音多音节 shu'ru'kuang' 尤其容易触发）。
                bool inpIsImeJunk = inp < 128;
                bool srcIsCjkOrBeyond = i >= _session.TypeText.Length || _session.TypeText[i] > 127;
                if (srcIsCjkOrBeyond && inpIsImeJunk) realLen--;
                else break;
            }
            var input = realLen < rawInput.Length ? rawInput.Substring(0, realLen) : rawInput;

            // 双保险：如果输入长度等于上次染色长度且未回退，说明只是 IME 切换/光标移动，跳过
            if (input.Length == _session.LastInputLen && _session.Started)
            {
                if (rawInput.Length < _prevRawLen)   // raw 变短而 committed 不变 → 删拼音
                {
                    _session.ImeBackspace++;
                    App.Diag("TEXT", $"imebs++ rawLen={rawInput.Length} prevRaw={_prevRawLen} ImeBs={_session.ImeBackspace}");
                }
                App.Diag("TEXT", $"skip-same rawLen={rawInput.Length} realLen={realLen} len={input.Length} Last={_session.LastInputLen} Cz={_session.Cz} Hg={_session.Hg} ImeBs={_session.ImeBackspace} composing={_imeComposing}");
                _prevRawLen = curRawLen;
                return;
            }

            // 记录回改范围（输入变短）→ 主染色后再触发黄色闪烁，避免被主循环清回默认色
            int hgFrom = -1, hgTo = -1;
            if (_session.Started && input.Length < _session.LastInputLen)
            {
                hgFrom = input.Length;
                hgTo   = _session.LastInputLen;
                _session.Hg++;
                TxtHg.Text = _session.Hg.ToString();
                App.Diag("TEXT", $"hg++ len={input.Length} last={_session.LastInputLen} Hg={_session.Hg}");
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

            // 对照逻辑与老版本（D:\old1）保持一致：逐字直接比较，不匹配即错字（含空格/字母/数字）。
            // 旧的"IME 漏字符防护"会把中文位置的空格当成未输入而截断，导致真打的空格既不显红也不计错，已移除。

            int cz = 0;
            // 差异染色：每次都对已输入区域整段重写背景，避免 RichTextBox 内部布局变化时
            // 缓存状态匹配但 Brush 已丢失导致灰底消失（偶发渲染异常）。性能开销极小（属性赋值）。
            for (int i = 0; i < len; i++)
            {
                byte newSt = Services.TextProcessor.NormalizeForCompare(input[i]) == Services.TextProcessor.NormalizeForCompare(_session.TypeText[i]) ? (byte)1 : (byte)2;
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
            App.Diag("TEXT", $"calc rawLen={rawInput.Length} realLen={realLen} len={len} Last={_session.LastInputLen} Cz={cz} Hg={_session.Hg} ImeBs={_session.ImeBackspace} Keys={_session.Keys} composing={_imeComposing} changes={FormatChanges(e)}");

            // 回改地点高亮（用户反馈干扰，已禁用；保留 TriggerHgFlash/HgFlashTimer 代码以备将来切回）
            // if (hgFrom >= 0) TriggerHgFlash(hgFrom, hgTo);

            // 段内事件 + 慢/回改位置记录（仅记录，不在跟打中染色，等 FinishTyping 时统一染）
            int prevLenBeforeAppend = _session.LastInputLen;
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
                int to   = Math.Min(_session.TypeText.Length, len);
                int from = prevLenBeforeAppend == len
                    ? Math.Max(0, to - 1)
                    : Math.Max(0, Math.Min(prevLenBeforeAppend, _session.TypeText.Length));
                for (int i = from; i < to; i++)
                {
                    if (i >= input.Length || Services.TextProcessor.NormalizeForCompare(input[i]) != Services.TextProcessor.NormalizeForCompare(_session.TypeText[i]))
                    {
                        tailWrong = true;
                        break;
                    }
                }

                if (!tailWrong)
                {
                    _session.Finished = true;
                    FinishTyping();
                    // 正常打完才自动续发（强制结算 TryForceFinalizeLastSegment 走的不是这条路径，不会误触发）
                    ScheduleAutoAdvance();
                }
                // 末字有错 → 不结束，允许用户回改纠正
                // 用户也可以选择不回改，直接载入新文（载入时 _session.Reset()）
            }

            _prevRawLen = curRawLen;
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

        /// <param name="restoreAfter">正常打完路径传 true：群比赛/每日文交卷后解除"完成只读冻结"，
        /// 复位成可重新开始的状态。强制结算（换文/复位触发，本身就在 LoadArticle 内部）传 false，
        /// 由外层流程接管界面状态，避免重入 LoadArticle。</param>
        private void FinishTyping(bool restoreAfter = true)
        {
            _timerTime.Stop();
            _timerStats.Stop();
            TbxInput.IsReadOnly = true;   // 完成后输入框只读，防止走跟打路径

            // 完成时按"全文长度"算，不再被 IME junk 干扰
            int total = _session.TypeText.Length;
            _session.LastInputLen = total;
            if (_session.EndTime == null) _session.EndTime = DateTime.Now;

            // 出成绩时统一染色：回改位置浅黄，慢字位置浅绿（错字仍是红色，由 TextChanged 持续维护）
            ApplyResultMarks();

            UpdateStatsDisplay();
            UpdateProgress();
            RefreshBmTips();

            var (speed, speed2, jj, mc, sec) = _session.ComputeStats(total);
            // 个人最佳(PB)：必须在把当前成绩写库前取历史最高速，否则纪录里已含本段
            double oldBest = HistoryRepository.LoadAggregate().MaxSpeed;
            App.Diag("FINISH", $"chars={total} Keys={_session.Keys} Hg={_session.Hg} Cz={_session.Cz} ImeBs={_session.ImeBackspace} Waste={_session.ComputeWasteKeys()} Reselect={_session.Reselect} sec={sec:0.00} speed={speed:0.00} jj={jj:0.00} mc={mc:0.00}");

            // 错字本：逐字比对原文与最终输入，采集"正确字→打成字"明细（独立 errorbook.db）
            CollectErrorsToBook(total);
            // 慢字本：按事件均摊估每字耗时/键数，采集慢/回改/高码长弱项明细（独立 slowchar.db；限速不入历史也照常采集）
            CollectSlowToBook(total);

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
                ImeBackspace = _session.ImeBackspace,
                LeftHand = _session.LeftHand,
                RightHand= _session.RightHand,
            };
            History.Insert(0, row);
            HistoryRepository.Insert(row);
            RefreshSummaryCache();
            }   // end if (!blockedByLimit)

            if (blockedByLimit)
            {
                Services.Toast.Warning($"速度 {speed:0.00} 低于阈值，未入历史（菜单 → 外观 → 个签 Tab 改阈值）");
            }
            else
            {
                // 个人最佳提醒：仅在本段已入历史（未被限速拦截）且历史非空时比较
                string pbLine = "";
                if (!blockedByLimit && oldBest > 0)
                {
                    if (speed > oldBest)
                        pbLine = $"🏆 新纪录！比上次最佳 +{speed - oldBest:0.0} 字/分\n";
                    else if (speed >= oldBest * 0.95)
                        pbLine = $"距纪录就差 {oldBest - speed:0.0} 字/分，加油！\n";
                }

                Services.Toast.Success(
                    pbLine +
                        $"完成！速度 {speed:0.00}（错一罚五 {speed2:0.00}）| 击键 {jj:0.00} | 码长 {mc:0.00} | 用时 {sec:0.00}s\n" +
                        $"错字 {_session.Cz} | 回改 {_session.Hg} | 键数 {_session.Keys} | 打词 {_session.Words} | 选重 {_session.Reselect} | 拼回 {_session.ImeBackspace} | 左:右 {_session.LeftHand}:{_session.RightHand}",
                    pbLine.Length > 0 ? 4 : 2);   // 破纪录/接近多停一会儿

                // 图片成绩：完成自动截 ReportWindow 复制到剪贴板
                if (TogImage != null && TogImage.IsChecked == true)
                    AutoCopyResultImage();
            }

            // 群比赛：若当前是 F4 抓来的比赛文，且开启自动上传，则把本段成绩交到云端。
            // 注意：比赛成绩上传不受本地「限速」开关影响——限速只过滤本地历史（练习用），
            // 比赛是真实成绩，哪怕速度低于本地阈值也必须交卷，否则群友会误以为已交卷却查无此人。
            bool cloudArticle = Services.CloudMatchService.IsMatchArticleLoaded;
            if (cloudArticle && (SettingsService.Instance.CloudAutoUpload ?? true))
            {
                // UploadMatchScore 是 async void：其同步段（读 token / isDaily / HasSubmitted）在本次调用内
                // 即刻执行完毕，真正的网络 await 之后才让出 → 此处返回时 token 已被捕获到上传方法的局部变量，
                // 后续 RestoreAfterCloudFinish 清/留比赛标记不会影响这次上传。
                UploadMatchScore(speed, jj, mc, _session.Cz, sec);
            }

            InlineChartMarkFinish();

            // 解除"完成只读冻结"：群比赛/每日文打完并已触发后台交卷后，复位成可重新开始的状态，
            // 让用户立刻能重新跟打 / 正常打字 / 发文（不等上传完成；上传成功/失败仅 Toast 提示）。
            // 仅正常打完路径执行；强制结算路径 restoreAfter=false，由外层 LoadArticle/复位接管。
            if (cloudArticle && restoreAfter)
                RestoreAfterCloudFinish();
            else if (restoreAfter)
                ShowSessionSlowSummary();   // 普通练习结算：对照区一角浮现"本场最卡字"（云结算随后会复位/清空，不展示）

            // 自定义文章续打：正常打完当前段后把续打记录推进到"下次应载入段"。
            // 仅正常完成（restoreAfter）且当前确为发文段（_currentSegNo>0）才推进；强制结算/换文/复位走
            // FinishTyping(false) 不在此推进。
            // 用 Mark 判是否还有下一段：Mark 是发文推进的真实偏移（载入第 N 段后即"第 N 段末尾"），
            // 也是 NextSegment 判 null 的依据。不受"停止发文"(Active=false 致 EnumerateSegments 返空)
            // 与 EnumerateSegments 的 500 段上限影响。CustomFile/InitialMark/群比赛等范围由
            // RecordResumeProgress/ClearResumeProgress 内部守卫处理。
            if (restoreAfter && _currentSegNo > 0)
            {
                if (_sending.State.Mark < _sending.State.FullText.Length)
                    RecordResumeProgress(_currentSegNo + 1);   // 还有下一段 → 推进
                else
                    ClearResumeProgress();                     // 当前是末段 → 清除
            }
        }

        /// <summary>群比赛/每日文打完并已触发交卷后，解除"完成只读冻结"，把界面复位成可重新开始的状态。
        /// 复用 <see cref="LoadArticle"/> 的整套复位（解只读 / 清空输入 / 重新聚焦 / 重置计时·统计·钩子 /
        /// 重建对照区 / _session.Finished 复位），避免只翻标志位漏掉其它状态。
        /// 比赛文（只许打一遍）：复位顺带退出比赛态（恢复被锁的功能键）。
        /// 每日文（可反复打卷刷分）：复位后重置每日标记与展示标题，保持"再打一卷"体验。</summary>
        private void RestoreAfterCloudFinish()
        {
            bool wasDaily = Services.CloudMatchService.IsDailyArticle;
            string token  = Services.CloudMatchService.CurrentArticleToken;
            string aTitle = Services.CloudMatchService.CurrentArticleTitle;
            string winTitle = this.Title;     // 保留"📚 每日文 …"等自定义标题
            string headTitle = TxtTitle.Text;

            LoadArticle(_session.TypeText, _session.Title);

            if (wasDaily)
            {
                Services.CloudMatchService.SetCurrentArticle(token, aTitle, "daily");
                SetNavCollapsed(true);
                this.Title    = winTitle;
                TxtTitle.Text = headTitle;
            }
        }

        /// <summary>导航栏「群比赛设置」入口。</summary>
        private void MenuItem_OpenCloudMatch_Click(object sender, RoutedEventArgs e)
        {
            var win = new Views.CloudMatchWindow(this);
            win.ShowDialog();
        }

        /// <summary>导航栏「抓比赛文」入口（等同 F4）。</summary>
        private void MenuItem_FetchMatch_Click(object sender, RoutedEventArgs e) => FetchMatchArticle();

        /// <summary>导航栏「每日一文」入口：抓每日文，可反复跟打、不锁键、不断刷新最好成绩。</summary>
        private void MenuItem_FetchDaily_Click(object sender, RoutedEventArgs e) => FetchDailyArticle();

        /// <summary>F4 抓文进行中标志：防 await 网络期间重复按 F4 弹出多个口令框/重复抓取。</summary>
        private bool _fetchingMatch;

        /// <summary>F4：凭本场口令从云端抓比赛文并载入，载入后标记为比赛文（锁键 + 完成自动交卷）。</summary>
        private async void FetchMatchArticle()
        {
            if (_fetchingMatch) return;   // 抓取进行中，忽略重复 F4
            _fetchingMatch = true;
            try
            {
                // 本场口令每场都换 → F4 时现场输入（预填上次口令方便连打同场）
                var prompt = new Views.TokenPromptWindow(this, SettingsService.Instance.SessionToken);
                if (prompt.ShowDialog() != true) return;
                string token = prompt.Token;
                SettingsService.Instance.SessionToken = token;   // 记住本场口令，下次预填
                SettingsService.Save();
                try
                {
                    var (title, content, tk) = await Services.CloudMatchService.FetchArticleAsync(token);
                    if (Services.CloudMatchService.LastFetchedMode == "daily")
                    {
                        Services.Toast.Warning("这是每日一文，请用每日一文入口抓取", 4);
                        return;
                    }
                    _currentSegNo = 0;
                    LoadArticle(content, title);                       // 内部会先清比赛标记
                    Services.CloudMatchService.SetCurrentArticle(tk, title, "match"); // 再置位 → 进入比赛态
                    SetNavCollapsed(true);                             // 比赛态：左侧导航自动缩回，跟打区更专注
                    this.Title = "🏆 比赛中 - " + (string.IsNullOrEmpty(title) ? "比赛文" : title);
                    TxtTitle.Text = "🏆 比赛中 · " + (string.IsNullOrEmpty(title) ? "比赛文" : title);
                    Services.Toast.Success($"已抓取比赛文：{title}（比赛中仅 F8 可用）", 3);
                }
                catch (Exception ex)
                {
                    Services.Toast.Error("抓文失败：" + ex.Message, 4);
                }
            }
            finally
            {
                _fetchingMatch = false;
            }
        }

        /// <summary>「每日一文」：凭口令从云端抓每日文并载入。daily 不锁键、可反复打、反复交卷刷新最好成绩。</summary>
        private async void FetchDailyArticle()
        {
            if (_fetchingMatch) return;   // 抓取进行中，忽略重复触发
            _fetchingMatch = true;
            try
            {
                var prompt = new Views.TokenPromptWindow(this, SettingsService.Instance.SessionToken);
                if (prompt.ShowDialog() != true) return;
                string token = prompt.Token;
                SettingsService.Instance.SessionToken = token;   // 记住口令，下次预填
                SettingsService.Save();
                try
                {
                    var (title, content, tk) = await Services.CloudMatchService.FetchArticleAsync(token);
                    if (Services.CloudMatchService.LastFetchedMode != "daily")
                    {
                        Services.Toast.Warning("这是比赛文，请用 F4 比赛入口抓取", 4);
                        return;
                    }
                    _currentSegNo = 0;
                    LoadArticle(content, title);                            // 内部会先清比赛标记
                    Services.CloudMatchService.SetCurrentArticle(tk, title, "daily"); // 置为每日文
                    SetNavCollapsed(true);
                    this.Title = "📚 每日文 - " + (string.IsNullOrEmpty(title) ? "每日一文" : title);
                    TxtTitle.Text = "📚 每日文 · " + (string.IsNullOrEmpty(title) ? "每日一文" : title);
                    Services.Toast.Success($"已加载每日一文：{title}（可反复打卷刷新最好成绩）", 3);
                }
                catch (Exception ex)
                {
                    Services.Toast.Error("抓文失败：" + ex.Message, 4);
                }
            }
            finally
            {
                _fetchingMatch = false;
            }
        }
        private async void UploadMatchScore(double speed, double jj, double mc, int cz, double sec)
        {
            // 提交那一刻的本场 token：成绩卡看榜用它（bug1：不依赖可能已被复位的全局 token）。
            // bug7：连同提交瞬间的 isDaily/mode 一并快照，重试窗内用户手动切场也不漂移。
            string submitToken = Services.CloudMatchService.CurrentArticleToken;
            bool   submitIsDaily = Services.CloudMatchService.IsDailyArticle;
            string submitMode = Services.CloudMatchService.CurrentArticleMode;
            if (!Services.CloudMatchService.IsDailyArticle && Services.CloudMatchService.HasSubmitted(submitToken))
            {
                Services.Toast.Warning("本场你已交过卷了，不再重复上传", 3);
                return;
            }
            try
            {
                var result = await Services.CloudMatchService.UploadScoreAsync(speed, jj, mc, cz, sec, submitToken, submitIsDaily, submitMode);

                // bug7：write_conflict 是服务端明确「本次未登记」的安全可重试冲突，
                // 自动重试 1 次（共 2 次尝试）再放弃，仍冲突才提示用户手动重交；
                // 只对 write_conflict 重试，duplicate/cap/限流/身份类一律不重试。
                if (result.Code == "write_conflict")
                {
                    Services.Toast.Info("提交冲突，正在自动重试…", 2);
                    await System.Threading.Tasks.Task.Delay(300);
                    result = await Services.CloudMatchService.UploadScoreAsync(speed, jj, mc, cz, sec, submitToken, submitIsDaily, submitMode);
                }

                // Bug16：先按统一 code 分流；之后才走旧布尔兜底（成功 match/daily）。
                switch (result.Code)
                {
                    case "duplicate":
                        Services.Toast.Warning("本场你已交过卷了，不再重复上传", 3);
                        ShowCloudScoreCard(speed, jj, mc, cz, sec, result, null, submitToken);
                        return;
                    case "write_conflict":
                        Services.Toast.Warning("提交冲突，请点重打/稍后再交一次；本次没有登记到云榜", 4);
                        ShowCloudScoreCard(speed, jj, mc, cz, sec, result, null, submitToken);
                        return;
                    case "daily_over_limit":
                        Services.Toast.Info("今日重打次数已达上限，本次不再上传", 4);
                        ShowCloudScoreCard(speed, jj, mc, cz, sec, result, null, submitToken);
                        return;
                    case "score_cap_reached":
                        Services.Toast.Error("本场提交人数已达上限，请联系主持", 5);
                        ShowCloudScoreCard(speed, jj, mc, cz, sec, result, null, submitToken);
                        return;
                    case "rate_limited":
                        Services.Toast.Warning("请求过于频繁，请稍后再试；本次未登记到云榜", 4);
                        ShowCloudScoreCard(speed, jj, mc, cz, sec, result, null, submitToken);
                        return;
                    case "local_too_fast":
                        Services.Toast.Info($"交得太快啦，{result.RetryAfterSeconds} 秒后可再上传；本次本地成绩已结算", 4);
                        ShowCloudScoreCard(speed, jj, mc, cz, sec, result, null, submitToken);
                        return;
                    case "local_in_flight":
                        Services.Toast.Info("上一卷正在上传，请稍候再交；本次本地成绩已结算", 4);
                        ShowCloudScoreCard(speed, jj, mc, cz, sec, result, null, submitToken);
                        return;
                    case "mode_mismatch":
                        Services.Toast.Error("文章模式不符，请用正确入口重新抓取", 4);
                        return;
                    case "invalid_code":
                        Services.Toast.Error("个人码无效", 4);
                        return;
                    case "invalid_device":
                        Services.Toast.Error("设备标识无效，无法交卷", 4);
                        return;
                    case "missing_identity":
                        Services.Toast.Error("缺少身份信息，无法交卷", 4);
                        return;
                    case "session_expired":
                        Services.Toast.Error("本场已超时", 4);
                        return;
                    case "session_not_found":
                        Services.Toast.Error("本场已结束或无发文", 4);
                        return;
                }

                // 旧布尔兜底：成功路径（Code 为空）。
                if (result.IsDuplicate)
                {
                    Services.Toast.Warning("本场你已交过卷了，不再重复上传", 3);
                }
                else if (result.IsDaily)
                {
                    double cur = result.New ?? speed;
                    if (result.Improved)
                    {
                        string fromTo = result.Old.HasValue
                            ? $"{result.Old.Value:0.00}→{cur:0.00}"
                            : $"{cur:0.00}";
                        Services.Toast.Success($"新纪录！{fromTo}（最佳 {result.Best:0.00}）", 4);
                    }
                    else
                    {
                        Services.Toast.Info($"本次 {cur:0.00}，未超越最佳 {result.Best:0.00}", 3);
                    }
                }
                else
                {
                    Services.Toast.Success($"成绩已交卷：{result.Name}  速度 {speed:0.00}", 3);
                }

                // Bug4：名次直接用云端回的 MyNo，不再交卷后二次拉 /rank 算名次（省一次云请求）。
                ShowCloudScoreCard(speed, jj, mc, cz, sec, result, result.MyNo, submitToken);
            }
            catch (Exception ex)
            {
                Services.Toast.Error("上传成绩失败：" + ex.Message, 4);
            }
        }

        /// <summary>弹云成绩窗（非模态）。订阅其「看榜单」事件 → 看榜入口①（防抖拉一次 /rank 开榜单窗）。
        /// rankToken：提交那一刻的本场 token，成绩卡看榜用它拉本场榜（bug1：不依赖可能已复位的全局 token）。</summary>
        private void ShowCloudScoreCard(double speed, double jj, double mc, int cz, double useTime,
                                        Services.CloudMatchService.UploadResult result, int? rank, string rankToken)
        {
            var card = new Views.CloudScoreCard(speed, jj, mc, cz, useTime, result, rank, result.DailyOverLimit);
            card.Owner = this;
            // 看榜入口①：成绩弹窗内「看榜单」→ 走统一的防抖拉榜（榜单窗 owner 用成绩窗），用提交时 token。
            card.OnViewRank = () => { _ = OpenRankWindowAsync(card, rankToken); };
            card.Show();
        }

        /// <summary>看榜拉取进行中标志：进行中再点忽略，杜绝并发刷请求。</summary>
        private bool _rankFetching;
        /// <summary>上次看榜点击时间（UTC），用于 2.5s 防抖。</summary>
        private DateTime _lastRankClickUtc = DateTime.MinValue;

        /// <summary>看榜统一入口：防抖 2.5s + 进行中禁用，校验本场口令，拉一次 /rank 后开榜单窗。
        /// cost-guard：每次点击只发 1 次，失败给友好提示，不重试不轮询。</summary>
        private async System.Threading.Tasks.Task OpenRankWindowAsync(Window owner, string tokenOverride = null)
        {
            if (_rankFetching) return;                                           // 请求进行中：忽略
            if ((DateTime.UtcNow - _lastRankClickUtc).TotalSeconds < 2.5) return; // 防抖 2.5s
            // bug1：成绩卡看榜传提交时的本场 token；常驻看榜按钮当前有文用全局 token，交卷后传完成场口令兜底。
            string token = string.IsNullOrEmpty(tokenOverride)
                ? Services.CloudMatchService.CurrentArticleToken
                : tokenOverride;
            if (string.IsNullOrEmpty(token))
            {
                Services.Toast.Warning("当前没有比赛文，先按 F4 抓比赛文再看榜", 3);
                return;
            }
            _lastRankClickUtc = DateTime.UtcNow;
            _rankFetching = true;
            try
            {
                var result = await Services.CloudMatchService.FetchRankAsync(token);
                Views.CloudRankWindow.Show(result, owner ?? this);
            }
            catch (Exception ex)
            {
                Services.Toast.Error("拉榜失败：" + ex.Message, 4);
            }
            finally
            {
                _rankFetching = false;
            }
        }

        /// <summary>看榜入口②：导航栏群比赛区「看榜」常驻按钮。点击禁用按钮 + 防抖，拉一次 /rank 开榜单窗。</summary>
        private async void MenuItem_OpenRank_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Control;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                // bug1：当前有比赛文就看当前场；交卷后 CurrentArticleToken 已复位时，
                // 用最近完成的本场口令兜底，仍能看本场榜（下一场新发文则优先当前场）。
                string fallback = string.IsNullOrEmpty(Services.CloudMatchService.CurrentArticleToken)
                    ? Services.CloudMatchService.LastFinishedMatchToken
                    : null;
                await OpenRankWindowAsync(this, fallback);
            }
            finally { if (btn != null) btn.IsEnabled = true; }
        }

        /// <summary>逐字比对原文与最终输入，把所有"打错的字"(正确字→打成字)写入错字本独立库。
        /// 末位多打/少打不算（只比对原文长度内的对应位）。</summary>
        private void CollectErrorsToBook(int total)
        {
            string input = TbxInput.Text ?? string.Empty;
            int len = Math.Min(total, Math.Min(input.Length, _session.TypeText.Length));
            var errs = new System.Collections.Generic.List<(string correct, string typed)>();
            var chars = new System.Collections.Generic.List<(string correct, bool wrong)>();
            for (int i = 0; i < len; i++)
            {
                char correct = _session.TypeText[i];
                char typed = input[i];
                bool wrong = Services.TextProcessor.NormalizeForCompare(typed) != Services.TextProcessor.NormalizeForCompare(correct);
                chars.Add((correct.ToString(), wrong));
                if (wrong)
                    errs.Add((correct.ToString(), typed.ToString()));
            }
            Services.ErrorBookRepository.InsertBatch(errs, _session.Title);
            Services.ErrorBookRepository.UpsertBatch(chars);
        }

        /// <summary>慢字本：结算时按事件均摊估每字耗时(秒)/键数，逐字采集"慢/回改/高码长"弱项明细落库。
        /// 仅最终打对的字才算（打错归错字本）；噪声(>6s)、标点/空白/控制/emoji 一律跳过；
        /// 只喂弱项（慢 or 回改 or 高码长），避免正常字撑爆 slow_log。失败仅 Debug.WriteLine，不阻塞结算。</summary>
        private void CollectSlowToBook(int total)
        {
            var entries = new List<Services.SlowEntry>();
            _lastSessionSlowEntries = entries;   // 每次结算前清空再填（供结算摘要 UI 读取）
            try
            {
                if (total <= 0) return;

                // 按事件均摊估每字耗时(秒)/键数（镜像 ScoreCard.BuildHeat 口径：后写事件覆盖前者=最终产生该字的耗时）
                var charSec  = new double[total];
                var charTick = new double[total];
                int prev = 0;
                foreach (var ev in _session.Report)
                {
                    if (ev.Length <= 0) { prev = ev.End; continue; }
                    double ps = ev.TotalTime / ev.Length;
                    double pt = (double)ev.TotalTick / ev.Length;
                    int to = Math.Min(ev.End, total);
                    for (int i = prev; i < to; i++) { charSec[i] = ps; charTick[i] = pt; }
                    prev = ev.End;
                }

                string input = TbxInput.Text ?? string.Empty;
                int len = Math.Min(total, Math.Min(input.Length, _session.TypeText.Length));
                for (int i = 0; i < len; i++)
                {
                    // 仅最终打对才算慢字；打错归错字本
                    if (Services.TextProcessor.NormalizeForCompare(input[i]) != Services.TextProcessor.NormalizeForCompare(_session.TypeText[i]))
                        continue;

                    double ps = charSec[i];
                    double pt = charTick[i];
                    if (ps > MaxSlowCharSec) continue;   // 停顿/离开等噪声丢弃

                    bool slow = ps >= SlowCharThresholdSec;
                    bool hg   = _hgMarks.Contains(i);
                    bool hk   = pt >= HighKeyPerChar;
                    if (!(slow || hg || hk)) continue;   // 只喂弱项

                    char center = _session.TypeText[i];
                    if (!IsCollectibleChar(center)) continue;   // 标点/空白/控制/emoji 不入库

                    string ctx = BuildSlowContext(_session.TypeText, i);
                    entries.Add(new Services.SlowEntry
                    {
                        Ch            = center.ToString(),
                        Context       = ctx,
                        Pos           = i,
                        PerSec        = ps,
                        ThresholdSec  = SlowCharThresholdSec,
                        PerTick       = pt,
                        Slow          = slow,
                        Hg            = hg,
                        HighKey       = hk,
                        SourceSnippet = ctx,
                    });
                }

                string segLabel = _currentSegNo > 0 ? _currentSegNo.ToString() : null;
                Services.SlowCharRepository.InsertBatch(entries, _session.Title, segLabel);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("CollectSlowToBook: " + ex); }
        }

        /// <summary>慢字本可采集字符：保留中文/英文/数字；空白/制表/换行/控制/标点/符号/emoji(代理对) 跳过。</summary>
        private static bool IsCollectibleChar(char c)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c)) return false;
            if (char.IsSurrogate(c)) return false;                 // emoji 等代理对
            if (char.IsPunctuation(c) || char.IsSymbol(c)) return false;
            return char.IsLetterOrDigit(c);
        }

        /// <summary>取以 center 为中心、左右各至多 2 字的上下文片段；遇标点/空白/emoji 即停（不跨句），长度 1~5。</summary>
        private static string BuildSlowContext(string text, int center)
        {
            int lo = center, hi = center;
            for (int step = 0; step < 2 && lo - 1 >= 0 && !IsContextBoundary(text[lo - 1]); step++) lo--;
            for (int step = 0; step < 2 && hi + 1 < text.Length && !IsContextBoundary(text[hi + 1]); step++) hi++;
            return text.Substring(lo, hi - lo + 1);
        }

        /// <summary>上下文窗口的"句子边界"：空白/控制/标点/符号/代理对都视为停止扩展点。</summary>
        private static bool IsContextBoundary(char c)
        {
            return char.IsWhiteSpace(c) || char.IsControl(c)
                || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsSurrogate(c);
        }

        // ===== 本场"最卡字"摘要 / 生成慢字练习（sc-finish-summary-ui） =====

        /// <summary>"本场最卡字"摘要列表项（仅供结算摘要 ItemsControl 绑定显示）。</summary>
        public sealed class SessionSlowItem
        {
            public string Ch { get; set; }
            public string Stat { get; set; }       // 耗时 · 慢次数 · 回改标记
            public string Context { get; set; }    // 代表上下文片段
        }

        /// <summary>把本场落库的弱项明细 <see cref="_lastSessionSlowEntries"/> 按字聚合为弱项行：
        /// WeakScore 与 SlowCharRepository 同口径，按 WeakScore 倒序取 Top N（结算摘要与生成练习共用）。</summary>
        private List<Services.SlowRankRow> AggregateSessionTopSlow(int topN)
        {
            var rows = new List<Services.SlowRankRow>();
            var entries = _lastSessionSlowEntries;
            if (entries == null || entries.Count == 0) return rows;

            var order = new List<string>();
            var groups = new Dictionary<string, List<Services.SlowEntry>>();
            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrEmpty(e.Ch)) continue;
                if (!groups.TryGetValue(e.Ch, out var g)) { g = new List<Services.SlowEntry>(); groups[e.Ch] = g; order.Add(e.Ch); }
                g.Add(e);
            }

            foreach (var ch in order)
            {
                var g = groups[ch];
                int slow = 0, hg = 0, hk = 0, overN = 0;
                double overSum = 0;
                foreach (var e in g)
                {
                    if (e.Slow) { slow++; overSum += Math.Max(0.0, e.PerSec - e.ThresholdSec); overN++; }
                    if (e.Hg) hg++;
                    if (e.HighKey) hk++;
                }
                var row = new Services.SlowRankRow
                {
                    Ch           = ch,
                    SlowCount    = slow,
                    AvgOverSec   = overN > 0 ? overSum / overN : 0.0,
                    HgCount      = hg,
                    HighKeyCount = hk,
                    ErrorCount   = 0,
                    Mastered     = false,
                    LastSeen     = DateTime.Now,
                };
                row.WeakScore = row.SlowCount * 3.0
                              + row.AvgOverSec * 2.0
                              + row.HgCount * 1.5
                              + row.HighKeyCount * 0.8
                              + row.ErrorCount * 1.0;
                rows.Add(row);
            }

            rows.Sort((a, b) => b.WeakScore.CompareTo(a.WeakScore));
            if (rows.Count > topN) rows.RemoveRange(topN, rows.Count - topN);
            return rows;
        }

        /// <summary>把 Top 弱项行配上每字代表上下文/耗时（取该字 PerSec 最大那条）做成摘要列表项。</summary>
        private List<SessionSlowItem> BuildSessionSlowItems(IReadOnlyList<Services.SlowRankRow> top)
        {
            var items = new List<SessionSlowItem>();
            var entries = _lastSessionSlowEntries;
            foreach (var r in top)
            {
                Services.SlowEntry rep = null;
                if (entries != null)
                    foreach (var e in entries)
                        if (e != null && e.Ch == r.Ch && (rep == null || e.PerSec > rep.PerSec)) rep = e;

                double perSec = rep != null ? rep.PerSec : 0.0;
                string ctx = rep != null && !string.IsNullOrEmpty(rep.Context) ? rep.Context : r.Ch;
                string stat = $"{perSec:0.0}s · 慢{r.SlowCount}" + (r.HgCount > 0 ? " · 回改" : "");
                items.Add(new SessionSlowItem { Ch = r.Ch, Stat = stat, Context = ctx });
            }
            return items;
        }

        /// <summary>结算后浮现"本场最卡字"摘要：聚合 Top N，空则不显示该块。</summary>
        private void ShowSessionSlowSummary()
        {
            var top = AggregateSessionTopSlow(SessionSlowTopN);
            if (top.Count == 0)
            {
                HideSessionSlowSummary();
                return;
            }
            IcSlowSummary.ItemsSource = BuildSessionSlowItems(top);
            SessionSlowSummary.Visibility = Visibility.Visible;
        }

        /// <summary>隐藏"本场最卡字"摘要（换文 / 复位 / 载入练习时调用）。</summary>
        private void HideSessionSlowSummary()
        {
            if (SessionSlowSummary == null) return;
            SessionSlowSummary.Visibility = Visibility.Collapsed;
            IcSlowSummary.ItemsSource = null;
        }

        /// <summary>"生成慢字练习"：用本场聚合的 Top 弱项调 SlowCharDrillBuilder，载入主窗跟打区（沿用覆盖确认）。</summary>
        private void BtnGenSlowDrill_Click(object sender, RoutedEventArgs e)
        {
            var top = AggregateSessionTopSlow(SessionSlowTopN);
            var (text, title) = Services.SlowCharDrillBuilder.BuildFromSession(top);
            if (string.IsNullOrEmpty(text))
            {
                Services.Toast.Info("本场没有明显慢字");
                return;
            }
            // 用户在覆盖确认里取消 → 不覆盖，保持当前页与摘要
            if (!LoadPracticeText(text, title)) return;
            Services.Toast.Success($"已生成 {text.Length} 字慢字练习，去主窗开打");
        }

        private void BtnCloseSlowSummary_Click(object sender, RoutedEventArgs e)
        {
            HideSessionSlowSummary();
        }

        /// <summary>完成时若"图片"开启，渲染 ScoreCard UserControl 复制到剪贴板。</summary>
        private void AutoCopyResultImage()
        {
            try
            {
                var card = new Views.ScoreCard(_session);
                // 强制布局尺寸（UserControl 未挂到窗口树时不会自动 Measure/Arrange）
                card.Measure(new Size(card.Width, card.Height));
                card.Arrange(new Rect(0, 0, card.Width, card.Height));
                card.UpdateLayout();

                // 延迟一帧让 OxyPlot / Canvas SizeChanged 完成绘制
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        int w = (int)Math.Ceiling(card.Width);
                        int h = (int)Math.Ceiling(card.Height);
                        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                            w * 2, h * 2, 192, 192,    // 2x DPI 让图更清晰
                            System.Windows.Media.PixelFormats.Pbgra32);
                        rtb.Render(card);
                        System.Windows.Media.Imaging.BitmapSource frozen = rtb;
                        // 防 OpenClipboard 0x800401D0：被其他进程占用时重试
                        bool ok = false;
                        for (int retry = 0; retry < 4 && !ok; retry++)
                        {
                            try { System.Windows.Clipboard.SetImage(frozen); ok = true; }
                            catch { System.Threading.Thread.Sleep(80); }
                        }
                        if (ok) Services.Toast.Success("成绩图已自动复制到剪贴板");
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine("AutoCopyResultImage render: " + ex); }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("AutoCopyResultImage outer: " + ex); }
        }
    }
}
