using System.Windows;
using newgdq.Services;

namespace newgdq.Views
{
    public partial class CloudRankWindow : Wpf.Ui.Controls.FluentWindow
    {
        public CloudRankWindow(CloudMatchService.RankResult result, Window owner = null)
        {
            InitializeComponent();
            if (owner != null) Owner = owner;
            Bind(result);
        }

        private void Bind(CloudMatchService.RankResult result)
        {
            string title = string.IsNullOrEmpty(result.Title) ? "比赛榜单" : result.Title;
            Title = title;
            Bar.Title = title;
            TxtTitle.Text = title;
            TxtTotal.Text = $"共 {result.Total} 人";

            var rows = result.Rows;
            bool empty = rows == null || rows.Count == 0 || result.Total == 0;
            if (empty)
            {
                DgvRank.ItemsSource = null;
                DgvRank.Visibility = Visibility.Collapsed;
                TxtEmpty.Visibility = Visibility.Visible;
            }
            else
            {
                DgvRank.ItemsSource = rows;
                DgvRank.Visibility = Visibility.Visible;
                TxtEmpty.Visibility = Visibility.Collapsed;
            }

            if (string.IsNullOrEmpty(result.Ad))
            {
                AdBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtAd.Text = result.Ad;
                AdBorder.Visibility = Visibility.Visible;
            }
        }

        public static void Show(CloudMatchService.RankResult result, Window owner = null)
        {
            new CloudRankWindow(result, owner).Show();
        }
    }
}
