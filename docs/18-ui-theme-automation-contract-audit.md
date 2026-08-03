# Long方格 UI 主题与自动化合同审计

日期：2026-08-03

状态：**Conditional Pass / 自动化基础通过，人工辅助功能与视觉矩阵未关闭**

风险等级：R1（仅进程内 UI 状态；无文件、Shell、配置或系统主题写入）

基线：`main` / `860d77a`（已合入 PR #66 的只读 UI Shell）

## 1. 本切片目标

上一切片已经证明正式 App 能恢复、构建、启动和正常关闭，但自动截图工具无法稳定归属未打包窗口，导致导航和 UIA 也被一并记为 `Inconclusive`。本切片不绕过视觉问题，而是把可机器验证的内容从人工/视觉矩阵中拆开：

- 为正式 App 建立稳定且唯一的 AutomationId；
- 提供明确的导航访问键和 UIA 键盘焦点证据；
- 实现跟随系统、浅色、深色三个进程内主题状态；
- 建立 CI 可执行的源码合同和本机可执行的真实窗口 UIA 冒烟；
- 保持桌面、DesktopHost、配置、文件和 Windows 系统主题完全不变。

## 2. 实现与合同

### 2.1 UI 交互

- 导航入口 `NavOverview`、`NavAppearance`、`NavSafety` 分别提供 `Alt+1`、`Alt+2`、`Alt+3` 访问键；
- 根节点、NavigationView、三个页面区段、三个主题选项和主题状态文本共 12 个稳定 AutomationId；
- 根界面与页面区段显式进入 UIA Content 视图并提供语义名称，辅助技术可以识别当前页面容器；
- 外观页提供“跟随系统 / 浅色 / 深色”单选项；切换只设置 `RootLayout.RequestedTheme`；
- 主题状态明确标注“仅内存”，以 `Polite` LiveSetting 暴露变化；关闭应用后不保存。

### 2.2 自动化入口

`eng/Test-LongGridUi.ps1 -ContractOnly`：

- 解析 XAML，要求 12 个 AutomationId 唯一存在；
- 固定三个访问键与三个主题事件绑定；
- 检查 `Default/Light/Dark` 三种 RequestedTheme 映射；
- 扫描 code-behind，阻止文件、目录、桌面目录、Shell 通知和 DesktopHost 接线越界；
- 不启动 GUI，适合无交互桌面的 CI runner。

`eng/Test-LongGridUi.ps1`：

- restore/build 后直接启动本次构建的准确 `LongGrid.App.exe`；
- 使用该进程的 `MainWindowHandle` 附着 UIA，不依赖窗口截图工具的应用归属；
- 验证标题、NavigationView 与三个导航项；
- 验证三个导航项均可键盘聚焦，并将焦点移动到外观入口；
- 通过 SelectionItem/Invoke Pattern 执行导航和主题交互；
- 验证“跟随系统 → 深色 → 跟随系统”状态文本往返；
- 切换安全边界页并验证语义容器可见；
- 正常关闭窗口；只有 5 秒内无法关闭时才终止脚本自己启动的准确 PID。

## 3. 安全与需求对齐

| 要求 | 证据 | 判定 |
|---|---|---|
| 现代主题能力 | 系统主题跟随、浅色、深色均使用 WinUI RequestedTheme | Pass（进程内） |
| 不擅自修改系统 | 不调用系统主题、注册表、配置或文件 API | Pass |
| 键盘可达 | 导航访问键；UIA `IsKeyboardFocusable` 与焦点移动通过 | Conditional Pass，待人工完整顺序 |
| 自动化稳定 | 12 个唯一 ID；真实进程句柄 UIA 往返 | Pass（本机场景） |
| CI 可复读 | `-ContractOnly` 不依赖桌面会话 | Pass（本地）；待 PR CI |
| Narrator/高对比/缩放/DPI | AutomationId 不能替代听读与视觉检查 | Pending |
| 平滑动效 | 仍只使用系统控件默认行为，无新增持续动效 | Pass（范围） |
| 真实桌面安全 | 无枚举、通知、移动、删除、DesktopHost 或配置写入 | Pass（源码边界） |

本切片对齐 Phase 1 的主题、键盘和自动化基础，但仍是 E2 开发集成证据。它没有把 UI Shell 升级为桌面整理 MVP，也没有改变 Issue #19–#24、ADR-0001 或 5 人体验验证的进入顺序。

## 4. 本地验证记录

- UI 结构合同：12 个 AutomationId、3 个访问键、3 个主题模式、只读边界全部 Pass；
- 锁定恢复与全解决方案 Release 构建：通过，0 警告、0 错误；
- `dotnet format --verify-no-changes`：通过；
- Core 回归：88/88 通过；行覆盖率 91.28%（2366/2592），分支覆盖率 77.39%（582/752），通过 90%/75% 门禁；
- Release 启动链 `-ValidateOnly`：通过；
- 真实 UIA：窗口标题 `Long方格`、三个导航项发现与键盘聚焦、外观选择、主题 system-dark-system 往返、安全页选择均 Pass；
- 冒烟正常关闭其启动的 App 进程；未读取或修改真实桌面；
- 配置持久化 20 个场景、文件操作安全探针：通过；文件探针仍按既有边界保持 Conditional Pass；
- 缩略图 worker 隔离矩阵：安全分类、预算与清理通过，整体仍为既有 Conditional Pass；本机 AppContainer 对合成样本安全拒绝，不宣称提取兼容；
- 依赖漏洞门禁：未发现已知漏洞；
- PR #67 的 CI 首次执行中，新增 UI 合同、构建、测试、覆盖率、配置与文件安全均通过；既有缩略图探针仅因 parent-exit 场景的一次 AppContainer profile 删除返回 false 而失败，分支未修改该探针；
- 对同一 commit 重跑完整作业后，缩略图隔离与其余全部步骤在 3 分 40 秒内通过。该结果支持“runner 瞬时 profile 释放竞态”判断，但不消除既有探针的 Conditional Pass 边界；若再次发生，应单独修复/验证有界删除重试，而不在 UI PR 中静默扩展范围。

## 5. 未关闭项与下一步

以下项目不能因本次 UIA Pass 自动升级：

1. 浅色、深色和高对比截图基线；
2. Narrator 实际朗读名称、顺序、状态与操作反馈；
3. Tab/Shift+Tab、访问键、方向键和焦点可见性的人工完整矩阵；
4. 100%–300% DPI、文本缩放、窄窗口与多显示器视觉检查；
5. 减少动画、低性能和远程桌面场景；
6. 至少 5 人对“引用/移动/安全边界”的无提示体验验证。

下一产品切片仍应优先关闭体验与负责人决策。若进入代码开发，只允许在批准的生产配置合同下接入匿名/沙箱化的只读 Core 状态；不得从本主题切换直接跨越到真实文件整理。
