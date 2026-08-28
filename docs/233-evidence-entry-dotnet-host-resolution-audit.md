# Stage 233：工程证据入口 SDK 解析收敛审计

日期：2026-08-28

审计输入基线：`origin/main@ec756995c7851d2e1bde300f346e05953dae39e5`

状态：`CorrectionComplete / LocalAuditPassed / RemoteAuditPending / ProductEvidenceBlocked`

## 1. 接续依据与范围

Stage 232 已让启动、打包、漏洞、许可证、SBOM 和打包子进程统一使用 `global.json` 对应的兼容 SDK，但重新扫描全部 `eng/*.ps1` 后，仍发现 15 个公开人工/产品证据入口直接调用 PATH 中的 `dotnet`，另有 2 个会话入口只用 `Get-Command dotnet` 判断“存在”，不能判断该 host 是否包含兼容 SDK。

本轮只关闭这个可复现的换机接续缺口。M1 的受保护签名、可丢弃 Windows 会话和安全 WinUI/UIA 运行时均未出现新事实；没有启动产品物理旅程、发送输入、安装 unsigned MSIX、修改任务栏或系统配置。

## 2. Expected / Actual / Difference / Correction

| 项目 | 内容 |
|---|---|
| Expected | 任一公开工程入口只要需要 restore/build/run，就应按仓库 `global.json` 选择兼容 SDK；PATH 中较早的 x86/App Alias host 不得使入口失败；`-NoBuild`、`ValidateOnly`、`ContractOnly` 和清理模式不得被无故改成必须安装 SDK |
| 首次 Actual | 默认 PATH 首项为 `C:\Program Files (x86)\dotnet\dotnet.exe`，该 host 没有 SDK；直接执行只读 `Test-LongGridTaskbarCompatibilityProbe.ps1` 时，`dotnet build` 以 `-2147450725` 失败，未进入兼容性探针 |
| Difference | Stage 232 的共享解析器没有覆盖人工矩阵、M1/UI、BOX/PF-002、桌面启动和任务栏证据入口；单纯 `Get-Command dotnet` 还会把“不兼容 host 存在”误判为可运行 |
| Correction | 15 个直接执行 SDK 命令的入口复用 `LongGrid.DotNetHost.ps1` 并调用绝对 host；解析延迟到实际 restore/build/run 分支。批量无障碍与资源稳定入口删除错误的 PATH 存在性检查，由其调用的 `Start-LongGrid.ps1` 负责真实解析 |
| 修正后 Actual | 同一默认坏 PATH 下只读任务栏入口完成 Release build 与两次真实兼容性探针，`Difference=None / outcome=Pass`；PATH 中完全没有 `dotnet` 时，`-NoBuild` 仍能使用预构建 Worker 通过，证明未扩大该模式前置条件 |

## 3. 代码与安全审计

- 覆盖 17 个入口：Issue #19/#20/#24、两类桌面交互、批量无障碍、资源稳定、M1、UI-R1E、BOX-R1、PF-002、桌面首次启动、运行时开启、任务栏只读与原生认证以及完整 UI 门禁。
- SDK 解析只读取程序目录、PATH 与 `global.json`，不写用户或机器环境变量；调用使用绝对路径。
- `NoBuild`、合同验证和清理分支在不需要 SDK 时不解析 SDK；已有预构建产物仍可在 PATH 无 `dotnet` 的环境运行。
- 新增全目录静态测试，除共享解析器自身外，任何 `eng/*.ps1` 再出现直接 PATH `dotnet` 调用或 `Get-Command dotnet` 都会失败。
- `Test-LongGridUi.ps1` 的 UTF-8 BOM 保持 `EF BB BF`；Windows PowerShell 5.1 合同不变。
- 本轮只运行只读任务栏兼容性探针和无副作用的合同模式；没有执行需人工确认、系统状态变更或产品窗口交互的 live 模式。

## 4. 本地验证证据

- 修正前默认坏 PATH 复现：x86 host 无 SDK，任务栏只读入口 build 失败，exit `-2147450725`；
- 修正后同一入口：Release build `0 warning / 0 error`，两次只读探针均 Pass，任务栏窗口身份不变，`modifiedSystemState=false`，运行时继续 `DeniedNoCertifiedBuild`；
- PATH 完全无 `dotnet` + `-NoBuild`：同一只读入口 Pass；
- 11 个 `ValidateOnly/ContractOnly` 入口：11/11 Pass；
- SDK 解析专项：7/7 Pass；
- PowerShell 全脚本 AST parse：Pass；`Test-LongGridUi.ps1` BOM：Pass；
- `dotnet format --verify-no-changes`：Pass；
- Release solution build：0 warning / 0 error；
- 完整测试：1,390/1,390，0 skipped；
- 覆盖率：lines `90.43% (23,545/26,036)`，branches `76.15% (7,729/10,149)`。

远端 PR/main CI、CodeQL 和最终主线提交在本文首次提交时仍为 Pending；不得提前写成远端 Pass。

## 5. 需求、阶段与接续审计

开发目标审计：所有当前 PowerShell 工程证据入口的 SDK 选择已从“依赖 PATH 排序”收敛为“需要命令时按 `global.json` 解析绝对兼容 host”，并用全目录合同防止回退。Stage 232 的打包结论不再只对包装链成立。

需求对齐审计：本轮提高换机接续、人工证据和只读探针的可执行性，没有改变任何产品功能状态或证据口径。M1/M2 仍为 `0/2 Complete`；#19/#20/#24/#23/#274 继续 OPEN；所有产物仍为 unsigned、uninstallable、distribution unapproved。

唯一接续点不变：负责人在 #23/#274 提供许可证、正式 Publisher、托管签名方案和签名包，并取得安全、独占、可丢弃 Windows 会话后执行 BOX-R1-C/D 与 M1 物理旅程；TASKBAR-R2B1-B 仍等待 Sandbox/专用 VM、硬件和隔离配置达到 `ReadyToLaunch`。外部事实未变化时只处理新的真实回归、质量或安全缺陷。
