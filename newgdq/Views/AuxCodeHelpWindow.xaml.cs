using System.Windows;

namespace newgdq.Views
{
    /// <summary>自然码辅助码说明窗口（只读说明，菜单 帮助 → 自然码辅助码说明）。</summary>
    public partial class AuxCodeHelpWindow : Wpf.Ui.Controls.FluentWindow
    {
        public AuxCodeHelpWindow(Window owner)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
