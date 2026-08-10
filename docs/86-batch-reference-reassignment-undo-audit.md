# 同源方格批量引用改归属与一次撤销审计

> 日期：2026-08-11
> 范围：正式 `LongGrid.App` 同一源方格内 1..256 个已解析引用的原子配置改归属与整批一次撤销
> 审计基线：`main` / `cbab825`（Stage 85 已合入）
> 当前结论：**产品配置切片已实现；桌面文件与 DesktopHost 继续零修改**

## 1. 需求与竞品交互对齐

桌面管理产品需要让用户把一组项目从当前分组快速调整到目标分组。Long方格沿用 Windows 标准 Ctrl/Shift 多选和既有“选择此方格前 256 项 / 清除选择”入口；当且仅当选择数量为 1..256、全部来自同一源方格且目标为另一个正式方格时，“批量改归属”才可提交。按钮持续显示当前数量，跨方格混合选择不会获得批量提交能力。

提交前显示默认焦点为“取消”的确认框，明确整批只修改 Long方格配置，不移动、删除、重命名或读取桌面文件内容。该语义吸收竞品快速分组调整的效率，但保留 Long方格“零惊吓、动作可解释、可整体撤销”的差异化边界。

## 2. 原子 reducer 与提交合同

`ProductWorkspaceReducer.ReassignResolvedReferences` 在不可变快照上先验证完整输入，再一次性从源方格移除所选引用并按源方格原顺序追加到目标方格。空集合、重复 ID、缺失项、未解析引用、锁定源/目标或无效状态均有限拒绝，不返回部分状态；原有单项 API 委托给批量 reducer，保持调用兼容和单一语义来源。

`ProductWorkspaceCommitCoordinator` 对请求执行以下门禁：

- 共享 edit revision 必须准确；
- 源/目标 ordinal 必须有效且不同；
- ordinal 数量必须为 1..256、无重复，并全部指向同一源方格内的已解析引用；
- reducer、v1 投影与唯一保存控制器必须全部接受；
- 每次接受只提交一次完整状态、只递增一次 revision，并使其他待撤销编辑失效。

请求和 presentation 只携带有限 ordinal、可见名称与数量；不向 UI 暴露路径、持久化目标、ProfileId、SourceId、ContainerId、ItemId、ParsingName、VolumeId 或 FileId。

## 3. 整批一次撤销

批量提交复用已审计的 `ProductWorkspaceReferenceReassignmentUndo` 状态指纹合同。令牌绑定操作 ID、改归属后的 revision、改归属后配置指纹和恢复配置指纹；撤销前再次核对准确令牌、当前 revision 与双指纹。确认成功时，整批前状态作为一次配置编辑进入同一保存控制器，revision 再递增一次，令牌立即消费；第二次撤销、其他成功编辑或外部 revision 推进均有限拒绝。

## 4. UI、辅助功能与安全披露

- 同一个多选列表服务批量移除和批量改归属，避免增加重复焦点与新的 AutomationId；
- 目标方格选择器只在同源、有限数量选择下开放；按钮文本显示批量数量；
- 提交确认默认取消，并明确“不会移动、删除或重命名桌面文件”；
- 状态区发布 `Count`、`Atomic=True`、revision、结果与 `DesktopFilesChanged=False`；
- 撤销播报明确为“上一次批量引用改归属已整体撤销”；
- UI 合同从单项改归属升级为 `same-source-bounded-256-confirmed-atomic-config-only-single-undo`，AutomationId 总数保持 134。

## 5. 自动化证据与停止规则

定向测试覆盖：批量移动两个引用、保持源顺序、输入状态不变、缺失/重复/越界/257 项无部分提交、锁定源/目标拒绝、一次 revision、整批一次撤销、第二次撤销拒绝，以及两个真实临时文件内容前后完全相同。应用全解构建为 0 警告、0 错误，134-ID UI 源码合同通过。

提交前完整本地门禁结果：

- Release 全解构建通过，0 警告、0 错误；546/546 测试通过；
- 单份 Stage 86 Cobertura 为行覆盖率 91.25%（16282/17844）、分支覆盖率 80.36%（4614/5742），通过 90%/75% 门槛；
- 启动链、134-ID UI、干净会话、单实例、BSA 及 Issue #19/#20/#23/#24 会话合同通过；人工、硬件和专用卷结论继续保持各自 Pending；
- DesktopHost 自动交互切片为 `Conditional Pass` 且 `DesktopFilesReadOrChanged=false`；配置持久化 20 个场景、文件操作安全探针和依赖漏洞门禁通过；
- 缩略图隔离探针首轮出现 `Verdict=Fail`，但退出码为 0、清理和资源预算均通过；未改动该子系统的独立复跑恢复为 `ConditionalPass`，并明确 `ProductFallbackRequired=true`、所有 ACL 恢复、AppContainer profile 删除成功。首轮异常保留为环境可重复性信号，不被复跑覆盖，也不升级为本切片阻断或产品 Pass。

准确提交的内部 RC、PR CI 和 main CI 在提交发布阶段继续复核；未取得这些结果前不把本分支描述为主线完成。

本阶段没有授权真实桌面文件移动、跨多个源方格的复合提交、DesktopHost 窗口执行、任务栏美化、小组件或插件运行时，也不替代 BSA-01–BSA-05、Issue #19/#20/#23/#24、许可证、签名与安装生命周期证据。

## 6. 后续方向

配置层的批量加入、同方格批量移除和同源批量改归属现已形成对称、有限、可撤销闭环。下一优先项仍是专用账户中的 BSA-01–BSA-05 和 Phase 0 外部矩阵；若环境不可用，只允许继续不扩大 Windows 权限面的正式工作区交互收口，不得用更多自动探针伪造人工或硬件 Pass。
