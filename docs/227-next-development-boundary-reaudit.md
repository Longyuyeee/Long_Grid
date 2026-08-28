# Stage 227：下一开发边界与真实阻断复审

日期：2026-08-28

审计基线：`origin/main@673e7b6ce692dd0f1928554c7455024d41a1eb50`

状态：`Complete / MergedToMain / MainVerificationPass / NoProductMutationAuthorized / ExternalInputsPending`

## 1. 开发目标

本阶段响应“进入下一步”，目标不是继续增加相邻底座，而是先用最新 main、当前宿主和远端发布状态判断哪一项 Core 旅程已真正具备执行条件；同时关闭 Stage 226、统一计划和 Stage 153 仍停留在旧 main/Pending 状态的权威文档漂移。

完成口径：只在 M1、TASKBAR-R2B1-B 或正式发布的全部准入条件真实成立时进入对应产品写入；否则记录首次 Actual、保持失败关闭、同步文档并明确需要哪项外部变化。CI、ValidateOnly、工程测试数量和内部 unsigned artifact 均不能换算成 Product Complete。

## 2. Initial Expected / Actual / Difference / Correction

| 检查 | Expected | Actual | Difference / Correction |
|---|---|---|---|
| 工作树权威基线 | 从最新远端 main 读取接续事实，同时保留用户本地分叉 | 初始工作树仍为用户 `main@7e6761c`，相对远端 ahead 1 / behind 94，因此首次本地文档读取命中旧快照；`origin/main=673e7b6` | 不 reset/rebase/覆盖用户 main；从 `origin/main` 创建 `codex/stage227-next-development-audit` 后重新读取真实代码和文档 |
| Stage 226 关闭状态 | 已 merge 且 main 门禁完成后文档应为 Complete | Stage 226 仍写 `LatestPullRequestVerificationPending`，统一计划与 Stage 153 仍指向 `061a590` / Stage 226 | 回填 PR #286、最新 PR/main CI/CodeQL/Code Scanning 证据，并把当前接续统一到 `673e7b6` / Stage 227 |
| M1 产品旅程 | 安全 WinUI/UIA runtime、独立会话、无外来产品进程冲突后才启动 | runtime `2.4.0.0`、XAML `3.2.3.0`，`KnownUnsafeCrossProcessUiaRuntimePairPresent / BlockedByKnownUpstream`；已有 `LongGrid.App` PID 45524 | external-automation 真实预检返回 `startsProcess=false / createsEvidenceSession=false`；前后 PID 集合完全一致，不终止或复用外来进程 |
| TASKBAR-R2B1-B | Sandbox launcher、硬件证明与安全隔离配置全部成立后才允许 Guest mutation | 真实预检 5 轮均在 633～1,022ms 返回 Blocked；`hardwareEvidenceCollected=false`、launcher=false、configuration missing、`modifiedSystemState=false / mutationAllowed=false` | 当前宿主禁止任务栏写入；等待具备 Stage 216 准入条件的可丢弃 Guest，不用本机或 mock 绕过 |
| 正式发布 | 许可证、正式 Publisher、托管签名、受保护权限和可丢弃安装环境齐备 | #23/#274 OPEN；repository/environment secrets 与 variables 均 0，deployments 0；一个 required reviewer，protected branches=true，custom policies=false | signing/MSIX/RC ValidateOnly 均 Pass，但 `liveSigningImplemented=false / signed=false / installable=false / distributionApproved=false`；等待负责人输入 |
| 远端状态查询 | 读取保护规则时应按真实嵌套字段计数 | 首次 PowerShell 投影把两个 protection rule 错当成两个 reviewer；随后对 custom policy endpoint 的读取因 custom policies=false 返回 HTTP 404 | 复读原始 environment JSON：实际为 2 条 protection rules，其中 required reviewers 只有 Longyuyeee 1 人；404 不表示环境缺失，也未发生远端 mutation |
| Markdown 本地链接 | 所有变更文档可从自身目录解析本地目标，任何包装器错误都不得产出成功结论 | 首次包装器处理仓库根 `README.md` 时，`Split-Path -Parent` 返回空字符串，触发多次非终止 `Join-Path/Test-Path` 错误；该命令的 exit 0 和链接列表无效 | 包装器改为 `$ErrorActionPreference='Stop'` 且根文档基准目录显式使用 `.` 后重新执行；首次运行没有修改文件，不记为 Pass |

开放工作项仍只有 #19、#20、#23、#24、#274；没有新的已授权产品写入 Issue。#23 明确 D23-11 和 P1～P5 Pending，#274 明确不授权立即签名、安装或分发。

## 3. 真实测试与安全结果

- M1 harness ValidateOnly：`Pass`，`startsProcess=false / drivesUserInput=false`；external-automation 真实 runtime preflight 精确返回 `BlockedByKnownUpstream`，未创建会话、未启动进程。
- 任务栏宿主预检连续 5 轮有限完成，全部 Blocked；没有系统状态或任务栏修改。
- signing ValidateOnly：`BlockedPendingApprovedPublisherCertificateAndManagedSigningProvider`，PR/main 无签名访问，私钥文件和自签名均不允许。
- MSIX lifecycle ValidateOnly：不启动进程、不修改 package state、不信任 unsigned 包；live evidence 仍为 `PendingSignedPackageAndDisposableWindowsProfile`。
- RC ValidateOnly：只承认 internal unsigned developer preview，许可证清算为 `PendingOwnerReviewAndNotice`，signed/installable/distributionApproved 全为 false。
- 完整本机门禁：locked restore、格式和 Release build 通过，`0 warning / 0 error`；关闭 build server 前完整套件 `1,382/1,382`、0 跳过，单份 coverage report 为 lines `90.41% (23538/26036)`、branches `76.17% (7730/10149)`。
- 供应链与文档门禁：Action pins、Dependabot、CodeQL workflow、漏洞、20 projects / 30 packages 许可证元数据均 Pass；修正包装器后 6 份变更文档 `missingLocalLinks=0`，`git diff --check` 通过。

这些是真实本机和 GitHub API Actual，但不是完整产品旅程。本机门禁已完成；Stage 227 文档提交仍须执行 PR CI/CodeQL、merge 与 main 复验。

## 4. 开发目标与需求对齐审计

开发目标对齐：本阶段关闭已合并阶段的权威状态漂移，并确认当前没有合法的产品 mutation 路径；没有用“继续写代码”替代真正的外部准入。需求对齐：三项 Core 顺序、本地优先、零惊吓、安全引用、不得在日常宿主试写任务栏、不得把 unsigned MSIX 视为可安装，以及 Expected / Actual / Difference / Correction 纪律均保持不变。

当前判定仍为：桌面盒子与单文件夹绑定 `EngineeringComplete / ProductEvidencePending`；任务栏 `EngineeringCompleteAtAdmissionBoundary / EnvironmentBlocked`；发布 `InternalUnsignedOnly / DistributionBlocked`；M1/M2 `0/2 Complete`。

## 5. 下一次状态变化的唯一触发条件

1. #23 由负责人批准 D23-11/商业边界并安排 P1～P5；#274 提供正式 Publisher、托管签名/OIDC、审批隔离和可丢弃安装环境。
2. 在无既有 Long方格进程、安全 WinUI/UIA runtime、签名包和可丢弃 Windows 账户/VM 上执行 M1；逐步保留物理操作的 Expected / Actual / Difference / Correction。
3. Stage 216 Guest 准入全部 ReadyToLaunch 后执行 TASKBAR-R2B1-B；宿主继续只读。
4. #19/#20/#24 的物理输入、显示/会话和真实卷矩阵进入同一受控环境。在上述外部状态没有变化时，只处理真实回归、质量门禁和安全缺陷，不插入自动整理、Tab、Widget、工作空间或新协议。

## 6. PR #287 与 main 最终集成证据

PR #287 已 squash merge 为 `main@29519568cfa9c4eeda19d557157cf8511389e938`，最终 PR head 与 merge commit 的 Git tree 均为 `e476bb9bbf4cb3eeb7aed7c67c60db0c8f4498bf`。PR CI run `33156144869` success：`1,382/1,382`、0 跳过，lines `90.12% (46926/52072)`、branches `76.04% (15434/20298)`，artifact 997,330 bytes；PR CodeQL run `33156144867` 的 C# 52 rules / 0 results、C++ 58 rules / 0 results。

合并后 main CI run `33156693058` success：`1,382/1,382`、0 跳过，lines `90.11% (46924/52072)`、branches `76.04% (15434/20298)`，artifact 997,556 bytes。main CodeQL run `33156693089` success；C# 52 rules / 0 results、C++ 58 rules / 0 results，open alerts 0。Stage 227 的边界复审与文档对齐因此关闭；该结论不改变本节之前记录的外部阻断，也不提升任何产品旅程状态。
