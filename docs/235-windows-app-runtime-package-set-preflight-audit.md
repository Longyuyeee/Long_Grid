# Stage 235：Windows App Runtime 完整包集合预检纠偏审计

日期：2026-08-29

输入基线：`origin/main@fd519c82e6a58e2f7251e3df14c741ac04f1394d`

状态：`CorrectionComplete / LocalPrAndMainAuditPassed / ProductEvidenceBlocked`

> 后续勘误（Stage 236）：本文首次实现正确补齐了 Runtime 包集合检查，但从机器最高 Framework `2.4.0.0` 反推精确 DDLM `2.4.0.0-x6` 的版本来源不正确。产品实际由 `LongGrid.App/packages.lock.json` 锁定 Runtime 最低版本 `2.3.1.0`，并由对应 SDK 元数据指定精确 DDLM `2.3.1.0-x6`；Framework/Main/Singleton 可选择满足最低版本的最佳兼容候选。当前权威结论见 [Stage 236](236-project-locked-runtime-target-preflight-audit.md)，本文其余内容作为当时审计记录保留。

## 1. 接续依据与原始需求对齐

Stage 234 真实启动已经证明：当前账户可以枚举 `Microsoft.WindowsAppRuntime.2` Framework 2.3.1/2.4.0，但仓库 Release 应用仍在进入用户 `Program.Main` 前显示“Required components of the Windows App Runtime are missing”。当前唯一执行计划只允许在 M1 外部条件未满足时关闭新发现的真实回归、质量或安全缺陷，不能增加邻接探针或把宿主错误框写成产品证据。

复读正式代码发现 `eng/Test-LongGridWinUiUiaRuntime.ps1` 只枚举 Framework 包和其中的 `Microsoft.UI.Xaml.dll`。它会把“Framework 可见”写成 `discoverableRuntime=true`，却没有验证 Windows App SDK 2.x 的 Main、Singleton 和与 Framework 版本/架构精确匹配的 DDLM，因此不能表达 Stage 234 已确认的 Runtime 安装不完整事实。

微软当前部署架构明确把 Framework、Main、Singleton 和 DDLM 定义为 Windows App SDK Runtime 包集合，并建议部署时始终安装全部包以避免用户体验中断；对于本项目使用 `<WindowsPackageType>None</WindowsPackageType>` 的 unpackaged App，Bootstrap 还必须初始化与 Framework 版本和架构对应的 DDLM：

- <https://learn.microsoft.com/windows/apps/windows-app-sdk/deployment-architecture>
- <https://learn.microsoft.com/windows/apps/windows-app-sdk/deploy-unpackaged-apps>
- <https://github.com/microsoft/WindowsAppSDK/blob/main/specs/Deployment/MSIXPackageVersioning.md>

## 2. Expected / 首次 Actual / Difference

| 检查 | Expected | 首次 Actual | Difference |
|---|---|---|---|
| Runtime 完整性 | 外部 UIA 或 M1 启动前同时确认匹配的 Framework、Main、Singleton、DDLM | 旧预检只选择最高 x64 Framework 并读取 XAML 版本 | `IncompleteRuntimePackageSetWasNotRepresented` |
| 当前机器 | 2.4.0.0 x64 Framework 应有同版本 `Main.2`、`Singleton@8002.4.0.0` 和 `DDLM.2.4.0.0-x6` | Framework 与 Singleton 存在；`MicrosoftCorporationII.WinAppRuntime.Main.2` 和 `Microsoft.WinAppRuntime.DDLM.2.4.0.0-x6` 缺失 | 与 Stage 234 宿主错误文本一致 |
| Live UI 入口 | 所有非 Pass 预检均在启动 App 前失败关闭 | 旧入口只专门处理 `BlockedByKnownUpstream`；若预检新增“不完整”状态但入口未同步，可能继续启动 | 消费者合同不完整 |

## 3. Correction

- Runtime 预检 schema 提升为 2，先选择最高版本 x64 `Microsoft.WindowsAppRuntime.2` Framework，再按 Windows App SDK 2.x 官方命名和版本规则构造并核对：
  - `MicrosoftCorporationII.WinAppRuntime.Main.<major>`；
  - `MicrosoftCorporationII.WinAppRuntime.Singleton@8000+major.minor.patch.revision`；
  - `Microsoft.WinAppRuntime.DDLM.<framework-version>-x6`。
- 缺少任一匹配组件时返回 `BlockedByIncompleteRuntime / IncompleteRuntimePackageSet`，同时输出有限的期望包名和缺失列表；Framework 不可发现仍为 `Inconclusive`。
- 只有完整包集合才继续判断既有 `2.4.0.0 / Microsoft.UI.Xaml.dll 3.2.3.0` 已知危险组合；危险组合仍返回 `BlockedByKnownUpstream`，没有放宽风险确认边界。
- Live UI 入口显式拒绝 `BlockedByIncompleteRuntime` 和 `Inconclusive`。`-AcknowledgeKnownUiaCrashRisk` 只能用于已审计的已知 UIA 风险，不能绕过缺失组件。
- 新增 `-ContractOnly` 四场景矩阵：Framework 缺失、不完整包集合、完整但已知危险组合、完整安全组合。C# 真实 PowerShell 5.1 进程测试执行该矩阵并锁定 Live UI 消费者合同。

## 4. 本机真实结果与副作用审计

修正后本机结果为：

- Framework `Microsoft.WindowsAppRuntime.2@2.4.0.0`：存在；
- XAML `3.2.3.0`：存在且仍属于已知危险组合；
- Singleton `MicrosoftCorporationII.WinAppRuntime.Singleton@8002.4.0.0`：存在；
- Main `MicrosoftCorporationII.WinAppRuntime.Main.2@2.4.0.0`：缺失；
- DDLM `Microsoft.WinAppRuntime.DDLM.2.4.0.0-x6@2.4.0.0`：缺失；
- 结论：`BlockedByIncompleteRuntime / runtimePackageSetComplete=false`。

M1 `-ExternalAutomation -NoBuild` 复测在产品启动和证据目录创建前返回同一状态，`startsProcess=false / createsEvidenceSession=false`；前后 LongGrid 进程数不变。没有安装、修复或卸载 Runtime，没有更改 Appx、注册表、系统安全策略、Explorer、任务栏或用户文件，也没有调用跨进程 UIA 或发送输入。

## 5. 本地验证

- 两份修改脚本 PowerShell AST parse：Pass；
- Runtime 四场景 `-ContractOnly`：`4/4`；
- 当前机器真实 Runtime 负向验证：Pass；
- M1 外部自动化零启动/零会话负向验证：Pass；
- Live UI 不完整 Runtime 负向验证：非零退出、精确缺失列表、产品进程 `0→0`；
- SDK/证据入口专项：`10/10`；
- `dotnet format --verify-no-changes`：Pass；
- Release solution build：`0 warning / 0 error`；
- 完整测试：`1,393/1,393`，0 skipped；
- 覆盖率：lines `90.40%`，branches `76.16%`。

## 6. 路线与完成度审计

开发目标审计：本阶段只修复 Runtime 完整性事实未被预检表达、Live UI 消费者可能继续启动两个真实门禁缺口，没有修改产品业务逻辑或扩张权限。需求对齐审计：变更加强了“真实证据优先、失败关闭、不把 Framework 枚举冒充完整 Runtime”的原始要求。

M1/M2 继续为 `0/2 Complete`；30 项 PF 仍为 `0 Complete`。BOX-R1-C/D、M1、TASKBAR-R2B1-B 和正式分发状态均未升级。

唯一接续点不变：由 #23/#274 提供许可证、Publisher、托管签名和签名包，在安装完整匹配 Windows App Runtime、具备安全 WinUI/UIA 且无既有 Long方格进程的独占可丢弃 Windows 会话中执行 BOX-R1 三场景和 M1 完整物理旅程。外部事实未变化时只处理新的真实回归、质量或安全缺陷。

## 7. 远端审计结果

实现提交 `bd26965` 与审计提交 `cd19db8` 经 [PR #300](https://github.com/Longyuyeee/Long_Grid/pull/300) 合并为 `main@0771554189c2e74eaa679a020e9a9540ed3e2e34`。PR CI run `33257117201` 用时 7m29s：完整测试 `1,393/1,393`、0 skipped，coverage lines `90.11% (46,924/52,072)`、branches `76.04% (15,434/20,298)`；漏洞为 0，许可证门保持 `PendingOwnerReviewAndNotice`，SBOM `805/805`，内部 RC 保持 `signed=false / installable=false / distributionApproved=false`。PR CodeQL run `33257117202` 中 C++ 58 rules / 0 results、C# 52 rules / 0 results。

精确合并提交自己的 main CI run `33257495444` 用时 7m20s：完整测试 `1,393/1,393`、0 skipped，coverage lines `90.12% (46,926/52,072)`、branches `76.04% (15,434/20,298)`；漏洞、许可证、SBOM `805/805` 与 unsigned RC 否定性门禁全部通过。main CodeQL run `33257495438` 成功，Code Scanning API 对该提交返回 C++ 58 rules / 0 results、C# 52 rules / 0 results。

PR 和精确 main 均未产生新的代码、质量、安全或供应链差异。本文只回填已完成的远端事实；它自己的文档提交仍须经独立 PR/main 检查，但不反向替代上述实现提交审计，也不改变产品完成度或唯一接续点。
