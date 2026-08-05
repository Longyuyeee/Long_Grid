# Long方格 App 保存状态与关闭接线审计

日期：2026-08-05

基线：`main` / `c1e7542`（PR #98 已合入）+ Issue #24 App 保存状态增量分支

证据等级：E2-E3 / App lifecycle and UIA contract slice

结论：**App controller ownership and privacy-safe static save UI contract pass / Ordinary product submissions remain zero / Final live UI rerun blocked by stale inaccessible instance / Issue #24 保持 OPEN**

## 1. 本轮准入边界

Infrastructure 已经能可靠编排连续保存，但 App 仍直接持有底层 workflow 并在关闭时只排空 workflow；可见界面也没有 Waiting/Saving/Saved/Failed/Retrying 合同。本轮完成 App 所有权、关闭和可见状态层，但不把匿名练习容器提交给控制器。

固定边界：

- App 中普通 `SaveAsync/EnqueueAsync` 调用为 0；
- MainWindow 中普通 `SaveAsync/EnqueueAsync` 调用为 0；
- 首次整理、示例工作区、匿名容器、拖放练习和恢复预览继续只在内存中；
- 启动和普通关闭在没有真实产品编辑时不创建产品配置；
- 恢复/导入/导出/证据操作仍是用户明确触发的独立配置事务，不伪装成普通自动保存。

## 2. App 唯一控制器所有权

`App` 现在创建 `ProductConfigurationSaveWorkflow` 后立即封装为唯一 `ProductWorkspaceSaveController`。MainWindow 只能获得有限 `Retry` 委托，不能获得 Store、workflow、coordinator 或文档保存入口。

控制器 `SnapshotChanged` 可能来自后台完成线程；App 检查 `DispatcherQueue.HasThreadAccess`，必要时 `TryEnqueue` 到窗口线程，再调用 `ApplyProductWorkspaceSaveState`。后台保存不能直接触碰 XAML 元素。

关闭链改为：

1. `AppWindow.Closing` 先取消系统关闭并保持 5 秒上限；
2. 调用 controller `CompleteAsync`，由它强制刷新最新等待编辑；
3. 超时只恢复窗口和关闭入口，已接受保存继续；
4. `BlockedByFailure` 保持窗口，重新呈现有限失败与重试；
5. 成功后 await controller `DisposeAsync`，再释放单实例并关闭窗口。

WinUI `Application` 生命周期由 XAML/COM 框架持有，不适合额外投影为 `IAsyncDisposable`。实际构建曾据 CA1001 要求尝试增加该接口，真实运行出现 `Microsoft.UI.Xaml.dll` 原生访问冲突；现已撤销接口，在 App 类型上用窄范围、带理由的 CA1001 抑制，并仍由审计关闭处理器显式 await 控制器释放。该抑制不覆盖控制器本身，也不允许失败时强制丢弃状态。

## 3. 隐私安全保存状态卡

概览新增 5 个稳定 AutomationId：

- `ProductSaveStatusCard`；
- `ProductSaveStatusTitle`；
- `ProductSaveStatusDetail`；
- `ProductSaveMotionPolicy`；
- `ProductSaveRetryButton`。

初始状态不是“已保存”，而是“自动保存待命 / 尚无需要保存的产品编辑”，UIA 为 `WorkspaceSaveClean:Revision=0:Motion=Static`。这避免在尚未建立真实产品状态时制造成功假象。

状态映射覆盖：

- `Clean → WorkspaceSaveClean`；
- `WaitingForDebounce → WorkspaceSaveWaiting`；
- `Saving + Save → WorkspaceSaveSaving`；
- `Saving + Retry → WorkspaceSaveRetrying`；
- `Saved → WorkspaceSaveSaved`；
- `Failed → WorkspaceSaveFailed:<finite failure>`。

UIA 只携带状态枚举、是否可重试和 revision，不包含配置 Document、路径、Catalog 名称、桌面显示名、原始异常或错误消息。重试按钮默认 `Collapsed + Disabled`，只在当前有限失败 `CanRetry=true` 时出现；点击只调用 controller `Retry`。

## 4. Save/Retry 活动语义

Core `ProductWorkspaceSaveSnapshot` 新增有限 `Activity=None/Save/Retry`：

- 普通防抖到期进入 `Save`；
- 显式重试进入 `Retry`；
- 新编辑、成功或失败完成恢复 `None`。

因此 UI 不需要根据按钮点击时间或文案猜测“保存中”是否为重试。旧完成被状态机忽略时也不会让 `Retrying` 回跳到旧状态。

## 5. Reduced Motion 基线

保存状态卡不包含 `Storyboard` 或 `Transition`，状态变化使用静态图标、标题、详情和按钮可用性切换；`ProductSaveMotionPolicy` 明确提示“静态状态切换 · 遵循减少动画偏好”。这形成 Reduced Motion 安全基线。

本轮没有声称整个应用已经完成最终动效设计。未来可在系统允许动画时加入短淡入，但必须保持状态顺序、UIA 文本和 Reduced Motion 下零动画，不能让保存成功闪烁或让陈旧通知回跳。

## 6. 自动与真实窗口证据

- 保存控制器/状态机定向测试：33/33 通过，新增断言 Save/Retry 活动与完成复位；
- 全量自动测试：275/275 通过；覆盖率 lines 91.26%（6728/7372）、branches 82.03%（1680/2048），继续高于 90%/75% 门禁；
- UIA 源码合同从 71 增至 76 个稳定 AutomationId，检查初始有限状态、Polite live region、重试默认关闭、静态动效、App controller 所有权、失败阻止关闭、异步释放及 App/MainWindow 零直写；
- Debug/Release 全解决方案构建：均为 0 warning / 0 error；
- 启动、单实例、Issue #19/#20/#23/#24 安全会话链与依赖漏洞门禁：全部通过；真实人工/专用环境证据继续保持 Pending；
- 首次真实 UIA 运行在尝试让 WinUI App 实现 `IAsyncDisposable` 时捕获两次 `Microsoft.UI.Xaml.dll` `0xc0000005`，该接口已撤销；
- 修正后的进程未产生新的崩溃事件，但先前崩溃留下无窗口 PID 39208，当前会话无法终止（Access Denied），固定单实例键把后续启动转发给该残留实例，最终真实 UIA 复跑因此为 **Inconclusive / environment contaminated**，不得记为 Pass；
- CI 继续执行源码 UIA 合同；需要在干净登录会话重新运行真实 UIA，确认状态卡可见和关闭退出后，才能升级实机证据。

## 7. 需求对齐与下一步

本轮完成了现代保存反馈所需的 App 生命周期、隐私、安全和无动效基线，但没有制造真实产品编辑来源。下一条产品切片应：

1. 在干净 Windows 会话重跑真实 76-ID UIA 与正常关闭，关闭本轮 Inconclusive；
2. 建立“正式配置加载 + 当前产品 Catalog 快照 → ProductWorkspaceState”的 App 会话所有权；
3. 对缺失/类型变化/歧义项目提供只读保留、显式重新选择和删除确认，不用匿名练习数据替代；
4. 只有上述加载来源、UIA 和关闭证据通过后，才把真实 reducer `Changed=true` 结果首次提交 controller；
5. 保持桌面文件零移动；普通配置保存不等于文件整理执行。

Issue #24 保持 OPEN；真实卷证据、自动证据保留/容量策略、真实动态显示/Narrator/Reduced Motion 矩阵和正式 v2 稳定身份字段仍未关闭。
