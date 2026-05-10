using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using newgdq.Models;
using newgdq.Services;

namespace newgdq.Views
{
    public partial class BmTipsWindow : Window
    {
        private readonly Window _owner;

        // 不同重数的颜色（与原版 Glob.BmColors 一致）
        private static readonly Brush[] RankColors =
        {
            Brushes.LightSkyBlue,    // 1 重
            Brushes.LightCoral,      // 2 重
            Brushes.MediumPurple,    // 3 重
            Brushes.HotPink,         // 4 重+
        };

        public BmTipsWindow(Window owner)
        {
            InitializeComponent();
            _owner = owner;
            Owner = owner;
            owner.LocationChanged += (s, e) => UpdatePosition();
            owner.SizeChanged     += (s, e) => UpdatePosition();
            UpdatePosition();
        }

        /// <summary>贴在主窗口正下方，居中。</summary>
        private void UpdatePosition()
        {
            if (_owner == null) return;
            Left = _owner.Left + (_owner.Width - Width) / 2;
            Top  = _owner.Top + _owner.Height + 4;
        }

        /// <summary>
        /// 显示从 text[startIndex] 开始往后 maxChars 个字符的编码提示。
        /// </summary>
        public void ShowTips(DictionaryService dict, string text, int startIndex, int maxChars = 12)
        {
            TxtContent.Inlines.Clear();
            if (dict == null || !dict.Loaded || string.IsNullOrEmpty(text) || startIndex >= text.Length)
            {
                TxtContent.Inlines.Add(new Run("-"));
                return;
            }

            int i = startIndex;
            int shown = 0;
            int end = System.Math.Min(text.Length, startIndex + maxChars);

            while (i < end && shown < maxChars)
            {
                var entry = dict.MatchAt(text, i);
                if (entry == null)
                {
                    TxtContent.Inlines.Add(new Run(text[i] + "? ") { Foreground = Brushes.Gray });
                    i++;
                    shown++;
                    continue;
                }

                Brush color = RankColors[System.Math.Min(entry.Rank - 1, RankColors.Length - 1)];
                TxtContent.Inlines.Add(new Run(entry.Word) { Foreground = color, FontWeight = FontWeights.Bold });
                TxtContent.Inlines.Add(new Run(entry.Code) { Foreground = Brushes.LightGray });
                if (entry.Rank > 1)
                    TxtContent.Inlines.Add(new Run(entry.Rank.ToString()) { Foreground = Brushes.Orange, FontSize = 10 });
                TxtContent.Inlines.Add(new Run("  "));
                i += entry.Word.Length;
                shown += entry.Word.Length;
            }
        }
    }
}
