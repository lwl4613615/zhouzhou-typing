using System;
using System.Windows;
using System.Windows.Documents;

namespace newgdq.Views
{
    /// <summary>关于窗口：项目信息 + 联系州州（按钮展开微信二维码 / 复制微信号）。</summary>
    public partial class AboutWindow : HandyControl.Controls.Window
    {
        private const string PROJECT_URL = "https://github.com/lwl4613615/zhouzhou-typing";
        private const string WECHAT_ID   = "synhxb";

        public AboutWindow(Window owner)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0";
            TxtTitle.Text = "州州跟打器 v" + ver;
            TxtProjectUrl.Text = PROJECT_URL;
        }

        private void LnkProject_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(PROJECT_URL); }
            catch (Exception ex) { HandyControl.Controls.Growl.Error(ex.Message); }
        }

        private void BtnContact_Click(object sender, RoutedEventArgs e)
        {
            // 切换二维码显示
            if (ImgQr.Visibility == Visibility.Visible)
            {
                ImgQr.Visibility = Visibility.Collapsed;
                BtnContact.Content = "显示微信二维码";
            }
            else
            {
                ImgQr.Visibility = Visibility.Visible;
                BtnContact.Content = "隐藏微信二维码";
            }
        }

        private void BtnCopyWeChat_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (newgdq.Services.ClipboardHelper.TrySetText(WECHAT_ID))
                    HandyControl.Controls.Growl.Success("已复制微信号：" + WECHAT_ID);
                else
                    HandyControl.Controls.Growl.Warning("剪贴板被其他程序占用，请稍后再试");
            }
            catch (Exception ex) { HandyControl.Controls.Growl.Error(ex.Message); }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
