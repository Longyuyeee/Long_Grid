# Stage 143：DesktopHost 原生生命周期恢复预检审计

- 审计日期：2026-08-14
- 开发基线：`main@9f921471c32edaa870ab54db5088414b23ad080b`
- 切片：M4b2
- 当前判定：**M4b2 Engineering Pass / M4b Engineering Pass**

## 1. 需求对齐与现状审计

M4b2 承接 Stage 142 明确留下的原生生命周期风险：Explorer 重启、session 暂不可用、显示器拓扑不可用/变化，以及 DesktopHost Surface 和窗口注册身份的释放、恢复与代际隔离。

现有代码已经分别具备 `ProductDesktopHostLifecycleController`、`ProductDisplayTopologyController`、系统表面事件和 `ProductDesktopHostWindowBridge`，单项测试也覆盖了隐藏/恢复、拓扑刷新和 Host 重启。但此前没有一个正式入口把它们串成“故障 → fail-closed → 恢复 → 旧身份拒绝 → 资源全释放”的 CI 组合门禁。本切片只补该门禁，不重写既有控制器。

## 2. 五场景验收矩阵

| 场景 | 注入与边界 | 必须满足的结果 |
| --- | --- | --- |
| Explorer 重启 | 向已 Ready 的正式 lifecycle 注入 `ExplorerRestarted`，再注入更高序列 `RecoveryCandidate` | Surface 转 Hidden、状态为 `SuspendedSystemSurface`；恢复后回到 `ReadyReadOnly` 和 Passive |
| session 不可用 | 在同一单调事件链注入 `SessionUnavailable` 后恢复 | 同样 fail-closed；不得遗留 Explicit 或继续对外声明 Passive |
| 拓扑不可用后恢复 | 正式 topology controller 先返回 `Unavailable`，后返回双显示器 `Ready` | 旧 Surface 被释放、拥有窗口数归零；新一代权威拓扑才允许重新创建 Surface |
| 显示器替换 | 单显示器基线替换为双显示器新代际 | 旧 Surface 已释放，恰好两个新 Surface 存活，无代际混用 |
| Host 重启 | Window bridge 从 Host generation 1 切换到 2 | 注册表清空；旧 claim 返回 `HostMismatch`，新 claim 成为唯一已验证窗口 |

## 3. 安全与非目标

- 所有 Surface、窗口观察、拓扑读取和 Host 身份均为进程内合成适配器；不创建 HWND，不读取真实显示器或 Explorer 状态；
- 报告只输出场景结果和安全布尔值，不输出窗口句柄、容器/项目身份或机器信息；
- 预检只验证正式状态机和注册桥的确定性合同，不冒充真实 Explorer 崩溃、锁屏/RDP、热插拔、DPI/旋转或多显卡设备证据；
- 不读取桌面文件内容，不移动、写入或删除真实文件；
- 不改变 #19/#20/#23/#24、外部人工证据、M4-ready、内部 RC、签名和公开分发状态。

## 4. 实现与门禁

- 新增 `ProductDesktopHostRecoveryPreflight`，直接复用正式 lifecycle、topology controller 和 window bridge；
- 新增 `LongGrid.Tools.DesktopHostRecoveryPreflight` 无参数 JSON 入口，任一场景或最终释放断言失败均返回非零；
- 新增同入口单元测试，避免测试与工具维护两套语义；
- CI 在 M4b1 恢复预检之后新增独立 `DesktopHost lifecycle recovery preflight` 步骤。

## 5. 本地结果

- 目标工具 Release 构建：0 warning / 0 error；
- 新增专项：1/1；完整核心测试：923/923；
- 独立工具：`Passed`，5/5 场景全部为 true；
- `allSyntheticSurfacesReleased=true`；
- `readsRealDesktop=false`、`createsNativeWindows=false`、`realFileOperationsAllowed=false`；
- PR 与 main 干净 Windows runner 均完成完整解决方案、覆盖率、探针与内部 unsigned RC 交付集复验。

## 6. 远端轨迹与下一步

- 实现 PR / head SHA：PR #201 / `6293dd708d00487dd7d24842ae9b6e6381685d19`；
- PR CI：run `31812773516` 成功，923/923，lines 90.68%（27512/30340），branches 78.34%（8580/10952）；M4b2 5/5 与全部释放/安全边界通过；
- squash 合并：`main@8d04e2f825992d93d770028feaf6c3e62118e6b9`；
- main CI：run `31813422201` 成功，923/923，覆盖率同上；M4a/M4b1/M4b2、安全探针、依赖门和内部 unsigned RC 交付集全部通过；
- 最终判定：M4b2 Engineering Pass，连同 Stage 142 的 M4b1，M4b 工程切片判定 Engineering Pass；这不是 M4-ready、真实设备证据、内部 RC 或公开分发批准；
- 下一步：进入 M4c 资源长稳预检；真实 Explorer/session/显示器设备矩阵仍在外部证据门中执行。
