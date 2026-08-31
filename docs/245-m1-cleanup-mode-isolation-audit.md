# Stage 245：M1 清理模式隔离与证据生命周期防遮蔽审计

日期：2026-08-31

输入基线：`origin/main@2f93ec8e5a70d5dd8bfc1a6d5ffa4e3026e9c9ff`

状态：`CorrectionComplete / LocalAndPullRequestVerificationPass / MainVerificationPending / ProductStatusUnchanged`

## 1. 接续条件复读

#23 与 #274 仍为 OPEN，最后更新时间仍分别为 `2026-08-28T03:51:37Z` 与 `2026-08-28T02:43:13Z`。`long-grid-release` 仍只有 required reviewer 与 protected branch policy，没有新的 Publisher、托管签名提供方或分发批准。签名 ValidateOnly 继续返回 `BlockedPendingApprovedPublisherCertificateAndManagedSigningProvider`，`liveSigningImplemented=false / installOrDistributionApproved=false`。

本机真实 WinUI/UIA Runtime schema 5 仍读取到 Framework `2.4.0.0`、XAML `3.2.3.0`、Singleton `8002.4.0.0`，缺 Main.2 `>=2.3.1.0` 与精确 DDLM `2.3.1.0-x6`，且 `knownUnsafePairAbsent=false`；Outcome=`BlockedByIncompleteRuntime`。因此 BOX-R1-C/D 与 M1 正向物理旅程继续停止，本阶段只修复已有 M1 入口的证据生命周期差异。

## 2. Expected / Initial Actual / Difference / Correction

| 检查 | Expected | Initial Actual | Difference / Correction |
|---|---|---|---|
| 清理与外部自动化组合 | cleanup 是独立生命周期操作；不能被启动准入结果静默替代 | 真实创建带精确 marker 的临时会话后调用 `-ExternalAutomation -CleanupSessionId`，返回 `BlockedByIncompleteRuntime`，目标目录仍存在 | 清理请求被 Runtime 预检遮蔽；现在明确拒绝组合调用，要求分开执行 |
| 非法组合副作用 | 非零退出，且不读 Runtime、不删目录、不启动或终止 LongGrid | 修正前返回 0/阻断 JSON；LongGrid 进程 `1→1`，目录保留 | 新增真实 Windows PowerShell 子进程回归；同时覆盖 ValidateOnly/ExternalAutomation 与 CleanupSessionId 的组合 |
| 合法清理 | 单独 cleanup 必须按精确 GUID、marker 与非 reparse-point 合同删除目标 | 修正前单独调用已 Pass | 修正后继续 Pass；同一真实 marker 目录被删除 |

修正后的手工真实复测：非法组合 exit=`1`，错误包含 `CleanupSessionId cannot be combined with ValidateOnly or ExternalAutomation.`，证据目录保持、LongGrid 进程 `1→1`，Difference=`None`；随后单独 cleanup 返回 `Pass / removed=true`，目录不存在，Difference=`None`。新增专项真实测试 `1/1` 通过。

第一次复现包装器直接使用 `Remove-Item` 被执行策略拒绝，未创建或删除夹具；第二次包装器把 `New-Item -LiteralPath` 用在当前不支持该参数的命令上，夹具未创建，因此其 `existsAfter=false` 不构成产品证据。修正包装器为先校验绝对前缀、使用 `New-Item -Path` 并断言目录存在后，才得到上述有效 Initial Actual；最终清理始终由产品脚本自己的 marker/path/reparse-point 合同完成。

## 3. 合法路径与本地完整真实验证

| 门禁 | Expected | Actual | Difference |
|---|---|---|---|
| ValidateOnly | 静态合同 Pass、零启动 | `Pass / startsProcess=false` | None |
| ExternalAutomation | Runtime 不完整时零启动、零建会话 | `BlockedByIncompleteRuntime / startsProcess=false / createsEvidenceSession=false` | None；进程差 0、证据目录差 0 |
| Format | 无格式差异 | 绝对 SDK host，attempts=1、`transientRetryObserved=false` | None |
| Release build | 0 warning / 0 error | 0 warning / 0 error | None |
| 完整测试 | 新回归进入全套并全部通过 | `1,397/1,397`、0 skipped、35 秒 | None |
| Coverage | lines >=90%、branches >=75% | lines `90.46% (47096/52064)`；branches `76.16% (15456/20294)` | None |
| 依赖门禁 | 漏洞 0；未批准前禁止分发 | 漏洞 0；20 项目/30 包；`PendingOwnerReviewAndNotice / distributionApproved=false` | None |

完整测试第一次也为 `1,397/1,397`，但覆盖率脚本聚合工作区 `TestResults` 中 43 份历史 coverage，得到失真的 `88.21%/74.77%` 并正确失败。确认该目录位于工作区、tracked files=0 后仅清理该生成目录，关闭 build server 并重新生成唯一 coverage，得到上表真实结果。该差异属于测试编排污染，不修改覆盖率阈值或产品代码。

## 4. PR #320 真实远端验证

精确提交 `411a050` 的 CI run `33358329483` 与 CodeQL run `33358329466` 均成功：Format 使用绝对 SDK host，attempts=1、`transientRetryObserved=false`；完整测试 `1,397/1,397`、0 skipped、24 秒；coverage lines `90.13% (46926/52064)`、branches `76.03% (15430/20294)`；漏洞 0；许可证继续 `PendingOwnerReviewAndNotice / distributionApproved=false`；artifact `9745979066`、1,002,858 bytes；C# / C++ CodeQL 成功且分支 open alerts=0。Difference=`None`。

## 5. 开发目标与需求对齐审计

开发目标审计：本阶段关闭“cleanup 请求可被 ValidateOnly/ExternalAutomation 分支遮蔽”的现有入口差异；非法组合失败关闭、合法 cleanup、两个合法非清理路径、真实子进程回归、本地完整门禁和精确 PR 提交的远端门禁均已通过。最终文档 head 与合并后精确 main 证据仍 Pending。

需求对齐审计：修正只隔离现有操作模式，不安装 unsigned 包、不修改 Runtime/Publisher/签名权限、不终止既有 LongGrid 进程，也不增加新的 M1 邻接探针。M1/M2 继续 `0/2 Complete`、30 项 PF 继续 `0 Complete`；下一唯一产品接续点仍是外部条件满足后的 BOX-R1-C/D 与 M1 完整物理旅程。
