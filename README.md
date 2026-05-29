# 州州跟打器（zhouzhou-typing）

> **现代化的 WPF 跟打练习器 / 中文打字训练 / 五笔拼音跟打**：基于 [taliove/tygdq](https://github.com/taliove/tygdq) v0.94 重写，去掉所有 QQ / 比赛 / 检查更新相关功能，专注**纯本地跟打体验**。
>
> Modern WPF Chinese typing tutor (Wubi/Pinyin), portable, dark/light theme, real-time stats. Rewrite of the classic 添雨跟打器 (tygdq).

**关键词** / Keywords：跟打器、中文打字练习、五笔练习、拼音练习、双拼练习、打字训练、typing tutor for Chinese, Wubi/Pinyin typing trainer, WPF typing software, tygdq successor

![.NET](https://img.shields.io/badge/.NET-Framework_4.8-512BD4?logo=dotnet)
![WPF](https://img.shields.io/badge/UI-WPF-blueviolet)
![License](https://img.shields.io/badge/license-Apache%202.0-green.svg)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey)
[![Release](https://img.shields.io/github/v/release/lwl4613615/zhouzhou-typing?label=release)](https://github.com/lwl4613615/zhouzhou-typing/releases/latest)
[![GitHub](https://img.shields.io/badge/GitHub-lwl4613615%2Fzhouzhou--typing-181717?logo=github)](https://github.com/lwl4613615/zhouzhou-typing)

---

## ✨ 特性

### 🎯 跟打核心
- **实时染色**：对照区按字符比对，绿色对 / 红色错
- **结算时统一标记**：跟打中只标错字；完成后回改(粉红) / 慢字(浅绿) 分类标注，复盘一目了然
- **节奏热力条**（替代旧跟打地图）：每字一格、绿→黄→橙→红渐变、最慢字白色发光 + 顶部刻度 + 慢字红色三角 ms 标记纵向堆叠；**鼠标悬停色块显示对应字符 + 耗时**（中文/英文/标点原样、空格等可读化）
- **实时统计**：速度（字/分）+ 错一罚五速度、击键（键/秒）、码长、回改、错字、左右手击键比
- **重打次数 / 发呆秒数 / 键准百分比** 实时显示
- 末字错允许回改不强制结束 / F3/F5/换文前强制结算

### 🎨 现代化 UI
- **暗 / 亮主题** 完整覆盖：所有子窗、嵌入图表、DataGrid 都跟主题切换
- **全局界面缩放**（高分屏友好）：Ctrl+滚轮 / Ctrl+加减号 / 菜单 100%–250% 一键放大，所有窗体字体控件统一缩放；子窗体比主窗口小一圈、主窗口最大；多屏边界自动裁剪、按各屏 DPI 换算不越界；首次启动按分辨率自动推荐倍数，设置持久化
- **设置-颜色** 点色块直接弹 HandyControl 拾色器
- **现代化 ScoreCard 成绩图**：左 KPI 大数字 + 右 速度曲线/节奏热力 + 下 8 项详细统计，2x DPI 高清，微信群发不糊
- **发文状态浮窗**：渐变卡片 / 霓虹进度条 / 三联字数大数 / 磁吸跟随主窗 / 缩放后自动重新贴边
- **历史表底部金色汇总行**：今日 / 本次训练时长、段数、均速、击键、码长 + 累计均速
- **成绩区 本次/全部 一键切换**：表头按钮 `[本次 N]` ↔ `[全部 N]`，footer 汇总数字跟着模式现算；历史一行不删，状态记 settings 持久化
- **信息条最右**接 SQLite 真实统计：今日字数 / 累计 / 训练天数 / 段数
- 对照区 / 输入区 / 历史区 三段可拖伸缩，信息条夹中固定不变形

### 📊 数据分析
- **跟打报告**：摘要 + 段内事件 DataGrid（回改奶油黄 / 卡顿粉红）+ 复制/保存成绩图 PNG
- **速度分析**：4 项时间分解（正常打字 / 卡顿 / 回改 / 错字罚时）+ 理论速度对比
- **击键评定**：9 级柱图 + jjC 评定值，对齐老版
- **平均成绩**：全部历史 SQLite 聚合
- **嵌入式速度曲线**：OxyPlot 实时面积图，主题色 + ClearType

### 📚 编码 / 词组
- **bm.txt 词典**：76145 行反向索引，最长匹配优先词组
- **词组模式**（菜单）：开启时下划线 + 词组编码提示 + 词组理论码长
  - 蓝=2字 / 紫=3字 / 红=4字+，全码实线 / 非全码虚线
- 单字模式：无下划线、只查单字码
- 状态条显示 [重数色块 + 字 + 编码]
- **测试自定义 bm.txt** 工具

### 📤 发文
- 自带文章 / 自定义 / 剪贴板 / 配置预设 4 个 Tab
- 顺序 / 乱序 / 一句结束 / 全文一次发出 / 乱序不重复
- **停止发文后可一键继续**：按钮停止后变身绿色「继续发文」，从断点接续、不与自动发文冲突；已发完则置灰
- **本次发送字数记忆**：改过的字数下次打开自动沿用
- 段号点击弹列表跳段
- 发文参数预设（命名保存/应用）
- F2 时检测到发文中弹确认

### ⌨️ 键盘 / IME
- **热键**：F2/F3/F4/F6/F8/F9 + Ctrl+R/F2/方向键/T/B/E/G/Q/W，**仅主窗激活时生效**避免误触
- **双口径键数统计**（设置 → 个人 → 并击模式）：
  - **并击模式**（默认）：Keys 在 `TbxInput.PreviewKeyDown` 累加，IME 在前面已经把候选键吃掉，行为与老版 `TextBox.KeyDown` 一致，适合并击键盘 / 传统输入法用户
  - **串行模式**：Keys 在低层钩子累加，每个物理 down 都算 1 击，适合排查抖键 / 模拟按键噪声
  - Hg / 选重 / 左右手 统一由钩子统计，不受模式影响
- **KeyHook 过滤**：LLKHF_INJECTED + VK_PROCESSKEY，排除明确的软件模拟键
- **IME 兼容**：搜狗 / QQ 拼音 / 微软拼音的"空格/字母占位"陷阱已修复
- **拼回**计数：IME 候选框里按退格删拼音的次数（区分于跟打区回改）

### 💾 持久化（便携模式）
- 配置：JSON 原子写入 + .bak 自动备份回滚
- 历史：SQLite + WAL 模式 + Busy Timeout
- 文件都在 exe 同目录，**整个文件夹拷哪都跟着走**

### 🖥️ 系统集成
- 单实例 Mutex（防双开污染数据）
- 托盘图标 + 最小化到托盘 + 右键菜单
- app.manifest：asInvoker + PerMonitorV2 高 DPI + UTF-8 代码页
- 全局异常处理 + `newgdq.log` 日志
- 长时间未跟打自动重打（菜单可设阈值）

### ⚡ 性能优化
- 差异染色（大文段 CPU -60%）
- 节奏热力条 + 速度曲线限点 + 批量刷新
- SQLite WAL + 200 条历史预加载

---

## 🖼️ 截图

![主界面](docs/images/程序截图.png)

---

## 🚀 快速开始

### 用户

1. 到 [Releases](https://github.com/lwl4613615/zhouzhou-typing/releases) 下载最新 zip
2. 解压到任意目录
3. 双击 `newgdq.exe`
4. **菜单 → 功能 → 内部测速** 选一篇文章开始练习

### 开发者

```powershell
git clone https://github.com/lwl4613615/zhouzhou-typing.git
cd zhouzhou-typing
# 用 Visual Studio 2022+ 打开 newgdq/newgdq.csproj，按 F5
# 或 MSBuild 直接构建：
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
  newgdq\newgdq.csproj /p:Configuration=Release
```

需要环境：
- Windows 10/11
- .NET Framework 4.8 SDK
- Visual Studio 2022 或 MSBuild 17+
- 首次构建会自动从 NuGet 还原 HandyControl 3.5.1 / OxyPlot 2.2.0 / System.Data.SQLite.Core 1.0.118 / Hardcodet.NotifyIcon.Wpf 1.1.0

---

## 🎮 主要操作

### 热键（主窗激活时生效）

| 键 | 功能 |
|---|---|
| **F2** | 打开发文窗口（发文中再按弹确认） |
| **F3** | 重打当前段 |
| **F4** | 从剪贴板载文 |
| **F6** | 发下一段 |
| **F8** | 暂停 / 继续 |
| **F9** | 复制最新一段成绩 |
| **Ctrl+R** | 发下一段（同 F6） |
| **Ctrl+F2** | 打开发文状态窗 |
| **Ctrl+←/→** | 上一段 / 下一段（跳段） |
| **Ctrl+T** | 发送图片成绩 |
| **Ctrl+B** | 击键评定 |
| **Ctrl+E** | 速度分析 |
| **Ctrl+G** | 跟打报告 |
| **Ctrl+Q** | 将目前文章乱序 |
| **Ctrl+W** | 英文标点换中文 |

### 菜单 / 标记栏

| 操作 | 说明 |
|---|---|
| 菜单 → 外观... | 字体 / 颜色（拾色器）/ 个签 / 主题 / 托盘 / 自动重打 / 速度限制 |
| 菜单 → 功能 → 内部测速 | 加载内置文章 8 篇 |
| 菜单 → 功能 → 词组模式 | 开启 = 下划线 + 词组编码提示；关闭 = 单字模式 |
| 菜单 → 功能 → 跟打报告/速度分析/击键评定/平均成绩 | 数据分析窗口 |
| 菜单 → 功能 → 测试自定义 bm.txt | 验证用户自定义词典 |
| 底部标记栏：图片/编码/曲线/节奏/标记/极简/限制/替换/详细 | 9 个开关 |
| 对照/输入/历史区之间分割条 | 鼠标拖动调整高度（信息条 Auto 高度固定不变） |

---

## 🏗️ 项目结构

```
newgdq/
├── App.xaml(.cs)             — 应用入口 + HandyControl 主题
├── MainWindow.xaml(.cs)      — 主窗口 UI + 跟打主循环
├── Themes/
│   ├── Dark.xaml             — 深空蓝 + 青色辉光
│   └── Light.xaml            — 薄雾灰 + 琥珀强调
├── Models/
│   ├── TypingSession.cs      — 单段跟打状态（替代原版 Glob 全局）
│   ├── HistoryRow.cs         — 历史成绩行
│   ├── TypeDate.cs           — 段内输入事件明细
│   ├── BmEntry.cs            — 字典条目
│   ├── WordHit.cs            — 分词命中
│   └── SendingState.cs       — 发文状态
├── Services/
│   ├── KeyHook.cs            — WH_KEYBOARD_LL 全局钩子 + 5 层物理键过滤
│   ├── DictionaryService.cs  — bm.txt 字典 + 最长匹配
│   ├── HistoryRepository.cs  — SQLite WAL + 汇总聚合
│   ├── SettingsService.cs    — JSON 原子写 + .bak 回滚
│   ├── SendingService.cs     — 发文段管理（顺序/乱序/跳段）
│   ├── UiScaleManager.cs     — 全局界面缩放（多窗体统一 + 多屏边界）
│   ├── TextProcessor.cs      — 乱序/英标转中标/剔除空格
│   └── ArticleLoader.cs      — 嵌入式文章资源加载
├── Views/
│   ├── ScoreCard.xaml        — 现代化成绩卡（2x DPI 复制/保存图）
│   ├── ReportWindow.xaml     — 跟打报告
│   ├── SpeedAnalysisWindow   — 速度分析（4 项柱图）
│   ├── JjCheckWindow         — 击键评定
│   ├── AverageWindow         — 平均成绩
│   ├── SendTextWindow        — 发文设置（4 Tab）
│   ├── SendStatusWindow      — 发文状态浮窗（磁吸跟随主窗）
│   ├── SettingsWindow        — 设置（字体/颜色拾色器/个签/存储）
│   ├── BmTipsWindow          — 编码提示
│   └── AboutWindow           — 关于
├── Properties/
│   └── app.manifest          — UAC asInvoker + PerMonitorV2 + UTF-8
└── Resources/
    ├── bm.txt                — 词典（UTF-8，76145 行）
    ├── myicon.ico            — 程序图标
    ├── erweima.jpg           — 微信二维码
    └── TXT/                  — 内置文章 8 篇
```

---

## 🆚 与上游 [tygdq](https://github.com/taliove/tygdq) 的差异

| 类别 | 上游 (WinForms v0.94) | 州州跟打器 (WPF) |
|---|---|---|
| UI 框架 | WinForms 自绘 + QQ 风格 | WPF + HandyControl 暗/亮双主题 |
| 数据存储 | INI + Access (.mdb) | JSON + SQLite WAL |
| **QQ 群发文 / 群名解析** | 有 | **删** |
| **比赛模式 / 精五成绩生成 / 测速点** | 有 | **删** |
| **检查更新** | 有 | **删** |
| 词典查询 | `List<List<string>>` 全表线性扫描 | `Dictionary<char, List<BmEntry>>` 反向索引 + 最长匹配 |
| 词组下划线渲染 | 动态浮动 Label 控件 | `Run.TextDecorations`（性能更好） |
| 全局状态 | `static Glob`（252 行字段） | `TypingSession` 实例化 |
| 跟打核心代码 | `FormType.cs` 5476 行 | `MainWindow.xaml.cs` ~1900 行 + 拆分 services |
| 击键计数 | 信任所有 KeyHook 事件（IME 注入混入） | 5 层物理键过滤 |
| 成绩图 | 截窗体 96 DPI | 独立 ScoreCard 2x DPI 高清 |
| 主题 | 单一 QQ 风 | 暗/亮 + 拾色器配色 |
| **跟打、染色、统计、编码提示等核心功能** | ✓ | ✓ |

---

## 📋 路线图

- [x] **P1** WPF 骨架 + 主窗口 + 跟打核心
- [x] **P2** 键钩子 + IME + 实时统计 + 编码提示 + 词组下划线 + 速度曲线
- [x] **P3** 发文窗口 + 设置窗口 + 主题切换（暗/亮）
- [x] **P4** 配置 JSON 持久化 + SettingsWindow + SQLite 历史
- [x] **P5** 跟打报告 + 速度分析 + 击键评定 + 平均成绩 + 成绩图导出
- [x] **P6** 托盘 + 单实例 Mutex + 全局异常处理 + manifest + app icon
- [x] **P7** 性能优化（差异染色 / 限点 / SQLite WAL）+ 边界 bug 修复
- [x] **P8** ytgdq 借鉴：重打次数 / 发呆显示 / 键准 / 高度可拖
- [x] **P9** (v0.6) 现代成绩卡 ScoreCard + 节奏热力条 + 浅色主题全面适配 + KeyHook 严格过滤
- [x] **P10** (v0.7) 全局界面缩放（高分屏/多屏）+ 节奏色块悬停显字 + 停止后继续发文 + 发送字数记忆 + 双拼默认自然码 + 中文位空格误判修复

**核心迁移已完成** ✅ 详细完成情况见 [迁移方案.md](迁移方案.md) 与 [ROADMAP.md](ROADMAP.md)。

---

## 💬 交流

- **微信**：`synhxb`（点信息条右上"联系州州"复制）
- **QQ 群**：`17079867`
- **Issues**：https://github.com/lwl4613615/zhouzhou-typing/issues

---

## ☕ 支持作者

如果觉得有用，欢迎请喝杯咖啡：

<img src="docs/images/捐赠码.jpg" alt="捐赠码" width="240"/>

---

## 🤝 贡献

欢迎 PR / Issues。开发约定见 [AGENTS.md](AGENTS.md)（基于 Andrej Karpathy 的 LLM 编码准则）。

---

## 📄 协议

本项目采用 [Apache License 2.0](LICENSE)，与上游 [taliove/tygdq](https://github.com/taliove/tygdq) 保持一致。

复用的上游代码版权归原作者 **taliove** 所有，详见 [NOTICE](NOTICE)。

WPF 重写部分 © 2026 4613615@qq.com，遵循 Apache-2.0。

---

## 🙏 致谢

- [taliove/tygdq](https://github.com/taliove/tygdq) — 原版添雨跟打器，本项目所有跟打核心算法、bm.txt 词典、内置文章均源自上游
- [HandyOrg/HandyControl](https://github.com/HandyOrg/HandyControl) — WPF 控件库
- [oxyplot/oxyplot](https://github.com/oxyplot/oxyplot) — 图表库
- [System.Data.SQLite](https://system.data.sqlite.org/) — SQLite 持久化
- [hardcodet/wpf-notifyicon](https://github.com/hardcodet/wpf-notifyicon) — 托盘图标