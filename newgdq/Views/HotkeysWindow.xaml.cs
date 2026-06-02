using System.Windows;

namespace newgdq.Views
{
    public partial class HotkeysWindow : Wpf.Ui.Controls.FluentWindow
    {
        public HotkeysWindow(Window owner = null)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
