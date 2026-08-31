# Stage 249：当前执行源与真实接续条件新鲜度审计

日期：2026-08-31

输入基线：`origin/main@a35e1d47fc3c00ace8149d02038027bb80a1739c`

状态：`Complete / SourceOfTruthCorrected / ExternalEnvironmentBlocked`

## 1. 开发目标

Stage 248 已用本机真实 self-contained 启动证明 Windows App SDK 2.4.0 仍复现相同 XAML 崩溃指纹，并撤回无收益升级。进入下一步时，权威统一执行计划的页头仍写 `origin/main@2a7c811 / Stage 247`，当前队列又把 Stage 234 写成“最新精确接续条件”；Stage 153 详细 backlog 仍停在更早的 `fd519c8 / Stage 235`。这会让后续接手者跳过 Stage 246～248 的真实窗口 Ready、清理和 2.4.0 对照结论。

本阶段目标是先重跑两个唯一合法产品入口的真实准入，再把权威文档更新到当前 main 和最新证据。若准入仍未成立，只修正文档，不增加 M1 邻接探针、不转做外围功能、不在宿主执行任务栏写入。

## 2. Expected / Actual / Difference / Correction

| 检查 | Expected | Actual | Difference / Correction |
|---|---|---|---|
| M1 外部自动化准入 | Runtime 包集合完整、已知风险不存在后才启动 | Framework `2.4.0.0` / XAML `3.2.3.0`；缺 Main.2 `>=2.3.1.0` 与 DDLM `2.3.1.0-x6`；`BlockedByIncompleteRuntime` | 当前电脑仍不准入；`startsProcess=false / createsEvidenceSession=false` |
| TASKBAR-R2B1-B 准入 | Host 返回 `ReadyToLaunch` 后才允许 Guest mutation | `Blocked`；`HardwareEvidenceUnavailable / WindowsSandboxLauncherMissing / SandboxConfigurationMissing` | `mutationAllowed=false / modifiedSystemState=false`；不得改在宿主试写 |
| 副作用 | 准入检查不得处置既有产品进程或创建 M1 会话 | LongGrid PID 集合 `45524 → 45524`；证据目录集合 `0 → 0` | Difference=`None` |
| 权威执行基线 | 指向当前 main、最新 Runtime 审计和当前唯一入口 | 统一计划仍停在 `2a7c811 / Stage 247 / Stage 234`；详细 backlog 停在 `fd519c8 / Stage 235` | 更新至 `a35e1d4 / Stage 248 / Stage 249`，保留 Stage 241 的整体换机边界 |
| 外部负责人输入 | #23/#274 有变化时才能进入许可证、签名或安装实现 | #23 仍 OPEN，最后更新 `2026-08-28T03:51:37Z`；#274 仍 OPEN，最后更新 `2026-08-28T02:43:13Z` | 没有新增授权；签名、安装、分发和五人证据继续 Pending |

## 3. 真实测试要求

本轮准入直接调用仓库正式 PowerShell 入口，读取真实 Appx Runtime 清单、真实 Windows/Sandbox 可用性和真实进程/临时证据目录集合。它们不是 mock 或静态文本检查。最终文档树的本机结果如下：

| 门禁 | Expected | Actual | Difference |
|---|---|---|---|
| Locked restore / format / Release build | 锁文件可复现、零格式差异、0 warning/error | 全部通过；Release `0 warning / 0 error` | None |
| 完整测试 | 当前基线全部真实执行 | `1,398/1,398`、0 failed、0 skipped、41 秒 | None |
| UI 合同 | 产品合同不退化 | ContractOnly `198` IDs，Pass | None |
| 漏洞 | 已知漏洞 0 | 真实锁定依赖扫描通过，0 | None |
| 许可证 | 真实清单完整，未审批时禁止分发 | 20 项目/30 包，metadata complete；正/负确定性门禁通过；`PendingOwnerReviewAndNotice / distributionApproved=false` | None |

PR head 与合并后精确 main 仍须通过远端 CI/CodeQL；远端结果未完成前不得把本阶段标记为合并完成。

## 4. 开发目标与需求对齐审计

开发目标审计：已把“下一步从哪里继续”的权威入口与当前 main、最新 Runtime 事实重新对齐；没有用新的阶段编号制造功能完成度，也没有重复 2.4.0 启动实验。

需求对齐审计：M1/M2 继续 `0/2 Complete`，30 项 PF 继续 `0 Complete`。本轮没有安装 Runtime、启用 Windows Optional Feature、提权、重启、调用 UIA、发送输入、修改任务栏、签名或分发。

下一接续点保持单一且可执行：#23/#274 提供许可证、Publisher、托管签名和受保护签名包，同时取得完整兼容 Runtime与独占可丢弃 Windows 会话后，执行 BOX-R1-C/D 与 M1 两分钟物理旅程；或者 Stage 216 Host/Guest 准入真实达到 `ReadyToLaunch / GuestReady` 后，在 Guest 内执行 TASKBAR-R2B1-B。两者都未成立时，只处理新出现的真实回归、质量或安全缺陷，不扩展外围功能。
