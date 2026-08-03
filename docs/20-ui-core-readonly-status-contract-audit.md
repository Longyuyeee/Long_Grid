# Long方格 Core 只读运行状态接线审计

审计日期：2026-08-03

基线：`main` / `7721384`（PR #68 已合入）

结论：**Conditional Pass / E1-E2 开发集成，不是桌面数据接线**

## 1. 本切片解决什么

上一阶段的三张概览状态卡把“只读 UI Shell”“文件操作关闭”“DesktopHost 未接线”直接写在 XAML 中。视觉结果正确，但这些文字不是来自 Core 合同，未来接入正式适配器时容易形成两套状态来源。

本切片建立最小单向链：

`LongGrid.Core.Runtime.RuntimeStatusSnapshot -> LongGrid.App.MainWindow -> UI Automation`

它只把当前已经成立的安全事实变成不可变状态：

- 运行模式为 `DevelopmentReadOnly`；
- Desktop Catalog 为 `Disconnected`；
- 文件操作为 `DisabledBySafetyPolicy`；
- DesktopHost 为 `Disconnected`。

## 2. 明确不做什么

本切片没有也不得：

- 枚举用户/Public/重定向桌面；
- 读取路径、文件名、Shell Namespace、图标或缩略图；
- 创建 DesktopHost HWND 或连接探针；
- 写配置、移动、删除、重命名或复制文件；
- 把匿名示例工作区宣称为真实桌面数据；
- 绕过 Issue #19–#24、5 人测试或 ADR-0001 决策。

因此它不是 `LongGrid.Infrastructure`，也不是首个 E4 生产垂直切片。

## 3. 合同设计

Core 新增：

- `RuntimeMode`：当前只有 `DevelopmentReadOnly`；
- `RuntimeCapabilityState`：只允许 `Disconnected` 与 `DisabledBySafetyPolicy`；
- `RuntimeStatusSnapshot`：只读属性、无路径/句柄/集合/委托，通过 `CreateDevelopmentReadOnly()` 构造安全形状。

App 在窗口初始化时获取快照，再映射成本地化可见文本。三个值元素通过 `AutomationProperties.ItemStatus` 分别暴露：

- `DevelopmentReadOnly`；
- `DisabledBySafetyPolicy`；
- `Disconnected`。

可见文字服务用户，`ItemStatus` 服务自动化与诊断，避免测试依赖中文文案。

## 4. 安全与架构审计

| 检查 | 结果 |
|---|---|
| 依赖方向 | 仅 `App -> Core`，Core 不引用 WinUI/Win32/Shell |
| 数据最小化 | 快照不含用户名、路径、文件名、计数或时间戳 |
| 副作用 | 工厂只分配托管对象，无 I/O、进程、窗口或配置操作 |
| 文件能力 | 明确为 `DisabledBySafetyPolicy`，UI 无执行入口 |
| DesktopHost | 明确为 `Disconnected`，不创建或调用宿主 |
| UI 自动化 | 结构合同检查 Core 工厂接线和只读边界；真实 UIA 复读三个状态 |
| 回归保护 | 真实 DesktopItems 命名空间/`DesktopCatalog` 调用、Shell、DesktopHost 生产类型和 `System.IO` 仍由脚本阻断 |

原结构规则曾用裸文本 `DesktopCatalog` 禁止目录接线，会误报安全快照的状态属性。本切片把它收紧为真实命名空间或静态调用模式；禁止能力没有放宽，只消除名称碰撞。

## 5. 验证计划与当前证据

本地已完成：

- `dotnet format LongGrid.sln --no-restore`；
- Release 全解决方案构建：0 警告、0 错误；
- Core 测试：90/90；
- Cobertura：行 91.43%（2412/2638）、分支 77.25%（584/756）；
- UI 结构合同：20 个 AutomationId、Core 状态合同和只读边界通过；
- 真实窗口 UIA：复读三个 Core 状态，宽→720 px 紧凑→宽、键盘焦点、主题往返和安全页通过；
- 配置持久化 20 场景、文件操作安全探针、缩略图隔离/清理/预算和依赖漏洞门禁通过；两项原生探针保持既有 `ConditionalPass` 限制。

上述结果来自本地 Windows 26200 x64 环境。GitHub CI 结果以本切片 PR 为最终证据。

## 6. 需求对齐与下一步

本切片对齐“让 UI 逐步使用 Core 状态”的开发方向，同时服从 Phase 0 停止规则：只接合同，不接真实用户数据或执行能力。它没有改变竞品对标、桌面分组、任务栏美化、小组件或 Long助手兼容的优先级。

下一步仍应优先完成 Issue #23 的首次整理/引用与移动语义验证，以及 #19–#20 人工和硬件矩阵。只有生产合同和 ADR 获批后，才能新增只读 Desktop Catalog 适配器，并把匿名示例替换为真实但最小化的桌面引用状态。
