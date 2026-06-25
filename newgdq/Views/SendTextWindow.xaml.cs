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
    public partial class SendTextWindow : Wpf.Ui.Controls.FluentWindow
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
        // 续打身份：随 _currentText 一起更新，保证与实际载入的正文来源一致。
        private string _currentArticleKind = "";   // "Builtin" / "CustomFile" / "Clipboard"
        private string _currentArticleSourceId = ""; // 自带=资源名；本地=文件路径；剪贴板=空

        public SendTextWindow()
        {
            InitializeComponent();
            foreach (var b in Builtin) LbxBuiltin.Items.Add(b.Header);
            ReloadPresets();
            CmbStyle.SelectedIndex = 0;  // 自动

            // 恢复上次"本次发送字数"（默认 25）
            int lastCount = SettingsService.Instance.LastSendCount ?? 25;
            if (lastCount > 0) TbxSendCount.Text = lastCount.ToString();

            // 恢复上次"自定义文章"文件夹
            var lastFolder = SettingsService.Instance.LastCustomFolder;
            if (!string.IsNullOrEmpty(lastFolder) && Directory.Exists(lastFolder))
                BuildCustomTree(lastFolder);
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
            if (string.IsNullOrEmpty(name)) { Services.Toast.Warning("请填名称"); return null; }
            if (!int.TryParse(TbxPresetCount.Text, out int count) || count <= 0)
            { Services.Toast.Warning("每段字数无效"); return null; }
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
            Services.Toast.Success("已保存预设：" + p.Name);
        }

        private void BtnPresetUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (!(LbxPresets.SelectedItem is SendPreset old))
            { Services.Toast.Info("先在左侧选一个预设"); return; }
            var p = BuildPresetFromForm(); if (p == null) return;
            var list = SettingsService.Instance.SendPresets;
            int idx = list.IndexOf(old);
            if (idx < 0) return;
            list[idx] = p;
            SettingsService.Save();
            ReloadPresets();
            LbxPresets.SelectedItem = p;
            Services.Toast.Success("已更新：" + p.Name);
        }

        private void BtnPresetDelete_Click(object sender, RoutedEventArgs e)
        {
            if (!(LbxPresets.SelectedItem is SendPreset p))
            { Services.Toast.Info("先在左侧选一个预设"); return; }
            SettingsService.Instance.SendPresets.Remove(p);
            SettingsService.Save();
            ReloadPresets();
            Services.Toast.Success("已删除：" + p.Name);
        }

        /// <summary>把选中预设的参数灌到主参数区（Tab 1-3 共用的发文参数输入框）。</summary>
        private void BtnPresetApply_Click(object sender, RoutedEventArgs e)
        {
            if (!(LbxPresets.SelectedItem is SendPreset p))
            { Services.Toast.Info("先在左侧选一个预设"); return; }
            TbxSendCount.Text  = p.CountPerSeg.ToString();
            TbxStartSeg.Text   = p.StartSeg.ToString();
            TbxSendStart.Text  = p.Mark.ToString();
            RbnOutOrder.IsChecked = p.IsRandom;
            RbnInOrder.IsChecked  = !p.IsRandom;
            ChkNoRepeat.IsChecked = p.RandomNoRepeat;
            ChkOneEnd.IsChecked   = p.OneSentenceEnd;
            ChkTickOut.IsChecked  = p.TickOut;
            Services.Toast.Success("已套用：" + p.Name + "（参数已填入面板，回到前面 Tab 检查或直接 开启发文）");
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
                _currentArticleKind = "Builtin";
                _currentArticleSourceId = fileName;
                TxtBuiltinPreview.Text = _currentText.Length > 200
                    ? _currentText.Substring(0, 200) + "..."
                    : _currentText;
                RefreshInfo();
            }
            catch (Exception ex)
            {
                Services.Toast.Error("载入失败：" + ex.Message);
            }
        }

        // ===== Tab 2 自定义文章：本地 TXT 文件树 =====
        private void BtnPickFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "选择存放 TXT 文章的文件夹";
                var last = SettingsService.Instance.LastCustomFolder;
                if (!string.IsNullOrEmpty(last) && Directory.Exists(last))
                    dlg.SelectedPath = last;
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                SettingsService.Instance.LastCustomFolder = dlg.SelectedPath;
                SettingsService.Save();
                BuildCustomTree(dlg.SelectedPath);
            }
        }

        private void BtnPickTxtFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "文本文件|*.txt|所有文件|*.*"
            };
            if (dlg.ShowDialog() == true)
                LoadCustomFile(dlg.FileName);
        }

        private void BtnRefreshTree_Click(object sender, RoutedEventArgs e)
        {
            var folder = SettingsService.Instance.LastCustomFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                Services.Toast.Info("请先选择文件夹");
                return;
            }
            BuildCustomTree(folder);
        }

        /// <summary>以 root 为根重建文件树（只显示子目录与 .txt 文件，子目录懒加载）。</summary>
        private void BuildCustomTree(string root)
        {
            TxtCustomFolder.Text = root;
            TrvCustom.Items.Clear();
            try
            {
                var rootNode = CreateDirNode(root, System.IO.Path.GetFileName(root.TrimEnd('\\', '/')) is var n && string.IsNullOrEmpty(n) ? root : n);
                rootNode.IsExpanded = true;
                PopulateDir(rootNode);
                TrvCustom.Items.Add(rootNode);
            }
            catch (Exception ex)
            {
                Services.Toast.Error("读取文件夹失败：" + ex.Message);
            }
        }

        private TreeViewItem CreateDirNode(string fullPath, string display)
        {
            var item = new TreeViewItem { Header = "📁 " + display, Tag = new DirTag(fullPath) };
            item.Items.Add("__dummy__");           // 占位，触发可展开
            item.Expanded += DirNode_Expanded;
            return item;
        }

        private TreeViewItem CreateFileNode(string fullPath)
        {
            return new TreeViewItem
            {
                Header = System.IO.Path.GetFileName(fullPath),
                Tag = fullPath                       // 文件节点：Tag 为字符串路径
            };
        }

        private void DirNode_Expanded(object sender, RoutedEventArgs e)
        {
            if (!(sender is TreeViewItem item) || !(item.Tag is DirTag tag)) return;
            if (tag.Loaded) return;
            tag.Loaded = true;
            item.Items.Clear();
            PopulateDir(item);
        }

        private void PopulateDir(TreeViewItem dirItem)
        {
            var tag = dirItem.Tag as DirTag;
            if (tag == null) return;
            tag.Loaded = true;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(tag.Path).OrderBy(p => p))
                {
                    try
                    {
                        var di = new DirectoryInfo(dir);
                        if ((di.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        dirItem.Items.Add(CreateDirNode(dir, di.Name));
                    }
                    catch { /* 跳过无权限目录 */ }
                }
                foreach (var file in Directory.EnumerateFiles(tag.Path, "*.txt").OrderBy(p => p))
                    dirItem.Items.Add(CreateFileNode(file));
            }
            catch (Exception ex)
            {
                Services.Toast.Error("展开失败：" + ex.Message);
            }
        }

        private void TrvCustom_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (!(e.NewValue is TreeViewItem item) || !(item.Tag is string path)) return; // 目录节点 Tag 是 DirTag
            LoadCustomFile(path);
        }

        /// <summary>读入单个 TXT 文件并载入为当前自定义文章（树节点选中与"选择TXT文件"共用）。</summary>
        private void LoadCustomFile(string path)
        {
            try
            {
                string text = ArticleLoader.LoadFromFile(path);
                if (string.IsNullOrWhiteSpace(text))
                {
                    Services.Toast.Warning("文件内容为空");
                    return;
                }
                _currentText = text;
                _currentTitle = System.IO.Path.GetFileNameWithoutExtension(path);
                _currentArticleKind = "CustomFile";
                _currentArticleSourceId = path;
                TxtCustomPreview.Text = text.Length > 400 ? text.Substring(0, 400) + " ..." : text;
                RefreshInfo();
            }
            catch (Exception ex)
            {
                TxtCustomPreview.Text = "无法读取该文件：" + ex.Message;
                Services.Toast.Error("读取失败：" + ex.Message);
            }
        }

        /// <summary>目录节点标记（区分文件节点的字符串 Tag），记录路径与懒加载状态。</summary>
        private sealed class DirTag
        {
            public string Path;
            public bool Loaded;
            public DirTag(string path) { Path = path; }
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
            _currentArticleKind = "Clipboard";
            _currentArticleSourceId = "";
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
                Services.Toast.Warning("请先选择/输入文章");
                return;
            }
            string text = ChkTickOut.IsChecked == true ? TextProcessor.TickBlock(_currentText) : _currentText;
            if (text.Length == 0) { Services.Toast.Warning("文章为空"); return; }

            if (!int.TryParse(TbxSendCount.Text, out int countPerSeg) || countPerSeg <= 0)
            {
                Services.Toast.Warning("发送字数无效");
                return;
            }
            // 记住本次发送字数，下次打开沿用
            SettingsService.Instance.LastSendCount = countPerSeg;
            try { SettingsService.Save(); } catch { }
            int.TryParse(TbxSendStart.Text, out int mark);
            int.TryParse(TbxStartSeg.Text, out int startSeg);
            if (startSeg <= 0) startSeg = 1;
            if (mark < 0) mark = 0;
            if (mark >= text.Length) { Services.Toast.Warning("起始位置超出范围"); return; }

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
                AutoAdvance    = ChkAutoAdvance.IsChecked == true,
                CountPerSeg    = countPerSeg,
                Mark           = mark,
                StartSeg       = startSeg,
                SourceName     = GetCurrentSourceName(),
                ArticleKind    = _currentArticleKind,
                ArticleId      = _currentArticleSourceId,
                TickOut        = ChkTickOut.IsChecked == true,
                InitialMark    = mark,
            };
            ResultState = state;
            OnStartSending?.Invoke(state);
            this.Close();
        }

        private string GetCurrentSourceName()
        {
            if (MainTab?.SelectedItem is System.Windows.Controls.TabItem t)
                return t.Header?.ToString() ?? "-";
            return "-";
        }

        // ===== 发全文（不分段）：忽略每段字数，整篇作为一段发出 =====
        private void BtnSendAll_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentText))
            {
                Services.Toast.Warning("请先选择/输入文章");
                return;
            }
            string text = ChkTickOut.IsChecked == true ? TextProcessor.TickBlock(_currentText) : _currentText;
            if (text.Length == 0) { Services.Toast.Warning("文章为空"); return; }

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
                SourceName     = GetCurrentSourceName(),
                ArticleKind    = _currentArticleKind,
                ArticleId      = _currentArticleSourceId,
                TickOut        = ChkTickOut.IsChecked == true,
                InitialMark    = 0,
            };
            ResultState = state;
            OnStartSending?.Invoke(state);
            this.Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Close();
    }
}
