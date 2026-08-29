# Stage 240：Live UI Runtime 预检宿主与准入完整性审计

日期：2026-08-30

基线：`origin/main@4ef00d4a2a31c35465a138b4156bd6a9887c6661`

状态：`CorrectionComplete / PrAndMainAuditPassed / ProductEvidenceBlocked`

## 1. 接续复读与路线约束

本轮从 Stage 239 最终 `main` 复核 Runtime 预检的两个真实消费者、入口进程边界、合同校验与外部接续条件。#23/#274、完整兼容 Runtime、签名包和独占可丢弃 Windows 会话均未出现新的准入事实，因此没有继续增加 Runtime 探针或扩张产品功能，只处理复读中证实会绕过既有失败关闭路线的安全缺陷。

M1 入口已直接执行精确的仓库预检脚本，并且只有 `outcome=Pass` 才启动。Live UI 则仍调用 PATH 解析的裸 `powershell`，只显式处理三个阻断结果；PATH 前置的同名命令可以伪造 `Pass`，未知或缺失 Outcome 也会继续进入应用和跨进程 UIA 启动。

## 2. Expected / Actual / Difference

| 检查 | Expected | 首次 Actual | Difference |
|---|---|---|---|
| 预检宿主 | 在已进入的受信 PowerShell 进程执行精确仓库脚本 | 再通过 PATH 解析裸 `powershell` | `PathSelectedPreflightHostCouldBeSpoofed` |
| 合同身份 | 仅接受当前 schema、purpose 与有限 Outcome | 仅解析 JSON，不核对 schema/purpose | `UnverifiedPreflightContractIdentity` |
| 启动准入 | 只有完整语义一致的 Pass，或显式确认的精确已知风险可继续 | 未知/缺失 Outcome 会绕过三个 if 分支 | `UnsupportedOutcomeFellThroughToLiveLaunch` |

该差异会把调用者可控的命令解析或未来合同漂移转化为 Live UI 放行，属于真实 fail-open，明确偏离最初“失败关闭、真实证据优先、不得用伪造探针替代物理结果”的路线。

## 3. Correction

- Live UI 使用调用运算符直接执行 `$runtimePreflightPath`，不再创建 PATH 解析的 PowerShell 子进程。
- 消费者固定接受 schema 5、`LongGridWinUiCrossProcessUiaRuntimePreflight` purpose 与四个已知 Outcome；其他合同先于应用启动失败关闭。
- 准入要求项目 Runtime 目标、包清单、兼容 Framework、选中 Framework 元数据和完整包集合五项均明确为真。
- 普通 Pass 必须同时满足 `difference=None` 与 `knownUnsafePairAbsent=true`。
- `BlockedByKnownUpstream` 仅在 Difference 精确匹配、风险事实明确为 false 且调用者显式传入确认参数时继续；既有诊断逃生口没有扩大。
- 真实进程回归在 PATH 首位放置能够伪造 schema 5 Pass 并写标记的 `powershell.cmd`，断言它未被调用，真实 Runtime 阻断仍在产品启动前发生。

## 4. 实际环境与副作用审计

当前机器真实预检仍为 `BlockedByIncompleteRuntime`：Framework `2.4.0.0`、XAML `3.2.3.0` 与 Singleton `8002.4.0.0` 可发现，仍缺 Main.2 `>=2.3.1.0` 和项目锁定 DDLM `2.3.1.0-x6`。

Live UI `-NoBuild` 退出 1；M1 `-ExternalAutomation -NoBuild` 返回 `startsProcess=false / createsEvidenceSession=false`。LongGrid 进程保持 `0→0`，没有创建 M1 证据会话。本轮没有安装或修改 Appx、Runtime、注册表、Explorer、任务栏、安全策略或用户文件，没有发送输入或调用跨进程 UIA。

## 5. 本地验证

- PowerShell AST parse：Pass；
- Runtime schema 5 九场景合同：`9/9`；
- SDK/证据入口真实进程专项：`11/11`，含 PATH PowerShell 投毒；
- `dotnet format --verify-no-changes`：Pass；
- Release solution build：`0 warning / 0 error`；
- 完整测试：`1,394/1,394`，0 skipped；
- 独立结果目录覆盖率：lines `90.43% (23,544/26,036)`，branches `76.16% (7,729/10,149)`；
- 漏洞门禁：0；依赖许可证仍为 `PendingOwnerReviewAndNotice`；
- SBOM ValidateOnly：Pass；签名与 RC 均保持 `signed=false / installable=false / distributionApproved=false`。

## 6. 路线与完成度审计

本轮只加固现有外部证据入口，没有修改产品业务逻辑、权限、系统状态、UI 功能或发布流程，符合最初失败关闭与零惊吓要求。Stage 239 的结构化预检结论没有被推翻，而是补齐了消费者宿主和结果准入边界。

M1/M2 继续为 `0/2 Complete`，30 项 PF 继续为 `0 Complete`；BOX-R1-C/D、TASKBAR-R2B1-B、真实可见交互、签名安装生命周期和正式分发均未升级。工程门禁不能替代物理用户旅程。

唯一接续点不变：等待 #23/#274 提供许可证、Publisher、托管签名和签名包；在安装完整兼容 Runtime、具备安全 WinUI/UIA、没有既有 Long方格进程的独占可丢弃 Windows 会话中，执行 BOX-R1 三场景与 M1 完整物理旅程。外部事实未变化时，只处理新的真实回归、质量或安全缺陷。

## 7. 远端审计

实现提交 `d3b5084` 与文档提交 `b21de46` 经 [PR #310](https://github.com/Longyuyeee/Long_Grid/pull/310) 合并为 `main@b41d92af2a7559ef0a9d37e2726c9b3bafae0c78`。PR CI run `33266636450` 全部通过：完整测试 `1,394/1,394`、0 skipped，coverage lines `90.11% (46,920/52,072)`、branches `76.03% (15,432/20,298)`；漏洞 0，依赖包 30，许可证继续为 `PendingOwnerReviewAndNotice`，SBOM 805 个文件，内部 RC 保持 `signed=false / installable=false / distributionApproved=false`。PR CodeQL run `33266636431` 的 C++、C# 分析均成功，Code Scanning API 对该分支返回开放告警 0。

精确合并提交自己的 main CI run `33266990470` 全部通过：完整测试 `1,394/1,394`、0 skipped，coverage lines `90.11% (46,924/52,072)`、branches `76.04% (15,434/20,298)`；漏洞 0、许可证待审、SBOM 805 个文件与 unsigned RC 否定性门禁均保持。main CodeQL run `33266990478` 的 C++、C# 分析均成功，main 开放告警 0。

PR 与精确 main 没有产生新的代码、质量、安全或供应链差异。#23/#274 均继续开放且更新时间未变化，远端工程门禁不能替代尚未取得的物理产品证据。本次回填不改变 M1/M2、PF 或唯一接续点；本文自己的文档提交只需独立通过 PR/main 检查，不再追加第三层文档收口。
