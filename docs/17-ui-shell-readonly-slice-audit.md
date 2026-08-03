# Long方格开发期只读 UI Shell 审计

日期：2026-08-03

状态：**Conditional Pass / 开发验证可用，不是正式 MVP**

风险等级：R1（非破坏性 UI；不接 Shell、文件操作或持久化）

关联要求：[`14-visual-branding-and-delivery-requirements.md`](14-visual-branding-and-delivery-requirements.md)、[`10-development-workflow.md`](10-development-workflow.md)、[`adr/0001-windows-technology-stack.md`](adr/0001-windows-technology-stack.md)

## 1. 为什么现在建立 UI Shell

Phase 0 的 Issue #19–#24、5 人体验测试和负责人支持范围决策尚未全部关闭，ADR-0001 仍为 `Proposed`。因此本轮不能宣称正式 MVP 已开工，也不能创建安装、最低系统或文件整理承诺。

同时，应用关闭、单实例、正式主题、启动链和渲染表面只有在真实 `LongGrid.App` 存在后才能形成证据。为避免循环门槛，本轮建立一个严格受限的开发期 UI Shell：验证项目结构、WinUI 构建、品牌 Token、导航骨架、启动和关闭，不接任何真实桌面能力。

## 2. 技术与依赖决策

| 项目 | 选择 | 依据与边界 |
|---|---|---|
| Runtime | .NET 8 LTS | 延续仓库 `global.json` 与 ADR-0001 |
| UI | WinUI 3 | 仅用于自有管理窗口，不决定 DesktopHost 最终栈 |
| Windows App SDK | `Microsoft.WindowsAppSDK` 2.3.1 Stable | 2026-08-03 官方 Stable 当前版本；禁止 Preview/Experimental |
| 部署形态 | `WindowsPackageType=None`，framework-dependent | 只用于开发启动；不是发行渠道决定 |
| 架构 | x64 | 负责人尚未批准 ARM64 首发；不提前扩大矩阵 |
| 目标 TFM | `net8.0-windows10.0.19041.0` | 编译目标，不构成最低系统市场承诺 |
| 权限 | `asInvoker`、`uiAccess=false` | 不提权、不获取跨进程 UI 访问 |

官方证据：

- [Windows App SDK 下载页](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)在 2026-07-17 将 2.3.1 列为 Stable；
- [Windows App SDK release channels](https://learn.microsoft.com/windows/apps/windows-app-sdk/release-channels)说明 Stable 是受支持的生产通道，Preview/Experimental 不受支持；
- [Microsoft.WindowsAppSDK 2.3.1 NuGet](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.3.1)由 Microsoft/Windows 所有者发布；
- [未打包应用部署指南](https://learn.microsoft.com/windows/apps/windows-app-sdk/deploy-unpackaged-apps)说明 `WindowsPackageType=None` 会启用 framework-dependent 未打包应用的运行时自动初始化。

依赖包要求接受 Microsoft Windows App SDK 软件许可。许可允许在 Windows 上开发、测试并按条款分发 binplaced 文件，但项目自身许可证与公开发行条款仍由 Issue #23 决定；本轮不发布二进制。

## 3. 实现范围

- 新建 `src/LongGrid.App`，依赖方向仅为 `App -> Core`；
- 建立 `App.xaml`、`MainWindow.xaml` 和集中 `Styles/DesignTokens.xaml`；
- 使用 RC1 的 48 px 品牌资产，不复制或重绘竞品素材；
- 提供概览、外观与 Token、安全边界三个导航面板；
- 只显示匿名示例容器，不读取用户名、路径、文件名或桌面截图；
- 使用系统默认 Mica 和系统控件状态，不增加持续动画或自定义输入延迟；
- 提供 `eng/Start-LongGrid.ps1`，支持 Debug/Release、x64、跳过恢复/构建和 `ValidateOnly`；
- CI 在完成 Release build 后调用 `ValidateOnly`，验证统一入口而不打开 GUI。

## 4. 明确非目标与安全不变量

以下能力在本轮必须保持不存在：

- 桌面目录或 Shell Namespace 枚举；
- Shell 变化通知、缩略图、DesktopHost、任务栏或 Explorer 修改；
- 文件移动、删除、重命名、拖放或规则执行；
- 产品配置、注册表、开机启动、网络、遥测或诊断上传；
- 单实例、托盘、MSIX、签名、安装、更新和卸载；
- 对最低 Windows build、ARM64 或公开发行渠道作出承诺。

任何后续 PR 若引入上述能力，必须重新分级并满足对应 R2/R3 门槛，不能借 UI Shell 静默进入。

## 5. 视觉与交互对齐

| 要求 | 本轮证据 | 状态 |
|---|---|---|
| 现代、扁平、精致但克制 | NavigationView、安静表面、有限品牌焦点、无阴影堆叠 | Pass（源码/构建） |
| 4 px 网格 | 间距 Token 为 4/8/12/16/24/32 | Pass |
| 浅色、深色、高对比 | ThemeDictionaries 映射自有表面；高对比引用系统颜色 | Conditional Pass，待实机 |
| 品牌与交互色分工 | 品牌靛蓝只用于识别；系统控件资源承担选择/焦点 | Pass（设计） |
| 平滑且可降级动效 | 本轮没有自定义动画，只使用系统控件默认行为 | Pass（范围） |
| 键盘与 Narrator | NavigationView 具备系统键盘基础，图标有自动化名称 | Pending 人工/UIA 矩阵 |
| 文本缩放与 100%–300% DPI | 使用 XAML 布局和滚动容器 | Pending 实机矩阵 |
| 正常/空/错误/恢复状态 | 本轮只实现“安全只读/未接线”开发状态 | Partial |

## 6. 验证证据

- `dotnet restore src/LongGrid.App/LongGrid.App.csproj --disable-parallel`：通过；首次下载官方包耗时 3 分 41 秒，后续使用锁文件与缓存；
- Debug x64 构建：通过，0 警告、0 错误；
- 本机 Windows App Runtime 2.3.1 x64 存在；
- 通过 `eng/Start-LongGrid.ps1 -Configuration Release -NoRestore -NoBuild` 真实启动：15 秒门槛内得到标题为“Long方格”的可响应窗口，正常关闭后 runner 在 10 秒内以退出码 0 返回：Pass；
- framework-dependent Debug 输出为 51 个文件、约 37.37 MiB；这不是安装包大小或常驻内存预算；
- Windows 捕获工具两次把该未打包窗口错误归属到 OneDrive 应用路径，拒绝返回稳定窗口句柄：视觉截图、导航点击和 UIA 树自动检查为 **Inconclusive**，不得冒充 Pass；
- Release、全解决方案测试、格式、覆盖率、漏洞和 GitHub CI 仍须在 PR 门禁执行。

## 7. 需求对齐与偏移判断

本轮与 Phase 1 的 UI Shell/Design Token/一键启动目标一致，但执行时间早于 ADR-0001 最终状态。该偏移被接受为受控开发切片，理由是：

1. 不形成文件、Shell、安装或支持范围承诺；
2. 不把探针重命名为产品能力，也不复用探针实现；
3. 明确显示“开发期只读 UI Shell”和未接线状态；
4. 只验证必须由真实 App 承载的构建、主题、启动与关闭合同；
5. ADR-0001 保持 `Proposed`，Issue #19–#24 与 5 人测试顺序不变。

如果 UI Shell 后续开始读取真实桌面、写配置、连接 DesktopHost 或创建安装包，而上述门槛仍未关闭，应立即停止并回退到当前只读边界。

## 8. 下一步

在合入本切片后，优先补充可自动执行的 UIA smoke 与主题截图基线，并继续关闭 Issue #23 的体验/负责人决策。只有 ADR-0001 和生产配置合同满足进入条件后，才把匿名示例替换为 Core 的只读桌面引用状态。
