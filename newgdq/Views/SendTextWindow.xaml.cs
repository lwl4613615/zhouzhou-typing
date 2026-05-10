using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using newgdq.Models;
using newgdq.Services;

namespace newgdq.Views
{
    /// <summary>
    /// 发文设置窗口（4 Tab）。
    /// 选好文章 + 参数 → 点"开启发文" → 关闭窗口 → 主窗口接收并加载第一段。
    /// </summary>
    public partial class SendTextWindow : Window
    {
        /// <summary>窗口关闭时如果用户点了"开启发文"，此处填好待主窗口接收。</summary>
        public SendingState ResultState { get; private set; }

        /// <summary>主窗口启动发文时调用此回调。</summary>
        public Action<SendingState> OnStartSending;

        // 内置文章映射（Header → 资源文件名）
        private static readonly (string Header, string FileName)[] Builtin =
        {
            ("10 字测试",       "10字测试.txt"),
            ("常用单字 前五百", "常用单字前五百.txt"),
            ("常用单字 中五百", "常用单字中五百.txt"),
            ("常用单字 后五百", "常用单字后五百.txt"),
            ("常用词组 前三百", "常用词组前三百.txt"),
            ("岳阳楼记",       "岳阳楼记.txt"),
            ("为人民服务节选", "为人民服务节选.txt"),
            ("前 1500 单字",   "前1500单字.txt"),
        };

        private string _currentText = "";
        private string _currentTitle = "";

        public SendTextWindow()
        {
            InitializeComponent();
            foreach (var b in Builtin) LbxBuiltin.Items.Add(b.Header);
        }

        // ===== Tab 1 自带文章 =====
        private void LbxBuiltin_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = LbxBuiltin.SelectedIndex;
            if (idx < 0 || idx >= Builtin.Length) return;
            try
            {
                var fileName = Builtin[idx].FileName;
                _currentText = ArticleLoader.LoadInternal(fileName);
                _currentTitle = Builtin[idx].Header;
                TxtBuiltinPreview.Text = _currentText.Length > 200
                    ? _currentText.Substring(0, 200) + "..."
                    : _currentText;
                RefreshInfo();
            }
            catch (Exception ex)
            {
                HandyControl.Controls.Growl.Error("载入失败：" + ex.Message);
            }
        }

        // ===== Tab 2 自定义 =====
        private void TbxCustom_TextChanged(object sender, TextChangedEventArgs e)
        {
            _currentText = TbxCustomBody.Text ?? "";
            _currentTitle = string.IsNullOrEmpty(TbxCustomTitle.Text) ? "自定义文章" : TbxCustomTitle.Text;
            RefreshInfo();
        }

        // ===== Tab 3 剪切板 =====
        private void BtnReGet_Click(object sender, RoutedEventArgs e)
        {
            try { TbxClipBody.Text = Clipboard.GetText() ?? ""; }
            catch (Exception ex) { TbxClipBody.Text = "[剪切板读取失败] " + ex.Message; }
        }

        private void BtnTickBlock_Click(object sender, RoutedEventArgs e)
        {
            TbxClipBody.Text = TextProcessor.TickBlock(TbxClipBody.Text);
        }

        private void BtnFillIt_Click(object sender, RoutedEventArgs e)
        {
            TbxClipBody.Text = TextProcessor.FillWith(TbxClipBody.Text, ",");
        }

        private void TbxClip_TextChanged(object sender, TextChangedEventArgs e)
        {
            _currentText = TbxClipBody.Text ?? "";
            _currentTitle = string.IsNullOrEmpty(TbxClipTitle.Text) ? "来自剪切板" : TbxClipTitle.Text;
            RefreshInfo();
        }

        // ===== 信息刷新 =====
        private void RefreshInfo()
        {
            string text = _currentText;
            if (ChkTickOut.IsChecked == true) text = TextProcessor.TickBlock(text);
            LblTitle.Text = _currentTitle;
            LblTextCount.Text = text.Length.ToString();
            bool isArt = TextProcessor.IsArticle(text);
            LblStyle.Text = isArt ? "文章" : "单字";
            // 文章模式下默认顺序更合理
            if (isArt && RbnOutOrder.IsChecked == true) RbnInOrder.IsChecked = true;
        }

        // ===== 数字输入限制 =====
        private void DigitOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (var c in e.Text) if (c < '0' || c > '9') { e.Handled = true; return; }
        }

        // ===== 开启发文 =====
        private void BtnGoSend_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentText))
            {
                HandyControl.Controls.Growl.Warning("请先选择/输入文章");
                return;
            }
            string text = ChkTickOut.IsChecked == true ? TextProcessor.TickBlock(_currentText) : _currentText;
            if (text.Length == 0) { HandyControl.Controls.Growl.Warning("文章为空"); return; }

            if (!int.TryParse(TbxSendCount.Text, out int countPerSeg) || countPerSeg <= 0)
            {
                HandyControl.Controls.Growl.Warning("发送字数无效");
                return;
            }
            int.TryParse(TbxSendStart.Text, out int mark);
            int.TryParse(TbxStartSeg.Text, out int startSeg);
            if (startSeg <= 0) startSeg = 1;
            if (mark < 0) mark = 0;
            if (mark >= text.Length) { HandyControl.Controls.Growl.Warning("起始位置超出范围"); return; }

            var state = new SendingState
            {
                Active         = true,
                FullText       = text,
                PoolText       = text,
                Title          = _currentTitle,
                Type           = TextProcessor.IsArticle(text) ? SendingTextType.Article : SendingTextType.Single,
                IsRandom       = RbnOutOrder.IsChecked == true,
                RandomNoRepeat = ChkNoRepeat.IsChecked == true,
                OneSentenceEnd = ChkOneEnd.IsChecked == true,
                CountPerSeg    = countPerSeg,
                Mark           = mark,
                StartSeg       = startSeg,
            };

            ResultState = state;
            OnStartSending?.Invoke(state);
            this.Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Close();
    }
}
