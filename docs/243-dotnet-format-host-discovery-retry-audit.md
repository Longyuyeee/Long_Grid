# Stage 243：dotnet format 宿主发现竞态纠偏审计

日期：2026-08-31

输入基线：`origin/main@b0fb5c7142bc7f63b571210c38f538c5cb2fe287`

状态：`SecondDifferenceCorrected / LocalVerificationPass / PullRequestReverificationPending / ProductStatusUnchanged`

## 1. 接续原因与开发目标

Stage 242 的 PR #315 两轮 CI/CodeQL 全部通过并 squash merge 后，精确 main CI run `33349579578` 首次出现新差异：setup-dotnet v6、工具还原和 solution locked restore 均通过，但 `dotnet format LongGrid.sln --verify-no-changes --no-restore` 在没有任何格式诊断时返回 `Unable to locate dotnet CLI. Ensure that it is on the PATH.`，导致 build/test/coverage 未执行。同期 main CodeQL run `33349579586` 的 C++ 与 C# 均成功。

目标不是重跑获取偶然绿灯，而是把已知上游间歇性宿主发现竞态收敛为有界、可审计的格式门禁：使用仓库既有的项目兼容绝对 SDK host；只对精确已知错误重试一次；真实格式差异、其他错误或第二次失败仍立即失败。

上游依据：[dotnet/format #2218](https://github.com/dotnet/format/issues/2218) 记录同一间歇错误；其维护者指向工具内部宿主探测路径。历史 [dotnet/format #2000](https://github.com/dotnet/format/pull/2000) 说明 `dotnet --version` 子进程输出重定向存在过竞态；当前错误在新版仍有开放报告。因此本阶段不宣称修复上游，只在仓库门禁边界有限吸收精确瞬态。

## 2. Expected / Actual / Difference / Correction

| 检查 | Expected | Initial Actual | Difference / Correction |
|---|---|---|---|
| PR 到 main 一致性 | 相同 setup-dotnet v6、SDK 和命令应在精确 main 继续通过 | PR 两轮通过；main 的 setup、tool restore、solution restore 通过，format 内部宿主探测失败 | 保留 main run，不以 PR 绿灯覆盖；新增仓库格式门禁入口 |
| SDK host | 工程脚本不得依赖 PATH 中偶然优先的 dotnet | workflow 直接执行 `dotnet format`，未复用 Stage 230～233 的绝对 host 解析 | `Test-LongGridFormat.ps1` 复用 `Resolve-LongGridDotNetHost`，以绝对 host 启动 format |
| 重试范围 | 只吸收可识别瞬态，不隐藏格式错误 | 上游错误文本精确为 `Unable to locate dotnet CLI. Ensure that it is on the PATH.` | 最多 2 次；仅第一次非零且包含精确文本时重试；普通格式失败和成功均不重试 |
| 可回归性 | 重试策略和 workflow 消费必须进入自动化 | 旧套件没有 format 门禁合同 | 新增真实 PowerShell 进程合同，验证 exact=true、other=false、success=false，并断言 CI 不再直接调用 `dotnet format` |
| 产品与权限 | 工程纠偏不改变产品、权限或发布状态 | main CodeQL 双语言成功；产品代码未参与失败 | 不修改 `src`、Action 权限、覆盖率阈值、签名或分发入口 |

## 3. 实现边界

- 新增 `eng/Test-LongGridFormat.ps1`：从 `global.json` 所在仓库根解析兼容 SDK host，以绝对路径运行 format；
- `-ContractOnly` 对三种判定做确定性验证：精确瞬态重试、普通失败不重试、成功不重试；
- CI 的 Format step 改为调用该入口，其他步骤和权限不变；
- 新增 `FormatGateRetriesOnlyTheExactBoundedHostDiscoveryDifference` 真实 PowerShell 进程测试，并在 poisoned PATH 下证明合同不依赖 PATH 选择；
- 不通过无限重跑、降低格式要求或 `continue-on-error` 吸收失败。

## 4. 本机真实验证

| 门禁 | Expected | Actual | Difference |
|---|---|---|---|
| format 合同 | exact transient=true；other/success=false；最多 2 次 | `Pass`，三个布尔值与预期一致 | None |
| 真实 format | 使用兼容绝对 host，当前代码无格式差异 | `C:\Program Files\dotnet\dotnet.exe`，attempts=1，`transientRetryObserved=false / Pass` | None；本机没有伪造瞬态 |
| Release build | 0 warning / 0 error | 0 warning / 0 error | None |
| 完整测试 | 新合同进入套件且全部通过 | `1,395/1,395`、0 skipped，31 秒 | None |
| coverage | lines >=90%、branches >=75% | lines `90.43% (47090/52072)`；branches `76.16% (15458/20298)` | None |
| 漏洞 | 0 known vulnerable packages | Pass | None |
| Action / CodeQL 合同 | 固定 SHA、双语言与最小权限不变 | Pass，签名或分发访问 false | None |

本机真实 format 一次通过只能证明正常路径；首次 main run 已提供精确瞬态 Actual。最终出口是本阶段 PR 和合并后 main 的 GitHub Hosted Runner：无瞬态时一次通过；若瞬态复现，日志必须显示唯一一次 warning、第二次通过；若第二次或其他错误失败，门禁必须保持红色。

## 5. PR #316 首轮真实远端验证

精确提交 `13dbc92` 的 GitHub Hosted Runner 结果：

| 门禁 | Expected | Actual | Difference |
|---|---|---|---|
| Format | 绝对 host；正常路径一次通过；瞬态最多重试一次 | CI run `33350286859`：`C:\Program Files\dotnet\dotnet.exe`，attempts=1，`transientRetryObserved=false` | None；该次未出现上游瞬态 |
| 完整测试 | 1,395 项全部通过、0 skipped | `1,395/1,395`、0 skipped、29 秒 | None |
| coverage | lines >=90%、branches >=75% | lines `90.11% (46924/52072)`；branches `76.04% (15434/20298)` | None |
| 依赖风险 | 0 known vulnerable packages | 0，Pass | None |
| 许可证元数据 | 20 项目、30 包；未获 owner 批准前不允许分发 | Pass；`PendingOwnerReviewAndNotice`，`distributionApproved=false` | None；发布阻塞未被绕过 |
| 测试证据制品 | 上传 TRX 与 coverage | artifact `9743477715`，1,002,020 bytes | None |
| CodeQL | C# / C++ 均成功，分支 open alerts=0 | run `33350286860`：C# 7m02s、C++ 3m27s，均 Pass；open alerts=0 | None |

第一轮 PR 证明原 main 失败点已不再阻断格式门禁，且格式正常路径没有无意义重试。文档证据写回后的精确提交仍须再次通过 CI/CodeQL；合并后还须以精确 main 运行完成最终闭环。

## 6. 精确文档提交暴露的第二项真实差异

证据写回提交 `d0d6739` 的 CodeQL run `33350735630` 仍为 C# / C++ 全绿，但 CI run `33350735683` 在 Format 已越过原 main 失败点后，于完整测试中被 2 分钟 inactivity watchdog 终止：已通过 `1,289` 项，正在运行的 `NativeActivationSourceExposesFiniteInvokeAndHideRestoreContract` 未能退出原生菜单模态循环。失败附件 artifact `9743642026` 保留了 TRX、Cobertura 与 sequence；sequence 精确标记该测试 `Completed=false`。这不是本轮 format 实现失败，也不能用第一轮 PR 绿灯覆盖。

复审 Stage 201 与真实代码发现，菜单 UIA observer readiness 已有专用线程握手，但证据入口仍依靠菜单拥有线程自身的 Win32 `SetTimer/TIMERPROC` 调用 `EndMenu`；在 Hosted Runner 的 UIA/菜单模态消息路径中，该关闭信号仍可能失去有界性。修正前本机以 10 个独立 testhost 运行精确场景为 `10/10`，说明断言成立但无法复现远端调度差异。

修正后，只有内部 `ShowPendingContainerMenuForEvidence` 改为独立托管计时器：1.2 秒后向真实拥有窗口投递 `WM_CANCELMODE`，并在投递失败时硬失败；用户触发的正式产品菜单路径、菜单内容、UIA observer 和按钮 Invoke 断言均不改变。测试同时要求真实菜单生命周期位于 0.9～10 秒，避免再次无限占用 testhost。修正后同一场景再次以 10 个独立 testhost 执行，`10/10` 通过，且每次实际走新的关闭路径。

随后完整本机覆盖运行首次得到 `1,393/1,395`：两个真实缩略图 worker 场景分别在 1.5/2 秒预算附近返回 `TimedOut/FailedFallback`。既有 [统一计划](PRODUCT_EXECUTION_PLAN.md) 已记录 Release 编译服务残留会对真实 Worker/Shell 测试造成并发资源竞争；本次保留失败 TRX/Cobertura，不修改产品 1.5 秒预算。CI 在 Test 前新增 `dotnet build-server shutdown`，自动合同强制该步骤位于 `dotnet test` 之前。本机按相同顺序关闭 MSBuild 与 Roslyn server 后，完整套件为 `1,395/1,395`、0 skipped、37 秒；精确通过附件 coverage 为 lines `90.46% (23548/26032)`、branches `76.16% (7728/10147)`。把通过与中止附件混合聚合会得到无意义的 `88.07%/74.68%`，因此失败附件保留但不冒充同一运行的覆盖率。

## 7. 开发目标与需求对齐审计

开发目标审计：main format 宿主竞态、PR 原生菜单模态挂起、压力后缩略图资源竞争三项 Actual 均已保留。修正分别限定为 format 一次精确重试、证据菜单的真实窗口有限取消、真实进程测试前释放编译服务；没有降低格式、UIA、测试、覆盖率或产品缩略图预算。新精确提交的 PR 与合并后 main 验证仍 Pending。

需求对齐审计：本阶段是 CI 确定性修正，不修改 Long方格产品运行时、用户文件、Windows Runtime、任务栏、签名或分发。M1/M2 继续 `0/2 Complete`、30 项 PF 继续 `0 Complete`；下一产品接续点仍是 Stage 241 的 BOX-R1-C/D 与 M1 完整物理旅程，外部条件没有因本修正改变。
