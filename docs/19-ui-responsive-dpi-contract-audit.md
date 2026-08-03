# Long方格响应式布局与 DPI 窗口合同审计

日期：2026-08-03

状态：**Conditional Pass / 自动化窄窗口子集通过，系统缩放与视觉矩阵未关闭**

风险等级：R1（管理窗口进程内布局；无桌面、Shell、文件或配置接线）

基线：`main` / `e2dde41`（已合入 PR #67 的主题与 UI 自动化合同）

## 1. 目标与需求对齐

上一切片只在默认 1180×760 启动尺寸验证导航与主题。源码审计发现三个明确的窄窗口风险：概览状态固定三列、示例工作区固定四列、品牌表面固定三列，主题选项也固定横排。ScrollViewer 只能处理纵向内容，不能把横向挤压视为响应式完成。

本切片建立管理窗口的最小响应式合同：

- 760 XAML 有效像素及以上使用宽布局；低于 760 使用紧凑布局；
- 紧凑态 NavigationView 使用 `LeftMinimal`，内容边距从 32 缩为 16；
- 三张状态卡、四个匿名工作区项和三张品牌表面均从横排切为纵向流；
- 主题选项从横排切为纵排；
- 水平滚动明确关闭，内容必须重排而不是隐藏在横向滚动后；
- 标题栏徽标显示当前“只读/紧凑布局”状态，不把开发骨架误报为产品能力。

## 2. DPI 审计发现

`AppWindow.Resize(SizeInt32)` 接收物理像素，而 XAML `SizeChanged` 与断点使用有效像素。原先直接传入 1180×760，在本机 200% 缩放下只得到约一半的 XAML 可用宽度，默认窗口会错误进入紧凑态。

最终实现等待 XAML Root 加载后：

1. 读取 `XamlRoot.RasterizationScale`；
2. 将 1180×760 有效像素转换为物理像素；
3. 读取当前窗口所在 DisplayArea 的 WorkArea；
4. 将目标宽高分别限制在工作区的 90%；
5. 由 `RootLayout.SizeChanged` 的有效宽度决定宽/紧凑布局。

这保证高 DPI 下不会把物理像素误当 DIP，也避免默认窗口超出小屏幕工作区。

## 3. 实现选择与纠偏

第一版使用 XAML AdaptiveTrigger。Release 编译能够通过，但在真实窗口从 1154 物理像素缩至 720 时，布局状态没有可靠切换；仅有源码结构不能升级为运行时 Pass。最终改为单一 `ApplyResponsiveLayout`，由 RootLayout 的 `SizeChanged` 确定性设置：

- NavigationView PaneDisplayMode；
- 内容 Padding 与徽标文本；
- Grid Row/Column/ColumnSpan 和行列间距；
- 主题选项 Orientation；
- UIA ItemStatus 诊断值。

同一方法同时写入宽态与紧凑态的完整位置，因此窗口恢复宽度时不会遗留紧凑 Row/ColumnSpan。

## 4. 自动化合同

`eng/Test-LongGridUi.ps1 -ContractOnly` 新增检查：

- 760 断点、RootLayout SizeChanged、LeftMinimal 与 UIA 状态暴露存在；
- 默认尺寸使用 RasterizationScale、DisplayArea 和 90% 工作区上限；
- Content ScrollViewer 禁止水平滚动；
- 20 个关键 AutomationId 唯一；
- 只读 code-behind 不出现文件、目录、桌面目录、Shell 通知或生产 DesktopHost 类型。

真实 UIA 冒烟新增：

1. 等待 DPI 感知默认宽布局稳定；
2. 显式选择概览页；
3. 将准确 App 进程窗口缩至 720 物理像素宽；
4. 验证紧凑徽标和三张状态卡纵向顺序、左右边界；
5. 恢复原宽度并验证宽布局状态返回；
6. 在恢复后的新 UIA 树继续验证焦点、主题 system-dark-system 和安全页。

## 5. 当前证据

| 检查 | 结果 |
|---|---|
| App Release x64 编译 | Pass，0 警告/0 错误 |
| 结构合同 | Pass：20 IDs、1 个断点、DPI 感知尺寸、无水平滚动、只读边界 |
| 宽→紧凑→宽 | Pass，紧凑宽度 720 物理像素 |
| 三张状态卡纵向与左右边界 | Pass |
| 原有键盘/主题/安全页冒烟 | Pass |
| Core 回归与覆盖率 | 88/88；行 91.28%（2366/2592），分支 77.39%（582/752） |
| 配置/文件/缩略图安全门禁 | Pass；文件与缩略图仍保持既有 Conditional Pass 边界 |
| 依赖漏洞 | 未发现已知漏洞 |
| 真实桌面、配置、系统主题 | 未读取、未修改 |

全解决方案锁定恢复、Release build、`dotnet format --verify-no-changes`、启动链和上述本地门禁均通过。PR #68 CI `30788603814` 在 2 分 56 秒内通过构建、启动/UI 合同、测试/覆盖率、配置、文件、缩略图、漏洞和工件上传全部步骤；没有重复出现 PR #67 首轮的 AppContainer profile 瞬时释放竞态。

## 6. 仍未关闭

- 100%/125%/150%/200%/300% DPI 的人工视觉与截图基线；
- Windows“文本大小”设置 100%–200% 后的截断、滚动和焦点检查；
- Narrator、高对比、减少动画、RTL 与长本地化文本；
- 多显示器跨 DPI 拖动和 `WM_DPICHANGED` 后窗口位置/尺寸；
- 极小窗口的产品最小尺寸决策；当前只证明 720 px 受控宽度，不承诺无限缩小；
- 5 人体验测试和真实只读数据接线。

因此本切片仍是 E2 / Conditional Pass，不是“DPI 已全部支持”。下一步应执行人工辅助功能/视觉矩阵或在已批准生产合同后接入匿名、沙箱化的只读 Core 状态。
