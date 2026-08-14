# Stage 144：加速资源长稳预检审计

- 审计日期：2026-08-14
- 开发基线：`main@5b23198f1a1574f5500be017b723120417723de2`
- 切片：M4c1
- 当前判定：**Accelerated Engineering Pass（本地）/ 远端证据 Pending / 24 小时实机 Pending**

## 1. 需求对齐与拆分

M4c 原目标是验证 DesktopHost Surface、UIA、目录观察/刷新、缩略图 worker 与正式 App 长时间运行时没有持续的内存、句柄、线程、窗口或状态增长。审计确认，几分钟 CI 只能验证确定性的资源所有权和释放合同，不能替代真实 App 的 24 小时趋势证据，因此拆分为：

- **M4c1（本切片）**：加速生命周期 churn，验证 Surface、目录控制器、系统表面状态机和订阅通知成对收敛；CI 必须继续执行既有缩略图 worker Job/Profile/孤儿清理门；
- **M4c2（待执行）**：在支持设备上运行正式 App 至少 24 小时，采集 private bytes、handle、thread、产品窗口、worker/Profile 和状态修订趋势，并按事先冻结的预算判定。

M4c1 只允许输出 `AcceleratedPass`，报告必须同时输出 `realApp24HourSoakRequired=true` 与 `real24HourEvidenceCollected=false`；不得据此宣称 M4-ready。

## 2. 加速矩阵与验收

| 范围 | 循环与动作 | 验收 |
| --- | --- | --- |
| DesktopHost lifecycle/UIA 合同 | 200 轮：单屏 Ready → Explorer fail-closed/恢复 → topology refreshing → 双屏 Ready → dispose | 每轮创建 3 个合成 Surface；旧代际立即释放；Ready 必须有只读可访问性和 Passive 证明；最终 600/600 释放 |
| 目录控制器 | 200 个控制器，每个 3 次权威空目录刷新 | 共 600 次刷新、1200 次 `Refreshing/Ready` 通知；每个 generation 精确为 3，退订后 dispose |
| 系统表面分类器 | 200 个独立状态机：安全基线 → FocusLost → 两个安全样本 | 每轮只产生一次 `FocusLost`，第二个安全样本才产生一次 `RecoveryCandidate`，无状态漂移 |
| 缩略图 worker | 复用紧随其后的既有 CI `Thumbnail worker isolation probe` | AppContainer、Job kill-on-close、硬超时恢复、孤儿/Profile/ACL 清理与 500 请求预算继续强制通过 |

所有自动循环均为内存合成身份，不创建 HWND、不读取真实桌面、显示器或文件；输出不含容器、项目、路径、句柄或机器身份。

## 3. 为什么不测进程内存阈值

短 CI 中的 `GC.GetTotalMemory`、private bytes 或 handle 单点差值会混入 JIT、程序集加载、Shell/COM 初始化、测试运行器和 GC 时机，既可能误报，也可能掩盖缓慢泄漏。本切片只断言代码可证明的所有权计数和最终释放，不用一次加速运行伪造 24 小时资源 SLA。

M4c2 必须在正式 App、真实 HWND、UIA provider、目录来源和 worker 启用的支持设备上，以固定采样周期记录趋势；阈值必须在运行前进入文档，不能看见结果后放宽。

## 4. 实现与门禁

- 新增 `ProductResourceStabilityPreflight`，复用正式 DesktopHost lifecycle、目录 controller 与系统表面 classifier；
- 新增 `LongGrid.Tools.ResourceStabilityPreflight` JSON 入口和同入口单元测试；
- 任一计数、代际、状态、通知或最终释放不匹配均返回非零；
- CI 在 M4b2 恢复预检后新增 `Accelerated resource stability preflight`，缩略图 worker 隔离探针仍为独立后继硬门。

## 5. 本地结果

- 目标工具 Release 构建：0 warning / 0 error；
- 独立工具：`AcceleratedPass`；
- lifecycle/catalog/classifier：200/200/200 轮；
- Surface：600 created / 600 released；目录：600 refreshes / 1200 notifications；
- `systemEventStateRecoveredEveryIteration=true`、`allOwnedResourcesReleased=true`；
- `thumbnailWorkerIsolationGateRequired=true`、`realApp24HourSoakRequired=true`、`real24HourEvidenceCollected=false`；
- 完整测试、覆盖率、worker 探针与内部 unsigned RC 交付集以 PR runner 为权威。

## 6. 远端轨迹与下一步

- 实现 PR / head SHA：Pending；
- PR CI / 合并 commit / main CI：Pending；
- 合并前判定：M4c1 本地 Accelerated Engineering Pass；M4c、M4-ready 与 RC 均保持 Pending；
- 下一步：完成 PR/main 双重门禁并关闭 M4c1，再冻结 M4c2 24 小时正式 App 采样入口、预算、证据格式和停止条件。
