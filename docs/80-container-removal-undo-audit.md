# 正式方格删除与一次撤销审计

审计日期：2026-08-10  
范围：正式方格的配置级删除、同会话一次撤销、UIA 与保存边界

> 结论：**Conditional Pass；方格配置可安全删除并撤销，真实桌面文件操作继续为零**

## 1. 需求对齐

此前正式工作区已经具备方格创建、重命名、锁定、折叠、有限外观和布局，以及引用加入、移除、改归属和一次撤销，但缺少完整 CRUD 中的删除。此次只补配置语义：

- 用户必须选择一个未锁定方格并通过默认聚焦“取消”的确认对话框；
- 确认文案显示方格可见名称与引用数量，并明确真实桌面文件不会被删除、移动或重命名；
- 成功后可撤销最近一次方格删除一次；
- 不接线 Explorer 拖放、文件删除、文件移动或 DesktopHost 窗口执行。

## 2. Core 与提交边界

既有 `ProductWorkspaceReducer.RemoveContainer` 继续承担不可变删除与正式配置复核。锁定方格拒绝；未解析引用只有在上层已经完成整方格删除确认时才允许随方格配置一并移除。

`ProductWorkspaceCommitCoordinator` 只接受 ordinal，不向 UI 暴露容器 ID。删除请求必须同时满足当前 edit revision、有效 ordinal、空名称、无其他动作参数和 `Confirmed=true`。接受路径只投影一次、调用一次 `saves.Submit`、推进一次 revision，并返回删除后的 Document。

## 3. 一次撤销

删除成功后签发 `ProductWorkspaceContainerRemovalUndoToken`，绑定随机操作 ID、删除提交后的 edit revision、删除后配置 SHA-256 指纹和恢复配置 SHA-256 指纹。

撤销要求令牌、revision、当前配置指纹和恢复状态指纹全部一致，并再次显式确认。任何其他成功引用编辑、方格编辑、布局恢复、撤销或外部 revision 推进都会清除令牌；成功撤销后立即消费，第二次撤销返回 `Unavailable`。

## 4. UI、隐私与辅助功能

正式方格编辑区新增 `ProductWorkspaceContainerRemoveButton` 与 `ProductWorkspaceContainerRemovalUndoButton`。删除按钮只在可编辑会话、有效选择且方格未锁定时开放；撤销按钮只在协调器持有有效令牌时开放。

presentation 只包含可见名称、ordinal、锁定/折叠、引用计数及有限外观/布局值，不包含路径、持久化 target、ProfileId、SourceId、ContainerId、ItemId、ParsingName、VolumeId 或 FileId。机器状态固定声明 `DesktopFilesChanged=False`。UI 源码合同由 127 增至 129；干净会话 `ValidateOnly` 同步要求 129 个 AutomationId。

## 5. 自动化证据与剩余风险

全量 Release 测试为 537/537 通过。单份 Cobertura 为行 91.29%（7750/8489）、分支 80.85%（2200/2721），通过 90%/75% 门槛。定向测试覆盖成功删除、一次撤销、第二次撤销拒绝、后续成功编辑使撤销失效、锁定、未确认、旧 revision、令牌/指纹/revision 门禁和真实临时桌面文件内容不变。Release 构建为 0 警告、0 错误；129-ID 源码合同与干净会话 `ValidateOnly` 已通过。

合入前仍必须通过启动链、单实例、漏洞、三项安全探针、内部 unsigned RC 以及 PR/main 双 CI。真实 live UIA、Narrator、键鼠/触控、批量选择、Explorer 数据对象、文件移动和硬件矩阵继续 Pending。

## 6. 后续进展

有限批量选择的首个配置切片已在 [81-batch-reference-addition-undo-audit.md](81-batch-reference-addition-undo-audit.md) 落地：多选未分组项目原子加入一个方格并可整批撤销一次，真实文件操作仍为零。
