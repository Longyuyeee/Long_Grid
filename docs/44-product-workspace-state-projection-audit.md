# Long方格产品工作区状态与 v1 投影审计

日期：2026-08-05

基线：`main` / `7ab2a74`（PR #94 已合入）+ Issue #24 产品状态投影增量分支

证据等级：E3 / Core product-state projection slice

结论：**UI-independent workspace state + validated v1 projection pass / No ordinary UI save admitted / Issue #24 保持 OPEN**

## 1. 需求复核与准入决定

上一切片已经建立有限保存结果和显式重试，但 MainWindow 中的方格、匿名项目与主题仍是仅内存原型。直接从控件文字、列表索引或匿名示例生成配置，会让展示状态成为数据事实，也无法证明引用对应哪个真实桌面对象。

本切片只准入一条纯 Core 链：

`DesktopCatalogEntry + DesktopItemIdentity → ProductWorkspaceState → current-v1 ProductConfigurationDocument`

该链不依赖 WinUI，不枚举文件系统，不读取 MainWindow 控件，不执行 I/O，也不向匿名原型注入保存入口。它为后续真实桌面目录接线提供唯一、可测试的投影边界。

## 2. 产品工作区状态

新增以下 UI 无关状态：

- `ProductWorkspaceState`：profile、容器集合与待保留扩展字段；
- `ProductContainerState`：领域 ID、名称、锁定、外观、DIP/显示器放置和引用集合；
- `ProductItemReferenceState`：领域 ID、已解析的 `DesktopCatalogEntry` 和待保留扩展字段。

项目状态不接收显示文字作为目标。真实引用必须携带 Catalog Entry；Entry 再携带 provider、canonical target、可选 Volume/File ID、来源、显示名称和类型。投影只消费身份目标与类型，显示名称、SourceId、ParsingName、VolumeId 和 FileId 不写入当前 v1。

这些 record 是不可变快照形状，可用 `with` 产生下一状态；容器与项目列表在进入配置层时再次深快照，调用方随后修改列表或扩展字典不会改变已投影文档。

## 3. v1 投影合同

`ProductWorkspaceConfigurationProjector` 固定执行：

1. 只接受当前已建立的 `filesystem` provider；
2. 要求可选 VolumeId/FileId 同时存在或同时缺失，拒绝半身份；
3. 要求 canonical target 是完全限定路径，并用 `Path.GetFullPath` 规范化；
4. 将 File/Directory/Shortcut/InternetShortcut 确定性映射为 file/folder/shortcut/url；
5. 固定 `behavior=reference`，不准入真实移动；
6. 通过正式 JSON serializer/validator 完成 4 MiB、100 容器、500 项、ID、外观、DIP 和扩展字段复核；
7. 反序列化为脱离调用方可变对象的深快照后才返回成功。

投影结果只公开 `None / InvalidState / UnsupportedIdentityProvider / InvalidCanonicalTarget / ConfigurationRejected` 及现有有限配置错误。失败不返回半成品文档、目标路径或原始异常。

## 4. 保存工作流接线

`ProductConfigurationSaveWorkflow` 新增接收 `ProductWorkspaceState` 的入口。它必须先通过上述投影，成功后才能进入既有深快照/latest-wins 保存队列；投影失败统一成为不可重试的 `InvalidConfiguration`，并取代旧失败快照，避免用户的新无效意图仍暴露旧重试按钮。

App 关闭排空继续使用该工作流，但 MainWindow 仍没有普通保存委托或 `SaveAsync` 调用。当前接线只能描述为“真实状态进入保存层的类型安全入口已建立”，不能描述为真实桌面自动保存已经上线。

## 5. v1 身份限制

运行期 `ProductWorkspaceState` 可以携带 Volume/File ID，辅助识别重命名；正式 v1 schema 只保存 canonical target 和独立领域 ID，不能跨重启保留文件系统稳定身份。本切片明确不把稳定 ID 偷塞进未知扩展字段，因为那会未经 schema 审批定义产品字段。

因此：

- 保存后的配置不包含 Catalog SourceId、显示名称、ParsingName、VolumeId 或 FileId；
- 重启加载必须由后续 resolver 使用当前 Catalog 重新解析 target；
- 目标已重命名、缺失或产生歧义时必须进入可恢复的 unresolved/missing 状态，不能静默绑定到同名对象；
- 如产品决定跨重启保存稳定文件身份，必须以真实 v2 字段、迁移和隐私审计正式准入。

## 6. 自动证据

- 产品状态投影与保存工作流定向测试：17/17 通过；
- 覆盖四种项目类型、只取 canonical target、元数据不落盘、深快照、相对/显示文字目标拒绝、provider 拒绝、半稳定身份拒绝、v1 重复 ID 有限失败、结构失败、工作区直达保存和失败零写入；
- 全量 Release：216/216 通过；
- Debug 与 Release 全解决方案构建：0 warning / 0 error；
- `dotnet format --verify-no-changes`：通过；
- Release 覆盖率：行 91.00%（5276/5798），分支 81.43%（1324/1626），超过 CI 的 90%/75% 门禁；
- 完整启动、Issue #19/#20/#23/#24 会话、DesktopHost、71 个 UI Automation ID、单实例、配置 100/2/2 压力、文件安全、缩略图隔离与依赖漏洞门禁均通过；需要人工、硬件或专用卷的结论继续保持 Conditional/Pending；
- PR/main 双重 CI 作为合入后的最终证据。

## 7. 下一条产品切片

下一切片应建立“配置加载 → 当前 Catalog 解析 → 工作区状态”的恢复合同：

1. 已解析项恢复完整运行期身份；
2. 缺失、类型变化、重复目标和歧义分别返回有限状态；
3. unresolved 引用保留原领域 ID、类型和 target，但不能被误当成新 Catalog Entry；
4. 未知扩展字段在加载、编辑、再投影后继续保留；
5. 解析不读取 UI 文本、不自动删除缺失项、不执行文件操作；
6. 完成恢复合同后，才设计真实产品状态 reducer、debounce/latest-wins 策略和保存状态/重试 UI。

Issue #24 继续保持 OPEN；专用真实卷证据、获批的自动证据生命周期策略、真实 WinUI/显示竞态和正式 v2 迁移也仍未关闭。
