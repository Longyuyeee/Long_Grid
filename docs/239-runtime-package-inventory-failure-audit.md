# Stage 239：Runtime 包清单枚举失败关闭审计

日期：2026-08-30

基线：`origin/main@58c7ade94075f5ee45f001d0053ee8ea0e1f14cf`

状态：`CorrectionComplete / LocalAuditPassed / ProductEvidenceBlocked`

## 1. 接续复读与路线约束

本轮同步 `main` 后复核 Stage 238、Runtime 预检、Live UI/M1 消费者、真实进程测试、#23/#274 和开放 PR。两项外部 Issue 仍无更新，开放 PR 为 0；完整兼容 Runtime、签名包和独占可丢弃 Windows 会话仍未到位。因此继续只处理会影响既有失败关闭判断的新发现回归、质量或安全缺陷。

Stage 238 已处理单个 Framework 的文件访问与版本解析异常，但真实入口仍直接运行 `Get-AppxPackage -ErrorAction Stop`。包数据库、权限、服务或命令执行异常会在结果函数和 JSON 输出之前终止脚本。消费者不会继续启动产品，但无法取得稳定 Difference、Outcome 和可复读的零启动证据。

## 2. Expected / Actual / Difference

| 检查 | Expected | 首次 Actual | Difference |
|---|---|---|---|
| 包清单枚举异常 | 返回结构化 `Inconclusive` 并保留项目目标 | 脚本在 JSON 输出前终止 | `RuntimePackageInventoryFailureEscapedSchema` |
| 枚举失败与空列表 | 两者保持不同事实 | 都无法由 schema 稳定区分 | `InventoryFailureWasNotDistinguishedFromNoFramework` |
| Live UI/M1 审计 | 启动前阻断并携带稳定 Difference/Outcome | 仅由脚本异常中止，证据字段缺失 | `FailClosedButNotAuditable` |

该缺口没有形成安全放行，但偏离了最初“真实证据优先、失败关闭且可审计”的要求，属于当前接续约束允许关闭的真实质量缺陷。

## 3. Correction

- 新增 `Get-RuntimePackageInventory`，在同一 try/catch 边界内执行包读取并完整收集输出；成功返回 `Discoverable=true` 与包数组，任何异常返回 `Discoverable=false` 与空数组，不暴露异常文本或本地环境细节。
- `Get-RuntimePreflightResult` 增加清单可发现性输入；schema 升为 5，Expected/Actual 新增 `runtimePackageInventoryDiscoverable`。
- 清单不可读取时保留项目最低版本，其他 Runtime 事实为未知，Difference 固定为 `RuntimePackageInventoryNotDiscoverable`、Outcome 为 `Inconclusive`。
- 成功读取但没有兼容 Framework 继续返回 `RuntimeFrameworkNotDiscoverable`；不得用空数组冒充读取失败，也不得把读取失败写成确认未安装。
- Live UI 的通用 `Inconclusive` 提示明确包含 package inventory；M1 继续原样携带结构化预检对象，且只在 Outcome 为 `Pass` 时允许进入启动路径。
- 合同 schema 升为 5，以真实 scriptblock 注入枚举异常，并验证成功读取的四包集合未丢失；总场景数由 8 增为 9。

## 4. 实际环境与副作用审计

当前机器包清单读取成功，`runtimePackageInventoryDiscoverable=true`。最高兼容 Framework 仍为 `2.4.0.0`，XAML `3.2.3.0`；Main.2 `>=2.3.1.0` 与精确 DDLM `2.3.1.0-x6` 缺失，因此保持 `IncompleteRuntimePackageSet / BlockedByIncompleteRuntime`。

Live UI 负向复测退出码 1；M1 `-ExternalAutomation -NoBuild` 返回 `startsProcess=false / createsEvidenceSession=false`。LongGrid 进程与证据目录均为 `0→0`。本轮没有安装、修复或卸载 Runtime，没有修改 Appx、注册表、Explorer、任务栏、安全策略或用户文件，也没有调用跨进程 UIA 或发送输入。

## 5. 本地验证

- Runtime 九场景合同：`9/9`；
- PowerShell AST parse：Pass；
- SDK/证据入口专项真实 PowerShell 进程测试：`10/10`；
- `dotnet format --verify-no-changes`：Pass；
- Release solution build：`0 warning / 0 error`；
- 完整测试：`1,393/1,393`，0 skipped；
- 独立结果目录覆盖率：lines `90.41% (47,078/52,072)`，branches `76.17% (15,460/20,298)`。

## 6. 路线与完成度审计

本轮只补全 Runtime 包清单读取失败的结构化证据，不修改产品业务逻辑、权限、系统状态或发布流程；与最初需求路线一致。

M1/M2 继续为 `0/2 Complete`，30 项 PF 继续为 `0 Complete`；BOX-R1-C/D、TASKBAR-R2B1-B、签名、安装生命周期和正式分发均未升级。工程门禁不能替代物理用户旅程。

唯一接续点不变：等待 #23/#274 提供许可证、Publisher、托管签名和签名包；在安装完整兼容 Runtime、具备安全 WinUI/UIA、没有既有 Long方格进程的独占可丢弃 Windows 会话中，执行 BOX-R1 三场景与 M1 完整物理旅程。外部事实未变化时，继续只处理新的真实回归、质量或安全缺陷。

## 7. 远端审计

待实现与文档提交推送后回填 PR、CI、CodeQL、合并提交与最终 `main` 结果。远端门禁不能替代尚未取得的物理产品证据。
