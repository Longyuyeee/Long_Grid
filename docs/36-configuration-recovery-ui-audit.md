# Long方格配置恢复状态 UI 审计

审计日期：2026-08-04

基线：`main` / `ea81f21` + 配置恢复 UI 增量分支

结论：**Read-only recovery presentation pass / Destructive repair not implemented / Local UIA session Inconclusive / Issue #24 保持 OPEN**

## 1. 本轮目标

把正式 `ProductConfigurationStore.LoadAsync` 接入 App 启动，并让用户能够区分以下四种有限状态：

1. 尚无保存配置；
2. 主配置校验通过；
3. 主配置不可用、已从备份只读恢复；
4. 主配置和备份均不可用、进入安全模式。

本轮只负责读取和呈现，不新增重置、删除、覆盖、导入或自动保存入口。损坏证据继续由 Store 保护；当前 App 仍无 `configurationSaves.EnqueueAsync`。

## 2. 分层与隐私边界

Infrastructure 新增 `ProductConfigurationStartupState`，把完整加载结果缩减为：

- `NoSavedConfiguration`；
- `LoadedPrimary`；
- `RecoveredBackupReadOnly`；
- `SafeMode`；
- 两个有限的 `ProductConfigurationStorageFailure` 分类。

该状态不携带配置 Document、ProfileId、路径、文件名、原始 JSON、异常文本或 `PrimaryContractError` / `BackupContractError`。MainWindow 只把有限失败分类翻译为“缺失、为空、过大、未通过校验、暂时无法读取”等固定文案。

## 3. 启动与交互设计

- App 先创建并激活原有窗口，再异步只读加载配置，避免磁盘读取阻塞首帧；
- `Missing` 不创建配置目录或文件；
- 概览页原有“安全只读原型”InfoBar 被复用为启动状态表面，不增加第二个横幅；
- 正常缺失使用 Informational，主配置有效使用 Success，备份恢复使用 Warning，安全模式使用 Error；
- 横幅不可关闭，避免用户把未解决的损坏状态误认为已经修复；
- 备份恢复明确说明不会覆盖损坏证据；安全模式明确说明没有加载或覆盖配置，并引导查看“安全边界”页；
- 原有“恢复预览”页面继续只描述显示布局差异，不与配置恢复混为一谈。

当前没有“确认使用备份”“重置配置”或“覆盖损坏文件”按钮。这些操作会改变磁盘证据，必须在独立切片中定义备份、确认、审计和失败回滚后才能实现。

## 4. 自动证据

- 137/137 自动测试通过；
- 行覆盖率 91.81%（3294/3588），分支覆盖率 80.00%（888/1110），均超过 CI 的 90%/75% 门槛；
- 四种加载状态到有限启动状态的映射全部覆盖；
- `null` 加载结果被拒绝；
- 缺失配置的读取不会创建存储目录；
- UI 源码合同验证 App 调用正式 `LoadAsync`、使用有限状态、区分备份只读恢复/安全模式、不访问原始合同错误，并继续禁止任何 App 保存入队；
- Release solution build 为 0 warning / 0 error。

## 5. 本机 UI 证据限制

本轮真实 UIA 冒烟在当前桌面会话持续遇到 WinUI Provider `E_UNEXPECTED`。将本轮 App、XAML 和 MainWindow 改动完全还原到 `main` 后，同一基线仍复现，因此不能归因于本切片；App 进程和 HWND 在 5 秒观察中保持稳定。试验性 UIA 异常重试已撤销，没有以放宽工具结论掩盖环境问题。

随后使用 Windows 应用控制工具按显式 Release EXE 尝试只读截图核对，但工具未返回 Long方格目标窗口，因此该补充证据同样记为 Inconclusive。CI 继续执行结构 UI 合同；Narrator、高对比、文本缩放和真人视觉判断仍按 Issue #19 保持 Pending。

## 6. 需求对齐与下一步

本切片关闭的是“正式加载状态可以安全、脱敏地呈现”门槛，不是完整配置修复体验。Issue #24 仍需：

1. 经明确确认的备份接受、损坏证据归档或安全重置流程；
2. 经批准的真实产品状态保存、错误提示和重试；
3. 真实保存排空期间的第二实例激活竞态矩阵；
4. I24-01/I24-02 专用卷真实证据；
5. 断电、非 NTFS、企业重定向目录和长期跨进程压力。

在这些证据完成前，Issue #24 保持 OPEN，不得把只读状态横幅描述为配置已经被修复。
