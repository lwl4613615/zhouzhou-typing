using System.Windows;

namespace newgdq.Views
{
    /// <summary>
    /// F4 抓比赛文时弹出的"本场口令"输入框。口令每场都换，现场填最合理。
    /// 预填上次填过的口令方便连打同场；确定后写回 SettingsService.SessionToken。
    /// </summary>
    public partial class TokenPromptWindow : Wpf.Ui.Controls.FluentWindow
    {
        /// <summary>用户确认后输入的口令（取消时为 null）。</summary>
        public string Token { get; private set; }

        public TokenPromptWindow(Window owner, string prefill)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            TbxToken.Text = prefill ?? string.Empty;
            Loaded += (s, e) => { TbxToken.Focus(); TbxToken.SelectAll(); };
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            string t = (TbxToken.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t))
            {
                Services.Toast.Warning("请先填本场口令", 2);
                return;
            }
            Token = t;
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
