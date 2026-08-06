# Long方格（Long Grid）

Long方格（Long Grid）是一款面向 Windows 10/11 的桌面整理与工作空间管理工具。项目当前处于立项与技术验证阶段，目标不是简单复刻某个竞品，而是把“桌面收纳、快速访问、工作空间恢复、自动整理”做成稳定、轻量、可信赖的系统级体验。

> 当前状态：处于 Phase 0 收尾阶段。桌面/Shell 数据链、DesktopHost/显示恢复、交互宿主和配置持久化探针均已进入 `main`，主干 CI 与保护规则生效。开发期 App 已具备现代 UI Shell、Design Token、品牌 RC1、一键启动、117-ID UIA、响应式布局、正式工作区视图和有限产品会话。用户桌面与公共桌面第一层通过 generation/latest-wins 只读适配器接入；只有双来源完整成功才作为权威 Catalog。正式 session 可投影为脱敏容器/引用视图；有限容器、引用与布局恢复编辑共享同一 edit revision、v2 投影和 App-owned 保存控制器。v2 保存脱敏显示拓扑；需要映射或可见性纠正时，确认令牌绑定保存/当前拓扑指纹、配置指纹、拓扑 generation 与 edit revision，复核通过后只更新 Long方格配置。所有桌面文件与真实窗口操作仍为零。真实 UIA 复跑受当前 Windows 会话残留无窗口单实例污染而保持 Inconclusive；自动保留/容量阈值未批准。Issue #23 首发范围已批准，许可证延期；5 人测试仍未完成，多数系统能力仍是 Conditional Pass。

## 产品原则

1. **零惊吓**：绝不擅自移动、删除或加密用户文件；所有自动化均可预览、撤销和恢复。
2. **轻量常驻**：启动快、空闲占用低，不干扰 Explorer、游戏、演示和多显示器切换。
3. **本地优先**：核心能力无需登录和联网；用户数据默认只留在本机。
4. **融入 Windows**：遵循系统交互、辅助功能、DPI、主题和快捷键约定。
5. **渐进增强**：先把桌面分组和布局恢复做可靠，再扩展工作空间、插件和智能能力。

## 文档导航

- [仓库与工程审计](docs/00-repository-audit.md)
- [竞品研究与机会地图](docs/01-competitive-analysis.md)
- [产品需求文档（PRD）](docs/02-product-requirements.md)
- [技术架构与数据设计](docs/03-architecture.md)
- [路线图与验收门槛](docs/04-roadmap.md)
- [质量、安全与隐私基线](docs/05-quality-security.md)
- [桌面管理与任务栏美化深度审计](docs/06-desktop-taskbar-audit.md)
- [核心 Windows 能力实现审计](docs/08-core-windows-implementation-audit.md)
- [交互设计审计与体验规范](docs/09-interaction-design-audit.md)
- [开发流程与交付规范](docs/10-development-workflow.md)
- [当前开发状态与后续方向审计](docs/11-development-status-and-direction-audit.md)
- [初始计划对齐与偏移审计](docs/13-original-plan-alignment-audit.md)
- [视觉品牌、动效与交付要求](docs/14-visual-branding-and-delivery-requirements.md)
- [“L + 方格”图标概念审计](docs/15-icon-concept-audit.md)
- [应用图标 RC1 生产审计](docs/16-brand-asset-production-audit.md)
- [开发期只读 UI Shell 审计](docs/17-ui-shell-readonly-slice-audit.md)
- [UI 主题与自动化合同审计](docs/18-ui-theme-automation-contract-audit.md)
- [响应式布局与 DPI 窗口合同审计](docs/19-ui-responsive-dpi-contract-audit.md)
- [Core 只读运行状态接线审计](docs/20-ui-core-readonly-status-contract-audit.md)
- [首次整理模式原型审计](docs/21-first-organization-prototype-audit.md)
- [匿名容器与撤销原型审计](docs/22-anonymous-container-undo-prototype-audit.md)
- [匿名项目与拖放语义原型审计](docs/23-anonymous-items-drop-semantics-audit.md)
- [布局恢复差异原型审计](docs/24-layout-recovery-difference-prototype-audit.md)
- [正式产品配置合同审计](docs/28-product-configuration-contract-audit.md)
- [Issue #21–#22 关闭就绪审计](docs/29-issue-21-22-closure-readiness-audit.md)
- [Issue #19 人工矩阵就绪审计](docs/26-issue-19-manual-matrix-readiness-audit.md)
- [Issue #19 Win32 Unicode 窗口标题边界审计](docs/32-issue-19-win32-unicode-title-audit.md)
- [Issue #19 输入与系统表面运行手册](docs/manual-testing/issue-19-input-system-surface-runbook.md)
- [Issue #20 动态显示矩阵就绪审计](docs/27-issue-20-display-matrix-readiness-audit.md)
- [Issue #20 动态显示与会话运行手册](docs/manual-testing/issue-20-dynamic-display-session-runbook.md)
- [Issue #23 五人可用性测试计划](docs/usability/issue-23-first-organization-test-plan.md)
- [Issue #23 五人测试主持人手册](docs/usability/issue-23-facilitator-runbook.md)
- [Issue #23 五人测试就绪审计](docs/25-issue-23-usability-readiness-audit.md)
- [Issue #23 首发产品决策记录](docs/30-issue-23-product-decision-proposal.md)
- [Issue #24 专用环境就绪审计](docs/31-issue-24-dedicated-environment-readiness-audit.md)
- [Issue #24 生产配置边界专用环境运行手册](docs/manual-testing/issue-24-persistence-boundary-runbook.md)
- [Long方格单实例激活与参数转发审计](docs/35-single-instance-activation-audit.md)
- [Long方格配置恢复状态 UI 审计](docs/36-configuration-recovery-ui-audit.md)
- [Long方格已验证备份接受与损坏证据归档审计](docs/37-validated-backup-acceptance-audit.md)
- [Long方格 SafeMode 安全重置与证据事务审计](docs/38-safe-mode-reset-audit.md)
- [Long方格受限外部配置导入审计](docs/39-bounded-configuration-import-audit.md)
- [Long方格配置导出与匿名证据清单审计](docs/40-configuration-export-evidence-inventory-audit.md)
- [Long方格原始配置证据导出与迁移准入审计](docs/41-configuration-evidence-export-audit.md)
- [Long方格配置证据生命周期基础审计](docs/42-configuration-evidence-lifecycle-foundation-audit.md)
- [Long方格真实产品状态保存与重试合同审计](docs/43-product-state-save-retry-contract-audit.md)
- [Long方格产品工作区状态与 v1 投影审计](docs/44-product-workspace-state-projection-audit.md)
- [Long方格配置到桌面 Catalog 解析审计](docs/45-configuration-catalog-resolution-audit.md)
- [Long方格产品工作区 reducer 与连续保存状态审计](docs/46-product-workspace-reducer-save-state-audit.md)
- [Long方格产品工作区连续保存控制器审计](docs/47-product-workspace-save-controller-audit.md)
- [Long方格 App 保存状态与关闭接线审计](docs/48-app-product-save-status-ui-audit.md)
- [Long方格正式产品工作区会话加载审计](docs/49-product-workspace-session-load-audit.md)
- [Long方格只读物理桌面目录与刷新代次审计](docs/50-readonly-physical-desktop-catalog-audit.md)
- [Long方格未解析引用审查与双版本门禁审计](docs/51-unresolved-reference-review-gate-audit.md)
- [Long方格引用编辑正式保存提交审计](docs/52-reference-edit-save-submission-audit.md)
- [Long方格正式工作区只读视图审计](docs/53-formal-workspace-readonly-view-audit.md)
- [Long方格正式容器创建与重命名提交审计](docs/54-container-create-rename-commit-audit.md)
- [Long方格正式容器锁定与折叠提交审计](docs/55-container-lock-collapse-commit-audit.md)
- [Long方格正式容器受限外观提交审计](docs/56-container-finite-appearance-commit-audit.md)
- [Long方格正式容器受限布局预设提交审计](docs/57-container-bounded-placement-commit-audit.md)
- [Long方格产品布局恢复只读预览合同审计](docs/58-product-layout-recovery-preview-contract-audit.md)
- [Long方格产品显示拓扑只读适配器审计](docs/59-product-display-topology-adapter-audit.md)
- [Long方格 v2 保存时显示拓扑合同与迁移审计](docs/60-versioned-saved-display-topology-audit.md)
- [Long方格布局恢复审查令牌与配置级确认审计](docs/61-layout-recovery-review-confirmation-audit.md)
- [正式产品配置存储适配器审计](docs/33-product-configuration-store-audit.md)
- [配置 latest-wins 与 App 关闭排空审计](docs/34-configuration-shutdown-drain-audit.md)
- [贡献指南](CONTRIBUTING.md)
- [小组件与 Long助手插件兼容设计](docs/07-widget-plugin-compatibility.md)
- [Long助手兼容协议交付包](docs/protocol/README.md)
- [Long 插件小组件兼容协议（LPWP）1.0](docs/protocol/LONG_WIDGET_PROTOCOL_V1.md)
- [Long助手 LPWP 实施交接单](docs/protocol/LONG_ASSISTANT_IMPLEMENTATION_HANDOFF.md)
- [LPWP Widget JSON Schema](docs/protocol/long-widget.schema.json)
- [ADR-0001：首选 Windows 技术路线](docs/adr/0001-windows-technology-stack.md)
- [ADR-0002：缩略图工作进程文件隔离](docs/adr/0002-thumbnail-worker-file-isolation.md)

## 建议的下一步

按纠偏后的 `Phase 0 Exit` 顺序推进：Issue #23 首发范围已批准，仍需完成 5 人验证、#19 输入/Narrator/系统表面和 #20 动态显示矩阵；#24 已具备正式配置存储、有限产品会话、权威门禁只读 Catalog、有限产品编辑、生产级只读当前拓扑、v2 保存时拓扑，以及带双拓扑/配置指纹和双 revision 门禁的配置级恢复确认。下一步应建立配置恢复前后快照与一次性撤销合同，仍不连接 DesktopHost 或移动真实窗口；同时需在干净会话关闭 117-ID UIA Inconclusive。Shell 虚拟项、DesktopHost 真实提交、自动保留/容量策略和真实测试卷仍 Pending。许可证选择延期到正式分发或接受外部贡献之前。

## 开发启动

在 Windows x64 开发机上使用统一入口启动当前只读 UI Shell：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-LongGrid.ps1
```

该入口默认执行锁定依赖恢复、必要构建和启动；不提权、不枚举真实桌面，也不执行文件操作。当前依赖 Windows App Runtime 2.3.1 x64，缺失或启动失败时返回非零退出码。详细边界见[开发期只读 UI Shell 审计](docs/17-ui-shell-readonly-slice-audit.md)。

Issue #19 人工输入与系统表面矩阵开始前，先验证安全会话链：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-Issue19ManualMatrixSession.ps1 `
  -ValidateOnly
```

正确结果保持 `PendingManualEvidence`；该预检不打开窗口，也不代表任何人工场景通过。

Issue #20 动态显示与会话矩阵开始前，先验证只读 observer 会话链：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-Issue20DisplayMatrixSession.ps1 `
  -ValidateOnly
```

预检固定保持 `PendingManualEvidence`；不会修改显示、设备、电源或会话状态。

执行 Issue #23 五人测试前，先验证匿名会话链：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-Issue23UsabilitySession.ps1 `
  -ValidateOnly
```

真实会话必须按照[主持人手册](docs/usability/issue-23-facilitator-runbook.md)分别使用 P1–P5 启动全新进程。预检和 CI 通过只代表入口就绪，测试状态仍是 `Results Pending`。

执行 Issue #24 真实卷边界测试前，先验证专用环境会话合同：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-Issue24PersistenceBoundarySession.ps1 `
  -ValidateOnly
```

预检固定保持 `PendingDedicatedEnvironmentEvidence`，不写卷、不填盘、不改变卷状态，也不运行配置探针。真实 I24-01/I24-02 只能按照[专用环境运行手册](docs/manual-testing/issue-24-persistence-boundary-runbook.md)在可恢复的独立测试卷执行。

## 仓库验证与故障排查

需要 .NET SDK `8.0.400` feature band 或兼容 patch：

```powershell
dotnet restore LongGrid.sln
dotnet build LongGrid.sln --configuration Release --no-restore
dotnet test LongGrid.sln --configuration Release --no-build
dotnet format LongGrid.sln --verify-no-changes --no-restore
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Verify-VulnerablePackages.ps1
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Test-LongGridUi.ps1 -ContractOnly
```

在可交互 Windows 会话中执行真实窗口、宽/紧凑布局、导航焦点与内存态主题往返冒烟：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Test-LongGridUi.ps1 -Configuration Release
```

运行 Phase 0 桌面身份探针：

```powershell
dotnet run --project probes/LongGrid.Spikes.DesktopCatalog `
  --configuration Release -- --json

dotnet run --project probes/LongGrid.Spikes.ShellDesktopCatalog `
  --configuration Release -- --json

dotnet run --project probes/LongGrid.Spikes.FileIdentity `
  --configuration Release -- --json

dotnet run --project probes/LongGrid.Spikes.ShellChangeNotifications `
  --configuration Release -- --json

dotnet run --project probes/LongGrid.Spikes.ShellItemImages `
  --configuration Release -- --json

dotnet run --project probes/LongGrid.Spikes.DesktopHostWindowModels `
  --configuration Release -- --json

dotnet run --project probes/LongGrid.Spikes.DesktopHostWindowModels `
  --configuration Release -- --batch-transaction --json

dotnet run --project probes/LongGrid.Spikes.DesktopHostWindowModels `
  --configuration Release -- --region-transaction --json

dotnet run --project probes/LongGrid.Spikes.DesktopHostWindowModels `
  --configuration Release -- --composition-uia --json

dotnet run --project probes/LongGrid.Spikes.DesktopHostWindowModels `
  --configuration Release -- --composite-transaction --json

dotnet run --project probes/LongGrid.Spikes.DesktopHostWindowModels `
  --configuration Release -- --visible-input-uia --json

dotnet run --project probes/LongGrid.Spikes.DesktopHostWindowModels `
  --configuration Release -- --interactive-slice-smoke --json

# 人工交互原型；点击聚焦，Esc 退出
dotnet run --project probes/LongGrid.Spikes.DesktopHostWindowModels `
  --configuration Release -- --interactive-slice

dotnet run --project probes/LongGrid.Spikes.DisplayTopology `
  --configuration Release -- --json

dotnet run --project probes/LongGrid.Spikes.DisplayTopology `
  --configuration Release -- --watch-seconds 3 --json

dotnet run --project probes/LongGrid.Spikes.DisplayTopology `
  --configuration Release -- `
  --matrix-scenario baseline --watch-seconds 5 --json

dotnet run --project probes/LongGrid.Spikes.ConfigurationPersistence `
  --configuration Release -- `
  --iterations 1000 --kill-iterations 10 `
  --acl-kill-iterations 10 --json
```

真实桌面始终只读；变化压力测试仅发生在自动清理的临时沙箱，图像探针不把位图写入磁盘。报告默认不输出桌面项目名称、路径、扩展名、PIDL、稳定 ID 或逐项错误。详见[Phase 0 探针报告目录](docs/spikes/README.md)。

## 项目边界

Long Grid 与 iTop Easy Desktop、Stardock Fences、Nimi Places、Microsoft PowerToys 等产品不存在隶属或授权关系。竞品仅用于公开能力研究；不得复制其商标、视觉资产、文案、私有协议或实现代码。
