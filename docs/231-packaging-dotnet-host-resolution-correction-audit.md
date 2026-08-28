# Stage 231：一键打包 .NET SDK 解析纠偏审计

日期：2026-08-28

审计输入基线：`origin/main@c53379fb0d055d1f4b9f1335b845016de3d55c5e`

状态：`CorrectionComplete / PackagingRecovered / DistributionBlocked`

## 1. 接续依据与范围

Stage 230 恢复根启动链后，本轮先复读实际打包代码，并按 [视觉品牌与交付要求](14-visual-branding-and-delivery-requirements.md) 的原始一键打包要求直接执行 portable 打包。启动入口已经能绕过 PATH 中无兼容 SDK 的 x86 `dotnet.exe`，但 `Pack-LongGrid.ps1` 和推荐的 `Build-LongGridReleaseCandidate.ps1` 仍依赖 PATH，形成同机“可以一键启动、不能一键打包”的真实偏移。

本轮只统一 SDK host 解析并锁定回归，不安装应用、不启动产品窗口、不修改系统 PATH、不签名或分发产物，也不改变 M1/M2 的产品完成状态。

## 2. Expected / Actual / Difference / Correction

| 项目 | 内容 |
|---|---|
| Expected | 从干净提交调用推荐 RC 入口或 portable 打包入口时，应找到满足仓库 `global.json` 的 SDK；PATH 中无效 host 不应遮蔽同机有效 SDK |
| 首次 Actual | `pwsh -File eng/Pack-LongGrid.ps1 -Version 0.1.0-stage231 -SkipQualityGates -NoRestore` 解析到 `C:\Program Files (x86)\dotnet\dotnet.exe`，该 host 没有兼容 SDK，self-contained publish 以 `-2147450725` 失败；`C:\Program Files\dotnet\dotnet.exe` 实际可解析 `8.0.423` |
| Difference | Stage 230 解析逻辑只存在于启动脚本；portable 和聚合 RC 路径继续隐式调用 PATH 中的 `dotnet`，不满足同一仓库的一键交付可复现要求 |
| Correction | 抽取共享 `Resolve-LongGridDotNetHost`；启动和 portable 入口显式使用解析出的绝对 host；聚合 RC 在自身进程范围内临时把选定 host 目录置于 PATH 首位，使 MSIX、SBOM、license 等既有子链一致使用兼容 SDK，并在 `finally` 恢复原 PATH |
| 修正后 Actual | 不调整调用者 PATH 的 portable 实际打包成功；启动、portable 和聚合 RC 的 `ValidateOnly` 均选择 `C:\Program Files\dotnet\dotnet.exe`，污染 PATH 真实子进程回归 3/3 通过 |

## 3. 实际代码与安全边界审计

- `eng/LongGrid.DotNetHost.ps1` 是唯一 host 解析实现：检查有限候选，并在仓库根执行 `--version`，只接受能满足 `global.json` 的 host；不安装、不下载、不改机器配置。
- `Start-LongGrid.ps1` 与 `Pack-LongGrid.ps1` 显式调用同一解析器，避免后续两套规则再次漂移。
- 聚合 RC 只修改当前 PowerShell 进程的 PATH，作用域覆盖其同步子脚本；无论成功或失败都恢复原值，不写用户或系统环境变量。
- `dotnetHost` 只进入 `ValidateOnly` 和控制台即时证据，不写 portable 或 RC 的持久清单，避免把机器绝对路径写进可复现产物。
- CI restore 合同同步接受显式 `$dotnetHostPath`，没有放宽 locked restore、格式、测试、覆盖率、漏洞、许可证、SBOM、签名或安装门禁。

## 4. 验证证据

- `dotnet format --verify-no-changes`：通过；
- 污染 PATH 的启动、portable、聚合 RC 真实子进程回归：3/3，通过，0 skipped；
- 三个正式入口 `ValidateOnly`：全部通过并选择 x64 兼容 host；
- CI/release restore 合同：通过；
- Release solution build：0 warning / 0 error；
- 全量测试：1,386/1,386，通过，0 skipped；
- 覆盖率：lines `90.43% (23545/26036)`，branches `76.16% (7729/10149)`，通过 90% / 75% 门禁；
- 从已提交代码 `50a2fc8fa018e9ed0382bca2fe2516b41e919619` 实际生成 portable ZIP：802 个 payload files，`deterministicArchive=true`，SHA-256 `9a5dace0d27bef296ed1a0712860b2c1b94e0df18099ee29ad7be09bd5fc4576`；
- 该测试产物仍为 `signed=false / installer=false / distributionApproved=false`，不构成安装或发布证据；本地安全策略拒绝删除两个未跟踪测试产物，它们不会进入 Git 提交或推送，可由同一命令重新生成。

## 5. 需求与阶段状态审计

开发目标审计：启动与一键打包现在共享同一兼容 SDK 判定，实际 portable 打包已恢复，目标完成。

需求对齐审计：修复直接对应原始一键打包和干净提交可复现要求；没有绕过质量门禁，也没有把 unsigned portable/RC 冒充可安装或可公开分发版本。BOX/FOLDER/PF-007 继续为 `EngineeringComplete / ProductEvidencePending`，TASKBAR-R2B1-B 继续为 `EnvironmentBlocked`，M1/M2 继续为 `0/2 Complete`。

## 6. 下一接续点

唯一执行顺序不变：

1. #23/#274 提供许可证、正式 Publisher、托管签名和签名包后，在可丢弃 Windows 环境执行 BOX-R1-C/D 与 M1 完整物理旅程；
2. Windows Sandbox/专用 VM、硬件和隔离配置达到 `ReadyToLaunch` 后，才进入 TASKBAR-R2B1-B；
3. 外部门禁没有变化时，只处理新的真实回归、质量或安全缺陷，不通过增加邻接代码制造虚假进度。
