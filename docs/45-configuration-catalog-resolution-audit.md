# Long方格配置到桌面 Catalog 解析审计

日期：2026-08-05

基线：`main` / `c8bd1b3`（PR #95 已合入）+ Issue #24 Catalog 解析增量分支

证据等级：E3 / Core configuration-resolution slice

结论：**Resolved/missing/type-changed/ambiguous/unsupported finite state pass / No silent rebinding or deletion / Issue #24 保持 OPEN**

## 1. 本轮准入边界

上一切片已经能把已解析 Catalog 身份投影到 v1，但 v1 只保存领域 ID、kind 和 target，没有 Volume/File ID。应用重启后不能伪造一个“已解析身份”，也不能看到同名项目就自动改绑。

本切片建立纯 Core 反向合同：

`validated current-v1 document + current DesktopCatalogEntry snapshot → ProductWorkspaceState`

resolver 不枚举文件系统、不访问 Shell、不读取 UI、不打开目标、不修改配置，也不执行任何文件操作。Catalog 必须由调用方在进入 Core 前准备完成。

## 2. 已解析与恢复引用的类型边界

`ProductItemReferenceState` 现在区分：

- `Resolved`：由公开 `CreateResolved` 工厂创建，必须携带 Catalog Entry；
- `Missing`：当前 Catalog 没有目标；
- `TypeChanged`：目标唯一存在，但当前类型与 v1 kind 不同；
- `Ambiguous`：同一 canonical target 出现多个候选；
- `UnsupportedTarget`：v1 target 不是本切片支持的完全限定文件系统路径。

未解析状态只由 Core resolver 的内部恢复入口创建，Catalog Entry 必须为空，同时保留原领域 ID、persisted kind、persisted target 和扩展字段。这样“新建引用必须已解析”和“旧配置缺失引用必须可保留”不会混为同一个无约束构造器。

## 3. 统一身份策略

projector、resolver 和状态工厂共用 `ProductWorkspaceIdentityPolicy`，固定：

- 当前只接受 `filesystem` provider；
- canonical target 必须完全限定并经 `Path.GetFullPath`；
- Windows 文件系统目标按 `OrdinalIgnoreCase` 匹配；
- VolumeId/FileId 必须同时存在或同时缺失；
- File/Directory/Shortcut/InternetShortcut 与 file/folder/shortcut/url 使用同一映射。

统一策略避免写入链和恢复链以后出现 provider、路径或类型判断漂移。

## 4. 解析算法与防误绑

resolver 先通过正式 serializer/validator 深快照 v1；无效配置直接返回 `InvalidConfiguration + ProductConfigurationError`，不会继续消费 Catalog。

随后验证整个 Catalog 快照。任一 Entry 缺少 Identity、SourceId、DisplayName，provider/类型无效，稳定身份只有一半，或 canonical target 不是完全限定路径，整体返回 `InvalidCatalog`，避免把适配器错误伪装成“项目缺失”。

对每个配置引用：

1. target 不支持 → `UnsupportedTarget`；
2. 无匹配 → `Missing`；
3. 多匹配 → `Ambiguous`，即使候选内容相同也不选择；
4. 唯一匹配但类型不同 → `TypeChanged`；
5. 唯一匹配且类型相同 → `Resolved`，恢复当前完整运行期 Catalog 身份。

结果同时返回五类匿名计数；Summary 和失败输出不返回目标路径、候选名称或原始异常。成功的 Core 产品状态仍保留渲染与再次保存所必需的 persisted target，但它不进入诊断摘要。类型变化和歧义都不会用“最像”“第一个来源”或显示名称自动解决。

## 5. 未解析引用的无损重投影

projector 对 `Resolved` 继续只使用当前 Catalog canonical target 和类型；对其余四种恢复状态，原样写回 persisted kind、target、领域 ID 和扩展字段，并固定 `behavior=reference`，最后仍经过正式 v1 validator。

因此刷新 Catalog、打开设置或保存其他容器不会自动删除缺失项，也不会把类型变化/歧义项目绑定到错误对象。调用方原配置和扩展字典在 resolver 返回后被修改，也不会改变已恢复状态。

## 6. 自动证据

- 产品解析、投影与保存工作流定向测试：27/27 通过；
- 覆盖五类状态、匿名汇总、大小写不敏感 canonical 匹配、空 Catalog、重复候选、无效配置优先、无效 provider/路径/半稳定身份 Catalog、全层扩展字段无损重投影和深快照；
- 全量 Release：226/226 通过；
- Debug 与 Release 全解决方案构建：0 warning / 0 error；
- `dotnet format --verify-no-changes`：通过；
- Release 覆盖率：行 91.43%（5630/6158），分支 81.58%（1408/1726），超过 CI 的 90%/75% 门禁；
- 完整启动、Issue #19/#20/#23/#24 会话、DesktopHost、71 个 UI Automation ID、单实例、配置 100/2/2 压力、文件安全、缩略图隔离与依赖漏洞门禁均通过；需要人工、硬件或专用卷的结论继续保持 Conditional/Pending；
- PR/main 双重 CI 作为合入后的最终证据。

## 7. 下一条产品切片

下一步才适合建立真实产品状态 reducer 与保存状态模型：

1. reducer 只接受 resolver 产生的初始状态和已解析 Catalog Entry；
2. 创建/重命名/外观/布局/添加/移除引用产生不可变新状态；
3. Missing/TypeChanged/Ambiguous/UnsupportedTarget 必须有明确保留、移除或重新选择动作，默认保留；
4. 定义 debounce、latest-wins、保存中/已保存/失败/可重试状态；
5. 覆盖连续编辑、关闭、第二实例、Catalog 刷新、导入/恢复和显示变化竞态；
6. 完成 reducer 与 UIA 合同后，才允许 MainWindow 首次普通产品保存入队。

Issue #24 继续保持 OPEN；真实卷证据、自动证据生命周期审批、真实 WinUI/显示矩阵和正式 v2 稳定身份字段也仍未关闭。
