using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace newgdq.Services
{
    /// <summary>
    /// 全局界面缩放管理器（高分屏 UI 偏小时整体放大字体/控件）。
    ///
    /// 做法：对每个 <see cref="Window"/> 的内容根 (Window.Content) 套一个 <see cref="ScaleTransform"/>
    /// 的 LayoutTransform，使其字体、控件按比例等比放大。通过 EventManager.RegisterClassHandler
    /// 一次性钩住所有窗口（含 HandyControl 的 hc:Window，因其派生自 Window），无需逐个改 XAML。
    ///
    /// 多屏边界：调整窗口尺寸/位置时，用窗口当前所在屏幕的工作区（System.Windows.Forms.Screen.FromHandle）
    /// 而非主屏，且把设备像素按该窗口实际 DPI 换算成 WPF 的 DIP，避免在副屏 / 高 DPI 屏上被裁切或越界。
    /// </summary>
    public static class UiScaleManager
    {
        public const double Min  = 1.0;
        public const double Max  = 2.5;
        public const double Step = 0.25;

        private static double _scale = 1.0;
        public static double Scale => _scale;

        /// <summary>缩放变化后触发（参数：新倍数）。用于刷新菜单勾选等。</summary>
        public static event Action<double> ScaleChanged;

        // 跟踪当前打开的窗口（弱引用，窗口关闭即可被回收）
        private static readonly List<WeakReference<Window>> _tracked = new List<WeakReference<Window>>();

        // 附加属性：是否由管理器代为按比例调整窗口尺寸。
        // 主窗口自己持久化几何（保存的就是当前缩放下的尺寸），设为 false 不让管理器在加载时再缩放尺寸。
        public static readonly DependencyProperty ManageSizeProperty =
            DependencyProperty.RegisterAttached("ManageSize", typeof(bool), typeof(UiScaleManager),
                new PropertyMetadata(true));

        public static void SetManageSize(Window w, bool v) => w.SetValue(ManageSizeProperty, v);
        public static bool GetManageSize(Window w) => (bool)w.GetValue(ManageSizeProperty);

        // 附加属性：该窗口相对全局倍数的额外系数（默认 1.0）。
        // 例如跟打状态浮窗设 0.8 → 实际缩放 = 全局倍数 × 0.8，比主窗口小一圈。
        public static readonly DependencyProperty ScaleFactorProperty =
            DependencyProperty.RegisterAttached("ScaleFactor", typeof(double), typeof(UiScaleManager),
                new PropertyMetadata(1.0));

        public static void SetScaleFactor(Window w, double v) => w.SetValue(ScaleFactorProperty, v);
        public static double GetScaleFactor(Window w) => (double)w.GetValue(ScaleFactorProperty);

        // 子窗体默认相对系数：所有非主窗口默认比主窗口小一圈（主窗口最大）。
        // 个别窗口可通过 SetScaleFactor 显式覆盖（如更小的浮窗设 0.8）。
        public const double ChildScale = 0.85;

        /// <summary>某窗口的实际缩放倍数 = 全局倍数 × 该窗口系数。
        /// 主窗口恒为 1.0 系数（最大）；其它窗口未显式设置时用 ChildScale。</summary>
        private static double EffectiveScale(Window w)
        {
            if (w is MainWindow) return _scale;
            double f = GetScaleFactor(w);
            if (Math.Abs(f - 1.0) < 0.0001) f = ChildScale; // 未显式覆盖 → 用子窗体默认系数
            return _scale * f;
        }

        /// <summary>程序启动时调用一次：确定初始倍数并注册全局窗口钩子。</summary>
        public static void Initialize()
        {
            var s = SettingsService.Instance;
            _scale = Clamp(s.UiScale ?? AutoRecommend());
            s.UiScale = _scale;

            EventManager.RegisterClassHandler(typeof(Window),
                FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
            EventManager.RegisterClassHandler(typeof(Window),
                UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel), true);
            EventManager.RegisterClassHandler(typeof(Window),
                UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnKey), true);
        }

        /// <summary>设置全局缩放倍数，并对所有已打开窗口生效。</summary>
        public static void SetScale(double newScale)
        {
            newScale = Clamp(newScale);
            if (Math.Abs(newScale - _scale) < 0.001) return;

            double old = _scale;
            _scale = newScale;
            SettingsService.Instance.UiScale = newScale;

            double ratio = old > 0 ? newScale / old : 1.0;
            foreach (var w in LiveWindows())
            {
                ApplyTransform(w);
                // 运行时改倍数：Manual 尺寸窗口按比例放大/缩小；SizeToContent 窗口会自动重测量。
                if (w.SizeToContent == SizeToContent.Manual
                    && !double.IsNaN(w.Width) && !double.IsNaN(w.Height))
                {
                    w.Width  *= ratio;
                    w.Height *= ratio;
                }
                ClampToScreen(w);
            }

            ScaleChanged?.Invoke(newScale);
        }

        public static void StepUp()   => SetScale(_scale + Step);
        public static void StepDown() => SetScale(_scale - Step);

        // ===== 窗口加载钩子 =====

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            var w = sender as Window;
            if (w == null) return;
            Track(w);

            ApplyTransform(w);

            // 新打开的窗口此刻是 100% 设计尺寸：Manual 尺寸 + 允许管理 → 放大到 设计×scale。
            // 主窗口 ManageSize=false（自己恢复的几何已是当前缩放下的尺寸），不在此二次缩放。
            if (GetManageSize(w) && w.SizeToContent == SizeToContent.Manual
                && !double.IsNaN(w.Width) && !double.IsNaN(w.Height))
            {
                double eff = EffectiveScale(w);
                w.Width  *= eff;
                w.Height *= eff;
            }
            ClampToScreen(w);
        }

        private static void Track(Window w)
        {
            _tracked.RemoveAll(r => !r.TryGetTarget(out var t) || ReferenceEquals(t, w));
            _tracked.Add(new WeakReference<Window>(w));
        }

        private static List<Window> LiveWindows()
        {
            var list = new List<Window>();
            _tracked.RemoveAll(r => !r.TryGetTarget(out _));
            foreach (var r in _tracked)
                if (r.TryGetTarget(out var w)) list.Add(w);
            return list;
        }

        private static void ApplyTransform(Window w)
        {
            if (!(w.Content is FrameworkElement root)) return;
            if (!(root.LayoutTransform is ScaleTransform st))
            {
                st = new ScaleTransform();
                root.LayoutTransform = st;
            }
            double eff = EffectiveScale(w);
            st.ScaleX = eff;
            st.ScaleY = eff;
        }

        // ===== Ctrl + 滚轮 / Ctrl + 加减号 =====

        private static void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            SetScale(_scale + (e.Delta > 0 ? Step : -Step));
            e.Handled = true;
        }

        private static void OnKey(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (e.Key == Key.OemPlus || e.Key == Key.Add)
            { SetScale(_scale + Step); e.Handled = true; }
            else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
            { SetScale(_scale - Step); e.Handled = true; }
        }

        // ===== 多屏 / 边界 =====

        /// <summary>把窗口尺寸限制在其当前所在屏幕的工作区内，越界则回拉，保证不超出可视范围。</summary>
        private static void ClampToScreen(Window w)
        {
            if (w.WindowState != WindowState.Normal) return;
            var wa = GetWorkAreaDip(w);
            if (wa.Width <= 0 || wa.Height <= 0) return;

            if (!double.IsNaN(w.Width)  && w.Width  > wa.Width)  w.Width  = wa.Width;
            if (!double.IsNaN(w.Height) && w.Height > wa.Height) w.Height = wa.Height;

            double width  = double.IsNaN(w.Width)  ? w.ActualWidth  : w.Width;
            double height = double.IsNaN(w.Height) ? w.ActualHeight : w.Height;

            if (!double.IsNaN(w.Left))
            {
                if (w.Left + width > wa.Right)  w.Left = wa.Right - width;
                if (w.Left < wa.Left)           w.Left = wa.Left;
            }
            if (!double.IsNaN(w.Top))
            {
                if (w.Top + height > wa.Bottom) w.Top = wa.Bottom - height;
                if (w.Top < wa.Top)             w.Top = wa.Top;
            }
        }

        /// <summary>窗口当前所在屏幕的工作区（DIP）。多屏下按窗口句柄定位屏幕，并按该窗口 DPI 换算。</summary>
        private static Rect GetWorkAreaDip(Window w)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(w).Handle;
                var screen = hwnd != IntPtr.Zero
                    ? System.Windows.Forms.Screen.FromHandle(hwnd)
                    : System.Windows.Forms.Screen.PrimaryScreen;
                var wa = screen.WorkingArea; // 设备像素

                var src = PresentationSource.FromVisual(w);
                double dx = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                double dy = src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
                if (dx <= 0) dx = 1.0;
                if (dy <= 0) dy = 1.0;

                return new Rect(wa.X / dx, wa.Y / dy, wa.Width / dx, wa.Height / dy);
            }
            catch
            {
                var a = SystemParameters.WorkArea;
                return new Rect(a.Left, a.Top, a.Width, a.Height);
            }
        }

        // ===== 自动推荐 =====

        /// <summary>首次启动：仅当系统缩放=100%（WPF 未自动放大）时，按主屏物理分辨率推荐倍数。</summary>
        private static double AutoRecommend()
        {
            try
            {
                var primary = System.Windows.Forms.Screen.PrimaryScreen;
                int w = primary.Bounds.Width;
                // 系统 DPI（设备像素 / DIP）。系统已放大时 WPF 自身会处理，避免叠加。
                double dpiScale = 1.0;
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                    dpiScale = g.DpiX / 96.0;

                if (dpiScale <= 1.01)
                {
                    if (w >= 3800) return 2.0;   // 4K
                    if (w >= 2500) return 1.5;   // 2.5K / 1440p
                    if (w >= 1900) return 1.25;  // 大屏 1080p
                }
            }
            catch { }
            return 1.0;
        }

        private static double Clamp(double v)
        {
            if (v < Min) v = Min;
            if (v > Max) v = Max;
            return Math.Round(v * 20) / 20.0; // 量化到 0.05
        }
    }
}
