using System.Windows;

namespace newgdq.Views
{
    /// <summary>
    /// 群比赛设置窗：云地址 / 本场口令 持久化到 settings.json；
    /// 个人码只写进内存（CloudMatchService.PersonalCode），绝不落地。
    /// </summary>
    public partial class CloudMatchWindow : Wpf.Ui.Controls.FluentWindow
    {
        public CloudMatchWindow(Window owner = null)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;

            var s = Services.SettingsService.Instance;
            TbxUrl.Text   = s.CloudUrl ?? string.Empty;
            TbxCode.Text  = Services.CloudMatchService.PersonalCode ?? string.Empty;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var s = Services.SettingsService.Instance;
            s.CloudUrl        = (TbxUrl.Text ?? string.Empty).Trim();
            Services.SettingsService.Save();

            // 个人码：只进内存，不持久化
            Services.CloudMatchService.PersonalCode = (TbxCode.Text ?? string.Empty).Trim();

            Services.Toast.Success("群比赛设置已保存（个人码仅本次有效）", 2);
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
