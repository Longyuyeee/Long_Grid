# Long方格配置证据生命周期基础审计

日期：2026-08-05

基线：`main` / `3f1f96a`（PR #92 已合入）+ Issue #24 证据生命周期增量分支

证据等级：E3 / Production lifecycle slice

结论：**Observed lifecycle metadata + explicit single-item removal pass / No automatic cleanup policy admitted / Issue #24 保持 OPEN**

## 1. 需求复核与准入决定

上一切片留下保留期限、容量预算、选择性删除、自动清理、清理日志和中断恢复六项。重新审计需求与仓库后，没有发现负责人已批准的保留天数、容量上限、最少保留份数、清理频率或企业策略覆盖规则。直接写死“30 天”或“64 MiB”并在后台删除，会把未经批准的产品判断变成不可恢复的数据破坏。

因此本切片只准入两项可以独立证明安全的能力：

- 只读统计已经观察到的证据条数、总字节数与最早归档时间；
- 用户在匿名清单中明确单选后，经过默认取消的永久删除确认，只清理这一条。

本切片不启动定时器，不在启动、关闭、刷新或配置写入时自动删除，不定义保留期限或容量阈值，也不把单项结果冒充完整生命周期策略。

## 2. 匿名统计边界

清单仍最多扫描 4096 个目录项、最多向 UI 返回 256 个安全条目，并跳过重解析点。Store 在同一次有界扫描中累计：

- `ObservedItemCount`：匹配精确 Long方格归档格式且非重解析点的观察条数；
- `ObservedSizeBytes`：使用饱和加法的观察总字节数，避免整数溢出；
- `OldestObservedArchivedUtc`：已观察安全证据中最早的归档时间。

达到扫描上限时，UI 明确把数量、容量和最早时间视为至少值。公开结果不包含路径、真实文件名、随机归档标识、内容或原始异常。

## 3. 单项永久清理合同

清理按钮默认禁用，与导出按钮共享同一个匿名单选条件。确认框默认焦点为取消，并明确说明操作无法撤销、只影响当前一条、不启用自动清理，以及如需保留副本应先取消并导出。

Infrastructure 在删除前执行以下步骤：

1. 要求 `userConfirmed=true`；
2. 复核来源规范化父目录等于当前 Store 目录；
3. 复核精确 `.damaged|import.<32 hex>.primary|backup` 名称、存在且非重解析点；
4. 复核大小与最后写入时间仍等于最近清单快照；
5. 获取与保存、恢复和导入共用的有界跨进程写租约；
6. 在租约内再次完成相同复核；
7. 以拒绝写入、允许删除共享的只读句柄固定当前对象，再次检查长度/时间并只删除该路径。

单文件删除不会产生半批次状态。未确认、快照变化、来源消失、写租约超时、调用方取消、来源争用、权限或 I/O 失败都返回有限错误并保持证据不变。成功结果只返回来源类型、主备角色和释放字节数；UI 清空陈旧清单并要求刷新复核容量。

## 4. UI 与自动化合同

新增 `RemoveConfigurationEvidenceButton`，UI Automation 合同增至 71 个 AutomationId。固定状态包括：

- `EvidenceSelectedForExplicitActions` / `NoEvidenceSelected`；
- `EvidenceRemovalCancelled`；
- `EvidenceRemovalInProgress`；
- `EvidenceRemovalCommitted:SingleItem`；
- `EvidenceRemovalFailed:<finite error>`。

MainWindow 继续不得读取证据路径、文件名或内容，也不得直接使用文件系统 API。所有破坏性判断与写租约都位于 Infrastructure。

## 5. 自动证据

- 聚焦配置导出/证据测试：31/31 通过；全量 Release：199/199 通过；
- 新增覆盖：观察容量与最早时间、确认后只删除所选项、未确认零删除、快照变化、来源消失、写租约超时、来源争用和调用方取消；
- Debug 与 Release 全解决方案构建：0 warning / 0 error；
- UI 源码合同：71 个 AutomationId，Pass；
- 最终 Release 覆盖率：行 90.37%（4806/5318），分支 81.45%（1212/1488），超过 CI 的 90%/75% 门禁；
- 完整启动/Issue #19/#20/#23/#24 会话、DesktopHost、71 个 UI Automation ID、单实例、配置 100/2/2 压力、文件安全、缩略图隔离与依赖漏洞门禁均通过；需要人工/硬件环境的结论继续保持 Conditional/Pending。

## 6. 仍未关闭

Issue #24 继续保持 OPEN：

1. 负责人批准的保留天数、容量预算、最少保留份数、清理频率和企业策略覆盖；
2. 自动清理预览、批次日志、异常中断恢复、失败重试和可选宽限/撤销机制；
3. 真实产品状态保存入队、错误提示与关闭/第二实例竞态矩阵；
4. I24-01/I24-02 专用卷、断电、非 NTFS、企业重定向目录和长期压力证据；
5. 真实 WinUI 键盘、Narrator、高对比、文本缩放和 DPI 复核；
6. 真实 v2 字段获批后的正式相邻 schema 迁移。

当前能力只能描述为“匿名生命周期统计和用户明确确认的单项永久清理”，不能描述为自动证据清理器、完整保留策略、可恢复回收站或发布就绪。
