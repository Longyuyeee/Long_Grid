# Stage 120：隔离输入转发与人工证据门禁审计

日期：2026-08-13

基线：`main` / `c0d39c7`（PR #168 合并且 main CI 通过）

阶段：B6c3（只把归一化输入转给 Intent 准备；正式输入捕获、Explicit 与真实文件操作仍关闭）

## 1. 审计目标

Stage 119 已能在完整 Passive 证据下准备 5 秒 Intent，但 App 没有输入转发边界。B6c3 的目标不是让正式 HWND 拦截输入，而是建立一个可独立关闭、可防重放、可审计的归一化转发层，证明“一次已确认动作最多触发一次准备”。

本阶段必须同时满足：

1. Host、Interaction、Intent bridge、Input forwarding 与两个对应人工会话确认均精确为 `1`；
2. 只接受来源已证明、非注入、非自动重复、序号递增且 ActionId 未使用的有限输入；
3. 指针、键盘和辅助技术只映射为 B2 已定义的三种 activation；
4. 唯一出口是 B6c2 `ProductDesktopInteractionIntentPreparationBridge`；
5. 系统表面变化、Surface/证据变化、关闭与显式失效同步撤销准备；
6. 正式 HWND 继续 `HTTRANSPARENT`，产品 adapter 继续拒绝 Explicit；
7. 不安装 Hook、不读取原始按键流、不发送合成输入、不调用文件 API。

## 2. 代码边界

### 2.1 第四重默认关闭策略

`ProductDesktopInteractionInputForwardingPolicy` 新增：

- `LONGGRID_ENABLE_DESKTOP_INPUT_FORWARDING=1`；
- `LONGGRID_ACKNOWLEDGE_DESKTOP_INPUT_FORWARDING_SESSION=1`。

两个值都采用 `StringComparison.Ordinal` 精确比较。上游 Intent bridge 未启用时，下游值无论为何都不能开启转发。

### 2.2 隔离转发适配器

`ProductDesktopInteractionInputForwardingAdapter` 只接收有限的 `ProductDesktopInteractionForwardedInput`：

- `PrimaryPointerPress`；
- `KeyboardActivation`；
- `AssistiveTechnologyActivation`。

输入必须携带非空 ActionId、正序号、非未来时间、显示器 ID、非负客户区坐标和来源证明，并明确不是注入或自动重复。适配器保留最多 64 个近期 ActionId；序号回退或 ActionId 重用不能形成第二次准备。容量固定，长期会话不会无界增长。

适配器没有 Windows 输入源、窗口消息循环、全局 Hook、Raw Input 或键盘状态查询。它不声称操作系统级“可信输入”，只验证上游隔离源提供的有限证明；真实来源必须由后续独立探针和人工矩阵验证。

### 2.3 生命周期接线

App composition root 构造策略、B6c2 bridge 与转发适配器，再交给 `ProductDesktopHostLifecycleController`。App/MainWindow 没有调用 `ForwardInteractionInput`，因此当前产品不会自行捕获桌面动作。

生命周期只有在 `ReadyReadOnly`、Passive adapter 和当前 registry generation 全部成立时才允许转发。失焦、Win+D、全屏、会话/RDP、Explorer、拓扑替换、Surface 释放和 shutdown 都先失效适配器及 bridge；恢复仍只能回到 Passive。

## 3. 状态与权限证明

转发状态限定为：

`DisabledBySafetyPolicy → AwaitingPassiveSurface → Prepared/PreparationRejected/ReplayedInput/InvalidInput → Invalidated → Completed`

所有快照固定声明：

- `CapturesGlobalInput = false`；
- `SendsSyntheticInput = false`；
- `ExplicitInteractionEntered = false`；
- `RealFileOperationsAllowed = false`。

适配器不引用 `ProductDesktopInteractionAdmissionController`、`ProductDesktopInteractionSurfaceModeTransaction`、`ApplyExplicit`、`System.IO` 或文件操作 API。正式 Native adapter 的 `ApplyExplicit` 仍返回 `false`，正式窗口的 `WM_NCHITTEST` 仍返回 `HTTRANSPARENT`。

## 4. 验证矩阵

自动化覆盖：

- 精确第四开关与精确人工确认；
- 上游 bridge 关闭优先；
- 指针、键盘、辅助技术等价映射；
- 未证明来源、注入、自动重复、非法枚举与结构异常拒绝；
- 序号和 ActionId 双重防重放；
- 命中失败/锁定/陈旧证据只得到有限拒绝；
- 系统事件、Surface 释放和 shutdown 联动失效；
- 快照持续证明无捕获、无合成、无 Explicit、无文件权限；
- 会话启动器缺少任一确认时拒绝，并在退出后恢复进程环境。

CI 新增 `Start-DesktopInteractionInputForwardingSession.ps1 -ValidateOnly`，静态合同同时禁止 Hook、Raw Input、合成输入、Admission、Explicit 和文件 API 回流。

## 5. 人工证据与当前结论

人工手册定义 B6C3-01 至 B6C3-08，覆盖鼠标、键盘、Narrator/UIA、重放/注入标记、Win+D/全屏、锁屏/RDP、Explorer/显示变化及紧急退出。启动器不会记录原始输入、路径、标题或用户身份，也不会自动写结果文件。

当前代码和 CI 只能给出合同级结论；真实设备与 Narrator 矩阵尚未执行，因此 B6c3 总结果为 **PendingManualEvidence**，不能表述为正式桌面输入已经可用。

## 6. 需求对齐与下一步

本阶段推进了最初“桌面直接操作、鼠标/键盘/触控/辅助技术一致、失败可退出”的底层安全链，但用户仍看不到可点击的正式桌面方格。任务栏美化、窗口特效、Widget Host 与 Long助手插件权限没有提前混入核心常驻路径。

下一切片 B6c4 应使用仅由测试会话拥有的隔离原生窗口验证真实 pointer/key/UIA Invoke 来源归一化和一次动作一次准备，并执行 B6C3 人工矩阵。只有该证据通过后，才能单独评审正式 HWND Explicit Surface；真实文件移动/复制/删除继续后置。
