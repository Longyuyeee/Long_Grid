# Stage 116：受控开发态交互 Composition Root 基础审计

日期：2026-08-13
阶段：B6a（开发态策略接线通过；产品 Surface adapter 与显式输入仍关闭）

## 1. 目标与结论

Stage 115 已证明 B4 adapter 可以由真实、探针自有 HWND 实现，但正式 App 仍完全不知道交互策略。直接把探针代码迁入正式 HWND 会同时扩大启动、输入、UIA 和原生资源风险。因此 B6 被拆为两个有明确出口的切片：

- B6a：让 App composition root 持有受控开发运行时，先证明开关、紧急禁用、系统表面暂停、被动恢复和退出收口；
- B6b：只在 B6a 门禁后构造产品 Surface adapter，先开放 Hidden/Passive 生命周期，再单独审计 Explicit 输入。

B6a 结论为 **Pass（策略与 Composition Root 基础）**。它不是桌面直接交互功能验收，也不改变当前用户可见行为。

## 2. 三层开关与优先级

App 启动时按以下顺序形成不可混用的决定：

1. `LONGGRID_DISABLE_DESKTOP_INTERACTION=1`：精确 emergency-disable，最高优先级；
2. `LONGGRID_ENABLE_DESKTOP_HOST=1`：DesktopHost 开发 opt-in；
3. `LONGGRID_ENABLE_DESKTOP_INTERACTION=1`：Interaction 独立开发 opt-in。

只有后两个值都按 Ordinal 精确等于 `1`，且 emergency-disable 不等于 `1`，控制器才进入 Passive。空值、`0`、`true`、前后空格和大小写近似值都不能开启能力。emergency-disable 的近似值也不会被误判为已执行熔断，避免配置文本被模糊解释。

## 3. Composition Root 所有权

`App` 唯一持有一个 `ProductDesktopInteractionDevelopmentController`：

- 它与现有 `ProductDesktopHostLifecycleController` 分离，不能用 Host 开关顺带开启交互；
- 启动只读取有限环境值，不向 MainWindow 传递 HWND、路径、进程/线程 ID 或交互对象；
- shutdown 在释放 DesktopHost 原生生命周期前先调用 `Complete`，发布永久 HiddenRequired 的完成快照；
- 没有 App 入口能够创建 `ProductDesktopInteractionIntent`、调用 hit-test、构造 B4 transaction 或取得 Surface adapter。

## 4. Fail-closed 状态机

开发控制器只包含：

- `DisabledBySafetyPolicy`：任一 opt-in 缺失；
- `Passive`：双 opt-in 成立、无 lease、无原生 adapter；
- `SuspendedFailClosed`：系统表面变化后要求相关宿主隐藏；
- `EmergencyDisabled`：进程内不可逆紧急禁用；
- `Completed`：应用退出后的终态。

失焦、Esc、Win+D、全屏、锁屏/断开、RDP 和 Explorer 重启都走统一 cancellation 语义。恢复 Passive 必须重新证明 NativeHost connected、ReadyReadOnly、只读 UIA、Passive window contract、workspace/topology/window-registry 三类正 generation，以及有效的容器集合。证明不完整时保持 SuspendedFailClosed。

运行时 `EmergencyDisable` 是幂等且不可逆的；完成状态同样幂等。二者都要求隐藏，不能被后续恢复调用重新开启。

## 5. 权限和生产隔离

B6a 快照固定：

- `NativeSurfaceAdapterConnected=false`；
- `RealFileOperationsAllowed=false`。

控制器源码不引用 `System.IO`、`File`、`Directory`、`IFileOperation`、MoveFile 或 DeleteFile。正式 `WindowsProductDesktopHostReadOnlySurface` 仍让 `WM_NCHITTEST` 返回 `HTTRANSPARENT`，App 不引用 `IProductDesktopInteractionSurfaceModeAdapter`、B4 transaction、hit-test adapter 或 intent factory。因此双 opt-in 当前只允许审计策略运行，不会让桌面窗口拦截点击。

本阶段也没有扩大 Explorer、任务栏、小组件、插件、全局 hook、合成输入或桌面文件权限。

## 6. 自动化矩阵

新增确定性测试覆盖：

1. 零开关、仅 Host、仅 Interaction 均关闭；
2. 精确双开关只从 Passive 启动，无 lease、adapter 或文件能力；
3. emergency-disable 精确值优先，近似值不误触发；
4. Esc、失焦、Win+D、全屏、会话、RDP、Explorer 信号全部进入 fail-closed；
5. 缺少 Passive attestation 时拒绝恢复，完整证明才恢复；
6. 运行时 emergency-disable 不可逆且幂等；
7. shutdown 完成幂等且永久要求隐藏；
8. EvidenceChanged/LeaseTimerElapsed 不能伪装为无证据系统切换。

静态 UI 门禁同时证明 Composition Root 唯一所有权、三个环境策略、退出调用、零文件 API、App 零 Surface/命中接线以及正式 HWND 继续穿透。

## 7. 验证与限制

本地工程门禁结果：Release 全解决方案构建 0 warning / 0 error；822/822 自动化测试通过；Cobertura 行覆盖率 91.96%（21094/22938），分支覆盖率 81.41%（6524/8014），高于 90%/75% 阈值；格式、locked restore、142-ID UI 合同、启动、干净会话、单实例、CI hang、RC restore、配置持久化 20 场景与依赖漏洞门禁通过。B5 真实 HWND 探针继续为 Conditional Pass 且资源平台期通过；文件安全探针继续仅在随机临时沙箱内 ConditionalPass；缩略图 worker 的隔离、清理和预算通过，但本机受控 AppContainer 提取/恢复仍需产品 fallback，因此保持 ConditionalPass。内部 unsigned RC、PR CI 和合入后的 main CI 是提交后的最终交付证据。

B6a 没有验证真实产品 adapter，也没有验证真实鼠标、键盘、Narrator、触控、笔、Win+D、全屏、锁屏、RDP、Explorer 重启或多显示器 DPI 会话。A5-01..A5-06 继续保持 PendingManualEvidence，不能被本阶段自动化改写为通过。

## 8. 下一切片 B6b

B6b 只做产品 adapter 的 Hidden/Passive 生命周期：

1. 仅在双 opt-in 成立且 emergency-disable 未触发时由 App composition root 构造；
2. 复用产品拥有窗口 registry，不枚举或挂接 Explorer；
3. 启动先 Hidden，完整 Host/Topology/Workspace/Registry 证明后才 Passive；
4. 系统表面信号、证明漂移、异常和 shutdown 必须隐藏全部相关宿主；
5. 恢复前复读 Window Region、ToolWindow/NoActivate、非 Topmost、无 Owner、前台不变与 UIA 被动合同；
6. 继续不提供 Explicit、hit-test 命中、Selection pattern、真实文件操作、任务栏或插件权限。

只有 B6b 的创建/暂停/恢复/紧急隐藏/销毁和资源平台期通过后，才允许为 Explicit 输入建立 B6c 的独立准入与人工会话矩阵。
