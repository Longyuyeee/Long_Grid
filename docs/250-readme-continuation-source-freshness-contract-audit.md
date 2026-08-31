# Stage 250：README 接续源新鲜度合同与真实漂移修正审计

日期：2026-08-31

输入基线：`origin/main@2a6f2579719996aded5bff5a576007ba19b95b44`

状态：`Complete / FreshnessContractAdded / ExternalEnvironmentBlocked`

## 1. 开发目标

进入 Stage 249 之后重新从 README 实际接续，发现顶部状态已指向 Stage 249，但“文档导航”仍把 Stage 247/246 标为当前，“建议的下一步”仍引用已被取代的 Stage 226，并缺少完整 Runtime、Stage 216 Guest 与 `#23/#274` 当前准入条件。同一份入口文档因此会给出互相冲突的接续路径。

本阶段目标是修正真实漂移，并将统一计划、README、Stage 153 backlog 和路线图之间的最新基线/Stage 关系变成 Windows PowerShell 5.1 可执行的只读 CI 合同。合同不得写系统状态，也不得把受阻的 M1/TASKBAR 工作伪装成完成。

## 2. Expected / Actual / Difference / Correction

| 检查 | Expected | Initial Actual | Difference / Correction |
|---|---|---|---|
| README 文档导航 | 包含统一计划声明的最新 Stage 249 | 最新入口仍是 Stage 247/246 | 增加 Stage 249/248，旧 Stage 改为“上一轮” |
| README 建议接续点 | 与 Stage 249/241 的唯一边界一致 | 引用 Stage 226；缺 Runtime、Stage 216 Guest、`#23/#274` | 改为 BOX/M1 与 TASKBAR 两条精确准入路径；未准入时只处理真实缺陷 |
| 自动漂移检测 | 当前错误必须失败，修正后通过 | 仓库原先没有 README/计划新鲜度合同 | 新增 `eng/Test-LongGridExecutionSourceFreshness.ps1`，由 CI 已执行的 Action 固定门禁调用；不要求 workflow 写权限 |
| Windows PowerShell 5.1 | 正式 CI 宿主能解析并执行 | 初版无 BOM UTF-8 脚本含中文常量，实际发生 ParserError | 合同源码改为纯 ASCII，使用结构和稳定链接识别中文文档 |
| 确定性否定测试 | 人为回退建议段时必须给出精确差异 | 修正前无此保护 | 内存中把当前 Stage 改回 Stage 226，固定得到 `readme-continuation:audit-expected=250-actual=226` |
| 完整套件真实进程稳定性 | 1,398 项在并行负载下全部通过 | 首轮 `1,397/1,398`；任务栏认证测试第一次兼容性探测在测试专用 3 秒内未完成，独立复跑 959 ms 通过 | 只把该测试类的有界进程预算改为 10 秒；产品 App 3 秒预算和失败关闭不变 |

## 3. 真实测试结果

文档漂移复现直接读取仓库真实 README 与三份权威文档，不使用 mock 文件。首次可执行合同按 Stage 249 基线非零退出，精确报告：导航缺 Stage 249、建议段实际 Stage 226，并缺 Runtime、Stage 216 与 `#23/#274`。修正文档后同一正式 Windows PowerShell 5.1 入口通过，同时内存负向变体被拒绝。完整测试首轮又真实暴露任务栏认证测试在并行进程负载下的测试预算差异；正式认证脚本与独立测试均通过，证明系统状态未变化，修正仅扩大测试 harness 的有界等待，不修改产品预算。

| 门禁 | Expected | Actual | Difference |
|---|---|---|---|
| 新鲜度合同正向 | 四份真实文档一致 | Stage 250、基线 `2a6f257`、四文档一致，Pass | None |
| 新鲜度合同负向 | 回退到 Stage 226 必须失败 | `readme-continuation:audit-expected=250-actual=226` | None |
| Locked restore / format / Release build | 可复现、零格式差异、0 warning/error | 全部通过；Release `0 warning / 0 error` | None |
| 任务栏认证专项 | 修正后真实进程稳定，系统状态不变 | 连续 `10/10`；正式脚本 `difference=None / modifiedSystemState=false / taskbarWindowIdentityUnchanged=true` | None |
| 完整测试与覆盖率 | 1,398 项基线全部执行；覆盖率不降低 | `1,398/1,398`、0 failed、0 skipped、38 秒；lines `90.46% (47,096/52,064)`、branches `76.16% (15,456/20,294)` | None |
| UI 合同 | 产品合同不退化 | ContractOnly `198` IDs，Pass | None |
| 漏洞 | 已知漏洞 0 | 真实锁定依赖扫描通过，0 | None |
| 许可证 | 清单完整，未批准不得分发 | 20 项目/30 包；确定性正/负门禁通过；`PendingOwnerReviewAndNotice / distributionApproved=false` | None |

## 4. 开发目标与需求对齐审计

开发目标审计：真实 README 漂移已从一次性文字修正升级为可重复、带确定性负向场景的 CI 合同；合同来源是统一计划，不要求每次手工维护固定 Stage 常量。完整套件发现的真实进程测试预算差异也已按“修正前失败—独立/正式入口对照—修正后专项与全量通过”闭环，没有用简单重跑掩盖失败。

需求对齐审计：本阶段只修改文档与只读工程门禁，不修改产品运行时、用户文件、Runtime 包、Sandbox、任务栏、签名、安装或分发状态。M1/M2 继续 `0/2 Complete`，30 项 PF 继续 `0 Complete`。

下一接续点不变：`#23/#274`、完整兼容 Runtime、受保护签名包和独占可丢弃 Windows 会话到位后执行 BOX-R1-C/D 与 M1；或者 Stage 216 Host/Guest 达到 `ReadyToLaunch / GuestReady` 后仅在 Guest 执行 TASKBAR-R2B1-B。两者均未成立时，只处理新复现的回归、质量或安全缺陷。
