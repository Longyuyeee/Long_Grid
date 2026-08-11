# Long方格（Long Grid）

Long方格（Long Grid）是一款面向 Windows 10/11 的桌面整理与工作空间管理工具。项目当前处于立项与技术验证阶段，目标不是简单复刻某个竞品，而是把“桌面收纳、快速访问、工作空间恢复、自动整理”做成稳定、轻量、可信赖的系统级体验。

> 当前状态：处于 Phase 0/内部 RC 交付收尾阶段。开发期 App 已具备现代 UI Shell、Design Token、品牌 RC1、一键启动、137-ID UIA、响应式布局、正式工作区视图和有限产品会话；用户桌面与公共桌面的第一层元数据已经只读接线。正式方格现会以文字徽标区分“空方格 / 引用正常 / 有引用待审查”，可用标准键盘 ComboBox 按全部、待审查、空或正常状态筛选，在匿名审查快照精确对齐时通过显式按钮聚焦既有审查选择器，并可从每个可见方格卡片按唯一序号直达现有管理选择器或快速折叠/展开；结果通过现有 live region 播报。它不会把缺失或类型变化引用误报成未经验证的“离线”。用户可在 1..256 边界内原子批量添加未分组引用、移除同一方格内引用，或把同一源方格内引用批量改归属到另一正式方格，并通过可键盘访问的有限操作栏管理批量选择。布局恢复、批量加入、批量移除、批量改归属或方格删除中最近一次仍有效的配置编辑会出现在统一即时撤销入口；所有路径继续复用同一强校验令牌与保存控制器。文件内容读取、桌面文件写入/移动和 DesktopHost 窗口执行仍保持零接线。便携 ZIP、unsigned MSIX 及其 SPDX 2.2 SBOM 已具备可复核的单命令链路，PR/main CI 不具备签名、密钥、OIDC write、安装或发布权限。许可证、正式 Publisher/证书、签名安装生命周期、#19/#20/#23/#24 外部矩阵仍未完成，所有产物均不可公开分发。

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
- [Long方格正式方格健康状态审计](docs/88-formal-container-health-state-audit.md)
- [Long方格正式方格健康筛选审计](docs/89-formal-container-health-filter-audit.md)
- [Long方格正式待审查引用快捷入口审计](docs/90-formal-review-shortcut-audit.md)
- [Long方格正式方格卡片直达管理入口审计](docs/91-formal-container-direct-navigation-audit.md)
- [Long方格 CI VSTest 挂起诊断与有界失败审计](docs/92-ci-vstest-hang-diagnostics-audit.md)
- [Long方格 DesktopHost 调度器测试确定性审计](docs/93-desktop-host-dispatcher-test-determinism-audit.md)
- [Long方格正式方格卡片快速折叠审计](docs/94-formal-container-quick-collapse-audit.md)
- [Long方格正式方格卡片单向快速锁定审计](docs/95-formal-container-quick-lock-audit.md)
- [Long方格正式方格卡片操作区自适应布局审计](docs/96-formal-container-card-action-layout-audit.md)
- [Long方格正式容器创建与重命名提交审计](docs/54-container-create-rename-commit-audit.md)
- [Long方格正式容器锁定与折叠提交审计](docs/55-container-lock-collapse-commit-audit.md)
- [Long方格正式容器受限外观提交审计](docs/56-container-finite-appearance-commit-audit.md)
- [Long方格正式容器受限布局预设提交审计](docs/57-container-bounded-placement-commit-audit.md)
- [Long方格产品布局恢复只读预览合同审计](docs/58-product-layout-recovery-preview-contract-audit.md)
- [Long方格产品显示拓扑只读适配器审计](docs/59-product-display-topology-adapter-audit.md)
- [Long方格 v2 保存时显示拓扑合同与迁移审计](docs/60-versioned-saved-display-topology-audit.md)
- [Long方格布局恢复审查令牌与配置级确认审计](docs/61-layout-recovery-review-confirmation-audit.md)
- [Long方格布局恢复一次性配置撤销审计](docs/62-layout-recovery-one-time-undo-audit.md)
- [Long方格真实窗口恢复准入与收口阶段审计](docs/63-real-window-recovery-admission-and-closeout-audit.md)
- [Long方格产品自有窗口注册表与只读 DesktopHost 桥审计](docs/64-product-owned-window-registry-readonly-bridge-audit.md)
- [Long方格配置与产品窗口复合事务审计](docs/65-configuration-window-composite-transaction-audit.md)
- [Long方格 verified-window 批处理适配器审计](docs/66-verified-window-batch-adapter-audit.md)
- [Long方格同步配置暂存适配器审计](docs/67-synchronous-configuration-staging-adapter-audit.md)
- [Long方格 DesktopHost 线程封送与复合故障矩阵审计](docs/68-desktop-host-thread-dispatch-composite-fault-matrix-audit.md)
- [Long方格复合事务生命周期失效与恢复矩阵审计](docs/69-composite-lifecycle-invalidation-audit.md)
- [正式产品配置存储适配器审计](docs/33-product-configuration-store-audit.md)
- [配置 latest-wins 与 App 关闭排空审计](docs/34-configuration-shutdown-drain-audit.md)
- [DesktopHost 输入与关闭排空审计](docs/70-desktop-host-input-shutdown-drain-audit.md)
- [干净会话 UIA 链路审计](docs/71-clean-session-uia-chain-audit.md)
- [便携发布链审计](docs/72-portable-publish-chain-audit.md)
- [MSIX 身份与生命周期审计](docs/73-msix-identity-lifecycle-audit.md)
- [SBOM 与受保护签名边界审计](docs/74-sbom-protected-signing-boundary-audit.md)
- [内部 RC 交付集合与干净检出审计](docs/75-internal-rc-delivery-set-audit.md)
- [真实只读运行边界披露审计](docs/76-truthful-readonly-runtime-disclosure-audit.md)
- [真实桌面项目加入正式方格审计](docs/77-resolved-desktop-reference-add-audit.md)
- [已解析引用移除与一次撤销审计](docs/78-resolved-reference-remove-undo-audit.md)
- [已解析引用原子改归属与一次撤销审计](docs/79-resolved-reference-reassignment-audit.md)
- [正式方格删除与一次撤销审计](docs/80-container-removal-undo-audit.md)
- [批量引用加入与一次撤销审计](docs/81-batch-reference-addition-undo-audit.md)
- [同方格批量引用移除与一次撤销审计](docs/82-batch-reference-removal-undo-audit.md)
- [批量选择操作栏与键盘可达性审计](docs/83-batch-selection-toolbar-accessibility-audit.md)
- [批量选择状态播报与紧凑布局审计](docs/84-batch-selection-live-region-responsive-audit.md)
- [批量选择无障碍人工矩阵就绪审计](docs/85-batch-selection-accessibility-manual-matrix-audit.md)
- [同源方格批量引用改归属与一次撤销审计](docs/86-batch-reference-reassignment-undo-audit.md)
- [批量选择无障碍人工矩阵运行手册](docs/manual-testing/batch-selection-accessibility-runbook.md)
- [贡献指南](CONTRIBUTING.md)
- [小组件与 Long助手插件兼容设计](docs/07-widget-plugin-compatibility.md)
- [Long助手兼容协议交付包](docs/protocol/README.md)
- [Long 插件小组件兼容协议（LPWP）1.0](docs/protocol/LONG_WIDGET_PROTOCOL_V1.md)
- [Long助手 LPWP 实施交接单](docs/protocol/LONG_ASSISTANT_IMPLEMENTATION_HANDOFF.md)
- [LPWP Widget JSON Schema](docs/protocol/long-widget.schema.json)
- [ADR-0001：首选 Windows 技术路线](docs/adr/0001-windows-technology-stack.md)
- [ADR-0002：缩略图工作进程文件隔离](docs/adr/0002-thumbnail-worker-file-isolation.md)

## 建议的下一步

按纠偏后的 `Phase 0 Exit` 顺序推进：真实窗口恢复内部工程链、137-ID 干净会话入口，以及聚合便携 ZIP、unsigned MSIX、SPDX 2.2 和签名隔离状态的一键内部 RC 入口已经建立。App 只读枚举真实桌面第一层元数据，并允许把最多 256 个未分组项目原子加入 Long方格配置、从同一方格原子移除，或把同一源方格内最多 256 个引用原子改归属到另一正式方格；批量列表提供键盘可达的有限选择与清除、空选择状态复位、单次 live-region 播报和紧凑布局重排，也可继续使用标准 Ctrl/Shift 多选。专用 BSA-01–BSA-05 会话链已把纯键盘、Narrator、高对比度、200% 文本缩放和紧凑宽度拆成独立人工场景；CI 只验证安全入口，五项真实结果仍需在专用账户执行。布局恢复、批量加入、批量移除、批量改归属或方格删除中恰好一个仍有效的令牌会投影为统一即时撤销按钮；冲突或畸形状态默认关闭，仍保持零文件内容读取、零桌面文件写入/移动和零 DesktopHost 窗口执行。没有正式 Publisher/证书/许可证/受保护 Release environment 时，交付机械链停止扩张，优先收集 GitHub #19、#20、#23、#24 的真实人工、硬件或专用卷证据。任务栏美化、小组件/插件运行时和广泛窗口特效属于 MVP 后续。

## 开发启动

在 Windows x64 开发机上使用统一入口启动当前只读 UI Shell：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-LongGrid.ps1
```

该入口默认执行锁定依赖恢复、必要构建和启动；不提权，App 启动后只读枚举用户桌面与公共桌面第一层元数据，不读取文件内容，也不执行桌面文件写入/移动或 DesktopHost 窗口操作。当前依赖 Windows App Runtime 2.3.1 x64，缺失或启动失败时返回非零退出码。详细边界见[真实只读运行边界披露审计](docs/76-truthful-readonly-runtime-disclosure-audit.md)。

从干净、已提交的工作树一键生成并交叉验证完整内部 RC 交付集合：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Build-LongGridReleaseCandidate.ps1 `
  -PortableVersion 0.1.0-rcdev `
  -PackageVersion 0.1.0.0
```

该入口依次执行现有打包质量门禁、便携 ZIP、unsigned MSIX、SPDX 2.2 和聚合证据复核。只有所有产物来自同一提交、哈希/sidecar/版本/SBOM subject 一致且签名与安装仍明确阻断，才生成 `internal-rc-evidence.json`。它仍不可安装或公开分发；详细边界见[内部 RC 交付集合与干净检出审计](docs/75-internal-rc-delivery-set-audit.md)。下列三个底层命令保留用于单项诊断。

从干净、已提交的工作树执行完整质量门禁并生成内部便携开发包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Pack-LongGrid.ps1 `
  -Version 0.1.0-dev
```

输出位于 `artifacts/LongGrid-<version>-win-x64.zip` 及同名 `.sha256`。压缩包内含逐文件 `SHA256SUMS.txt`、不可变构建清单和 `Install-Preflight.ps1`；必须解压完整目录并先运行前置检查。该产物自包含 .NET 与 Windows App SDK，但仍未签名、不是安装器、未批准公开分发。详细证据与剩余阻断见[便携发布链审计](docs/72-portable-publish-chain-audit.md)。

从同一干净提交生成并验证未签名 MSIX Developer Preview：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Pack-LongGridMsix.ps1 `
  -PackageVersion 0.1.0.0
```

脚本复用或重建当前提交的便携 payload，生成准确尺寸的 L+方格 MSIX 图标，通过官方 `MakeAppx` 连续打包、双份解包内容指纹比对，并复核身份、最小能力和无签名状态。`MakeAppx` 容器元数据不保证字节级复现，构建清单会如实记录；输出 `.msix`、`.sha256` 和外部构建清单，该包不能安装或发布。生命周期预检和剩余风险见[MSIX 身份与生命周期审计](docs/73-msix-identity-lifecycle-audit.md)。

为当前提交的 unsigned MSIX 生成并验证 SPDX 2.2 SBOM：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/New-LongGridSbom.ps1 `
  -PackageVersion 0.1.0.0
```

脚本恢复仓库固定的 Microsoft SBOM Tool，对 MSIX 解包布局生成并官方验证清单，输出 `.spdx.json`、`.sha256` 和绑定源码/MSIX/SBOM 哈希的证据文件。它不签名、不安装、不上传 Release；详细边界见[SBOM 与受保护签名边界审计](docs/74-sbom-protected-signing-boundary-audit.md)。

Issue #19 人工输入与系统表面矩阵开始前，先验证安全会话链：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-Issue19ManualMatrixSession.ps1 `
  -ValidateOnly
```

正确结果保持 `PendingManualEvidence`；该预检不打开窗口，也不代表任何人工场景通过。

正式工作区批量选择无障碍矩阵开始前，验证专用产品会话链：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-LongGridBatchAccessibilitySession.ps1 `
  -ValidateOnly
```

预检固定复核 137-ID 和 8 个关键控件合同，并保持 `PendingManualEvidence`；真实 BSA-01–BSA-05 必须按照[专用运行手册](docs/manual-testing/batch-selection-accessibility-runbook.md)在无个人内容的测试账户逐项执行。

> Stage 89 增量：正式方格健康筛选新增 1 个 AutomationId，当前权威 UI 合同为 136-ID；Stage 87–88 文档中的 135-ID 保留为历史状态。
>
> Stage 90 增量：正式待审查引用快捷入口新增 1 个 AutomationId，当前权威 UI 合同为 137-ID；Stage 89 文档中的 136-ID 保留为历史状态。
>
> Stage 91 增量：正式方格卡片可通过标准重复按钮按唯一序号直达既有管理选择器；重复实例不分配 AutomationId，当前权威 UI 合同保持 137-ID。
>
> Stage 94 增量：正式方格卡片在双快照唯一匹配、未锁定且折叠状态一致时可就地折叠/展开；重复实例不分配 AutomationId，当前权威 UI 合同保持 137-ID。
>
> Stage 95 增量：正式方格卡片在双快照唯一匹配且两侧未锁定时可单向快速锁定；卡片不提供快速解锁，重复实例不分配 AutomationId，当前权威 UI 合同保持 137-ID。
>
> Stage 96 增量：方格卡片操作区改为管理入口独占首行、折叠与锁定等宽分列的两层 Grid；标准 Tab 顺序保持管理→折叠→锁定，当前权威 UI 合同保持 137-ID。

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
