# Long方格产品自有窗口注册表与只读 DesktopHost 桥审计

> 审计日期：2026-08-06
>
> 范围：产品自有容器窗口所有权、DesktopHost 生命周期、句柄复用防护、匿名只读状态、下一阶段准入
>
> 结论：工程阶段 B 完成；没有创建或移动真实容器窗口，真实窗口恢复提交仍保持阻断

## 1. 本阶段交付

`ProductDesktopHostWindowBridge` 建立了基础设施内部的产品窗口注册表。它只接受由当前 Long方格 DesktopHost 身份声明、且经只读原生复查一致的窗口。每条内部记录绑定：

- 稳定容器 ID；
- DesktopHost 实例 ID、宿主 generation、进程和线程所有权；
- 窗口 generation、非零原生实例标识和内部 HWND；
- 最近一次只读复查得到的 Bounds；
- 当前所有权是否仍然可信。

对 App 可见的 `ProductDesktopHostWindowSnapshot` 只有有限状态、桥 generation、注册数、验证数和拒绝计数。它不包含 HWND、容器 ID、进程/线程 ID、Bounds 或可执行委托。内部证据可向后续准入层提供排序后的容器集合，但不会穿透到展示层。

## 2. 所有权与生命周期规则

注册必须同时满足：当前宿主已连接；宿主实例/generation/进程/线程完全一致；容器 ID、窗口 generation、句柄和实例标识有效；容器 ID 与句柄均未重复；只读复查确认窗口存在、Bounds 有面积，并且进程、线程和实例标识与声明完全一致。

有限拒绝覆盖：未连接、无效声明、宿主不匹配、重复容器、重复句柄、窗口不可用和所有权不匹配。拒绝不会把外部窗口加入注册表。

- DesktopHost 更换实例或 generation 时清空旧记录；旧宿主声明不能进入新代际；
- 注销必须携带准确的窗口 generation，旧回调不能删除新窗口；
- 每次刷新重新读取存在性、进程、线程、实例标识和 Bounds；
- 窗口销毁或同一 HWND 被复用时，实例标识/所有权不再匹配，记录转为未验证，桥进入 `Degraded`；
- 重复容器和重复句柄均有限拒绝，不采用覆盖式“最后写入获胜”。

实例标识是由未来的 Long方格窗口创建方在窗口创建生命周期内安装的不可猜测非零标记；本阶段的 Windows inspector 只通过 `GetPropW` 读取，不负责写入。仅比较 HWND、PID 和 TID 不足以识别同线程中的句柄复用，因此不得删除此标识检查。

## 3. 零窗口变更边界

Windows 只读 inspector 仅使用：

- `IsWindow`；
- `GetWindowThreadProcessId`；
- `GetWindowRect`；
- `GetPropW`。

本阶段没有调用 `SetWindowPos`、`DeferWindowPos`、`MoveWindow`、`ShowWindow`、`SetForegroundWindow`、`SetWindowRgn` 或 `SetPropW`，也没有改变 Z 序、激活、Region、合成、UIA、Explorer 或桌面文件。App 没有实例化该桥，也没有取得注册声明或 HWND，因此用户当前看到的 UI 与桌面行为不变。

## 4. 自动化证据

新增定向测试覆盖：匿名初始快照、合法注册与 Bounds 复读、进程/线程/实例标识不匹配、重复容器、重复句柄、窗口销毁、句柄复用、DesktopHost 重启、陈旧注销和 32 路并发注册。

`eng/Test-LongGridUi.ps1 -ContractOnly` 新增源码边界：注册表必须包含代际、实例标识、Bounds、重复拒绝和匿名计数合同；App 不得连接窗口桥；bridge/inspector 不得出现窗口移动、激活、Region 或属性写入 API。AutomationId 仍为 118，不新增 UI 控件。

本地完整结果为 420/420 测试通过；覆盖率为行 91.60%（11312/12350）、分支 81.80%（3020/3692），通过仓库 90%/75% 门槛；Release `-warnaserror` 为零警告零错误，格式检查、118-ID UI 源码合同、单实例合同和依赖漏洞门禁通过。DesktopHost 交互探针仍为 `Conditional Pass`；#19、#20、#23、#24 的 ValidateOnly 分别保持 `PendingManualEvidence`、`ResultsPending` 或 `PendingDedicatedEnvironmentEvidence`，没有伪造真实证据。远端 CI 结果在 PR 发布后复核。

## 5. 需求对齐与剩余阶段

与最初需求的对齐如下：

| 需求 | 本阶段状态 | 说明 |
| --- | --- | --- |
| 桌面文件整理 | 未扩权 | 本阶段不读写桌面文件，继续沿用安全引用与显式文件操作边界 |
| 桌面分组容器 | 基础所有权完成 | 注册表可以证明未来容器窗口属于 Long方格及哪个代际 |
| 布局恢复 | 前置条件完成 | 提供容器集合和所有权证据，但尚未执行真实窗口提交 |
| 任务栏美化 | MVP 后续 | 未修改任务栏、Explorer 或系统注册表 |
| 自定义窗口效果 | MVP 后续 | 未连接 Region、合成或第三方窗口控制 |
| 小组件/Long助手插件 | 协议已设计、运行时后续 | 本阶段不改变 LPWP 协议和安全隔离边界 |

以“桌面管理 MVP 首条布局恢复垂直链”为范围，阶段 B 完成后还剩 **2 个工程阶段**：

1. 配置与窗口复合事务：把单次明确确认绑定到配置、窗口计划、注册表 generation 和一次性撤销；完成 capture/apply/reread、双向补偿和故障矩阵，仍不得操作非产品窗口；
2. RC 硬化与交付收口：干净会话 118-ID UIA、输入/显示/关闭/故障矩阵、性能预算、一键启动/打包和发布候选审计。

外部 Phase 0 证据仍有 GitHub #19、#20、#23、#24 四项，必须由真实人工、硬件或专用卷完成，不能用本阶段测试替代。任务栏美化、小组件/插件运行时和广泛窗口特效不包含在上述两个 MVP 收口阶段内。

## 6. 下一阶段 C 的强制入口条件

下一阶段可以设计“配置 + 产品窗口”的复合事务，但开始真实窗口应用前必须同时满足：

1. 从本注册表取得与计划完全一致的容器集合，且所有记录仍为 verified；
2. 事务令牌绑定当前注册表 generation、DesktopHost 实例/generation、配置指纹、edit revision、显示拓扑 generation、计划指纹和一次性配置撤销；
3. 窗口批处理适配器只能从已验证内部记录解析 HWND，App 和 Core 计划不得携带 HWND；
4. 任一代际、指纹、所有权或 Bounds 复读变化都必须在第一次窗口写入前拒绝；
5. 配置失败与窗口失败都必须执行双向补偿并验证恢复结果；没有完整回滚故障矩阵时保持准入阻断。

本阶段不会把 `ProductWorkspaceRealWindowRecoveryAdmission` 接入 App。只有阶段 C 的复合事务和阶段 D 的真实证据全部满足，真实窗口恢复按钮才有资格解除阻断。
