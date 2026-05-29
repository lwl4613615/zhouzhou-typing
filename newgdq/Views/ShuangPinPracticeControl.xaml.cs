using System;
using System.Collections.Generic;
using System.Linq;
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

            // 恢复上次方案
            var saved = SettingsService.Instance.ShuangPinScheme;
            _suppressEvents = true;
            CmbScheme.SelectedIndex = saved == "Wubi" ? 2 : (saved == "Ziranma" ? 1 : 0);
            _suppressEvents = false;
            if (CmbScheme.SelectedIndex == 2)
            {
                _scheme = ShuangPinScheme.Create(ShuangPinKind.Xiaohe); // 占位，五笔不使用
                ApplyWubi();
            }
            else
            {
                ApplyScheme(CmbScheme.SelectedIndex == 1 ? ShuangPinKind.Ziranma : ShuangPinKind.Xiaohe);
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

            // 加权填充：每个练习项按 最终权重 放入多份，再整体打乱。
            // 最终权重 = 固定难度权重 × (1 + 实时错误率因子)，保证别扭键/易错键多练，
            // 同时每项至少 1 份 → 全键位仍然全覆盖。
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

        /// <summary>最终权重 = 固定难度 × (1 + 错误率因子)，范围约 1~15。</summary>
        private int FinalWeight(char key)
        {
            int diff = DifficultyWeight.TryGetValue(key, out var d) ? d : 1;
            _attempts.TryGetValue(key, out int a);
            _errors.TryGetValue(key, out int e);
            // 没练过 → 错误率因子取中性 0.5；练过 → 用真实错误率
            double rate = a == 0 ? 0.5 : (double)e / a;
            // 因子 0~4：错误率越高加得越多
            double factor = 1.0 + rate * 4.0;
            int w = (int)Math.Round(diff * factor);
            return w < 1 ? 1 : w;
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

        // ===== 出题 / 判定 =====

        private void NextItem()
        {
            if (!_wubi && _mode == ModeSimple) { NextChar(); return; }
            ClearKeyHighlights();
            if (_pool.Count == 0) { _current = null; TxtPrompt.Text = "—"; return; }
            if (_bag.Count == 0) RefillBag();
            _current = _bag.Dequeue();
            TxtPromptKind.Text = _wubi ? "五笔字根" : (_current.IsInitial ? "声母" : "韵母");
            TxtPrompt.Text = _current.Token;
            UpdateHint();
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
            TxtPromptKind.Text = "简单字 · " + _curChar.Pinyin;
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
                if (_step == 0)
                {
                    // 立即推进到第二键，避免按得太快时仍按第一键判定
                    _step = 1;
                    UpdateCharHint();
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
                FlashKey(pressed, false, null);
            }
            UpdateStats();
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
            if (!TryMapKey(e.Key, out char c)) return;
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
                FlashKey(_current.Key, true, NextItem);
            }
            else
            {
                _bad++; _streak = 0;
                _errors[target] = (_errors.TryGetValue(target, out var er) ? er : 0) + 1;
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
            TxtAcc.Text = total == 0 ? "100%" : ($"{_ok * 100 / total}%");
            UpdateWeakKeys();
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
            _ok = _bad = _streak = 0;
            _attempts.Clear();
            _errors.Clear();
            _bag.Clear();      // 清空牌堆，按重置后的权重重新发牌
            _charBag.Clear();
            UpdateStats();
            NextItem();
            FocusForInput();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
            => BackRequested?.Invoke(this, EventArgs.Empty);
    }
}
