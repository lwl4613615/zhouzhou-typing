# 州州跟打器（zhouzhou-typing）

> 一个现代化的 WPF 五笔/拼音跟打练习器，基于 [taliove/tygdq](https://github.com/taliove/tygdq) v0.94 重写，去掉所有 QQ / 比赛 / 检查更新相关功能，专注**纯本地跟打体验**。

![.NET](https://img.shields.io/badge/.NET-Framework_4.8-512BD4?logo=dotnet)
![WPF](https://img.shields.io/badge/UI-WPF-blueviolet)
![License](https://img.shields.io/badge/license-Apache%202.0-green.svg)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey)
[![GitHub](https://img.shields.io/badge/GitHub-lwl4613615%2Fzhouzhou--typing-181717?logo=github)](https://github.com/lwl4613615/zhouzhou-typing)

---

## ✨ 特性

- **WPF 现代深色主题**（HandyControl 3.5.1）—— 玻璃标题栏、阴影圆角、自适应缩放
- **跟打核心**
  - 实时染色：对照区按字符比对，绿色对 / 红色错
  - 实时统计：速度（字/分）、击键（键/秒）、码长、回改、错字、左右手击键比
  - 段内输入事件明细收集（为跟打报告准备）
  - 末字错允许回改不强制结束（与原版 `LastInput=1` 等价）
- **编码提示**（基于 `bm.txt`）
  - 76145 行词典反向索引，O(1) 查找，最长匹配优先词组
  - 当前光标处显示 [重数色块 + 字 + 编码]
- **词组下划线**（自动智能测词）
  - 蓝=2字 / 紫=3字 / 红=4字+
  - 全码实线 / 非全码虚线
- **速度面积曲线**（OxyPlot.Wpf）
- **IME 兼容**：搜狗 / QQ 拼音 / 微软拼音的"空格/字母占位"陷阱已修复
- **全局键盘钩子**：F3 重打不依赖窗口焦点

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
```

需要环境：
- Windows 10/11
- .NET Framework 4.8 SDK
- Visual Studio 2022 或 MSBuild 18.x

第一次构建会自动从 NuGet 还原 HandyControl / OxyPlot。

---

## 🎮 主要操作

| 操作 | 说明 |
|---|---|
| **F3** | 重打当前文章（全局热键） |
| 菜单 → 功能 → 内部测速 | 加载内置文章 |
| 菜单 → 复位 | 清空当前文章 |
| 底部"编码"按钮 | 切换状态条编码提示 |
| 底部"曲线"按钮 | 显示速度面积曲线 |
| 底部"标记"按钮 | 切换词组下划线显示 |
| 底部"详细"按钮 | 切换底部历史 Grid 显示 |

---

## 🏗️ 项目结构

```
newgdq/
├── App.xaml(.cs)             — 应用入口 + HandyControl 主题
├── MainWindow.xaml(.cs)      — 主窗口 UI + 跟打主循环
├── Models/
│   ├── TypingSession.cs      — 单段跟打状态（替代原版 Glob 全局）
│   ├── HistoryRow.cs         — 历史成绩行
│   ├── TypeDate.cs           — 段内输入事件明细
│   ├── BmEntry.cs            — 字典条目
│   └── WordHit.cs            — 分词命中
├── Services/
│   ├── KeyHook.cs            — WH_KEYBOARD_LL 全局钩子
│   ├── ImeWatcher.cs         — IME 合成事件监听
│   ├── DictionaryService.cs  — bm.txt 字典 + 最长匹配
│   └── ArticleLoader.cs      — 嵌入式文章资源加载
├── Views/
│   ├── BmTipsWindow.xaml     — （已弃用，编码提示移到状态条）
│   └── SpeedChartWindow.xaml — 速度面积曲线浮窗
└── Resources/
    ├── bm.txt                — 词典（UTF-8，从上游 GBK 转码）
    └── TXT/                  — 内置文章 8 篇
```

---

## 🆚 与上游 [tygdq](https://github.com/taliove/tygdq) 的差异

| 类别 | 上游 (WinForms v0.94) | 州州跟打器 (WPF) |
|---|---|---|
| UI 框架 | WinForms 自绘 + QQ 风格 | WPF + HandyControl 深色主题 |
| **QQ 群发文 / 群名解析** | 有 | **删** |
| **比赛模式 / 精五成绩生成 / 测速点** | 有 | **删** |
| **检查更新** | 有 | **删** |
| **Access 数据库** | PerTyping.mdb + DataSet | 计划换 SQLite (P5) |
| 配置存储 | INI (`Ttyping.ty`) | 计划换 JSON (P4) |
| 词典查询 | `List<List<string>>` 全表线性扫描 | `Dictionary<char, List<BmEntry>>` 反向索引 + 最长匹配 |
| 词组下划线渲染 | 动态浮动 Label 控件 | `Run.TextDecorations`（性能更好） |
| 全局状态 | `static Glob`（252 行字段） | `TypingSession` 实例化 |
| 跟打核心代码 | `FormType.cs` 5476 行 | `MainWindow.xaml.cs` ~600 行 + 拆分 services |
| **跟打、染色、统计、编码提示等核心功能** | ✓ | ✓ |

---

## 📋 路线图

- [x] **P1** WPF 骨架 + 主窗口
- [x] **P2** 跟打核心 + 键钩子 + 实时统计
- [x] **P3** 编码提示 + 词组下划线 + 速度曲线
- [ ] **P4** 发文窗口 + 设置窗口 + 主题切换
- [ ] **P5** 跟打报告 + SQLite 历史持久化 + 图片成绩
- [ ] **P6** 托盘 + 单实例 + 关于页 + 打包

详细 TODO 与已知缺陷见 [ROADMAP.md](ROADMAP.md)。

---

## 💬 交流群

QQ 群：**17079867** （问题反馈 / 跟打交流）

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
