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
        private SpeedChartWindow _chartWin;

        // 重数色 (与原版 Glob.BmColors 一致)
        private static readonly Brush[] RankBrushes =
        {
            new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xE2)),  // 1重
            new SolidColorBrush(Color.FromRgb(0xE2, 0x4A, 0x4A)),  // 2重
            new SolidColorBrush(Color.FromRgb(0x9C, 0x4A, 0xE2)),  // 3重
            new SolidColorBrush(Color.FromRgb(0xE2, 0x4A, 0x9C)),  // 4重+
        };

        public ObservableCollection<HistoryRow> History { get; } = new ObservableCollection<HistoryRow>();

        // 颜色
        private static readonly Brush BrushDefault = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        private static readonly Brush BrushRight   = new SolidColorBrush(Color.FromRgb(0x16, 0x6F, 0x16));
        private static readonly Brush BrushRightBg = new SolidColorBrush(Color.FromRgb(0xCC, 0xF2, 0xCC));
        private static readonly Brush BrushWrong   = new SolidColorBrush(Color.FromRgb(0xCC, 0x33, 0x33));
        private static readonly Brush BrushWrongBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0xD8));
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

            DgvHistory.ItemsSource = History;

            _timerTime.Tick  += TimerTime_Tick;
            _timerStats.Tick += TimerStats_Tick;

            _keyHook.KeyDown += KeyHook_KeyDown;
            try { _keyHook.Start(); } catch { /* 钩子安装失败，不影响 UI */ }

            // 异步加载词典（76145 行，主线程加载会卡 ~200ms，可以接受但提示一下）
            try { _dict.LoadFromResource(); } catch { /* 词典加载失败不致命 */ }

            this.Closed += (s, e) =>
            {
                _timerTime.Stop();
                _timerStats.Stop();
                _keyHook.Dispose();
                _chartWin?.Close();
            };
        }

        // ===== 速度曲线 =====

        private void TogChart_Toggled(object sender, RoutedEventArgs e)
        {
            if (TogChart.IsChecked == true)
            {
                if (_chartWin == null)
                {
                    _chartWin = new SpeedChartWindow(this);
                    _chartWin.Closed += (s, _) => { _chartWin = null; TogChart.IsChecked = false; };
                }
                _chartWin.Show();
            }
            else
            {
                _chartWin?.Hide();
            }
        }

        // ===== 编码提示（当前字 1 个）=====

        private void MenuItem_OpenBmTips_Click(object sender, RoutedEventArgs e)
        {
            TogBmTips.IsChecked = true;
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
            // 词组优先：在当前位置最长匹配，匹中词组则显示整词编码
            var entry = _dict.MatchAt(_session.TypeText, len);
            if (entry == null)
            {
                BmChar.Text = _session.TypeText[len].ToString();
                BmCode.Text = "无";
                BmRankBox.Background = Brushes.Gray;
                return;
            }
            BmChar.Text = entry.Word;
            BmCode.Text = entry.Code;
            BmRankBox.Background = RankBrushes[Math.Min(entry.Rank - 1, RankBrushes.Length - 1)];
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

            // 末尾汇总：今日/总字数/天数/记录字数 (P5 接持久化后上真数据)
            TxtTotalInfo.Text = $"{len}/{len}/1天/{len}";
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
            if (_session.Started && !_session.Finished
                && (TbxInput.Text?.Length ?? 0) >= _session.TypeText.Length
                && _session.TypeText.Length > 0)
            {
                _session.Finished = true;
                FinishTyping();
            }

            _session.Load(text, title);

            // 重建对照区
            RtbCompare.Document.Blocks.Clear();
            RtbCompare.Document.PagePadding = new Thickness(0);
            _charRuns.Clear();

            var para = new Paragraph { Margin = new Thickness(0), Padding = new Thickness(0) };
            foreach (var ch in _session.TypeText)
            {
                var run = new Run(ch.ToString()) { Foreground = BrushDefault };
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
            TbxInput.Clear();
            TbxInput.Focus();
            UpdateProgress();
            RefreshBmTips();
            ComputeAndShowTheoryMc();
            _chartWin?.Reset();
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
        }

        // ===== 词组下划线 =====

        private void ApplyPhraseUnderlines()
        {
            if (TogMark == null || TogMark.IsChecked != true) return;
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
            // 详细关闭同时隐藏曲线窗
            if (!show && _chartWin != null && _chartWin.IsVisible)
                _chartWin.Hide();
        }

        // ===== 重打 (F3) =====
        private void MenuItem_Repeat_Click(object sender, RoutedEventArgs e) => Repeat();

        private void Repeat()
        {
            if (_session.TypeText.Length == 0) return;
            // 重载当前文章（重置 session 但保留原文）
            LoadArticle(_session.TypeText, _session.Title);
        }

        // ===== 复位 =====清空当前文章、输入区、历史不动
        // ===== 发文 =====
        private void MenuItem_OpenSendText_Click(object sender, RoutedEventArgs e) => OpenSendTextWindow();

        private void MenuItem_LoadClipboard_Click(object sender, RoutedEventArgs e) => LoadFromClipboard();

        /// <summary>F4 载文：从剪贴板拉一段文字直接载入对照区（不走发文窗口）。</summary>
        private void LoadFromClipboard()
        {
            try
            {
                string raw = System.Windows.Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    HandyControl.Controls.Growl.Warning("剪贴板为空");
                    return;
                }
                string text = TextProcessor.TickBlock(raw);
                if (text.Length == 0)
                {
                    HandyControl.Controls.Growl.Warning("剪贴板内容剔除空格后为空");
                    return;
                }
                LoadArticle(text, "来自剪切板");
                HandyControl.Controls.Growl.Info($"已载入 {text.Length} 字");
            }
            catch (Exception ex)
            {
                HandyControl.Controls.Growl.Error("载文失败：" + ex.Message);
            }
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
                SendNext();   // 立即发第一段
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
            string title = $"{_sending.State.Title} · 第 {curSeg} 段";
            LoadArticle(seg, title);
            // 段尺动态游标（发文后才显示）
            SegRulerBox.Visibility = Visibility.Visible;
            TxtCurSeg.Text = curSeg.ToString();
        }
        private void MnuSmartCi_Click(object sender, RoutedEventArgs e)
        {
            if (MnuSmartCi.IsChecked != true)
            {
                TxtTheoryMc.Text = "-";
                HandyControl.Controls.Growl.Info("智能测词已关闭");
                return;
            }
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
        private const string PROJECT_URL  = "https://github.com/lwl4613615/zhouzhou-typing";

        private void MenuItem_Hotkeys_Click(object sender, RoutedEventArgs e)
        {
            HandyControl.Controls.MessageBox.Show(
                "F2  打开发文窗口\n" +
                "F3  重打当前段（全局）\n" +
                "F4  载文（剪贴板内容直接进对照区）\n" +
                "F5  复位\n" +
                "F6  乱序重抽\n\n" +
                "暂停：菜单 → 暂停（输入框失焦也会自动暂停）\n" +
                "继续：回到输入框敲任意键自动继续",
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

        private void MenuItem_About_Click(object sender, RoutedEventArgs e)
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0";
            string msg =
                "州州跟打器 v" + ver + "\n\n" +
                "现代 WPF 跟打练习器，基于 taliove/tygdq 重写。\n" +
                "已砍掉所有 QQ / 比赛 / 检查更新功能。\n\n" +
                "项目主页：" + PROJECT_URL + "\n" +
                "QQ 群：  " + QQ_GROUP_ID + "\n" +
                "加群链接：" + QQ_GROUP_URL + "\n" +
                "作者：    4613615@qq.com\n" +
                "协议：    Apache-2.0";
            HandyControl.Controls.MessageBox.Show(msg, "关于 州州跟打器");
        }

        private void MenuItem_Reset_Click(object sender, RoutedEventArgs e)
        {
            _session.Load(string.Empty, string.Empty);
            RtbCompare.Document.Blocks.Clear();
            _charRuns.Clear();
            this.Title = "州州跟打器";
            TxtTitle.Text = "-";
            TxtWordCount.Text = "0/0字";
            ResetUi();
            TbxInput.IsReadOnly = true;
            TbxInput.Clear();
            UpdateProgress();
            RefreshBmTips();
            if (TxtTheoryMc != null) TxtTheoryMc.Text = "-";
            _chartWin?.Reset();
            HandyControl.Controls.Growl.Info("已复位");
        }

        // ===== 暂停 / 继续（与原版一致：菜单点 或 输入框失焦则暂停；敢一个键自动继续）=====
        private DateTime _pauseStart;
        private bool _isPaused;
        private readonly DispatcherTimer _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private bool _flashOn;
        private static readonly Brush PauseFlashBrush = new SolidColorBrush(Color.FromRgb(0xCD, 0x5C, 0x5C));
        private static readonly Brush NormalTimeBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));

        private void MenuItem_Pause_Click(object sender, RoutedEventArgs e) => PauseType();

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

        private void KeyHook_KeyDown(object sender, int vk)
        {
            // 全局热键（不依赖输入框焦点）
            // F2 发文 / F3 重打 / F5 复位 / F6 发文及换文
            switch (vk)
            {
                case 0x71: // F2 打开发文窗口
                    Dispatcher.BeginInvoke(new Action(OpenSendTextWindow));
                    return;
                case 0x73: // F4 载文（拉剪贴板直接进对照区）
                    Dispatcher.BeginInvoke(new Action(LoadFromClipboard));
                    return;
                case 0x72: // F3 重打
                    Dispatcher.BeginInvoke(new Action(Repeat));
                    return;
                case 0x74: // F5 复位
                    Dispatcher.BeginInvoke(new Action(() => MenuItem_Reset_Click(null, null)));
                    return;
                case 0x75: // F6 乱序重抽
                    Dispatcher.BeginInvoke(new Action(SendShuffle));
                    return;
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

            // 左右手字母区（与原版一致）。
            if ((vk >= 65 && vk <= 71) || (vk >= 81 && vk <= 84) || vk == 88 || vk == 90)
                _session.LeftHand++;
            else if ((vk >= 72 && vk <= 80) || vk == 85 || vk == 89)
                _session.RightHand++;
        }

        private void TbxInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.F3)
            {
                Repeat();
                e.Handled = true;
                return;
            }
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

            // 第一次有字符 -> 启动计时
            if (!_session.Started && input.Length > 0)
            {
                _session.Started = true;
                _session.StartTime = DateTime.Now;
                _timerTime.Start();
                _timerStats.Start();
            }

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
            for (int i = 0; i < len; i++)
            {
                var run = _charRuns[i];
                if (input[i] == _session.TypeText[i])
                {
                    run.Foreground = BrushRight;
                    run.Background = BrushRightBg;
                }
                else
                {
                    run.Foreground = BrushWrong;
                    run.Background = BrushWrongBg;
                    cz++;
                }
            }
            for (int i = len; i < _charRuns.Count; i++)
            {
                _charRuns[i].Foreground = BrushDefault;
                _charRuns[i].Background = null;
            }
            _session.Cz = cz;
            TxtCz.Text = cz.ToString();
            TxtWordCount.Text = $"{len}/{_session.TypeText.Length}字";

            // 段内事件
            if (_session.Started && len != _session.LastInputLen)
                _session.AppendEvent(len);

            // 编码提示 + 进度条刷新
            UpdateProgress();
            RefreshBmTips();

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

            // 速度曲线采样（仅当窗口打开 + 已开始）
            if (_chartWin != null && _chartWin.IsVisible && _session.Started)
            {
                int len = TbxInput.Text?.Length ?? 0;
                var (speed, _, _, sec) = _session.ComputeStats(len);
                if (sec > 0) _chartWin.AddPoint(sec, speed);
            }
        }

        private void UpdateStatsDisplay()
        {
            int len = TbxInput.Text?.Length ?? 0;
            var (speed, jj, mc, _) = _session.ComputeStats(len);
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

            UpdateStatsDisplay();
            UpdateProgress();
            RefreshBmTips();

            var (speed, jj, mc, sec) = _session.ComputeStats(total);
            _historyIndex++;
            History.Insert(0, new HistoryRow
            {
                Index   = _historyIndex,
                Time    = DateTime.Now.ToString("HH:mm:ss"),
                Seg     = "1",
                Speed   = Math.Round(speed, 2),
                Jj      = Math.Round(jj, 2),
                Mc      = Math.Round(mc, 2),
                Hg      = _session.Hg,
                Cz      = _session.Cz,
                Js      = _session.Keys,
                Words   = total,
                DaCi    = 0,
                UseTime = Math.Round(sec, 2),
            });

            HandyControl.Controls.Growl.Success(
                $"完成！速度 {speed:0.00} | 击键 {jj:0.00} | 码长 {mc:0.00} | 用时 {sec:0.00}s\n" +
                $"错字 {_session.Cz} | 回改 {_session.Hg} | 键数 {_session.Keys} | 左:右 {_session.LeftHand}:{_session.RightHand}");

            _chartWin?.MarkFinish();
        }
    }
}
