# AGENTS.md — 开发约定与协作指南

本文件面向参与本项目的开发者与 AI 编码助手，沉淀构建方式、Git 流程与已知技术坑。
理念基于 Andrej Karpathy 的 LLM 编码准则：**小步快跑、改完即验、避免过度工程**。

---

## 项目概览

- **州州跟打器**（newgdq）：WPF / .NET Framework 4.8 中文跟打练习软件
- 主工程：`newgdq/newgdq.csproj`；解决方案：`newgdq.slnx`
- 依赖（NuGet）：HandyControl 3.5.1、OxyPlot 2.2.0、System.Data.SQLite.Core 1.0.118、Hardcodet.NotifyIcon.Wpf 1.1.0
- DPI：`app.manifest` 启用 PerMonitorV2 + UTF-8；WPF 文本输入走 **TSF** 而非 IMM32

---

## 构建

每次改动代码后请重新编译 Release 验证：

```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild "newgdq\newgdq.csproj" /p:Configuration=Release /t:Rebuild /v:minimal /nologo
```

成功输出：`newgdq -> ...\bin\Release\newgdq.exe`

---

## Git 流程

- 分支 `main`，远程 `origin`
- 提交信息用中文，遵循 `类型: 摘要` 前缀（feat / fix / release / docs 等）
- 发布版本时：同步 bump `newgdq/Properties/AssemblyInfo.cs` 的 `AssemblyVersion` / `AssemblyFileVersion`，并更新 `README.md`

---

## 发布 / 部署约定

> 路径按机器而异，不要写死。下面用变量表示，换机器只改这两行：
> - `$repo`   = 仓库根（本机当前克隆位置，如 `git rev-parse --show-toplevel`）
> - `$deploy` = 部署目录（本机自定，示例机为 `D:\gdq\exe`）

部署命令（排除用户数据，避免覆盖历史/设置）：

```powershell
$repo   = (git rev-parse --show-toplevel)        # 仓库根
$deploy = 'D:\gdq\exe'                            # 部署目录（换机器改这里）

Get-Process newgdq -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
Copy-Item "$repo\newgdq\bin\Release\*" $deploy -Recurse -Force -Exclude 'history.db','settings.json','settings.json.bak','newgdq.log'
Copy-Item "$repo\更新说明.txt" $deploy -Force     # 更新说明随程序一起发布
Start-Process "$deploy\newgdq.exe"
```

**默认约定：更新说明随程序版本一起走。**

- 仓库根维护一份 `更新说明.txt`，**纳入 Git 仓库**，每次发布更新时同步刷新内容
- 部署时把它一并复制进部署目录（上面的命令已含这一步）
- 内容含：版本号 + 日期、本次更新分组要点（新功能 / 修复 / 其他）、常用快捷键速查
- 面向最终用户，用中文白话，不写代码细节
- 版本号与 `AssemblyInfo.cs` 保持一致

---

## 双轨发布（4.8 与 net10 并行维护）

项目有两条长期并行的发布线，分歧只在 **UI 外壳 + 工程文件**，**业务逻辑层共享**：

| 线 | 分支 | 工程 | UI 框架 | tag 前缀 | 本地部署目录 |
|---|---|---|---|---|---|
| 稳定线（老机器兼容） | `main` | 老式 csproj + packages.config | HandyControl（`Growl`） | `v0.*` | `D:\gdq\48EXE` |
| 主力线（现代 UI） | `net10` | SDK 式 + PackageReference | WPF-UI（`Toast` / `FluentWindow`） | `v1.*` | `D:\gdq\net10exe` |

### 维护工作流（单向流动：net10 → main）

- **新功能/修复一律先在 `net10` 做**，验证、提交后再按需挑回 `main`。
- **纯逻辑改动**（`Services/` `Models/` 判错/计时/统计/IME 剥离）→ `git cherry-pick <hash>`，冲突一般只落在 UI 控件名/窗口基类那几行。
- **涉及 UI 的改动**（窗口、XAML、提示控件）→ **不要 cherry-pick**，两边各写各的（一边 `Growl`、一边 `Toast`）。
- 想让 cherry-pick 命中率更高：把纯逻辑尽量下沉到 `Services/`，UI 事件里只调用 Service。
- **绝不反向 merge**（main → net10），避免工程文件互相污染。

### CI 自动发布（GitHub Actions，`.github/workflows/build.yml`）

- **两分支各有一份适配自己的 `build.yml`**：CI 跑 tag 时 checkout 的是 **tag 所指 commit 的 workflow**，所以互不干扰。
  - `main` 版：`microsoft/setup-msbuild` + `nuget restore packages.config` + `msbuild`，触发 `v0.*`。
  - `net10` 版：`actions/setup-dotnet`（10.0.x）+ `dotnet restore/build`，触发 `v1.*`。
- 推送 `v*` tag → CI 自动编译、打包 zip（含 README/LICENSE/NOTICE，剔除 pdb/xml）、创建 Release 并上传附件、`generate_release_notes` 自动生成说明。

### 发版步骤（每条线相同，只是 tag 前缀不同）

1. bump `newgdq/Properties/AssemblyInfo.cs` 的 `AssemblyVersion` / `AssemblyFileVersion`（main 走 `0.x`、net10 走 `1.x`）。
2. 本地编 Release 验证（见上方各自构建命令）+ 部署到对应目录。
   > ⚠️ **切分支后先清 `newgdq/obj`**：net10 的 SDK restore 产物（`project.assets.json`）会让 4.8 的 MSBuild 报 *"does not reference .NETFramework v4.8"*。切到 main 编译前先 `Remove-Item newgdq\obj -Recurse -Force`，再 `msbuild /t:Restore,Rebuild`。
3. 提交 → `git push origin <branch>` → 打 tag（`git tag -a v1.0.0 -m "..."`）→ `git push origin <tag>`。
4. CI 自动出 Release。核对：`gh release list`、`gh release view <tag> --json assets,url`。

### 运行时说明

- net10 包是**框架依赖发布**，用户机器需自行安装 **.NET 10 Desktop Runtime**（程序启动时系统会给安装提示）。**不打包 self-contained**，保持 zip 体积小。
- 4.8 包面向无 .NET Core 运行时的老机器（Win7/旧 Win10），开箱即用。

---

## 已知技术坑

### 1. IME 中文判错（WPF/TSF 合成串污染 TextBox.Text）

**现象**：在中文原文位置真打错的空格、英文字母、整串英文不被判错（漏判）。

**根因**：WPF 走 TSF，拼音合成期间合成中间态（拼音字母 / 占位空格）会**实时写进 `TbxInput.Text`**，与原文逐字比对时被误判或被错误剥离。参考 [dotnet/wpf#6194](https://github.com/dotnet/wpf/issues/6194)（症状是丢字，但同样印证合成串会写入 Text；.NET 6.0.3 已修，但本项目是 Framework 4.8 吃不到）。

**正解（方案 A'，纯 WPF 事件，已实装）**：
- 用 `TextCompositionManager.AddPreviewTextInputStartHandler` / `AddPreviewTextInputHandler` 维护 `_imeComposing` 标志
- 尾部占位符剥离循环改为 `while (_imeComposing && realLen > 0)`：仅合成进行中才保护尾部；非合成态（英文直打 / 已上屏）走**纯逐字比对**
- 代码位置：`MainWindow.xaml.cs` 构造函数订阅 + `TbxInput_TextInputStart` / `TbxInput_TextInputDone` + `TbxInput_TextChanged` 剥离段

> ⚠️ 不要退回到"靠猜测尾部 ASCII 占位符并无条件剥离"的旧逻辑——它无法区分"未上屏拼音"与"真打错的英文/空格"。

### 2. 已删除文件

- `Services/ImeWatcher.cs` 已删除，勿再引用。

### 3. 全局界面缩放

- 由 `Services/UiScaleManager.cs` 统一管理；子窗体比主窗口小一圈（ChildScale），多屏边界处理见该文件。
- .NET 4.8 下 `ConditionalWeakTable` 不可枚举，窗口跟踪用 `List<WeakReference<Window>>`。

---

## 编码准则（摘要）

- 只做被明确要求或显然必要的改动，避免顺手重构 / 加注释 / 加防御性代码
- 系统边界才做校验，不为不可能发生的场景加错误处理
- 大改前先评估难度与边界冲突，再动手
