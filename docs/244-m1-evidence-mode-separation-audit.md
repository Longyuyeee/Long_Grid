# Stage 244：M1 证据模式分离与 Runtime 准入防误判审计

日期：2026-08-31

输入基线：`origin/main@70787059f694ed7519d25fb04b081aa8fcbef99b`

状态：`CorrectionComplete / LocalVerificationPass / PullRequestPending / ProductStatusUnchanged`

## 1. 接续判断

按 Stage 241 的停止规则重新读取真实外部事实：#23 与 #274 仍为 OPEN，最后更新时间仍分别是 `2026-08-28T03:51:37Z` 与 `2026-08-28T02:43:13Z`；`long-grid-release` 只有 reviewer/branch protection，未出现 Publisher、托管签名提供方或分发批准。签名合同继续为 `BlockedPendingApprovedPublisherCertificateAndManagedSigningProvider`，`liveSigningImplemented=false / installOrDistributionApproved=false`。

本机 Runtime schema 5 的 Actual 仍为 Framework `2.4.0.0`、XAML `3.2.3.0`、Singleton `8002.4.0.0` 可读，缺 Main.2 `>=2.3.1.0` 与精确 DDLM `2.3.1.0-x6`，同时 `knownUnsafePairAbsent=false`；Outcome=`BlockedByIncompleteRuntime`。因此不允许进入 BOX-R1-C/D 正向安装和 M1 物理旅程，也不新增相邻功能。

## 2. Expected / Actual / Difference / Correction

| 检查 | Expected | Initial Actual | Difference / Correction |
|---|---|---|---|
| M1 静态合同 | `-ValidateOnly` 只验证启动器静态合同，不代表机器 Runtime 可用 | 单独调用返回 `mode=validate-only / outcome=Pass` | None |
| 外部自动化准入 | `-ExternalAutomation` 必须消费真实 Runtime 预检，并在不安全时零启动 | 单独调用返回 `BlockedByIncompleteRuntime`、`startsProcess=false / createsEvidenceSession=false` | None；LongGrid 进程 `1→1`、M1 证据目录 `0→0` |
| 模式组合 | 静态合同与机器准入不能在一个调用中被混淆 | `-ValidateOnly -ExternalAutomation` 实际返回普通 `Pass`，因为 ValidateOnly 分支先退出并完全跳过 Runtime 预检 | 新的安全合同差异；在任何读取/构建/启动前明确拒绝该组合 |
| 自动回归 | 必须以真实 PowerShell 子进程证明非零退出与零副作用 | 原套件只有脚本源码合同，没有执行该错误组合 | 新增真实进程测试，断言精确错误、非零退出、LongGrid.App PID 集合和证据目录集合不变 |

修正后的组合调用 exit=`1`，错误为 `ValidateOnly and ExternalAutomation are mutually exclusive.`；真实专项 `1/1` 通过，进程与证据目录 Difference 均为 0。第一次审计包装器使用 `[string]::Join` 比较空目录数组时自身抛出 `Value cannot be null`；改用 `Compare-Object` 后复测，确认该错误来自包装器而不是产品脚本创建目录。

## 3. 合法路径与完整真实验证

| 门禁 | Expected | Actual | Difference |
|---|---|---|---|
| 合法 ValidateOnly | 静态合同继续 Pass、零进程 | `Pass / startsProcess=false / drivesUserInput=false` | None |
| 合法 ExternalAutomation | 真实缺失 Runtime 时在启动前阻断 | `BlockedByIncompleteRuntime / startsProcess=false / createsEvidenceSession=false` | None |
| Format | 无格式差异 | 绝对 host，attempts=1，`transientRetryObserved=false` | None |
| Release build | 0 warning / 0 error | 0 warning / 0 error | None |
| 完整测试 | 新增回归进入全套且全部通过 | `1,396/1,396`、0 skipped、31 秒 | None |
| Coverage | lines >=90%、branches >=75% | lines `90.46% (23548/26032)`；branches `76.16% (7728/10147)` | None |
| 依赖风险 | 漏洞 0；许可证未批准前禁止分发 | 漏洞 0；20 项目/30 包；`PendingOwnerReviewAndNotice / distributionApproved=false` | None |

## 4. 开发目标与需求对齐审计

开发目标审计：本阶段没有假定外部条件变化，而是按唯一接续点重跑真实 Runtime、签名、Issue 和 M1 入口。新发现的模式误判差异已由最小互斥检查和真实子进程回归关闭，两个合法模式与完整套件均未回归。PR 与合并后 main 证据仍 Pending。

需求对齐审计：修正加强“真实准入不能由静态合同 Pass 替代”的既有要求，不安装 unsigned 包、不启动新产品进程、不创建证据会话、不终止现有进程，也不降低 Runtime、签名、许可证或物理旅程门槛。M1/M2 继续 `0/2 Complete`、30 项 PF 继续 `0 Complete`；下一唯一产品接续点仍是外部条件满足后的 BOX-R1-C/D 与 M1 完整物理旅程。
