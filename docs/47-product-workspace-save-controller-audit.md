# Long方格产品工作区连续保存控制器审计

日期：2026-08-05

基线：`main` / `5c8aa18`（PR #97 已合入）+ Issue #24 连续保存控制器增量分支

证据等级：E3 / Infrastructure product-save orchestration slice

结论：**Captured debounce/latest-result controller pass / Close failure and timeout contract pass / Ordinary MainWindow save and visible UIA still pending / Issue #24 保持 OPEN**

## 1. 本轮准入边界

上一切片已经定义不可变 reducer 和 revision 保存纯状态，但纯状态机只发出命令，不负责计时、保存工作流调用或应用关闭。若直接让 WinUI 控件自行 `Task.Delay` 后调用 `SaveAsync`，会重新引入四类竞态：旧回调晚到、可变状态在等待期被污染、关闭时最新编辑尚未入队、旧失败覆盖新编辑。

本切片新增 Infrastructure `ProductWorkspaceSaveController`，固定链路：

`ProductWorkspaceEditResult → acceptance-time v1 snapshot → bounded debounce → ProductConfigurationSaveWorkflow → finite product save state`

控制器不枚举桌面、不访问 Shell、不移动文件，也未注入 `MainWindow`。匿名开发 UI、启动和普通关闭继续零产品写入。

## 2. 接受时深快照与有限提交结果

控制器只接受 reducer 的有限结果：

- reducer 失败返回 `RejectedEdit`，保留编辑/projector/configuration 错误；
- `Changed=false` 返回 `NoChange`，不递增 revision、不安排计时；
- reducer 返回后若状态被调用方再次破坏，重新投影失败并返回 `InvalidState`；
- 只有再次通过正式 projector/current-v1 validator 的状态返回 `Accepted`；
- 完成关闭后返回 `Completed`，不再接受保存或重试。

`Accepted` 在进入防抖前已经取得独立 `ProductConfigurationDocument` 深快照。调用方随后修改列表、字典或 `JsonElement` 不会改变待保存内容。控制器以后只把该快照传给工作流，不在计时回调中重新读取 UI 状态。

## 3. 可替换调度器与 latest-result 语义

默认防抖为 400 ms，构造边界必须大于 0 且不超过 10 秒。`IProductWorkspaceSaveScheduler` 允许测试用确定性调度器替代系统 `Task.Delay`，自动化不依赖真实时间抖动。

每次新编辑都会取消旧防抖等待并递增 revision。旧等待取消只终止计时，不撤销已经进入正式保存工作流的物理提交。保存进行中出现新编辑时，新 revision 重新等待；旧保存完成只用于结束其自身后台操作，不能把新编辑标为 `Saved`，也不能为新状态显示旧失败或旧重试。

控制器通过 `SnapshotChanged` 发布有限状态。观察者异常被隔离，不能中断保存排序、重试或关闭；没有状态变化的陈旧完成不会制造重复通知。该事件是后续 UI presenter 的输入，不等于本轮已经完成 UIA。

## 4. 正式工作流错误映射与重试

`ProductConfigurationSaveWorkflow` 现在实现最小 `IProductConfigurationSaveWorkflow`，控制器只依赖文档保存、重试和完成三个正式入口。错误映射固定为：

- `InvalidConfiguration → InvalidConfiguration`，不可重试；
- `DamagedEvidence → DamagedEvidence`，可重试；
- `WriteLeaseUnavailable → WriteLeaseUnavailable`，可重试；
- `IoFailure → IoFailure`，可重试；
- 工作流意外没有保留其声称存在的重试快照 → `RetryUnavailable`，不可循环重试。

重试按钮以后只能调用控制器 `Retry`。它只在当前状态机明确允许时进入 `Saving`，并使用工作流已保留的最新失败深快照；新编辑会清除产品层旧失败显示。

## 5. 关闭、超时与资源生命周期

`CompleteAsync` 串行化关闭请求并原子停止接受新提交：

1. 若最新状态仍在防抖等待，取消等待并立即把该深快照送入保存；
2. 等待全部已接受计时/保存/重试操作结束；
3. 最新状态成功后才完成底层工作流；
4. 最新状态失败则返回 `BlockedByFailure`，恢复接受编辑，窗口层必须保留界面供重试或修正；
5. 调用方关闭等待超时只取消本次等待，后台保存继续，控制器恢复接受新编辑；
6. 完成后拒绝后续提交和重试。

控制器实现 `IAsyncDisposable`。释放会先走同一安全完成合同；若最新状态仍失败则抛出明确异常，禁止通过资源释放静默丢弃未保存状态。关闭协调资源只在安全完成后释放。

## 6. 自动证据

- 控制器与保存纯状态定向测试：33/33 通过；
- 覆盖有界防抖、拒绝/无变化、接受时快照、调用后状态破坏、连续编辑、陈旧完成、四错误映射、显式重试、重试快照不一致、关闭强制刷新、失败阻止关闭、超时恢复、完成拒绝、观察者隔离、真实 Store 落盘和失败状态释放保护；
- 全量 Release：275/275 通过；覆盖率行 91.28%（6718/7360）、分支 82.13%（1682/2048），超过 CI 的 90%/75% 门槛；
- Debug/Release 全解决方案构建 0 warning / 0 error，格式、启动链、71 个 UI Automation ID、单实例源码合同和依赖漏洞门禁通过；
- Issue #19/#20/#23/#24 安全会话预检通过，但正确保持 `PendingManualEvidence/ResultsPending/PendingDedicatedEnvironmentEvidence`；PR/main 双重 CI 作为最终远端证据；
- 人工输入、Narrator、动态显示、五人可用性和真实卷结果继续保持 Pending。

## 7. 需求对齐与下一切片

本轮关闭“连续编辑如何可靠进入正式 latest-wins 保存工作流”的非可见基础门槛，直接支持现代化 UI 的连续拖动、调整大小、重命名和锁定反馈，同时保持零惊吓与本地优先。

下一切片才进入可见保存体验：

1. 建立 App 级 controller 所有权，普通产品编辑只经 reducer/controller，不得直接调用 workflow/coordinator；
2. 建立不泄露路径、名称或异常的保存 presenter 与 UIA：`Waiting/Saving/Saved/Failed/Retrying`、重试可用性和稳定 AutomationId；
3. 把 App 关闭改为 controller `CompleteAsync`，覆盖失败保留窗口、超时、第二实例激活与重复关闭；
4. 先用受控内存产品状态接线，确认首次普通保存的加载来源、Catalog 快照和 UIA 自动化后，才允许真实 ordinary save；
5. 动效必须尊重 Reduced Motion，保存成功提示不得闪烁或因陈旧通知回跳。

Issue #24 继续保持 OPEN；本切片不是可见 MVP，也不关闭真实卷、自动证据保留/容量策略、真实 WinUI/显示矩阵或正式 v2 稳定身份字段。

## 8. 2026-08-12 修订提交准入补充

main CI 暴露了“旧完成不覆盖新状态”之外的入队竞态：旧修订可能在让出执行权后晚于新修订调用工作流，使旧文档成为最后入队内容。Stage 102 增加普通保存的 `Saving + Save activity + CurrentRevision + ActiveSaveRevision` 四条件准入，并以窄提交门固定工作流调用顺序；重试保持独立入口。受控调度测试强制新修订先恢复、旧修订后恢复，证明旧文档不会进入工作流。详见[连续保存修订准入与 CI 确定性审计](102-save-revision-admission-determinism-audit.md)。
