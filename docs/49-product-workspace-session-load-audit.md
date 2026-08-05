# Long方格正式产品工作区会话加载审计

日期：2026-08-05

基线：`main` / `94f5622`（PR #99 已合入）+ Issue #24 产品会话加载增量分支

证据等级：E2-E3 / formal configuration-to-session loading contract

结论：**Finite product session loading pass / Unavailable catalog is not an empty catalog / App owns session snapshot / Ordinary submissions remain zero / Issue #24 保持 OPEN**

## 1. 审计发现与本轮边界

上一轮 App 已持有连续保存控制器，但启动只把 `ProductConfigurationLoadResult` 转换成存储提示，没有形成正式 `ProductWorkspaceState`。同时开发期 `RuntimeStatusSnapshot` 明确把 Desktop Catalog 标记为 `Disconnected`。如果直接用空数组调用 resolver，会把配置中的全部引用误判为 Missing；如果把匿名示例当 Catalog，又会造成未经授权的身份绑定。

本轮只建立“正式配置加载结果 + 明确可用性的 Catalog 快照 → 有限产品会话”的所有权和状态展示：

- 不扫描桌面，不连接 Shell/DesktopHost；
- 不把匿名容器或四个匿名示例引用放入正式会话；
- App/MainWindow 普通 `SaveAsync/EnqueueAsync` 调用继续为 0；
- 恢复、导入、导出和证据操作仍是用户明确触发的独立配置事务；
- 不启用普通 reducer 编辑，不向保存控制器提交状态；
- 不声称当前已经拥有可用于产品解析的真实 Desktop Catalog。

## 2. Catalog 可用性合同

`ProductWorkspaceCatalogSnapshot` 只有两种受控构造：

- `Unavailable`：目录没有接入，entries 固定为空，但这个空集合绝不具有“当前桌面确实为空”的语义；
- `Available(entries)`：调用方明确声明这是当前受控快照，并在入口复制集合；真实空桌面可表示为 `Available([])`。

加载器只有在 `Available` 时才调用正式 resolver。`Unavailable` 时状态停在 `AwaitingCatalog`，解析计数保持 0，不生成 `ProductWorkspaceState`，也不把任何引用分类成 Missing。两种空集合由类型状态区分，避免静默数据降级。

## 3. 有限产品会话状态

`ProductWorkspaceSessionSnapshot` 覆盖：

- `Loading`：App 尚未完成配置读取；
- `NoSavedConfiguration`：配置确实不存在，不自动创建文件；
- `AwaitingCatalog`：配置有效，但 Catalog 未连接；
- `Ready`：主配置与明确可用的 Catalog 已解析；
- `RecoveredBackupReadOnly`：备份已解析但继续只读；
- `SafeMode`：没有生成产品状态，损坏证据不被覆盖；
- `Failed`：有限失败 `InconsistentLoadResult/InvalidConfiguration/InvalidCatalog`。

快照还保留有限 `Source=None/Primary/RecoveredBackup`、Catalog 可用性、只读标志及 resolved/missing/type-changed/ambiguous/unsupported 匿名计数。只有 Ready 或 RecoveredBackupReadOnly 才携带解析后的 `ProductWorkspaceState`。

配置在 Catalog 不可用时仍先通过正式 validator；伪造的 Loaded 状态无文档、Missing/SafeMode 却携带文档、非法配置或非法 Catalog 均失败关闭，不产生半成品会话。

## 4. App 所有权与事务后刷新

App 新增唯一 `ProductWorkspaceSessionSnapshot` 字段。启动、接受备份、安全重置和确认导入后的复读结果统一经过 `ApplyProductConfigurationLoadResult`：

1. 生成原有存储启动状态；
2. 用正式加载器和当前开发期 `CatalogSnapshot.Unavailable` 生成产品会话；
3. 同时刷新配置恢复提示和产品会话卡；
4. 不触发保存控制器，也不执行文件操作。

因此用户明确完成恢复或导入后，不会出现“存储提示已更新、产品会话仍引用旧状态”的分裂。未来接入只读 Catalog 时只需替换明确的快照来源并重新执行同一加载入口，不需要绕过 resolver。

## 5. 隐私安全 UIA

概览新增 4 个稳定 AutomationId：

- `ProductWorkspaceSessionCard`；
- `ProductWorkspaceSessionTitle`；
- `ProductWorkspaceSessionDetail`；
- `ProductWorkspaceSessionSummary`。

UIA 从 76 增至 80。初始有限状态为 `WorkspaceSessionLoading:Source=None:Catalog=Unavailable:ReadOnly=True`。后续 ItemStatus 只包含状态、来源、Catalog 可用性、只读标志和匿名分类计数；不包含 profile/container/item 名称、canonical target、路径、文件身份、显示名或原始异常。

会话卡使用 Polite live region，且不包含 Storyboard/Transition，保持静态 Reduced Motion 基线。`Ready` 文案也明确说明开发期普通编辑提交仍未开放，避免把“解析成功”误报成“已经启用自动保存”。

## 6. 自动证据

- 产品会话加载器定向测试覆盖 9 个路径：缺少配置、Catalog 未连接、权威空 Catalog、成功解析、备份只读、安全模式、不一致加载、Catalog 未连接前非法配置、非法 Catalog；
- 全量自动测试：284/284 通过；覆盖率 lines 91.28%（6968/7634）、branches 82.04%（1718/2094），继续高于 90%/75% 门禁；
- UI 源码合同：80 个稳定 AutomationId，验证初始有限状态、Polite live region、静态动效、App 会话所有权、Unavailable Catalog 和零普通直写；
- Debug/Release 全解决方案构建：均为 0 warning / 0 error；
- 启动、单实例、Issue #19/#20/#23/#24 安全会话链与依赖漏洞门禁：全部通过；真实人工/专用环境证据继续保持 Pending；
- 真实 UIA 仍受上一轮记录的当前 Windows 会话僵死单实例污染，不在该环境中伪造 Pass；需要干净登录会话复跑。

## 7. 需求对齐与下一步

本轮关闭了“正式配置加载结果没有进入产品会话”和“空 Catalog 语义混淆”两个缺口，但尚未接入真实只读目录数据，也未提供未解析引用的操作界面。

下一条安全切片应：

1. 把已经验证过的 Desktop Catalog/Shell 快照通过受控、只读适配器接入 App，保留断开/失败/刷新代次；
2. 重新加载正式配置并展示 resolved/missing/type-changed/ambiguous/unsupported 匿名计数；
3. 为未解析引用提供只读保留、显式重新选择和显式删除确认，不自动绑定、不自动删除；
4. 在干净 Windows 会话复跑 80-ID UIA、Narrator 和关闭矩阵；
5. 只有真实状态来源、解析 UI 和关闭证据通过后，才启用第一条 reducer 编辑并提交 controller；桌面文件继续零移动。

Issue #24 保持 OPEN；真实卷证据、自动保留/容量策略、正式 v2 字段、完整关闭竞态和跨进程公平性仍未关闭。
