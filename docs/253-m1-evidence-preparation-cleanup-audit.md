# Stage 253：M1 marker 后证据准备异常统一清理审计

日期：2026-08-31

输入基线：`origin/main@645261f548e5c8471e505348b4024eef2a2757dd`

状态：`Complete / PullRequestAndMainVerificationPass / ExternalEnvironmentBlocked`

## 1. 接续条件与开发目标

从 Stage 252 最终 main 重新执行真实准入：#23/#274 无更新，M1 ExternalAutomation 仍为 `BlockedByIncompleteRuntime / startsProcess=false / createsEvidenceSession=false`；TASKBAR Host 仍为 `Blocked / mutationAllowed=false`。因此 BOX-R1-C/D、正向 M1 和 TASKBAR-R2B1-B 继续停止，本阶段只处理实际代码中复现的证据生命周期质量缺陷。

Stage 247 已把产品启动、刷新与 Ready 等待统一纳入异常清理，但实际启动器仍在该 `try/catch` 之前创建配置目录、Unicode 夹具、精确 marker 和 `journey.json`。如果 marker 建立后任一准备写入失败，失败不经过 `Remove-EvidenceDirectory`，会留下可被误认为待处理会话的半成品目录。

## 2. Expected / Initial Actual / Difference / Correction

| 检查 | Expected | Initial Actual | Difference / Correction |
|---|---|---|---|
| marker 后准备失败 | 非零退出；本次 GUID/marker 目录清理；不启动产品 | 测试自有真实脚本副本在 marker 后注入写入异常，新增残留会话 `d8c4368244644664bca4025e4d34dbdb` | 将 marker 后的夹具、journey、启动和 Ready 等待纳入同一 catch |
| 清理所有权 | 只清理已建立精确 marker 的本次会话 | 旧准备阶段没有异常责任边界 | 新增 `$markerWritten`；仅为 true 时调用既有 GUID/path/root reparse-point/marker cleanup |
| marker 前失败 | 不得用递归 cleanup 掩盖原始磁盘/路径异常 | 若无条件扩大 catch，严格 cleanup 会因 marker 不存在再次抛错 | marker 前不调用 `Remove-EvidenceDirectory`；本阶段不宣称解决无法建立所有权标记的文件系统故障 |
| 相邻既有路径 | Stage 252 缺失 cleanup、Stage 247 启动失败、合法 cleanup 不回归 | 必须保留原合同 | 同一真实测试类 M1 相关 `5/5` 通过 |

修正前测试 finally 只删除自己发现且通过 32 字符 GUID 与精确 marker 复核的新增目录；未终止外来进程、未修改用户文件或系统设置。修正后同一故障注入返回原始有限错误，新增证据目录为 0，LongGrid PID 集合零差异。

## 3. 本机真实验证

| 门禁 | Expected | Actual | Difference |
|---|---|---|---|
| M1 相关真实子进程 | 准备失败、启动失败、缺失/合法 cleanup 与模式隔离均确定 | `5/5` | None |
| Locked restore / format / Release | 锁定依赖、零格式差异、0 warning/error | 全部通过；Release `0 warning / 0 error` | None |
| 完整测试 | 新回归进入全套 | `1,400/1,400`、0 skipped、20 秒 | None |
| Coverage | lines >=90%、branches >=75% | lines `90.43% (47,082/52,064)`；branches `76.17% (15,458/20,294)` | None |
| UI 合同 | 产品合同不退化 | ContractOnly `198` IDs，Pass | None |
| M1 Runtime | 不完整/不安全时零启动、零会话 | `BlockedByIncompleteRuntime / startsProcess=false / createsEvidenceSession=false` | None |
| 漏洞与许可证 | 漏洞 0；未批准前禁止分发 | 漏洞 0；20 项目/30 包；`PendingOwnerReviewAndNotice / distributionApproved=false` | None |

历史 `TestResults` 在最终 coverage 前被可恢复地移入 ignored artifacts，只聚合本轮唯一结果。没有安装 Runtime、启用 Sandbox、修改任务栏、发送物理输入、签名或分发产物。

## 4. 开发目标与需求对齐审计

开发目标审计：修正前残留、测试自有补偿清理、修正后零残留和相邻生命周期路径均已有真实子进程证据。清理责任以精确 marker 为边界，没有把未知或未取得所有权的目录纳入递归删除。

需求对齐审计：本阶段只修正 M1 内部证据准备失败的副作用，不修改正式产品功能、用户配置、桌面文件、任务栏适配器、Runtime、签名、安装或分发状态。M1/M2 继续 `0/2 Complete`，30 项 PF 继续 `0 Complete`。

下一唯一接续点仍由 `#23/#274`、完整兼容 Runtime、受保护签名包和独占可丢弃 Windows 会话共同约束；TASKBAR-R2B1-B 仍要求 Stage 216 Host/Guest 达到 `ReadyToLaunch / GuestReady`。两者未成立时，只处理新复现的真实回归、质量或安全缺陷。

## 5. 远端交付

精确提交 `0bc79b7b156524ea5c39507ee0c1c1686057f8aa` 已推送到短分支并创建 PR #332；PR 无评论、无 review，状态 `MERGEABLE`。首轮 CI run `33397308160` 通过：完整测试 `1,400/1,400`、0 skipped、32 秒，coverage lines `90.14% (46,932/52,064)`、branches `76.04% (15,432/20,294)`，198-ID、漏洞 0、20 项目/30 包、许可证继续 `PendingOwnerReviewAndNotice / distributionApproved=false`。artifact `9760096291`，1,003,430 bytes，digest `sha256:3ac698c60f3cd5d1c81823df7479264c606b7263641b7b8f408cedb8e20f92f4`。首轮 CodeQL run `33397308208` 的 C# 与 C++ 分析均通过。

记录首轮结果的最终 PR head `cf3c13ff712f0b0dbef2a6e3a87467c8de6272e5` 也已重新验证：CI run `33398153681` 与 CodeQL run `33398153641` 全部通过。PR #332 随后 squash 合并为 `main@0087e3479c01163ebbb82e5a1894d7ff060b8f8f`。

合并后的精确 main 再次通过 CI run `33398947213`：完整测试 `1,400/1,400`、0 skipped、29 秒，coverage lines `90.14% (46,932/52,064)`、branches `76.04% (15,432/20,294)`；198-ID 合同、漏洞 0、20 项目/30 包许可证门禁均通过，许可证状态仍为 `PendingOwnerReviewAndNotice / distributionApproved=false`。测试与覆盖率 artifact `9760730299`，1,002,990 bytes，digest `sha256:38e2725cd3e7818b60308a79226066920f0f27f09fff8bfa21b2a77d6d386728`。内部 RC 仍明确不可分发；portable digest `sha256:9c94dda79913da03777181d876eae2b1fce799bb2ce77e3f21a0e4989fe5c43b`，unsigned MSIX digest `sha256:72c87c9a5579128c5e86cc7e06721f16ad0871f95a71877d204048efcde11f66`，SBOM digest `sha256:2a66bbfb3743daf07dff6391952d131cf298036d12482435180ded27f6247c29`。

同一 main 的 CodeQL run `33398947202` 通过：C++ `3m47s`，C# `6m29s`。至此实现、测试、审计、PR、合并与 main 验证闭环完成；外部 Runtime、签名、Sandbox/Host 条件仍未改变，也未被本阶段错误标记为已完成。
