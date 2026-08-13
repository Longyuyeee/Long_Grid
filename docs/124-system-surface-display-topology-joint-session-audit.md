# Stage 124：系统表面与显示拓扑联合失效人工会话审计

日期：2026-08-13

基线：`main` / `4e9617a`（Stage 123、PR #172 已合并且 main CI 通过）

阶段：B6c7（probe 自有人工会话的只读显示拓扑接入；正式 App 输入、Explicit、Intent 消费与桌面文件操作仍关闭）

## 1. 目标与结论

Stage 123 已能让 Prepared Intent 在失焦、Win+D/桌面显示、全屏、会话/RDP 和 Explorer Shell 身份变化时立即失效，但 B6C3-07 的显示拓扑 generation 子项仍不可观察。本阶段把仓库已有产品级只读显示链路接入同一个 probe 自有会话，不新建显示枚举实现，也不主动改变显示配置。

会话启动后先保持窗口隐藏，通过 `ProductDisplayTopologyReader.CreateForCurrentSession()` 读取活动路径和监视器信息。只有 `Ready` 结果才能计算 `DisplayTopologyFingerprint`；默认静默期后连续两个一致权威样本形成基线，窗口才以 `SW_SHOWNOACTIVATE` 出现。之后指纹变化或读取降级立即清除 Prepared 并隐藏。恢复要求显示拓扑重新稳定，同时 Stage 118 系统表面门也处于安全状态；任一门仍不安全都不能重新显示。

结论仍固定为 **PendingManualEvidence**。代码、CI 和计数只能证明入口与状态合同，不能替代物理显示变化、真人输入、Narrator、Explorer 重启或恢复体验的人工结论。

## 2. 复用链路与代次语义

- `ProductDisplayTopologyReader`：复用正式 Windows `QueryDisplayConfig`、监视器枚举、稳定目标身份、DPI、旋转、Bounds/WorkArea 完整性检查；非 `Ready` 结果不被当作安全证据。
- `DisplayTopologyFingerprint`：按稳定显示 ID、相对坐标、尺寸、WorkArea、DPI、旋转与主显示器状态形成规范指纹，不记录或输出设备身份。
- `DisplayTopologyStabilizer`：复用 750 ms 静默期、250 ms 采样间隔、10 秒上限和两个一致样本；指纹变化形成新 generation，超时或暂停状态不能恢复。
- `ReadOnlyDisplayTopologyGenerationObserver`：只负责周期读取、有限事件映射与释放；不持有 App、产品 Surface 或文件能力。

首次权威基线只用于允许 probe 来源出现，不计入 `DisplayTopologyGenerationChangeCount`。基线后发生变化才增加该计数。读取降级单独增加 `DisplayTopologyUnavailableCount`；它同样 fail-closed，但不能冒充已观察到真实 topology generation 变化。

## 3. 联合安全门

系统表面门与显示拓扑门均初始保守：

1. topology 基线形成前，来源保持隐藏；
2. 任一系统危险事件把 `systemSurfaceSafe` 置为 false，失效 Prepared；
3. topology 指纹变化或非权威读取把 `displayTopologySafe` 置为 false，失效 Prepared；
4. 系统事件源产生 `RecoveryCandidate` 只恢复系统门；
5. topology 产生 `Stabilized` 只恢复拓扑门；
6. 只有两个门同时为 true，adapter 才回 `AwaitingPassiveSurface`，窗口才不激活地显示；
7. 恢复不会重建旧 Prepared，必须由真人执行新动作。

单锁串行联合门、转发 adapter 和窗口可见性更新，避免一个观察器的恢复覆盖另一个观察器仍处于危险状态。退出时先退订并释放 topology 轮询，再退订系统事件源，之后完成 adapter 和销毁 probe HWND。

## 4. 启动与人工范围

启动器现在接受 `B6C3-05`、`B6C3-06`、`B6C3-07`，并要求五项确认，其中 `-AcknowledgeReadOnlyDisplayTopologyObservation` 明确表示操作员理解“程序只观察，场景变化与恢复由受控环境负责”。`-ValidateOnly` 只输出合同，不启动窗口、不读取当前 topology，也不改变系统状态。

B6C3-07 必须把 Explorer 身份变化和显示拓扑变化分别执行、分别记录。显示变化步骤继续服从 Issue #20/A5 手册；启动器不调用 `SetDisplayConfig`、`ChangeDisplaySettings`、设备控制、进程终止、注销、RDP 切换或重启命令。

## 5. 权限与隐私边界

本阶段继续禁止：

- 启动正式 Long方格 App 或把来源接入正式 DesktopHost；
- 全局 Hook、Raw Input、`SendInput` 或原生注入检测声明；
- 主动修改显示配置、会话、电源、Explorer 或任务栏；
- Admission/Explicit、Intent 消费、拖放和任何桌面文件读写；
- 输出稳定设备 ID、桌面项、路径、窗口标题、原始输入或用户身份；
- 自动截图、证据文件或 Pass 结论。

人工记录只允许匿名操作员、Windows build、显示器/DPI 类别、有限计数、首次结果与恢复结论。

## 6. 自动验证与未完成项

自动合同检查确认：正式读取器/指纹/稳定器被复用；系统和 topology 联合门存在；启动器声明只读且不改变状态；危险 API、正式 App、Explicit 和文件能力未接入。Release 构建、全量测试、覆盖率、依赖锁定和内部 RC 结果以本 PR 的最终 CI 为准。

自动验证不执行热插拔、旋转、DPI/WorkArea 变化、Explorer 重启、锁屏/RDP、真实输入或 Narrator。因此 B6C3-01～08 仍需 B6c8 真人矩阵和负责人复核，Stage 124 不能据此宣称交互产品化完成。

## 7. 需求对齐与下一步

本阶段继续推进 iTop/Fences 类桌面管理的关键底座：真实交互在显示和系统环境变化时必须稳定、可恢复、无幽灵点击，并且旧意图不能跨代次生效。它没有扩大任务栏美化、小组件/插件、广泛窗口特效或真实文件操作范围。

下一阶段 B6c8 只执行和复核匿名人工矩阵。只有物理输入/Narrator、系统表面、Explorer、显示拓扑、清理和恢复均具备可复读证据后，才评审正式 App 输入接线；在此之前保持 fail-closed。
