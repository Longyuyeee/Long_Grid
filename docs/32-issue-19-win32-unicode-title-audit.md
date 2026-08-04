# Issue #19 Win32 Unicode 窗口标题边界审计

审计日期：2026-08-04

结论：**Unicode boundary fixed / Automated regression pass / I19-01 manual evidence remains Inconclusive**

## 1. 发现

在 `main` / `19e2359` 上按 Issue #19 运行手册以匿名操作员 O1 启动 I19-01。进程正常响应且 HWND 存在，但外部读取的原生窗口标题只有首字符，通用 Windows 窗口控制工具也没有返回该 ToolWindow。为避免猜测坐标和把自动化冒充人工输入，本轮没有发送任何键盘或鼠标事件，测试进程随后被清理。

源码传入的标题是完整英文常量。窗口类注册和创建已经使用 Unicode，但以下消息边界没有显式绑定 Unicode 入口：

- `DefWindowProc`；
- `GetMessage`；
- `DispatchMessage`。

Unicode 窗口类经 ANSI 默认窗口过程处理 `WM_GETTEXT` 时，外部读取会把 UTF-16 缓冲误解释为单字节字符串，表现为只得到首字符。

## 2. 修复

- 显式绑定 `DefWindowProcW`、`GetMessageW`、`DispatchMessageW`；
- Window procedure delegate 明确使用 `CharSet.Unicode`；
- 新增 `GetWindowTextLengthW` / `GetWindowTextW`，使用显式非托管 UTF-16 缓冲区，避免隐式 `StringBuilder` P/Invoke；
- interactive smoke 新增 `NativeWindowTitleUnicodeVerified`，完整标题不匹配时结果必须为 `Fail`；
- CI 新增独立 interactive smoke，持续阻断 Unicode 标题、UIA、非激活或资源闭环回归；
- 保留 `WS_EX_TOOLWINDOW`、非 Topmost、初次 `SWP_NOACTIVATE` 和可显式聚焦语义不变。

构建最初按警告即错误拦截了 `StringBuilder` P/Invoke 的 CA1838/CA2101；改为显式缓冲区后 Release 构建恢复 0 警告/0 错误。

## 3. 自动证据

修复后 Release interactive smoke：

| 合同 | 结果 |
|---|---|
| 完整原生 Unicode 标题 | Pass；`NativeWindowTitleUnicodeVerified=true` |
| ToolWindow / 非 Topmost | Pass |
| 初次不激活 / 自动 Pattern 前隐藏 | Pass |
| UIA 树、Selection、Invoke 与事件 | Pass |
| 不合成输入、不读桌面、不改显示 | Pass |
| USER/GDI/进程句柄恢复 | Pass |
| 总结果 | `Conditional Pass` |

## 4. 人工证据边界

修复后第二次启动可从进程完整读取窗口标题，但通用控制工具仍未把 `WS_EX_TOOLWINDOW` 返回到普通窗口列表。这是外部工具的目标过滤限制，不是移除 ToolWindow 样式的理由：该样式正是避免普通任务栏和 Alt+Tab 项的待验证产品语义。

第二次尝试仍未发送输入，因此不能把自动 smoke 或完整标题升级为 I19-01 人工 Pass。I19-01 保持 `Inconclusive/Pending`；I19-02–I19-10 未执行。最终证据必须由真人直接操作可见窗口，或由能够显式定位 ToolWindow 且不改变其窗口样式的受控操作工具完成。

## 5. 需求对齐

本修复只纠正 Win32 Unicode ABI 边界并提高人工测试可定位性，不接真实桌面、不改变输入区域、不修改窗口层级、不增加任务栏项，也不更新 ADR-0001。Issue #19 继续保持打开，直到 I19-01–I19-10 均有可复读人工证据、恢复确认且没有开放阻断缺陷。
