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
            ReloadPresets();
            CmbStyle.SelectedIndex = 0;  // 自动
        }

        /// <summary>当前用户选择的文段类型；返回 null 表示"自动按是否含中文标点判断"。</summary>
        private SendingTextType? GetStyleOverride()
        {
            if (CmbStyle?.SelectedItem is System.Windows.Controls.ComboBoxItem cbi)
            {
                string s = cbi.Content?.ToString() ?? "";
                if (s == "文章") return SendingTextType.Article;
                if (s == "单字") return SendingTextType.Single;
            }
            return null;
        }

        private SendingTextType ResolveStyle(string text)
        {
            return GetStyleOverride() ?? (TextProcessor.IsArticle(text) ? SendingTextType.Article : SendingTextType.Single);
        }

        private void CmbStyle_Changed(object sender, SelectionChangedEventArgs e)
        {
            // 用户切换时立即更新 LblStyle + 顺序/乱序默认
            if (LblStyle == null) return;
            RefreshInfo();
        }

        // ===== Tab 4 发文参数预设 =====

        private void ReloadPresets()
        {
            var s = SettingsService.Instance;
            if (s.SendPresets == null) s.SendPresets = new System.Collections.Generic.List<SendPreset>();
            LbxPresets.ItemsSource = null;
            LbxPresets.ItemsSource = s.SendPresets;
        }

        private void LbxPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(LbxPresets.SelectedItem is SendPreset p)) return;
            TbxPresetName.Text       = p.Name;
            TbxPresetCount.Text      = p.CountPerSeg.ToString();
            TbxPresetStartSeg.Text   = p.StartSeg.ToString();
            TbxPresetMark.Text       = p.Mark.ToString();
            ChkPresetRandom.IsChecked   = p.IsRandom;
            ChkPresetNoRepeat.IsChecked = p.RandomNoRepeat;
            ChkPresetOneEnd.IsChecked   = p.OneSentenceEnd;
            ChkPresetTickOut.IsChecked  = p.TickOut;
        }

        /// <summary>把编辑面板里的字段读出来构建一个 SendPreset。失败返回 null + Growl 提示。</summary>
        private SendPreset BuildPresetFromForm()
        {
            string name = (TbxPresetName.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name)) { HandyControl.Controls.Growl.Warning("请填名称"); return null; }
            if (!int.TryParse(TbxPresetCount.Text, out int count) || count <= 0)
            { HandyControl.Controls.Growl.Warning("每段字数无效"); return null; }
            int.TryParse(TbxPresetStartSeg.Text, out int startSeg); if (startSeg <= 0) startSeg = 1;
            int.TryParse(TbxPresetMark.Text, out int mark);         if (mark < 0)    mark = 0;
            return new SendPreset
            {
                Name = name,
                CountPerSeg = count,
                StartSeg = startSeg,
                Mark = mark,
                IsRandom       = ChkPresetRandom.IsChecked   == true,
                RandomNoRepeat = ChkPresetNoRepeat.IsChecked == true,
                OneSentenceEnd = ChkPresetOneEnd.IsChecked   == true,
                TickOut        = ChkPresetTickOut.IsChecked  == true,
            };
        }

        private void BtnPresetCreate_Click(object sender, RoutedEventArgs e)
        {
            var p = BuildPresetFromForm(); if (p == null) return;
            var list = SettingsService.Instance.SendPresets;
            // 同名提示替换还是新增？默认追加（允许同名）
            list.Add(p);
            SettingsService.Save();
            ReloadPresets();
            LbxPresets.SelectedItem = p;
            HandyControl.Controls.Growl.Success("已保存预设：" + p.Name);
        }

        private void BtnPresetUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (!(LbxPresets.SelectedItem is SendPreset old))
            { HandyControl.Controls.Growl.Info("先在左侧选一个预设"); return; }
            var p = BuildPresetFromForm(); if (p == null) return;
            var list = SettingsService.Instance.SendPresets;
            int idx = list.IndexOf(old);
            if (idx < 0) return;
            list[idx] = p;
            SettingsService.Save();
            ReloadPresets();
            LbxPresets.SelectedItem = p;
            HandyControl.Controls.Growl.Success("已更新：" + p.Name);
        }

        private void BtnPresetDelete_Click(object sender, RoutedEventArgs e)
        {
            if (!(LbxPresets.SelectedItem is SendPreset p))
            { HandyControl.Controls.Growl.Info("先在左侧选一个预设"); return; }
            SettingsService.Instance.SendPresets.Remove(p);
            SettingsService.Save();
            ReloadPresets();
            HandyControl.Controls.Growl.Success("已删除：" + p.Name);
        }

        /// <summary>把选中预设的参数灌到主参数区（Tab 1-3 共用的发文参数输入框）。</summary>
        private void BtnPresetApply_Click(object sender, RoutedEventArgs e)
        {
            if (!(LbxPresets.SelectedItem is SendPreset p))
            { HandyControl.Controls.Growl.Info("先在左侧选一个预设"); return; }
            TbxSendCount.Text  = p.CountPerSeg.ToString();
            TbxStartSeg.Text   = p.StartSeg.ToString();
            TbxSendStart.Text  = p.Mark.ToString();
            RbnOutOrder.IsChecked = p.IsRandom;
            RbnInOrder.IsChecked  = !p.IsRandom;
            ChkNoRepeat.IsChecked = p.RandomNoRepeat;
            ChkOneEnd.IsChecked   = p.OneSentenceEnd;
            ChkTickOut.IsChecked  = p.TickOut;
            HandyControl.Controls.Growl.Success("已套用：" + p.Name + "（参数已填入面板，回到前面 Tab 检查或直接 开启发文）");
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
            if (TbxCustomBody == null || TbxCustomTitle == null) return;  // XAML 加载中
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
            if (TbxClipBody == null || TbxClipTitle == null) return;
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
            var style = ResolveStyle(text);
            LblStyle.Text = style == SendingTextType.Article ? "文章" : "单字";
            // 文章模式下默认顺序更合理
            if (style == SendingTextType.Article && RbnOutOrder.IsChecked == true) RbnInOrder.IsChecked = true;
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
                Type           = ResolveStyle(text),
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

        // ===== 发全文（不分段）：忽略每段字数，整篇作为一段发出 =====
        private void BtnSendAll_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentText))
            {
                HandyControl.Controls.Growl.Warning("请先选择/输入文章");
                return;
            }
            string text = ChkTickOut.IsChecked == true ? TextProcessor.TickBlock(_currentText) : _currentText;
            if (text.Length == 0) { HandyControl.Controls.Growl.Warning("文章为空"); return; }

            var state = new SendingState
            {
                Active         = true,
                FullText       = text,
                PoolText       = text,
                Title          = _currentTitle,
                Type           = ResolveStyle(text),
                IsRandom       = false,
                RandomNoRepeat = false,
                OneSentenceEnd = false,
                CountPerSeg    = text.Length,   // 关键：每段 = 全文长度 → 一次发完
                Mark           = 0,
                StartSeg       = 1,
            };
            ResultState = state;
            OnStartSending?.Invoke(state);
            this.Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Close();
    }
}
