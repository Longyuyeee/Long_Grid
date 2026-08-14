# Stage 136：目录—投影修订集成审计

审计日期：2026-08-14

## 1. 判定

M3a 的本地实现判定为 **Engineering Pass / Remote Evidence Pending**。本切片修复正式配置加载后刷新桌面目录会使 DesktopHost 进入冲突故障的问题：目录的 `Refreshing`、`Ready`、`Failed`、`Cancelled` 与新 generation 现在先形成单调的工作区外部修订，再重建正式工作区和 DesktopHost 投影。

这只是 M3 的第一个集成缺口，不代表 M3、M4-ready、Phase 0 或内部 RC 完成。实现没有读取文件内容，没有公开路径，也没有移动、重命名、删除或写入桌面文件。

## 2. 缺陷与根因

既有 App 已把配置加载、恢复、编辑、保存/重试和 DesktopHost 投影分别接入，但目录刷新仍有一处跨链断点：

1. 已加载配置的投影使用 `workspaceCommits.CurrentEditRevision` 作为防冲突身份；
2. 一次目录刷新会依次发布同 generation 的 `Refreshing` 与终态快照，正式工作区的引用解析和可见名称随之变化；
3. 目录变化此前没有推进工作区修订，因此 lifecycle 会收到“同工作区修订 + 同拓扑代次 + 不同投影内容”；
4. lifecycle 按安全合同将其识别为冲突、释放窗口并进入 `Faulted`，后续同代次 Ready 不能恢复。

根因不是 DesktopHost 防冲突过严，而是目录来源身份没有进入工作区修订链。降低冲突检查会允许同身份内容漂移，因此不采用。

## 3. 实现与需求对齐

| 合同 | 实现 | 当前判定 |
| --- | --- | --- |
| 配置优先 | `ProductWorkspaceCatalogRevisionSynchronizer` 复用既有 `ProductWorkspaceCommitCoordinator.AdvanceExternalRevision()`，不建立第二套配置或保存状态 | 本地通过 |
| 刷新可恢复 | `Refreshing` 与同 generation 的终态分别推进修订；投影先安全隐藏，Ready 后以更高修订恢复 | 本地通过 |
| 令牌不漂移 | 每次目录身份变化同时清除既有编辑/审查/撤销令牌，旧 UI 动作不能提交到新的解析模型 | 本地通过 |
| 重启/恢复一致 | 配置加载、导入或显式恢复已经推进外部修订；同步器只重设当前目录基线，不重复推进 | 本地通过 |
| 幂等与乱序 | 相同 generation/status 不重复推进；低 generation 快照被拒绝，且不回写正式目录 UI | 本地通过 |
| 最后有效配置 | 本切片不调用保存，不修改配置文档；既有保存失败、显式重试和关闭阻断合同保持不变 | 本地通过 |
| 安全引用 | 只比较目录状态和代次，不记录条目名称、路径、内容或用户身份 | 本地通过 |

`LongGrid.Infrastructure` 仅向正式 `LongGrid.App` 增加明确的 internal 可见性，以保持同步器不成为公共 SDK 合同；测试程序集原有 internal 可见性不变。

## 4. 自动化与人工边界

- 新增 5 项同步器测试：加载基线、`Refreshing → Ready` 双修订、重复快照幂等、旧 generation 拒绝、配置重载不双重推进；
- App Release build：0 warning / 0 error；
- 同步器与 DesktopHost lifecycle 专项：42/42；
- 本地全量：Release 0 warning / 0 error，912/912；line 90.73%（25684/28308），branch 79.41%（8294/10444）；格式、依赖漏洞、11 个安全 launcher、3 项原生 DesktopHost probe、UI/clean-session/single-instance/hang/release-restore/RC 合同全部通过；
- PR CI 与 main CI：待本切片提交后执行并回填；
- 物理目录刷新、Explorer 重启、Narrator、高对比、文本缩放和动态系统表面的人工证据继续 Pending。

## 5. 剩余 M3 缺口

1. 审计并闭合正式桌面选择与 App 内当前工作区/错误状态的匿名可观察链，不能暴露路径或复制选择状态；
2. 验证保存失败、显式重试、配置恢复、目录失败/取消与 DesktopHost 隐藏/恢复的正式组合旅程；
3. 补最小匿名交互证据导出和清理入口，继续复用既有证据库存与删除确认；
4. M3 闭合后再进入 500 项规模、故障恢复和资源长稳自动预检，推进 M4-ready；
5. 到达 ReadyForExternalValidation 后停止功能扩张，按 X1～X5 汇合不可伪造的外部证据。

## 6. 远端轨迹

- 实现 PR / merge SHA：待回填；
- PR CI / main CI：待回填；
- 结论：本地 Engineering Pass，远端证据未完成前不得升级为 M3a Final Pass。
