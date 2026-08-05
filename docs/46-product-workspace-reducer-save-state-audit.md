# Long方格产品工作区 reducer 与连续保存状态审计

日期：2026-08-05

基线：`main` / `1be0f58`（PR #96 已合入）+ Issue #24 产品编辑状态增量分支

证据等级：E3 / Core product-state slice

结论：**Immutable edit reducer pass / Debounce and latest-result state contract pass / Timer, workflow adapter and ordinary UI save still pending / Issue #24 保持 OPEN**

## 1. 本轮准入边界

配置与 Catalog 已经可以双向有限转换，但此前仍缺少唯一、可测试的产品编辑入口。若 WinUI 直接修改列表，锁定、未知字段保留、未解析引用和保存脏状态会分散到控件事件中，无法证明连续拖动或重命名不会误删、误绑或用旧保存结果覆盖新编辑。

本切片在 Core 建立两层纯合同：

1. `ProductWorkspaceReducer`：从已验证工作区快照产生不可变下一状态；
2. `ProductWorkspaceSaveStateMachine`：只根据 revision 和有限事件产生下一保存状态及调度命令。

两者不访问文件系统、Shell、注册表、WinUI、计时器或配置存储。`MainWindow` 继续没有普通产品 `SaveAsync/EnqueueAsync` 调用。

## 2. 不可变编辑 reducer

Reducer 现覆盖：

- 创建、重命名和删除容器；
- 更新外观、DIP/显示器放置和锁定状态；
- 添加已解析引用、显式重新选择引用、移除引用；
- 容器、项目及各层扩展字段的深快照；
- 每次编辑前后都经正式 `ProductWorkspaceConfigurationProjector` 和 current-v1 validator 复核。

结果只返回有限错误：`InvalidState`、`ContainerNotFound`、`ItemNotFound`、`ContainerLocked`、`UnresolvedReferenceRequiresConfirmation` 和 `ConfigurationRejected`，并保留 projector/configuration 的细分错误供上层映射。无变化的重命名返回成功但 `Changed=false`，上层不得因此制造脏状态。

容器锁定后，名称、外观、放置、项目和容器删除均被拒绝；只有显式解锁动作可以改变锁状态。所有领域 ID 仍由正式 v1 全局唯一性规则复核，不能在 reducer 内用列表位置代替稳定 ID。

## 3. 未解析引用不会被顺手删除或改绑

`Missing/TypeChanged/Ambiguous/UnsupportedTarget` 默认完整保留。删除单个未解析引用，或删除包含未解析引用的容器，必须由调用方传入明确确认；否则返回 `UnresolvedReferenceRequiresConfirmation`，且不产生新状态。

重新选择是独立的 `ReplaceReference` 动作，只接受当前已解析 `DesktopCatalogEntry`，保留原项目领域 ID和未知扩展字段，并重新经过 provider、canonical target、类型与正式 schema 校验。Reducer 不按显示名、来源顺序或“最像”候选自动绑定。

## 4. 连续编辑保存状态机

状态机公开五个产品状态：

- `Clean`：尚无未保存编辑；
- `WaitingForDebounce`：最新编辑等待防抖；
- `Saving`：某 revision 已发出保存或重试命令；
- `Saved`：最新 revision 已确认保存；
- `Failed`：最新 revision 保存失败，并携带有限失败与 `CanRetry`。

状态机自身不启动线程或计时器，只发出 `ScheduleDebounce/Save/Retry/None` 命令。每次有效编辑递增 revision、清除旧失败和旧重试入口，并请求重新安排防抖。只有当前 revision 的防抖到期能发出 `Save`；旧防抖回调直接忽略。

保存完成必须匹配活动 revision。若保存期间又发生编辑，旧成功不能把新状态标为 `Saved`，旧失败也不能暴露过期重试；最新编辑仍保持 `WaitingForDebounce`。只有最新、可重试失败才能发出 `Retry`，无效配置等不可重试失败保持有限状态。

## 5. 安全与交互对齐

本合同直接落实初始需求中的“零惊吓”和现代交互基础：

- 连续拖动/缩放可以由 UI 高频提交编辑，但保存只依据 revision 合并；
- 锁定容器不能被手势、快捷键或后台刷新绕过；
- 缺失和歧义项目需要明确保留、删除或重新选择，不出现静默变化；
- `Changed=false` 允许 UI 避免无意义的保存动画；
- 保存 UI 以后只能从有限状态派生自动化名称、状态文本和重试可用性。

本轮没有声称已实现实际 300–500 ms 防抖、保存提示动效或 UI Automation。计时值和视觉反馈仍需结合交互规范与真实 WinUI 接线验证。

## 6. 自动证据

- Reducer 与保存状态机定向测试：28/28 通过；
- 覆盖不可变快照、无变化编辑、正式 schema 拒绝、锁定矩阵、未解析引用确认、显式重新选择、未知字段保留、有限未找到错误、最新防抖、成功/失败/重试及陈旧完成隔离；
- 全量 Release：254/254 通过；
- Release 覆盖率：行 91.99%（6202/6742），分支 82.12%（1552/1890），超过 CI 的 90%/75% 门槛；
- Debug/Release 全解决方案构建：0 warning / 0 error；`dotnet format --verify-no-changes`、启动链、71 个 UI Automation ID 合同、单实例源码合同与依赖漏洞门禁通过；
- Issue #19/#20/#23/#24 安全会话预检通过，但正确保持 `PendingManualEvidence/ResultsPending/PendingDedicatedEnvironmentEvidence`；
- PR 与合入后主干 CI 将作为双重远端证据；
- 真实卷、Narrator、输入、动态显示和五人可用性证据继续保持 Conditional/Pending。

## 7. 下一条产品切片

下一步实现应用层连续保存控制器，而不是直接在控件事件中调用存储：

1. 用可替换时钟/调度器执行有界防抖，并把状态机命令映射到 `ProductConfigurationSaveWorkflow`；
2. 深快照每个被接受的 reducer 状态，明确计时取消只取消旧等待、不撤销已接受物理保存；
3. 映射正式保存错误，串起显式重试、关闭排空和新编辑取代旧失败；
4. 为保存中、已保存、失败和重试建立 UIA 合同，不泄露路径、Catalog 名称或原始异常；
5. 覆盖关闭、第二实例、Catalog 刷新、导入/恢复和显示变化与连续编辑的竞态；
6. 上述控制器和 UIA 自动化通过后，才允许 `MainWindow` 首次普通产品保存入队。

Issue #24 继续保持 OPEN；真实卷证据、自动证据保留/容量策略审批、真实 WinUI/显示矩阵和正式 v2 稳定身份字段仍未关闭。
