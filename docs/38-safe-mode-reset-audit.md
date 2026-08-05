# Long方格 SafeMode 安全重置与证据事务审计

审计日期：2026-08-04

基线：`main` / `0f3959b` + Issue #24 SafeMode 重置增量分支

结论（本历史切片当时）：**Confirmed safe-mode reset pass / External import not implemented in this slice / Real-volume evidence pending / Issue #24 保持 OPEN**。当前 v1 受限导入已由后续切片完成，见 [`39-bounded-configuration-import-audit.md`](39-bounded-configuration-import-audit.md)。

## 1. 本轮目标与非目标

当主配置与备份都无法提供可用文档，或上次重置在发布前被中断时，用户可以经过默认取消的二次确认创建标准空白 v1 配置。任何现存损坏主配置和备份必须先按各自身份保留为随机后缀证据。

本轮不导入外部配置、不自动重置、不恢复容器或桌面项目、不删除历史 `.damaged.*`、不接入普通产品状态自动保存，也不替代 I24-01/I24-02 专用卷验证。

## 2. 标准空白配置

Core 新增 `ProductConfigurationDefaults.CreateEmpty()`，固定返回：

- 当前 schema version；
- 非个人化 `profileId = "default"`；
- 空容器集合；
- 无扩展字段、桌面引用、路径或用户内容。

该默认文档通过与普通产品配置相同的 `ProductConfigurationValidator` 和 JSON 资源预算。重置不从损坏文档复制任何字段，避免把未经校验内容带回新配置。

## 3. 重置事务

`ResetSafeMode` 与备份接受共用正式 `RecoverAsync` 入口，但要求的加载状态不同。写入只在 `UserConfirmed: true`、锁前预检为 `SafeMode`、取得有界跨进程写租约且锁内复检仍为 `SafeMode` 后开始。

锁内顺序为：

1. 序列化标准空白配置到独立 `.recovery.new`；
2. 写穿、落盘刷新并通过正式合同复读；
3. 若损坏备份存在，将其在同目录原子改名为 `.damaged.<random>.backup`；
4. 若损坏主配置存在，用 `File.Replace` 一次发布空白主配置并把旧主配置归档为 `.damaged.<random>.primary`；若主配置缺失，则用同目录 `File.Move` 发布；
5. 返回有限动作，以及主、备证据是否实际归档的两个布尔值；不返回路径、文件名、Document 或原始异常。

如果主配置发布失败，Store 尝试把已经改名的备份移回原位。回滚成功时恢复原 SafeMode；回滚本身失败时保留有效 `.recovery.new` 标记和已归档证据，使下一次加载继续返回 SafeMode，而不是误报 Missing。下一次经确认重试会复读、替换该标记并完成空白配置发布。

## 4. 交互与可访问性

- 配置恢复动作按钮默认折叠；
- `RecoveredBackupReadOnly` 显示“检查并接受备份”；
- `SafeMode` 显示“检查安全重置”；
- SafeMode 对话框明确说明会归档现存损坏主备证据、创建不含容器或桌面项目的空白配置、写入配置目录且无法在 Long方格内自动撤销；
- 默认按钮始终是“取消”，只有 `ContentDialogResult.Primary` 才传递 `ResetSafeMode + UserConfirmed`；
- 成功、执行中和有限失败均设置固定 UI Automation 状态；
- I/O 失败文案只承诺证据不会被静默丢弃，不伪称所有失败都绝对零文件位置变化。

结构 UI 合同继续覆盖 62 个 AutomationId。当前桌面会话的 WinUI UIA Provider 基线问题仍为 `Inconclusive`；本轮没有修改真实 `%LOCALAPPDATA%` 来构造损坏主备文件，因此真人视觉、键盘、Narrator、高对比和文本缩放仍待隔离用户配置环境复核。

## 5. 自动证据

- 152/152 自动测试通过；
- 行覆盖率 91.73%（1796/1958），分支覆盖率 80.48%（474/589），超过 CI 的 90%/75% 门槛；
- 标准空白配置验证 schema、默认 profile、空容器和零扩展数据；
- 主备均损坏时验证两个原始字节分别归档、空白主配置可重新加载且活动备份消失；
- 仅主或仅备存在时验证结果布尔值与归档数量准确；
- 未确认和非 SafeMode 请求验证零写入/零目录创建；
- 有效恢复暂存标记验证加载保持 SafeMode，之后可重试完成；
- 以主配置路径被目录占用模拟发布失败，验证已改名备份成功回滚、恢复暂存清理且 SafeMode 保持；
- UI 源码合同验证有限动作路由、默认取消、SafeMode 执行/成功状态和正式 Store 确认合同；
- Release solution build 为 0 warning / 0 error。

## 6. 剩余门槛

Issue #24 仍需：

1. 外部配置导入的来源授权、大小/类型/重解析点约束、有限预览、冲突和回滚合同已由后续切片完成；仍需旧 schema 迁移、配置导出和证据生命周期；
2. 多份 `.damaged.*` 的查看、导出、保留期限和明确删除策略；
3. 经批准的真实产品状态保存入队、保存错误提示与重试；
4. 真实待保存/恢复写入期间的第二实例激活与关闭排空竞态矩阵；
5. I24-01/I24-02 专用卷真实证据；
6. 断电、非 NTFS、企业重定向目录、长期跨进程压力及隔离配置环境中的真实 UI/无障碍复核。

完成这些证据前，本切片只能描述为“经确认的空白 SafeMode 重置并保全现存损坏证据”，不能描述为完整配置导入、自动修复或发布就绪。
