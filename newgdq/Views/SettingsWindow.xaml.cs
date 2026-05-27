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
    public partial class SettingsWindow : HandyControl.Controls.Window
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

            // 存储 Tab
            TxtConfigPath.Text = SettingsService.FilePath;
            bool isLight = string.Equals(s.ThemeName, "Light", StringComparison.OrdinalIgnoreCase);
            RdoThemeLight.IsChecked = isLight;
            RdoThemeDark.IsChecked  = !isLight;
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

            string oldTheme = string.IsNullOrEmpty(s.ThemeName) ? "Dark" : s.ThemeName;
            s.ThemeName = RdoThemeLight.IsChecked == true ? "Light" : "Dark";

            _owner.ApplyAppearance();
            SettingsService.Save();
            if (!string.Equals(oldTheme, s.ThemeName, StringComparison.OrdinalIgnoreCase))
            {
                HandyControl.Controls.Growl.Info("主题已保存，重启程序后完全生效");
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
