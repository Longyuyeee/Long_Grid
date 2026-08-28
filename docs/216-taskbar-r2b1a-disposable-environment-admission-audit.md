# Stage 216：TASKBAR-R2B1-A 可丢弃环境准入审计

日期：2026-08-27

开发基线：`origin/main@4a733cf96b242aac1f8875ffedf14bf23c2adc33`

状态：`EngineeringComplete / RealHostBlockedAsExpected / GitHubPrPass / IntegrationPending / NativeEffectPending`

## 1. 阶段目标与结论

R2B1 原计划开始首个 `Clear → SystemDefault` 原生效果实验。开始前的真实环境审计发现当前宿主没有 Windows Sandbox 启动器、Hyper-V 管理工具、VMware 或 VirtualBox，可选功能在线查询又因当前进程未提升而拒绝。直接在日常任务栏写入会违反“可丢弃、可回滚、真实测试”的既定门禁，因此本阶段收敛为 R2B1-A 环境准入，不执行任何任务栏写调用。

本阶段交付：

- `Test-LongGridTaskbarDisposableEnvironment.ps1`：复读真实 OS、架构、逻辑处理器、物理内存、虚拟化固件、SLAT 与 Windows Sandbox 启动器；
- 同一脚本生成并安全复读 `.wsb`：Networking 关闭，仓库映射只读，只有独立证据目录可写，剪贴板、打印、音频和视频重定向关闭，LogonCommand 固定为仓库 Guest 门禁；
- `Test-LongGridTaskbarDisposableGuest.ps1`：只证明默认 `WDAGUtilityAccount` 身份和 `C:\LongGridSource`、`C:\LongGridEvidence` 两条映射，不修改任务栏；
- `-RequireReady` 在任一差异存在时退出 2；普通审计始终输出有限 Expected、Actual、Difference，方便不同机器复核；
- 全量测试中的真实 PowerShell 子进程会生成临时 `.wsb`、复读全部保护项，再按本机实际 Ready/Blocked 结果验证严格退出码；测试结束仅删除自己的临时目录。

微软将 Windows Sandbox 定义为基于虚拟机监控程序的可丢弃隔离桌面，关闭后删除其中软件、文件和状态；官方安装前提包括 BIOS 虚拟化、至少 4 GB 内存、1 GB 磁盘和两个 CPU 核心。`.wsb` 官方合同支持禁用网络/剪贴板、只读映射和 LogonCommand，因此采用该边界而不是普通本地账户。参考：[安装要求](https://learn.microsoft.com/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-install)、[Sandbox 生命周期](https://learn.microsoft.com/windows/security/application-security/application-isolation/windows-sandbox/)、[WSB 配置](https://learn.microsoft.com/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file)。

竞品实现审计再次确认 TranslucentTB 的经典任务栏属性路径会对任务栏 HWND 调用 `SetWindowCompositionAttribute`；它不是 Long方格当前已接入的能力，也不能在没有逐 build 实机恢复证据时进入正式目录。参考：[TranslucentTB TaskbarAttributeWorker](https://github.com/TranslucentTB/TranslucentTB/blob/release/TranslucentTB/taskbar/taskbarattributeworker.cpp)。

## 2. 真实测试：预期、实际与差异

| 检查 | 预期效果 | 当前真实效果 | 差异与修正 |
|---|---|---|---|
| Windows/架构 | Windows 10/11，X64 或 Arm64 | Windows `10.0.26200.0`，X64 | 无 |
| CPU/内存 | 至少 2 个逻辑处理器、4 GiB 内存 | 16 个逻辑处理器；42,268,614,656 bytes（约 39.4 GiB） | 无 |
| 虚拟化固件 | 已启用 | `VirtualizationFirmwareEnabled=true` | 无 |
| SLAT | 必须由当前宿主证明 | `SecondLevelAddressTranslation=false` | `SecondLevelAddressTranslationNotAttested`；失败关闭 |
| Sandbox 启动器 | 正式 `WindowsSandbox.exe` 存在 | System32 不存在；命令解析也无结果 | `WindowsSandboxLauncherMissing`；失败关闭 |
| 其他可丢弃 VM | 至少存在一个已配置替代品 | Hyper-V 管理工具、VMware、VirtualBox 均不存在 | 不降级到宿主 |
| `.wsb` 网络/映射 | 断网、源码只读、仅证据目录可写 | 生成后 XML 安全解析全部为 true | 无 |
| 重定向与命令 | 关闭剪贴板/打印/音视频，Guest 命令固定 | 六项复读全部符合 | 无 |
| 系统变化 | 环境准入不得改变任务栏或开放 mutation | `ModifiedSystemState=false`、`mutationAllowed=false` | 无 |
| 严格准入 | 有任一差异时明确阻断 | `outcome=Blocked`；`-RequireReady` 退出 2 | 无 |

定向真实进程测试 1/1、Release 全解决方案 build 0 warning/error、格式和 `git diff --check` 已通过。按 CI 参数执行全量测试为 **1346/1346**；coverage lines **90.38% (46718/51690)**、branches **76.01% (15286/20110)**，继续通过 90%/75% 门槛。191-ID UI 合同与 RC restore 合同通过。

PR #263 首轮远端 run `33050907194` 完整通过：1346/1346，coverage lines **90.09% (46566/51690)**、branches **75.88% (15260/20110)**；格式、构建、启动链、真实环境子进程、UI 合同、配置/产品恢复、资源稳定、文件安全、受限缩略图 Worker、依赖漏洞和内部未签名 RC 清单全部成功。runner 的 Actual 可以与本机硬件不同，但同样没有开放 mutation。

## 3. 需求对齐与偏移审计

本阶段没有扩展小组件、插件、自动整理、Tab、工作空间、教程 UI 或通用窗口特效，直接服务第三根 Core 的“任务栏美化必须可恢复”。虽然没有产生用户可见清透效果，但这不是以底座替代产品进度：原计划的真实效果测试因缺少隔离环境被明确阻断，文档同步把 R2B1-B 标为 `EnvironmentBlocked`，没有宣称完成。

三项核心状态保持：

- 桌面盒子和文件夹绑定工程链不变，物理产品旅程仍 Pending；
- 任务栏只读、恢复日志、唯一租约、启动预检和默认空原生目录继续通过；
- 当前产品任务栏预设仍不可用；
- 当前日常宿主不会成为“临时测试环境”。

## 4. 下一开发项与阻断解除条件

下一项仍是 `TASKBAR-R2B1-B`，但只有以下事实全部成立才能开始：

1. 管理员在宿主启用 Windows Sandbox 并完成所需重启，或提供另一台可快照回滚的 Windows VM；
2. Host 门禁实际返回 `ReadyToLaunch`；
3. 启动生成的 `.wsb` 后，Guest 门禁写出 `guest-admission.json / GuestReady`；
4. 明确允许在该 Guest 内执行任务栏原生写入；
5. 应用前截图、像素采样、任务栏 HWND/class/PID 和系统个性化状态已采集；
6. Guest 关闭后确认宿主任务栏状态完全未变。

官方启用命令需要提升 PowerShell：`Enable-WindowsOptionalFeature -FeatureName Containers-DisposableClientVM -All -Online`，并可能要求重启。本阶段不自行提权、不启用功能、不安排重启。阻断解除前不得编写一个只能靠 mock 宣称有效的原生适配器，也不得在宿主桌面执行 `SetWindowCompositionAttribute`。

## 5. Stage 225 真实预检期限增量（2026-08-28）

后续 hosted runner 曾出现预检在 10 秒总期限后仍未产出有限结果。Expected 为两次 CIM 查询均有独立上限、进程总期限仍为 10 秒、任何超时都终止测试自有进程树并排空 stdout/stderr；旧 Actual 是 CIM 无独立期限，测试只取消 stdout 读取且不清理子进程。Stage 225 为两次 `Get-CimInstance` 分别增加 2 秒 operation timeout，并以真实 60 秒 PowerShell 子进程证明 500ms 超时后 PID 已不存在。当前宿主真实预检和连续压力测试通过；详情见 [Stage 225 审计](225-real-process-handshake-and-timeout-audit.md)。这只提高准入审计的有限性，不改变本机 `Blocked` 结论，也不开放任务栏 mutation。
