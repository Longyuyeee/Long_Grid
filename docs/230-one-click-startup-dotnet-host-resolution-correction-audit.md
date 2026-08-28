# Stage 230：一键启动 .NET SDK 解析纠偏审计

日期：2026-08-28

审计输入基线：`origin/main@7f1d65979ddf29182db30865d43ab85097e7b85c`

状态：`CorrectionComplete / OneClickStartupRecovered / ProductJourneysPending`

## 1. 接续依据与范围

Stage 229 后外部产品门禁没有变化，因此本轮先在 PowerShell Core 下执行 26 个既有 `ValidateOnly` 正式入口。25 个正常通过，根启动链 `Start-LongGrid.ps1 -ValidateOnly` 唯一失败。统一开发计划将“根目录一键启动、Debug/Release 构建继续通过”列为 M1 必须持续交付项，所以本轮只修复该真实回归，不增加外围功能。

本轮不安装应用、不启动真实产品窗口、不修改桌面或任务栏、不签名或分发产物，也不改变 M1/M2 的产品完成状态。

## 2. Expected / Actual / Difference / Correction

| 项目 | 内容 |
|---|---|
| Expected | 根目录 `启动Long方格.cmd` 转发到 `Start-LongGrid.ps1` 后，应找到满足 `global.json` 的 .NET SDK，完成锁定恢复、指定配置构建和启动/验证；PATH 中无效 host 不应遮蔽有效 SDK |
| 首次 Actual | 当前机器 PATH 优先解析 `C:\Program Files (x86)\dotnet\dotnet.exe`；该 host 存在但没有 8.0.400 feature band SDK。脚本只用 `Get-Command dotnet` 判断“命令存在”，随后 `dotnet restore` 以 `-2147450725` 失败；同机 `C:\Program Files\dotnet\dotnet.exe --version` 实际为 `8.0.423` |
| Difference | 启动链验证的是可执行文件存在，而不是该 host 能否在仓库 `global.json` 下解析兼容 SDK；x86/App Alias 或损坏 host 可以抢占 PATH，造成已安装正确 SDK 仍无法一键启动 |
| Correction | 新增有限 host 解析：优先检查 64 位 Program Files，再检查普通 Program Files 和 PATH 候选；每个候选都在仓库根目录执行 `--version`，只接受退出码 0 且返回版本的 host；restore/build/run 全部使用选定绝对路径 |
| 修正后 Actual | 不修改 PATH 的 PowerShell Core 正式入口成功选择 `C:\Program Files\dotnet\dotnet.exe`，完成 locked restore 和 Release build，0 warning / 0 error；Windows PowerShell 与 PowerShell Core 的 no-build 验证也均通过 |

## 3. 实际代码与测试审计

- `Resolve-LongGridDotNetHost` 只读取环境变量、候选文件和 SDK 版本，不安装 SDK、不修改 PATH、不下载工具。
- 解析在仓库根目录执行，因此 `--version` 必须真实满足仓库 `global.json`，不能只证明机器上存在任意 SDK。
- 失败时列出已经检查的 host，并明确报告没有与 `global.json` 兼容的 SDK，替代此前下游 restore 的模糊错误。
- 新增真实子进程测试，将子进程 PATH 限制到退出码 37 的伪 `dotnet.cmd`，验证启动链仍选择 Program Files 中的兼容 host；测试只运行 `ValidateOnly / NoRestore / NoBuild`，不启动窗口、不重复嵌套构建。
- 初版回归尝试曾在子进程内启动完整 WinUI build，因超出有界测试时间被立即终止并改为上述快速解析证据；终止后确认没有遗留 dotnet、MSBuild、testhost 或 PowerShell 子进程。

## 4. 验证证据

- PowerShell Core 实际修正前：exit 1，`dotnet restore failed with exit code -2147450725`；
- PowerShell Core 实际修正后：locked restore + Release build + startup validation 通过，0 warning / 0 error；
- Windows PowerShell / PowerShell Core no-build 启动链：均 exit 0；
- PowerShell Core 全部既有 `ValidateOnly` 入口复审：26/26 通过；
- 污染 PATH 定向真实进程回归：1/1，通过，约 419 ms；
- `git diff --check` 与 `dotnet format --verify-no-changes`：通过；
- Release solution build：0 warning / 0 error；
- 全量测试：1,384/1,384，通过，0 skipped；
- 覆盖率：lines `90.43% (23545/26036)`，branches `76.16% (7729/10149)`，通过 90% / 75% 门禁。

## 5. 需求与阶段状态审计

需求对齐：修复直接服务于 M1 明列的根目录一键启动和 Debug/Release 持续可用要求；没有以工程探针替代产品旅程，也没有扩展自动整理、Tab、Widget、工作空间或任务栏系统写入。

状态纠偏：一键启动工程链恢复不等于签名安装、Explorer 菜单或完整物理旅程完成。BOX/FOLDER/PF-007 继续为 `EngineeringComplete / ProductEvidencePending`，TASKBAR-R2B1-B 继续为 `EnvironmentBlocked`，M1/M2 继续为 `0/2 Complete`，所有 unsigned 产物继续不可公开分发。

## 6. 下一接续点

唯一执行顺序不变：

1. #23/#274 提供许可证、正式 Publisher、托管签名、签名 MSIX 和可丢弃 Windows 环境后，进入 BOX-R1-C/D 与 M1 完整物理旅程；
2. Windows Sandbox/专用 VM、硬件和隔离配置达到 `ReadyToLaunch` 后，才进入 TASKBAR-R2B1-B；
3. 外部门禁没有变化时，只处理新的真实回归、质量或安全缺陷，不通过增加邻接代码制造虚假进度。
