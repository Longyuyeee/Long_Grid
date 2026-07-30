# P0-04/P0-05a：DesktopHost 原生窗口模型与跨进程命中

执行日期：2026-07-30

结果：**Conditional Pass（下一原型优先每显示器 HWND + 显式交互区域）**

前置：P0-01a/P0-01b/P0-01c/P0-02/P0-03

## 1. 目标

在不嵌入 Explorer、不模拟鼠标键盘、不修改文件和不改变系统桌面状态的前提下，比较：

1. 模型 A：每个容器一个顶层 HWND；
2. 模型 B：每个显示器一个顶层 HWND，在窗口区域中只保留容器交互岛；
3. 100 个容器时的 HWND/USER/GDI/进程句柄成本；
4. 容器内部命中、容器外空白区域跨进程穿透；
5. 创建窗口是否抢占前台；
6. Passive 状态是否避免 Topmost、任务栏按钮和 Alt+Tab 条目；
7. 所有 HWND、窗口类和 GDI Region 是否完整回收。

本子探针只关闭“原生窗口形状、命中和资源成本”风险，不宣称完成 Win+D、全屏、拖放、触控或无障碍验证。

## 2. 官方语义与审计修正

### 2.1 不能把 `HTTRANSPARENT` 当作 Explorer 穿透方案

微软对 [`WM_NCHITTEST`](https://learn.microsoft.com/windows/win32/inputdev/wm-nchittest) 的说明明确限定：`HTTRANSPARENT` 会继续寻找**同一线程**下的底层窗口。Explorer 属于另一个进程/线程，因此“全屏透明 HWND + 空白处返回 `HTTRANSPARENT`”不足以证明可靠跨进程穿透。

本探针没有采用这个捷径，而是使用 [`SetWindowRgn`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowrgn) 把每显示器 HWND 的系统窗口区域限制为容器矩形的并集。区域之外从窗口管理器视角就不属于 Long Grid 窗口。

`SetWindowRgn` 成功后，Region 所有权转移给系统，调用方不得再次删除；失败前仍由调用方负责 `DeleteObject`。探针对两条所有权路径分别收口。

### 2.2 Passive 窗口样式

微软的 [Extended Window Styles](https://learn.microsoft.com/windows/win32/winmsg/extended-window-styles) 定义：

- `WS_EX_TOOLWINDOW` 不出现在任务栏和 Alt+Tab；
- `WS_EX_NOACTIVATE` 不因点击成为前台窗口，默认也不出现在任务栏；
- `WS_EX_TOPMOST` 会长期高于普通窗口，Passive 状态不得使用；
- `WS_EX_TRANSPARENT` 主要规定同线程兄弟窗口的绘制顺序，不能替代跨进程输入设计。

`WS_EX_NOACTIVATE` 文档也提醒辅助技术不应通过键盘导航激活该窗口。因此它只能作为 Passive 模式候选，不能永久覆盖 Editing、键盘操作和 Narrator 路径。

### 2.3 命中取样

[`WindowFromPoint`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-windowfrompoint) 返回包含指定屏幕点的可见窗口，并忽略隐藏或禁用窗口。探针在窗口短暂可见期间采样：

- 每个容器中心；
- 每个容器单元格中的空白间隙；
- 空白间隙命中的窗口是否属于其他进程。

探针不发送点击，不读取其他窗口标题、类名或进程身份。

## 3. 实现

Core 新增纯 .NET `DesktopHostWindowPlanner`：

- `PerContainer`：每个可见容器产生一个 Surface；
- `PerDisplay`：每个有内容的显示器产生一个 Surface，内部保存显示器相对坐标的交互区域；
- 容器在显示器边缘被裁剪；
- 未知显示器、空边界和重复显示器 ID 直接拒绝；
- Core 不暴露 HWND、HRGN 或其他 Win32 类型。

原生探针：

- 申请 Per-Monitor V2 DPI awareness；
- 在主显示器工作区生成 10 × 10、共 100 个矩形容器；
- 创建 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED` 窗口；
- 使用 1/255 Alpha，窗口只在本地取样期间近乎不可见；
- 使用 `SetWindowPos(..., SWP_NOACTIVATE)` 显示，不请求 Topmost；
- 每显示器模型用 100 个矩形合并 HRGN；
- 用 `WindowFromPoint` 验证容器内外；
- 用 `GetGuiResources`、进程句柄计数和窗口销毁后快照验证回收；
- 首次 Region 初始化后再采集稳定 GDI 基线。

## 4. 安全与隐私

- 未调用 Win+D、未合成输入、未移动前台窗口；
- 未读取或输出其他窗口标题、类名、PID 或应用身份；
- 未使用 `SetParent`、`Progman`、`WorkerW`、Explorer 注入或 Hook；
- 未创建 Topmost 窗口；
- 未修改注册表、显示设置、任务栏、Explorer 或真实桌面项目；
- 临时窗口接近完全透明，探针结束后全部销毁；
- 报告只输出聚合命中计数和资源数量。

## 5. 实测环境

| 项目 | 值 |
|---|---|
| OS | Microsoft Windows NT `10.0.26200.0` |
| 架构 | x64 |
| .NET SDK | `8.0.419` |
| 工作区 | 2560 × 1344 物理像素 |
| DPI 上下文 | Per-Monitor V2 请求成功 |
| 容器 | 100 |
| 容器内取样 | 每轮每模型 100 |
| 空白区取样 | 每轮每模型 100 |

## 6. 三轮结果

### 6.1 正确性与资源

| 指标 | 每容器 HWND | 每显示器 HWND |
|---|---:|---:|
| Surface/HWND | 100 | 1 |
| 交互区域 | 100 | 100 |
| 容器内部命中 | 三轮均 100/100 | 三轮均 100/100 |
| 空白区离开探针窗口 | 三轮均 100/100 | 三轮均 100/100 |
| 空白区命中其他进程 | 三轮均 100/100 | 三轮均 100/100 |
| 前台窗口保持 | 是 | 是 |
| ToolWindow/NoActivate | 全部存在 | 存在 |
| Topmost | 全部不存在 | 不存在 |
| USER 基线 → 峰值 → 回收 | 2 → 103 → 2 | 2 → 4 → 2 |
| GDI 基线 → 峰值 → 回收 | 3 → 3 → 3 | 3 → 3 → 3 |
| 进程句柄 | 每轮无净增长 | 每轮无净增长 |
| 清理 | 三轮通过 | 三轮通过 |

### 6.2 建立和验证耗时

| 轮次 | 每容器 HWND | 每显示器 HWND |
|---|---:|---:|
| 1 | 1522.63 ms | 28.47 ms |
| 2 | 1477.19 ms | 17.11 ms |
| 3 | 1284.76 ms | 18.82 ms |

这些耗时包含创建、区域设置、200 个命中/归属判断及销毁，不是渲染帧时间；它们只用于本机相对比较。

## 7. 结论

### 已通过

- 两种模型都能在不抢前台、不置顶的情况下准确命中容器；
- 每显示器模型通过系统 Window Region 实现了真实跨进程空白区穿透；
- 两种模型销毁后 USER、GDI 和进程句柄均回到稳定基线；
- `WS_EX_TOOLWINDOW`/`WS_EX_NOACTIVATE` 可作为 Passive 模式的窗口样式基线；
- Core 可以用相同布局数据规划两种模型，无需把 Win32 类型泄漏到领域层。

### 相对结论

100 个容器时，每容器模型额外占用约 101 个 USER 对象，而每显示器模型只增加 2 个；三轮记录中的建立/验证耗时相差约 53–86 倍。模型 A 的主要优势是每个容器天然独立命中和可能更直接的 UIA Root；模型 B 在窗口数量、批量合成和布局原子更新方面明显更适合作为下一原型。

因此临时决策是：

```text
下一 DesktopHost 原型
  = 每显示器一个 HWND
  + 显式交互区域
  + 单一 Composition 场景
  + 每显示器 UIA Fragment 树

特殊 Peek/弹出表面
  = 独立、短生命周期窗口
  ≠ 每个普通容器永久一个 HWND
```

### 为什么仍是 Conditional Pass

- 未执行 Win+D/显示桌面恢复；
- 未验证全屏、游戏、演示、锁屏、休眠和 Explorer 重启；
- 未真实打开 Alt+Tab/任务栏 UI，只验证了官方样式契约；
- 未验证 Narrator、UI Automation Fragment、键盘导航和焦点恢复；
- 未验证 OLE 拖放、触控、笔、滚轮、右键菜单和捕获；
- 未覆盖多显示器、混合 DPI、旋转、拔插、RDP、Windows 10 和 ARM64；
- 矩形 HRGN 不等同于圆角视觉、动画期间连续同步或复杂非矩形命中。

P0-04/P0-05 的完整发布门禁仍未关闭。

## 8. 产品实现约束

1. 不允许仅靠 `HTTRANSPARENT` 实现 Explorer 穿透；
2. 每显示器 HWND 的窗口区域必须与当前交互容器区域原子同步；
3. Passive 状态禁止 Topmost，使用非激活样式且不得抢焦点；
4. Editing 状态必须提供可激活、可键盘导航和 Narrator 可达的路径，不能永久 `NOACTIVATE`；
5. Peek 使用单独、短生命周期、可明确退出的 Z-order 策略；
6. Region 更新失败时必须回退为隐藏 DesktopHost，不能留下整屏输入遮挡；
7. DPI 或显示器变化时先停止输入，再重算像素 Region 和 Composition；
8. UIA 采用每显示器 Root + 容器/项目 Fragment，避免 100 个顶层无障碍窗口；
9. 圆角视觉和实际命中必须一致；区域同步不可靠时回退矩形命中；
10. 生产 DesktopHost 必须持续监测 HWND、USER/GDI、命中区域代次和异常恢复。

## 9. 后续 P0-04/P0-05b

1. 人工/自动化混合矩阵：Win+D 两次往返、显示桌面按钮、Alt+Tab、任务栏和任务视图；
2. 普通全屏、无边框全屏、游戏/演示和 Peek；
3. Narrator、UI Automation、Tab/方向键、Editing 激活与焦点归还；
4. OLE 拖放、鼠标捕获、右键、触控、笔和滚轮；
5. 双屏混合 DPI、负坐标、旋转、拔插、睡眠/RDP；
6. Explorer/DWM 重启与 Region 更新失败恢复；
7. 真实 Composition 渲染下 100 容器/500 项目的帧时间、内存、GPU 和空闲 CPU。
