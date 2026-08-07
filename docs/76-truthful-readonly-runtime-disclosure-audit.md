# Long方格真实只读运行边界披露审计

日期：2026-08-07
结论：**Pass（产品文案、运行状态与真实接线重新一致；未扩大权限）**

## 1. 发现

`LongGrid.App` 启动时已经通过 `ProductDesktopCatalogReader.CreateForCurrentUser()` 只读枚举用户桌面与公共桌面的第一层项目，并把完整代次用于正式工作区引用解析。Catalog 卡和 Core 运行快照也会在完整读取后显示 `ConnectedReadOnly`。

但概览顶部仍写着“当前界面不扫描真实桌面”和“下方内容均为匿名示例数据”。这两句属于早期 UI Shell 的历史描述，已经与当前代码事实冲突，会让用户无法判断哪些区域使用真实元数据、哪些区域仍是练习数据。

## 2. 本轮需求对齐

本轮不新增系统权限、不读取文件内容、不开放文件移动，也不连接 DesktopHost。只统一三个事实：

- 概览目录与正式工作区：只读使用用户桌面和公共桌面第一层元数据；
- 首次整理、拖放与恢复练习：继续使用匿名内存数据；
- 执行边界：不读取文件内容，不移动、重命名、删除或写入桌面文件，DesktopHost 执行保持关闭。

这与“零惊吓、本地优先、渐进增强”的最初要求一致，也避免把只读发现误称为完全未接线。

## 3. 实现与门禁

- 概览保留持续可见的数据范围说明，不依赖会被配置恢复状态替换的 InfoBar 文案；
- 启动 InfoBar 改为真实的只读启动说明，配置加载后仍按既有有限状态展示；
- `eng/Test-LongGridUi.ps1` 结构门禁要求同时出现真实元数据范围、匿名练习区、零文件内容读取、零桌面写入和零 DesktopHost 执行；
- 门禁明确禁止旧的“下方内容均为匿名示例数据”和“不扫描真实桌面”重新进入界面；
- 活跃 README、路线图、开发状态审计和视觉交付要求同步修正，历史切片文档保留当时事实，不回写成伪历史。

## 4. 未改变的风险与后续方向

- Issue #19 干净交互会话仍被当前无权限管理的外来无窗口进程阻挡，本轮不终止该进程，也不伪造 live UIA 证据；
- Issue #20 动态显示/会话矩阵、Issue #23 五人无提示测试、Issue #24 真实卷矩阵仍需外部环境证据；
- 下一产品开发阶段仍应先完成 Phase 0 外部证据收口，再进入桌面分组/引用的真实只读 MVP 体验；
- 任务栏美化、小组件/Long助手插件运行时和广泛窗口特效继续属于 MVP 之后的分阶段能力，不在本轮扩展。

## 5. 验收

合入前必须通过：

1. Core 单元测试；
2. UI XAML/代码结构合同；
3. Release 构建；
4. 仓库统一验证入口；
5. PR CI。

## 6. 本地审计证据

2026-08-07 在 Windows x64 开发环境复核：

- `dotnet format LongGrid.sln --verify-no-changes --no-restore`：Pass；
- `dotnet build LongGrid.sln --configuration Release --no-restore --disable-build-servers`：Pass，0 警告、0 错误；
- `dotnet test LongGrid.sln --configuration Release --no-build`：Pass，512/512；
- 覆盖率门禁：行 91.17%，分支 81.65%，均高于 90%/75% 门槛；
- `eng/Test-LongGridUi.ps1 -ContractOnly`：Pass，118 个必需 AutomationId；
- 启动链与单实例 source contract：Pass；
- `eng/Test-LongGridCleanSession.ps1 -ValidateOnly`：Pass，`liveEvidence=PendingCleanInteractiveSession`、`terminatesForeignProcess=false`。

最后一项只证明链路安全且可执行，不替代 Issue #19 的真实干净交互会话证据。
