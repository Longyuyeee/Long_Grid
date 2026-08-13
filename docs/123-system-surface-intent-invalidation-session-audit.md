# Stage 123：系统表面事件与准备态失效人工会话审计

日期：2026-08-13

基线：`main` / `bb49169`（Stage 122、PR #171 已合并且 main CI 通过）

阶段：B6c6（probe 自有系统表面人工会话；正式 DesktopHost 输入、Explicit、显示拓扑 generation 与桌面文件操作仍关闭）

## 1. 目标与审计结论

Stage 122 已建立可由真人操作的短生命周期输入来源，但该窗口没有订阅 Stage 118 的真实 Windows 系统表面事件源，因此无法观察 Prepared Intent 在失焦、Win+D、全屏、锁屏/RDP 或 Explorer 身份变化后是否立即失效。

本阶段增加 `--native-input-system-surface-session`，在同一个 probe 自有来源窗口中订阅公开的 `WindowsProductDesktopInteractionSystemSurfaceEventSource`。危险事件到达时，转发适配器立即 `Invalidate()`，并隐藏来源窗口；事件源只有在连续两个安全样本后才产生 `RecoveryCandidate`，此时适配器回到 `AwaitingPassiveSurface`，窗口以不激活方式恢复。程序只显示有限状态和计数，最终结论固定为 **PendingManualEvidence**。

## 2. 真实观察面

本会话复用 Stage 118 已审计的公开 Windows 读取面：

- `GetShellWindow` 与 `GetForegroundWindow`：桌面显示和 Explorer Shell 身份变化；
- `SHQueryUserNotificationState`：D3D 全屏或演示模式；
- `GetSystemMetrics(SM_REMOTESESSION)`：本地/远程会话变化；
- `SystemEvents.SessionSwitch`：锁定、解锁、登录、注销候选、控制台/RDP 连接变化；
- `SystemEvents.PowerModeChanged`：挂起与恢复；
- probe `WM_KILLFOCUS`：可见来源失焦。

会话本身不会触发 Win+D、全屏、锁屏、RDP、Explorer 重启或电源变化，只观察操作员在受控环境中明确执行的单一场景。

## 3. 失效、隐藏与恢复合同

操作员先在可见来源窗口产生一次 Prepared。随后发生任一危险事件时：

1. 事件必须是定义值、单调序号和有效 UTC 时间；
2. forwarding adapter 同步清除当前 Prepared Intent；
3. 若事件前确有 Prepared，`PreparedIntentInvalidationCount` 增加一次；
4. probe 来源窗口立即隐藏，不留下透明输入层；
5. 连续两个安全样本后产生 RecoveryCandidate；
6. adapter 只回 `AwaitingPassiveSurface`，窗口不激活地恢复；
7. 恢复不重建旧 Prepared，必须由真人重新执行明确动作。

Escape/关闭先销毁 HWND 并结束消息循环；随后会话退订事件、释放 SystemEvents/Timer、Complete adapter，最后由 Dispose 注销随机窗口类。输出只包含事件种类和有限计数，不包含原始键值、窗口标题、路径、进程身份或用户标识。

## 4. 场景范围与未覆盖项

独立启动器只接受：

- `B6C3-05`：失焦、Win+D、全屏切换；
- `B6C3-06`：锁屏/解锁、会话或 RDP 往返；
- `B6C3-07-EXPLORER`：Explorer Shell 身份变化子项。

本阶段不订阅 `ProductDisplayTopologyReader`，输出固定 `DisplayTopologyGenerationObserved: false`。因此显示器热插拔、旋转、DPI/WorkArea 和 topology generation 变化仍属于 Issue #20/A5 的独立矩阵，不能用 Explorer 子项替代。

## 5. 权限与安全边界

本会话继续禁止启动正式 App、全局输入、主动改变系统状态、Admission/Explicit、Intent 消费、桌面文件操作及自动证据或 Pass。启动必须提供匿名操作员、单一场景及受控环境、系统状态变化、显示拓扑限制、禁止 Explicit、恢复方案五项确认。紧急禁用生效时拒绝启动；临时 opt-in 在退出后恢复原值。

## 6. 自动化证据边界

CI 只执行启动器 `-ValidateOnly` 和源码合同，确认真实事件源、Invalidate/AwaitPassiveSurface、Hide/ShowNoActivate、场景限制和禁止能力仍在。Stage 118 已自动覆盖分类器双安全样本、未知全屏 fail-closed、会话/远程/Explorer 事件和退订；Stage 121 自动探针继续覆盖输入归一化与资源清理。

CI 不执行 Win+D、锁屏、RDP、Explorer 重启或物理输入，也不会启动可见窗口。因此本阶段代码完成不等于 B6C3-05～07 人工结果通过。

本机自动化结果：Release 构建 0 warning / 0 error；873/873 测试通过；行覆盖率 91.08%（23030/25286），分支覆盖率 80.64%（7304/9058）；锁定还原、格式、依赖漏洞、启动器正负向门禁、UI 源码合同均通过。真实系统状态矩阵仍未执行，不能由这些数字替代。

## 7. 需求对齐与下一步

本阶段推进了 iTop/Fences 类桌面产品必须具备的系统表面稳定性：一旦桌面显示、全屏或会话环境不安全，交互准备立即失效并移除输入来源；安全恢复不会偷偷复用旧意图。它没有增加桌面文件整理写能力、任务栏美化、Widget/Long助手插件或窗口特效。

下一阶段 B6c7 执行 B6C3-01～06 与 07-Explorer 的真实匿名矩阵，并单独评估把只读显示拓扑 generation 接入 probe 会话。只有输入来源和系统事件两组证据均复核通过后，才可规划正式 App 输入来源；真实文件操作继续后置。
