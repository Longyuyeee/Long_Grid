# Stage 255：M1 证据根重解析点清理安全审计

日期：2026-08-31

输入基线：`origin/main@f653f2f830e242e11f59a2ac9ec19a1339366c4b`

状态：`ImplementationComplete / LocalAuditPass / RemotePending / ExternalEnvironmentBlocked`

## 1. 接续条件与开发目标

从 Stage 254 最终 main 重新同步并复读真实准入。#23 仍为 OPEN，最后更新 `2026-08-28T03:51:37Z`；#274 仍为 OPEN，最后更新 `2026-08-28T02:43:13Z`。Runtime 实测为 Framework `2.4.0.0`、XAML `3.2.3.0`、Singleton `8002.4.0.0`，仍缺 Main.2 `>=2.3.1.0` 与 DDLM `2.3.1.0-x6`；M1 ExternalAutomation 返回 `BlockedByIncompleteRuntime / startsProcess=false / createsEvidenceSession=false`，LongGrid PID 和 M1 session 集合前后均为空。TASKBAR Host 生成并复读安全 `.wsb` 后，仍因 `HardwareEvidenceUnavailable / WindowsSandboxLauncherMissing` 返回 `Blocked / mutationAllowed=false / modifiedSystemState=false`。

两条产品入口均未准入，本阶段只处理实际代码复读和真实测试复现的安全缺陷。M1 cleanup 已验证 session GUID、路径前缀、精确 marker 和 session 目录本身不是重解析点，但固定 `%TEMP%\LongGridM1ManualEvidence` 根目录没有同等检查。若该根是 junction，字符串路径仍在预期前缀内，session 子目录也可以是普通目录，递归删除会落到 junction 目标。

## 2. Expected / Initial Actual / Difference / Correction

| 检查 | Expected | Initial Actual | Difference / Correction |
|---|---|---|---|
| 证据根为 junction | 非零拒绝，不删除目标 session/sentinel | 测试自有临时脚本、临时 junction 和精确 marker 会话真实返回 exit 0；目标 session 被删除 | cleanup 在解析 session 前先验证固定根不是 reparse point |
| 正常会话创建 | 不得在重定向根下创建配置、夹具或 marker | 旧代码直接对根下配置路径 `New-Item -Force` | 先显式创建固定根并验证，再生成 session ID 和任何会话内容 |
| 零副作用模式 | ValidateOnly/Runtime 阻断不得创建根或 session | 现有分支在创建逻辑前返回 | 保持分支顺序；Windows PowerShell 5.1 与 PowerShell 7 ValidateOnly 均 Pass |
| 相邻生命周期 | 缺失 session、合法 cleanup、启动/准备失败清理不回归 | 必须保留 Stage 245/247/252/253 合同 | M1 相关真实子进程专项 `6/6` 通过 |

首次测试夹具尝试使用目录 symbolic link，当前账户真实返回“客户端没有所需的特权”；测试随后改用不要求该特权的 PowerShell junction，未放宽产品断言。修正前同一回归精确失败为 `Expected: Not 0 / Actual: 0`；修正后错误包含 `Refused to use a reparse-point M1 manual evidence root.`，目标 session、sentinel 和 LongGrid PID 集合均不变。测试 finally 先确认 fixture 根确为 reparse point，再只删除该链接，测试目标位于本次 GUID 临时树内。

首次专项命令还真实发现当前 shell 的裸 `dotnet` 指向无 SDK 宿主；按仓库既有解析合同改用 `C:\Program Files\dotnet\dotnet.exe`，没有修改 `global.json`、PATH 或 SDK 要求。

## 3. 本机真实验证

| 门禁 | Expected | Actual | Difference |
|---|---|---|---|
| M1 根与相邻 cleanup 专项 | junction 必须拒绝，既有生命周期不回归 | `6/6` | None |
| Locked restore / format / Release | 锁定依赖、零格式差异、0 warning/error | 全部通过；format attempts=1；Release `0 warning / 0 error` | None |
| 完整测试 | 新回归进入全套 | `1,401/1,401`、0 failed、0 skipped、19 秒 | None |
| Coverage | lines >=90%、branches >=75% | lines `90.43% (23,542/26,032)`；branches `76.17% (7,729/10,147)` | None |
| UI / 执行源 | 产品合同与接续入口不退化 | UI ContractOnly `198` IDs；freshness 与 Action pins Pass | None |
| 漏洞与许可证 | 漏洞 0；未批准前禁止分发 | 漏洞 0；20 项目/30 包；`PendingOwnerReviewAndNotice / distributionApproved=false` | None |

覆盖率使用独立 ignored `artifacts/stage255-test-results`，未聚合历史 `TestResults`。本阶段没有安装 Runtime、启用 Sandbox、修改任务栏、启动 LongGrid、发送物理输入、签名、安装或分发产物。

## 4. 开发目标与需求对齐审计

开发目标审计：已从“代码看似有 session 重解析点检查”推进到根与 session 两级都失败关闭，并以真实 Windows junction 证明修正前删除和修正后零删除。修正没有扩大到 M1 邻接探针或产品功能。

需求对齐审计：修正直接落实 README 的“绝不擅自删除用户文件”和统一计划的证据所有权边界。产品运行时、正式配置、用户文件、桌面、任务栏、Runtime、签名和安装状态均未改变。M1/M2 继续 `0/2 Complete`，30 项 PF 继续 `0 Complete`，内部 RC 继续不可分发。

下一唯一接续点仍由 `#23/#274`、完整兼容 Runtime、受保护签名包和独占可丢弃 Windows 会话共同约束；TASKBAR-R2B1-B 仍要求 Stage 216 Host/Guest 达到 `ReadyToLaunch / GuestReady`。两者未成立时，只处理新复现的真实回归、质量或安全缺陷。

## 5. 远端交付

Pending。实现和本地审计完成后，将通过短分支 PR 推送；必须记录 PR head CI/CodeQL、合并提交和精确 main 复验，未完成前不把本阶段状态写成远端闭环。
