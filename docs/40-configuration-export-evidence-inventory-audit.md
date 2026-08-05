# Long方格配置导出与匿名证据清单审计

日期：2026-08-05

基线：`main` / `df0e918`（PR #90 已合入）+ Issue #24 配置导出增量分支

证据等级：E3 / Production lifecycle slice
结论：**Bounded explicit export and read-only evidence inventory pass / Migration, evidence export-cleanup policy and real-volume evidence pending / Issue #24 保持 OPEN**

## 1. 需求与切片范围

本切片关闭 PRD 中“用户主动导出当前有效配置”的首个生产合同，并为恢复、重置和导入事务留下的配置归档提供只读匿名清单。它延续既有“有限预览 → 默认取消确认 → 用户选择位置 → 安全提交”模式。

本轮当时明确不实现：普通产品状态自动保存、旧 schema 迁移、原始损坏内容预览或导出、证据删除/自动清理、保留期限执行、云端/虚拟 provider 目标、诊断包、真实卷断电或非 NTFS 保证。单项原始证据导出已由后续切片完成，见[配置原始证据导出审计](41-configuration-evidence-export-audit.md)；观察生命周期统计与单项永久清理见[配置证据生命周期基础审计](42-configuration-evidence-lifecycle-foundation-audit.md)。

## 2. 导出预览与隐私边界

`PrepareExportAsync` 只接受两类可验证来源：有效主配置，或主配置损坏时已经通过正式合同验证的只读备份。Missing 和 SafeMode 没有可导出的有效文档，均返回有限 `ExportNotAvailable`，不会把损坏字节当作配置导出。

Store 在加载前后计算主配置、备份和恢复/导入标记的有界修订。UI 得到的不透明 `ProductConfigurationExportPlan` 只公开 schema、容器数量、引用项目数量和 `LoadedPrimary` / `RecoveredBackupReadOnly` 来源状态；payload、profile、容器名、target、源路径和原始异常均不公开。

## 3. 授权与发布合同

确认对话框默认焦点是“取消”，并明确说明导出的配置文件包含已保存的引用目标。只有明确选择主按钮后，App 才打开 Windows `FolderPicker`。取消预览确认不会请求文件夹授权；取消 picker 不写文件。

当前目标边界固定为：

- 本次由用户明确选择；
- 具有本地文件系统路径；
- 不是 UNC 路径；
- 所选目录元数据与 Infrastructure 复检均表明它不是重解析点；
- 目录已经存在，Store 不代替用户创建目标目录。

Store 生成 `LongGrid-Configuration-v<schema>-<UTC>-<GUID>.json` 唯一名称，不接受调用方指定文件名，也不覆盖既有文件。有效 payload 先写入目标目录内的 `.new` 文件，使用 `WriteThrough` 和 `Flush(flushToDisk: true)`，再由正式 JSON 合同复读。发布前再次比较配置修订；若预览后配置变化，则删除暂存并返回 `StoreChanged`。最终使用不覆盖的同目录移动发布。

这提供应用层的同目录原子发布和既有文件不覆盖合同；它不宣称在所有文件系统、远程重定向、驱动缓存或突然断电情况下具有 NTFS 等价耐久性。

## 4. 证据清单合同

清单只匹配 Store 自身产生的精确名称：

- `<主配置名>.damaged.<32 位小写十六进制>.primary|backup`；
- `<主配置名>.import.<32 位小写十六进制>.primary|backup`。

`.recovery.new`、`.import.new`、`.import.next`、备份、锁文件和近似名称均忽略。重解析点证据跳过并只累计有限数量。单次最多扫描 4096 个目录项、返回 256 项，任一上限触发时标记 `Truncated`，不会无界占用 UI 时间或内存。

每项只包含来源类型、主/备角色、字节大小和归档 UTC 时间。UI 显示本地化时间，但不接收文件名、GUID、路径、profile、target 或内容。刷新是显式只读操作；目录缺失时返回空清单且不创建目录。本切片当时没有删除、清理、打开位置或原文查看入口；后续只增加匿名单选的显式导出与永久清理，仍没有打开位置或原文预览。

## 5. UI 与自动化合同

安全边界页新增：

- `ExportConfigurationButton` / `ConfigurationExportStatus`；
- `RefreshConfigurationEvidenceButton` / `ConfigurationEvidenceStatus`；
- `ConfigurationEvidenceList`。

导出和证据状态均使用 polite live region。文件夹 picker、路径与重解析点判断留在 App 边界；MainWindow 只接收不透明计划、有限结果和匿名清单。UI 源码门禁继续禁止 MainWindow 直接使用 `File`、`Directory` 或桌面数据 API。

## 6. 自动证据

- Release 全解决方案构建：0 warning / 0 error；
- 本切片当时 184/184 自动测试通过；后续原始证据导出切片将全量基线提升至 192/192；
- 单次覆盖率：行 90.25%（4444/4924），分支 81.62%（1146/1404），超过 CI 的 90%/75% 门禁；
- Missing 拒绝、有效主配置导出、已验证备份导出、取消零写入、预览冲突、目标授权/本地/重解析点/存在性边界通过；
- 导出文件通过正式配置合同复读，主配置和损坏证据保持不变，目标无 `.new` 遗留；
- 证据目录缺失零创建、精确名称筛选、匿名输出、256 项上限与截断通过；
- UI 源码合同覆盖 69 个 AutomationId，以及预览先于 picker、默认取消、有限错误和 MainWindow 无文件 I/O 边界。

## 7. 仍未关闭与下一步

Issue #24 继续保持 OPEN。剩余项按优先级为：

1. 正式迁移等待真实 v2 字段获批后，再实现显式迁移预览、逐版本升级与失败回滚；
2. 单项证据原文导出、观察容量与选择性单项清理已完成；仍需获批的保留期限、容量预算、自动批次日志与中断恢复；
3. 经批准的真实产品状态保存入队、错误表面与重试；
4. 第二实例激活、待保存批次、导入/导出和关闭排空竞态矩阵；
5. I24-01/I24-02 专用卷、断电、非 NTFS、企业重定向目录和长期压力证据；
6. 真实 WinUI picker、键盘、Narrator、高对比、文本缩放和 DPI 视觉复核。

因此本切片当时只能描述为“本地有效配置的用户主动受限导出，以及 Long方格归档的只读匿名清单”。后续已增加用户明确选择后的单项原始证据受限导出，但仍不能描述为完整备份中心、证据生命周期管理、云同步或发布就绪。
