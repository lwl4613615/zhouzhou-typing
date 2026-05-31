using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using newgdq.Services;

namespace newgdq.Views
{
    /// <summary>
    /// 双拼键位练习面板（内嵌 MainWindow，非独立窗口）。
    /// 随机出题（声母 zh/ch/sh + 韵母），用户按对应键，实时高亮 + 统计。
    /// </summary>
    public partial class ShuangPinPracticeControl : UserControl
    {
        /// <summary>用户点击"返回跟打"时触发。</summary>
        public event EventHandler BackRequested;

        private static readonly char[] Row1 = "qwertyuiop".ToCharArray();
        private static readonly char[] Row2 = "asdfghjkl".ToCharArray();
        private static readonly char[] Row3 = "zxcvbnm".ToCharArray();

        private readonly Dictionary<char, Border> _keyBorders = new Dictionary<char, Border>();
        private readonly Dictionary<char, TextBlock> _keyLetters = new Dictionary<char, TextBlock>();
        private readonly Dictionary<char, TextBlock> _keyLabels = new Dictionary<char, TextBlock>();

        private ShuangPinScheme _scheme;
        private int _mode;            // 0=混合 1=仅声母 2=仅韵母
        private bool _wubi;           // true=五笔字根模式（不走双拼方案）
        private List<DrillItem> _pool = new List<DrillItem>();
        private readonly Queue<DrillItem> _bag = new Queue<DrillItem>();  // 洗牌发牌队列，保证均匀覆盖
        private DrillItem _current;
        private readonly Random _rng = new Random();

        private int _ok, _bad, _streak;
        private bool _suppressEvents;  // 初始化期间避免 SelectionChanged 重入

        // ===== 速度 / 反应时间统计 =====
        private readonly Stopwatch _reactSw = new Stopwatch();   // 当前目标键出现起计时
        private readonly Stopwatch _sessionSw = new Stopwatch(); // 本次练习累计用时（首次按键开始）
        private long _reactSumMs;     // 答对键的反应时间累计
        private int _reactCount;      // 答对键计数（= 反应样本数）
        private bool _soundOn = true; // 对/错系统提示音

        // 自适应加权：每个键的累计尝试 / 错误次数（内存统计，不持久化）
        private readonly Dictionary<char, int> _attempts = new Dictionary<char, int>();
        private readonly Dictionary<char, int> _errors   = new Dictionary<char, int>();

        // 固定难度权重：小指/边角键先天别扭，权重高（无实战数据时的兜底）
        private static readonly Dictionary<char, int> DifficultyWeight = BuildDifficulty();

        private static Dictionary<char, int> BuildDifficulty()
        {
            var m = new Dictionary<char, int>();
            void Set(string keys, int w) { foreach (var c in keys) m[c] = w; }
            foreach (var c in "abcdefghijklmnopqrstuvwxyz") m[c] = 1; // 默认
            Set("pqz", 3);  // 小指 + 边角，最别扭
            Set("ml", 3);   // 右小指/右无名边角
            Set("awx", 2);  // 无名指上下伸
            Set("o", 2);    // 右无名上排
            return m;
        }

        private readonly DispatcherTimer _flash = new DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(170) };
        private char _flashKey;
        private Action _afterFlash;   // 闪烁结束后要做的动作（换题 / 进入下一键）

        // ===== 简单字模式 =====
        private const int ModeSimple = 3;
        private List<SimpleChar> _charPool = new List<SimpleChar>();
        private readonly Queue<SimpleChar> _charBag = new Queue<SimpleChar>();
        private SimpleChar _curChar;
        private char[] _curCode;   // 当前字的两键
        private int _step;         // 0=第一键 1=第二键

        public ShuangPinPracticeControl()
        {
            InitializeComponent();
            _flash.Tick += Flash_Tick;
            BuildKeyboard();

            // 恢复上次方案（未保存过时默认自然码）
            var saved = SettingsService.Instance.ShuangPinScheme;
            _suppressEvents = true;
            CmbScheme.SelectedIndex = saved == "Wubi" ? 2 : (saved == "Xiaohe" ? 0 : 1);
            _soundOn = SettingsService.Instance.PracticeSoundOn ?? true;
            ChkSound.IsChecked = _soundOn;
            // 恢复范围 + 提示键开关
            int savedMode = SettingsService.Instance.PracticeMode ?? 0;
            if (savedMode < 0 || savedMode > 3) savedMode = 0;
            _mode = savedMode;
            CmbMode.SelectedIndex = savedMode;
            ChkHint.IsChecked = SettingsService.Instance.PracticeHint ?? true;
            _suppressEvents = false;
            if (CmbScheme.SelectedIndex == 2)
            {
                _scheme = ShuangPinScheme.Create(ShuangPinKind.Xiaohe); // 占位，五笔不使用
                ApplyWubi();
            }
            else
            {
                ApplyScheme(CmbScheme.SelectedIndex == 0 ? ShuangPinKind.Xiaohe : ShuangPinKind.Ziranma);
            }

            this.Loaded += (s, e) => FocusForInput();
        }

        /// <summary>让面板获得键盘焦点，开始接收按键。</summary>
        public void FocusForInput()
        {
            Keyboard.Focus(this);
            this.Focus();
        }

        // ===== 键盘渲染 =====

        private void BuildKeyboard()
        {
            KeyboardHost.Children.Clear();
            _keyBorders.Clear(); _keyLetters.Clear(); _keyLabels.Clear();
            AddRow(Row1, 0);
            AddRow(Row2, 26);   // 第二行整体右移半键
            AddRow(Row3, 52);
        }

        private void AddRow(char[] keys, double leftPad)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(leftPad, 4, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            foreach (var k in keys)
                panel.Children.Add(MakeKey(k));
            KeyboardHost.Children.Add(panel);
        }

        private Border MakeKey(char k)
        {
            var letter = new TextBlock
            {
                Text = char.ToUpper(k).ToString(),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("LabelFG"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 1)
            };
            var label = new TextBlock
            {
                Text = string.Empty,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("ValueFG"),
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                LineHeight = 13,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(2, 9, 2, 2)
            };
            var grid = new Grid();
            grid.Children.Add(label);
            var corner = new TextBlock
            {
                Text = char.ToUpper(k).ToString(),
                FontSize = 11,
                Foreground = (Brush)FindResource("LabelFG"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(3, 1, 0, 0)
            };
            grid.Children.Add(corner);

            var border = new Border
            {
                Width = 66,
                Height = 60,
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(5),
                Background = (Brush)FindResource("CellBG"),
                BorderBrush = (Brush)FindResource("GridLine"),
                BorderThickness = new Thickness(1),
                Child = grid
            };

            _keyBorders[k] = border;
            _keyLetters[k] = corner;
            _keyLabels[k] = label;
            return border;
        }

        private void RefreshKeyLabels()
        {
            foreach (var kv in _keyLabels)
            {
                var tb = kv.Value;
                if (_wubi)
                {
                    tb.Inlines.Clear();
                    var wk = WubiRadicalTable.Get(kv.Key);
                    if (wk == null) { tb.Text = string.Empty; continue; }
                    var nameBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // 键名=绿
                    var spBrush = (Brush)FindResource("AccentFG");                        // 特殊=强调色
                    var normal = (Brush)FindResource("ValueFG");
                    bool first = true;
                    foreach (var r in wk.Radicals)
                    {
                        if (!first) tb.Inlines.Add(new Run(" "));
                        first = false;
                        var run = new Run(r);
                        if (r == wk.Name) run.Foreground = nameBrush;
                        else if (wk.IsSpecial(r)) { run.Foreground = spBrush; run.FontWeight = FontWeights.Bold; }
                        else run.Foreground = normal;
                        tb.Inlines.Add(run);
                    }
                }
                else
                {
                    tb.Text = _scheme.LabelFor(kv.Key);
                }
            }
        }

        // ===== 方案 / 范围 =====

        private void ApplyScheme(ShuangPinKind kind)
        {
            _wubi = false;
            CmbMode.IsEnabled = true;
            _scheme = ShuangPinScheme.Create(kind);
            SettingsService.Instance.ShuangPinScheme = kind == ShuangPinKind.Ziranma ? "Ziranma" : "Xiaohe";
            try { SettingsService.Save(); } catch { }
            RefreshKeyLabels();
            RebuildPool();
            NextItem();
        }

        /// <summary>切换到五笔字根练习：键面显示字根，出题=字根，按其所在键。</summary>
        private void ApplyWubi()
        {
            _wubi = true;
            _mode = 0;                 // 五笔不使用双拼范围
            CmbMode.IsEnabled = false; // 混合/仅声母/仅韵母/简单字 对五笔无意义
            SettingsService.Instance.ShuangPinScheme = "Wubi";
            try { SettingsService.Save(); } catch { }
            RefreshKeyLabels();
            RebuildPool();
            NextItem();
        }

        private void RebuildPool()
        {
            if (_wubi)
            {
                _pool = WubiRadicalTable.BuildDrills().ToList();
                _bag.Clear();
                _charPool = new List<SimpleChar>();
                _charBag.Clear();
                return;
            }
            if (_mode == ModeSimple)
            {
                // 简单字：取本方案能换算的字（含 ü 等无法换算的已自动剔除）
                _charPool = SimpleCharTable.Items
                    .Where(sc => _scheme.TryEncode(sc.Shengmu, sc.Yunmu, out _, out _))
                    .ToList();
                _charBag.Clear();
                _pool = new List<DrillItem>();
                _bag.Clear();
                return;
            }
            IEnumerable<DrillItem> q = _scheme.Drills;
            if (_mode == 1) q = q.Where(d => d.IsInitial);
            else if (_mode == 2) q = q.Where(d => !d.IsInitial);
            _pool = q.ToList();
            _bag.Clear();   // 范围/方案变了重新发牌
        }

        /// <summary>把整池打乱后压入发牌队列；避免新一轮首项与上一题相同。</summary>
        private void RefillBag()
        {
            if (_pool.Count == 0) return;

            // 加权填充：每个练习项按 最终权重(1~3) 放入多份，再整体打乱。
            // 每项至少 1 份 → 全键位每轮都覆盖；最高:最低 ≤ 3:1 → 频率温和，
            // 不会出现某键刷屏、其它键久久不出现。
            var bagList = new List<DrillItem>();
            foreach (var d in _pool)
            {
                int w = FinalWeight(d.Key);
                for (int i = 0; i < w; i++) bagList.Add(d);
            }
            for (int i = bagList.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                var t = bagList[i]; bagList[i] = bagList[j]; bagList[j] = t;
            }
            // 若新一轮首项与刚出的题相同，和后面某项交换，避免连续重复
            if (bagList.Count > 1 && _current != null && ReferenceEquals(bagList[0], _current))
            {
                int swap = 1 + _rng.Next(bagList.Count - 1);
                var t = bagList[0]; bagList[0] = bagList[swap]; bagList[swap] = t;
            }
            foreach (var d in bagList) _bag.Enqueue(d);
        }

        /// <summary>
        /// 出题权重（1~3）。在"每轮每键至少一次"的全覆盖基础上，对别扭键 / 已练够且易错的键
        /// 最多各多给 1 份；最高:最低 ≤ 3:1，避免个别键刷屏、其它键久久不出现。
        /// </summary>
        private int FinalWeight(char key)
        {
            // 难度：原 1~3 → 附加分 0 / 0.5 / 1（别扭键略多练）
            int diff = DifficultyWeight.TryGetValue(key, out var d) ? d : 1;
            double diffBonus = (diff - 1) * 0.5;

            // 错误率：练满 3 次才计，最多再加 1 份（避免单次早期手误就被狂推）
            _attempts.TryGetValue(key, out int a);
            _errors.TryGetValue(key, out int e);
            double errBonus = a >= 3 ? Math.Min(1.0, (double)e / a * 2.0) : 0.0;

            int w = 1 + (int)Math.Round(diffBonus + errBonus);  // 1~3
            return w < 1 ? 1 : (w > 3 ? 3 : w);
        }

        private void CmbScheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;
            if (CmbScheme.SelectedIndex == 2) ApplyWubi();
            else ApplyScheme(CmbScheme.SelectedIndex == 1 ? ShuangPinKind.Ziranma : ShuangPinKind.Xiaohe);
            FocusForInput();
        }

        private void CmbMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents || _scheme == null) return;
            _mode = CmbMode.SelectedIndex < 0 ? 0 : CmbMode.SelectedIndex;
            RebuildPool();
            NextItem();
            FocusForInput();
        }

        private void ChkHint_Changed(object sender, RoutedEventArgs e)
        {
            if (_scheme == null) return;
            UpdateHint();
            FocusForInput();
        }

        private void ChkSound_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;
            _soundOn = ChkSound.IsChecked == true;
            SettingsService.Instance.PracticeSoundOn = _soundOn;
            try { SettingsService.Save(); } catch { }
            FocusForInput();
        }

        // ===== 出题 / 判定 =====

        private void NextItem()
        {
            if (!_wubi && _mode == ModeSimple) { NextChar(); return; }
            ClearKeyHighlights();
            if (_pool.Count == 0) { _current = null; TxtPrompt.Text = "—"; return; }
            var prev = _current;
            DrillItem next = null;
            // 取下一张牌；若与上一题相同则跳过（牌堆内同键有多份，避免相邻重复）。
            // 池中不止一种时最多尝试几次，保证一定能拿到不同的题。
            for (int guard = 0; guard < 8; guard++)
            {
                if (_bag.Count == 0) RefillBag();
                if (_bag.Count == 0) break;
                next = _bag.Dequeue();
                if (prev == null || _pool.Count < 2 || !ReferenceEquals(next, prev)) break;
            }
            _current = next;
            if (_current == null) { TxtPrompt.Text = "—"; return; }
            TxtPromptKind.Text = _wubi ? "五笔字根" : (_current.IsInitial ? "声母" : "韵母");
            TxtPrompt.Text = _current.Token;
            UpdateHint();
            _reactSw.Restart();
        }

        // ===== 简单字出题 / 判定 =====

        private void NextChar()
        {
            ClearKeyHighlights();
            _current = null;
            if (_charPool.Count == 0)
            {
                _curChar = null;
                TxtPromptKind.Text = "简单字";
                TxtPrompt.Text = "—";
                TxtHint.Text = " ";
                Hands.Point(null);
                return;
            }
            if (_charBag.Count == 0) RefillCharBag();
            _curChar = _charBag.Dequeue();
            _scheme.TryEncode(_curChar.Shengmu, _curChar.Yunmu, out char k1, out char k2);
            _curCode = new[] { k1, k2 };
            _step = 0;
            UpdateCharHint();
            _reactSw.Restart();
        }

        private void RefillCharBag()
        {
            if (_charPool.Count == 0) return;
            var list = new List<SimpleChar>(_charPool);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                var t = list[i]; list[i] = list[j]; list[j] = t;
            }
            if (list.Count > 1 && _curChar != null && ReferenceEquals(list[0], _curChar))
            {
                int s = 1 + _rng.Next(list.Count - 1);
                var t = list[0]; list[0] = list[s]; list[s] = t;
            }
            foreach (var c in list) _charBag.Enqueue(c);
        }

        private void UpdateCharHint()
        {
            if (_curChar == null) return;
            // 题面突出读音：多音字额外标注"按此音"，避免用户按另一读音拆键
            bool poly = SimpleCharTable.IsPolyphonic(_curChar.Char);
            TxtPromptKind.Text = poly
                ? $"简单字 · 读音 {_curChar.Pinyin} · 多音字（按此音）"
                : $"简单字 · 读音 {_curChar.Pinyin}";
            TxtPrompt.Text = _curChar.Char;
            char cur = _curCode[_step];
            if (ChkHint.IsChecked == true)
            {
                bool hasFinger = FingerHandsControl.TryGetFinger(cur, out var finger);
                string code = char.ToUpper(_curCode[0]).ToString() + char.ToUpper(_curCode[1]);
                string step = _step == 0 ? "第1键" : "第2键";
                TxtHint.Text = hasFinger
                    ? $"{code}    现在按 {step}  {char.ToUpper(cur)} · {FingerHandsControl.FingerName(finger)}"
                    : $"{code}    现在按 {step}  {char.ToUpper(cur)}";
                HighlightTarget(cur);
                Hands.Point(hasFinger ? finger : (Finger?)null);
            }
            else
            {
                TxtHint.Text = " ";
                ClearKeyHighlights();
                Hands.Point(null);
            }
        }

        private void JudgeChar(char pressed)
        {
            char target = _curCode[_step];
            _attempts[target] = (_attempts.TryGetValue(target, out var a) ? a : 0) + 1;
            if (pressed == target)
            {
                RecordReaction();
                PlaySound(true);
                if (_step == 0)
                {
                    // 立即推进到第二键，避免按得太快时仍按第一键判定
                    _step = 1;
                    UpdateCharHint();
                    _reactSw.Restart();
                    FlashKey(target, true, null);   // 视觉反馈放在刷新之后，绿闪可见
                }
                else
                {
                    _ok++; _streak++;
                    NextChar();                     // 立即换字
                    FlashKey(target, true, null);
                }
            }
            else
            {
                _bad++; _streak = 0;
                _errors[target] = (_errors.TryGetValue(target, out var er) ? er : 0) + 1;
                _step = 0;                          // 答错回到第一键
                UpdateCharHint();
                ShowCharError(pressed);             // 覆盖提示行：给出正解读音 + 应打键，区分"指法错/读音错"
                PlaySound(false);
                FlashKey(pressed, false, null);
            }
            UpdateStats();
        }

        /// <summary>简单字答错时，把提示行替换为"正解"：读音 + 整字应打的两键 + 实际按键，
        /// 让用户一眼分清是"手指按错"还是"按了另一个读音"。</summary>
        private void ShowCharError(char pressed)
        {
            if (_curChar == null) return;
            string code = char.ToUpper(_curCode[0]).ToString() + " " + char.ToUpper(_curCode[1]);
            string poly = SimpleCharTable.IsPolyphonic(_curChar.Char) ? "【多音字】" : "";
            TxtHint.Text = $"{poly}本字读「{_curChar.Pinyin}」→ 应打 {code}，你按了 {char.ToUpper(pressed)}";
        }

        private void UpdateHint()
        {
            if (_current == null) { TxtHint.Text = " "; Hands.Point(null); return; }
            bool hasFinger = FingerHandsControl.TryGetFinger(_current.Key, out var finger);
            bool showHint = ChkHint.IsChecked == true;

            // 五笔：标记特殊字根（题面变强调色 + 类别提示）
            bool special = false;
            if (_wubi)
            {
                special = WubiRadicalTable.IsSpecial(_current.Key, _current.Token);
                TxtPrompt.Foreground = special ? (Brush)FindResource("AccentFG") : (Brush)FindResource("ValueFG");
                TxtPromptKind.Text = special ? "五笔字根 · 特殊字根" : "五笔字根";
            }
            else
            {
                TxtPrompt.Foreground = (Brush)FindResource("ValueFG");
            }

            if (showHint)
            {
                TxtHint.Text = hasFinger
                    ? $"按下  {char.ToUpper(_current.Key)}   ·   {FingerHandsControl.FingerName(finger)}"
                    : "按下  " + char.ToUpper(_current.Key);
                if (_wubi)
                {
                    if (special) TxtHint.Text += "    ★ 特殊字根";
                    var note = WubiRadicalTable.NoteFor(_current.Token);
                    if (note != null) TxtHint.Text += "    （" + note + "）";
                    var wk = WubiRadicalTable.Get(_current.Key);
                    if (wk != null) TxtHint.Text += "    口诀：" + wk.Mnemonic;
                }
                HighlightTarget(_current.Key);
                Hands.Point(hasFinger ? finger : (Finger?)null);
            }
            else
            {
                TxtHint.Text = " ";
                ClearKeyHighlights();
                Hands.Point(null);
            }
        }

        private void HighlightTarget(char key)
        {
            ClearKeyHighlights();
            if (_keyBorders.TryGetValue(key, out var b))
            {
                b.BorderBrush = (Brush)FindResource("AccentFG");
                b.BorderThickness = new Thickness(2);
            }
        }

        private void ClearKeyHighlights()
        {
            foreach (var b in _keyBorders.Values)
            {
                b.BorderBrush = (Brush)FindResource("GridLine");
                b.BorderThickness = new Thickness(1);
                b.Background = (Brush)FindResource("CellBG");
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.IsRepeat) return;            // 按住不放只判定一次
            // 小结卡片展示时：回车/空格关闭，其余按键忽略（不计入练习）
            if (SummaryOverlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Enter || e.Key == Key.Space || e.Key == Key.Escape)
                {
                    e.Handled = true;
                    BtnSummaryClose_Click(this, new RoutedEventArgs());
                }
                else e.Handled = true;
                return;
            }
            if (!TryMapKey(e.Key, out char c)) return;
            if (!_sessionSw.IsRunning) _sessionSw.Start();   // 首次按键开始计时
            if (!_wubi && _mode == ModeSimple)
            {
                if (_curChar == null) return;
                e.Handled = true;
                JudgeChar(c);
                return;
            }
            if (_current == null) return;
            e.Handled = true;
            Judge(c);
        }

        private static bool TryMapKey(Key key, out char c)
        {
            c = '\0';
            if (key >= Key.A && key <= Key.Z)
            {
                c = (char)('a' + (key - Key.A));
                return true;
            }
            return false;
        }

        private void Judge(char pressed)
        {
            bool correct = pressed == _current.Key;
            // 自适应统计：尝试/错误都记在"目标键"上（错误率驱动该键的加权）
            char target = _current.Key;
            _attempts[target] = (_attempts.TryGetValue(target, out var a) ? a : 0) + 1;
            if (correct)
            {
                _ok++; _streak++;
                RecordReaction();
                PlaySound(true);
                FlashKey(_current.Key, true, NextItem);
            }
            else
            {
                _bad++; _streak = 0;
                _errors[target] = (_errors.TryGetValue(target, out var er) ? er : 0) + 1;
                PlaySound(false);
                FlashKey(pressed, false, null);
            }
            UpdateStats();
        }

        private void FlashKey(char key, bool ok, Action after)
        {
            _flashKey = key;
            if (_keyBorders.TryGetValue(key, out var b))
                b.Background = ok ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                                  : new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
            _afterFlash = after;
            _flash.Stop();
            _flash.Start();
        }

        private void Flash_Tick(object sender, EventArgs e)
        {
            _flash.Stop();
            if (_keyBorders.TryGetValue(_flashKey, out var b))
                b.Background = (Brush)FindResource("CellBG");
            var a = _afterFlash; _afterFlash = null;
            a?.Invoke();   // 答对→换题/进入下一键；答错→保留或回到第一键
        }

        private void UpdateStats()
        {
            TxtOk.Text = _ok.ToString();
            TxtBad.Text = _bad.ToString();
            TxtStreak.Text = _streak.ToString();
            int total = _ok + _bad;
            TxtAcc.Text = total == 0 ? "100%" : ($"{(double)_ok * 100.0 / total:0.0}%");
            // 速度：答对键数 / 累计用时（分）
            double min = _sessionSw.Elapsed.TotalMinutes;
            TxtKpm.Text = (min > 0.001 && _reactCount > 0) ? Math.Round(_reactCount / min).ToString() : "0";
            // 平均反应
            TxtReact.Text = _reactCount > 0 ? Math.Round((double)_reactSumMs / _reactCount).ToString() : "—";
            UpdateWeakKeys();
        }

        /// <summary>记录当前目标键的反应时间（仅答对时调用）。</summary>
        private void RecordReaction()
        {
            if (!_reactSw.IsRunning) return;
            long ms = _reactSw.ElapsedMilliseconds;
            _reactSw.Stop();
            if (ms <= 0 || ms > 10000) return;   // 过滤离开/异常样本
            _reactSumMs += ms;
            _reactCount++;
        }

        /// <summary>对/错系统提示音（可在工具条关闭）。</summary>
        private void PlaySound(bool ok)
        {
            if (!_soundOn) return;
            try { if (ok) SystemSounds.Asterisk.Play(); else SystemSounds.Hand.Play(); }
            catch { /* 无音频设备时忽略 */ }
        }

        /// <summary>显示当前错误率最高的前 3 个键（至少练过 1 次）。</summary>
        private void UpdateWeakKeys()
        {
            var weak = _attempts.Keys
                .Where(k => _attempts[k] > 0 && _errors.TryGetValue(k, out var e) && e > 0)
                .OrderByDescending(k => (double)_errors[k] / _attempts[k])
                .ThenByDescending(k => _errors[k])
                .Take(3)
                .Select(k => char.ToUpper(k).ToString())
                .ToList();
            TxtWeak.Text = weak.Count == 0 ? "—" : string.Join(" ", weak);
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            if (ShowSummary("本次练习小结", DoReset)) return;  // 有数据→先展示小结，关闭后再重置
            DoReset();
            FocusForInput();
        }

        private void DoReset()
        {
            _ok = _bad = _streak = 0;
            _attempts.Clear();
            _errors.Clear();
            _bag.Clear();      // 清空牌堆，按重置后的权重重新发牌
            _charBag.Clear();
            _reactSumMs = 0; _reactCount = 0;
            _reactSw.Reset(); _sessionSw.Reset();
            UpdateStats();
            NextItem();
            FocusForInput();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (ShowSummary("练习小结", () => BackRequested?.Invoke(this, EventArgs.Empty))) return;
            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>填充并淡入展示本次练习小结卡片。无练习记录返回 false（调用方直接执行后续动作）。</summary>
        private bool ShowSummary(string title, Action onClose)
        {
            int total = _ok + _bad;
            if (total == 0) return false;   // 没练过，不打扰

            var span = _sessionSw.Elapsed;
            TxtSummaryTitle.Text = title;
            TxtSumDur.Text = span.TotalHours >= 1
                ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
                : $"{span.Minutes:00}:{span.Seconds:00}";
            double min = span.TotalMinutes;
            TxtSumKpm.Text = (min > 0.001 && _reactCount > 0) ? Math.Round(_reactCount / min).ToString() : "0";
            TxtSumReact.Text = _reactCount > 0 ? Math.Round((double)_reactSumMs / _reactCount).ToString() : "—";
            TxtSumOk.Text = _ok.ToString();
            TxtSumBad.Text = _bad.ToString();
            TxtSumAcc.Text = $"{(double)_ok * 100.0 / total:0.0}%";

            var weak = _attempts.Keys
                .Where(k => _attempts[k] > 0 && _errors.TryGetValue(k, out var er) && er > 0)
                .OrderByDescending(k => (double)_errors[k] / _attempts[k])
                .ThenByDescending(k => _errors[k])
                .Take(5)
                .Select(k => char.ToUpper(k).ToString())
                .ToList();
            TxtSumWeak.Text = weak.Count > 0 ? string.Join(" ", weak) : "无";

            _summaryOnClose = onClose;
            SummaryOverlay.Visibility = Visibility.Visible;
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                new Duration(TimeSpan.FromMilliseconds(160)));
            SummaryOverlay.BeginAnimation(OpacityProperty, fade);
            return true;
        }

        private Action _summaryOnClose;

        private void BtnSummaryClose_Click(object sender, RoutedEventArgs e)
        {
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0,
                new Duration(TimeSpan.FromMilliseconds(140)));
            fade.Completed += (s2, e2) =>
            {
                SummaryOverlay.Visibility = Visibility.Collapsed;
                var act = _summaryOnClose; _summaryOnClose = null;
                act?.Invoke();
            };
            SummaryOverlay.BeginAnimation(OpacityProperty, fade);
        }
    }
}
