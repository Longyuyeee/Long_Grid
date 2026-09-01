# Stage 251：当前开发状态与换电脑接续手册

日期：2026-08-31

交接输入基线：`origin/main@462380cd859bc4e7a6085e1a5fab5c5987586b45`

状态：`HandoffPrepared / DevelopmentPaused / ExternalAdmissionPending`

> 2026-09-01 接续更新：本文保留 Stage 251 当时的环境交接证据；最新代码和唯一功能接续点已前进到 [Stage 266](266-pf010b1-unified-history-action-breadth-audit.md)。换电脑仍必须从最新 GitHub `main` 全新拉取，不使用本文的历史 SHA 作为 checkout 目标。PF-010B1 已完成删除、布局、文件夹和引用动作广度、批量一步语义及 Failed-save 历史补偿，最新完整 Release 测试为 `1,447/1,447`、Release 0 warning/error、211-ID UI 合同；下一步只进入 PF-010B2 重启后最近一次安全恢复点。

## 1. 交接结论

从本阶段开始暂停继续扩展功能，换电脑后的唯一代码来源是 GitHub `main`。当前项目不是“功能全部完成”，也不是“需要重新开发 Core”；准确状态是：桌面盒子、单文件夹绑定和 PF-001～PF-007 的工程链已经形成，但顶层 M1/M2 和 30 项 PF 均没有取得完整产品证据；M1 受 Runtime、签名、独占可丢弃 Windows 会话及负责人输入阻断，TASKBAR-R2B1-B 受 Host/Guest 准入阻断。

换电脑后不得按照聊天记录猜测进度，也不得复制当前电脑的 Runtime、临时证据、进程、证书或系统状态。先拉取 GitHub `main`，再按照本文从只读合同、机器预检到真实测试逐层恢复；任一准入失败就记录 Actual 并停止对应正向旅程。

## 2. 当前严格完成度

| 范围 | 当前事实 | 不能误解为 |
|---|---|---|
| 顶层里程碑 | M1/M2 `0/2 Complete` | 工程测试通过不等于真实物理旅程完成 |
| 30 项 PF | `0 Complete` | PF-001～PF-007 的工程链完成不等于完整产品证据完成 |
| 桌面盒子 Core | BOX-R1-A/B、FOLDER-R1-A～D、PF-001～PF-007 工程链已合入 | 不需要重写 Core，但仍需 BOX-R1-C/D 与 M1 真实旅程 |
| 任务栏 Core | TASKBAR-R1A～R2B1-A2 已完成只读探测、恢复边界、原生适配器默认禁用、环境准入与有限预设基础 | 尚未执行 R2B1-B Guest 原生效果、R3 恢复和 R4 完整矩阵 |
| 发布 | unsigned 内部 RC、SBOM、漏洞与许可证技术门禁存在 | `signed=false / installable=false / distributionApproved=false`，不可公开分发 |
| 扩展功能 | 自动整理、Tab、Peek、工作空间、Widget/插件等仍在 Core 之后 | 外部门禁未满足时不得借机转做外围能力 |

## 3. 最近阶段已经完成的工作

1. Stage 244～245：M1 `ValidateOnly / ExternalAutomation / Cleanup` 模式互斥和清理语义已失败关闭。
2. Stage 246～247：M1 Ready 必须同时具备 `AppConstructed + ProductWindowActivated + 非空标题`；启动异常后的本次进程与证据目录统一清理已修正。
3. Stage 248：真实升级 Windows App SDK 2.4.0 后仍复现 `Microsoft.UI.Xaml.dll 3.2.3.0 / 0xc000027b / offset 0x3a9c5d`，无收益升级已撤回，项目继续锁定 2.3.1。
4. Stage 249：统一计划、Stage 153 backlog、路线图和 README 顶部已对齐当前真实接续条件；本机 M1 与 TASKBAR 准入仍阻断且零系统修改。
5. Stage 250：README 内部 Stage 249/247/226 接续冲突已修正；新增跨文档新鲜度合同并由现有 CI 门禁调用。完整测试首轮真实出现任务栏认证测试 harness 3 秒超时，产品环境和正式脚本正常；只将该测试类预算改为 10 秒，产品 App 3 秒预算保持不变，专项 `10/10` 和全量 `1,398/1,398` 通过。

这些工作已经在 [PR #328](https://github.com/Longyuyeee/Long_Grid/pull/328) 合并为 `main@462380c`。PR CI `33377906345` 为 `1,398/1,398`、coverage lines `90.14% (46,930/52,064)`、branches `76.04% (15,432/20,294)`、漏洞 0、许可证继续阻断分发；PR CodeQL `33377906325` 的 C# 与 C/C++ 均通过。

精确合并提交自己的 main CI `33378857111` 也已通过：`1,398/1,398`、0 skipped、33 秒，coverage lines `90.14% (46,932/52,064)`、branches `76.04% (15,432/20,294)`，漏洞 0，20 项目/30 包，`PendingOwnerReviewAndNotice / distributionApproved=false`；测试制品 ID `9753180080`。main CodeQL `33378857146` 的 C# 与 C/C++ 均通过。PR 与精确 main 之间没有产生新的代码、质量、安全或供应链差异。

## 4. 当前电脑的时间点事实

以下事实只能解释为什么当前电脑没有进入正向旅程，不能直接迁移为新电脑的 Actual：

| 检查 | 最近一次真实 Actual | 当前结论 |
|---|---|---|
| M1 Runtime 准入（Stage 249） | Framework `2.4.0.0`、XAML `3.2.3.0`、Singleton `8002.4.0.0`；缺 Main.2 `>=2.3.1.0` 与项目锁定 DDLM `2.3.1.0-x6` | `BlockedByIncompleteRuntime / startsProcess=false / createsEvidenceSession=false` |
| 自包含启动（Stage 248） | 产品退出且窗口标题为空；WER 指纹仍为 `Microsoft.UI.Xaml.dll 3.2.3.0 / 0xc000027b / 0x3a9c5d` | 2.4.0 未证明修复，不得放宽 Ready |
| TASKBAR Host 准入（Stage 249） | `HardwareEvidenceUnavailable / WindowsSandboxLauncherMissing / SandboxConfigurationMissing` | `Blocked / mutationAllowed=false / modifiedSystemState=false` |
| 任务栏认证（Stage 250） | Windows `10.0.26200.0`；两个 Explorer 任务栏窗口身份前后不变 | `adapterAvailability=Unavailable / runtimeAdmission=DeniedNoCertifiedBuild / modifiedSystemState=false` |
| 负责人输入 | #23、#274 仍 OPEN；最后更新时间分别为 `2026-08-28T03:51:37Z`、`2026-08-28T02:43:13Z` | 许可证、Publisher、托管签名、五人证据仍 Pending |

Issue #19、#20、#24 也仍 OPEN，分别持有人工输入/系统表面矩阵、动态显示/会话矩阵和真实持久化卷边界；自动合同通过不能关闭这些人工或专用环境证据。

## 5. 可以通过 GitHub 迁移的内容

- `main` 中的源代码、锁文件、测试、`eng/` 工程入口、工作流和审计文档；
- Stage 241 的证据传递边界、Stage 248 的 Runtime 对照、Stage 249 的准入复核、Stage 250 的新鲜度合同与真实测试差异；
- PR #328 及 CI/CodeQL 运行记录；
- Issue #19、#20、#23、#24、#274 的远端状态；
- 匿名、有限、已提交的 Expected/Actual/Difference 和哈希证据。

## 6. 不得复制或冒充可迁移证据的内容

- `%TEMP%`、仓库 `artifacts/`、`TestResults/` 中的本机测试/证据目录、窗口句柄、PID、Explorer 进程身份、WER 缓存和截图；
- 当前电脑安装的 Appx/Runtime 包集合、Windows Sandbox/可选功能状态、显示器拓扑、系统构建号和账户状态；
- PFX/P12、私钥、Publisher 身份、token、client secret、OIDC 凭据或证书存储；
- unsigned MSIX/ZIP 不能当作签名安装包或公开分发产物；
- 合同测试、自动 UIA、源码断言或当前电脑的负向阻断，不能替代新电脑的物理鼠标、键盘、Narrator、高对比、安装/卸载和恢复证据。

当前电脑存在本阶段本机测试生成的已忽略 `artifacts/stage250-test-results` 与 `artifacts/stage250-final-test-results`；自动清理被本地安全策略拒绝。这两个目录不在 Git 提交中、不需要复制，换电脑时直接以全新 clone 开始。

## 7. 新电脑恢复开发的精确步骤

### 7.1 取得唯一代码源

```powershell
git clone https://github.com/Longyuyeee/Long_Grid.git
Set-Location Long_Grid
git switch main
git pull --ff-only origin main
git status --short --branch
git rev-parse HEAD
git rev-parse origin/main
```

要求：工作区干净，`HEAD == origin/main`，并能看到本文。不要复制旧电脑工作树、`bin/obj/artifacts/TestResults` 或本地分支来代替 clone。

### 7.2 恢复工具并验证文档执行源

```powershell
dotnet --info
dotnet tool restore
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Test-LongGridWorkflowActionPins.ps1
```

该入口会同时执行 Stage 250 引入的新鲜度合同。Expected 为当前 README、统一计划、Stage 153 backlog 与路线图指向同一 Stage/基线，并且 Stage 226 内存回退被拒绝。若失败，先修文档源，不得按过期入口开发。

### 7.3 只读机器准入复测

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Test-LongGridWinUiUiaRuntime.ps1

powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-LongGridM1ManualEvidenceSession.ps1 `
  -ExternalAutomation

powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Test-LongGridTaskbarDisposableEnvironment.ps1
```

每条命令都必须保存新电脑自己的 Expected、Actual、Difference、Outcome。不要预先安装未知 Runtime、启用 Windows Optional Feature、改系统策略或在宿主任务栏试写来强行获得 Pass。

### 7.4 恢复工程基线

只有只读入口没有发生意外系统修改后，执行：

```powershell
dotnet restore LongGrid.sln --locked-mode
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Test-LongGridFormat.ps1
dotnet build LongGrid.sln --configuration Release --no-restore
dotnet build-server shutdown
dotnet test LongGrid.sln `
  --configuration Release `
  --no-build `
  --collect "XPlat Code Coverage" `
  --results-directory TestResults
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Verify-Coverage.ps1 `
  -MinimumLineRate 0.90 `
  -MinimumBranchRate 0.75
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Test-LongGridUi.ps1 `
  -ContractOnly
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Verify-VulnerablePackages.ps1
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Test-LongGridDependencyLicenseGate.ps1
```

基线 Expected：Release 0 warning/error、1,398 项全部通过、coverage 至少 90%/75%、UI 198 IDs、漏洞 0；许可证在负责人未批准前仍应为 `PendingOwnerReviewAndNotice / distributionApproved=false`。若数量因新提交合法变化，应以最新 `main` 文档和 CI 为准，但不得降低阈值或删除失败测试来对齐旧数字。

## 8. 换机后的开发决策树

### 路径 A：继续 BOX-R1-C/D 与 M1

必须同时满足：

1. #23/#274 已提供许可证、Publisher 和托管签名所需负责人输入；
2. 新电脑 Runtime 预检为完整兼容集合，且不存在已知不安全 WinUI/XAML 组合；
3. 取得受保护签名包，而不是 unsigned 开发包；
4. 使用无个人数据、无既有 Long方格进程的独占可丢弃 Windows 账户/VM；
5. 按 Stage 241/249 的顺序执行 BOX `Initial / Redirect / DuplicateRedirect`，再完成 M1 两分钟物理旅程。

任一条件不满足，M1 继续 Pending，不得再增加邻接探针冒充进展。

### 路径 B：继续 TASKBAR-R2B1-B

只有 Stage 216 Host 返回 `ReadyToLaunch`，Guest 再返回 `GuestReady` 时，才允许在 Guest 内执行有限原生预设与恢复验证。不得在宿主桌面试写、提权、关闭安全功能或将 `DeniedNoCertifiedBuild` 改成 Allowed。

### 路径 C：外部条件仍未满足

只处理从真实代码、真实进程或真实 CI 中新复现的回归、质量或安全缺陷。修正必须保留“修正前 Actual—修正后 Actual”的对照，并在阶段结束更新文档、开发目标审计、需求对齐审计、PR 和精确 main 结果。不得扩张自动整理、Tab、Peek、工作空间、Widget 或 Long助手运行时。

## 9. 新电脑首轮必读顺序

1. [README](../README.md) 当前状态和“建议的下一步”；
2. [统一开发计划](PRODUCT_EXECUTION_PLAN.md)；
3. 本文 Stage 251；
4. [Stage 250](250-readme-continuation-source-freshness-contract-audit.md)；
5. [Stage 249](249-execution-source-of-truth-freshness-audit.md)；
6. [Stage 248](248-windows-app-sdk-2-4-runtime-upgrade-audit.md)；
7. [Stage 241](241-current-development-handoff-audit.md)；
8. [Stage 216](216-taskbar-r2b1a-disposable-environment-admission-audit.md)。

## 10. 交接审计

开发目标审计：本阶段停止继续编码，只把最新 `main`、严格完成度、真实阻断、不可迁移状态、恢复命令、准入决策树和停止规则固化为换机入口。

需求对齐审计：没有修改产品代码、Runtime、系统设置、任务栏、签名、安装或分发状态；M1/M2 仍为 `0/2 Complete`，30 项 PF 仍为 `0 Complete`。换电脑不改变完成度，也不构成外部准入。

后续每一步仍须使用真实测试，明确 Expected / Actual / Difference / Correction；阶段结束必须更新相关文档、审计开发目标与需求对齐，并通过 PR 与合并后精确 `main` 检查后再进入下一阶段。
