# Long方格正式工作区只读视图审计

> 审计日期：2026-08-06
>
> 范围：正式 session 驱动的容器/引用只读呈现、隐私与辅助功能边界
>
> 结论：代码与自动合同通过后可合入；容器 CRUD、桌面文件操作与真实 UIA 仍不在本切片范围

## 1. 需求对齐

本切片把已有的正式配置、权威 Desktop Catalog 和产品 session 第一次转化为用户可见的真实工作区，而不是继续扩展示例卡片。它对齐最初“桌面文件整理/分组”的第一层展示需求，也为后续容器创建、重命名、锁定、布局编辑和小组件挂载建立稳定读取面。

本轮明确不实现：

- 创建、删除、重命名或移动容器；
- 修改容器布局、颜色、透明度或折叠状态；
- 移动、删除、重命名、打开任何桌面文件；
- Shell 虚拟项、DesktopHost 覆盖层、真实拖放或插件小组件；
- 用静态示例冒充正式产品状态。

## 2. 数据与信任边界

正式读取链固定为：

```text
validated configuration + authoritative catalog
  -> ProductWorkspaceSessionSnapshot
  -> ProductWorkspaceReadModel.Create
  -> v1 projector validation
  -> privacy-minimized read snapshot
  -> App presentation mapping
  -> MainWindow binding
```

`ProductWorkspaceReadModel` 先复用正式 v1 projector 校验完整状态；校验失败只返回有限错误，不产生部分 UI 快照。输出不含 profile/container/item ID、DisplayKey、坐标、持久化 target、canonical target、SourceId 或文件身份字段。

已解析引用的 Catalog `DisplayName` 是产品必须显示的用户内容，因此允许进入可见文本和辅助功能名称。未解析引用没有可信 Catalog 名称，只输出序号、保存类型和有限解析状态，禁止从保存路径、解析名或内部 ID 推导名称。

## 3. 交互与视觉审计

- 正式工作区卡复用现有 Design Token、卡片圆角、间距、主题 brush 和扁平层级；
- 容器按 session 稳定顺序展示名称、锁定状态、引用计数、折叠状态与透明度；
- 已展开容器展示引用名称、类型和解析状态；已折叠容器只保留摘要，符合状态语义并限制渲染量；
- 列表 `SelectionMode=None`、`IsItemClickEnabled=False`，不会暗示尚未准入的编辑能力；
- 不使用 Storyboard 或 Transition，保持 Reduced Motion 安全静态基线；
- 空配置与不可用会话均有诚实空状态，不自动创建示例。

与 iTop Easy Desktop 等竞品的对齐点是“分组内容立即可读、状态信息低干扰、卡片层次清晰”；Long方格保留自己的安全差异：真实编辑能力必须逐项经过配置门禁，读取视图不借 UI 暗示文件已被移动。

## 4. 辅助功能与诊断隐私

新增 5 个稳定 AutomationId，UI 源码合同从 93 增至 98。容器与已解析项目的可见名称同步进入 `AutomationProperties.Name`，以便 Narrator 用户获得与视觉用户等价的信息；这属于当前 UI 可访问内容，不属于匿名遥测。

`AutomationProperties.ItemStatus` 只包含有限枚举、序号、布尔值和计数，不包含名称、路径、ID 或异常文本。未解析引用的辅助功能名称同样保持匿名。自动测试与日志不得打印动态可见名称。

## 5. 自动证据

- Core 新增 5 项只读模型测试：稳定顺序、解析/未解析计数、未解析路径与身份不泄漏、容器展示状态、非法状态有限失败、空工作区；
- UI 源码合同校验 98 个 AutomationId、只读列表、静态动效基线、初始有限状态、session 重建统一刷新、Core/UI/MainWindow 三层边界；
- Debug/Release 全解决方案构建均为 0 warning / 0 error，全量测试 327/327；
- Release 覆盖率 lines 91.53%（8066/8812）、branches 82.51%（1944/2356），高于 90%/75% 门禁；
- 格式、98-ID UI 源码合同、启动、单实例、Issue #19/#20/#23/#24 安全会话链和依赖漏洞门禁均通过；人工与专用环境结果保持 Pending；
- 当前 Windows 会话仍存在无法访问、`MainWindowHandle=0` 的既有 LongGrid.App 进程（PID 39208）；真实 UIA 在 `FindFirst` 返回 `E_UNEXPECTED`，因此保持 Inconclusive。本轮不终止无权限进程，也不把源码合同 Pass 记作真实窗口 Pass。

## 6. 后续开发方向

下一切片应在相同 session/read-model 边界上准入第一组容器配置编辑，建议顺序为：创建/重命名 -> 锁定/折叠 -> 外观 -> 布局。每项都必须复用单调 edit revision、v1 投影、唯一保存控制器提交和内存基线重建，并提供取消、冲突、保存失败和撤销语义。桌面文件操作继续独立审批；插件小组件只能在容器与布局合同稳定后接入 LPWP 沙箱宿主。
