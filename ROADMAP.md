# 州州跟打器 开发路线图

> 本文档跟踪所有**未完成功能**和**已知缺陷**，方便换机继续开发。
> 完成项见 [README.md](README.md) 的"路线图"。

---

## 已完成（参考）

- ✅ P1 WPF 骨架 + 主窗口（HandyControl 深色主题）
- ✅ P2 跟打核心 + 全局键盘钩子 + IME (TSF) 兼容
- ✅ P2-B 架构清理（拆 `TypingSession` / `DictionaryService` / `KeyHook` / `ImeWatcher` / `ArticleLoader`）
- ✅ P3-A 编码提示（状态条中部"重数色块+字+编码"3 格）
- ✅ P3-B 速度面积曲线（OxyPlot 浮窗，贴主窗下方）
- ✅ P3-D 词组下划线（蓝/紫/红 + 全码实线/非全码虚线）
- ✅ F3 全局重打热键
- ✅ 段内 TypeReport 收集（为 P5 报告准备）
- ✅ 末字错允许回改（`LastInput=1` 等价逻辑）
- ✅ 复位菜单
- ✅ 输入框三态控制（默认只读 / 载文可输入 / 完成只读）
- ✅ 完成 Growl 详细统计（速度/击键/码长/用时/错字/回改/键数/左右手）

---

## 待办

### 阶段 A — 跟打体验小修补（每条 5-15 分钟）

- [ ] **暂停 / 继续菜单** —— DispatcherTimer 暂停 + 累加 PauseTimes
- [ ] **段尺动态游标** —— 红色 14 写死的，应跟当前段号联动（无段号时隐藏）
- [ ] **完成后曲线窗加"完成标志"** —— 末点显示金色圆点 / 一条结束线
- [ ] **信息条"段号"格** —— 当前显示 `-`，发文时跟随推进（依赖 P4）
- [ ] **左右手单独刷新** —— TimerStats 已刷新，但 IME 启动延迟下首段前几秒为 0:0
- [ ] **历史 Grid 行号** —— 可加"显示总段数"汇总行

### 阶段 B — 发文窗口 + 设置（P4）

> 上游 [`新发文.cs`](https://github.com/taliove/tygdq/blob/main/WindowsFormsApplication2/发文重写/新发文.cs) 完整功能复刻，**砍 QQ 发送**。

- [ ] **发文窗口** 4 Tab：自带文章 / 本地文件 / 剪贴板 / 自定义
  - 自带文章 = 现在菜单"内部测速"那 8 篇
  - 本地文件 = OpenFileDialog 选 .txt
  - 剪贴板 = `Clipboard.GetText()`
  - 自定义 = 用户多行文本框
- [ ] **发文配置** 起始段号、每段字数、周期数、是否自动剔除空格
- [ ] **发文状态浮窗** —— 上游 SendTextStatic（你截过那张图：当前序列/总序列/已发字数/剩余字数/独练/自动）
- [ ] **段号 Pre_Cout 联动** —— 推进时段尺红游标动起来 + 信息条段号格显示
- [ ] **替换按钮生效** —— 载文时英标→中标
- [ ] **设置窗口**（按 Tab 分区）
  - 外观：对照区/输入区颜色、字体
  - 颜色：打对/打错色块
  - 热键：F3/F4/F5 自定义
  - 显示：曲线/详细/标记 默认状态
- [ ] **配置存储** appsettings.json + 强类型 `AppSettings`
- [ ] **可选** 老 `Ttyping.ty` INI 一次性导入

### 阶段 C — 跟打报告 + 持久化（P5）

- [ ] **跟打报告窗口** 用 `_session.Report` 段内事件画分析图
  - 段内每字下面用时柱
  - 用时最高 Top10 标红
  - 速度低于均值字加波浪线
  - 击键 ≥ 8 高击键段亮
  - 回改段（Length<0）粉色背景
- [ ] **平均成绩**（多段累计）
- [ ] **击键评定**（击键能力分析）
- [ ] **速度分析**（详细速度分析）
- [ ] **SQLite 历史持久化**
  - 表 1：`history`（每段成绩）
  - 表 2：`type_events`（段内输入明细）
  - 表 3：`stats_daily`（每日累计）
- [ ] **状态条末尾接真数据** —— 今日跟打/累计字数/天数/记录字数
- [ ] **最高速度/击键/码长记录**

### 阶段 D — 图片成绩 + 高级（P5 后半段）

- [ ] **"图片"按钮生效** —— 跟打完调成绩生成算法（WriteableBitmap）→ 复制剪贴板
  - 参考上游 [`图片成绩设计/PicGoal_Class.cs`](https://github.com/taliove/tygdq/blob/main/WindowsFormsApplication2/跟打部分/图片成绩设计/PicGoal_Class.cs)
- [ ] **"极简"模式** —— 短文本格式发送（`Glob.simpleSplite` 分隔符）
- [ ] **"限制"按钮** —— 速度阈值，超过才"发送"

### 阶段 E — 收尾（P6）

- [ ] **单实例 mutex** —— 同一时刻只能开一个 newgdq.exe
- [ ] **托盘 NotifyIcon** —— 用 Hardcodet.NotifyIcon.Wpf
- [ ] **关闭最小化到托盘**
- [ ] **关于页** —— 版本、作者、上游致谢
- [ ] **捐助页** —— 用 docs/images/捐赠码.jpg
- [ ] **打包发布**
  - GitHub Actions 自动构建 zip
  - 申请 SignPath 免费 EV 签名（流程见 [README.md](README.md)）

---

## 已知缺陷 / 边角问题

### 跟打统计

- [ ] **左右手统计**：仅字母键 A-Z 区分，标点/空格不算左右手（与原版一致，但容易让用户疑惑），可考虑加 ToolTip 说明
- [ ] **击键统计** Tab 键被排除（白名单未包含），如打英文文章用 Tab 缩进会丢键数。当前白名单：字母/数字/标点/回车/退格/空格

### IME 兼容

- [x] 搜狗/QQ 拼音空格占位 → 已在 TextChanged 截断（详见 [/memories/repo/newgdq-ime-trap.md](../memories/repo/newgdq-ime-trap.md)）
- [ ] **微软拼音"内嵌候选"模式** 有时把候选字母写入 TextBox.Text，需要测试
- [ ] **五笔输入法** 未测试（理论上五笔提交即字符，无 junk，但需验证）

### UI

- [ ] **段尺**红 14 写死，无段号联动
- [ ] **极简/限制/替换/图片**按钮 hover 有 ToolTip 但点击无效（P4/P5 实现）
- [ ] **"换群"菜单**保留但功能空白（"换群"是 QQ 群相关，可能需要砍）
- [ ] **"暂停"菜单**未实现
- [ ] **进度条文字** 当前是"已打字数,百分比%"格式，可以选择其他显示方式

### 性能

- [ ] **bm.txt 加载** 76145 行同步加载 ~200ms，启动有轻微卡顿。可以改异步
- [ ] **词组下划线** `_charRuns[i].TextDecorations = dec` 对长文章（>5000 字）有渲染延迟
- [ ] **DispatcherTimer 200ms** 速度刷新可能在低端机上掉帧

### 持久化

- [ ] **关闭程序后所有数据丢失**（历史成绩、累计字数、设置全空）—— P5 SQLite 后解决
- [ ] **"今日跟打/天数/记录"**末尾汇总条永远是 0/0/1/0 —— 同上

### 待澄清/讨论

- [ ] "换群"菜单保留与否（无 QQ 后是否还有意义？）
- [ ] "图片成绩"功能保留与否（无 QQ 不发送，复制剪贴板还是有用？）
- [ ] 主题切换：是否要做浅色 / 多种深色配色？还是固定 HandyControl Dark 就行？
- [ ] 字体是否要做用户可配置？（当前对照区固定宋体 22）

---

## 开发约束（重要）

详见 [/memories/repo/zhouzhou-typing.md](../memories/repo/zhouzhou-typing.md) 和 [/memories/repo/newgdq-ime-trap.md](../memories/repo/newgdq-ime-trap.md)：

1. 输入比对**必须**用 `_session.LastInputLen`，**禁用** `TbxInput.Text.Length`
2. 回改用 `TbxInput.PreviewKeyDown` 而非全局钩子
3. 击键统计严格白名单
4. 输入框默认只读，载文后启用，完成后再次只读
5. 产品名"州州跟打器"贯穿所有 UI 文案，**禁止**写"添雨跟打器"

---

## 换机继续开发 checklist

1. `git clone https://github.com/lwl4613615/zhouzhou-typing.git`
2. 安装 .NET Framework 4.8 SDK + Visual Studio 2022（含 WPF）
3. 用 VS 打开 `newgdq/newgdq.csproj`，按 F5 应能构建运行
4. 阅读 [README.md](README.md) 的"项目结构"部分
5. 阅读本文档"开发约束"
6. 选阶段 A 的最小项开始（如"暂停菜单"），熟悉项目后再做 P4
