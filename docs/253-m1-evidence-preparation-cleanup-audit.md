# Stage 253：M1 marker 后证据准备异常统一清理审计

日期：2026-08-31

输入基线：`origin/main@645261f548e5c8471e505348b4024eef2a2757dd`

状态：`LocalComplete / PullRequestPending / ExternalEnvironmentBlocked`

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

本节在短分支、PR、CI/CodeQL 和精确 main 验证完成后补充；在此之前不得把本地通过写成远端完成。
