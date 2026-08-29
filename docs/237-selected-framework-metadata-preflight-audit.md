# Stage 237：最高兼容 Framework 元数据闭锁纠偏审计

日期：2026-08-29

基线：`origin/main@a03bc37c99f330e46334b12e5a427085f695cbf3`

状态：`CorrectionComplete / PrAndMainAuditPassed / ProductEvidenceBlocked`

> 后续补全：本文结果函数对“选中候选元数据不可读”的闭锁语义保持有效；真实文件访问或版本解析异常在进入结果函数前终止的问题，已由 [Stage 238](238-runtime-metadata-read-failure-audit.md) 归一化为同一结构化 `Inconclusive`。本文其余最高候选选择结论继续有效。

## 1. 接续复读与路线约束

本轮先同步并复读 Stage 236、`eng/Test-LongGridWinUiUiaRuntime.ps1`、Live UI 消费者、真实进程测试与项目锁文件。#23/#274 没有新的负责人输入，开放 PR 为 0；完整兼容 Runtime、受保护签名包和独占可丢弃 Windows 会话仍未到位。因此本轮继续遵守既定路线：不扩张邻接探针，只关闭会影响真实失败关闭判断的新发现回归、质量或安全缺陷。

Stage 236 已正确把最低 Runtime 与精确 DDLM 身份锚定到项目锁文件，但脚本在选择最高 Framework 之前先要求 `Microsoft.UI.Xaml.dll` 元数据可读。Bootstrap 的候选身份由包名、架构和最低版本决定；元数据可读性是对已选候选的诊断条件，不是把它从候选集合中删除的理由。

## 2. Expected / Actual / Difference

| 检查 | Expected | 首次 Actual | Difference |
|---|---|---|---|
| Framework 选择对象 | 先选最高兼容 x64 Framework，与 Bootstrap 实际对象一致 | 先过滤 XAML 元数据不可读候选，再从剩余集合选最高 | `UnreadableHighestFrameworkWasSkipped` |
| 选中候选元数据不可读 | 启动前显式 `Inconclusive`，保留被选中的版本 | 静默退回较旧且可读的 Framework | `SelectedRuntimeFrameworkMetadataNotDiscoverable` 未被表达 |
| Live UI 消费者 | 对目标、候选或候选元数据不可确认统一失败关闭并报告 Difference | 提示只声称“找不到兼容 Framework” | `InconclusiveMessageWasTooNarrow` |

该差异可能让预检评估不同于应用 Bootstrap 将加载的版本，并在旧候选安全时产生假通过，属于当前唯一接续约束允许修复的真实失败关闭准确性缺陷。

## 3. Correction

- Framework 候选只按项目 Runtime 大版本、x64 架构和不低于项目最低版本筛选，然后按版本降序选出唯一评估对象。
- 选中最高候选后才读取其 XAML 元数据。元数据不可读时返回 schema 4：`discoverableRuntime=true`、`selectedFrameworkMetadataDiscoverable=false`、保留 `runtimePackageVersion`，包集合与已知危险对均不作假结论。
- Difference 固定为 `SelectedRuntimeFrameworkMetadataNotDiscoverable`，Outcome 为 `Inconclusive`；Live UI 在创建产品进程前失败关闭并带出 Difference。
- 合同 schema 升为 3，并新增“较低安全 Framework 可读、较高 Framework 元数据不可读”场景，断言必须选择较高版本且不得回退；总场景数由 6 增为 7。
- Stage 236 保留项目锁定最低版本与精确 DDLM 的正确结论，同时增加显著后续纠偏说明，不改写历史。

## 4. 实际环境与副作用审计

当前机器的最高兼容 x64 Framework 仍为 `Microsoft.WindowsAppRuntime.2@2.4.0.0`，其 XAML `3.2.3.0` 可读；Singleton `8002.4.0.0` 兼容。缺失项仍为 `MicrosoftCorporationII.WinAppRuntime.Main.2@2.3.1.0-or-later` 与精确 `Microsoft.WinAppRuntime.DDLM.2.3.1.0-x6`，结果为 `BlockedByIncompleteRuntime`，已知危险对判断仍为 false（即危险对存在）。

Live UI 负向复测退出码为 1，LongGrid 进程 `0→0`。M1 `-ExternalAutomation -NoBuild` 返回 `startsProcess=false / createsEvidenceSession=false`，证据目录 `0→0`。本轮没有安装、修复或卸载 Runtime，没有修改 Appx、注册表、Explorer、任务栏、安全策略或用户文件，也没有调用跨进程 UIA 或发送输入。

## 5. 本地验证

- 两份 PowerShell 脚本 AST parse：Pass；
- Runtime 七场景合同：`7/7`；
- SDK/证据入口专项真实 PowerShell 进程测试：`10/10`；
- `dotnet format --verify-no-changes`：Pass；
- Release solution build：`0 warning / 0 error`；
- 完整测试：`1,393/1,393`，0 skipped；
- 独立结果目录覆盖率：lines `90.41% (47,078/52,072)`，branches `76.17% (15,460/20,298)`。

## 6. 路线与完成度审计

本轮只修正预检评估对象与 Bootstrap 实际选择对象的错位，没有改变产品业务逻辑、权限、系统状态或发布门禁，未偏离最初“真实证据优先、失败关闭、不注入 Explorer、不擅改用户文件”的路线。

M1/M2 继续为 `0/2 Complete`，30 项 PF 继续为 `0 Complete`；BOX-R1-C/D、TASKBAR-R2B1-B、签名、安装生命周期和正式分发均未升级。工程测试通过不能替代真实用户旅程证据。

唯一接续点不变：等待 #23/#274 提供许可证、Publisher、托管签名和签名包；在安装完整兼容 Runtime、具备安全 WinUI/UIA、没有既有 Long方格进程的独占可丢弃 Windows 会话中，执行 BOX-R1 三场景与 M1 完整物理旅程。外部事实未变化时，继续只处理新的真实回归、质量或安全缺陷。

## 7. 远端审计

实现提交 `ab0c4ad` 与文档提交 `653f9bd` 经 [PR #304](https://github.com/Longyuyeee/Long_Grid/pull/304) 合并为 `main@a8664d233c8eb80a615fa328bb0be236b33459df`。PR CI run `33261128658` 全部通过：完整测试 `1,393/1,393`、0 skipped，coverage lines `90.12% (46,926/52,072)`、branches `76.04% (15,434/20,298)`；漏洞 0，许可证继续为 `PendingOwnerReviewAndNotice`，SBOM `805/805`，内部 RC 保持 `signed=false / installable=false / distributionApproved=false`。PR CodeQL run `33261128715` 的 C++、C# 分析均成功，Code Scanning API 对分支返回开放告警 0。

精确合并提交自己的 main CI run `33261498010` 全部通过：完整测试 `1,393/1,393`、0 skipped，coverage lines `90.12% (46,926/52,072)`、branches `76.04% (15,434/20,298)`；漏洞 0、许可证待审、SBOM `805/805` 与 unsigned RC 否定性门禁均保持。main CodeQL run `33261498018` 的 C++、C# 分析均成功，main 开放告警 0。

PR 与精确 main 没有产生新的代码、质量、安全或供应链差异。远端门禁不能替代尚未取得的物理产品证据；本次回填不改变 M1/M2、PF 或唯一接续点。本文自己的文档提交只需独立通过 PR/main 检查，不再追加第三层文档收口。
