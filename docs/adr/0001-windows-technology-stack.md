# ADR-0001：首选 Windows 技术路线

- 状态：Proposed
- 日期：2026-07-29
- 产品范围确认：2026-08-04 / ProjectOwner（D23-01–D23-10）
- 最终技术决策者：待 #19/#20、安装和可用性证据完成后确认

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

已批准的首个技术预览边界为 Windows 11 x64、完全本地无账户、仅安全引用、MSIX 目标渠道和类型图标安全回退。Windows 10、ARM64、托管移动、Folder Portal 与未验证 Provider 不属于首发承诺。该产品范围确认不把本 ADR 提升为 `Accepted`；真实输入、Narrator、动态显示、安装和五人测试仍决定最终 `Accepted` 或 `Revised`。

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

P0-07b2b1 已完成不依赖真实定时器的显示变化稳定器，验证事件静默合并、连续一致采样、暂停/恢复、总超时和代次失效。真实 `WM_DISPLAYCHANGE`、`WM_DPICHANGED`、电源/会话通知接入及动态矩阵尚未通过，因此 ADR 状态不变。

P0-07b2b2a 已验证真实隐藏顶层消息窗口、当前会话 WTS 注册、系统 Timer、专用 CCD/DPI 采样线程和资源闭环。当前只观察到 Startup 稳定链，没有诱发真实显示、电源或会话变化，也没有执行 DesktopHost 事务，因此 ADR 继续保持 Proposed。

P0-07b2b2b1 已验证纯 Core 布局事务协调器的审批门禁、四阶段代次核对、提交后逐窗复读以及补偿回滚/回滚验证。Win32 适配器可使用 `BeginDeferWindowPos`/`DeferWindowPos`/`EndDeferWindowPos` 降低多窗口刷新撕裂，但仍必须保留应用前快照和补偿路径；真实 HWND、Region/Composition/UIA 与显示动态矩阵未通过，因此 ADR 继续保持 Proposed。

P0-07b2b2b2a 已在两个隐藏、同线程、探针自有 HWND 上验证 `BeginDeferWindowPos`/`DeferWindowPos`/`EndDeferWindowPos` 正常批量路径、逐窗复读、提交后代次失效补偿和部分变更补偿。负坐标、焦点、被动样式和资源闭环通过，但可见渲染、Region/Composition/UIA、跨线程窗口和真实硬件动态矩阵尚未通过，因此 ADR 继续保持 Proposed。

P0-07b2b2b2b1 已验证 Window Region 的独立捕获、系统所有权转移、逐窗部分失败补偿、代次失效补偿和 GDI 闭环。Region 没有跨 HWND 原子提交能力，因此生产事务必须先关闭输入并保留完整快照；DirectComposition 与 UIA provider 仍未通过，ADR 继续保持 Proposed。

P0-07b2b2b2b2 已验证真实 DirectComposition Root 的 `Commit/WaitForCommitCompletion`、真实 HWND UIA Provider 和 `AutomationElement` 客户端读取。只有 DComp Wait 完成且 generation 仍有效才发布不可变 UIA 快照；失效时重交旧 Root 并恢复 HWND Bounds。DComp 原子性只覆盖同一 device，不能覆盖 Win32 Bounds、Region 或 UIA；可见渲染、Fragment 树、四层故障编排和硬件动态矩阵尚未通过，因此 ADR 继续保持 Proposed。

P0-07b2b2b2b3 已验证固定四层顺序、全层快照、失败层开始的逆序恢复、全部恢复后的统一复读和回滚失败紧急隐藏。真实探针证明 UIA HWND Bounds 依赖要求补偿采用 Restore/Verify 两阶段，不能逐层恢复后立即判定。该结果支持继续采用 WinUI 3 设置端 + 独立原生 DesktopHost，并把可见输入/UIA Fragment 自动验证与 Narrator/硬件动态人工矩阵拆开；ADR 继续保持 Proposed。

P0-07b2b2b2b4a 已验证短时可见 DesktopHost 的复杂/空/复杂 Region 输入门、跨进程穿透和每显示器 HWND 下的真实 UIA FragmentRoot/容器 Fragment 树。输入关闭时 Win32 命中穿透，Fragment 点分派不返回子项，树节点保留只读可发现性并标记 disabled。该结果进一步支持独立原生 DesktopHost，但 Narrator、真实输入、跨进程辅助技术和硬件动态矩阵仍未通过，ADR 继续保持 Proposed。

P0-04/P0-05b1 已验证原生 ToolWindow 可在初次不激活的同时保留用户显式聚焦能力，并以同一状态机支撑键盘命令、UIA SelectionItem/Invoke Pattern 和事件。GDI 系统色只用于交互语义原型，不代表最终渲染选型。由于真实键鼠/Narrator、拖放、系统表面、DPI/高对比和 DirectComposition 最终视觉仍未通过，ADR 继续保持 Proposed。

## 参考

- [Windows 应用开发文档](https://learn.microsoft.com/windows/apps/)
- [Windows App SDK 概览](https://learn.microsoft.com/windows/apps/windows-app-sdk/)
- [.NET 官方支持策略](https://dotnet.microsoft.com/platform/support/policy)
