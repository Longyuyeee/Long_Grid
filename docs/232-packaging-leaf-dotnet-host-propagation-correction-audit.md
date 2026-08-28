# Stage 232：打包叶子门禁与子进程 SDK 传播纠偏审计

日期：2026-08-28

审计输入基线：`origin/main@7168a88bcb0d82c8492e381ad2de483691a791f4`

状态：`CorrectionComplete / FullPackagingChainRecovered / DistributionBlocked`

## 1. 接续依据与范围

Stage 231 统一了启动、portable 主命令和推荐 RC 的 SDK host 解析，但本轮按 [视觉品牌与交付要求](14-visual-branding-and-delivery-requirements.md) 重新执行默认质量链时发现其“打包恢复”结论过宽：默认 Pack 的漏洞扫描、公开 SBOM 和独立许可证扫描仍直接调用 PATH 中的 `dotnet`；Pack 启动的测试子进程也继续继承原坏 PATH。

#23 与 #274 仍为 OPEN；`long-grid-release` 仍只有受保护分支和一名必需审批人，没有 Publisher、托管签名输入或分发批准。本轮因此只纠正一键交付工程回归，不安装、签名、发布或修改系统状态。

## 2. Expected / Actual / Difference / Correction

| 项目 | 内容 |
|---|---|
| Expected | 默认 portable 一键打包必须执行 restore、format、Release build、全测、覆盖率和漏洞门禁；公开的 SBOM/许可证叶子入口应在同一 `global.json` SDK 规则下独立工作；子进程不得重新落回 PATH 中的不兼容 host |
| 首次 Actual | 直接运行 `Verify-VulnerablePackages.ps1` 时，x86 host 无 SDK 并以 `-2147450725` 失败。随后从干净提交执行不带 `SkipQualityGates` 的 Pack，1,389 项测试中任务栏恢复真实子进程因 `ProcessStartInfo("dotnet")` 继承坏 PATH，10 秒内没有 readiness 文件；其余 1,388 项通过 |
| Difference | 主脚本显式使用正确 host 只能保护当前命令；没有显式解析的叶子脚本和测试/工具子进程仍可能重新解析 PATH。Stage 231 的实际 Pack 使用了 `SkipQualityGates`，没有覆盖该默认链 |
| Correction | 漏洞、依赖许可证和 SBOM 叶子入口显式复用共享解析器；Pack 在自身进程范围内临时把已选 host 目录置于 PATH 首位，并在 `finally` 恢复，使测试与同步子脚本继承同一 SDK；不修改用户/系统 PATH |
| 修正后 Actual | 默认坏 PATH 下漏洞门禁直接通过；默认完整 Pack 1,389/1,389、漏洞扫描、self-contained publish 和确定性 ZIP 全部通过；独立 SBOM 入口完成 MSIX、工具恢复、SPDX 生成和 805/805 官方校验 |

## 3. 实际代码与安全边界审计

- `Verify-VulnerablePackages.ps1`、`Test-LongGridDependencyLicenses.ps1` 和 `New-LongGridSbom.ps1` 都显式解析并调用绝对 `$dotnetHostPath`，不依赖父包装器碰巧修正 PATH。
- SBOM 与许可证 `ValidateOnly` 增加即时 `dotnetHost` 证据；机器绝对路径不写入持久化 package/SBOM/license 清单。
- Pack 的 PATH 调整仅限当前 PowerShell 进程及其同步子进程，无论成功或异常都在 `finally` 恢复；不写注册表、用户环境或机器环境。
- 任务栏恢复测试的 10 秒 readiness、产品恢复语义和断言均未放宽；首次失败由实际错误 host 解释，修正传播后原测试直接通过。
- 污染 PATH 测试初版把 PATH 缩减为仅伪目录，导致 NuGet 漏洞查询在 20 秒和临时 60 秒尝试中都等待超时；纠正为“伪 host 置首、保留原系统 PATH”，准确复现原始抢占问题。统一测试上限恢复为 20 秒，6 项在 3 秒内通过。

## 4. 验证证据

- 默认坏 PATH 下 `Verify-VulnerablePackages.ps1`：Pass，实际 host `C:\Program Files\dotnet\dotnet.exe`；
- SBOM、依赖许可证与 portable `ValidateOnly`：均 Pass，实际 host 一致；
- 污染 PATH 真实子进程回归：6/6，通过，0 skipped；
- `dotnet format --verify-no-changes`：通过；
- Release solution build：0 warning / 0 error；
- 独立全量测试：1,389/1,389，通过，0 skipped；
- 独立覆盖率：lines `90.43% (47090/52072)`，branches `76.16% (15458/20298)`；
- 从干净提交 `00d60fe229c75b2a9eca71fe88eb55f3546b99e5` 执行默认完整 Pack：802 个 payload files，`deterministicArchive=true`，ZIP SHA-256 `18a1db8c34dbc8faf5d645f1a1b2f2d2399ae0a33d7374be4201a23e0f74de5b`；
- 同一提交直接执行独立 SBOM：unsigned MSIX SHA-256 `85117303aff3ba8227525506f73b2b25aff65f68538271945ec47c87246873ed`，SPDX SHA-256 `2f39b9e7399fe6b3e566efcd192a7c2027b0f3691a575092db8dd47f00dbe6c8`，805/805 文件验证成功；
- 所有产物继续为 `signed=false / installable=false / distributionApproved=false`。

PR #293 首轮远端 head 完整通过：CI run `33181486230` 用时 11m53s，1,389/1,389、0 skipped，coverage lines `90.12% (46926/52072)`、branches `76.04% (15434/20298)`；聚合 RC 选择 `C:\Program Files\dotnet\dotnet.exe`，805 文件 SBOM 和固定依赖许可证报告通过，仍为 `PendingOwnerReviewAndNotice / signed=false / installable=false / distributionApproved=false`。CodeQL run `33181486211` 的 C# / C++ 分别在 7m13s / 3m22s 成功。本地首次差异没有在远端重现，也没有通过重跑或降低门限取得该结果。

## 5. 需求、阶段与接续审计

开发目标审计：默认 portable 的完整质量链、独立漏洞/许可证/SBOM 入口和其子进程已统一到同一兼容 SDK，Stage 231 未覆盖的实际缺口关闭。

需求对齐审计：修复直接满足默认一键打包必须执行安全门禁、底层命令可诊断和干净提交可复现要求；没有降低测试、性能、漏洞、许可证、SBOM、签名或安装门禁。M1/M2 仍为 `0/2 Complete`，所有产物仍不可公开分发。

唯一接续点不变：负责人在 #23/#274 提供许可证、正式 Publisher、托管签名方案和签名包，并取得安全的可丢弃 Windows 会话后执行 BOX-R1-C/D 与 M1 物理旅程；Sandbox/专用 VM、硬件和隔离配置达到 `ReadyToLaunch` 后才进入 TASKBAR-R2B1-B。外部事实未变化时只处理新的真实回归、质量或安全缺陷。
