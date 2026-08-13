# Stage 115：原生交互 Surface 适配器探针审计

日期：2026-08-13
阶段：B5（探针自有原生 HWND 通过；正式 DesktopHost 仍保持被动只读）

## 1. 目标与结论

Stage 114 已定义 Passive/Explicit/Hidden 的产品事务，但 adapter 仍是接口。本阶段在 `LongGrid.Spikes.DesktopHostWindowModels` 中建立真实 Win32 垂直切片，回答以下问题：

- B4 adapter 能否由真实 HWND 样式、Region、窗口消息和 UIA provider 支撑；
- 显式模式能否在不激活、不拥有前台的情况下发布可命中、可聚焦和 Selection 语义；
- Apply、复核、恢复和隐藏失败能否被真实窗口状态证明，而不是只靠内存假对象；
- 创建、切换、UIA 查询、隐藏、销毁与窗口类注销是否存在持续资源增长。

结论为 **Conditional Pass**。全部自动化语义通过，但真实用户输入、Narrator、触控、笔和硬件会话矩阵仍需人工证据，因此不能直接启用正式 DesktopHost 交互。

## 2. 权限与数据边界

探针只创建一个：

- 随机窗口类名；
- 固定匿名标题；
- 默认隐藏；
- `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED`；
- 非 Topmost、无 Owner；
- 仅含 `container-1`、`item-1`、`item-2` 匿名 ID。

探针不枚举 Explorer 窗口，不寻找 Progman/WorkerW，不读取路径或 Shell identity，不打开或改变桌面文件，不发送合成鼠标/键盘输入，不注册全局 hook，也不修改显示器或会话状态。

## 3. 原生合同

### 3.1 Passive

- HWND 以 `ShowWindow(SW_SHOWNOACTIVATE)` 有界显示，alpha 为 1，仅用于机器复核；
- 保持 ToolWindow、NoActivate、非 Topmost、无 Owner、非前台；
- full Window Region 存在，但 `WM_NCHITTEST` 返回 `HTTRANSPARENT`；
- `WM_MOUSEACTIVATE` 返回 `MA_NOACTIVATE`；
- HWND UIA 根不可键盘聚焦，不提供 Selection pattern。

### 3.2 Explicit

- B4 admission 和 registry generation 通过后，`WM_NCHITTEST` 返回 `HTCLIENT`；
- `WM_MOUSEACTIVATE` 仍返回 `MA_NOACTIVATE`；
- UIA 根可键盘聚焦并提供真实 `ISelectionProvider`；
- `AutomationElement.FromHandle` 和 `TryGetCurrentPattern(SelectionPattern)` 从客户端侧复读成功；
- 窗口始终不成为前台窗口。

### 3.3 Hidden

- 应用空 Region 后调用 `ShowWindow(SW_HIDE)`；
- 复读不可见、HTTRANSPARENT、不可聚焦、无 Selection pattern；
- B4 只有复核到完整 Hidden 合同才报告 `HiddenFailClosed`。

## 4. 事务与故障矩阵

自动化运行覆盖：

1. Passive→Explicit→Esc→Passive 成功往返；
2. ApplyExplicit 已真实改变 HWND 后返回失败，恢复精确 Passive；
3. 注入 Explicit evidence 污染，事务复核失败并恢复；
4. Apply 与 Restore 同时失败，进入并复核 Hidden；
5. Hide 也失败时报告 `EmergencyHideFailed`，不冒充安全；
6. topology generation 漂移取消 lease 并恢复 Passive；
7. 每个检查点复核前台窗口未变化。

## 5. UIA、消息和 Region 证据

探针使用实际 `WM_GETOBJECT`/`AutomationInteropProvider.ReturnRawElementProvider` 暴露 provider，并由 UIA 客户端从 HWND 重新读取属性和 Selection pattern。命中与不激活使用同步窗口消息，不通过合成输入制造通过结果。Window Region 由新建 HRGN 交给 `SetWindowRgn`，再由 `GetWindowRgn` 与 `PtInRegion` 复核实际交互点；Hidden 使用空 Region。每个探针结束时销毁 HWND 并注销随机窗口类。

## 6. 资源审计

预热三次完整 UIA/HWND 周期后采集基线。一次测量的结果为：

- USER：`2→4→2`；
- GDI：`1→2→2`；
- process handles：`346→350→348`。

UIA/WPF 在首次测量后保留一个进程级 GDI 对象和两个进程句柄。探针没有把它描述成净零；它额外执行三个完整的创建、Passive/Explicit UIA 查询、隐藏、销毁周期，并要求 USER/GDI/handle 每轮都精确保持在测量后的平台值。平台期通过，未观察到随 HWND 周期增长。

## 7. CI 与生产隔离

PR/main CI 新增 `--native-interaction-surface --json`。`eng/Test-LongGridUi.ps1` 还要求：

- 探针必须真实包含 B4 adapter、CreateWindowEx、SetWindowRgn、窗口消息、UIA、故障注入和资源平台期；
- 报告必须固定 `SyntheticInputUsed=false`、`DesktopFilesReadOrChanged=false`、`ExplorerWindowInspected=false`；
- `LongGrid.App` 与正式 `WindowsProductDesktopHostReadOnlySurface` 不得引用 B5 类型；
- 正式 read-only surface 的 `WM_NCHITTEST` 继续返回 `HTTRANSPARENT`。

所以本阶段没有改变当前产品可见行为，也没有扩大文件、Explorer、任务栏、插件或发布权限。

## 8. 限制与下一切片

本地最终工程门禁：Release 全解决方案构建 0 warning / 0 error；803/803 自动化测试通过；Cobertura 行覆盖率 91.87%、分支覆盖率 81.18%，高于 90%/75% 门槛；格式、locked restore、142-ID UI 合同、启动链、干净会话、单实例、hang diagnostics、RC restore、依赖漏洞和持久化 20 场景通过。文件操作安全探针保持仅限临时沙箱的 `ConditionalPass`；缩略图 worker 保持隔离/清理/预算通过但提取与恢复要求产品回退的 `ConditionalPass`；人工会话入口通过但真实结果继续 Pending。精确提交 RC、PR CI 与合并后 main CI 在提交后复核。

自动化通过不能替代真实 pointer/keyboard、Narrator、高对比、200% 文本、触控、笔、Win+D、全屏、锁屏、RDP、Explorer 重启和多显示器/DPI 人工矩阵。

下一步建议为 **B6：受控开发 opt-in composition root 与双重关闭开关**：

1. 正式 App composition root 只在 DesktopHost 与 Interaction 两个独立开发开关同时开启时构造 adapter；
2. 启动仍为 Passive，只有 B1 有效意图可短时进入 Explicit；
3. Esc、失焦、Win+D、全屏、会话/RDP、Explorer、到期、异常和 shutdown 强制恢复 Passive/Hidden；
4. 增加进程级 emergency disable，任何未复核状态立即关闭交互并隐藏受影响宿主；
5. 继续不开放真实移动、复制、删除、任务栏美化、Widget 或插件权限。

B6 只有在开发 opt-in 会话矩阵通过后，才可考虑更细的框选、拖动/缩放与安全引用拖放切片。
