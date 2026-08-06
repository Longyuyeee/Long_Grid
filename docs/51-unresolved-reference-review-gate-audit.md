# Long方格未解析引用审查与双版本门禁审计

日期：2026-08-05

基线：`main` / `b19a2ad`（PR #101 已合入）+ 未解析引用审查增量分支

证据等级：E1-E3 / Core decision gate + WinUI dry-run integration

> 后续状态：本报告记录 Dry-run 阶段；其成功结果已由[引用编辑正式保存提交审计](52-reference-edit-save-submission-audit.md)在下一切片接入 App-owned 保存控制器。

结论：**匿名审查通过 / Catalog generation + edit revision 组合门禁通过 / 显式重选与移除确认通过 / 零普通提交 / 零配置与磁盘修改 / Issue #24 保持 OPEN**

## 1. 本轮解决的问题

正式产品会话已经能把配置引用解析为 `Resolved/Missing/TypeChanged/Ambiguous/UnsupportedTarget`，但此前只能展示汇总计数，用户无法逐项审查，也没有防止“目录刷新后仍使用旧候选”的产品门禁。

本轮新增两个 Core 边界：

- `ProductWorkspaceReferenceReview`：先用正式 projector 验证工作区，再按容器/项目稳定顺序只投影未解析引用；可见层仅使用从 1 开始的匿名序号、有限解析状态和锁定标志；
- `ProductWorkspaceReferenceGate`：每个审查项携带 Catalog generation、edit revision、容器/项目内部 ID 和预期解析状态。执行时重新核对当前权威目录、当前修订、对象存在性、解析状态、锁定状态及候选唯一性。

内部 ID 只用于定位，候选 presentation 也只携带 generation、匿名索引和类型标签；真实 CatalogEntry 只在 App 边界内按同代索引解析。UI 文本与 UIA 不显示 profile、container/item ID、路径、文件名、显示名、canonical target、Volume/File ID 或异常原文。

## 2. 有限操作与失败状态

支持三种明确动作：

1. `Keep`：默认安全语义，不产生 edit；
2. `Replace`：必须由用户从当前匿名候选中明确选择；候选不存在或身份重复时拒绝；成功只生成 reducer 深快照预演；
3. `Remove`：必须明确确认；成功只预演从配置移除引用，不删除、移动或打开桌面文件。

门禁有限失败为 `InvalidState/StaleCatalogGeneration/StaleEditRevision/ItemChanged/ContainerLocked/ConfirmationRequired/ReplacementRequired/ReplacementNotFound/ReplacementAmbiguous/ReducerRejected`。未知 action、无效状态或 reducer 拒绝不会抛出存储细节到 UI。

重选对话框打开前捕获审查 token 与候选快照；即使对话框期间目录刷新或会话变化，确认仍提交旧 token 给 Core，随后由 generation/revision 门禁有限拒绝，不能静默改用新候选。

## 3. WinUI 产品交互

概览新增审查卡：

- 选择框只显示“引用 N + 有限状态”；
- 重选框只显示“候选 N + 文件/文件夹/快捷方式/网址快捷方式”；
- 锁定分组、备份只读会话、非权威 Catalog、无正式状态或无候选时对应操作保持禁用；
- 移除对话框明确声明只影响 Long方格配置引用且本阶段不提交，也不会删除桌面文件；
- 每次结果都明确显示“配置未改变”或 `Committed=False`。

新增 8 个稳定 AutomationId，UIA 总数由 85 增至 93。状态卡使用 Polite live region，不使用 Storyboard/Transition，保持 Reduced Motion 静态基线。

## 4. 零提交边界

本轮故意只开放预演，不调用 `productWorkspaceSaves.Submit`，App/MainWindow 也继续没有普通 `SaveAsync/EnqueueAsync`。Gate 返回的 `ProductWorkspaceEditResult` 是分离深快照，只用于判断“若未来提交是否会改变”，不会替换当前 session、递增 edit revision 或写入 Store。

因此：

- 配置内存状态不变；
- 主配置、备份和证据文件不变；
- 桌面文件不移动、不删除、不重命名；
- 关闭排空仍只处理已存在的 controller 状态，没有本轮产生的新保存任务。

## 5. 自动证据

- Core 定向测试 14/14：匿名稳定顺序、无效版本/状态、generation/revision 过期、对象状态变化、锁定、默认保留、移除确认、重选必选、不存在/歧义候选、领域 ID/未知字段保留与源状态不变；
- UI 源码合同：93 个稳定 AutomationId；初始禁用、Polite live region、静态动效、匿名字段、不透明 generation/index 候选句柄、有限门禁、App 双版本所有权及零 submit 均已断言；
- Debug/Release 全解决方案构建均为 0 warning / 0 error；全量测试 314/314，覆盖率 lines 91.18%（7754/8504）、branches 81.91%（1884/2300），高于 90%/75% 门禁；
- 启动、单实例、Issue #19/#20/#23/#24 安全会话链和依赖漏洞门禁全部通过；人工与专用环境结果仍保持 Pending，不以预检冒充真实证据；
- 真实 UIA 仍需干净 Windows 会话复跑；当前残留无窗口单实例问题不得伪造成 Pass。

## 6. 需求对齐与下一步

本轮完成上一审计要求的“默认保留、显式重选、显式删除确认、generation + revision 组合门禁”，但还没有启用第一条真实产品编辑。

下一切片应在干净会话完成 93-ID UIA 以及 Narrator/文本缩放复核后，选择风险最低的 `Keep` 之外单项引用操作，把 Gate 成功预演得到的 edit 作为唯一输入提交 App-owned save controller，并在接受时递增 edit revision。提交后必须重建正式 session/review，保持 latest-wins、失败可重试、关窗排空、锁定和陈旧 token 拒绝；仍不得触碰桌面文件。

Shell 虚拟项、正式 v2、真实卷、自动证据保留策略、完整人工矩阵与安装包继续 Pending。
