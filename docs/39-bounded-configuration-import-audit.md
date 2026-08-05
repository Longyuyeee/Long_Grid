# Long方格受限外部配置导入审计

日期：2026-08-05

基线：`main` / `591e757` + Issue #24 外部配置导入增量分支

证据等级：E3 / Production import transaction slice
结论：**Bounded explicit import pass / Migration, export and evidence lifecycle pending / Issue #24 保持 OPEN**

## 1. 本切片关闭什么

本切片建立“用户选择 → 有限预览 → 默认取消确认 → 原子发布”的正式导入合同。它允许用户在无配置、主配置有效、备份只读恢复或 SafeMode 四种状态下，显式导入一份当前 v1 Long方格配置。

本轮不接普通产品状态自动保存，不解释单实例 activation payload，不导入旧 schema，不支持云端/虚拟提供程序来源，不导出配置，不删除历史证据，也不执行真实卷、断电或非 NTFS 测试。

## 2. 来源与解析边界

导入入口由 Windows `FileOpenPicker` 提供，文件类型筛选固定为 `.json`。App 只把以下有限来源事实传给 Infrastructure：

- 是否由用户本次明确选择；
- 扩展名是否为 `.json`，比较不区分大小写；
- 是否具有本地文件系统路径；
- 所选文件本身是否为重解析点。

任一条件不满足都在创建配置目录或写租约之前拒绝。当前切片主动拒绝无本地路径的云端/虚拟 provider 和所选文件重解析点；已经完整落地并表现为普通本地文件的同步副本仍按本地文件处理，本轮不提供来源签名或同步提供程序证明。这是保守范围，不代表其他来源永远不支持。

Store 从流中最多读取 `4 MiB + 1 byte`：空文件、超限文件、畸形 JSON、未知/未来 schema、资源预算或字段合同不合法均返回有限错误，不向 UI 传播路径、JSON、profile、target 或原始异常。

## 3. 有限预览与冲突合同

`ProductConfigurationImportPlan` 是 Store 创建的内存态不透明计划。UI 只能读取：

- schema 版本；
- 容器数量；
- 引用项目总数；
- 当前存储属于 Missing、LoadedPrimary、RecoveredBackupReadOnly 或 SafeMode。

计划不公开 payload、路径、profile ID、容器名或项目 target。Store 在生成预览前后分别计算当前主配置、备份、恢复标记和两个导入标记的 SHA-256 修订；超大证据以长度和最多 `4 MiB + 1 byte` 的有界内容参与修订。两次修订不同则预览失败。

确认提交时，Store 在创建目录前比较一次修订，取得有界跨进程写租约后再比较一次。只要预览后存储发生变化，就返回 `StoreChanged` 并要求重新选择和预览，不覆盖后来状态。

## 4. 发布、证据与中断语义

确认对话框默认焦点为“取消”，并明确展示 schema、容器/项目数量、当前状态的替换结果、证据归档、会写入配置目录且不可在 Long方格内自动撤销。只有 `ContentDialogResult.Primary` 才调用 `ImportAsync(..., userConfirmed: true)`。

Store 把预览时复制的原始有效字节写入同目录导入暂存，执行 `WriteThrough + Flush(flushToDisk: true)`，再通过正式产品合同复读。发布语义如下：

| 当前状态 | 主配置 | 备份 | 导入结果 |
| --- | --- | --- | --- |
| Missing | 缺失 | 缺失 | 同目录移动为首份主配置 |
| LoadedPrimary | 有效 | 按原状保留 | `File.Replace` 发布，旧主配置归档为 `.import.*.primary` |
| RecoveredBackupReadOnly | 损坏 | 有效并保留 | 发布导入配置，损坏主配置归档 |
| SafeMode | 可能损坏/缺失 | 可能损坏/缺失 | 现存备份先改名归档，主配置发布时同步归档；发布失败则回滚备份改名 |

导入使用 `.import.new` 与 `.import.next` 双槽位。如果上次中断已经留下一个标记，新导入先完整写入另一个槽位，不先删除旧标记。加载器只要看到任一恢复/导入标记就保持 SafeMode，避免主备都缺失时误报 Missing。成功发布后才清理恢复和导入暂存；回滚本身失败时保留新标记，供下次安全识别。

## 5. UI 与无障碍合同

“安全边界”页新增“选择配置…”入口和 polite live status。UIA 固定覆盖：

- `ImportPickerOpen`；
- `ImportCancelled` / `ImportCancelledAfterPreview`；
- `ImportPreviewValidated`；
- `ImportCommitInProgress`；
- `ImportCommitted:EvidencePreserved`；
- `ImportFailed:<finite error>`。

UI 只持有不透明计划和有限预览；文件选择、路径/重解析点判断、流读取及正式 Store 调用均由 App 边界执行。导入不会扫描桌面、移动文件或接入 DesktopHost。

## 6. 自动证据

- Release 全解决方案构建：0 warning / 0 error；
- 168/168 自动测试通过；
- 最终单次采集行覆盖率 91.10%（2048/2248），分支覆盖率 81.13%（529/652），超过 CI 的 90%/75% 门禁；
- 来源未授权、扩展名、本地来源、重解析点、空/超限/畸形文档均在零存储创建下拒绝；
- Missing、LoadedPrimary、RecoveredBackupReadOnly、SafeMode 四态导入通过；
- 取消、预览后冲突和写租约争用验证零发布；
- 正常旧主配置归档、备份保持、SafeMode 主备分别归档与发布失败回滚通过；
- 旧导入标记、双槽位发布、失败保留旧标记与 SafeMode 保持通过；
- UI 源码合同覆盖 64 个 AutomationId、系统 picker、`.json` 筛选、重解析点检查、有限预览和确认提交。

## 7. 仍未关闭

Issue #24 继续保持 OPEN，后续仍需：

1. v1 之外的显式迁移策略、版本预览与迁移回滚；
2. 有效配置受限导出、匿名证据查看、单项原始证据导出和单项永久清理已由后续切片完成；仍需负责人批准的保留期限、容量预算和自动批次策略；
3. 经批准的真实产品状态保存入队、错误提示与重试；
4. 真实待保存批次下的第二实例激活与关闭排空竞态矩阵；
5. I24-01/I24-02 专用卷真实证据，以及断电、非 NTFS、企业重定向目录和长期压力；
6. 真实 WinUI 辅助功能与视觉复核。

因此本切片只能描述为“当前 v1、本地 `.json`、用户显式选择和确认的受限导入”，不能描述为通用备份迁移、云同步、自动修复或发布就绪。
