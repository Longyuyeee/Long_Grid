# Stage 258：M1 正式 App 精确 marker 消费安全审计

日期：2026-09-01

输入基线：`origin/main@fc29a40dfff80b047d479c4008565359ef53720c`

状态：`ImplementationComplete / LocalAuditPass / PullRequestPending / ExternalEnvironmentBlocked`

## 1. 接续、需求与真实差异

从 Stage 257 最终 main 复读 PRD、统一计划、cleanup 和正式 App 会话消费代码。#23/#274 无更新；M1 仍因缺 Main.2、项目锁定 DDLM 与已知不安全 XAML 组合返回 `BlockedByIncompleteRuntime / startsProcess=false / createsEvidenceSession=false`。TASKBAR Host 仍为 `Blocked / mutationAllowed=false / modifiedSystemState=false`，差异为 `HardwareEvidenceUnavailable / WindowsSandboxLauncherMissing / SandboxConfigurationMissing`。

原始安全要求是 M1 证据会话只能由专用根下、非重解析点目录和精确所有权 marker 授权。Stage 257 已让 cleanup 对 marker 原文大小写敏感、逐字符匹配，但正式 `ProductM1ManualEvidenceSession.TryCreateFromEnvironment()` 仍读取 `File.ReadAllText(markerPath).Trim()`。因此同一份 ` GUID ` 非精确 marker 会被 cleanup 拒绝，却被 App 启动端接受，属于实际代码与文档已宣称安全语义的偏移。

## 2. Expected / Initial Actual / Correction

| 检查 | Expected | Initial Actual | Correction |
|---|---|---|---|
| 带前后空白 marker | App 在创建会话前拒绝 | `Assert.Throws Failure: No exception was thrown` | 移除 App 消费端 `.Trim()`，保留 `StringComparison.Ordinal` |
| 精确 marker | 继续创建隔离会话 | 无直接消费端对照 | 新增精确 marker 正向回归 |
| 启动/清理语义 | 两端对同一所有权令牌给出一致结论 | cleanup 拒绝，App 接受 | 两端均按原文精确 GUID 匹配 |

测试项目以 linked compile 直接编译正式 `ProductM1ManualEvidenceSession.cs`，不重写验证器或用静态字符串断言代替行为。环境变量测试集禁止并行，并在 `finally` 恢复原值及删除测试自有 GUID 目录。

## 3. 本地审计结果

| 门禁 | Actual |
|---|---|
| App marker 消费端 | `2/2` |
| M1 相关专项 | `10/10` |
| restore / format / Release | Pass；format attempts=1；`0 warning / 0 error` |
| 完整测试 | `1,405/1,405`、0 failed、0 skipped、18 秒 |
| Coverage | lines `90.44% (23,543/26,032)`；branches `76.17% (7,729/10,147)` |
| UI / 执行源 | 198 required Automation IDs；Stage 258 freshness 在 Windows PowerShell 5.1、PowerShell 7 与 Action-pin 聚合入口均 Pass |
| 依赖 / 许可证 | 已知漏洞 0；20 项目/30 包；`PendingOwnerReviewAndNotice / distributionApproved=false` |

覆盖率使用全新 ignored `artifacts/stage258-test-results`。首次精确测试命令命中 PATH 首项 x86 `dotnet` 而看不到 SDK，按已有 Stage 252 宿主合同改用 `C:\Program Files\dotnet\dotnet.exe`，未改变 SDK 版本或测试预算。

## 4. 需求对齐与下一接续点

本轮只收紧已有 M1 证据所有权消费，不修改产品配置、用户文件、Runtime、任务栏、签名、安装或分发状态。M1/M2 继续 `0/2 Complete`，30 项 PF 继续 `0 Complete`，内部 RC 继续不可分发。

下一唯一接续点仍由 `#23/#274`、完整兼容 Runtime、受保护签名包、独占可丢弃 Windows 会话或 Stage 216 TASKBAR Host/Guest 准入持有；未满足时只处理新复现的真实回归、质量或安全缺陷。PR 与 main 结果待远端门禁后补入。
