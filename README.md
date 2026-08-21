# Long方格（Long Grid）

Long方格（Long Grid）是一款面向 Windows 10/11 的桌面整理与工作空间管理工具。项目当前处于立项与技术验证阶段，目标不是简单复刻某个竞品，而是把“桌面收纳、快速访问、工作空间恢复、自动整理”做成稳定、轻量、可信赖的系统级体验。

> 当前状态：产品功能主线按 [Stage 153](docs/153-product-feature-parity-development-plan.md) 逐项推进。Stage 170 已让正式 Release App 在专用临时配置、真实 XAML 和 UI 线程中连续两次通过 Preview 取消—确认—保存—重载证据，且桌面、用户配置和临时目录边界无差异。当前 `WindowsAppRuntime 2.4.0.0 + Microsoft.UI.Xaml.dll 3.2.3.0` 对动态可见 UIA 树仍会触发上游 fail-fast，因此可见 Preview/视图发布、物理输入、UIA/Narrator 与正式撤销证据继续 Pending。PF-002H 保持 `EngineeringComplete / ProductEvidencePending`，PF-001/PF-002 仍为 `InProgress`，详见 [Stage 170](docs/170-pf002-formal-app-inprocess-evidence-audit.md)。所有产物仍不可公开分发。

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
- [当前开发状态、计划对齐与后续验收审计（Stage 159）](docs/159-current-development-status-and-next-plan-audit.md)
- [PF-002D1 可编辑创建预览工程审计（Stage 160）](docs/160-pf002d1-editable-create-preview-audit.md)
- [原生 UIA 激活与前台拒绝恢复审计（Stage 161）](docs/161-native-uia-activation-recovery-audit.md)
- [PF-002D2 桌面候选位置原生预览审计（Stage 162）](docs/162-pf002d2-native-inline-preview-audit.md)
- [PF-002D 正式 App 实机交互尝试审计（Stage 163）](docs/163-pf002d-real-app-interaction-attempt-audit.md)
- [PF-002E 创建保存与可见发布补偿审计（Stage 164）](docs/164-pf002e-create-save-publication-compensation-audit.md)
- [PF-002 桌面拖画矩形创建工程审计（Stage 165）](docs/165-pf002-drag-rectangle-create-engineering-audit.md)
- [PF-002H 已选引用创建方格原子事务基础审计（Stage 166）](docs/166-pf002h-selected-reference-atomic-transaction-audit.md)
- [PF-002H 已选引用创建方格正式 App 接线审计（Stage 167）](docs/167-pf002h-selected-reference-app-integration-audit.md)
- [WinUI 跨进程 UIA 阻断与真实窗口冒烟审计（Stage 168）](docs/168-winui-cross-process-uia-blocker-and-window-smoke-audit.md)
- [WinUI UIA 已知崩溃运行时失败关闭审计（Stage 169）](docs/169-winui-uia-fail-closed-preflight-audit.md)
- [PF-002 正式 App 进程内证据与 WinUI 安全预览审计（Stage 170）](docs/170-pf002-formal-app-inprocess-evidence-audit.md)
- [Phase 0、桌面 MVP 与内部 RC 收尾执行计划](docs/125-phase0-internal-rc-closeout-plan.md)
- [E2a 原子 Intent 消费边界审计](docs/131-atomic-intent-consumption-audit.md)
- [E2b 正式输入源设计审计](docs/132-formal-input-source-design-audit.md)
- [当前开发状态与收尾方向审计（Stage 133）](docs/133-current-development-status-and-closeout-audit.md)
- [正式产品激活源与交互入口审计（Stage 134）](docs/134-formal-product-activation-source-audit.md)
- [正式项目选择与可访问交互审计（Stage 135）](docs/135-formal-item-selection-and-accessibility-audit.md)
- [正式产品 500 项规模预检审计（Stage 141）](docs/141-product-500-item-scale-preflight-audit.md)
- [正式产品故障恢复预检审计（Stage 142）](docs/142-product-recovery-preflight-audit.md)
- [DesktopHost 原生生命周期恢复预检审计（Stage 143）](docs/143-desktop-host-lifecycle-recovery-preflight-audit.md)
- [加速资源长稳预检审计（Stage 144）](docs/144-accelerated-resource-stability-preflight-audit.md)
- [正式 App 24 小时资源长稳会话合同审计（Stage 145）](docs/145-formal-app-resource-stability-session-contract-audit.md)
- [正式 App 匿名资源遥测审计（Stage 146）](docs/146-formal-app-anonymous-resource-telemetry-audit.md)
- [正式受限缩略图 worker 接线审计（Stage 147）](docs/147-formal-restricted-thumbnail-worker-integration-audit.md)
- [M4c2c 资源长稳证据复审门禁审计（Stage 148）](docs/148-m4c2c-resource-evidence-review-gate-audit.md)
- [竞品差距逐项关闭与验收总计划（Stage 150）](docs/150-competitive-parity-gap-closure-plan.md)
- [M4c2c 只读环境预检门禁审计（Stage 151）](docs/151-m4c2c-environment-preflight-gate-audit.md)
- [当前开发状态、需求对齐与收尾顺序审计（Stage 152）](docs/152-current-development-status-audit.md)
- [对标产品功能逐项开发与验收总文档（Stage 153）](docs/153-product-feature-parity-development-plan.md)
- [PF-001 桌面方格总开关实现与验收审计（Stage 154）](docs/154-pf001-boxes-enabled-implementation-audit.md)
- [PF-002 桌面空状态创建入口实现与验收审计（Stage 155）](docs/155-pf002-desktop-empty-create-entry-audit.md)
- [PF-002B 统一创建默认值与连续创建审计（Stage 156）](docs/156-pf002b-deterministic-create-defaults-audit.md)
- [PF-002C 桌面右键、键盘与统一创建请求审计（Stage 157）](docs/157-pf002c-desktop-context-keyboard-create-audit.md)
- [PF-002C2 非空工作区持续桌面创建审计（Stage 158）](docs/158-pf002c2-persistent-desktop-create-audit.md)
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
- [Long方格正式工作区可见搜索审计](docs/97-formal-workspace-visible-search-audit.md)
- [Long方格正式工作区有限排序审计](docs/98-formal-workspace-finite-sort-audit.md)
- [Long方格正式工作区零结果恢复审计](docs/99-formal-workspace-zero-results-recovery-audit.md)
- [Long方格正式空工作区创建入口审计](docs/100-formal-workspace-empty-create-shortcut-audit.md)
- [Long方格正式方格名称即时引导审计](docs/101-formal-container-name-guidance-audit.md)
- [Long方格连续保存修订准入与 CI 确定性审计](docs/102-save-revision-admission-determinism-audit.md)
- [Long方格后续产品开发详细执行计划](docs/103-next-product-development-execution-plan.md)
- [Long方格 CI 内部 RC 运行时恢复确定性审计](docs/104-ci-rc-runtime-restore-determinism-audit.md)
- [Long方格保存控制器测试工作流准入确定性审计](docs/105-save-controller-test-workflow-admission-determinism-audit.md)
- [Long方格 DesktopHost 生命周期与默认关闭开关审计](docs/106-desktop-host-lifecycle-feature-flag-audit.md)
- [Long方格 DesktopHost 单显示器只读产品表面审计](docs/107-desktop-host-single-monitor-readonly-surface-audit.md)
- [Long方格 DesktopHost 每显示器 Generation 批次审计](docs/108-desktop-host-per-display-generation-batch-audit.md)
- [Long方格 DesktopHost 动态拓扑生命周期加固审计](docs/109-desktop-host-dynamic-topology-lifecycle-audit.md)
- [Long方格 DesktopHost 只读 UIA 与产品会话合同审计](docs/110-desktop-host-readonly-uia-session-contract-audit.md)
- [Long方格桌面交互准入与模式状态机审计](docs/111-desktop-interaction-admission-state-machine-audit.md)
- [Long方格桌面交互命中与取消适配器审计](docs/112-desktop-interaction-hit-test-cancellation-audit.md)
- [Long方格桌面交互选择、焦点与 UIA Selection 合同审计](docs/113-desktop-interaction-selection-focus-uia-contract-audit.md)
- [Long方格隔离交互 Surface 与输入模式事务审计](docs/114-desktop-interaction-surface-mode-transaction-audit.md)
- [Long方格原生交互 Surface 适配器探针审计](docs/115-native-interaction-surface-adapter-probe-audit.md)
- [Long方格受控开发态交互 Composition Root 基础审计](docs/116-controlled-development-interaction-composition-root-audit.md)
- [Long方格产品 Hidden/Passive Surface 生命周期审计](docs/117-product-hidden-passive-surface-lifecycle-audit.md)
- [Long方格系统表面事件与 Fail-Closed 桥审计](docs/118-system-surface-event-fail-closed-bridge-audit.md)
- [Long方格产品 Intent 准备与人工会话门禁审计](docs/119-product-intent-preparation-manual-session-gate-audit.md)
- [Long方格系统表面与显示拓扑联合失效会话审计](docs/124-system-surface-display-topology-joint-session-audit.md)
- [Long方格 C2 输入、系统表面与无障碍实机矩阵就绪审计](docs/126-c2-manual-matrix-readiness-audit.md)
- [Long方格 C5a 正式产品存储真实卷会话宿主审计](docs/127-c5a-product-store-volume-session-host-audit.md)
- [Long方格已合并远端分支卫生审计](docs/128-merged-branch-hygiene-audit.md)
- [Long方格外部证据延期与工程主线恢复决策](docs/129-external-evidence-deferment-decision.md)
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

按[后续产品开发执行计划](docs/103-next-product-development-execution-plan.md)推进。B6c7 已把独立系统表面人工会话与权威只读显示拓扑采样合并：probe 自有来源在失焦、Win+D/桌面显示、全屏、会话/RDP、Explorer 身份变化、拓扑指纹变化或非权威读取时立即失效 Prepared 并隐藏；只有系统表面安全且拓扑经过静默期与两个一致样本后，才以非激活 AwaitingPassiveSurface 恢复。它不启动正式 App、不改变系统或显示配置、不进入 Explicit 或文件操作，也不自动写 Pass。下一阶段执行 B6C3 真人矩阵并复核匿名证据，再决定是否进入正式 App 输入接线。A5、B6c2–B6c7 真实会话结果继续 PendingManualEvidence；任务栏美化、小组件/插件运行时和广泛窗口特效属于 MVP 后续。

## 开发启动

在 Windows x64 开发机上使用统一入口启动当前只读 UI Shell：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-LongGrid.ps1
```

该入口默认执行锁定依赖恢复、必要构建和启动；不提权。DesktopHost 按用户级“显示桌面方格”设置启动，首次默认开启；可用 `LONGGRID_DISABLE_DESKTOP_HOST=1` 紧急安全禁用。App 只读枚举用户桌面与公共桌面第一层元数据，不读取文件内容，也不执行桌面文件写入/移动。受控宿主矩阵使用 `eng/Start-DesktopHostProductSessionMatrix.ps1`。显式 Interaction、Intent Bridge 和原生输入转发仍要求各自精确 opt-in 与人工会话确认。当前依赖 Windows App Runtime 2.3.1 x64，缺失或启动失败时返回非零退出码。详细边界见 [Stage 154](docs/154-pf001-boxes-enabled-implementation-audit.md)、[Stage 118](docs/118-system-surface-event-fail-closed-bridge-audit.md)与[Stage 119](docs/119-product-intent-preparation-manual-session-gate-audit.md)。

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

预检固定复核 142-ID 和 8 个关键控件合同，并保持 `PendingManualEvidence`；真实 BSA-01–BSA-05 必须按照[专用运行手册](docs/manual-testing/batch-selection-accessibility-runbook.md)在无个人内容的测试账户逐项执行。

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

> Stage 97 增量：正式工作区可按方格名、有限健康标签和当前可见引用名搜索，并与健康筛选取交集；查询不进入机器状态，折叠隐藏引用不参与匹配。新增搜索框后当前权威 UI 合同为 138-ID，Stage 90–96 的 137-ID 保留为历史状态。

> Stage 98 增量：正式工作区可按配置顺序、名称升降序或待审查优先稳定排列，并与搜索、健康筛选组合；没有可信事实源时不提供最近使用排序。新增排序器后当前权威 UI 合同为 139-ID，Stage 97 的 138-ID 保留为历史状态。

> Stage 99 增量：原工作区有方格但组合条件得到零结果时，显示显式“重置工作区视图”主动作；一次恢复空搜索、全部筛选和配置顺序并移动焦点，不保存配置。新增恢复按钮后当前权威 UI 合同为 140-ID，Stage 98 的 139-ID 保留为历史状态。

> Stage 100 增量：正式工作区确认为空、读模型与编辑器候选精确对齐且允许创建时，显示“开始创建第一个方格”。它只把焦点移到现有名称编辑器，不填充名称、不自动创建、不保存；用户仍需明确点击“创建并保存”。当前权威 UI 合同为 141-ID，Stage 99 的 140-ID 保留为历史状态。

> Stage 101 增量：正式方格名称编辑新增提交前有限状态提示，锁定方格和未变化名称不再开放重命名按钮；提示同时作为名称输入框 HelpText，但不使用 live region 逐键播报。当前权威 UI 合同为 142-ID，Stage 100 的 141-ID 保留为历史状态。

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

dotnet run --project tools/LongGrid.Tools.ProductScalePreflight `
  --configuration Release --no-build
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
