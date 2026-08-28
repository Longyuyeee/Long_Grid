# Stage 229：任务栏一次性环境准入 PowerShell Core 纠偏审计

日期：2026-08-28

审计输入基线：`origin/main@fb0b05da7691f34ef88c504032633c374996b218`

状态：`CorrectionComplete / QualityRegressionFixed / EnvironmentStillBlocked`

## 1. 接续依据与范围

Stage 228 明确规定：外部准入未变化时，只处理真实回归、质量或安全缺陷，不增加 M1/M2 邻接探针，也不转做外围功能。本轮重新执行既有任务栏一次性环境准入入口时，首次 Actual 不是预期的有限 `Blocked` JSON，而是 PowerShell Core 在脚本赋值阶段抛错。因此本轮只修复这个会阻断既定 TASKBAR-R2B1-B 准入的真实质量缺陷。

本轮不实现任务栏原生效果、不启用 Windows Sandbox、不修改宿主系统状态、不降低认证门禁，也不改变 M1/M2 的产品完成度。

## 2. Expected / Actual / Difference / Correction

| 项目 | 内容 |
|---|---|
| Expected | `Test-LongGridTaskbarDisposableEnvironment.ps1` 在受支持的 PowerShell 宿主下读取环境并有限返回 `ReadyToLaunch` 或 `Blocked`；任何情况下保持 `modifiedSystemState=false / mutationAllowed=false` |
| 首次 Actual | 在 PowerShell Core 运行时，脚本对 `$isWindows` 赋值触发 `Cannot overwrite variable IsWindows because it is read-only or constant.`，没有生成准入 JSON |
| Difference | PowerShell 变量名不区分大小写；局部 `$isWindows` 与 PowerShell Core 的只读自动变量 `$IsWindows` 冲突。原有真实进程测试固定调用 `powershell.exe`，Windows PowerShell 5.1 不提供该自动变量，因此没有覆盖差异 |
| Correction | 将局部变量改为不会碰撞自动变量的 `$runningOnWindows`；保留原判定和 JSON schema；新增 PowerShell Core 可用时的真实进程回归，并继续保留 Windows PowerShell 5.1 路径 |
| 修正后 Actual | PowerShell Core 正常返回 `Blocked` JSON；当前机器差异为 `HardwareEvidenceUnavailable`、`WindowsSandboxLauncherMissing`、`SandboxConfigurationMissing`，且 `modifiedSystemState=false / mutationAllowed=false` |

## 3. 实际代码审计

- 运行时修改仅是局部变量重命名，Windows OS 判定、架构/处理器/硬件证据、Sandbox 配置审计、`RequireReady` 退出码以及失败关闭规则均未改变。
- 回归测试通过真实 `pwsh.exe` 子进程执行正式脚本并解析输出，不以源码字符串检查代替执行证据。
- PowerShell Core 不存在于 PATH 时，新增用例有限返回；原有 `powershell.exe` 真实进程覆盖仍始终执行。
- 测试仍断言脚本不修改系统状态；没有启动 Sandbox，也没有允许任务栏 mutation。

## 4. 验证证据

- `git diff --check`：通过；
- `dotnet format LongGrid.sln --verify-no-changes --no-restore`：通过；
- `dotnet build LongGrid.sln --configuration Release --no-restore`：通过，0 warning / 0 error；
- 定向真实进程测试：3/3，通过，0 skipped；
- 全量测试：1,383/1,383，通过，0 skipped；
- 覆盖率：lines `90.41% (23539/26036)`，branches `76.17% (7730/10149)`，继续通过 90% / 75% 门禁；
- PowerShell Core 本机实际准入：`Blocked`，只读返回三项真实环境差异，没有系统 mutation。

## 5. 需求与阶段状态审计

需求对齐：本轮直接恢复“任务栏效果只能在通过准入的一次性 Guest 中执行”的安全入口，符合本地优先、零惊吓、独立故障域和失败关闭原则。没有插入自动整理、Tab、Widget、工作空间或新协议。

状态纠偏：准入脚本可执行不等于 Guest 已就绪，更不等于任务栏原生效果完成。TASKBAR-R2B1-B 继续为 `EnvironmentBlocked`；M1/M2 顶层旅程继续为 `0/2 Complete`；所有 unsigned 产物继续不可安装或分发。

## 6. 下一接续点

唯一执行顺序不变：

1. #23/#274 提供许可证、正式 Publisher、托管签名和审批输入后，在一次性 Windows 环境进入 BOX-R1-C/D 与 M1 完整物理旅程；
2. Windows Sandbox/专用 VM、硬件证据和隔离配置全部通过本脚本并达到 `ReadyToLaunch` 后，才进入 TASKBAR-R2B1-B 原生效果、15 秒确认/回退和恢复矩阵；
3. 外部门禁仍未变化时，只接受新的真实回归、质量或安全缺陷，不以继续增加测试或外围功能制造虚假进度。
