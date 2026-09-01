# Stage 267：PF-010B2 重启后最近安全恢复点审计

日期：2026-09-01

输入基线：`origin/main@330aee2`（PF-010B1 / PR #350 已全绿合入）

状态：`PF010B2 EngineeringComplete / RealStorePass / ProductEvidencePending`；PF-010 整体仍为 `InProgress`

## 1. 本阶段交付结论

PF-010B2 已实现 App 重启后最近一次已保存安全恢复点。配置 Store 继续复用现有 `configuration.json.bak` 作为上一次有效配置，不新增包含真实文件路径的配置副本；新增 sidecar 只保存 schema 版本、随机恢复 ID、当前主配置 SHA-256、备份 SHA-256、UTC 时间和固定动作摘要，最大 4 KiB。

每次主配置成功原子发布后，Store 才尽力发布恢复元数据；首存、NoChange 等价配置或元数据发布失败均不会制造可用恢复点，也不会把已成功的主配置保存反向报告为失败。启动后只有主配置与备份同时通过合同校验、两个指纹与 sidecar 精确匹配时才显示恢复提示。

用户必须在正式 App 中二次确认。恢复前再次取得写租约并复核恢复 ID、当前指纹和备份指纹；有未保存、保存中或失败配置时 App 拒绝恢复。成功后备份原子替换主配置、sidecar 一次性消费、工作区重新加载并使旧会话历史失效。整个过程只修改 Long方格配置，不删除、移动或修改真实文件。

## 2. Expected、Initial Actual、Difference、Correction、Final Actual

| 检查 | Expected | Initial Actual | Difference | Correction | Final Actual |
|---|---|---|---|---|---|
| 正式恢复能力 | Store 暴露发现与确认恢复 API | 精确反射 xUnit 失败：`GetRestartRecoveryPointAsync` 为 null | 只有损坏主配置时接受 backup，没有正常重启恢复表面 | 新增双指纹恢复 API、一次性 token 和 App 入口 | 同一红测目标转为真实 Store 旅程并通过 |
| 重启旅程 | 保存前后两版、重建对象、确认恢复、再次重载 | 重启只加载最新 primary，正常 backup 不可显式恢复 | PF-010 的重启安全点缺失 | 复用既有 backup，sidecar 绑定 primary/backup 指纹 | 重建两次后恢复为“恢复前”，恢复点已消费 |
| 真实文件边界 | 配置恢复不触碰引用文件 | 此前没有正常重启恢复旅程证据 | 需验证真实路径与内容 | 真实中文目录和两个文件执行保存/重启/恢复并比较 SHA-256 | 内容、数量与 SHA-256 完全不变；sidecar 不含目录或文件名 |
| 只恢复已保存状态 | 未完成或失败保存不能产生/执行恢复点 | 原模型不存在此门禁 | 内存、磁盘和恢复点可能竞争 | 元数据只在主保存成功后发布；App 校验 SaveSnapshot | lease 失败不产生恢复点；Waiting/Saving/Failed/修订不等均被拒绝 |
| 外部变化 | 不恢复未知或已变化配置 | backup 只按文件存在与否使用 | 可能恢复错版本 | 当前与备份分别绑定 SHA-256，并在写租约内复核 | 分别返回 CurrentConfigurationChanged / RecoveryPointChanged |
| 故障处理 | 元数据或恢复写失败不破坏当前配置 | 无正常恢复故障模型 | 需有限失败语义 | 4 KiB 上限、损坏 fail-closed、暂存验证、一次性消费 | 损坏、占用、暂存失败均保留“after”主配置；可重试场景保留点 |

## 3. 真实测试结果

- Initial Actual：`StoreExposesExplicitRestartRecoverySurface` 首次执行失败，`Assert.NotNull` 的 Actual 为 null，证明正常重启恢复 API 确实不存在。
- PF-010B2 专项：`13/13`；覆盖真实 Store/SaveController、真实重启、确认门禁、外部变化、损坏元数据、写租约、暂存失败、sidecar 发布失败及 App 保存状态准入。
- Store、重启恢复与产品恢复预检相关回归：`65/65`。
- 真实中文文件：`真实用户目录/项目甲.txt`、`项目乙.txt` 执行两次真实保存、Store 对象重建、未确认、确认恢复、第二次对象重建；文件内容和 SHA-256 不变。
- sidecar 隐私边界：元数据中不包含 sandbox 路径、`项目甲.txt` 或 `项目乙.txt`，只含有限摘要与两个 64 位十六进制指纹。
- 真实 SaveController：只有 SavedRevision=1 后重启才出现恢复点，并成功恢复到前一版。
- 产品恢复预检从 5 场景扩展到 6 场景；Release CLI 返回 `outcome=Passed`、`restartSafePointRecovered=true`、sandbox 清理完成、真实桌面读取和文件操作均为 false。
- 完整 Release：`1,460/1,460`，0 failed，0 skipped。
- Release 全解决方案：`0 warning / 0 error`；格式和差异检查通过。
- UI 工程合同：`213` 个唯一必需 AutomationId 合同通过；新增恢复 banner/button、隐藏默认、显式确认、双指纹、4 KiB、一次消费和文件零修改合同通过。
- 正式跨进程 UIA 仍受本机 WinAppRuntime Main.2/DDLM 缺失阻塞；保持 `ProductEvidencePending`，未把源码合同冒充物理 App 证据。

## 4. 开发目标与需求对齐审计

开发目标审计：PF-010B2 的最近一次已保存安全点、对象重建发现、正式 App 提示、显式确认、写租约内复核、一次性消费、成功后工作区重载、未保存状态拒绝、有限失效原因、真实 Store/文件证据和恢复预检均已闭环。没有持久化完整 50 步会话历史，也没有复制 catalog 运行时对象或新增真实路径副本。

需求对齐审计：本阶段直接完成“放心整理后即使重启也能回到最近安全配置”的核心旅程。安全检查只服务数据正确性，没有扩张权限或安全邻接开发。功能范围没有混入长期历史、云同步、版本浏览或真实文件恢复。

完成度审计：PF-010A、PF-010B1、PF-010B2 均达到各自工程完成；PF-010 整体仍为 `InProgress`，因为规则应用尚未接入统一历史、旧 LatestUndo 用户入口尚未最终收敛、正式键盘/Narrator 物理证据仍 Pending。M1/M2 仍为 `0/2 Complete`，30 项 PF 仍为 `0 Complete`，产物不可公开分发。

## 5. 唯一接续开发点

下一步只进入 **PF-010B3：规则应用统一历史与旧撤销入口收敛**：

1. 审计真实代码中所有“规则应用”用户动作，先证明哪些仍未进入统一历史；
2. 以一次用户规则执行为一个原子 history item，完成 apply→undo→redo→undo；
3. 对目录现实变化只恢复 Long方格配置，不覆盖真实文件状态；
4. 将仍展示给用户的 LatestUndo 入口收敛到统一历史，保留内部失败补偿 token，不保留两套用户语义；
5. 用真实目录/文件和保存失败注入验证 Expected、Initial Actual、Difference、Correction、Final Actual；
6. 环境满足时补 Ctrl+Z/Ctrl+Y、恢复提示、焦点和 Narrator 物理证据；否则继续 Pending；
7. 阶段结束继续完成目标审计、需求对齐、文档、推送和 CI 收口。

PF-010B3 收口前不并行展开 PF-011，也不新增与当前用户旅程无关的权限或安全工作。
