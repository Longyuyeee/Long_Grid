# Stage 219：当前开发审计与跨电脑接续快照

日期：2026-08-28  
审计基线：`origin/main@f6cda670a9614284da4547d80bcb512d35f97d55`  
主干 CI：`33098018735`（Success）  
状态：`Audited / HandoffReady / ExternalEnvironmentBlocked`

## 1. 审计目的与边界

本轮不开发新功能，只把换电脑后必须依赖的事实收敛到一个可复读接续点。审计同时复核：

- [产品需求文档](02-product-requirements.md)定义的三项 Core 与 P0 文件夹绑定状态；
- [统一开发计划](PRODUCT_EXECUTION_PLAN.md)的当前队列、完成口径和主干基线；
- `src`、`tests`、`eng` 中已合入的正式代码与安全入口；
- GitHub PR、主干 CI、远端分支和未完成的外部证据；
- 下一台电脑应先做什么，以及哪些动作仍被禁止。

本审计不把源码合同、CI、真实 HWND 探针或未签名产物写成物理用户旅程通过，也不安装包、不修改 Explorer/任务栏、不降低 Windows 安全策略。

## 2. 仓库与 GitHub 事实

| 检查 | 预期 | 实际 | 结论 |
|---|---|---|---|
| 主干同步 | 本地 `main` 与 `origin/main` 一致，工作树干净 | 两端均为 `f6cda67`，审计开始时无未提交文件 | `Pass` |
| 最新功能交付 | FOLDER-R1 恢复状态纠偏已经进入主线 | PR #270 已 squash 合入 `f6cda67`；功能分支已删除 | `Pass` |
| 主干门禁 | 合并后完整 Windows CI 成功 | run `33098018735`：`1,381/1,381`，lines `90.11% (46924/52072)`，branches `76.04% (15434/20298)`；Release、198-ID UI 合同、文件安全、Worker、漏洞和内部 RC/SBOM 均成功 | `Pass` |
| 开放 PR | 不应存在可被误认成当前接续点的旧审计分支 | PR #266 仍开放在 `c726018`，基于 `d069f11` 时期的 1,353 tests / 195 IDs，当前为 `CONFLICTING/DIRTY`，且不含 #267～#270 | 已被本审计取代；关闭旧 PR，不合并旧 lock-file 或状态快照 |

本轮在当前 Windows 主机实际复跑换机入口：WinUI/UIA 预检发现 runtime `2.4.0.0` / XAML `3.2.3.0`，返回 `BlockedByKnownUpstream`；MSIX 生命周期 `-ValidateOnly` 返回 `Pass / startsProcess=false / modifiesPackageState=false / trustsUnsignedPackage=false`；M1 人工会话 `-ValidateOnly` 返回 `Pass / startsProcess=false / drivesUserInput=false / isolatesConfiguration=true`；UI `-ContractOnly` 返回 198 Automation IDs / `Pass`。这些结果证明入口继续失败关闭，不提升物理证据状态。

## 3. 原始需求与实际代码对齐

PRD 的核心任务仍是：桌面空白处创建/管理盒子、绑定一个真实文件夹、任务栏预设与可靠恢复；Explorer 拖入和盒子间改归属是核心交互。P0 文件夹绑定明确要求路径、正常、空、加载、失效、离线、权限拒绝和恢复状态。

| 产品范围 | 实际工程状态 | 尚缺出口 | 审计判断 |
|---|---|---|---|
| BOX-R1 桌面右键创建 | A/B 已有统一创建激活、原生 `IExplorerCommand` DLL 和 MSIX `Directory\Background` 注册 | C/D 的受保护签名安装、真实菜单点击、Explorer 重启、多显示器/DPI、卸载恢复 | `EngineeringComplete / ProductEvidencePending` |
| FOLDER-R1 单文件夹绑定 | A～D 的身份、绑定、内容、刷新、watcher、打开、权限/离线/替换恢复已合入；#267～#270 又补齐路径、三种持久化基础排序、真实加载状态和一次性恢复反馈 | 物理 Picker、可见加载/失效/恢复、刷新/打开/排序、键盘和 Narrator | `EngineeringComplete / RealFilesystemPass / ProductEvidencePending` |
| PF-007 拖入与改归属 | A1/A2/B 已有 OLE Link、盒子间改归属、原子提交/补偿和一次撤销 | 真实 Explorer 指针、盒子间物理鼠标与可见撤销证据 | `EngineeringComplete / RealHwndPass / ProductEvidencePending` |
| TASKBAR Core | R1～R2B1-A2 已有只读探测、恢复凭据、唯一租约、启动恢复预检、默认空原生边界、可丢弃环境准入和两张预设卡片 | R2B1-B 原生 `Clear → SystemDefault`、R3 恢复矩阵、R4 逐 build 认证 | `EngineeringCompleteAtAdmissionBoundary / EnvironmentBlocked` |
| M3～M5 | 仍是增强、生态和正式分发阶段 | 三项 Core 的 M1/M2 尚未同时完成 | 不得抢占当前 Core 出口 |

恢复状态不是文档占位：`ProductWorkspaceFolderContentSet.MarkRecoveriesFrom` 只在更高 generation 的上一份非 `Resolved` 已发布结果转为当前可用 `Resolved` 时附加有限来源；App 在指纹变化/无绑定时清除基线，读取模型和 presentation 传播有限枚举，控制中心与 UIA 显示恢复反馈。它不覆盖 `Ready/Empty/Truncated`，不持久化路径，不增加轮询或文件写入，下一次普通健康刷新会清除一次性元数据。

## 4. 当前完成度与风险

- M1 桌面盒子/文件夹绑定完整旅程：未完成，`ExternalEnvironmentBlocked / ProductEvidencePending`。
- M2 任务栏美化完整旅程：未完成，`EnvironmentBlocked`。
- 因 M1 与 M2 都未关闭，三项 Core 的顶层出口仍为 `0/2 Complete`；不能用工程切片数量替代产品完成度。
- 当前产物只有内部未签名 Developer Preview，不可安装、不可公开分发；正式签名与生命周期属于 M5 门禁。
- 本机已知跨进程 UIA 风险组合为 Windows App Runtime `2.4.0.0` / Microsoft.UI.Xaml `3.2.3.0`，外部自动化必须先执行失败关闭预检。
- 结构风险继续上升：`MainWindow.xaml.cs` 5,659 行、`App.xaml.cs` 4,258 行、`WindowsProductDesktopHostReadOnlySurface.cs` 3,701 行。后续只能在当前用户旅程内就近提取协调器/适配器，不另开无用户结果的长期重构阶段，也不再把新业务集中进这三个文件。
- `src`、`tests`、`eng` 未发现新的 `TODO/FIXME/HACK/NotImplementedException` 静默占位；历史文档中的 Pending 不能据此提升为完成。

## 5. 文档偏移与本轮纠正

审计前的统一计划仍把 FOLDER-R1 恢复状态写为“当前分支进行中”，并把主干写成 `9657a20`；实际已经由 #270 合入 `f6cda67` 并通过主干 CI。README 的“建议下一步”又单独指向同样受外部环境阻断的 TASKBAR-R2B1-B，容易让换机开发绕过唯一执行队列。

本轮纠正为：

1. FOLDER-R1 恢复状态纠偏标记为已合入工程事实；
2. 当前唯一执行项恢复为 M1 完整物理旅程，但明确受签名、可丢弃账户和安全运行时阻断；
3. TASKBAR-R2B1-B 保持并列外部环境阻断，不得改在宿主上试写；
4. README 只链接本快照和统一计划，不再另行发布相互竞争的执行顺序；
5. PR #266 作为被后续四个功能 PR 和本快照取代的历史审计关闭，不合并。

## 6. 换电脑后的唯一接续顺序

### 6.1 先建立可信基线

```powershell
git switch main
git pull --ff-only origin main
git status --short --branch
powershell -NoProfile -ExecutionPolicy Bypass -File .\eng\Test-LongGridWinUiUiaRuntime.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\eng\Test-LongGridMsixLifecycle.ps1 -ValidateOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\eng\Start-LongGridM1ManualEvidenceSession.ps1 -ValidateOnly
```

预期：Git 干净且与 `origin/main` 一致；两个 ValidateOnly 入口均声明不启动进程、不驱动输入、不修改包状态；运行时预检如实返回 `Pass`、`BlockedByKnownUpstream` 或 `Inconclusive`。

### 6.2 环境满足时恢复 M1

只有同时具备受保护签名产物、可丢弃 Windows 账户/VM、零既有 Long方格进程和可安全执行的 WinUI/UIA 条件，才进入完整两分钟旅程：

1. 安装签名包并从桌面空白处创建盒子；
2. Picker 绑定 Unicode 真实文件夹；
3. 观察加载、权限拒绝/离线、恢复、排序、刷新和系统打开；
4. 从 Explorer 以 Link 拖入，再把已选引用拖到另一盒子并撤销；
5. 验证高对比、减少动画、键盘与 Narrator；
6. 重启 Explorer，最后卸载并核对系统与文件恢复。

每一步记录 Expected / Actual / Difference；任一门禁不满足就保持 Pending，不自动点击，不以日常账户、未签名包、关闭安全策略或源码合同替代。

### 6.3 环境仍不满足时停止扩张

- 不重复开发 FOLDER-R1 路径、排序、加载或恢复状态；
- 不新增 M1 邻接探针，不转向自动整理、Tab、Widget、插件或新协议；
- 不在宿主上实现或试写任务栏原生适配器；TASKBAR-R2B1-B 只能在通过 Stage 216 准入的可丢弃 Guest 中继续；
- 只允许修复真实回归、维护门禁或准备已获批准的受保护签名/隔离环境。若新电脑提供了新的环境事实，先把事实和证据写回统一计划，再开始对应实现。

## 7. 接续完成定义

本快照合入且合并后主干 CI 全绿、旧 PR #266 标明被取代、本地重新同步到新的 `origin/main` 后，才是可换电脑接续状态。后续开发必须从本节第 6 节开始，不从聊天记录、旧 Stage 或开放历史分支猜测进度。
