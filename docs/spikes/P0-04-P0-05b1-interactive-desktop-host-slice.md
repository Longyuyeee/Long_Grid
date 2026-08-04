# P0-04/P0-05b1：可交互 DesktopHost 垂直切片

日期：2026-07-30

结果：**Conditional Pass（可见容器/项目、真实 UIA Pattern/事件、初始不激活和资源闭环通过；人工键鼠、Narrator、拖放与系统表面矩阵未执行）**

## 1. 目标

建立第一个真正可见、可操作但不接触用户桌面数据的 DesktopHost 垂直切片，为后续键盘、鼠标、Narrator、触控和系统交互矩阵提供统一测试对象。

切片包含：

```text
Long Grid 交互原型 (Pane)
└── 当前项目，容器，3 个演示项目，已展开 (List)
    ├── 需求文档，演示项目，目标可用 (ListItem)
    ├── 设计参考，演示项目，目标可用 (ListItem)
    └── 项目计划，演示项目，目标可用 (ListItem)
```

三个项目全部是进程内演示数据。Invoke 只改变原型状态文字，不读取、打开、移动或修改任何文件。

## 2. 官方合同

- Microsoft 的 [Win32 无障碍设计指南](https://learn.microsoft.com/windows/win32/uxguide/inter-accessibility)要求每个可交互元素同时提供键盘路径和无障碍 API；
- [UI Automation Providers 概览](https://learn.microsoft.com/windows/win32/winauto/uiauto-providersoverview)要求自定义 Win32 控件实现对应 Provider 与 Control Pattern；
- [UI Automation 最佳实践](https://learn.microsoft.com/windows/win32/winauto/accessibility-best-practices)要求逻辑 Tab 顺序、清晰焦点指示、描述性名称、DPI 和系统设置适配；
- [`RaiseAutomationEvent`](https://learn.microsoft.com/dotnet/api/system.windows.automation.provider.automationinteropprovider.raiseautomationevent)要求无论调用来自鼠标、键盘还是 UIA Pattern，都在实际动作发生时发出对应事件；
- [Keyboard interactions](https://learn.microsoft.com/windows/apps/design/input/keyboard-interactions)区分应用焦点视觉与 Narrator 焦点矩形，要求键盘焦点可见且顺序合理。

## 3. 窗口语义

该原型使用一个自有、非 Topmost 的 `WS_EX_TOOLWINDOW` 顶层 HWND：

- 初次展示使用 `SWP_NOACTIVATE`，不抢用户当前前台；
- 不使用永久 `WS_EX_NOACTIVATE`；
- 用户点击项目，或辅助技术显式调用 `SetFocus` 后，窗口可以获得真实键盘焦点；
- ToolWindow 语义用于避免普通任务栏按钮；Alt+Tab、任务视图和 Win+D 仍需人工验证；
- Esc 销毁窗口并退出消息循环。

审计结论：Desktop Passive 的“启动不抢焦点”和“用户可以键盘访问”不是同一个开关。永久 `WS_EX_NOACTIVATE` 虽然安全地不抢焦点，却会破坏键盘和辅助技术入口；正确实现是初次 Show 不激活，用户明确交互后允许激活。

## 4. 视觉与输入

GDI 原型使用 Windows 系统颜色，而不是写死品牌色：

- `COLOR_WINDOW` / `COLOR_WINDOWTEXT`；
- `COLOR_BTNFACE` / `COLOR_BTNTEXT`；
- `COLOR_HIGHLIGHT` / `COLOR_HIGHLIGHTTEXT`。

当前仅验证信息层级与状态语义，不代表最终视觉方案。选中项使用系统 Highlight；窗口真实获得键盘焦点后，再绘制内层焦点框，避免把“选中”和“键盘焦点”混为一谈。

统一交互状态：

| 输入 | 行为 |
|---|---|
| 点击项目 | 选择项目并把焦点交给宿主 |
| Tab / Shift+Tab | 前后循环选择 |
| 方向键 | 在项目之间移动 |
| Home / End | 首项 / 末项 |
| Enter / Space | 调用当前项目 |
| Esc | 关闭原型 |
| UIA SelectionItem.Select | 与键盘/鼠标共用同一选择状态 |
| UIA Invoke.Invoke | 与 Enter/Space 共用同一调用状态 |

## 5. UIA Provider

根实现 `IRawElementProviderFragmentRoot`，容器实现 `ISelectionProvider`，项目实现：

- `IRawElementProviderFragment`；
- `ISelectionItemProvider`；
- `IInvokeProvider`。

Provider 暴露：

- Pane → List → ListItem 的三层 Raw View；
- 父子和前后兄弟导航；
- 稳定且不透明的 RuntimeId；
- 物理屏幕 BoundingRectangle；
- Name、AutomationId、ControlType、IsEnabled；
- IsKeyboardFocusable 与 HasKeyboardFocus；
- SelectionContainer 与单选状态；
- 点命中与当前焦点 Fragment。

选择变化统一发出 `SelectionItem.ElementSelectedEvent`；鼠标、键盘或 UIA Invoke 都通过同一 `InvokeItem` 路径发出 `Invoke.InvokedEvent`。当前 smoke 没有夺取焦点，因此 FocusChanged 只在人工/辅助技术真实聚焦时发出。

## 6. 自动 smoke

命令：

```powershell
dotnet run --project probes/LongGrid.Spikes.DesktopHostWindowModels `
  --configuration Release -- `
  --interactive-slice-smoke --json
```

Smoke 不注入鼠标或键盘。它先短时可见展示并检查初始不激活，然后在调用自动化 Pattern 前隐藏宿主，避免 UIA Core 对可激活 HWND 的潜在前台切换。之后使用真实 `AutomationElement` 客户端：

1. 沿 Raw View 读取 Pane、List 和 3 个 ListItem；
2. 读取 Selection 与 Invoke Pattern；
3. 通过 SelectionItem 选择第三项；
4. 通过 Invoke 调用第二项；
5. 订阅并接收 SelectionItem 与 Invoke 事件；
6. 确认自动 Pattern 前已隐藏，并在初次展示后和 UIA 操作后两个检查点确认原型 HWND 不是前台；
7. 销毁窗口并复读 USER/GDI/进程句柄。

窗口类、创建、默认窗口过程和消息循环统一使用 Win32 Unicode `W` 入口；smoke 还会通过 `GetWindowTextW` 回读完整原生标题。该门禁防止 ANSI 默认窗口过程把标题截断为首字符，进而破坏外部定位和辅助技术的窗口级语义。

三轮独立进程结果：

| 指标 | Run 1 | Run 2 | Run 3 |
|---|---:|---:|---:|
| UIA 树 | 通过 | 通过 | 通过 |
| Selection / Invoke Pattern | 通过 | 通过 | 通过 |
| Selection / Invoke 状态 | 通过 | 通过 | 通过 |
| Selection / Invoke 事件 | 收到 | 收到 | 收到 |
| 初次不激活 / 两检查点非前台 | 通过 | 通过 | 通过 |
| USER | `44→46→44` | `44→46→44` | `44→46→44` |
| GDI | `80→80→80` | `80→80→80` | `80→80→80` |
| 进程句柄 | `614→614→614` | `614→614→614` | `614→614→614` |
| 结果 | Conditional Pass | Conditional Pass | Conditional Pass |

首轮连续执行中曾把“外部前台句柄发生变化”误判为宿主抢焦点。验收先修正为直接检查宿主 HWND；随后真实复核又观察到可激活 HWND 在 UIA Pattern 调用期间可能成为前台。最终 smoke 在初始展示检查后、自动 Pattern 调用前隐藏宿主。外部前台稳定性保留为观察字段，人工模式仍负责验证用户显式聚焦后的行为。检查点不能证明两次采样之间绝无短暂激活，因此真实交互矩阵仍需事件级和人工观察。

正式三轮运行时系统已有其他 UIA/窗口检查客户端连接，因此绝对 USER/GDI/句柄基线高于此前单独运行；验收条件是每个独立进程在预热后精确回到自身基线，而不是跨工具环境比较绝对值。

2026-08-04 增量复核发现 Unicode 窗口类仍通过未显式指定的默认窗口过程/消息循环入口处理消息，外部 `WM_GETTEXT` 只能读到标题首字符。修复统一切换到 `DefWindowProcW`、`GetMessageW` 和 `DispatchMessageW`，并新增 `GetWindowTextW` 完整标题门禁。修复后单轮 Release smoke 的 `NativeWindowTitleUnicodeVerified=true`，其余 UIA、非激活和资源闭环字段继续通过；该单轮不能回填上表历史三轮。

## 7. 人工模式

```powershell
dotnet run --project probes/LongGrid.Spikes.DesktopHostWindowModels `
  --configuration Release -- --interactive-slice
```

按窗口内说明操作，Esc 退出。

本轮尝试使用通用 Windows 窗口控制工具定位原型时，首次暴露原生标题被截断为首字符的 Unicode 消息边界缺陷；修复后进程可正确回读完整标题，但 ToolWindow 仍未出现在工具的普通窗口列表中。两次均没有回退为猜测坐标操作，也没有发送输入。后者与 ToolWindow 的测试工具过滤一致，不能通过移除 `WS_EX_TOOLWINDOW` 规避，也不能替代 I19-01 或 Alt+Tab/任务视图人工验证。

## 8. 尚未通过

- 键盘焦点框的人工视觉确认；
- 鼠标点击、双击、滚轮、框选和拖放；
- Narrator 真实朗读、搜索列表和开发者模式；
- UIA FocusChanged 的跨进程辅助技术接收；
- 触控、笔、长按和惯性滚动；
- 高对比度、200% 文本缩放、减少动画；
- Per-Monitor DPI 移动与 `WM_DPICHANGED` 重排；
- Win+D、Peek、全屏、Alt+Tab、任务视图和 Explorer 重启；
- DirectComposition 最终视觉、阴影、透明度和动画；
- 真实桌面项目、Shell 打开、引用和拖放数据对象。

## 9. 决策

**Conditional Pass**

原生 DesktopHost 的交互状态与 UIA Pattern/事件可以继续进入垂直切片迭代。该结果不等于最终技术栈接受，也不允许接入真实文件或默认启用桌面接管。下一步在受控环境执行键盘、鼠标、Narrator 和系统表面矩阵，并补充 DPI/高对比适配。
