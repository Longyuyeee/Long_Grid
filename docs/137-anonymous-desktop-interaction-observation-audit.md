# Stage 137：匿名桌面交互观察链审计

审计日期：2026-08-14

## 1. 判定

M3b 判定为 **Engineering Pass / Manual Evidence Pending**。正式 DesktopHost 的唯一 selection transaction 现在派生出最小匿名观察摘要，并通过既有 lifecycle 快照进入 App 状态卡；进入 Explicit、选择变化、Escape、系统表面失效和投影释放都能使 App 观察状态同步收敛。

本切片没有建立第二份可写选择状态。App 只收到是否处于 Explicit、选中数量、是否存在焦点和选择修订，不收到容器 ID、项目 ID、名称、路径或文件内容，也不能从摘要提交配置编辑或文件操作。

## 2. 审计基线与缺口

Stage 135 已完成 pointer、有限键盘与 UIA 的同源选择事务，Stage 136 已闭合目录刷新与工作区/投影修订链。但 App 状态卡此前只展示宿主窗口数、方格数和拓扑代次：

1. 用户在桌面进入 Explicit 或选择项目后，正式 App 无法确认当前交互是否生效；
2. Escape、失焦或系统表面隐藏后，App 无法显示匿名选择已清理；
3. 直接把 transaction 或项目 ID 传给 App 会复制敏感身份和扩大命令边界，不符合配置优先与最小披露原则。

因此本步只增加由 transaction 派生、不可反向操作的匿名摘要，不把 App 变成第二个选择控制器。

## 3. 实现与需求对齐

| 合同 | 实现 | 当前判定 |
| --- | --- | --- |
| 唯一真源 | `ProductDesktopHostLifecycleSnapshot` 的四个观察字段只从 `intentConsumption.Snapshot.Transaction.Selection` 派生 | 本地通过 |
| 进入可见 | Prepared Intent 成功消费后发布 `ExplicitInteractionActive=true`，即使尚未选择项目也可观察 | 本地通过 |
| 选择可见 | pointer、keyboard、UIA 共用的 `ApplyInteractionSelection` 成功后发布数量、焦点存在性和选择修订 | 本地通过 |
| 取消收敛 | Escape 与系统表面取消后摘要归零；投影、拓扑和关闭路径继续通过 lifecycle 更新归零 | 本地通过 |
| 幂等 | 派生值未变化时不增加 lifecycle generation，也不重复发布 UI 状态 | 本地通过 |
| 匿名最小化 | lifecycle 不包含 container/item ID、名称、路径或内容；自动化状态明确 `Anonymous=True` 与 `RealFileOperationsAllowed=False` | 本地通过 |
| App 只读呈现 | 状态卡显示“交互已激活/已选择 N 项”、焦点存在性和修订；没有新增按钮、编辑委托或文件能力 | 本地通过 |
| 无障碍观察 | 既有 `DesktopHostValue` AutomationId 保持不变，`ItemStatus` 增加稳定匿名字段 | 本地通过 |

## 4. 自动化与人工边界

- App Release build：0 warning / 0 error；
- DesktopHost lifecycle 与原生 UIA 专项：50/50；
- 事务测试覆盖 Explicit 空选择、单项选择、焦点/修订、匿名文本不含 `container-1`/`item:2`、系统失焦归零和事件发布；
- UI automation 合同：143 个 AutomationId 与既有安全边界通过；
- 本地全量两次均为 911/912：唯一失败是既有 `NativeActivationSourceExposesFiniteInvokeAndHideRestoreContract` 在 UIA 调用中未取得 Windows 前台许可并按安全合同返回 `ElementNotEnabledException`；调用前窗口可见、合同、CanActivate 与 IsEnabled 均已复读为真。没有放宽 `SetForegroundWindow`/NoActivate 合同，也没有把失败改写为通过；
- 失败运行仍生成覆盖率：line 90.52%（25726/28420），branch 79.05%（8316/10520），覆盖率门禁通过；M3b 核心事务独立重跑通过；
- 格式、依赖漏洞、11 个安全 launcher、3 项原生 DesktopHost probe、UI/clean-session/single-instance/hang/release-restore/RC 合同，以及持久化、文件操作安全和缩略图隔离探针均通过；
- 独立 PR Windows runner 未复现本机前台许可异常：PR #191 run `31775852179` 为 912/912，line 90.21%（25632/28414），branch 79.05%（8316/10520），完整流水线通过；
- squash 合并为 `main@93c77e59a28820a9aaff13fcc4cb2a59ec91dc4d`；main run `31776200563` 再次为 912/912、line 90.21%、branch 79.05%，全部 34 步通过；
- 物理 pointer/keyboard、Narrator、高对比、文本缩放、动态系统表面和 Explorer 生命周期人工证据继续 Pending。

## 5. 剩余 M3 顺序

1. M3c：审计保存失败、显式重试、配置恢复、目录失败/取消与 DesktopHost 隐藏/恢复的正式组合旅程；
2. M3d：补最小匿名交互证据导出与确认清理，复用既有证据库存，不记录项目身份；
3. M3 工程闭合后进入 500 项规模、故障恢复和资源长稳自动预检，推进 M4-ready；
4. ReadyForExternalValidation 后停止功能扩张，按 X1～X5 汇合不可伪造的外部证据。

## 6. 远端轨迹

- 实现 PR / merge SHA：PR #191 / `93c77e59a28820a9aaff13fcc4cb2a59ec91dc4d`；
- PR CI / main CI：`31775852179` / `31776200563`，均成功；
- 结论：M3b Engineering Pass / Manual Evidence Pending。M3、M4-ready、Phase 0、内部 RC 和公开分发均未因本切片完成。
