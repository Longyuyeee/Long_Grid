# Long Grid

Long Grid 是一款面向 Windows 10/11 的桌面整理与工作空间管理工具。项目当前处于立项与技术验证阶段，目标不是简单复刻某个竞品，而是把“桌面收纳、快速访问、工作空间恢复、自动整理”做成稳定、轻量、可信赖的系统级体验。

> 当前状态：处于 Phase 0 收尾阶段。桌面/Shell 数据链、图像资源、DesktopHost 原生窗口模型、静态双屏混合 DPI、CCD 路径、Core 恢复规划、四层补偿事务、可见输入门、UIA Fragment 树及首个可交互宿主切片已进入 `main`；PR #18 的配置持久化探针已取得 1,000 次四检查点真实强杀等 Conditional Pass 证据，等待审核合入。真实键鼠/触控/拖放/Narrator/系统表面、显示硬件动态矩阵、文件安全、真实卷故障、性能和可用性验证尚未关闭，正式 MVP 尚未开始。

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
- [贡献指南](CONTRIBUTING.md)
- [小组件与 Long助手插件兼容设计](docs/07-widget-plugin-compatibility.md)
- [Long助手兼容协议交付包](docs/protocol/README.md)
- [Long 插件小组件兼容协议（LPWP）1.0](docs/protocol/LONG_WIDGET_PROTOCOL_V1.md)
- [Long助手 LPWP 实施交接单](docs/protocol/LONG_ASSISTANT_IMPLEMENTATION_HANDOFF.md)
- [LPWP Widget JSON Schema](docs/protocol/long-widget.schema.json)
- [ADR-0001：首选 Windows 技术路线](docs/adr/0001-windows-technology-stack.md)

## 建议的下一步

先审核并合入 PR #18，再关闭已被 `main` 完整包含的 PR #2–#17、删除对应旧分支并启用主分支保护；随后继续 Phase 0 双轨验证。交互轨完成首次整理、引用/移动语义、撤销和 5 人可用性测试，技术轨完成键鼠/触控/拖放/Narrator、Win+D、全屏、Explorer 重启、动态显示、文件安全、缩略图隔离与 500 项性能矩阵，配置侧只补真实卷、应用关闭、单实例和正式 schema 边界。负责人确认许可证、支持矩阵和首版整理模式并批准 ADR-0001 后，才建立第一个只读 MVP 垂直切片。详见[当前开发状态与后续方向审计](docs/11-development-status-and-direction-audit.md)。

## 开发与验证

需要 .NET SDK `8.0.400` feature band 或兼容 patch：

```powershell
dotnet restore LongGrid.sln
dotnet build LongGrid.sln --configuration Release --no-restore
dotnet test LongGrid.sln --configuration Release --no-build
dotnet format LongGrid.sln --verify-no-changes --no-restore
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Verify-VulnerablePackages.ps1
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
