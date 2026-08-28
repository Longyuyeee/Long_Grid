# Stage 226：当前整体开发与功能对齐审计

日期：2026-08-28

审计基线：`origin/main@061a590daad0a8910fc3c71f5e0ca7e60c957202`

状态：`CorrectionFullLocalPass / PullRequestCorrectionPending / CoreJourneysPending / ExternalInputsPending`

## 1. 审计目标与完成口径

本阶段不新增产品功能。初始目标是关闭权威文档仍引用 Stage 219 / `f6cda67`、Stage 225 仍写 main Pending，而真实主线已经合入 PR #285 并完成 CI/CodeQL 的状态漂移；同时按“正式代码、自动化工程证据、真实产品旅程、安装分发”四层重新对齐完成度。PR 首轮真实 runner 又暴露两项既有任务栏测试因果差异，本阶段继续按首次 Actual 修正，不用重跑覆盖。

Core 类型、探针、源码合同或 CI 通过不等于用户功能完成。只有正式 App/DesktopHost 可发现、可操作、可恢复，并取得要求的物理交互、辅助功能、安装和恢复证据，才能把对应产品旅程记为 Complete。

## 2. Initial Expected / Actual / Difference / Correction

| 检查 | Expected | Initial Actual | Difference / Correction |
|---|---|---|---|
| 权威主线基线 | 统一计划和接续入口指向最新已验证 main | 统一计划与 Stage 153 仍指向 `f6cda67` / Stage 219；真实 main 为 `061a590` | 更新统一计划、README 和 Stage 153，Stage 219 明确被本审计取代 |
| Stage 225 状态 | 已合并且 main 门禁完成后状态必须关闭 | 文档仍为 `LatestCommitVerificationPending / MainVerificationPending` | 回填 PR #285、main CI `33149776515`、main CodeQL `33149776526` 和 Code Scanning API 结果 |
| 产品完成度 | 工程完成与产品完成分层表达 | 三项 Core 工程资产很多，但 M1/M2 顶层出口仍为 `0/2 Complete` | 保留严格出口；不按 Stage 数量、测试数量或代码量换算产品 Complete |
| 下一执行项 | 只能指向可真实执行且不降低安全边界的工作 | 当前 M1、任务栏 R2B1-B 和正式发布均缺外部环境/负责人输入 | 环境满足时执行 M1 或任务栏真实旅程；未满足时只允许真实回归修复、门禁维护及获批签名/隔离环境准备 |

## 3. 当前开发与功能对齐总账

| 范围 | 已完成工程事实 | 尚缺产品出口 | 当前判定 |
|---|---|---|---|
| 桌面盒子 | BOX-R1-A/B、PF-001～PF-007 的创建、管理、布局、图标/缩略图、选择/打开、OLE Link 和盒子间改归属正式链已进入 main | 签名安装后的 Explorer 背景菜单、真实鼠标/键盘/Narrator/高对比、Explorer 重启和卸载恢复 | `EngineeringComplete / ProductEvidencePending` |
| 单文件夹绑定 | FOLDER-R1-A～D、真实 NTFS 身份/内容/watcher、路径、三种基础排序、加载/离线/权限/替换/恢复状态已进入 main | 可见 Picker、刷新/打开/排序、物理键盘/Narrator 与完整两分钟旅程 | `EngineeringComplete / RealFilesystemPass / ProductEvidencePending` |
| 任务栏美化 | R1A～R2B1-A2 的只读探测、有界客户端、恢复事务/租约/启动预检、原生边界、环境准入和两张预设卡片已进入 main | R2B1-B 原生效果、15 秒确认/回退、Explorer/多屏/build 认证与卸载恢复 | `EngineeringCompleteAtAdmissionBoundary / EnvironmentBlocked` |
| 自动整理与竞品增强 | 规划器、恢复、多屏和协议底座存在 | 自动规则闭环、Quick-hide、托盘/全局快捷键、Portal、Tab、Peek、命名快照、工作空间 | `DeferredUntilCoreClosure` |
| 发布 | portable ZIP、unsigned MSIX、SPDX、哈希、依赖许可证元数据和受保护环境存在 | 许可证决定、正式 Publisher、托管签名、安装/升级/修复/卸载/回滚 | `InternalUnsignedOnly / DistributionBlocked` |

方向仍与三项 Core 对齐，没有转成 Explorer 注入器、文件搬运器或插件优先项目。当前主要差距不是缺少底座，而是工程能力尚未通过完整用户旅程和发布生命周期转化为可交付功能。

## 4. 真实环境与远端证据

| 验证 | Expected | Actual | Difference / 结论 |
|---|---|---|---|
| Stage 226 本机完整门禁 | locked restore、格式、Release、全量测试和覆盖率通过 | 最终修正后 `0 warning / 0 error`；关闭 build server 后 `1,382/1,382`、0 跳过；lines `90.43% (47090/52072)`、branches `76.16% (15458/20298)` | None |
| 质量与发布否定性合同 | 供应链、安全和不可分发状态不漂移 | Action pins、Dependabot、CodeQL workflow、漏洞、20 projects / 30 packages 许可证元数据、signing 与 RC ValidateOnly 均 Pass；`liveSigningImplemented=false / signed=false / installable=false / distributionApproved=false` | None |
| 最新 main CI | `061a590` 全量门禁通过 | run `33149776515` success；`1,382/1,382`、0 跳过，lines `90.11% (46924/52072)`、branches `76.04% (15434/20298)`；artifact 997,390 bytes、未过期 | None |
| 最新 main CodeQL | C# 与 C++ 均成功上传并无当前发现 | run `33149776526` success；CodeQL `2.26.4`，C# 52 rules / 0 results，C++ 58 rules / 0 results；open alerts 0 | None；不表示永久无风险 |
| WinUI/UIA 环境 | 可发现 runtime，且已知危险组合不存在 | runtime `2.4.0.0`、XAML `3.2.3.0`，`KnownUnsafeCrossProcessUiaRuntimePairPresent / BlockedByKnownUpstream` | M1 外部 UIA/物理自动化继续失败关闭，不绕过 |
| M1/MSIX 入口 | ValidateOnly 不启动、不驱动输入、不改包状态 | M1 `startsProcess=false / drivesUserInput=false / isolatesConfiguration=true`；MSIX `modifiesPackageState=false / trustsUnsignedPackage=false / liveEvidence=PendingSignedPackageAndDisposableWindowsProfile` | 合同 Pass，不等于安装或旅程 Pass |
| 本机独占会话 | 无既有 Long方格进程 | 实际存在 1 个 `LongGrid.App`，PID `45524` | 当前账户不得启动第二 DesktopHost，也不得终止非本阶段进程 |
| 任务栏可丢弃环境 | Sandbox 启动器存在后，硬件查询也必须有限完成 | Windows `10.0.26200.0` / X64 / 16 processors；Sandbox launcher=false，因此不再启动下游 CIM 子进程；`hardwareEvidenceCollected=false / mutationAllowed=false / outcome=Blocked` | `HardwareEvidenceUnavailable`、`WindowsSandboxLauncherMissing`；未执行任务栏写入 |
| 发布外部输入 | 受保护环境存在，正式输入由负责人批准 | `long-grid-release` 限受保护分支、1 reviewer；repository/environment secrets 与 variables 均为 0，deployments 0；#23/#274 OPEN | 安全边界正确；许可证、Publisher 和托管签名仍未提供 |

### 4.1 PR 首轮真实失败与修正

PR #286 首轮 CI run `33151795685` 在 Test 步骤得到 `1,380/1,382`，保留为首次真实 Actual：

| 测试 | Expected | Actual | Difference / Correction |
|---|---|---|---|
| `RealHostPreflightProducesFiniteFailClosedEvidence` | 外层 10 秒内得到有限 JSON | 进程 PID 4516 到 10 秒仍无 stdout/stderr，抛出带 PID 的 `RealProcessTimeoutException` | `Get-CimInstance -OperationTimeoutSec 2` 不覆盖 PowerShell/CIM 初始化。硬件查询改到测试自有 PowerShell 子进程并设 6 秒硬期限；当更早的 Sandbox launcher 准入已经失败时不启动下游查询，明确 `hardwareEvidenceCollected=false`。本机真实脚本 5 轮均在 671～867ms 返回 Blocked |
| `RealKilledChildLeavesDurableRecoveryJournal` | 子进程完成 `Staged → Applied` 调用后强杀，磁盘仍为 Applied | 父进程曾读到 Applied 后立即强杀，最终复读为 Staged | “父进程看见新文件”不能证明子进程 `UpdatePhaseAsync` 已返回。子进程现在只在更新调用返回后写固定 readiness 文件，父进程见证后才强杀；状态断言仍要求 Applied |

两项失败与真实超时清理负向测试组成三项定向矩阵，修正后连续 5 轮 `15/15` 通过；Release 定向 build 保持 `0 warning / 0 error`。随后最终完整本机套件 `1,382/1,382`、0 跳过，coverage lines `90.43%`、branches `76.16%`，全部质量/发布否定性合同继续通过；最新 PR/main 门禁仍须在修正提交后重新执行。

提交前首次 Markdown 本地链接包装命令因 `Test-Path` 条件少一个右括号而在执行检查前触发 PowerShell `ParserError`，没有修改文件也没有产生链接结论。修正包装器后复跑 7 份变更文档，`missingLocalLinks=0`，`git diff --check` 通过；该编排差异不被隐藏为首次通过。

## 5. 开发目标与需求对齐审计

开发目标审计：本阶段目标是让权威文档准确反映 `061a590` 的工程事实、严格完成口径和真实阻断，并关闭同一 PR 暴露的两项既有任务栏测试因果差异；README、统一计划、Stage 153、Stage 216、Stage 219 和 Stage 225 已同步。需求对齐审计：三项 Core 顺序、本地优先、零惊吓、安全引用、不在日常宿主试写任务栏、不把 unsigned 包视为可安装的要求均保持不变；修正只约束测试自有进程和证据，不增加产品权限。

本阶段的完成不提升 M1、M2 或任何 PF 项为 Product Complete。它只建立一个不会诱导后续重复开发或绕过门禁的最新接续基线。

## 6. 下一接续点

1. 负责人先在 #23 决定许可证/商业边界，并在 #274 批准正式 Publisher、托管签名和可丢弃安装环境；秘密不得写入 Issue、仓库或普通 CI。
2. 具备签名包、无既有 Long方格进程的可丢弃 Windows 账户/VM 和安全 WinUI/UIA 运行时后，执行 M1 完整产品旅程，并逐步记录 Expected / Actual / Difference / Correction。
3. 具备 Stage 216 的真实 Guest 准入后，才执行 TASKBAR-R2B1-B；当前宿主继续禁止任务栏 mutation。
4. #19/#20/#24 的物理输入、显示/会话和真实卷证据并入上述受控环境；在 M1/M2 关闭前，不用自动整理、Tab、Widget、工作空间或新协议替代核心进度。
