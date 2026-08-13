# Stage 132：E2b 正式输入源设计审计

更新日期：2026-08-14

## 1. 审计结论

E2a 已关闭 Intent 原子消费，但 M1 的 Passive 产品表面必须整窗命中穿透，不能直接收到首次鼠标动作；同时它保持 `NoActivate`、非前台和无 owner，也不能承载真实键盘焦点。若直接在该 HWND 上增加 `WM_LBUTTONDOWN` / `WM_KEYDOWN`，代码存在但用户永远无法可靠到达，属于伪接线。

E2b 因此采用 **产品自有、每显示器一个、区域受限的 activation HWND**：它只覆盖每个可交互方格的有限激活按钮区域，主 DesktopHost Passive 表面继续全量穿透。activation HWND 接受首次 pointer 和 UIA Invoke；键盘-only 用户从 App 中新的标准“进入桌面交互”命令显式请求当前来源，生命周期在内部复核 HWND 后生成同类 KeyboardActivation 并把焦点交给来源，App 不取得句柄。三类激活统一进入 E2a 的 forwarding → preparation → atomic consumption；进入 Explicit 后，主表面处理项目 pointer/UIA 选择，activation HWND 作为有限键盘命令代理处理方向键、Home/End、Space 与 Escape。三条路径最终都调用同一 `ApplyInteractionSelection`，不复制选择状态机。

## 2. 不可变边界

- 继续要求 Host、Interaction、Intent Bridge、Input Forwarding 四重精确 opt-in；任一缺失时不创建来源 HWND；
- 不安装全局 Hook，不注册 Raw Input，不调用 `SendInput` / `GetAsyncKeyState` / `RegisterHotKey`，不检查或嵌入 Explorer、Progman、WorkerW；
- activation HWND 必须是当前进程、同宿主线程、自有实例标识的 ToolWindow，无 owner、非 Topmost、创建后不激活；App 不取得 HWND；
- 可命中 Region 只来自当前投影中的有限激活按钮，区域外 `HTTRANSPARENT`；不得把整显示器变成输入遮罩；
- pointer、keyboard、UIA action 均生成单调序号、唯一 ActionId、有限坐标与来源证明；键盘请求只能来自当前 App 标准命令并由生命周期复核当前 source，注入声明、自动重复、陈旧代次和重放继续拒绝；
- Passive 不消费业务选择；只有原子消费成功并进入 Explicit 后才接受选择命令；所有文件能力恒为 false；
- FocusLost、Win+D/桌面显示、全屏、会话/RDP、Explorer/拓扑变化、投影替换、关闭和 Escape 统一失效 Prepared、取消 Explicit 并隐藏/释放来源；
- 自动化不得写成物理设备或 Narrator Pass，外部结果继续 `PendingManualEvidence`。

## 3. 焦点与生命周期决策

首次显示必须使用 `SW_SHOWNOACTIVATE`。用户明确点击来源、调用其 UIA Invoke，或在 App 标准命令上执行键盘动作后，它可以成为当前进程的有限键盘焦点代理；这不授权主 DesktopHost 表面取得前台，也不改变 M1 的稳定窗口策略。没有 App 命令、当前来源或完整门禁时，键盘路径必须禁用，不能用全局快捷键补洞。

当前 App 把 MainWindow 任意 Deactivated 都报告为 FocusLost。E2b 必须先让生命周期以内部、精确 HWND 所有权判断“前台是否为当前 activation source”；只有这种同进程、同线程、同 instance/generation 的情况可忽略 MainWindow Deactivated。未知窗口、旧句柄、代次不符或复读失败仍立即按 FocusLost 取消。来源自身收到 `WM_KILLFOCUS` 时同样取消，不允许基于进程名或窗口标题放行。

系统恢复只能在既有两个安全样本和权威拓扑稳定后重建 Passive + activation source；不得自动恢复 Explicit 或选择。

## 4. 分步实现与验收

### E2b1：来源窗口与激活闭环

- 建立 source factory、受限 Region、来源证据、单调 action 和确定释放；
- 生命周期创建/隐藏/恢复/销毁 source，并把三类激活动作一次性送入 E2a；
- App 只得到有限 `CanRequestKeyboardInteraction` 状态和无句柄命令，标准按钮/AccessKey 由生命周期复核并聚焦当前来源；
- 验证区域外穿透、重复/注入/auto-repeat 拒绝、部分创建失败逆序销毁、系统事件取消和零资源残留。

### E2b2：Explicit 选择与可访问交互

- 主 surface 按当前投影将 pointer 命中映射为匿名 `item:N`；
- activation source 把有限键盘命令映射为现有 `ProductDesktopSelectionRequest`；
- UIA item 只在 Explicit 暴露 SelectionItem/Invoke，读取同一 selection snapshot 并发送对应 UIA 事件；
- pointer、keyboard、UIA 对同一项目产生相同 selection revision、focused/anchor/selected 结果；取消后 Pattern、焦点代理和可命中区域回到安全态。

E2b1 与 E2b2 各自使用独立实现 PR、专项测试、文档回填、PR CI 与 main CI。两者均通过后才可写 E2/M2 工程 Pass；随后仍须在 RC 前执行物理设备、Narrator、高对比、文本缩放和系统表面人工矩阵。

## 5. 非目标

本设计不准入文件打开、拖放、移动、删除或自动整理；不改变配置；不增加任务栏、Widget、插件或全局快捷键；不关闭 #19/#20/#23/#24、ADR、许可证、签名或安装门禁。
