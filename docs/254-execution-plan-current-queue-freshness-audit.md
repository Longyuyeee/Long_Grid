# Stage 254：统一执行计划当前队列新鲜度合同审计

日期：2026-08-31

输入基线：`origin/main@dd672af8c1389a66f75c316a87484e54dfe4f07c`

状态：`Complete / LocalVerificationPass / PullRequestPending / ExternalEnvironmentBlocked`

## 1. 接续条件与开发目标

从 Stage 253 最终 main 重新执行真实准入。#23 仍为 OPEN，最后更新 `2026-08-28T03:51:37Z`；#274 仍为 OPEN，最后更新 `2026-08-28T02:43:13Z`，没有新增许可证、Publisher、托管签名或安装授权。Runtime 预检仍为 Framework `2.4.0.0` / XAML `3.2.3.0`，缺 Main.2 `>=2.3.1.0` 与 DDLM `2.3.1.0-x6`；M1 ExternalAutomation 返回 `BlockedByIncompleteRuntime / startsProcess=false / createsEvidenceSession=false`。当前电脑为 Windows `10.0.22621.4317`；TASKBAR Host 生成并复读安全 `.wsb` 后仍因 `WindowsSandboxLauncherMissing / HardwareEvidenceUnavailable` 返回 `Blocked / mutationAllowed=false / modifiedSystemState=false`。

两条产品入口均未准入，因此本阶段只处理实际复现的执行源质量缺陷。统一计划页头已经声明 Stage 253 是最新质量修正，但第 9 节“当前唯一执行队列”仍把 Stage 249 写成“最新精确接续条件”。现有 freshness 合同只检查计划页头、README、Stage 153 backlog 与路线图，没有检查计划自身的当前队列，因而错误返回 Pass。

## 2. Expected / Initial Actual / Difference / Correction

| 检查 | Expected | Initial Actual | Difference / Correction |
|---|---|---|---|
| 计划页头与当前队列 | 两处必须引用同一最新 Stage/audit | 页头 Stage 253，当前队列 Stage 249 | 当前队列改为 Stage 253 |
| freshness 正向合同 | 计划自身不得包含过期执行入口 | 旧合同在真实漂移上返回 Pass | 从计划第 9 节结构化定位 BOX/M1 与 TASKBAR 双入口并要求最新相对链接 |
| freshness 负向合同 | 内存回退必须给出确定性差异 | 旧合同只测试 README 回退 | 新增计划 Stage 252 回退，固定得到 `execution-plan-current-queue:audit-expected=253-actual=252` |
| 宿主兼容 | Windows PowerShell 5.1 与 PowerShell 7 一致 | 新规则未验证 | 两个真实宿主均 Pass；脚本继续保持纯 ASCII |

增强合同应用于修正文档前时真实非零退出，精确报告 `execution-plan-current-queue:audit-expected=253-actual=249`；修正后同一入口通过。Stage 248/249 仍作为历史 Runtime 与准入事实保留，不被改写成当前来源。

## 3. 本机真实验证

| 门禁 | Expected | Actual | Difference |
|---|---|---|---|
| Freshness / Action pins | 正向一致，README 与计划负向回退均拒绝 | Windows PowerShell 5.1、PowerShell 7、Action-pin 聚合入口全部 Pass | None |
| Locked restore / format / Release | 锁定依赖、零格式差异、0 warning/error | 全部通过；format attempts=1；Release `0 warning / 0 error` | None |
| 完整测试 | 当前基线全部执行 | `1,400/1,400`、0 failed、0 skipped、20 秒 | None |
| Coverage | lines >=90%、branches >=75% | lines `90.42% (47,078/52,064)`；branches `76.16% (15,456/20,294)` | None |
| UI / M1 静态合同 | 正式产品合同不退化且不启动产品 | UI ContractOnly `198` IDs；M1 ValidateOnly Pass、`startsProcess=false` | None |
| 漏洞与许可证 | 漏洞 0；未批准前禁止分发 | 漏洞 0；20 项目/30 包；`PendingOwnerReviewAndNotice / distributionApproved=false` | None |

历史 `TestResults` 在本轮覆盖率前被可恢复地移入 ignored artifacts，只聚合唯一结果。准入检查没有安装 Runtime、启用 Sandbox、修改任务栏、启动 LongGrid、创建 M1 会话、签名、安装或分发产物。

## 4. 开发目标与需求对齐审计

开发目标审计：真实计划内部漂移已从一次性文字纠正升级为可重复、跨 PowerShell 宿主且带确定性负向场景的只读门禁。修正范围只覆盖权威当前队列，不再增加 M1 邻接探针或任务栏 mock。

需求对齐审计：本阶段不修改产品运行时、用户文件、Runtime 包、Sandbox、任务栏适配器、签名或安装权限。M1/M2 继续 `0/2 Complete`，30 项 PF 继续 `0 Complete`，内部 RC 继续不可分发。

下一唯一接续点不变：`#23/#274`、完整兼容 Runtime、受保护签名包和独占可丢弃 Windows 会话同时具备后执行 BOX-R1-C/D 与 M1；或 Stage 216 Host/Guest 达到 `ReadyToLaunch / GuestReady` 后只在 Guest 执行 TASKBAR-R2B1-B。两者均未成立时，只处理新复现的回归、质量或安全缺陷。

## 5. 远端交付

当前等待本轮短分支 PR 的精确提交、CI、CodeQL、artifact、合并后 main 与最终文档收口结果。远端证据未完成前不得把本阶段写成已合并。
