# Stage 118：系统表面事件与 Fail-Closed 桥审计

日期：2026-08-13
阶段：B6c1（系统事件接线通过；Explicit、意图签发与真实文件操作仍关闭）

## 1. 目标与结论

Stage 117 已证明产品自有 HWND 可以在 Hidden 与 Passive 之间安全往返，但 App 除 shutdown 外还没有真实系统事件来源。B6c1 的目标是把公开、有限、可审计的 Windows 状态转换为单向安全信号：系统状态不确定时隐藏产品 Surface；稳定后只能重新复核既有 Passive 合同，不能借此进入 Explicit。

本阶段结论为 **Conditional Pass**：事件分类、单调序号、并发串行化、双样本恢复、原生 HWND Hidden/Passive 往返和资源释放由自动化覆盖；真实 Win+D、全屏、锁屏、RDP、Explorer 重启、睡眠与多显示器会话仍须 A5/Issue #19/#20 人工矩阵，因此不能把自动化结果写成真实会话 Pass。

## 2. 事件来源与权限边界

`WindowsProductDesktopInteractionSystemSurfaceEventSource` 只在 DesktopHost 与 Interaction 两个精确 opt-in 同时成立时创建，并使用以下公开读取面：

- `GetShellWindow` 与 `GetForegroundWindow`：判断桌面 Shell 是否成为前台，以及 Shell 身份是否变化；
- `SHQueryUserNotificationState`：识别 D3D 全屏或演示模式；查询失败按不安全处理；
- `GetSystemMetrics(SM_REMOTESESSION)`：识别本地/RDP 状态变化；
- `SystemEvents.SessionSwitch`：识别锁定、解锁、登录、注销和控制台/远程连接变化；
- `SystemEvents.PowerModeChanged`：识别挂起与恢复；
- WinUI `Window.Activated`：控制中心失焦时报告有限 `FocusLost`。

没有全局键鼠 Hook、输入合成、前台窗口切换、窗口枚举、WorkerW/Progman 挂接、Explorer 注入或私有 Shell 协议。观察器不读取路径、标题、进程身份、文件内容或显示器身份，也不把 HWND 写入产品快照。

## 3. 有限事件合同

Core 只公开七种事件：

1. `FocusLost`；
2. `DesktopRevealRequested`；
3. `FullScreenTransition`；
4. `SessionUnavailable`；
5. `RemoteSessionTransition`；
6. `ExplorerRestarted`；
7. `RecoveryCandidate`。

每个事件携带进程内单调 `Sequence` 和 UTC 观察时间。生命周期拒绝零序号、默认时间、未定义值与迟到/重复序号。前六种只能映射到既有 cancellation 合同；`RecoveryCandidate` 明确不能转换为取消信号。

## 4. Fail-Closed 与恢复

危险事件到达时：

1. App 将事件封送到自身 DispatcherQueue；
2. DesktopHost 生命周期按序号去重；
3. 若尚无产品 Surface，仅消费序号，不制造 Fault；
4. 若 Surface 存在，交互控制器先取消 lease（如有）并要求 Hidden；
5. adapter 应用空 Region/隐藏并复核 Hidden contract；
6. 生命周期发布 `SuspendedSystemSurface`，保留匿名窗口数量和拓扑代次，但 `PassiveWindowContractAttested=false`；
7. 隐藏失败时释放全部 Surface 并进入 `Faulted`。

恢复不是单一事件的反操作。分类器要求连续两个安全样本，且 Shell 可用、桌面不在前台、非全屏、会话可用、电源已恢复；随后生命周期重新构造当前 workspace/topology/window-registry generation 证据，并复核 NativeHost、只读 UIA 与 Passive window contract。只有全部成立才回到 `ReadyReadOnly`，否则保持 Hidden。

## 5. 并发、稳定性与资源

- 1 秒采样周期只执行四个只读系统查询；不扫描窗口、文件或进程；
- timer 重入由原子门禁拒绝，分类器状态由单锁串行化；
- 系统事件、Timer 和 WinUI 失焦可并发到达，但对外序号严格递增；
- Start 在订阅 App handler 后执行，避免首个不安全状态丢失；
- 任一订阅或 Timer 启动失败会回滚已建立的订阅和资源；
- shutdown 先退订 `SurfaceChanged`，再注销 SystemEvents 并释放 Timer，之后才 Complete/销毁 HWND；
- 已封送到 UI 队列但晚于关闭门禁的事件只读取当前快照，不再调用生命周期；
- 原生回调异常不能撕裂进程，后续样本仍保持 fail-closed。

## 6. 自动化证据

新增或扩展测试覆盖：

- 桌面显示后必须连续两个安全样本才产生恢复候选；
- 全屏查询未知时按危险状态处理；
- Shell 身份变化与远程状态变化形成有限事件；
- 锁屏/电源恢复前不能生成恢复候选；
- 七种 Core 事件与既有 cancellation 合同严格映射；
- Recovery 不能伪装为 cancellation；
- 真实生命周期执行 Passive → Hidden → Passive；
- 迟到、重复与无效事件不能改变状态；
- Surface 创建前的事件不能制造 Fault；
- UI 静态合同禁止 Hook、SendInput、SetForegroundWindow、WorkerW/Progman、Explicit/Intent/File 接线。

Release 构建为 0 warning / 0 error；843/843 自动化通过；Cobertura 行覆盖率 90.98%（21940/24116），分支覆盖率 80.22%（6888/8586），高于 90%/75% 门槛。RC 哈希与 PR/main CI 结果在提交收口时复核。

## 7. 需求对齐

本阶段直接推进“桌面文件整理宿主在 Win+D、全屏、会话、RDP 和 Explorer 变化时不遮挡、不抢焦点、可安全恢复”的基础能力，也改善稳定性：不确定即隐藏，恢复必须重新证明，采样成本固定且无窗口枚举。

本阶段没有开放：

- 产品 hit-test、B1 intent 签发、二次用户动作或 Explicit；
- 键盘焦点、Selection、Invoke、拖放或文件移动/复制/删除；
- 任务栏美化、小组件、Long助手插件运行时；
- Explorer/WorkerW/Progman 挂接或未文档化桌面层方案。

## 8. 下一切片 B6c2

B6c2 才建立 Explicit 前的最小产品意图桥与人工会话门禁：

1. 必须有独立、精确、默认关闭的受控会话许可；
2. 必须由明确用户动作命中唯一方格，不能由系统事件自动触发；
3. intent 最长 5 秒并绑定 workspace/topology/registry generation；
4. Surface 切到 Explicit 前后都要原子复核与失败补偿；
5. Esc、失焦、本阶段全部系统信号、证据漂移和超时立即 Hidden/Passive；
6. 先跑专用人工键鼠/触控/Narrator/Win+D/全屏矩阵，真实文件操作仍保持关闭。

在 B6c2 和人工证据完成前，正式 HWND 继续固定 `HTTRANSPARENT`，产品 adapter 的 `ApplyExplicit` 继续返回 false。
