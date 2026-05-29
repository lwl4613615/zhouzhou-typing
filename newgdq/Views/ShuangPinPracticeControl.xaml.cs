using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        private List<DrillItem> _pool = new List<DrillItem>();
        private readonly Queue<DrillItem> _bag = new Queue<DrillItem>();  // 洗牌发牌队列，保证均匀覆盖
        private DrillItem _current;
        private readonly Random _rng = new Random();

        private int _ok, _bad, _streak;
        private bool _suppressEvents;  // 初始化期间避免 SelectionChanged 重入

        private readonly DispatcherTimer _flash = new DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(170) };
        private char _flashKey;

        public ShuangPinPracticeControl()
        {
            InitializeComponent();
            _flash.Tick += Flash_Tick;
            BuildKeyboard();

            // 恢复上次方案
            var saved = SettingsService.Instance.ShuangPinScheme;
            _suppressEvents = true;
            CmbScheme.SelectedIndex = saved == "Ziranma" ? 1 : 0;
            _suppressEvents = false;
            ApplyScheme(CmbScheme.SelectedIndex == 1 ? ShuangPinKind.Ziranma : ShuangPinKind.Xiaohe);

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
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("ValueFG"),
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
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
                Width = 62,
                Height = 54,
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
                kv.Value.Text = _scheme.LabelFor(kv.Key);
        }

        // ===== 方案 / 范围 =====

        private void ApplyScheme(ShuangPinKind kind)
        {
            _scheme = ShuangPinScheme.Create(kind);
            SettingsService.Instance.ShuangPinScheme = kind == ShuangPinKind.Ziranma ? "Ziranma" : "Xiaohe";
            try { SettingsService.Save(); } catch { }
            RefreshKeyLabels();
            RebuildPool();
            NextItem();
        }

        private void RebuildPool()
        {
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
            var shuffled = _pool.ToList();
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                var t = shuffled[i]; shuffled[i] = shuffled[j]; shuffled[j] = t;
            }
            // 池子>1 时，若新一轮首项与刚出的题相同，把它和后面某项交换，避免连续重复
            if (shuffled.Count > 1 && _current != null && ReferenceEquals(shuffled[0], _current))
            {
                int swap = 1 + _rng.Next(shuffled.Count - 1);
                var t = shuffled[0]; shuffled[0] = shuffled[swap]; shuffled[swap] = t;
            }
            foreach (var d in shuffled) _bag.Enqueue(d);
        }

        private void CmbScheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;
            ApplyScheme(CmbScheme.SelectedIndex == 1 ? ShuangPinKind.Ziranma : ShuangPinKind.Xiaohe);
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
            ClearKeyHighlights();
            if (_pool.Count == 0) { _current = null; TxtPrompt.Text = "—"; return; }
            if (_bag.Count == 0) RefillBag();
            _current = _bag.Dequeue();
            TxtPromptKind.Text = _current.IsInitial ? "声母" : "韵母";
            TxtPrompt.Text = _current.Token;
            UpdateHint();
        }

        private void UpdateHint()
        {
            if (_current == null) { TxtHint.Text = " "; Hands.Point(null); return; }
            bool hasFinger = FingerHandsControl.TryGetFinger(_current.Key, out var finger);
            bool showHint = ChkHint.IsChecked == true;
            if (showHint)
            {
                TxtHint.Text = hasFinger
                    ? $"按下  {char.ToUpper(_current.Key)}   ·   {FingerHandsControl.FingerName(finger)}"
                    : "按下  " + char.ToUpper(_current.Key);
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
            if (_current == null) return;
            if (!TryMapKey(e.Key, out char c)) return;
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
            if (correct)
            {
                _ok++; _streak++;
                FlashKey(_current.Key, true);
            }
            else
            {
                _bad++; _streak = 0;
                FlashKey(pressed, false);
            }
            UpdateStats();
        }

        private void FlashKey(char key, bool ok)
        {
            _flashKey = key;
            if (_keyBorders.TryGetValue(key, out var b))
                b.Background = ok ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                                  : new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
            _flash.Tag = ok;
            _flash.Stop();
            _flash.Start();
        }

        private void Flash_Tick(object sender, EventArgs e)
        {
            _flash.Stop();
            bool ok = _flash.Tag is bool b2 && b2;
            if (_keyBorders.TryGetValue(_flashKey, out var b))
                b.Background = (Brush)FindResource("CellBG");
            if (ok) NextItem();  // 答对后才换题；答错保留当前题继续尝试
        }

        private void UpdateStats()
        {
            TxtOk.Text = _ok.ToString();
            TxtBad.Text = _bad.ToString();
            TxtStreak.Text = _streak.ToString();
            int total = _ok + _bad;
            TxtAcc.Text = total == 0 ? "100%" : ($"{_ok * 100 / total}%");
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            _ok = _bad = _streak = 0;
            UpdateStats();
            NextItem();
            FocusForInput();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
            => BackRequested?.Invoke(this, EventArgs.Empty);
    }
}
