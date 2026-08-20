# Stage 165：PF-002 桌面拖画矩形创建工程审计

- 日期：2026-08-20
- 分支：`codex/pf002d-create-preview`
- 目标：用户在 Explicit 桌面交互面拖出矩形后，使用该位置和尺寸进入既有 Preview、提交与失败补偿链
- 结论：**Engineering Pass；正式 App 物理鼠标证据 Pending，PF-002 保持 `InProgress`**

## 1. 开发前审计

开发前只有按钮、右键菜单、快捷键和 UIA 创建入口。`ProductDesktopWorkspaceCreateRequest` 只携带显示器、工作区 revision 和拓扑 generation，不携带矩形。Preview 与最终提交都会调用默认布局策略，因此即使上层临时画出矩形，最终配置也会丢失它。

| 目标 | 开发前实际 | 差异 |
| --- | --- | --- |
| down/move/up 形成有限矩形 | 原生 Surface 只处理单次 `WM_LBUTTONDOWN` | 缺少手势状态 |
| 拖动期间不写配置 | 没有拖画状态 | 缺少内存 preview |
| 多显示器/DPI 保真 | 请求没有像素矩形 | 无法换算 |
| Preview 改名不丢布局 | 改名重新评估默认布局 | 会丢矩形 |
| 最终提交使用拖画位置 | 创建器重新生成默认位置 | 会丢矩形 |

## 2. 实现与安全边界

- 新增 `PointerDrag` 创建种类和可空 `RequestedBoundsPixels`；只有该种类允许携带矩形，其他入口携带矩形一律 `Invalid`。
- Win32 Surface 只在 `Explicit` 模式、空白区域、无 Ctrl/Shift 且 `GetCurrentInputMessageSource` 证明非注入时开始拖画。
- 使用 `SetCapture`、`WM_MOUSEMOVE`、`WM_LBUTTONUP`、`WM_CAPTURECHANGED`/`WM_CANCELMODE`；拖动中只用 `DrawFocusRect` 更新内存 outline，不提交配置。
- 切换 Passive/Hidden 或失去 capture 会取消手势并释放 capture。
- Requested bounds 使用显示器绝对像素；创建策略验证完整位于工作区，按有效 DPI 转为相对 DIP，并拒绝小于 `160×120 DIP` 的矩形。
- Preview 名称重校验和最终 `CreateDefaultContainer` 都复用同一 requested bounds；保存失败继续进入 Stage 164 补偿事务。
- 没有新增全局 hook、synthetic input、Explorer 注入或桌面文件读写。

## 3. 预期—首次实际—修正

| 门禁 | 预期 | 首次实际 | 修正后实际 |
| --- | --- | --- | --- |
| Release 编译 | 0 error | 新测试误写 `ProductContainerPlacementConfiguration`，1 个 CS0246 | 改为正式合同 `ContainerPlacementConfiguration`，0 error |
| 负坐标/200% DPI | `640×400 px` → `320×200 DIP`，位置相对工作区 | 尚未运行 | 精确为 `(100,100,320,200)` |
| 越界/过小 | 失败关闭 | 尚未运行 | 3 组均 `PlacementUnavailable` |
| 真实 Win32 Surface | Explicit 才接受，不取前台 | 尚未运行 | Passive 拒绝；Explicit 接受；foreground 未改变 |
| 真实配置重载 | 几何不回落默认值 | 尚未运行 | `display-secondary/(100,100,320,200)` 原样重载 |

首次编译失败是测试代码合同名称错误，不是产品几何失败；它已保留在本审计中，没有用重跑成功覆盖首次结果。

## 4. 真实测试与完整门禁

- 专项 28/28 Pass：admission、正反安全边界、负坐标/高 DPI、最小尺寸、Lifecycle 映射、真实 Win32 Surface、真实配置文件保存/重载。
- Release 全量：1002/1002 Pass。
- Release 构建：0 warning / 0 error。
- `dotnet format --verify-no-changes`：Pass。
- `eng/Test-LongGridUi.ps1 -ContractOnly -NoBuild`：Pass，146 AutomationId。

真实 Win32 测试创建并销毁产品 Surface，验证窗口模式和 foreground；真实存储测试使用 GUID 临时目录写入正式 JSON、重新加载并精确清理。它们不是物理鼠标证据，不能替代正式 App 的人工/硬件 down/move/up 测试。

## 5. 需求对齐结论

- 拖画矩形事实已贯穿到最终持久化，不再被默认布局覆盖。
- 手势中途取消零提交，尺寸/越界失败关闭，符合 iTop/Fences 类桌面拖画创建的核心反馈模型。
- 当前入口依赖已有 Explicit 桌面交互状态；空白桌面直接进入拖画模式的可发现入口仍需体验复审。
- 正式 App 物理拖画、Preview 编辑/取消/确认、鼠标 capture 丢失、触控、DPI 切换和 Narrator 仍 Pending。
- PF-002 不能标记 Complete，也不能提前进入 PF-003 的通用拖动/缩放。

## 6. 下一步

1. 开发“使用 Long方格已选引用创建”：只转移配置引用归属，不擅自移动真实桌面文件。
2. 在无并发输入 Windows 会话执行拖画：正向/反向、过小、越界、Esc/capture 丢失、确认、取消和保存失败矩阵。
3. 与 PF-002D 合并完成鼠标、键盘、触控、UIA、Narrator、高对比、文本缩放和多 DPI 正式证据。
