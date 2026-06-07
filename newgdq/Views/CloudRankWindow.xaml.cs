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
            // bug14/Bug4：人数文案用云端 totalAll（全量人数）；旧云端无该字段时回退 total。
            int totalAll = result.TotalAll > 0 ? result.TotalAll : result.Total;
            int shown = result.Rows?.Count ?? 0;
            TxtTotal.Text = totalAll > shown
                ? $"共 {totalAll} 人，显示前 {shown} 名"
                : $"共 {totalAll} 人";

            var rows = result.Rows;
            bool empty = rows == null || rows.Count == 0;
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
