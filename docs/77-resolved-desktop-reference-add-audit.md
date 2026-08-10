# Long方格真实桌面项目加入正式方格审计

日期：2026-08-07

结论：**只读 MVP 主动分组链已建立；只修改 Long方格配置，桌面文件零变化**

## 1. 审计发现

当前 App 已经能只读枚举用户桌面与公共桌面第一层、创建和编辑正式方格、恢复已有引用，也能展示正式工作区。但主动分组链仍断开：用户无法从当前真实 Catalog 选择一个桌面项目并加入方格。因此“有真实目录”和“有正式方格”尚未组成最初需求中的基本桌面分组体验。

本切片补齐以下最小路径：

```text
authoritative User/Public Desktop Catalog
  -> 过滤已分组项目
  -> 显示文件名与有限类型
  -> 用户选择目标方格和项目
  -> catalog generation + edit revision 复核
  -> ProductWorkspaceReducer.AddResolvedReference
  -> 正式配置投影校验
  -> 唯一 ProductWorkspaceSaveController 提交
  -> session / 工作区 / 候选列表重建
```

## 2. 需求对齐与边界

- 对齐桌面管理基本能力：真实桌面项目第一次可以被主动加入正式方格；
- 使用安全引用：配置保存 canonical target，但不创建目录、不复制或移动项目；
- UI 显示真实 `DisplayName` 和有限类型，这是用户完成选择所必需的本地内容；
- UIA 机器状态只包含序号、类型、generation、索引、revision 和有限结果，不包含名称、路径、内部 ID 或文件身份；
- 首次整理练习区继续使用匿名内存数据，不与正式工作区混合；
- DesktopHost、Shell 虚拟项、文件内容读取和真实拖放仍未接入。

## 3. 并发与一致性

提交必须同时满足：

1. 当前 Catalog 权威且 generation 大于零；
2. UI 候选的 generation 与提交时当前 generation 相同；
3. UI edit revision 与统一 commit coordinator 当前 revision 相同；
4. 方格和 Catalog 索引仍在界内；
5. 目标方格未锁定；
6. 同一 canonical target 尚未出现在任何正式方格；
7. reducer 与正式配置投影均通过；
8. 唯一保存控制器接受提交。

任一条件失败均返回有限状态，不推进 revision、不写配置、不改变桌面文件。成功提交会使旧引用审查、旧方格编辑和布局恢复撤销令牌失效，避免跨动作使用陈旧证据。

## 4. 交互与辅助功能

正式方格编辑区新增三个稳定 AutomationId：

- `ProductWorkspaceResolvedReferenceSelector`；
- `ProductWorkspaceResolvedReferenceAddButton`；
- `ProductWorkspaceResolvedReferenceAddStatus`。

UI 合同由 118 增至 121。选择器和按钮默认禁用；只有会话可编辑、Catalog 权威、存在未分组候选且当前方格未锁定时才开放。状态使用 Polite live region，并始终说明 `DesktopFilesChanged=False`。

## 5. 自动化证据与剩余风险

定向测试覆盖成功提交、单次保存、桌面文件内容不变、旧 revision、旧 Catalog generation、锁定方格、跨方格重复引用以及非法方格/目录索引。仓库级格式、Release 构建、全量测试、覆盖率、121-ID UI 合同、启动/单实例/干净会话预检和远端 CI 必须全部通过后才可合入。

本地 Windows x64 审计结果：Release 构建 0 警告、0 错误；519/519 测试通过；最新单份 Cobertura 为行 91.58%（7132/7787）、分支 81.60%（2014/2468），通过 90%/75% 门槛；121-ID UI 合同、启动链、单实例合同和干净会话 `ValidateOnly` 均通过。

外部 #19、#20、#23、#24 仍保持 Pending。本轮不把源码/UIA 合同当作真人、硬件或真实卷证据，也不解除真实窗口和文件操作阻断。

## 6. 下一步

后续切片已经在不接文件移动的前提下实现已解析引用的显式移除与同代次一次性配置撤销；下一步优先提供跨方格改归属。真实拖放必须在 #19 输入矩阵和 Explorer 数据对象合同通过后单独准入。任务栏美化、小组件与 LPWP 插件运行时继续保持后续阶段。
