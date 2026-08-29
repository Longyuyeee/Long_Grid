# Stage 236：项目锁定 Windows App Runtime 目标预检纠偏审计

日期：2026-08-29

基线：`origin/main@82eef33dcf90b6c05360e6477d33374cda5cf790`

状态：`CorrectionComplete / LocalPrAndMainAuditPassed / ProductEvidenceBlocked`

## 1. 接续约束与实际代码复读

Stage 235 规定：#23/#274、完整 Runtime、签名包和独占可丢弃 Windows 会话未到位时，只允许关闭新发现的真实回归、质量或安全缺陷，不得增加邻接探针或把宿主错误冒充产品证据。本轮开始前重新拉取 `main`、复核 #23/#274 与开放 PR；两项 Issue 无新负责人输入，开放 PR 为 0，因此 BOX-R1-C/D 与 M1 物理旅程仍不可执行。

复读 `eng/Test-LongGridWinUiUiaRuntime.ps1`、`src/LongGrid.App/LongGrid.App.csproj`、`src/LongGrid.App/packages.lock.json` 和还原的 Microsoft 包后发现：应用为 framework-dependent unpackaged WinUI，锁文件解析 `Microsoft.WindowsAppSDK.Runtime 2.3.1`。对应 `WindowsAppSDK-VersionInfo.json` 给出的 Runtime 最低版本是 `2.3.1.0`，x64 DDLM 家族是 `Microsoft.WinAppRuntime.DDLM.2.3.1.0-x6`；AutoInitializer 把该最低版本传给 Bootstrap。Stage 235 的脚本却选择机器最高 Framework `2.4.0.0`，并由它反推 DDLM `2.4.0.0-x6`，把已装最佳候选版本错误地当成项目目标身份。

## 2. Expected / Actual / Difference

| 检查 | Expected | 首次 Actual | Difference |
|---|---|---|---|
| 目标来源 | 由已提交的 LongGrid.App 锁文件确定实际 Runtime 最低版本 | 由机器最高已装 Framework 决定 | `InstalledFrameworkWasUsedAsProjectTarget` |
| Framework/Main | 同一大版本且版本不低于项目最低版本，选择最佳候选 | 强制等于最高 Framework 版本 | `CompatibleHigherVersionWasTreatedAsExactTarget` |
| Singleton | 还原 `8000+major` 编码后，与项目大版本一致且不低于最低版本 | 强制等于最高 Framework 的编码版本 | `SingletonCompatibilityWasNotProjectRelative` |
| DDLM | 使用项目 SDK 元数据对应的精确版本/架构身份 | 错误反推 `2.4.0.0-x6` | `DdlmIdentityDriftedFromBootstrapTarget` |
| 目标不可解析 | 在 App 启动前显式 `Inconclusive` | 可能在解析阶段直接异常 | `ProjectTargetFailureWasNotRepresented` |

## 3. Correction

- 从 `src/LongGrid.App/packages.lock.json` 的所有目标框架节点读取 `Microsoft.WindowsAppSDK.Runtime.resolved`，去重后必须恰好得到一个版本，并规范为四段版本。
- Framework 包名与枚举跟随项目目标大版本；从 x64、版本不低于最低版本且 XAML 元数据可读的候选中选择最高版本。
- Main 使用相同大版本并要求版本不低于项目最低版本；Singleton 先把 `8000+major.minor.build.revision` 还原为 Runtime 版本，再执行同样的兼容性判断。
- DDLM 保持项目锁定的精确 `version-architecture` 身份，不再跟随机器最高 Framework 漂移。
- 项目目标缺失、歧义或不可解析时返回 schema 3 的 `ProjectRuntimeTargetNotDiscoverable / Inconclusive`；Live UI 既有消费者继续在启动前失败关闭。
- 合同从四场景扩为六场景：目标缺失、Framework 缺失、包集合不完整、完整安全、完整已知危险、较新兼容组件配项目锁定 DDLM。

## 4. 当前机器真实结果与副作用审计

- 项目 Runtime 最低版本：`2.3.1.0`；
- 最佳 x64 Framework：`Microsoft.WindowsAppRuntime.2@2.4.0.0`；
- XAML：`Microsoft.UI.Xaml.dll 3.2.3.0`，已知风险不变；
- Singleton：`8002.4.0.0`，还原后为兼容 Runtime `2.4.0.0`；
- Main：缺少 `MicrosoftCorporationII.WinAppRuntime.Main.2@2.3.1.0-or-later`；
- DDLM：缺少精确 `Microsoft.WinAppRuntime.DDLM.2.3.1.0-x6`；
- 结论：`BlockedByIncompleteRuntime`。

Live UI 负向复测非零退出、LongGrid 进程 `0→0`。M1 `-ExternalAutomation -NoBuild` 返回 `startsProcess=false / createsEvidenceSession=false`，相关证据目录 `0→0`。本轮没有安装、修复或卸载 Runtime，没有修改 Appx、注册表、Explorer、任务栏、安全策略或用户文件，也没有发送跨进程 UIA 输入。

## 5. 本地验证

- 两份 PowerShell 脚本 AST parse：Pass；
- Runtime 六场景合同：`6/6`；
- SDK/证据入口专项真实 PowerShell 进程测试：`10/10`；
- `dotnet format --verify-no-changes`：Pass；
- Release solution build：`0 warning / 0 error`；
- 完整测试：`1,393/1,393`，0 skipped；
- 独立结果目录覆盖率：lines `90.43% (47,090/52,072)`，branches `76.16% (15,458/20,298)`。

## 6. 路线、完成度与接续结论

这是 Stage 235 新实现暴露出的真实失败关闭准确性缺陷，修正直接服务于 M1/Live UI 启动前事实判断，没有修改产品业务逻辑、扩大权限或增加邻接验证工作。Stage 235 的“完整集合”方向保留，但其版本来源已纠正为项目锁定元数据。

M1/M2 继续为 `0/2 Complete`；30 项 PF 仍为 `0 Complete`。BOX-R1-C/D、TASKBAR-R2B1-B、签名、安装生命周期和正式分发状态均未升级。

唯一接续点不变：等待 #23/#274 提供许可证、Publisher、托管签名和签名包，并在安装完整兼容 Runtime、具备安全 WinUI/UIA、无既有 Long方格进程的独占可丢弃 Windows 会话执行 BOX-R1 三场景与 M1 完整物理旅程。外部事实未变化时，继续只处理新的真实回归、质量或安全缺陷。

## 7. 远端审计

实现提交 `30de9dc` 与审计提交 `e4f7480` 经 [PR #302](https://github.com/Longyuyeee/Long_Grid/pull/302) 合并为 `main@00a4a99148fde31edbe757bbd2750bd0f80c772f`。PR CI run `33259079890` 用时 7m43s：完整测试 `1,393/1,393`、0 skipped，coverage lines `90.11% (46,922/52,072)`、branches `76.03% (15,432/20,298)`；漏洞为 0，许可证继续为 `PendingOwnerReviewAndNotice`，SBOM `805/805`，内部 RC 保持 `signed=false / installable=false / distributionApproved=false`。PR CodeQL run `33259079897` 的 C++、C# 分析均成功，Code Scanning API 对该提交返回开放告警 0。

精确合并提交自己的 main CI run `33259456792` 全部通过：完整测试 `1,393/1,393`、0 skipped，coverage lines `90.12% (46,926/52,072)`、branches `76.04% (15,434/20,298)`；漏洞 0、许可证待审、SBOM `805/805` 与 unsigned RC 否定性门禁均保持。main CodeQL run `33259456772` 的 C++、C# 分析均成功。

PR 与精确 main 没有产生新的代码、质量、安全或供应链差异。远端门禁不能替代尚未取得的物理产品证据；本次回填不改变 M1/M2、PF 或唯一接续点。本文自己的文档提交只需独立通过 PR/main 检查，不再追加第三层文档收口。
