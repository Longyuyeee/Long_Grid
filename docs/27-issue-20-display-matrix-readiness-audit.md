# Issue #20 动态显示与会话矩阵就绪审计

审计日期：2026-08-03

基线：`main` / `853a606` + 当前 Issue #20 短生命周期分支

结论：**Ready to execute / Pending manual evidence / 不得关闭 Issue #20**

## 1. 已有能力

现有 P0-07b2b2b2b4b1 observer 已通过 baseline、无事件防假阳性、隐藏消息窗口生命周期、稳定采样、脱敏事件分类和资源闭环验证。它支持 `scale`、`rotate`、`attach`、`detach`、`projection`、`sleep-resume`、`lock-unlock` 和 `remote-session`。

该 observer 不调用显示、设备、电源、锁屏或 RDP 修改 API。所有真实变化必须由操作员在进程外执行。

## 2. 本阶段补齐

- 统一 I20-01–I20-08 与底层 observer 枚举的映射；
- I20-03 强制区分 Attach/Detach，避免只测单向热插拔；
- 正常执行强制匿名操作员、受控环境和恢复计划确认；
- 固定 `observerPassIsFinalPass=false` 与 `PendingManualEvidence`；
- CI 只校验启动链和安全合同，不执行显示或会话变化；
- 运行手册明确每个场景的人工视觉、输入和恢复复核。

## 3. 证据分层

| 层级 | 可判定内容 | 不能替代 |
|---|---|---|
| `-ValidateOnly` | 参数、依赖、映射与安全合同存在 | observer 或人工场景执行 |
| observer | 公开事件、稳定采样、最终状态、资源数据 | 视觉布局、输入 Region、焦点与用户体验 |
| 人工复核 | 变化与恢复后的视觉、输入和系统状态 | 缺失的公开事件或未执行场景 |

## 4. 尚未完成

- I20-01–I20-08 尚未在受控硬件/会话环境逐项执行；
- I20-03 仍需 Detach 和 Attach 两次独立采证；
- I20-04 的每种投影模式、I20-07 的本地/RDP 往返和 I20-08 的混合 DPI 实机仍 Pending；
- Windows/架构/GPU 范围仍等待 Issue #23 负责人决策；
- 自动 baseline 或 CI 通过不得勾选 ADR-0001 的动态矩阵条目。

## 5. 下一动作

按[Issue #20 运行手册](manual-testing/issue-20-dynamic-display-session-runbook.md)一次执行一个变化和恢复，将脱敏 observer 输出、人工结论、缺陷及恢复确认写回 Issue #20。全部场景完成前，路线图中的 P0-07b2b2b2b4b2 保持未勾选。
