# Stage 256：M1 marker 前空会话目录清理审计

日期：2026-09-01

输入基线：`origin/main@fb5af3c864edc1b5e411c73e5df41da4ded16c21`

状态：`ImplementationComplete / PullRequestHeadPass / MergePending / ExternalEnvironmentBlocked`

## 1. 接续条件与开发目标

从 Stage 255 最终 main 重新同步并复读 PRD、统一计划、开发流程和实际启动器。#23 仍为 OPEN，最后更新 `2026-08-28T03:51:37Z`；#274 仍为 OPEN，最后更新 `2026-08-28T02:43:13Z`。Runtime 实测仍为 Framework `2.4.0.0`、XAML `3.2.3.0`、Singleton `8002.4.0.0`，缺 Main.2 `>=2.3.1.0` 与 DDLM `2.3.1.0-x6`；M1 ExternalAutomation 返回 `BlockedByIncompleteRuntime / startsProcess=false / createsEvidenceSession=false`。TASKBAR Host 返回 `HardwareEvidenceUnavailable / WindowsSandboxLauncherMissing / SandboxConfigurationMissing`、`Blocked / mutationAllowed=false / modifiedSystemState=false`。

两条正向产品入口均未准入，本阶段不扩展功能或邻接探针。实际代码显示 M1 会先创建 `config` 和 Unicode fixture，再写 `.longgrid-m1-session` 所有权 marker；Stage 253 明确只覆盖 marker 后异常。因此 marker 写入前若准备失败，会遗留无法由严格 cleanup 接管的无 marker GUID 半成品目录。

## 2. Expected / Initial Actual / Difference / Correction

| 检查 | Expected | Initial Actual | Difference / Correction |
|---|---|---|---|
| marker 前准备失败 | 非零退出；本次证据目录与 LongGrid PID 集合不变 | 测试自有脚本和隔离证据根在 marker 前注入异常，真实遗留无 marker GUID 目录 `c666bd31468f41daa8b27d671a617a12` | 先独占创建空会话目录与 marker，再创建配置/夹具 |
| 无 marker 清理所有权 | 不得对未知内容递归删除 | 旧代码不清理，避免误删但留下半成品 | 只对本次已确认创建、仍为空、非重解析点的精确 GUID 目录执行非递归删除；非空目录拒绝 |
| marker 后生命周期 | 准备/启动/Ready 失败继续严格清理 | Stage 253/247 已有精确 marker cleanup | marker 成功后仍只调用既有 `Remove-EvidenceDirectory` |
| 零副作用模式 | ValidateOnly/ExternalAutomation 阻断不创建根或 session | 现有分支在创建逻辑前返回 | 分支顺序不变；Windows PowerShell 5.1 与 PowerShell 7 ValidateOnly 均 Pass |

修正前测试精确失败为 `Assert.Empty() Failure: Collection was not empty`，集合包含上述本次 GUID；finally 只在确认它属于测试唯一证据根、名称为精确 GUID 且不存在 marker 后删除。修正后同一注入返回原始有限错误，证据目录和 LongGrid PID 集合均不变；M1 相邻真实进程专项 `7/7` 通过。

## 3. 本机与远端验证

| 门禁 | Expected | Actual | Difference |
|---|---|---|---|
| M1 marker 前与相邻生命周期 | 无残留，不回归既有严格 cleanup | `7/7` | None |
| Locked restore / format / Release | 锁定依赖、零格式差异、0 warning/error | 全部通过；format attempts=1；Release `0 warning / 0 error` | None |
| 完整测试 | 新回归进入全套 | `1,402/1,402`、0 failed、0 skipped、25 秒 | None |
| Coverage | lines >=90%、branches >=75% | lines `90.43% (23,542/26,032)`；branches `76.17% (7,729/10,147)` | None |
| UI / 执行源 | 产品合同与接续入口不退化 | UI ContractOnly `198` IDs；Stage 256 freshness 与 Action pins Pass | None |
| 漏洞与许可证 | 漏洞 0；未批准前禁止分发 | 漏洞 0；20 项目/30 包；`PendingOwnerReviewAndNotice / distributionApproved=false` | None |

覆盖率使用独立 ignored `artifacts/stage256-test-results`，未聚合历史 `TestResults`。当前没有安装 Runtime、启用 Sandbox、修改任务栏、启动 LongGrid、发送物理输入、签名、安装或分发产物。

## 4. 开发目标与需求对齐审计

开发目标审计：本阶段关闭 Stage 253 明确保留的 marker 前空目录生命周期缺口，没有把失败路径脚本数量记为产品功能进度。删除边界比已标记 cleanup 更窄：只允许删除本次创建且仍为空的目录，不允许递归处理未知内容。

需求对齐审计：修正落实 README“零惊吓”和 PRD 证据所有权边界，未改变产品配置、用户文件、桌面、任务栏、Runtime、签名、安装或分发状态。M1/M2 继续 `0/2 Complete`，30 项 PF 继续 `0 Complete`，内部 RC 继续不可分发。

下一唯一接续点仍由 `#23/#274`、完整兼容 Runtime、受保护签名包和独占可丢弃 Windows 会话共同约束；TASKBAR-R2B1-B 仍要求 Stage 216 Host/Guest 达到 `ReadyToLaunch / GuestReady`。两者未成立时，只处理新复现的真实回归、质量或安全缺陷。

## 5. 远端交付

实现提交 `0ceaf48e9d6130a146ca3176548cdd2b133f30fe` 已推送到短分支并创建 PR #338；PR 无评论、无 review，状态 `MERGEABLE / CLEAN`。CI run `33414842475` 通过（`7m57s`）：完整测试 `1,402/1,402`、0 skipped、29 秒，coverage lines `90.14% (46,932/52,064)`、branches `76.04% (15,432/20,294)`，UI 合同 `198` IDs，漏洞 0，20 项目/30 包，许可证继续 `PendingOwnerReviewAndNotice / distributionApproved=false`。测试与覆盖率 artifact `9766849837`，1,003,515 bytes，digest `sha256:5f95e52dba233f25a933f589a63f111a03c858ae7412c1198c95d400ae55b48b`。

同一 head 的 CodeQL run `33414842430` 通过：C/C++ `2m57s`，C# `6m59s`。本文记录首轮结果后形成纯审计收口提交；最终 PR head 与合并后的精确 main 仍须重新验证，未完成前不把本阶段写成远端闭环。
