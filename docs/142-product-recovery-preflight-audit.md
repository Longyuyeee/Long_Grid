# Stage 142：正式产品故障恢复预检审计

- 审计日期：2026-08-14
- 开发基线：`main@7a60ab4e0d18f3fad876f82dd30d5ed50ad96726`
- 切片：M4b1
- 当前判定：**本地 Engineering Pass / 远端证据 Pending**

## 1. 需求对齐与切片边界

M4b 的原始目标是覆盖配置损坏、目录不可用、Explorer 生命周期、显示器变化、取消/重试和进程恢复。审计确认这些风险跨越持久化和原生窗口两个边界，因此拆成可独立回滚的两个子切片：

- **M4b1（本切片）**：配置主/备份损坏、目录清单暂不可用、取消、显式重试和新存储实例模拟进程重启；
- **M4b2（下一切片）**：Explorer 重启、会话失效、显示器拓扑变化和 DesktopHost Surface 释放/重建。

本切片不注入真实 Explorer 或显示器故障，不读取、枚举、创建、移动或删除用户桌面文件，不把合成预检冒充真实设备、人工证据或发布门禁。

## 2. 五场景验收矩阵

| 场景 | 故障注入 | 必须满足的有限结果 |
| --- | --- | --- |
| 备份接管与重启 | 保存两个有效版本后损坏 primary，并创建新的 store 实例 | 首次为 `RecoveredFromBackup`；确认后归档损坏 primary，备份成为可写 primary |
| 安全模式重置与重启 | primary、backup 同时损坏，并创建新的 store 实例 | 首次为 `SafeMode`；确认后归档两份证据并发布空默认配置 |
| 目录暂不可用后恢复 | 带一个合成引用的有效配置先接收 `Unavailable`，再接收权威目录 | 不可用时为 `AwaitingCatalog` 且不误判缺失；恢复后为 `Ready` 且恰好解析一项 |
| 显式重试 | 损坏 primary 导致保存失败，修复边界后调用 `RetryAsync` | 首次为可重试的 `DamagedEvidence`；重试成功且候选配置可复读 |
| 取消 | 写租约受阻时取消已接受的保存 | 抛出有限取消；不得留下可由 UI 再次提交的含糊 retry intent |

每次运行均使用随机临时配置沙箱。成功返回只包含场景计数、安全布尔值和结果；不输出路径、文件身份或内容。任一断言失败返回非零，finally 仍尝试删除沙箱。

## 3. 实现与门禁

- `ProductWorkspaceRecoveryPreflight` 直接复用正式 `ProductConfigurationStore`、`ProductConfigurationSaveWorkflow` 和 `ProductWorkspaceSessionLoader`；
- `LongGrid.Tools.ProductRecoveryPreflight` 提供无参数 JSON 命令行入口，成功必须返回 5/5 场景和三个安全边界布尔值；
- `ProductWorkspaceRecoveryPreflightTests` 把同一正式入口纳入测试套件，避免工具与测试出现两套语义；
- CI 在 500 项规模预检后新增独立 `Product recovery fault preflight`，后续变更必须持续复验。

## 4. 本地审计结果

- 目标工具 Release 构建：0 warning / 0 error；
- 新增专项测试：1/1；
- 独立工具：`Passed`，5/5 场景全部为 true；
- `temporarySandboxCleaned=true`、`readsRealDesktop=false`、`realFileOperationsAllowed=false`；
- 全量测试、覆盖率、包审计、内部 unsigned RC 集与干净 Windows 构建以 PR runner 为最终权威。

## 5. 风险与未完成项

- 新实例验证的是持久化跨实例语义，不是强制终止真实 App 进程；真实崩溃/专用卷三阶段仍由 Issue #24 证据门负责；
- 取消场景保证没有含糊的 UI 重试意图，但已进入串行保存协调器的请求仍按既有完成语义收敛；
- Explorer、session、显示器拓扑和原生 Surface 故障尚未进入本切片，不能宣称 M4b 完成；
- M4c 24 小时资源长稳、#19/#20/#23/#24、ADR、许可证、签名和安装生命周期继续 Pending。

## 6. 远端轨迹与下一步

- 实现 PR / head SHA：Pending；
- PR CI / 合并 commit / main CI：Pending；
- 合并前判定：本地 Engineering Pass / 远端证据 Pending；
- 下一步：完成 PR 与 main 双重流水线复验后关闭 M4b1，再从最新 `main` 进入 M4b2 原生生命周期故障矩阵。
