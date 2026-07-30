# P0-07b2b2b2b4a：可见输入门与 UIA Fragment 树

日期：2026-07-30

结果：**Conditional Pass（短时可见宿主的输入开/关/重开、真实 UIA Raw View Fragment 树和资源闭环通过；Narrator、真实输入与显示动态矩阵未验证）**

## 1. 目标与安全边界

本探针补齐 P0-07b2b2b2b3 隐藏宿主无法证明的两个边界：

1. 同一可见 DesktopHost 的交互 Region 在输入门开启、关闭和重开时，是否真实改变 Win32 点命中；
2. 每显示器 HWND 能否通过 `IRawElementProviderFragmentRoot` 暴露容器 Fragment，并被真实 `AutomationElement` 客户端沿 Raw View 导航。

探针只建立一个 alpha=1、`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`、非 Topmost 的自有 HWND。它不移动真实光标、不注入鼠标/键盘/触控/笔输入、不取得前台、不改变外部窗口和显示配置。窗口选择离当前光标最远的工作区角落，且只在采样期间存在。

## 2. 合同对齐

实现以 Microsoft 公开合同为准：

- [`WindowFromPoint`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-windowfrompoint) 返回包含屏幕点的可见窗口，并忽略隐藏或禁用窗口；
- [`SetWindowRgn`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowrgn) 成功后由系统拥有 HRGN；调用方不得再删除或读取该句柄；
- [`IRawElementProviderFragmentRoot`](https://learn.microsoft.com/windows/win32/api/uiautomationcore/nn-uiautomationcore-irawelementproviderfragmentroot) 是复杂 UI 框架 Fragment 的根；
- [`ElementProviderFromPoint`](https://learn.microsoft.com/windows/win32/api/uiautomationcore/nf-uiautomationcore-irawelementproviderfragmentroot-elementproviderfrompoint) 使用屏幕坐标并应返回该点对应的 Fragment；
- [`IRawElementProviderFragment`](https://learn.microsoft.com/dotnet/api/system.windows.automation.provider.irawelementproviderfragment) 负责父子/兄弟导航、运行时 ID、屏幕坐标边界和 FragmentRoot；
- [UI Automation 树概览](https://learn.microsoft.com/windows/win32/winauto/uiauto-treeoverview) 区分 Raw/Control/Content View；
- [用于自动化测试的 UIA 指南](https://learn.microsoft.com/windows/win32/winauto/uiauto-usefortesting) 要求把 RuntimeId 作为不透明比较值，不依赖其内部格式。

## 3. 可见输入门

宿主包含两个不相交的容器岛。创建宿主前，探针记录两个中心点当前对应的外部进程 HWND；随后执行：

```text
Open
  → 把两个本地矩形合并为复杂 HRGN
  → SetWindowRgn 转移所有权
  → WindowFromPoint 2/2 命中宿主

Close
  → 建立空 HRGN
  → SetWindowRgn 转移所有权
  → WindowFromPoint 2/2 精确回到创建前的外部 HWND
  → FragmentRoot.ElementProviderFromPoint 返回 null
  → UIA 点查询不得返回任一子 Fragment

Reopen
  → 重新建立新的复杂 HRGN
  → WindowFromPoint 2/2 再次命中宿主
```

每次设置 Region 都使用新 HRGN；成功后立即放弃调用方所有权。空 Region 关闭的是宿主命中面，不等价于隐藏窗口。

## 4. UIA Fragment 模型

真实 `WM_GETOBJECT` 返回一个 `IRawElementProviderFragmentRoot`：

```text
LongGrid.VisibleFragmentRoot (Pane)
├── LongGrid.Container.Alpha (Group)
└── LongGrid.Container.Beta  (Group)
```

根节点提供 HWND Host Provider、物理屏幕 BoundingRectangle、FirstChild/LastChild、`ElementProviderFromPoint` 和输入门状态。子节点提供：

- Parent、PreviousSibling、NextSibling；
- 稳定且彼此不同的 RuntimeId；
- Name、AutomationId、Group ControlType；
- 物理屏幕 BoundingRectangle；
- 指向同一 FragmentRoot 的引用。

输入关闭时子节点仍可沿树读取，但 `IsEnabled=false`，且点查询不再返回子 Fragment。这样诊断工具仍能解释宿主结构，同时操作入口保持关闭。当前探针不调用 `SetFocus`，避免为了测试夺取用户焦点。

## 5. 审计发现

### 5.1 Win32 穿透与 UIA 点查询不是同一个断言

空 Window Region 后，`WindowFromPoint` 会精确回到原外部 HWND；UIA Core 的 `AutomationElement.FromPoint` 仍可能返回自有 HWND 根节点。因此“输入已关闭”的正确验收不是强制 UIA 返回外部进程，而是：

1. Win32 点命中不再落到宿主；
2. FragmentRoot 的点分派返回 `null`；
3. UIA 点查询不得返回任何可操作子 Fragment；
4. 树中子 Fragment 标记为 disabled。

把 UIA 根节点存在误判为输入泄漏，会让可访问性树和输入路由形成不必要的耦合。

### 5.2 RuntimeId 对客户端是不可解释值

Provider 端使用 `AutomationInteropProvider.AppendRuntimeId` 生成子节点标识；客户端只验证运行时 ID 非空、跨子节点不同、重复读取稳定，不检查前缀或数组布局。

### 5.3 自动探针不能替代真实输入和 Narrator

`WindowFromPoint` 能安全验证窗口命中层，但不能证明鼠标按下/拖放、键盘焦点、触控、笔、UIA Pattern、事件和 Narrator 朗读体验。它们继续留在人工受控矩阵中。

## 6. 三轮正式结果

环境：Windows `10.0.26200.0`、x64、Per-Monitor V2 请求成功。

| 指标 | Run 1 | Run 2 | Run 3 |
|---|---:|---:|---:|
| Open 命中 | `2/2` | `2/2` | `2/2` |
| Close 精确穿透 | `2/2` | `2/2` | `2/2` |
| Reopen 恢复 | `2/2` | `2/2` | `2/2` |
| UIA Raw View 树 | 通过 | 通过 | 通过 |
| FragmentRoot 点分派 | `2/2` | `2/2` | `2/2` |
| Close 子 Fragment 排除 | 通过 | 通过 | 通过 |
| 前台窗口 | 保持 | 保持 | 保持 |
| USER | `2→4→2` | `2→4→2` | `2→4→2` |
| GDI | `3→3→3` | `3→3→3` | `3→3→3` |
| 进程句柄 | `350→350→350` | `350→350→350` | `350→350→350` |

三轮独立进程均为 `Conditional Pass`。

## 7. 尚未通过

- 真实鼠标按下、双击、框选、拖放、滚轮、键盘、触控和笔；
- Fragment 的 Focus、Pattern、事件以及跨进程辅助技术；
- Narrator、NVDA、高对比度、放大镜和键盘全流程；
- Win+D、Peek、全屏、Alt+Tab、任务视图和 Explorer 重启；
- 多显示器、混合 DPI、`WM_DPICHANGED`、旋转、拔插、投影、睡眠唤醒和 RDP；
- ARM64 实机。

## 8. 决策

**Conditional Pass**

每显示器 HWND + 显式 Window Region + UIA Fragment 树可以进入 DesktopHost 垂直切片。P0-07b2b2b2b4 拆分为：

- `P0-07b2b2b2b4a`：本报告，安全自动化验证，已完成；
- `P0-07b2b2b2b4b`：Narrator、真实输入和硬件/会话动态矩阵，仍需人工受控环境。

在 b4b 完成前，无人确认的自动布局恢复、可见桌面接管和“完整无障碍兼容”声明继续关闭。
