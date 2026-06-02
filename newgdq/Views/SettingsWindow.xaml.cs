using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using newgdq.Models;
using newgdq.Services;

namespace newgdq.Views
{
    /// <summary>
    /// 设置窗口 —— 字体 / 颜色 / 个签三个 Tab。
    /// "确定" → 写入 SettingsService.Instance + 调 owner.ApplyAppearance() + 持久化。
    /// </summary>
    public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly MainWindow _owner;

        // 默认色（与 MainWindow 内 _brushXxx 同步）
        private const string DefRight    = "#166F16";
        private const string DefRightBg  = "#CCF2CC";
        private const string DefWrong    = "#CC3333";
        private const string DefWrongBg  = "#FFD8D8";
        private const string DefCmpBg    = "#FFFFFF"; // RichTextBox 默认白
        private const string DefInputBg  = "#FFFFFF";

        public SettingsWindow(MainWindow owner)
        {
            InitializeComponent();
            _owner = owner;
            Owner = owner;

            FillFontFamilies();
            LoadFromSettings();
            WirePreviewSync();
        }

        private void FillFontFamilies()
        {
            var names = Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
            CmbCompareFamily.ItemsSource = names;
            CmbInputFamily.ItemsSource   = names;
        }

        private void LoadFromSettings()
        {
            var s = SettingsService.Instance;
            CmbCompareFamily.Text = s.CompareFontFamily ?? "宋体";
            NudCompareSize.Value  = s.CompareFontSize ?? 22;
            CmbInputFamily.Text   = s.InputFontFamily ?? "宋体";
            NudInputSize.Value    = s.InputFontSize ?? 18;

            TxtColorRight.Text     = s.ColorRight     ?? DefRight;
            TxtColorRightBg.Text   = s.ColorRightBg   ?? DefRightBg;
            TxtColorWrong.Text     = s.ColorWrong     ?? DefWrong;
            TxtColorWrongBg.Text   = s.ColorWrongBg   ?? DefWrongBg;
            TxtColorCompareBg.Text = s.ColorCompareBg ?? DefCmpBg;
            TxtColorInputBg.Text   = s.ColorInputBg   ?? DefInputBg;
            UpdateAllPreviews();

            ChkSignEnabled.IsChecked = s.SignEnabled ?? false;
            TxtSignText.Text         = s.SignText ?? string.Empty;
            ChkMinimizeToTray.IsChecked = s.MinimizeToTray ?? false;
            NudAutoRepeat.Value = s.AutoRepeatMinutes ?? 0;
            NudSpeedLimit.Value = s.SpeedLimit ?? 0;
            ChkMergeChord.IsChecked = s.MergeChord ?? true;

            // 存储 Tab
            TxtConfigPath.Text = SettingsService.FilePath;
            if (string.Equals(s.ThemeName, "System", StringComparison.OrdinalIgnoreCase))
                RdoThemeSystem.IsChecked = true;
            else if (string.Equals(s.ThemeName, "Light", StringComparison.OrdinalIgnoreCase))
                RdoThemeLight.IsChecked = true;
            else
                RdoThemeDark.IsChecked = true;
        }

        private void WirePreviewSync()
        {
            TxtColorRight.TextChanged     += (s, e) => UpdatePreview(TxtColorRight,     PvwColorRight);
            TxtColorRightBg.TextChanged   += (s, e) => UpdatePreview(TxtColorRightBg,   PvwColorRightBg);
            TxtColorWrong.TextChanged     += (s, e) => UpdatePreview(TxtColorWrong,     PvwColorWrong);
            TxtColorWrongBg.TextChanged   += (s, e) => UpdatePreview(TxtColorWrongBg,   PvwColorWrongBg);
            TxtColorCompareBg.TextChanged += (s, e) => UpdatePreview(TxtColorCompareBg, PvwColorCompareBg);
            TxtColorInputBg.TextChanged   += (s, e) => UpdatePreview(TxtColorInputBg,   PvwColorInputBg);
        }

        private void UpdateAllPreviews()
        {
            UpdatePreview(TxtColorRight,     PvwColorRight);
            UpdatePreview(TxtColorRightBg,   PvwColorRightBg);
            UpdatePreview(TxtColorWrong,     PvwColorWrong);
            UpdatePreview(TxtColorWrongBg,   PvwColorWrongBg);
            UpdatePreview(TxtColorCompareBg, PvwColorCompareBg);
            UpdatePreview(TxtColorInputBg,   PvwColorInputBg);
        }

        private static void UpdatePreview(TextBox tbx, Border preview)
        {
            try
            {
                var obj = ColorConverter.ConvertFromString(tbx.Text);
                if (obj is Color c) preview.Background = new SolidColorBrush(c);
            }
            catch { preview.Background = Brushes.Transparent; }
        }

        /// <summary>一键配色预设：只填充跟打区 6 个色值，不改整体主题。
        /// 顺序：正确前景 / 正确背景 / 错字前景 / 错字背景 / 对照区背景 / 输入区背景。</summary>
        private void BtnPreset_Click(object sender, RoutedEventArgs e)
        {
            string tag = (sender as Button)?.Tag as string;
            string[] p;
            switch (tag)
            {
                case "eye":      // 护眼：豆沙绿底，柔和前景
                    p = new[] { "#1B5E20", "#D7E8C8", "#B71C1C", "#F0CFC2", "#E8F0D8", "#E8F0D8" };
                    break;
                case "contrast": // 高对比：黑底亮字
                    p = new[] { "#00E676", "#102010", "#FF5252", "#301010", "#101010", "#101010" };
                    break;
                case "dark":     // 暗夜：深灰底
                    p = new[] { "#7CD992", "#1F2A22", "#FF8A80", "#2A1F1F", "#1E1E1E", "#1E1E1E" };
                    break;
                default:         // 默认（与初始一致）
                    p = new[] { DefRight, DefRightBg, DefWrong, DefWrongBg, DefCmpBg, DefInputBg };
                    break;
            }
            TxtColorRight.Text     = p[0];
            TxtColorRightBg.Text   = p[1];
            TxtColorWrong.Text     = p[2];
            TxtColorWrongBg.Text   = p[3];
            TxtColorCompareBg.Text = p[4];
            TxtColorInputBg.Text   = p[5];
            UpdateAllPreviews();
        }

        /// <summary>颜色预览块点击 → 弹系统拾色器（WinForms ColorDialog），把选中的颜色写回对应 TextBox。
        /// XAML 里给 Border 设 Tag="TxtColorRight" 等，按名字反查 TextBox。</summary>
        private void Pvw_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var bd = sender as Border;
            if (bd == null) return;
            var tbxName = bd.Tag as string;
            if (string.IsNullOrEmpty(tbxName)) return;
            var tbx = this.FindName(tbxName) as TextBox;
            if (tbx == null) return;
            try
            {
                Color initial;
                try { initial = (Color)ColorConverter.ConvertFromString(tbx.Text); } catch { initial = Colors.White; }
                var dlg = new ColorPickerDialog(initial) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    var c = dlg.SelectedColor;
                    tbx.Text = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
                }
            }
            catch (Exception ex)
            {
                Services.Toast.Error("拾色器打开失败：" + ex.Message);
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            var s = SettingsService.Instance;
            s.CompareFontFamily = (CmbCompareFamily.Text ?? "宋体").Trim();
            s.CompareFontSize   = NudCompareSize.Value;
            s.InputFontFamily   = (CmbInputFamily.Text ?? "宋体").Trim();
            s.InputFontSize     = NudInputSize.Value;

            s.ColorRight     = TxtColorRight.Text.Trim();
            s.ColorRightBg   = TxtColorRightBg.Text.Trim();
            s.ColorWrong     = TxtColorWrong.Text.Trim();
            s.ColorWrongBg   = TxtColorWrongBg.Text.Trim();
            s.ColorCompareBg = TxtColorCompareBg.Text.Trim();
            s.ColorInputBg   = TxtColorInputBg.Text.Trim();

            s.SignEnabled = ChkSignEnabled.IsChecked == true;
            s.SignText    = TxtSignText.Text ?? string.Empty;
            s.MinimizeToTray = ChkMinimizeToTray.IsChecked == true;
            s.AutoRepeatMinutes = (int?)NudAutoRepeat.Value;
            s.SpeedLimit = NudSpeedLimit.Value;
            s.MergeChord = ChkMergeChord.IsChecked == true;

            string oldTheme = string.IsNullOrEmpty(s.ThemeName) ? "Dark" : s.ThemeName;
            s.ThemeName = RdoThemeSystem.IsChecked == true ? "System"
                        : RdoThemeLight.IsChecked == true ? "Light" : "Dark";

            _owner.ApplyAppearance();
            SettingsService.Save();
            if (!string.Equals(oldTheme, s.ThemeName, StringComparison.OrdinalIgnoreCase))
            {
                Services.Toast.Info("主题已保存，重启程序后完全生效");
            }
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
