# Stage 257：M1 精确 marker 内容清理安全审计

日期：2026-09-01

输入基线：`origin/main@5142e37033c1c3909b63267545d19216e9a64bd9`

状态：`Complete / PullRequestAndMainVerificationPass / ExternalEnvironmentBlocked`

## 1. 接续与真实差异

从 Stage 256 最终 main 复读 PRD、统一计划和实际 cleanup。#23/#274 无更新；M1 仍为 `BlockedByIncompleteRuntime / startsProcess=false / createsEvidenceSession=false`，TASKBAR Host 仍为 `Blocked / mutationAllowed=false / modifiedSystemState=false`，正向产品旅程未准入。

cleanup 声称要求精确 `.longgrid-m1-session` marker，却对原文 `.Trim()` 后比较。隔离测试创建普通 GUID 会话、内容为 ` GUID ` 的非产品 marker 和 sentinel；修正前真实 exit 0 并删除整个目录，精确失败为 `Expected: Not 0 / Actual: 0`。

## 2. 修正与边界

| 检查 | Expected | Initial Actual | Correction |
|---|---|---|---|
| 带空白 marker | 非零拒绝，目录/sentinel/PID 不变 | exit 0，目录被递归删除 | marker 原文 `-ceq` 规范化目录 GUID，不 Trim |
| 合法 marker | 继续可清理 | 既有合同 | 相邻 M1 真实进程专项 `8/8` |
| 删除权限 | 不扩大 | 已有 GUID/root/reparse/marker 边界 | 只收紧所有权令牌内容比较 |

修正没有改变产品配置、用户文件、Runtime、任务栏、签名、安装或分发状态。M1/M2 继续 `0/2 Complete`，30 项 PF 继续 `0 Complete`，内部 RC 继续不可分发。

## 3. 验证与接续

| 门禁 | Actual |
|---|---|
| M1 相邻真实进程 | `8/8` |
| restore / format / Release | Pass；format attempts=1；`0 warning / 0 error` |
| 完整测试 | `1,403/1,403`、0 failed、0 skipped、17 秒 |
| Coverage | lines `90.43% (23,542/26,032)`；branches `76.17% (7,729/10,147)` |
| UI / 接续源 | 198 IDs；Stage 257 freshness 与 Action pins Pass |
| 依赖 | 漏洞 0；20 项目/30 包；`PendingOwnerReviewAndNotice / distributionApproved=false` |

覆盖率使用独立 ignored `artifacts/stage257-test-results`。

## 4. 远端交付与 main 验证

| 对象 | 可迁移证据 |
|---|---|
| 实现 PR | [#340](https://github.com/Longyuyeee/Long_Grid/pull/340)，head `ea2f97192983765d6253d082b5a73a3004ffa543`，无 review/comment 遗留，MERGEABLE/CLEAN，squash 合并为 `12eb0b753aefda106e4c062053c2fb15c6f78dde` |
| PR CI | run `33419826323` / job `99579205039` Success；`1,403/1,403`；lines `90.14% (46,932/52,064)`；branches `76.04% (15,432/20,294)` |
| PR CodeQL | run `33419826361` Success；C# job `99579205972`；C++ job `99579206082` |
| PR 构件 | artifact `9768706656`，1,003,450 bytes，SHA-256 `c43efd735ad2123287f5654dd94515e6bc304f31bfbfde45bf6a79a40449ce1d` |
| main CI | run `33420634582` / job `99581845156` Success，7m43s；`1,403/1,403`、0 failed、0 skipped、31s |
| main coverage / UI | lines `90.14% (46,930/52,064)`；branches `76.04% (15,432/20,294)`；198 required Automation IDs |
| main 依赖 / 许可证 | 已知漏洞 0；20 项目/30 包；`PendingOwnerReviewAndNotice / distributionApproved=false` |
| main 构件 | artifact `9768996063`，1,003,972 bytes，SHA-256 `7d76e71ca02491e1e1acb32a3de8fd4cc1471a2176297e1e54f9c0baf6e51664` |
| main CodeQL | run `33420634564` Success；C# job `99581845110`；C++ job `99581845321` |

下一唯一接续点仍由 `#23/#274`、完整兼容 Runtime、受保护签名包、独占可丢弃 Windows 会话或 Stage 216 TASKBAR Host/Guest 准入持有；未满足时只处理新复现的真实回归、质量或安全缺陷。
