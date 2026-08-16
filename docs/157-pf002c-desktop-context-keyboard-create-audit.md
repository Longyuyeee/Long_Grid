# Stage 157：PF-002C 桌面右键、键盘与统一创建请求审计

- 日期：2026-08-16
- 开发基线：`main@bc961eaf3ebfb2d9df4edf97487a016dcc909454`
- 分支：`codex/pf002c-desktop-create-inputs`
- 范围：PF-002C1——空工作区的产品自有按钮、右键菜单、键盘快捷键与 UIA Invoke 统一入口
- 结论：**PF-002C1 Engineering Pass；PF-002 整体保持 `InProgress`**

## 1. 本轮审计结论

Stage 156 把桌面空状态和控制中心统一到同一套名称、显示器与布局默认值，但正式 DesktopHost 只有主点击和 UIA Invoke；右键、键盘以及输入从原生消息到配置提交之间的陈旧状态复核仍是空缺。本轮按 iTop Easy Desktop/Fences 的“桌面就近创建、多种输入进入同一结果”目标补齐空工作区最小闭环。

实现后，Long方格在**自己拥有的空工作区创建卡片**上提供四类输入：

1. 主鼠标按钮点击；
2. 在创建卡片上右键，显示产品自有上下文菜单，再选择“创建第一个方格”；
3. 空工作区 Passive Surface 存续时按 `Ctrl+Alt+N`；
4. 辅助技术通过标准 UIA Invoke 调用。

四类输入都变成 `ProductDesktopWorkspaceCreateRequest`，携带输入种类、目标显示器、工作区 revision、显示拓扑 generation、来源证明、注入标志和自动重复标志，并进入同一个 admission、默认值策略和配置提交协调器。App 在 UI dispatcher 入队前和执行时各复核一次，避免排队期间状态变化造成陈旧创建。

## 2. 与对标产品的能力对齐

| 对标能力 | Long方格本轮行为 | 结论 |
| --- | --- | --- |
| 桌面就近按钮创建 | 空工作区卡片主按钮直接请求创建 | 空态工程对齐 |
| 桌面右键创建 | 创建卡片右键后显示真实产品菜单，只有选择命令才创建；取消零提交 | 有限范围工程对齐 |
| 键盘快速创建 | 空态 Passive Surface 注册 `Ctrl+Alt+N`，隐藏、禁用、释放时注销 | 空态工程对齐 |
| 辅助技术创建 | 标准 UIA Button/Invoke，AccessKey 在快捷键注册成功时公开 | 工程对齐，真人 Narrator 待证据 |
| 多入口同一结果 | 四类输入统一请求、准入、显示器选择、默认命名/布局和提交协调器 | 工程对齐 |
| 不打断当前工作 | Surface 继续 `WS_EX_NOACTIVATE`；菜单选择后复核前台未改变且前台不是 Surface | 工程对齐 |
| 任意 Explorer 桌面空白处右键 | 未注入 Explorer 菜单，未安装 Shell Extension，也未捕获全局桌面输入 | **未对齐** |
| 已有方格时继续从桌面创建 | 首个方格创建后空态 Surface 与快捷键消失 | **未对齐**，下一切片 PF-002C2 |
| 框选矩形创建 | 未实现 | PF-002 后续步骤 |
| 创建前命名/尺寸预览 | 未实现 | PF-002D |

“右键入口已完成”只能用于 Long方格自有空态卡片，不能表述成“已经接管 Windows 桌面空白区右键”。要覆盖任意 Explorer 桌面空白区，需要另行评审 Shell Extension、Explorer 集成或其他受支持入口的生命周期、安装和兼容性风险。本轮明确不使用全局 hook、Raw Input、输入模拟或 Explorer 注入。

## 3. 统一请求与提交链

```text
主点击 / 产品菜单 / Ctrl+Alt+N / UIA Invoke
                 │
                 ▼
ProductDesktopWorkspaceCreateInput
                 │  DesktopHost 批次盖章
                 ▼
ProductDesktopWorkspaceCreateRequest
(kind + display + workspace revision + topology generation + source facts)
                 │
                 ▼
App 入队前 admission ──拒绝──> 零提交
                 │ Ready
                 ▼
UI dispatcher 执行时二次 admission
+ 权威显示器存在 + AwaitingWorkspace
                 │ Ready
                 ▼
统一创建默认值 → ProductWorkspaceCommitCoordinator
```

请求种类只有 `PrimaryPointer`、`ContextMenu`、`KeyboardShortcut`、`AssistiveInvoke` 四个有限枚举值。未知枚举、空显示器、非法 revision/generation 均返回 `Invalid`，不得猜测修复。

## 4. 输入与生命周期设计

### 4.1 右键菜单

- 只有 Surface 为 `Passive`、投影容器数为零、右键坐标命中空态创建按钮时才打开菜单；
- 使用 `CreatePopupMenu`、`AppendMenuW`、`TrackPopupMenuEx(TPM_RETURNCMD)` 创建产品自有菜单；
- 只有返回唯一命令 ID 才进入请求；点击菜单外、Esc、菜单创建失败或坐标转换失败均返回 false；
- 菜单前记录当前前台窗口，选择后要求前台仍相同且不是 DesktopHost Surface；
- 来源证明在进入嵌套菜单循环前读取，注入或无法证明的消息在统一 admission 中拒绝；
- `DestroyMenu` 位于 `finally`，成功、取消和异常路径都释放菜单句柄。

### 4.2 键盘快捷键

- 使用 `RegisterHotKey` 注册 `Ctrl+Alt+N`，并带 `MOD_NOREPEAT`；
- 只在空工作区 Surface 进入 `Passive` 时尝试注册；
- Surface Hidden、用户关闭、紧急禁用或 Dispose 时注销；
- 注册冲突不使 DesktopHost 启动失败，`EmptyWorkspaceKeyboardCreateAvailable=false`，按钮、右键和 UIA 仍可用；
- `WM_HOTKEY` 仍构造统一请求，不绕过 revision、topology、来源和状态门禁。

### 4.3 UIA

- 空态入口继续暴露标准 Button/Invoke，而不是自定义不可读控件；
- 快捷键注册成功时 `AccessKey=Ctrl+Alt+N`，失败时不虚报；
- `ItemStatus` 保留“不读取或移动真实文件”的边界提示，并公开键盘入口可用性；
- UIA Invoke 使用 `AssistiveInvoke`，与鼠标、菜单和键盘共享同一 App 请求链。

## 5. 失败与安全矩阵

| 输入/状态 | 判定 | 可见/持久副作用 |
| --- | --- | --- |
| 未证明来源 | `UntrustedSource` | 零创建、零保存 |
| 注入消息 | `Injected` | 零创建、零保存 |
| 自动重复 | `AutoRepeat` | 零创建、零保存 |
| 工作区 revision 已变化 | `StaleWorkspace` | 零创建、零保存 |
| 拓扑 generation 已变化 | `StaleTopology` | 零创建、零保存 |
| 请求显示器不再位于权威拓扑 | App 拒绝 | 零创建、零保存 |
| DesktopHost 不再 `AwaitingWorkspace` | App 拒绝 | 零创建、零保存 |
| 请求入队后任一状态变化 | dispatcher 二次复核拒绝 | 零创建、零保存 |
| 右键未命中创建卡片 | 忽略 | 保持桌面穿透 |
| 菜单取消/非唯一命令 | 忽略 | 零创建、菜单释放 |
| 菜单期间前台变化 | 拒绝 | 零创建、菜单释放 |
| 快捷键已被其他程序占用 | 注册失败并降级 | 其他三个入口保留 |
| Surface 隐藏/释放 | 注销快捷键 | 零残留注册 |
| 创建被接受 | 复用 Stage 156 默认值与唯一提交协调器 | 一次配置提交；不读取/移动文件 |

## 6. 自动验收与稳定性观察

本轮新增或扩展的证据覆盖：

- 4 种输入均可通过同一 admission；
- 未证明、注入、自动重复、陈旧 revision、陈旧 topology 和畸形请求有限拒绝；
- 原生 Surface 能把 ContextMenu/KeyboardShortcut 归一化为同一输入结构；
- Hidden/Passive 生命周期释放和恢复快捷键可用状态；
- UIA Name、ControlType、Invoke、AccessKey、ItemStatus 与不抢前台合同；
- 源码合同锁定真实菜单 API、右键/热键消息、注册/注销和全部拒绝状态。

本地最终结果：

- PF-002C admission 测试：`9/9`；
- 相关 admission + 原生/UIA 聚焦测试：`24/24`；
- 全量测试：`966/966`；
- Release 解决方案构建：`0 warning / 0 error`；
- `dotnet format --verify-no-changes`：通过；
- UI 源码合同：`146 AutomationId`，`outcome=Pass`；
- `git diff --check`：通过。

全量开发过程中，既有 `NativeActivationSourceExposesFiniteInvokeAndHideRestoreContract` 曾在一次全量运行中因 UIA 前台切换瞬态失败；该用例随后单独连续通过并在完整重跑中通过，最终全量 966/966。没有发现它与本轮请求、菜单或快捷键链的因果联系，因此未修改无关激活代码。真实物理键盘、鼠标菜单选择、触控和 Narrator 仍需合规人工会话，不能由同步原生测试冒充。

## 7. PF-002 验收记账

| Stage 153 验收目标 | 当前证据 | 状态 |
| --- | --- | --- |
| 三种首版入口进入同一创建流 | 空态按钮、右键菜单、键盘及 UIA 均进入同一请求/策略/协调器 | **空态工程通过**；非空继续创建未通过 |
| 当前显示器内、尺寸受限 | 请求绑定批次显示器并在 App 复核权威拓扑；Stage 156 规划限制 work area/DPI | 工程通过 |
| 名称异常确定处理 | Stage 156 默认/显式名称合同已覆盖 | 工程通过，桌面内联提示未通过 |
| 保存失败不留下幽灵方格 | 尚未把保存结果与 DesktopHost 可见发布形成补偿事务 | 未通过 |
| 连续创建 20 个方格 | Core/reducer 20 次通过；正式桌面 UI 只能创建首个 | 部分通过 |
| 鼠标、键盘、触控、UIA 一致 | 鼠标/键盘/UIA 工程链通过；触控和全部物理输入证据待补 | 部分通过 |
| 不读取内容、不移动真实文件 | 请求与创建均为零引用配置，文件边界未扩大 | 通过 |
| 无需控制中心建立首个方格 | 四类空态入口可请求首个方格 | 工程通过 |

PF-002 不能升级为完成，因为“直接创建方格”目前只在工作区为空时存在。创建第一个方格后，空态 Surface 和 `Ctrl+Alt+N` 都被释放；用户仍需进入控制中心才能创建第二个及后续方格。这是产品能力缺口，不是测试缺口。

## 8. 严格后续开发顺序

1. **PF-002C2：非空工作区持续创建入口**。在不干扰已有方格选择、激活和桌面穿透的前提下，提供 Long方格自有、可发现的“新建方格”桌面入口；右键、键盘和 UIA 继续复用本轮请求/admission。验收从正式桌面 UI 连续创建第二至第二十个方格、每次目标显示器正确、快捷键生命周期唯一、焦点不被抢占。
2. **PF-002D：提交前就地预览与命名**。预览名称、显示器、位置和尺寸；支持键盘/UIA 编辑；Esc、失焦、安全门关闭、revision/topology 变化必须取消且零残留。
3. **PF-002E：保存与窗口发布补偿事务**。保存成功后才确认正式可见结果；保存失败撤回新窗口/投影并给出有限重试，禁止幽灵方格。
4. **PF-002F：正式证据收口**。覆盖连续 20 次、物理鼠标/键盘/触控、Narrator、高对比、文本缩放、100%～400% DPI、快捷键冲突与资源终态。
5. **PF-002 后续入口**。再按 Stage 153 实现桌面绘制矩形和“使用 Long方格已选引用创建”；不接管 Explorer 原生选择。

只有上述验收关闭后，PF-002 才能从 `InProgress` 升级，随后进入 PF-003 拖动、缩放与吸附。
