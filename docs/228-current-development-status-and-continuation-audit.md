# Stage 228：当前开发状态与接续点审计

日期：2026-08-28

审计输入基线：`origin/main@920558134784768295d87ee5a3c4f4d9fecce52f`

状态：`AuditComplete / DocumentationOnly / ProductJourneysPending / ExternalInputsPending`

## 1. 审计目标与结论

本阶段只审计真实代码、权威文档、本机可验证状态和 GitHub 当前状态，不新增产品功能。目标是回答三个问题：工程已经做到哪里、产品功能真正完成到哪里、下一次具备条件时从哪里接续。

结论如下：

- 工程底座和自动化质量已经成熟：三项 Core 的主要正式代码链均已进入 main，当前完整测试规模为 1,382 项，格式、Release 构建、覆盖率、依赖与安全工作流门禁可通过。
- 产品闭环尚未完成：M1“桌面盒子与单文件夹绑定完整旅程”和 M2“任务栏美化完整旅程”仍为 `0/2 Complete`。工程合同、真实文件系统或只读预检通过，不能替代真实安装、可见操作、辅助功能和恢复证据。
- 当前没有新的已授权产品写入项。开放 Issue 仍为 #19、#20、#23、#24、#274，均需要人工矩阵、负责人决策或外部受控环境。继续增加相邻探针、自动整理、Tab、Widget、工作空间或新协议不会关闭当前 Core 缺口。
- 下一接续点不是再写一段邻接代码，而是等待对应触发条件成立后，按本文第 6 节进入唯一旅程；若实际执行暴露产品缺陷，再以首次 Actual 与预期差异驱动最小代码修正。

## 2. 真实代码与仓库规模

本次从最新远端 main 建立独立审计分支，没有 reset、rebase 或覆盖用户本地分叉 main。仓库当前有 800 个 tracked files，其中包括 373 个 C# 文件、3 个 C++/header 文件、48 个 PowerShell 文件、275 个 Markdown 文件和 22 个项目文件；C# 源码约 111,675 行，测试源码中有 1,042 个 `[Fact]` / `[Theory]` 声明。

代码结构风险仍然集中：

| 文件 | 当前行数 | 审计结论 |
|---|---:|---|
| `src/LongGrid.App/MainWindow.xaml.cs` | 5,343 | 页面状态、交互协调仍过度集中；后续只随真实用户旅程就近拆分，不另开长期纯重构阶段 |
| `src/LongGrid.App/App.xaml.cs` | 4,049 | 启动、会话与产品协调职责仍重；新增能力不得继续无边界堆入 |
| `src/LongGrid.Infrastructure/DesktopHost/WindowsProductDesktopHostReadOnlySurface.cs` | 3,415 | 原生桌面表面体量高；后续原生修正应提取有限适配器并保留失败关闭 |

## 3. 开发与功能对齐总账

| 范围 | 已进入 main 的真实能力 | 尚未完成的用户结果 | 当前状态 |
|---|---|---|---|
| 桌面盒子 | PF-001～PF-007、BOX-R1-A/B：创建、管理、布局、图标/缩略图、选择/打开、OLE Link、盒子间改归属、原生 Explorer 背景命令与 unsigned MSIX 工程链 | 受保护签名安装后的桌面空白处菜单；真实鼠标/键盘/Narrator/高对比；Explorer 重启、卸载与失败恢复 | `EngineeringComplete / ProductEvidencePending` |
| 单文件夹绑定 | FOLDER-R1-A～D：真实 NTFS 身份、枚举、watcher、路径、三种基础排序、加载/离线/权限/替换/恢复状态 | 物理 Picker、可见刷新/打开/排序、键盘/Narrator，以及与盒子创建、拖入、撤销串成的两分钟旅程 | `EngineeringComplete / RealFilesystemPass / ProductEvidencePending` |
| 任务栏美化 | TASKBAR-R1A～R2B1-A2：只读兼容探测、有界客户端、恢复事务/租约、启动预检、原生边界、Guest 准入、正式“系统默认/通透”预设卡片 | R2B1-B 原生效果、15 秒确认/回退、Explorer/多显示器/build 认证和卸载恢复 | `EngineeringCompleteAtAdmissionBoundary / EnvironmentBlocked` |
| 产品 UI | 产品导航、概览、盒子管理、个性化、设置分层和正式 XAML 渲染链 | 完整高对比、减少动画、Narrator、物理键盘与各 DPI 用户证据 | `EngineeringComplete / ManualEvidencePending` |
| 发布 | portable ZIP、unsigned MSIX、SPDX SBOM、哈希、依赖许可证元数据、受保护环境合同 | 许可证/NOTICE 决策、正式 Publisher、托管签名、签名安装、升级/修复/卸载/回滚 | `InternalUnsignedOnly / DistributionBlocked` |

因此不能用 Stage 数量、代码量或测试数量给出一个虚假的“整体完成百分比”。准确口径是：工程实现覆盖度高、自动化门禁稳定，但顶层产品旅程仍为 M1/M2 `0/2 Complete`，详细 PF 账本仍为 30 项 `0 Complete`；公开分发能力为未完成。

## 4. 本轮保留的真实证据

用户要求停止扩展测试前，本轮已经完成的本机证据如下；此后未再增加测试动作：

- locked restore、格式检查和 Release build 通过，`0 warning / 0 error`；
- 完整套件 `1,382/1,382` 通过、0 skipped；独立 coverage 为 lines `90.43% (47090/52072)`、branches `76.16% (15458/20298)`；
- Action pins、Dependabot、CodeQL workflow、20 projects / 30 packages 许可证元数据与漏洞门禁通过；许可证清算仍是 `PendingOwnerReviewAndNotice`；
- M1 external-automation 真实预检发现 runtime `2.4.0.0` / XAML `3.2.3.0` 已知危险组合，返回 `BlockedByKnownUpstream`，没有启动进程或创建证据会话；
- 任务栏宿主预检 5 轮均有限返回 Blocked，原因是硬件证据、Windows Sandbox launcher 和隔离配置缺失；`modifiedSystemState=false / mutationAllowed=false`；
- GitHub `long-grid-release` 真实存在并要求 1 位 reviewer，但 repository/environment secrets 与 variables 均为 0、deployments 为 0；signing/MSIX/RC 仍明确 `signed=false / installable=false / distributionApproved=false`；
- 审计前最新 main CI run `33157992266` 为 1,382/1,382、0 skipped，lines `90.11%`、branches `76.04%`；CodeQL run `33157992193` 成功，C# 与 C++ 均 0 results，open code scanning alerts 为 0。

这些证据证明工程基线健康，但没有把任何产品旅程升级为 Complete。

## 5. Expected / Actual / Difference / Correction

| 审计项 | Expected | Actual | Difference / Correction |
|---|---|---|---|
| 权威状态 | 最新文档应指向最新 main 和唯一接续点 | README、统一计划与 Stage 153 仍指向 `673e7b6` / Stage 227；真实最新 main 为 `9205581` | 新增 Stage 228，并把三个入口统一到本审计 |
| 整体完成度 | 工程完成与产品完成分层表达 | 大量正式代码和 1,382 项测试已通过，但 M1/M2 出口均未满足 | 保留 `0/2 Complete`，不把自动化绿灯换算为产品百分比 |
| M1 执行条件 | 安全 runtime、签名包、独占可丢弃 Windows 会话齐备 | 当前 runtime/UIA 组合失败关闭；正式签名和可丢弃安装环境未提供 | 不重复 M1 邻接开发；条件齐备后从 BOX-R1-C 安装/菜单开始完整串行旅程 |
| 任务栏执行条件 | Guest 准入 ReadyToLaunch 后才允许原生 mutation | 当前宿主缺 Sandbox launcher、硬件与隔离配置证据，5 轮均 Blocked | 宿主保持只读；只在合格 Guest 进入 TASKBAR-R2B1-B |
| 发布输入 | 负责人批准许可证、Publisher 和托管签名方案 | #23/#274 仍开放，受保护环境没有 secret/variable/deployment | 不创建空转 live-signing 代码，不安装或分发 unsigned MSIX |
| 结构维护性 | 新开发应降低高风险文件继续膨胀 | 三个热点文件仍为 5,343 / 4,049 / 3,415 行 | 后续缺陷修正随所在旅程提取协调器/适配器，并用原旅程测试保护；不单独制造长期重构分支 |

## 6. 下一次开发的唯一接续顺序

### 6.1 首选接续：M1 完整物理旅程

触发条件：#23/#274 对许可证与 Publisher/托管签名方案作出负责人决定；得到受保护签名 MSIX；有无既有 Long方格进程的可丢弃 Windows 账户/VM；WinUI/UIA runtime 通过安全预检。

接续起点：从 BOX-R1-C 的“安装 → 桌面空白处右键创建盒子”开始，串行完成“绑定真实文件夹 → 加载/刷新/打开/排序 → Explorer 拖入 → 盒子间改归属 → 撤销 → Explorer 重启 → 卸载恢复”。每一步记录 Expected、首次 Actual、Difference、Correction 和修正后的 Actual。物理 Picker、鼠标、键盘、Narrator、高对比与减少动画均不得用源码合同代替。

### 6.2 第二接续：TASKBAR-R2B1-B

触发条件：Stage 216 定义的 Windows Sandbox/专用 VM、硬件证据和隔离配置全部 ReadyToLaunch。

接续起点：只在 Guest 应用 Clear/SystemDefault 原生效果，完成 15 秒确认/自动回退，然后进入 Explorer 重启、多显示器、逐 build 和卸载恢复。日常宿主继续 `mutationAllowed=false`。

### 6.3 并行外部输入

- #19：真实输入和系统表面人工矩阵；
- #20：动态显示器、DPI 与会话矩阵；
- #23：D23-11、P1～P5 可用性/产品决定和许可证边界；
- #24：专用真实卷与持久化边界矩阵；
- #274：Publisher、托管签名/OIDC、审批隔离和可丢弃安装环境。

在这些触发条件没有变化时，只处理真实回归、质量或安全缺陷；不得插入自动整理、Tab、Peek、Widget、工作空间或新协议，也不得继续以新增探针代替产品证据。

## 7. 本阶段目标与需求对齐审计

开发目标审计：已从最新 main 复读真实代码与远端状态，分层写清工程完成度、产品完成度、发布边界和唯一接续入口；本阶段没有新增或修改产品运行时。

需求对齐审计：三项 Core 顺序、本地优先、零惊吓、安全引用、任务栏宿主失败关闭、unsigned 产物不可分发，以及真实 Expected / Actual / Difference / Correction 纪律均保持不变。下一阶段只有在对应外部触发条件成立或发现真实缺陷时才能进入代码修改。
