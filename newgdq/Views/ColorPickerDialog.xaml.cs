using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace newgdq.Views
{
    /// <summary>
    /// WPF-UI 风格的拾色器弹窗，替代系统原生 WinForms ColorDialog。
    /// 大预览 + RGB 滑块 + HEX 输入 + 常用色板，三者实时联动。
    /// 用法：var dlg = new ColorPickerDialog(initialColor){ Owner = this };
    ///       if (dlg.ShowDialog() == true) { var c = dlg.SelectedColor; }
    /// </summary>
    public partial class ColorPickerDialog : FluentWindow
    {
        public Color SelectedColor { get; private set; }

        private bool _sync; // 防止滑块/HEX 互相触发死循环

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

            SldR.ValueChanged += (s, e) => OnSliderChanged();
            SldG.ValueChanged += (s, e) => OnSliderChanged();
            SldB.ValueChanged += (s, e) => OnSliderChanged();
            TxtHex.TextChanged += (s, e) => OnHexChanged();

            SetColor(initial);
        }

        private void SetColor(Color c)
        {
            _sync = true;
            SldR.Value = c.R;
            SldG.Value = c.G;
            SldB.Value = c.B;
            LblR.Text = c.R.ToString();
            LblG.Text = c.G.ToString();
            LblB.Text = c.B.ToString();
            TxtHex.Text = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            PvwBig.Background = new SolidColorBrush(c);
            SelectedColor = c;
            _sync = false;
        }

        private void OnSliderChanged()
        {
            if (_sync) return;
            var c = Color.FromRgb((byte)SldR.Value, (byte)SldG.Value, (byte)SldB.Value);
            SetColor(c);
        }

        private void OnHexChanged()
        {
            if (_sync) return;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(TxtHex.Text);
                SetColor(Color.FromRgb(c.R, c.G, c.B));
            }
            catch { /* 输入未完成时忽略 */ }
        }

        private void Swatch_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is SolidColorBrush b)
                SetColor(b.Color);
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
