# Stage 238：Runtime 元数据读取失败归一化审计

日期：2026-08-30

基线：`origin/main@df12e15062d2627ed90c1b7868f584944dbae7db`

状态：`CorrectionComplete / LocalAuditPassed / ProductEvidenceBlocked`

## 1. 接续复读与路线约束

本轮先同步 `main`，复核 Stage 237、Runtime 预检、Live UI/M1 消费者、真实进程测试、#23/#274 和开放 PR。两项外部 Issue 没有更新，开放 PR 为 0；完整兼容 Runtime、受保护签名包和独占可丢弃 Windows 会话仍未到位。因此继续只关闭会影响真实失败关闭判断的新发现回归、质量或安全缺陷。

Stage 237 已让结果函数在选中最高 Framework 的 XAML 元数据为 `null` 时返回 `SelectedRuntimeFrameworkMetadataNotDiscoverable / Inconclusive`。但真实包枚举直接读取 `Microsoft.UI.Xaml.dll` 的 `VersionInfo.FileVersionRaw` 并强转 `[version]`：路径访问、文件元数据或版本格式异常会在结果函数之前终止脚本；任一较低候选发生异常也会阻断对真正最高候选的评估。

## 2. Expected / Actual / Difference

| 检查 | Expected | 首次 Actual | Difference |
|---|---|---|---|
| 文件访问异常 | 归一化为不可读，由选中候选产生结构化 `Inconclusive` | PowerShell 在输出 JSON 前终止 | `MetadataReadFailureEscapedPreflightSchema` |
| 空或非法版本 | 视为不可读，不把解析异常暴露给消费者 | `[version]` 强转可终止脚本 | `InvalidFileVersionEscapedNormalization` |
| 未选中低版本候选异常 | 不影响最高兼容候选的判断 | 枚举阶段读取所有候选，任一异常都可中断 | `UnselectedCandidateFailureAbortedSelection` |

当前消费者仍会因异常停止，未形成安全放行；但它失去稳定 Difference、Outcome 和副作用审计字段，违背 Stage 237 已记录的结构化失败关闭承诺，属于真实质量与可审计性缺陷。

## 3. Correction

- 新增受控 `ConvertFrom-XamlFileVersionRead`：在同一边界内执行读取、处理空值并转换版本；任何访问或格式异常统一返回 `$null`。
- 新增 `Get-XamlFileVersion`，把安装路径拼接、文件存在性和 `VersionInfo.FileVersionRaw` 读取全部放入上述受控边界。
- 真实包规范化不再直接访问或强转文件版本；各 Framework 都取得“可读版本或 null”。未选中候选的读取失败不再终止选择；若最高候选为 null，则继续由 schema 4 返回 `SelectedRuntimeFrameworkMetadataNotDiscoverable / Inconclusive`。
- 不输出安装路径、异常文本或用户环境细节，避免把本地路径引入证据 JSON。
- 合同 schema 升为 4，新增读取异常和非法版本注入检查，总场景数由 7 增为 8；既有最高候选不可读场景改用真实失败读取结果。

## 4. 实际环境与副作用审计

当前机器读取最高兼容 Framework `2.4.0.0` 的 XAML `3.2.3.0` 成功，`selectedFrameworkMetadataDiscoverable=true`。Main.2 `>=2.3.1.0` 与精确 DDLM `2.3.1.0-x6` 仍缺失，因此真实结果保持 `IncompleteRuntimePackageSet / BlockedByIncompleteRuntime`。

Live UI 负向复测退出码 1；M1 `-ExternalAutomation -NoBuild` 返回 `startsProcess=false / createsEvidenceSession=false`。LongGrid 进程与证据目录均为 `0→0`。本轮没有安装、修复或卸载 Runtime，没有修改 Appx、注册表、Explorer、任务栏、安全策略或用户文件，也没有调用跨进程 UIA 或发送输入。

## 5. 本地验证

- Runtime 八场景合同：`8/8`；
- PowerShell AST parse：Pass；
- SDK/证据入口专项真实 PowerShell 进程测试：`10/10`；
- `dotnet format --verify-no-changes`：Pass；
- Release solution build：`0 warning / 0 error`；
- 完整测试：`1,393/1,393`，0 skipped；
- 独立结果目录覆盖率：lines `90.41% (47,078/52,072)`，branches `76.17% (15,460/20,298)`。

## 6. 路线与完成度审计

本轮补全已有 Runtime 预检承诺，没有修改产品业务逻辑、权限、系统状态或发布流程；符合最初“真实证据优先、失败关闭、不注入 Explorer、不擅改用户文件”的路线。

M1/M2 继续为 `0/2 Complete`，30 项 PF 继续为 `0 Complete`；BOX-R1-C/D、TASKBAR-R2B1-B、签名、安装生命周期和正式分发均未升级。工程门禁不能代替物理用户旅程。

唯一接续点不变：等待 #23/#274 提供许可证、Publisher、托管签名和签名包；在安装完整兼容 Runtime、具备安全 WinUI/UIA、没有既有 Long方格进程的独占可丢弃 Windows 会话中，执行 BOX-R1 三场景与 M1 完整物理旅程。外部事实未变化时，继续只处理新的真实回归、质量或安全缺陷。

## 7. 远端审计

待实现与文档提交推送后回填 PR、CI、CodeQL、合并提交与最终 `main` 结果。远端门禁不能替代尚未取得的物理产品证据。
