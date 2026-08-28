# Stage 234：产品证据托管启动就绪与 PowerShell 5.1 清理纠偏审计

日期：2026-08-29

审计输入基线：`origin/main@9b1c5546899621da367ea34d645fda6d46c70729`

状态：`CorrectionComplete / LocalPrAndMainAuditPassed / ProductEvidenceBlocked`

## 1. 接续依据与原始需求对齐

Stage 233 后唯一允许继续的工程工作，是处理新出现的真实回归、质量或安全缺陷；BOX-R1-C/D 与 M1 完整物理旅程仍必须等待受保护签名包、独占可丢弃 Windows 会话和安全 WinUI/UIA 运行时。2026-08-29 复读当前机器时，先前的同名 `LongGrid.App` 进程已不存在，故按开发流程尝试执行 BOX-R1 `Initial` 真实 Release 场景。

本轮没有新增产品功能，也没有把源码合同或宿主错误窗口写成产品证据。纠偏目标严格是：证据入口必须区分“进程存在”与“产品托管入口已经就绪”，并在失败时只清理自己创建的进程和带精确标记的临时会话。

## 2. 首次 Actual、Difference 与根因

| 项目 | Expected | 首次 Actual | Difference / 根因 |
|---|---|---|---|
| BOX-R1 最终清理 | Windows PowerShell 5.1 与 PowerShell 7 都能结束入口自己启动的进程 | `finally` 调用 `.Kill($true)`，Windows PowerShell 5.1 的 .NET Framework `Process` 没有该重载，抛出 overload 错误并留下入口自有 PID | 工程脚本误用了 PowerShell 7/.NET Core 才具备的 kill-tree 重载 |
| BOX-R1 真实结果等待 | 应用托管入口启动后生成 `ready.json/result.json`；宿主启动失败应立即明确失败 | 修正清理后仍等待 20 秒并超时，没有 `result.json` | 等待逻辑只观察文件，不观察自有进程退出或宿主错误窗口 |
| M1 人工入口状态 | 只有应用写出 `AppConstructed` 阶段后才能返回 `ReadyForPhysicalInput` | 入口启动 750 ms 后只要 PID 存在就返回 Ready；实际窗口标题为 `LongGrid.App.exe - This application could not be started`，四个托管阶段全部缺失 | 把 .NET/Windows App Runtime 宿主错误框误判成产品窗口 |
| 当前机器运行时 | 框架依赖的 Windows App SDK 2.3.1 应有完整、兼容的 x64 Runtime 组件 | Win32 子窗口文本明确为 `Required components of the Windows App Runtime are missing; Version 2.x; MSIX package version >= 2.3.1.0`。机器虽有 `Microsoft.WindowsAppRuntime.2` Framework 2.3.1/2.4.0，但 Main 仅为 1.4，且没有对应 2.x DDLM；Bootstrap 在用户 `Program.Main` 前停止 | “Framework 包可枚举”不等于完整 Runtime 已安装；这是当前电脑外部环境缺口，不是产品业务代码回归 |

进程模块复读证明实际加载的是仓库 Release `LongGrid.App.exe/.dll`、.NET 8.0.29 CoreCLR 与 Windows App Runtime Bootstrap 2.0；没有加载 WinUI，也没有子进程。错误文本通过 Win32 `GetWindowText` 只读取得，没有调用当前已知危险的跨进程 UIA。

## 3. Correction

- `Test-LongGridBoxR1Activation.ps1` 将 `.Kill($true)` 收敛为 Windows PowerShell 5.1 与 PowerShell 7 均支持的 `.Kill()`；该证据入口没有子进程树需要扩大终止范围。
- 全目录静态回归测试禁止任何 `eng/*.ps1` 再引入 `.Kill($true)`。
- BOX-R1 的 `Wait-ForPath` 同时观察入口自有主进程：进程提前退出时报告退出码，发现 `This application could not be started` 宿主窗口时立即失败，不再用固定超时掩盖启动错误。
- M1 启动器最多等待 10 秒，并要求自己的 `launch.log` 已出现 `AppConstructed` 才返回 `ReadyForPhysicalInput`；提前退出、宿主错误窗口或阶段超时均终止入口自有进程，并复用 GUID、专用临时根、非 reparse point 与精确 marker 校验清理会话。
- M1 成功输出新增 `managedLaunchReady=true` 与实际窗口标题，便于人工执行前复读；`ValidateOnly` 和 C# 合同测试同时锁定该门禁。

## 4. 安全与副作用审计

- 没有安装、修复或卸载 Windows App Runtime，没有点击宿主错误框的“是/否”，没有修改系统安全策略、Appx、注册表、任务栏或 Explorer。
- 没有调用外部 UIA、Computer Use、SendInput 或物理输入；已知 `WindowsAppRuntime 2.4.0.0 / Microsoft.UI.Xaml 3.2.3.0` 跨进程 UIA 风险结论保持不变。
- 所有被终止的 PID 都由本轮唯一证据会话直接启动，并复读为仓库 Release 路径；没有结束外来同名进程。
- 修正后的 M1 真实负向验证在约 1.2 秒内失败，BOX-R1 在约 0.65 秒内失败；两者新增会话目录均为 0，最终 `LongGrid.App` 进程数均为 0。
- 首次 BOX-R1 清理异常留下的专用空目录已复读为 0 子项；本机安全策略拒绝删除该空目录，未绕过策略。它不包含证据或用户数据，也不影响后续会话使用唯一 GUID。

## 5. 本地验证证据

- 两份修改脚本 PowerShell AST parse：Pass；
- M1 `-ValidateOnly`：Pass；BOX-R1 `-ContractOnly`：Pass；
- SDK/脚本专项：9/9；
- 真实缺失 Runtime 组件负向矩阵：M1 与 BOX-R1 均快速明确失败、零新增会话、零剩余进程；
- `dotnet format --verify-no-changes`：Pass；
- Release solution build：`0 warning / 0 error`；
- 完整测试：`1,392/1,392`，0 skipped；
- coverage：lines `90.43% (47,088/52,072)`，branches `76.16% (15,458/20,298)`。

PR #296 head `8268bb85e084ccc33be916f60b6ab95c5235c988` 的 CI run `33190946429` 用时 8m36s，测试 `1,392/1,392`、0 skipped，coverage lines `90.11% (46,920/52,072)`、branches `76.03% (15,432/20,298)`；漏洞为 0，许可证门禁为 20 projects / 30 packages 且保持 `PendingOwnerReviewAndNotice / distributionApproved=false`，SBOM `805/805` 验证成功，内部 RC 继续 `signed=false / installable=false / distributionApproved=false`。同一 PR merge ref `2d0663c9819d1c5a1b22d3318cba14f30032d111` 的 CodeQL run `33190946594` 中，C++ 3m22s、58 rules / 0 results，C# 6m40s、52 rules / 0 results。

PR #296 合并提交为 `main@228540ab6dfcd04b97482f0098090d11d1b97a46`。该提交自己的 CI run `33191697714` 用时 9m07s，测试 `1,392/1,392`、0 skipped，coverage lines `90.11% (46,924/52,072)`、branches `76.04% (15,434/20,298)`；漏洞、许可证 20/30、SBOM `805/805` 和 unsigned RC 否定性合同再次通过。main CodeQL run `33191697875` 的 C++ 与 C# 均成功；Code Scanning API 对该精确 main commit 返回 C++ 58 rules / 0 results、C# 52 rules / 0 results。实现提交已完成独立 PR 与 main 远端审计。

本收口段是上述已完成运行的事实写回；其文档提交仍须通过独立 PR 与合并后 main 检查，不能反向替代实现提交的审计。

最终文档合并提交 `main@451ca22046cc6ed6e1a4d7d595931191fb0ce9e5` 的首次 CI run `33193173935` 又保留了一项新的真实差异：完整套件为 `1,391/1,392`，`NativeActivationSourceExposesFiniteInvokeAndHideRestoreContract` 在 39 秒后未观察到原生 `#32768` 菜单。复审实际代码确认产品证据菜单仍按设计同步显示 1.2 秒，失败来自测试用 `Task.Run` 启动观察者后未等待它进入 UIA 查询；繁忙 runner 可在菜单关闭后才调度观察任务。修正不延长产品/证据菜单、不放宽 UIA 断言，而是使用专用 `LongRunning` 观察任务，在它即将进入首次查询时显式握手，再由拥有线程打开菜单。首次尝试等待“完成一次空查询”时又真实发现 `FindFirst` 自身可阻塞直至菜单出现，故纠正为查询前握手并保留该差异。首次 main 失败不以重跑覆盖，修正后的 PR/main 结果须继续独立记录。

本地修正后该精确原生菜单/UIA 场景连续 `10/10` 通过；格式无差异，Release 测试项目构建 `0 warning / 0 error`，完整套件两次均为 `1,392/1,392`，coverage lines `90.43% (47,090/52,072)`、branches `76.16% (15,458/20,298)`。这些本地结果只证明握手修正，不覆盖 run `33193173935` 的首次失败；远端仍须在新提交上完整复测。

## 6. 阶段结论与唯一接续点

开发目标审计：PowerShell 5.1 清理回归、M1 假 Ready 和 BOX-R1 超时掩盖三个真实工程缺陷已关闭；产品托管代码没有因本轮被改动。需求对齐审计：修正加强了“真实证据优先、失败关闭、只清理自有资源、不把进程存在冒充产品就绪”的原始要求，没有扩大权限或伪造产品完成状态。

M1/M2 继续为 `0/2 Complete`，BOX-R1-C/D 与 M1 继续 `ExternalEnvironmentBlocked / ProductEvidencePending`。当前机器还缺完整兼容的 Windows App Runtime 2.3.1+ 组件，同时现有 2.4.0.0/XAML 3.2.3.0 组合仍不允许跨进程 UIA；#23/#274 的许可证、Publisher、托管签名与签名安装生命周期输入也未到位。

唯一接续点：负责人在 #23/#274 提供受审外部输入和签名包，并在具备完整兼容 Runtime、安全 WinUI/UIA、无既有 Long方格进程的独占可丢弃 Windows 会话中，串行执行 BOX-R1 `Initial / Redirect / DuplicateRedirect` 与 M1 完整物理旅程。外部事实未变化时只处理新的真实回归、质量或安全缺陷。
