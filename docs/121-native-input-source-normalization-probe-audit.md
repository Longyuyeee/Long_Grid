# Stage 121：原生输入来源归一化探针审计

日期：2026-08-13

基线：`main` / `6cbe5ec`（PR #169 合并且 main CI 通过）

阶段：B6c4（探针专属原生来源验证；正式桌面输入、Explicit 与真实文件操作仍关闭）

## 1. 目标和非目标

Stage 120 已证明有限归一化动作可以安全进入 Intent 准备桥，但尚未证明 Win32 消息和 UIA Provider 能以一致语义连接到该适配器。本阶段建立一个只属于探针的原生 HWND，验证三条来源路径：

- `WM_LBUTTONDOWN` → `PrimaryPointerPress`；
- `WM_KEYDOWN` 的 Enter/Space → `KeyboardActivation`；
- HWND UIA `IInvokeProvider.Invoke` → `AssistiveTechnologyActivation`。

本阶段不是人工输入验收，不把消息测试冒充物理鼠标/键盘，也不把 UIA 自动调用冒充 Narrator 用户判断。正式 App、正式 DesktopHost HWND 和 Explorer 均不接入探针。

## 2. 探针窗口与权限

`NativeInputForwardingProbeWindow` 使用随机类名、匿名标题和 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED` 创建 1-alpha 探针窗口。它：

- 不成为 Topmost，不调用 `SetForegroundWindow`；
- 不安装全局 Hook；
- 不注册 Raw Input；
- 不调用 `SendInput`；
- 不读取键盘全局状态或桌面文件；
- 销毁时清除 UIA Provider、销毁 HWND 并注销窗口类。

探针以同步 `SendMessage` 驱动 pointer/key 消息，因此报告明确设置 `SyntheticWindowMessagesUsed=true`、`PhysicalDeviceInputVerified=false`。这项自动化只证明 WndProc 归一化，不是物理设备证据。

## 3. UIA 与转发链

探针根节点只公开 Invoke Pattern，不提供选择、文件或外部打开能力。测试通过 `AutomationElement.FromHandle` 获取真实 HWND Provider 并调用 `InvokePattern.Invoke`，回调生成辅助技术归一化动作。

三类动作都进入 Stage 120 `ProductDesktopInteractionInputForwardingAdapter`，继而只能准备 Intent。每次使用新 ActionId 和单调序号；坐标唯一命中探针内存方格。测试同时验证：

- pointer、keyboard、UIA 各形成一次 Prepared；
- `WM_KEYDOWN` previous-key-state 位为 1 时标记 auto-repeat，并被 B6c3 拒绝；
- 不支持的普通键不产生转发；
- 不消费 Intent、不进入 Admission/Explicit、不修改文件。

## 4. 前台与资源

探针在创建、消息测试、UIA Invoke 和销毁后复读前台 HWND，要求保持不变。UI Automation 首次初始化存在进程级缓存，因此在资源基线前执行一次等价预热；正式测量周期必须回到预热后的 USER/GDI/句柄平台，不能用首次初始化成本伪报泄漏，也不能放宽平台阈值掩盖真实增长。

CI 新增 `--native-input-forwarding --json`，要求结果为 `Conditional Pass`。源码合同禁止 `SendInput`、Hook、Raw Input、全局键状态、文件操作、Admission 和 Explicit 回流，并要求 App 不引用探针。

## 5. 自动化结果与限制

当前机器验证结果：

- pointer message：Prepared once；
- keyboard message：Prepared once；
- UIA Invoke：Prepared once；
- auto-repeat：Rejected；
- unsupported key：Ignored；
- foreground：Stable；
- cleanup：Passed；
- 结论：`Conditional Pass`。

物理鼠标、键盘、触控、笔、IME、真实自动重复、Narrator 语音、Win+D、全屏、锁屏/RDP、Explorer 重启和显示器变化仍属于 B6C3 人工矩阵，总结论继续为 **PendingManualEvidence**。

## 6. 需求对齐和下一步

本阶段进一步对齐最初的鼠标、键盘与辅助技术一致性需求，并继续采用 iTop/Fences 类桌面产品必须具备的低干扰、前台稳定和可退出边界。但用户仍不能点击正式桌面方格；任务栏美化、Widget/插件和窗口特效也未混入本阶段。

下一步 B6c5 应先增加由人工会话显式拥有的短生命周期原生来源桥：只有 B6C3 会话启动器和第四重门禁成立时才临时绑定探针式来源，执行物理 pointer/key/Narrator 矩阵并退出恢复。人工证据通过后，才能单独评审正式产品 Explicit Surface；真实文件移动/复制/删除继续后置。
