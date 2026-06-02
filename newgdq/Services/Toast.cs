using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace newgdq.Services
{
    /// <summary>
    /// 轻量全局气泡提示，替代 HandyControl 的 Growl。
    /// 右上角向下堆叠、自动淡出，无需各窗口放置宿主控件，跨窗口可用。
    /// API 与 Growl 对齐：Success / Info / Warning / Error(string[, seconds])。
    /// </summary>
    public static class Toast
    {
        private const double Width = 340;
        private const double Margin = 16;
        private const double Gap = 8;

        // 当前活动的气泡（用于纵向堆叠定位）
        private static readonly List<ToastWindow> _active = new List<ToastWindow>();

        public static void Success(string message, int seconds = 2) => Show(message, ToastKind.Success, seconds);
        public static void Info(string message, int seconds = 2)    => Show(message, ToastKind.Info, seconds);
        public static void Warning(string message, int seconds = 3) => Show(message, ToastKind.Warning, seconds);
        public static void Error(string message, int seconds = 4)   => Show(message, ToastKind.Error, seconds);

        private enum ToastKind { Success, Info, Warning, Error }

        private static void Show(string message, ToastKind kind, int seconds)
        {
            var app = Application.Current;
            if (app == null) return;
            // 保证在 UI 线程
            if (!app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.BeginInvoke(new Action(() => Show(message, kind, seconds)));
                return;
            }

            var win = new ToastWindow(message, kind, Math.Max(1, seconds));
            win.Closed += (s, e) =>
            {
                _active.Remove(win);
                Relayout();
            };
            _active.Add(win);
            win.Show();
            Relayout();
        }

        private static void Relayout()
        {
            var wa = SystemParameters.WorkArea;
            double left = wa.Right - Width - Margin;
            double y = wa.Top + Margin;
            foreach (var w in _active)
            {
                w.Left = left;
                w.Top = y;
                y += w.ActualHeight > 0 ? w.ActualHeight + Gap : 64;
            }
        }

        private sealed class ToastWindow : Window
        {
            private readonly DispatcherTimer _timer;

            public ToastWindow(string message, ToastKind kind, int seconds)
            {
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                Background = Brushes.Transparent;
                ShowInTaskbar = false;
                Topmost = true;
                ResizeMode = ResizeMode.NoResize;
                SizeToContent = SizeToContent.Height;
                Width = Toast.Width;
                Focusable = false;
                ShowActivated = false;

                Color bar, bg;
                string icon;
                switch (kind)
                {
                    case ToastKind.Success: bar = Color.FromRgb(0x4C, 0xAF, 0x50); icon = "\u2714"; break;
                    case ToastKind.Warning: bar = Color.FromRgb(0xFF, 0xA7, 0x26); icon = "\u26A0"; break;
                    case ToastKind.Error:   bar = Color.FromRgb(0xF4, 0x43, 0x36); icon = "\u2716"; break;
                    default:                bar = Color.FromRgb(0x29, 0x9B, 0xF7); icon = "\u2139"; break;
                }
                bg = Color.FromRgb(0x26, 0x2B, 0x33);

                var border = new Border
                {
                    Background = new SolidColorBrush(bg),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(6),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 14,
                        ShadowDepth = 2,
                        Opacity = 0.45,
                        Color = Colors.Black,
                    },
                };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var accent = new Border
                {
                    Background = new SolidColorBrush(bar),
                    CornerRadius = new CornerRadius(8, 0, 0, 8),
                };
                Grid.SetColumn(accent, 0);
                grid.Children.Add(accent);

                var iconText = new TextBlock
                {
                    Text = icon,
                    Foreground = new SolidColorBrush(bar),
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(12, 12, 0, 12),
                };
                Grid.SetColumn(iconText, 1);
                grid.Children.Add(iconText);

                var msg = new TextBlock
                {
                    Text = message,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF6)),
                    FontSize = 13,
                    FontFamily = new FontFamily("\u5fae\u8f6f\u96c5\u9ed1"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10, 12, 14, 12),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(msg, 2);
                grid.Children.Add(msg);

                border.Child = grid;
                Content = border;

                // 点击立即关闭
                MouseLeftButtonUp += (s, e) => FadeOutAndClose();

                Opacity = 0;
                Loaded += (s, e) =>
                {
                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
                    BeginAnimation(OpacityProperty, fadeIn);
                };

                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
                _timer.Tick += (s, e) => { _timer.Stop(); FadeOutAndClose(); };
                _timer.Start();
            }

            private bool _closing;
            private void FadeOutAndClose()
            {
                if (_closing) return;
                _closing = true;
                var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(220));
                fadeOut.Completed += (s, e) => { try { Close(); } catch { } };
                BeginAnimation(OpacityProperty, fadeOut);
            }
        }
    }
}
