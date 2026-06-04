using System.Windows;

namespace newgdq.Views
{
    /// <summary>
    /// 群比赛设置窗：云地址持久化到 settings.json；昵称（显示名）落盘 AppSettings.CloudNickname。
    /// 设备身份只读展示（DeviceIdentity 明文 deviceId）；当前文章模式只读展示。
    /// </summary>
    public partial class CloudMatchWindow : Wpf.Ui.Controls.FluentWindow
    {
        public CloudMatchWindow(Window owner = null)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;

            var s = Services.SettingsService.Instance;
            TbxUrl.Text       = s.CloudUrl ?? string.Empty;
            TbxNickname.Text  = Services.CloudMatchService.Nickname ?? string.Empty;
            TbxDeviceId.Text  = Services.DeviceIdentity.GetDeviceId();
            TxtCurrentMode.Text = DescribeMode(Services.CloudMatchService.CurrentArticleMode);
        }

        private static string DescribeMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
                return "未抓文（按 F4 抓比赛文后这里显示当前模式）";
            if (string.Equals(mode, "daily", System.StringComparison.OrdinalIgnoreCase))
                return "每日文（daily）— 可反复跟打，不断刷新最好成绩";
            return "赛文（match）— 一场只能交一次";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var s = Services.SettingsService.Instance;
            string url = (TbxUrl.Text ?? string.Empty).Trim();
            // 非空时校验必须 https，防止明文 http 被中间人窃听/篡改
            if (url.Length > 0 &&
                (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri)
                 || !uri.Scheme.Equals("https", System.StringComparison.OrdinalIgnoreCase)))
            {
                Services.Toast.Warning("云地址必须以 https:// 开头", 3);
                return;
            }
            s.CloudUrl        = url;
            Services.SettingsService.Save();

            // 昵称：显示名，落盘 AppSettings.CloudNickname
            Services.CloudMatchService.Nickname = (TbxNickname.Text ?? string.Empty).Trim();

            Services.Toast.Success("群比赛设置已保存", 2);
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
