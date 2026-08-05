# Long方格原始配置证据导出与迁移准入审计

日期：2026-08-05

基线：`main` / `46a8d6f`（PR #91 已合入）+ Issue #24 证据导出增量分支

证据等级：E3 / Production lifecycle slice
结论：**Explicit raw-evidence export pass / No legitimate product schema migration admitted / Retention and cleanup policy pending / Issue #24 保持 OPEN**

## 1. 需求复核与开发方向调整

上一切片建议优先处理正式 schema 迁移。本轮重新审计完整 Git 历史、正式 Core 合同和 P0-06 探针后确认：

- `LongGrid.Core.Configuration` 的 v1 是第一个正式产品 schema；
- 仓库没有发布过 v0 或其他旧产品格式；
- P0-06 的 v1→v2 只增加探针测试字段，文档明确声明它不是产品 v2；
- 当前产品需求没有已批准、必须进入 v2 的新字段或行为。

因此本轮不伪造 v0→v1 数据，也不把探针夹具复制进正式程序集。正式迁移的准入条件保持为：真实产品字段获批、schema 版本递增、相邻版本迁移、确定性深拷贝、未知字段策略、失败不发布和旧版本备份回退同时具备。

Issue #24 中当前可实际交付的下一项是原始配置证据的用户主动导出。本切片完成该能力；保留期限、选择性删除和自动清理继续分离，避免在没有容量/恢复策略时引入破坏性入口。

## 2. 选择与隐私边界

证据清单继续只显示来源类型、主/备角色、大小和时间，不显示路径、真实文件名、随机归档标识或内容。每个清单项内部携带仅 Infrastructure 可读取的来源路径；MainWindow 只能把不透明对象回传给 Store。

用户必须先刷新清单并明确选择一项。导出按钮默认禁用；选择变化通过 UI Automation 暴露有限状态。确认对话框默认焦点为取消，并明确说明原始证据可能损坏，也可能包含保存过的路径、名称或其他私人配置。

只有用户选择主确认按钮后才打开 `FolderPicker`。取消确认不会请求目标位置；取消 picker 不写文件。

## 3. 来源复检与导出合同

Infrastructure 在写目标前复核：

- 不透明条目的规范化父目录必须等于当前 Store 配置目录；
- 文件名必须仍匹配 Store 的 `.damaged.*.primary|backup` 或 `.import.*.primary|backup` 精确格式；
- 来源必须存在且不是重解析点；
- 大小和最后写入时间必须与最近清单快照一致；
- 单项不得超过 64 MiB。

Store 以 `FileShare.Read` 打开来源，在整个复制期间拒绝写入和删除共享。目标继续复用本地、绝对、非 UNC、非重解析点、用户选择且已存在的目录合同。输出文件名只包含有限来源/角色词汇、UTC 与新 GUID，不复用来源归档标识，扩展名固定为 `.bin`，避免把损坏内容冒充有效 JSON。

复制采用 64 KiB 流式缓冲、目标同目录 `.new`、`WriteThrough` 与 `Flush(flushToDisk: true)`。Store 同时计算来源 SHA-256，关闭暂存后重新读取并计算目标 SHA-256，使用固定时间比较；只有完整性一致才执行不覆盖的同目录移动。成功和失败都不修改或删除原证据。

取消传播会清理暂存；来源争用、来源变化、来源消失、超限、完整性失败、目标不可用和授权拒绝均返回有限错误，不把路径或原始异常带入 UI。

## 4. UI 与自动化合同

安全边界页新增 `ExportConfigurationEvidenceButton`。`ConfigurationEvidenceList` 改为单选，但条目仍只显示匿名元数据。固定状态包括：

- `EvidenceSelectionRequired` / `EvidenceSelectedForExplicitExport`；
- `EvidenceExportCancelled`；
- `EvidenceExportFolderPickerOpen` / `EvidenceExportFolderPickerCancelled`；
- `EvidenceExportCommitted:SourcePreserved`；
- `EvidenceExportFailed:<finite error>`。

UI 源码合同增至 70 个 AutomationId，并继续禁止 MainWindow 使用 `File`、`Directory`、路径或桌面数据 API。文件夹选择和路径属性判断仍只位于 App 边界。

## 5. 自动证据

- Release 全解决方案构建：0 warning / 0 error；
- 192/192 自动测试通过；
- 最终 Release 覆盖率：行 90.33%（4668/5168），分支 81.59%（1188/1456），超过 CI 的 90%/75% 门禁；
- 损坏主配置证据与导入前备份证据逐字节导出通过，输出不含来源标识或路径；
- 未确认、清单后变化、清单后消失、64 MiB 超限、来源独占争用和调用方取消均零发布；
- 取消清理暂存，来源证据逐字节保持不变；
- UI 合同覆盖单选、默认禁用、确认后 picker、有限失败和源保留成功状态。

## 6. 仍未关闭

Issue #24 继续保持 OPEN：

1. 只有真实 v2 产品字段获批后，才实现正式相邻 schema 迁移与回滚；
2. 观察容量、选择性单项删除与清理前导出提示已由后续切片完成，见[配置证据生命周期基础审计](42-configuration-evidence-lifecycle-foundation-audit.md)；仍需获批的保留期限/容量阈值、自动批次日志与中断恢复；
3. 经批准的真实产品状态保存入队、错误提示与重试；
4. 真实待保存批次下的第二实例、导入/导出和关闭排空竞态；
5. I24-01/I24-02 专用卷、断电、非 NTFS、企业重定向目录和长期压力证据；
6. 真实 WinUI picker、键盘、Narrator、高对比、文本缩放和 DPI 视觉复核。

本切片当时只能描述为“用户明确选择后的单项原始证据受限导出”。后续已增加匿名生命周期统计和用户确认的单项永久清理，但仍不能描述为自动证据清理器、完整备份中心、通用迁移工具或发布就绪。
