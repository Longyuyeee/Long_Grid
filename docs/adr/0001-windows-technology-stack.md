# ADR-0001：首选 Windows 技术路线

- 状态：Proposed
- 日期：2026-07-29
- 决策者：待项目负责人确认

## 背景

Long Grid 是深度集成 Windows 桌面、Shell、拖放、多显示器和 DPI 的常驻应用。设置体验需要现代 UI，桌面宿主则更重视窗口语义、性能和可靠性。

## 决策

Phase 0 首选：

- C# 与当前受支持的 .NET LTS。
- WinUI 3 + Windows App SDK Stable 构建设置、引导和自有管理窗口。
- DesktopHost 先以 WinUI 3 验证，但最终技术栈由“每容器 HWND”和“每显示器 HWND”探针决定。
- 所有 Win32/COM/WinRT 调用封装在 Infrastructure。
- Core 保持纯 .NET，不依赖 UI 框架。
- 通过探针决定桌面宿主是否继续使用 WinUI Composition，或切换为 WPF/原生 DirectComposition。
- 发布架构不得依赖 `Progman`/`WorkerW`、Explorer 注入或内部 XAML。

不得将 Preview 或 Experimental SDK 用于发布构建。

## 原因

- 微软推荐 WinUI 3 + Windows App SDK 用于新的原生 Windows 桌面应用。
- C# 可提升迭代和测试效率，同时仍能调用 Win32/WinRT。
- 分离 Core 和平台适配器后，桌面宿主技术调整不会推倒业务层。
- 单一 Windows 目标无需承担跨平台 UI 框架的额外抽象成本。

## 备选方案

### WPF + Windows App SDK 互操作

优点：成熟、资料多、窗口行为稳定。
缺点：现代视觉和 Composition 集成需要额外工作。

若 WinUI 3 在桌面层 Z-order、输入、透明度或性能探针失败，WPF 是第一回退方案。

### C++/Win32 + DirectComposition

优点：控制力和性能上限最高。
缺点：开发成本、内存安全和测试复杂度更高。

仅用于被测量证明的热点或独立 DesktopHost，不作为全应用默认路线。

### Electron/Tauri/跨平台 UI

优点：Web 技术生态和跨平台潜力。
缺点：本项目的核心风险在 Windows Shell 与桌面窗口集成，跨平台抽象收益有限，还可能增加常驻资源与互操作复杂度。

当前不采用。

## 后果

- 团队需要掌握 XAML、WinUI 3、Win32 窗口模型、COM 和 DPI。
- 必须建立原生互操作的集中封装与集成测试。
- 技术选型在 Phase 0 结束前仍可逆。
- SDK 版本通过集中依赖管理锁定，并随 Stable 支持周期升级。

## 验证项

- [ ] Win+D、显示桌面、Explorer 重启和全屏应用。
- [ ] 每容器 HWND 与每显示器 HWND 的命中、Z-order、Alt+Tab 和无障碍对比。
- [ ] 用户/Public/重定向桌面枚举、Shell 通知与最终一致性对账。
- [ ] 透明窗口命中测试与拖放。
- [ ] 多显示器、混合 DPI、休眠和热插拔。
- [ ] 500 项目下的渲染、内存和空闲 CPU。
- [ ] MSIX 安装、升级、开机启动和卸载。
- [ ] UI Automation/Narrator 支持。

P0-04/P0-05a 已完成原生命中、Passive 样式和资源子集：当前证据支持下一原型采用“每显示器 HWND + 显式 Window Region”，但 Alt+Tab 实际 UI、Win+D、全屏和 UI Automation/Narrator 尚未验证，因此本 ADR 保持 Proposed，上述验证项不勾选。

P0-07a 已完成静态双屏混合 DPI、拓扑指纹和资源稳定性子集；P0-07b1 已完成 CCD 活动路径、virtual-mode 索引、rotation 和 monitor 一一关联。热切换、真实旋转、负坐标、休眠/RDP 和 `WM_DPICHANGED` 尚未验证，因此多显示器验证项继续保持未勾选。

P0-07b2a 已完成不依赖 UI 框架的恢复规划器，验证精确/相似映射、歧义阻断、DIP 重映射和最小可见性纠正；它只证明 Core 决策合同，不替代真实窗口事务与动态显示矩阵，因此 ADR 仍保持 Proposed。

## 参考

- [Windows 应用开发文档](https://learn.microsoft.com/windows/apps/)
- [Windows App SDK 概览](https://learn.microsoft.com/windows/apps/windows-app-sdk/)
- [.NET 官方支持策略](https://dotnet.microsoft.com/platform/support/policy)
