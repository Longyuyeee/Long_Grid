# Stage 252：M1 不存在会话清理与任务栏测试宿主纠偏审计

日期：2026-08-31

输入基线：`origin/main@1dcea5ce7745b8d9bee55c0655ccd952d3a047ed`

状态：`Complete / PullRequestChecksPass / MergePending / ExternalEnvironmentBlocked`

## 1. 接续条件与开发目标

按 Stage 251 在新电脑重新取得事实：main 与 origin/main 一致，M1 Runtime 仍为 `BlockedByIncompleteRuntime`，ExternalAutomation 为 `startsProcess=false / createsEvidenceSession=false`；TASKBAR Host 仍为 `Blocked / mutationAllowed=false`。因此不启动 BOX-R1-C/D、M1 正向旅程或 TASKBAR-R2B1-B，只允许处理新复现的质量、安全或回归缺陷。

本阶段在复读 `Start-LongGridM1ManualEvidenceSession.ps1` 的实际 cleanup 代码时发现：传入格式正确但根本不存在的 session GUID，脚本会跳过目录、marker 和 reparse-point 校验，却仍输出 `Pass / removed=true`。这会把“目录本来不存在”误写成“已验证清理”，偏离 Stage 245 固定的精确证据生命周期合同。

## 2. Expected / Initial Actual / Difference / Correction

| 检查 | Expected | Initial Actual | Difference / Correction |
|---|---|---|---|
| 不存在 M1 session cleanup | 非零退出；不得声称删除；进程和证据目录集合不变 | 随机不存在 GUID `135a8f5f82d04da68a517bc5becb8a3e` 返回 exit 0、`Pass / removed=true` | `Remove-EvidenceDirectory` 先要求精确目录实际存在，再执行既有 root reparse-point、精确 marker 和删除后不存在检查 |
| 合法 cleanup | 带精确 marker 的真实 GUID 目录仍可删除 | 既有 Stage 245 路径必须保持 | 原有真实子进程测试继续断言 `Pass / removed=true` 和目录消失 |
| 缺失 cleanup 副作用 | 不启动或终止 LongGrid，不创建/删除其他证据 | 修正前进程与集合虽不变，但结果语义错误 | 新增真实 Windows PowerShell 子进程回归，断言非零退出、精确有限错误、目录与 PID 集合零差异 |
| 完整套件真实进程 | x64 测试程序集必须由确定的 x64 dotnet host 启动 | 首轮 `1,398/1,399`；任务栏恢复子进程 10 秒内未创建 readiness；独立连续 `0/10` | PATH 首项是 x86 dotnet；同一子命令使用 Program Files x64 host 约 2 秒产生 journal/lock/readiness。测试固定 x64 host，10 秒预算不变 |

## 3. 本机真实验证

| 门禁 | Expected | Actual | Difference |
|---|---|---|---|
| M1 cleanup 专项 | 缺失目录失败关闭；真实 marker 目录正常清理 | `2/2` 通过 | None |
| 任务栏恢复真实子进程 | 不依赖 PATH 架构顺序；不放宽预算 | 修正前连续 `0/10`；修正后连续 `10/10`，单轮测试约 0.9～1.0 秒 | 固定 x64 SDK host；仍为 10 秒测试预算 |
| Locked restore / format / Release build | 可复现、零格式差异、0 warning/error | 全部通过；Release `0 warning / 0 error` | None |
| 完整测试 | 新回归进入全套，首次差异修正后全绿 | 首轮 `1,398/1,399`；修正后 `1,399/1,399`、0 skipped、13 秒 | None |
| Coverage | lines >=90%、branches >=75% | lines `90.43% (47,084/52,064)`；branches `76.17% (15,458/20,294)` | None |
| UI 合同 | 产品合同不退化 | ContractOnly `198` IDs，Pass | None |
| M1 Runtime | 不完整/不安全时零启动、零会话 | `BlockedByIncompleteRuntime / startsProcess=false / createsEvidenceSession=false` | None |
| 漏洞与许可证 | 漏洞 0；未批准前禁止分发 | 漏洞 0；20 项目/30 包；`PendingOwnerReviewAndNotice / distributionApproved=false` | None |

历史 `TestResults` 在执行前被可恢复地移入 ignored artifacts，首次失败结果也单独保留，最终 coverage 只聚合一份修正后的结果。任务栏诊断子进程只写测试自有临时目录，终止后目录移入 ignored artifacts；未修改系统任务栏、Runtime、Sandbox、签名、用户文件或产品配置。

## 4. 开发目标与需求对齐审计

开发目标审计：不存在的 M1 证据目录现在不能再产生伪 `removed=true`；合法精确会话清理保持可用。完整套件发现的第二个差异不是用重跑或增加超时掩盖，而是以 `where dotnet`、连续失败和显式 x64 host 正向对照定位到测试宿主架构漂移，并在不改变时间预算的前提下连续复测。

需求对齐审计：本阶段只修正 M1 内部证据声明准确性与测试 harness 的 SDK host 选择，不修改正式产品功能、任务栏适配器、用户文件、Runtime、Sandbox、签名、安装或分发状态。M1/M2 继续 `0/2 Complete`，30 项 PF 继续 `0 Complete`。

下一唯一接续点仍由 `#23/#274`、完整兼容 Runtime、受保护签名包和独占可丢弃 Windows 会话共同约束；TASKBAR-R2B1-B 仍要求 Stage 216 Host/Guest 达到 `ReadyToLaunch / GuestReady`。两者未成立时，继续只处理新复现的真实回归、质量或安全缺陷。

## 5. 远端交付

精确提交 `ee0cd44281590e85f31e4a4e483b9770e6ac6ef7` 已推送到短分支并创建 PR #330；PR 无评论、无 review，状态 `MERGEABLE`。CI run `33392409104` 通过：完整测试 `1,399/1,399`、0 skipped、28 秒，coverage lines `90.14% (46,932/52,064)`、branches `76.04% (15,432/20,294)`，198-ID、漏洞 0、20 项目/30 包、许可证继续 `PendingOwnerReviewAndNotice / distributionApproved=false`。测试与覆盖率 artifact `9758233048`，1,003,397 bytes，digest `sha256:d021e9779fee2a646216445f6d235e61c03aaa3d88b4ab452522d4ae7ed6317e`。

CodeQL run `33392409167` 的 C# 与 C++ 分析均通过。本文记录上述精确远端结果后再推送文档收口提交；最终 PR head 和合并后 main 仍须重新通过各自检查，未完成前不把 main 写成已交付。
