using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace newgdq.Views
{
    /// <summary>
    /// WPF-UI 风格的拾色器弹窗，替代系统原生 WinForms ColorDialog。
    /// SV 色谱方块 + Hue 竖条 + RGB 滑块 + HEX 输入 + 常用色板，多者实时联动。
    /// 用法：var dlg = new ColorPickerDialog(initialColor){ Owner = this };
    ///       if (dlg.ShowDialog() == true) { var c = dlg.SelectedColor; }
    /// </summary>
    public partial class ColorPickerDialog : FluentWindow
    {
        public Color SelectedColor { get; private set; }

        private bool _sync; // 防止滑块/HEX/色谱互相触发死循环
        private double _h;   // 色相 0..360
        private double _s;   // 饱和度 0..1
        private double _v;   // 明度 0..1

        private static readonly string[] Palette =
        {
            "#000000","#424242","#757575","#9E9E9E","#BDBDBD","#E0E0E0","#FFFFFF",
            "#B71C1C","#E53935","#FF5252","#FF8A80","#F4511E","#FB8C00","#FFB300",
            "#FDD835","#C0CA33","#7CB342","#43A047","#00897B","#00ACC1","#039BE5",
            "#1E88E5","#3949AB","#5E35B1","#8E24AA","#D81B60","#6D4C41","#546E7A",
            "#00E676","#7CF6C9","#4FC3F7","#FFD24C","#E8EEF6","#262B33","#1E1E1E",
        };

        public ColorPickerDialog(Color initial)
        {
            InitializeComponent();
            SelectedColor = initial;

            var items = new List<SolidColorBrush>();
            foreach (var hex in Palette)
            {
                try { items.Add(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))); }
                catch { }
            }
            Swatches.ItemsSource = items;

            SldR.ValueChanged += (s, e) => OnRgbSliderChanged();
            SldG.ValueChanged += (s, e) => OnRgbSliderChanged();
            SldB.ValueChanged += (s, e) => OnRgbSliderChanged();
            TxtHex.TextChanged += (s, e) => OnHexChanged();

            SvPanel.SizeChanged += (s, e) => UpdateCursors();
            HuePanel.SizeChanged += (s, e) => UpdateCursors();

            RgbToHsv(initial);
            ApplyFromHsv(updateRgb: true, updateHex: true);
        }

        // ---------- 颜色模型转换 ----------
        private void RgbToHsv(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double d = max - min;
            _v = max;
            _s = max <= 0 ? 0 : d / max;
            if (d <= 0) { _h = 0; }
            else if (max == r) { _h = 60 * (((g - b) / d) % 6); }
            else if (max == g) { _h = 60 * (((b - r) / d) + 2); }
            else { _h = 60 * (((r - g) / d) + 4); }
            if (_h < 0) _h += 360;
        }

        private static Color HsvToRgb(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs(((h / 60) % 2) - 1));
            double m = v - c;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromRgb(
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }

        // ---------- 由当前 HSV 刷新所有 UI ----------
        private void ApplyFromHsv(bool updateRgb, bool updateHex)
        {
            var c = HsvToRgb(_h, _s, _v);
            SelectedColor = c;
            _sync = true;

            SvHueLayer.Fill = new SolidColorBrush(HsvToRgb(_h, 1, 1));
            PvwBig.Background = new SolidColorBrush(c);

            if (updateRgb) { SldR.Value = c.R; SldG.Value = c.G; SldB.Value = c.B; }
            LblR.Text = c.R.ToString();
            LblG.Text = c.G.ToString();
            LblB.Text = c.B.ToString();
            if (updateHex)
                TxtHex.Text = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

            _sync = false;
            UpdateCursors();
        }

        private void UpdateCursors()
        {
            if (SvPanel.ActualWidth > 0 && SvPanel.ActualHeight > 0)
            {
                double x = _s * SvPanel.ActualWidth;
                double y = (1 - _v) * SvPanel.ActualHeight;
                SvCursor.Margin = new Thickness(x - SvCursor.Width / 2, y - SvCursor.Height / 2, 0, 0);
            }
            if (HuePanel.ActualHeight > 0)
            {
                double y = (_h / 360.0) * HuePanel.ActualHeight;
                HueCursor.Margin = new Thickness(0, y - HueCursor.Height / 2, 0, 0);
            }
        }

        // ---------- 交互 ----------
        private void SvPanel_Mouse(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(SvPanel);
            _s = Clamp01(p.X / SvPanel.ActualWidth);
            _v = Clamp01(1 - p.Y / SvPanel.ActualHeight);
            if (!SvPanel.IsMouseCaptured) SvPanel.CaptureMouse();
            ApplyFromHsv(updateRgb: true, updateHex: true);
        }

        private void HuePanel_Mouse(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(HuePanel);
            _h = Clamp01(p.Y / HuePanel.ActualHeight) * 360.0;
            if (!HuePanel.IsMouseCaptured) HuePanel.CaptureMouse();
            ApplyFromHsv(updateRgb: true, updateHex: true);
        }

        private void OnRgbSliderChanged()
        {
            if (_sync) return;
            var c = Color.FromRgb((byte)SldR.Value, (byte)SldG.Value, (byte)SldB.Value);
            RgbToHsv(c);
            ApplyFromHsv(updateRgb: false, updateHex: true);
        }

        private void OnHexChanged()
        {
            if (_sync) return;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(TxtHex.Text);
                RgbToHsv(Color.FromRgb(c.R, c.G, c.B));
                ApplyFromHsv(updateRgb: true, updateHex: false);
            }
            catch { /* 输入未完成时忽略 */ }
        }

        private void Swatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is SolidColorBrush b)
            {
                RgbToHsv(b.Color);
                ApplyFromHsv(updateRgb: true, updateHex: true);
            }
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (SvPanel.IsMouseCaptured) SvPanel.ReleaseMouseCapture();
            if (HuePanel.IsMouseCaptured) HuePanel.ReleaseMouseCapture();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
