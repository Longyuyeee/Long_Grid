# Long Grid

Long Grid 是一款面向 Windows 10/11 的桌面整理与工作空间管理工具。项目当前处于立项与技术验证阶段，目标不是简单复刻某个竞品，而是把“桌面收纳、快速访问、工作空间恢复、自动整理”做成稳定、轻量、可信赖的系统级体验。

> 当前状态：已进入 Phase 0。基础 .NET 8 解决方案、Core、测试和 CI 已建立；物理桌面目录与 Shell Desktop Namespace 的只读发现/对账探针已经通过当前机器验证。正式 DesktopHost 与 MVP 尚未开始。

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
- [贡献指南](CONTRIBUTING.md)
- [小组件与 Long助手插件兼容设计](docs/07-widget-plugin-compatibility.md)
- [Long助手兼容协议交付包](docs/protocol/README.md)
- [Long 插件小组件兼容协议（LPWP）1.0](docs/protocol/LONG_WIDGET_PROTOCOL_V1.md)
- [Long助手 LPWP 实施交接单](docs/protocol/LONG_ASSISTANT_IMPLEMENTATION_HANDOFF.md)
- [LPWP Widget JSON Schema](docs/protocol/long-widget.schema.json)
- [ADR-0001：首选 Windows 技术路线](docs/adr/0001-windows-technology-stack.md)

## 建议的下一步

继续 Phase 0 双轨验证：交互轨建立首次整理、拖放语义和撤销原型；技术轨下一步验证稳定文件身份、Shell 变更通知，并开始 DesktopHost HWND 模型对比。只有体验与技术风险同时通过，再进入 MVP 开发。

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

运行只读探针：

```powershell
dotnet run --project probes/LongGrid.Spikes.DesktopCatalog `
  --configuration Release -- --json

dotnet run --project probes/LongGrid.Spikes.ShellDesktopCatalog `
  --configuration Release -- --json
```

探针默认不输出桌面项目名称或完整路径。报告见[P0-01a：物理桌面目录发现](docs/spikes/P0-01a-desktop-directory-discovery.md)和[P0-01b：Shell Desktop Namespace 枚举与对账](docs/spikes/P0-01b-shell-desktop-namespace.md)。

## 项目边界

Long Grid 与 iTop Easy Desktop、Stardock Fences、Nimi Places、Microsoft PowerToys 等产品不存在隶属或授权关系。竞品仅用于公开能力研究；不得复制其商标、视觉资产、文案、私有协议或实现代码。
