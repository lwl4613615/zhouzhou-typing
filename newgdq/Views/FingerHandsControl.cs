using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace newgdq.Views
{
    /// <summary>十指枚举（左手小指 → 右手小指，拇指负责空格不参与练习）。</summary>
    public enum Finger
    {
        LeftPinky, LeftRing, LeftMiddle, LeftIndex,
        RightIndex, RightMiddle, RightRing, RightPinky
    }

    /// <summary>
    /// 矢量简笔双手：用圆角矩形拼出手掌 + 五指，纯代码绘制、跟随主题配色。
    /// 调用 <see cref="Point"/> 让指定手指做"下压回弹"循环动画并高亮。
    /// </summary>
    public sealed class FingerHandsControl : Viewbox
    {
        private const double W = 380, H = 170;

        private readonly Canvas _canvas = new Canvas { Width = W, Height = H };
        private readonly Dictionary<Finger, Rectangle> _fingers = new Dictionary<Finger, Rectangle>();
        private readonly Dictionary<Finger, TranslateTransform> _trans = new Dictionary<Finger, TranslateTransform>();

        private Finger? _active;

        public FingerHandsControl()
        {
            Stretch = Stretch.Uniform;
            StretchDirection = StretchDirection.DownOnly;
            Child = _canvas;
            BuildHands();
        }

        private Brush BaseFill => TryRes("CellBG") ?? new SolidColorBrush(Color.FromRgb(0x23, 0x29, 0x38));
        private Brush BaseStroke => TryRes("GridLine") ?? new SolidColorBrush(Color.FromRgb(0x2A, 0x33, 0x46));
        private Brush Accent => TryRes("AccentFG") ?? new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x4C));

        private Brush TryRes(string key)
        {
            try { return TryFindResource(key) as Brush; } catch { return null; }
        }

        private void BuildHands()
        {
            _canvas.Children.Clear();
            _fingers.Clear();
            _trans.Clear();

            // 左手手掌（在底部），四指竖立 + 拇指斜向内
            DrawPalm(20, 95, 150, 60);
            // 左手四指：小指→食指（食指靠中间）。tipY 越小越长。
            AddFinger(Finger.LeftPinky,  34,  58, 22, 84);
            AddFinger(Finger.LeftRing,   62,  40, 24, 102);
            AddFinger(Finger.LeftMiddle, 92,  30, 24, 112);
            AddFinger(Finger.LeftIndex,  122, 46, 24, 96);

            // 右手手掌
            DrawPalm(210, 95, 150, 60);
            // 右手四指：食指（靠中间）→小指
            AddFinger(Finger.RightIndex,  234, 46, 24, 96);
            AddFinger(Finger.RightMiddle, 264, 30, 24, 112);
            AddFinger(Finger.RightRing,   294, 40, 24, 102);
            AddFinger(Finger.RightPinky,  324, 58, 22, 84);
        }

        private void DrawPalm(double x, double y, double w, double h)
        {
            var palm = new Rectangle
            {
                Width = w,
                Height = h,
                RadiusX = 22,
                RadiusY = 22,
                Fill = BaseFill,
                Stroke = BaseStroke,
                StrokeThickness = 1.5
            };
            Canvas.SetLeft(palm, x);
            Canvas.SetTop(palm, y);
            _canvas.Children.Add(palm);
        }

        private void AddFinger(Finger f, double x, double tipY, double w, double h)
        {
            var rect = new Rectangle
            {
                Width = w,
                Height = h,
                RadiusX = w / 2,
                RadiusY = w / 2,
                Fill = BaseFill,
                Stroke = BaseStroke,
                StrokeThickness = 1.5
            };
            var t = new TranslateTransform();
            rect.RenderTransform = t;
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, tipY);
            _canvas.Children.Add(rect);
            _fingers[f] = rect;
            _trans[f] = t;
        }

        /// <summary>高亮并循环按压指定手指；传 null 复位全部。</summary>
        public void Point(Finger? finger)
        {
            // 复位上一根
            if (_active.HasValue && _fingers.TryGetValue(_active.Value, out var prev))
            {
                prev.Fill = BaseFill;
                prev.Stroke = BaseStroke;
                _trans[_active.Value].BeginAnimation(TranslateTransform.YProperty, null);
                _trans[_active.Value].Y = 0;
            }
            _active = finger;
            if (!finger.HasValue) return;

            if (_fingers.TryGetValue(finger.Value, out var rect))
            {
                rect.Fill = Accent;
                rect.Stroke = Accent;
                var anim = new DoubleAnimation
                {
                    From = 0,
                    To = 14,
                    Duration = TimeSpan.FromMilliseconds(420),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                _trans[finger.Value].BeginAnimation(TranslateTransform.YProperty, anim);
            }
        }

        /// <summary>主题切换后刷新画笔。</summary>
        public void RefreshTheme()
        {
            foreach (var kv in _fingers)
            {
                if (_active.HasValue && kv.Key == _active.Value)
                {
                    kv.Value.Fill = Accent;
                    kv.Value.Stroke = Accent;
                }
                else
                {
                    kv.Value.Fill = BaseFill;
                    kv.Value.Stroke = BaseStroke;
                }
            }
        }

        // ===== 键 → 手指 标准指法映射 =====
        private static readonly Dictionary<char, Finger> KeyFinger = BuildMap();

        private static Dictionary<char, Finger> BuildMap()
        {
            var m = new Dictionary<char, Finger>();
            void Set(string keys, Finger f) { foreach (var c in keys) m[c] = f; }
            Set("qaz", Finger.LeftPinky);
            Set("wsx", Finger.LeftRing);
            Set("edc", Finger.LeftMiddle);
            Set("rfvtgb", Finger.LeftIndex);
            Set("yhnujm", Finger.RightIndex);
            Set("ik", Finger.RightMiddle);
            Set("ol", Finger.RightRing);
            Set("p", Finger.RightPinky);
            return m;
        }

        public static bool TryGetFinger(char key, out Finger f)
            => KeyFinger.TryGetValue(char.ToLower(key), out f);

        public static string FingerName(Finger f)
        {
            switch (f)
            {
                case Finger.LeftPinky:   return "左手小指";
                case Finger.LeftRing:    return "左手无名指";
                case Finger.LeftMiddle:  return "左手中指";
                case Finger.LeftIndex:   return "左手食指";
                case Finger.RightIndex:  return "右手食指";
                case Finger.RightMiddle: return "右手中指";
                case Finger.RightRing:   return "右手无名指";
                case Finger.RightPinky:  return "右手小指";
                default: return "";
            }
        }
    }
}
