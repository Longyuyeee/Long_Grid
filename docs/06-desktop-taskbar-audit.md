# 桌面管理与任务栏美化深度审计

审计日期：2026-07-29
范围：iTop Easy Desktop、Stardock Fences、Nimi Places、Portals、PowerToys Workspaces，以及 TranslucentTB、RoundedTB、Windhawk、Start11、TaskbarX 等任务栏工具。

> 2026-08-27 状态更新：任务栏美化已明确为 Core 三支柱之一。R1A/R1B 已交付独立只读 Worker、有界客户端、冲突/build 准入和个性化真实状态；R2A1 已建立 15 秒确认、失败回退和真实强杀后仍可读的恢复凭据；R2A2a 已建立唯一跨进程恢复租约、写操作令牌和 Worker 强杀后内核释放实证。仓库仍没有通过认证的透明度/着色写入、原生恢复执行器或 Windows build 实机矩阵，因此不得描述为任务栏美化已可用。当前顺序见[统一开发计划](PRODUCT_EXECUTION_PLAN.md)。

## 1. 结论先行

桌面整理产品表面上都在画“盒子”，实际可分为三种模型：

1. **桌面项目分组**：在原桌面上组织快捷方式和文件，代表是 iTop Boxes、Fences。
2. **目录门户**：容器直接呈现某个真实目录，代表是 Folder Portals、Nimi Places、Portals。
3. **任务工作空间**：保存应用集合和窗口位置，代表是 PowerToys Workspaces。

Long Grid 最有价值的组合不是把所有工具拼在一起，而是：

> 用 Fences 式低摩擦容器作为入口，用 Portals/Nimi 式目录视图提供真实内容，用 Workspaces 式项目上下文形成差异化；所有真实文件操作均可预览和撤销。

任务栏美化则是另一条技术线。Windows 没有提供完整、稳定的“第三方任务栏换肤 API”。越接近彻底改造，越依赖内部实现、XAML Diagnostics、窗口裁剪、进程注入或 Hook，越容易被 Windows 更新破坏。

因此建议：

- Long Grid 1.0 不注入 Explorer，不替换系统任务栏。
- 桌面盒子、文件夹绑定和任务栏美化共同构成 Long Grid Core。
- 任务栏透明/着色作为独立可恢复组件交付，产品入口固定在“个性化 → 任务栏”；开发期仍使用 Feature Flag 和 Windows build 白名单控制风险。
- 真正可自由设计的圆角 Dock 使用 Long Grid 自己的 AppBar 窗口实现。

## 2. 竞品桌面管理到底是什么样

### 2.1 iTop Easy Desktop：一站式控制台 + 桌面 Boxes

#### 用户看到的形态

iTop 的设置应用是深色侧边栏式控制台，一级模块包括 Boxes、Wallpapers、Personalization、Widgets 和 AI Assistant。桌面整理以半透明矩形 Boxes 呈现，强调“安装后自动分类”，而不是先要求用户设计复杂规则。

公开功能包括：

- 自动按类型归类桌面图标。
- 自定义 Boxes、双击桌面隐藏全部盒子。
- 自动保存布局、新文件提示。
- Folder Portal 与密码保护的 Private Box。
- 壁纸、主题、组件、截图、搜索和 AI 助手。

#### 核心交互判断

- **优势**：首次使用反馈快；统一控制台把整理和美化放在一个入口。
- **不足**：功能跨度很大，用户难以判断哪些模块在联网、常驻或访问文件。
- **Long Grid 可吸收**：首次扫描、建议布局、实时预览、单击应用/撤销。
- **不应吸收**：首版同时加入壁纸库、天气、截图、AI 宠物等外围功能。

#### 实现确定性

iTop 是闭源产品。公开页面只能确认“分组结果”和功能，不足以证明 Boxes 是移动真实桌面项目、改变 Explorer 图标位置，还是维护独立引用层。因此不得把任何一种底层实现写成事实。Long Grid 应明确选择更安全的“逻辑引用优先”模型。

来源：[iTop Easy Desktop](https://www.itopvpn.com/itop-easy-desktop)、[透明任务栏样式说明](https://www.itopvpn.com/desktop-tips/transparent-taskbar-7828)

### 2.2 Stardock Fences：与桌面融合最深的成熟分组

#### 用户看到的形态

Fences 在壁纸上直接显示低对比度、有标题的阴影区域。Fences 6 的官方截图显示：

- 同一容器可有多个顶部标签页。
- 图标仍保持接近 Windows 桌面图标的外观。
- 不同容器可有不同颜色、透明度和标签色。
- 容器可卷起，只留下标题。
- Folder Portal 把真实目录映射到桌面。
- Peek 使用 `Win + Space` 把所有容器显示到其他窗口之上。
- Chameleon 与 Icon Tint 用于降低视觉噪声。

这套设计刻意弱化“应用窗口感”，让容器更像壁纸上的原生桌面层。

#### 成熟度来自哪里

Fences 的更新历史长期集中在：

- 多显示器拓扑变化与布局恢复。
- 250%–400% DPI。
- Explorer 重启、桌面目录迁移。
- 网络盘、云目录和离线 Folder Portal。
- 卷起、拖放、标签页和右键菜单的边缘条件。

这说明桌面整理的主要成本不是画出容器，而是让它在系统变化后仍然正确。

#### Long Grid 可吸收

- 标签页、卷起、Folder Portal、Peek。
- 规则按名称、类型、时间和目标匹配。
- 显示器变化后的快照与交换屏幕内容。
- 容器外观低噪声化。

来源：[Fences 6 官方页](https://www.stardock.com/products/fences/)、[Fences 更新历史](https://www.stardock.com/products/fences/history)

### 2.3 Nimi Places：目录容器与条件显示

#### 用户看到的形态

Nimi Places 的容器显示选定目录中的文件和文件夹，可用图标、缩略图、网格或多列列表展示，并可直接预览支持的媒体。

它比“桌面分类盒子”更接近轻量文件视图：

- 每容器独立主题、强调色、图标尺寸和表面效果。
- 滚动、键盘导航、快速搜索。
- 按名称、扩展名、类型、大小、时间、点击次数和内容排序。
- 容器可按位置、时间、虚拟桌面或前台窗口条件显示。
- 规则可对监视目录中的变更执行复制、移动或创建链接。
- 按显示器/分辨率保存和恢复位置。

#### Long Grid 可吸收

最值得吸收的是“情境容器”：例如仅当 VS Code 或某项目窗口在前台时显示相关资料。但涉及窗口标题时必须注意隐私，默认仅本地计算且不写日志。

来源：[Nimi Places](https://mynimi.net/Projects/Nimi-Places/)、[Nimi Places 功能清单](https://mynimi.net/Projects/Nimi-Places/Features/)

### 2.4 Portals：文件夹就是容器的数据源

#### 用户看到的形态

每个 Portal 对应一个或多个目录标签页，可设置标题位置、最小化状态、排序、背景图、颜色、字体、边框、透明度、图标大小和边距。布局可以绑定显示器配置，显示器变化后自动切换。

其更新历史暴露了真实工程难点：

- Win+D 与桌面层可见性。
- 不同 DPI 显示器的布局匹配。
- 图标/缩略图获取和内存泄漏。
- 大目录虚拟化。
- 拖到其他盘时应复制还是移动。
- 配置保存中断造成空文件。
- Explorer 右键菜单、失效图标和离线目录。

这些问题应直接进入 Long Grid 的测试矩阵。Portals 的公开 GitHub 仓库主要是发行说明和问题跟踪，并非完整源代码，不能据此判断其内部技术栈。

来源：[Portals 官方页](https://portals-app.com/)、[Portals 更新日志](https://portals-app.com/changelog/)

### 2.5 PowerToys Workspaces：从桌面布局升级到任务上下文

Workspaces 不画图标盒子，而是捕获：

- 要启动的应用。
- 应用窗口位置和尺寸。
- 应用启动参数。
- 是否移动已存在窗口。

启动时提供逐应用状态反馈。它适合作为 Long Grid “项目空间”的第二阶段：容器负责文件入口，Workspace 负责应用和窗口。

来源：[PowerToys Workspaces](https://learn.microsoft.com/windows/powertoys/workspaces)

## 3. 桌面管理实现模型

### 模型 A：逻辑引用容器

Long Grid 自己维护项目引用，容器中的图标不是 Explorer 桌面图标本体。

优点：

- 不改变用户真实目录和 Explorer 图标布局。
- 可安全提供标签、同一项目多处引用和撤销。
- 容器渲染完全可控。

缺点：

- 要自行实现 Shell 图标、右键菜单、拖放、重命名和失效路径。
- 原桌面仍可能出现一份图标，需要明确“托管/隐藏”策略。

**建议作为 Long Grid 默认模型。**

### 模型 B：真实桌面项目分组

容器管理用户桌面目录中的真实文件或快捷方式，尽量保持 Explorer 语义。

优点：用户认知直接，和现有桌面一致。
缺点：与 Explorer 图标位置、OneDrive 桌面同步、真实文件移动及恢复强耦合。

只适合作为经过充分验证的兼容模式。

### 模型 C：目录门户

每个容器以某个目录为数据源，相当于嵌入桌面的轻量文件浏览器。

优点：路径和内容关系清楚，天然支持项目目录。
缺点：需要处理大目录、网络盘、云占位、权限、缩略图和文件监控风暴。

单文件夹绑定盒子属于 Core/MVP；多目录标签、复杂 Portal 导航和高风险真实文件操作在后续版本提供。

### 模型 D：窗口工作空间

通过枚举窗口、应用身份、进程及启动参数记录任务上下文。

优点：差异化强。
缺点：单实例应用、管理员窗口、UWP/打包应用、多虚拟桌面和窗口重建都存在限制。

建议作为 V1/V2 独立模块。

## 4. iTop 的任务栏美化是什么样

从 iTop Easy Desktop v4.0 的官方截图可确认，其体验是“样式卡片预设”：

- Transparent。
- Transparent（Translucent Accented）。
- Transparent（Translucent Black）。
- Transparent（Translucent White）。
- Acrylic。
- 选择后卡片显示勾选；再次单击可撤销。

设置入口为 `Personalization > Taskbar`，同一顶栏还包含 Themes、Start Menu、Desktop Icons、Mouse。它更像一键主题选择器，而不是 TranslucentTB 式参数面板。

这类设计的优点是普通用户不用理解 alpha、模糊半径或 XAML 资源；缺点是可解释性较弱，Windows 更新失效时用户只会看到“点了没变化”。

闭源限制：无法从公开信息确认 iTop 在不同 Windows 版本分别使用哪种 API。根据外观和同类开源实现，可以提出候选机制，但不能认定其实际使用其中任何一种。

## 5. 任务栏美化的五条技术路线

### 5.1 系统支持的个性化设置

通过 Windows 自带设置控制深浅色、强调色、透明效果、对齐、自动隐藏和托盘图标。

优点：最稳定、最安全。
缺点：无法实现任意透明度、模糊半径、浮动圆角或分段 Dock。

微软正式 Taskbar API 主要面向应用自己的按钮、进度、缩略图和覆盖图标，并不是系统任务栏换肤 API。

来源：[Windows 任务栏自定义](https://support.microsoft.com/windows/experience/personalization/customize-the-taskbar-in-windows)、[Taskbar Extensions](https://learn.microsoft.com/windows/win32/shell/taskbar-extensions)

### 5.2 修改任务栏窗口的合成属性

经典做法是找到 `Shell_TrayWnd`/副任务栏窗口，然后设置透明、着色、Blur 或 Acrylic。

TranslucentTB 的公开代码可见：

- 动态获取 `SetWindowCompositionAttribute`。
- 使用 `ACCENT_POLICY` 设置透明渐变或 Acrylic。
- 监听前台、最大化、开始菜单、搜索和任务视图等状态，动态切换外观。
- 新版还包含 Explorer XAML 任务栏的适配层。

微软已经记录 `SetWindowCompositionAttribute`，但明确表示“不推荐使用”，建议使用 `DwmSetWindowAttribute`。问题在于 DWM 的正式材质 API主要针对调用者控制的窗口，并没有承诺稳定地重绘 Explorer 的任务栏。

适合：透明、着色、简单动态状态。
风险：跨版本行为不一致，Windows 11 新任务栏可能需要更深入适配。

来源：[TranslucentTB](https://github.com/TranslucentTB/TranslucentTB)、[`SetWindowCompositionAttribute`](https://learn.microsoft.com/windows/win32/dwm/setwindowcompositionattribute)

### 5.3 裁剪任务栏窗口区域

RoundedTB 的代码路径非常直接：

- 查找 `Shell_TrayWnd` 和 `Shell_SecondaryTrayWnd`。
- 用 `CreateRoundRectRgn` 创建圆角区域。
- 用 `SetWindowRgn` 裁剪任务栏窗口。
- 用 `SHAppBarMessage` 和 `SetWindowPos` 协调 AppBar 状态与位置。

这能制造边距、圆角、分段和类似 macOS Dock 的动态宽度，但 README 也明确记录了无抗锯齿、闪烁、自动隐藏不稳定、多屏限制以及与其他 Taskbar mod 的兼容问题。

适合：Windows 10/特定 Windows 11 构建上的圆角实验。
风险：视觉锯齿、命中区域、系统托盘和版本兼容。

来源：[RoundedTB](https://github.com/RoundedTB/RoundedTB)、[Application Desktop Toolbars](https://learn.microsoft.com/windows/win32/shell/application-desktop-toolbars)

### 5.4 修改 Explorer 内部 XAML 可视树

Windows 11 任务栏大量使用 XAML。Windhawk Taskbar Styler 的公开实现：

- 模块目标进程明确包含 `explorer.exe`。
- 通过 `InitializeXamlDiagnosticsEx` 和 XAML Diagnostics 观察可视树。
- 定位 `Taskbar.TaskListButton`、Start Button、背景 Border 等内部元素。
- 修改 `CornerRadius`、`Margin`、`Fill`、`Visibility`、字体、图片及 Acrylic/自定义 Blur。

这种方式能力最强，可以实现 DockLike、Squircle、渐变、图像背景、运行指示器和按钮级样式，但它依赖 Explorer 内部元素名称和结构。Windows 更新改变模板后，主题可能失效、错位或导致 Explorer 重启。

适合：高级用户模组。
风险：进程注入/Hook、安全软件告警、商店审核、更新脆弱性和崩溃影响面。

来源：[Windhawk](https://github.com/ramensoftware/windhawk)、[Windows 11 Taskbar Styler 指南](https://github.com/ramensoftware/windows-11-taskbar-styling-guide)

### 5.5 创建自己的 AppBar/Dock

Windows 正式支持应用使用 `SHAppBarMessage` 注册自己的屏幕边缘工具栏。Long Grid 可以创建完全自有的圆角、透明、Acrylic 项目启动器：

- 外观和动画由我们控制。
- 不修改 Explorer 内部 UI。
- 可显示工作空间、固定项目、最近文件和状态。
- 可为不同显示器设置不同内容。

但它不应伪装成完整系统任务栏。若要复制窗口切换、系统托盘、通知、时钟、输入法、溢出区和辅助功能，成本极高。更合理的定位是 **LongBar 工作空间 Dock**，与系统任务栏并存或由用户自行选择系统自动隐藏。

来源：[Using Application Desktop Toolbars](https://learn.microsoft.com/windows/win32/shell/application-desktop-toolbars)

## 6. 技术路线对比

| 路线 | 效果上限 | 稳定性 | Explorer 风险 | 商店/信任风险 | Long Grid 建议 |
|---|---:|---:|---:|---:|---|
| 系统个性化 | 低 | 高 | 无 | 低 | 默认支持 |
| 外部合成属性 | 中 | 中 | 低至中 | 中 | 实验模块 |
| 窗口区域裁剪 | 中 | 低至中 | 中 | 中 | 不作为首发 |
| Explorer XAML 注入 | 很高 | 低 | 高 | 高 | 核心产品禁止 |
| 自有 AppBar/Dock | 高 | 高 | 低 | 低至中 | 推荐差异化 |

### 代表产品的实际侧重点

| 产品 | 用户得到的效果 | 主要路线/确定性 | 对 Long Grid 的启示 |
|---|---|---|---|
| iTop Fancy Taskbar | Transparent、强调半透明、黑/白半透明、Acrylic 预设 | 闭源；具体实现未知 | 预设卡片和即时撤销值得采用 |
| TranslucentTB | 颜色、Alpha、Clear/Blur/Acrylic、按窗口状态切换 | 源码确认使用任务栏窗口、合成属性，并适配 Win11 XAML | 动态状态比单一透明更实用 |
| RoundedTB | 边距、圆角、分段、动态 Dock 宽度 | 源码确认使用窗口 Region/AppBar API | 裁剪简单但兼容性代价高 |
| Start11 | 颜色、纹理、透明、模糊、圆角、浮动、位置和经典行为 | 闭源；官方只公开能力 | 行为恢复与外观应分开设计 |
| TaskbarX | Windows 10 图标居中、动画、透明/模糊/Acrylic | 开源；官方说明新 Win11 构建已不适用 | 依赖旧任务栏结构会快速老化 |
| Windhawk Taskbar Styler | 按钮、背景、字体、图片、渐变、指示器的深度主题 | 源码确认注入 Explorer 并修改 XAML 可视树 | 效果上限最高，但不适合默认消费级核心 |

来源：[Start11](https://www.stardock.com/products/start11/)、[TaskbarX](https://github.com/ChrisAnd1998/TaskbarX)

## 7. Long Grid 的建议方案

### 第一层：协调美化

- 容器跟随 Windows 深浅色、强调色和高对比度。
- 从壁纸提取色板只在本机完成。
- 预设“清透、磨砂、专注、纯色”四套 Long Grid 容器主题。
- 引导用户进入 Windows 官方个性化设置，不静默篡改系统设置。

### 第二层：核心任务栏外观组件

- 独立组件；默认保持系统外观，但在个性化页始终可发现。
- 只做透明/着色和“最大化窗口时恢复实色”。
- 按 Windows build 白名单启用。
- 每次应用前保存原状态；提供托盘一键恢复和崩溃恢复。
- Explorer 重启后先恢复默认，再由用户设置决定是否重新应用。
- 不承诺跨 Insider 构建可用。

### 第三层：LongBar 工作空间 Dock

- 使用我们自己的 WinUI/Composition 窗口与 AppBar 协议。
- 圆角、Acrylic、阴影、动态宽度和多显示器均由自身控制。
- 展示工作空间、应用、文件夹入口和最近项目。
- 不接管系统托盘和通知，不注入 Explorer。
- 可与 Long Grid 容器共享主题、规则和布局快照。

## 8. 必须建立的任务栏测试矩阵

- Windows 10 与各受支持 Windows 11 构建分别测试。
- 主/副任务栏、多显示器、混合 DPI、顶部/底部/侧边任务栏。
- 自动隐藏、平板/触控、HDR、高对比度和减少动画。
- Start、Search、Task View、通知中心、快速设置打开时的外观。
- 最大化、全屏游戏、远程桌面、睡眠唤醒和 Explorer 重启。
- 与 TranslucentTB、RoundedTB、Start11、StartAllBack、ExplorerPatcher、Windhawk 冲突检测。
- 卸载、崩溃或强杀后能恢复系统默认任务栏。

## 9. 许可证与复用限制

TranslucentTB、RoundedTB 以及 Windows 11 Taskbar Styler 的公开代码均标注 GPLv3。可以研究其公开实现和 API 使用方式，但在 Long Grid 许可证确定前，不应复制源代码。若未来直接链接、修改或分发 GPL 代码，必须进行专门的许可证兼容评估。

本审计提供的是技术事实与架构判断，不构成法律意见。

## 10. 后续实现审计

本文主要回答竞品形态和可借鉴能力。Long Grid 自身的 Shell 数据管线、整理模式、DesktopHost、自有窗口材质、任务栏适配器、LongBar 和 Phase 0 探针，统一以[核心 Windows 能力实现审计](08-core-windows-implementation-audit.md)为准。

竞品交互路径、Long Grid 信息架构、首次启动、容器状态、拖放、规则模拟、恢复、Widget 和工作空间流程，统一以[交互设计审计与体验规范](09-interaction-design-audit.md)为准。
