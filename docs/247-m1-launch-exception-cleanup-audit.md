# Stage 247：M1 启动异常统一清理与真实进程审计

日期：2026-08-31

输入基线：`origin/main@2a7c8119bcd05b20a91a005845e13b39d94e4d6d`

状态：`Complete / LocalPullRequestAndMainVerificationPass / PhysicalJourneyPending`

## 1. 接续条件

#23 与 #274 仍为 OPEN，最后更新时间分别为 `2026-08-28T03:51:37Z` 与 `2026-08-28T02:43:13Z`。本机真实 Runtime 仍为 Framework `2.4.0.0`、XAML `3.2.3.0`、Singleton `8002.4.0.0`，缺 Main.2 `>=2.3.1.0` 与 DDLM `2.3.1.0-x6`；ExternalAutomation 继续 `BlockedByIncompleteRuntime / startsProcess=false / createsEvidenceSession=false`。因此不启动 BOX-R1-C/D 或正向 M1，只接续 Stage 246 新 Ready 路径的异常生命周期审计。

## 2. Expected / Initial Actual / Difference / Correction

| 检查 | Expected | Initial Actual | Difference / Correction |
|---|---|---|---|
| Start-Process 抛异常 | 非零退出；启动器自行删除刚创建的配置、夹具、journey 和 marker；不影响既有 LongGrid 进程 | 真实 Windows PowerShell 5.1 子进程以独立临时源码树和无效 `LongGrid.App.exe` PE 启动；exit 非 0，但新增 M1 会话 `3cc2f0d6349d46b4a148f808bee726f3` | 创建证据后只有环境变量恢复位于 finally，Start-Process 异常绕过窗口未就绪清理；把创建会话后的启动、刷新与 Ready 等待统一放入异常边界 |
| 已取得自有进程句柄后的异常 | 只允许终止本次启动的进程句柄，然后按精确 GUID、marker 与非 reparse-point 合同清理 | 旧代码只在 `productWindowReady=false` 的显式分支回收，其他异常没有统一责任边界 | catch 仅处理本次 `$process`；仍沿用 `Remove-EvidenceDirectory` 的路径/marker/reparse-point 保护，不枚举或终止既有 LongGrid |
| 修正后同一真实输入 | 仍非零退出，但证据目录差 0、进程集合差 0、stdout 为空且 stderr 明确失败 | 新增真实回归通过；完整套件结束后证据目录数为 0，既有 LongGrid PID 未被测试处置 | Difference=`None` |

第一次尝试以终端内联包装器建立夹具时，命令被本机执行策略在进程创建前拒绝；没有创建夹具、启动产品或删除内容，该结果不作为产品证据。随后改用 xUnit 中的真实 Windows PowerShell 子进程回归：它复制真实启动器与 dotnet 解析脚本、创建无效 PE，并在测试 finally 中只清理自己发现且通过 GUID/marker 复核的 Initial Actual 遗留目录。修正后的同一测试不再需要补偿清理。

## 3. 本机真实验证

| 门禁 | Expected | Actual | Difference |
|---|---|---|---|
| 启动异常真实回归 | 修正前失败、修正后通过 | Initial `0/1`，修正后相关 `4/4` | 从 1 个新增证据目录收敛为 0 |
| M1 ValidateOnly | 合同 Pass、零启动 | `Pass / startsProcess=false` | None |
| Format | 无格式差异 | 绝对 SDK host，attempts=1、无 retry | None |
| Release build | 0 warning / 0 error | 0 warning / 0 error | None |
| 完整测试 | 新回归进入全套 | `1,398/1,398`、0 skipped、39 秒 | None |
| Coverage | lines >=90%、branches >=75% | lines `90.46% (47096/52064)`；branches `76.16% (15456/20294)` | None |
| UI 合同 | 产品合同不退化 | ContractOnly `198` IDs，Pass | None |
| 依赖与分发 | 漏洞 0；未批准前禁止分发 | 漏洞 0；20 项目/30 包；`PendingOwnerReviewAndNotice / distributionApproved=false` | None |
| 当前 Runtime 准入 | 不完整/不安全时零启动、零会话 | `BlockedByIncompleteRuntime / startsProcess=false / createsEvidenceSession=false` | None |

覆盖率前确认 `TestResults` tracked files=0，只清理工作区内该生成目录并生成唯一 coverage；没有复用历史结果或降低阈值。

## 4. PR #324 与 main 真实远端验证

精确提交 `b7f6739` 的 CI run `33365941484` 与 CodeQL run `33365941661` 均成功：完整测试 `1,398/1,398`、0 skipped、29 秒；coverage lines `90.14% (46932/52064)`、branches `76.04% (15432/20294)`；漏洞 0；许可证继续 `PendingOwnerReviewAndNotice / distributionApproved=false`；artifact `9748470366`、1,002,241 bytes；C# / C++ CodeQL 成功。PR 无评论且 mergeable，Difference=`None`。

PR #324 已 squash 合并为 `main@2b70a3ff8343cc414c9d9269a8df53503a24b807`。该精确 main 的 CI run `33366600792` 与 CodeQL run `33366600748` 均成功：完整测试 `1,398/1,398`、0 skipped、29 秒；coverage lines `90.14% (46932/52064)`、branches `76.04% (15432/20294)`；漏洞 0；许可证和分发继续失败关闭；artifact `9748697558`、1,003,585 bytes；C# / C++ CodeQL 成功且 main open alerts=`0`。Difference=`None`。

文档收口 PR #325 首轮 CI run `33367422933` 在相同 `1,398` 项中的既有 `RealWorkflowPersistsControllerSnapshot` 失败：保存与复读断言已经完成，但测试自有 GUID 临时目录立即递归删除时，runner 报 `configuration.json.lock` 被另一进程使用；其余 `1,397` 项通过。Expected 为已完成的异步生命周期先显式释放，临时 Windows 清理占用只允许有界收敛，持续占用仍失败。测试现以 `await using` 显式释放控制器，并只对该测试自有目录的 IOException/UnauthorizedAccess 执行最多 `40×50ms` 删除重试；其他异常和最终一次失败不捕获。该修正沿用 Stage 213 已验证的 Windows runner 清理边界，不修改产品锁、保存或超时语义。本机目标用例连续 `20/20`、完整 `1,398/1,398`、coverage `90.46%/76.16%`、Release 0 warning/error、漏洞 0，Difference=`None`；修正后的 PR/main 结果待新提交验证。

## 5. 开发目标与需求对齐审计

开发目标审计：已关闭“证据目录创建后、Ready 判断前发生异常会遗留隔离配置与夹具”的生命周期缺口；真实失败复现、同输入修正复测、已有相邻路径、完整本机门禁、PR head 与合并后精确 main 门禁均通过。

需求对齐审计：修正只收口 M1 内部证据启动器自己的失败副作用，不安装 Runtime、不调用 UIA、不发送输入、不终止既有进程，也不改变签名、安装或分发权限。M1/M2 继续 `0/2 Complete`、30 项 PF 继续 `0 Complete`。

下一唯一接续点仍是完整兼容 Runtime、受保护签名包和独占可丢弃 Windows 会话中的 BOX-R1-C/D 与 M1 物理旅程；在外部条件到位前，不再用本机负向启动替代正向产品验收。
