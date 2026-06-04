using System;
using System.Globalization;
using System.Windows;
using newgdq.Services;

namespace newgdq.Views
{
    /// <summary>云成绩弹窗：展示本次五项成绩 + 登记名 + 名次 + 模式文案。
    /// 数据全部由调用方（wire-finish-flow 段）传入，本控件不发任何云请求。
    /// "看榜单"按钮仅抛出 <see cref="OnViewRank"/> 事件，由宿主处理拉 /rank（见 cscore-wire-rank-entry）。</summary>
    public partial class CloudScoreCard : Wpf.Ui.Controls.FluentWindow
    {
        /// <summary>宿主订阅此回调以处理"看榜单"点击（本控件不实现拉榜逻辑，避免重复触发云请求）。</summary>
        public Action OnViewRank;

        /// <param name="speed">速度 字/分</param>
        /// <param name="jj">击键 /s</param>
        /// <param name="mc">码长 /字</param>
        /// <param name="cz">错字</param>
        /// <param name="useTime">用时（秒）</param>
        /// <param name="result">上传结果（match 成功 / 重复交卷 / daily 各分支）</param>
        /// <param name="rank">本次名次；null 表示未上榜 / 未知</param>
        /// <param name="dailyOverLimit">daily 当日重打次数是否已达上限（429）。
        /// UploadResult 现有字段无法区分 429，由 wire 段以此标志传入，本控件不臆造字段。</param>
        public CloudScoreCard(double speed, double jj, double mc, int cz, double useTime,
                              CloudMatchService.UploadResult result, int? rank,
                              bool dailyOverLimit = false)
        {
            InitializeComponent();

            TxtSpeed.Text    = speed.ToString("0.00", CultureInfo.InvariantCulture);
            TxtJj.Text       = jj.ToString("0.00", CultureInfo.InvariantCulture);
            TxtMc.Text       = mc.ToString("0.00", CultureInfo.InvariantCulture);
            TxtCz.Text       = cz.ToString(CultureInfo.InvariantCulture);
            TxtUseTime.Text  = useTime.ToString("0.0", CultureInfo.InvariantCulture) + "s";

            TxtName.Text = string.IsNullOrWhiteSpace(result.Name) ? "—" : result.Name;
            TxtRank.Text = rank.HasValue ? "第 " + rank.Value + " 名" : "—";

            RenderMode(result, rank, dailyOverLimit);
        }

        private void RenderMode(CloudMatchService.UploadResult result, int? rank, bool dailyOverLimit)
        {
            string title;
            string msg;

            if (dailyOverLimit)
            {
                // 429：调用方传入"超上限"标志时显示
                title = "今日已达上限";
                msg   = "今日重打次数已达上限";
            }
            else if (result.IsDuplicate)
            {
                // match 重复交卷（409）：Name 可能为空，名次显示 —
                title = "已交卷";
                msg   = "本场你已交过卷了（一场只能交一次）";
                TxtRank.Text = "—";
            }
            else if (!result.IsDaily)
            {
                // match 成功
                title = "已交卷";
                msg   = "成绩已登记，当前名次 " + (rank.HasValue ? "第 " + rank.Value + " 名" : "—");
            }
            else if (result.Improved)
            {
                if (!result.Old.HasValue)
                {
                    // daily 首次成绩
                    title = "首次成绩";
                    msg   = "首次成绩已记录" + (result.Best.HasValue ? "（最好 " + Fmt(result.Best) + "）" : "");
                }
                else
                {
                    // daily 刷新最好
                    title = "刷新最好！";
                    msg   = "刷新最好！旧 " + Fmt(result.Old) + " → 新 " + Fmt(result.New)
                          + (result.Best.HasValue ? "（历史最好 " + Fmt(result.Best) + "）" : "");
                }
            }
            else
            {
                // daily 未超越
                title = "未超越";
                msg   = "未超越，历史最好仍为 " + (result.Best.HasValue ? Fmt(result.Best) : "—");
            }

            TxtTitle.Text = title;
            TxtMsg.Text   = msg;
        }

        private static string Fmt(double? v) =>
            v.HasValue ? v.Value.ToString("0.00", CultureInfo.InvariantCulture) : "—";

        private void BtnViewRank_Click(object sender, RoutedEventArgs e)
        {
            OnViewRank?.Invoke();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
