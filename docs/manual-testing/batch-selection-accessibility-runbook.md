# 批量选择无障碍人工矩阵运行手册

状态：**Ready to execute / Pending manual evidence**

本手册验证正式 `LongGrid.App` 工作区中的批量选择操作栏，而不是 DesktopHost 探针。范围固定为纯键盘、Narrator、高对比度、200% 文本缩放和小于 760 DIP 的紧凑布局。`-ValidateOnly`、CI、AutomationId 或源码断言只证明入口和结构合同存在，不能代替人工听读、焦点视觉、缩放或可操作性结论。

## 1. 安全与隐私边界

- 仅在 Windows 11 x64 专用测试账户或无个人内容的可抛弃测试配置中执行；
- 桌面只放置匿名测试项目，仓库和结果中不得记录用户名、路径、真实文件名、窗口标题、设备标识、原始截图或录音；
- 正式记录只使用 `O1`–`O9` 匿名操作员标签；
- 启动器只读枚举桌面第一层元数据，不读取文件内容，不创建、移动、重命名或删除桌面文件；
- 启动器不切换 Narrator、高对比度或文本缩放，不采集证据，不写结果文件，也不终止它没有启动的进程；它持有本轮 App 进程句柄，只有启动失败时才清理本轮由自身启动的进程；
- 场景只验证选择和清除选择，不点击“批量加入并保存”“批量移除并保存”或撤销按钮；准备阶段产生的 Long方格测试配置应留在专用账户中处理；
- 每轮只执行一个 BSA 场景。失败后先恢复窗口、主题、文本缩放和 Narrator 基线，再决定是否继续。

## 2. 环境与数据准备

人工执行前准备：

1. Windows 11 x64 专用测试账户，桌面至少有 3 个匿名、安全的自有测试项目；
2. Long方格内至少有 2 个未锁定正式方格，其中一个方格已有至少 2 个匿名引用，未分组列表仍至少有 2 项；
3. 记录 commit、Windows build、DPI、显示器数量、主题、文本缩放、Narrator 状态和窗口宽度；
4. 记录恢复计划：关闭 Long方格、关闭 Narrator、恢复原主题/文本缩放/窗口宽度；
5. 确认没有正在运行的 `LongGrid.App`。启动器遇到现有进程会拒绝继续，且不会终止该进程。

准备数据只为建立可复读状态，不属于任何 BSA 场景结果。不得用个人桌面或唯一工作账户执行。

## 3. 会话预检与启动

先执行不打开窗口的合同预检：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-LongGridBatchAccessibilitySession.ps1 `
  -ValidateOnly
```

正确输出必须同时包含：

- `requiredAutomationIds: 138`；
- `focusedAutomationIds: 8`；
- `resultStatus: PendingManualEvidence`；
- `launcherChangesDesktopFiles: false`；
- `terminatesForeignProcess: false`。

示例：由匿名操作员 O1 启动纯键盘场景：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-LongGridBatchAccessibilitySession.ps1 `
  -Scenario BSA-01 `
  -OperatorId O1 `
  -Configuration Release `
  -DedicatedTestAccountConfirmed `
  -RecoveryPlanConfirmed
```

启动后只执行所选场景。关闭 Long方格后，启动器还会复核没有残留 `LongGrid.App` 进程。

## 4. 场景矩阵

| ID | 场景 | 固定动作 | 人工通过条件 | 恢复确认 |
|---|---|---|---|---|
| BSA-01 | 纯键盘 | 不使用鼠标；用 Tab/Shift+Tab 到达两个多选列表及 4 个选择操作按钮；用方向键、Ctrl/Shift、Enter/Space 完成选择、扩展和清除 | 焦点顺序与视觉顺序一致且始终可见；标准多选可用；两个“选择前 256 项”入口均有限；清除后数量为 0；没有焦点陷阱 | 选择清空，窗口可正常关闭，无残留进程 |
| BSA-02 | Narrator | 人工开启 Narrator；分别调用“选择前 256 项”“选择此方格前 256 项”和两侧“清除选择”；再用 Ctrl/Shift 改变选择 | Name、角色、禁用/可用状态和列表语义可理解；每次用户动作只听到一次最终数量播报，不逐项重复最多 256 次；清除播报数量 0；能听到桌面文件未改变语义 | 人工关闭 Narrator，选择清空，音频状态恢复 |
| BSA-03 | 高对比度 | 人工记录原主题后切换 Windows 对比度主题；遍历列表、选中项、禁用按钮、焦点和状态文本 | 焦点、选中、未选中、禁用、悬停/按下状态不只依赖细微颜色；文字、边框和焦点标记可辨；操作语义与普通主题一致 | 恢复原主题并复核界面恢复，无残留设置 |
| BSA-04 | 200% 文本缩放 | 人工记录原文本大小并切换到 200%；重新启动单独会话；遍历两个列表、4 个操作按钮、状态文本和确认区 | 文本不截断、不重叠、不遮挡关键状态；按钮仍可聚焦和调用；列表与状态区可滚动到达；没有为看清内容而必须水平滚动 | 恢复原文本大小并重新登录/重启应用（如系统要求） |
| BSA-05 | 紧凑宽度 | 把窗口缩窄直到进入小于 760 DIP 的紧凑状态；用键盘遍历两组纵向重排按钮，再恢复宽布局 | 两组按钮均按“选择→清除”纵向排列且不重叠；可见顺序、Tab 顺序和 Narrator 顺序一致；宽→紧凑→宽可逆；状态文本保持可见 | 恢复原窗口宽度，布局无残留错位 |

## 5. 不可由自动化代替的判定

以下事实必须由人工确认：

- Narrator 是否只朗读一次最终选择状态，以及文案是否自然、可理解；
- 高对比度下焦点、选择和禁用状态是否真的可区分；
- 200% 文本缩放下是否存在视觉裁切、遮挡或不可达控件；
- 紧凑布局的视觉顺序是否与键盘/听读顺序一致；
- 操作过程中是否出现焦点跳失、重复播报、布局震荡或无法恢复。

若环境不具备 Narrator、系统设置恢复权限或准备数据，结果记为 `Inconclusive`，不得改写为 Pass。首次失败必须保留；重试成功只能作为补充记录。

## 6. 脱敏结果模板

每个场景单独记录，结果只允许 `Pass`、`Fail` 或 `Inconclusive`：

```text
Scenario: BSA-0X
Operator: O1-O9
Commit: <12/40 hex>
Environment: Windows build / x64 / display count / DPI / theme / text scale
PreparedWorkspace: anonymous-items>=3 / unlocked-containers>=2 / resolved-references>=2
FirstRunResult: Pass | Fail | Inconclusive
ObservedFocusOrder: Pass | Fail | Inconclusive
ObservedNarratorAnnouncement: Pass | Fail | Inconclusive | NotApplicable
ObservedVisualClipping: None | Present | Inconclusive
DesktopFilesChanged: False
RecoveryConfirmed: True | False
DefectReference: none | sanitized issue id
Notes: no names, paths, titles, screenshots, recordings, or device identifiers
```

只有固定动作全部完成、人工检查全部通过、`DesktopFilesChanged=False` 且恢复确认成功时，单场景才可标记 Pass。五个场景全部有真实记录之前，整体状态持续为 `PendingManualEvidence`。
