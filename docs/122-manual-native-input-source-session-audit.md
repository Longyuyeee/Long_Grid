# Stage 122：人工拥有的原生输入来源会话审计

日期：2026-08-13

基线：`main` / `d82c497`（Stage 121、PR #170 已合并且 main CI 通过）

阶段：B6c5（仅建立人工显式拥有的短生命周期输入来源；正式 DesktopHost、Explicit 和真实桌面文件操作仍关闭）

## 1. 本阶段解决的错位

Stage 121 已证明 probe 自有 HWND 能把 pointer、Enter/Space 和 UIA Invoke 归一化到 B6c3，但自动探针使用同步窗口消息，不能证明物理设备或 Narrator。原 B6C3 人工启动器只打开正式 App，而正式 App 没有输入来源，因而无法执行对应矩阵。

本阶段把人工启动器改为只启动 `LongGrid.Spikes.DesktopHostWindowModels` 中的 `--native-input-forwarding-session`。它不启动正式 App、不附着 Explorer，也不把 probe 类接入产品程序集。

## 2. 会话所有权与退出

人工会话必须同时提供场景、匿名操作员编号以及受控环境、隔离来源、禁止 Explicit、恢复方案四项确认。启动器临时设置六个精确进程级开关；probe 从环境重新执行 Host、Interaction、Intent Bridge 和 Input Forwarding 四层策略判断，任一门禁不成立即拒绝启动。

门禁成立后只创建一个可见、非 Topmost 的 ToolWindow。用户可：

- 在窗口内执行一次主指针按下；
- 让窗口获得焦点后按 Enter 或 Space；
- 通过真实 UI Automation/Narrator Invoke 调用根 Provider；
- 按 Escape 或关闭窗口销毁来源并结束消息循环。

窗口显示最近动作、有限状态以及 observed/prepared/rejected 计数。退出后会完成 forwarding adapter、销毁 HWND、清除 UIA Provider、注销随机窗口类，并由启动器恢复全部进程级开关。

## 3. 保持关闭的能力

该会话只把来源已证明的归一化通知送到 Intent 准备桥，不消费 Intent。源码和启动合同继续禁止：

- 全局 Hook、Raw Input、全局键状态轮询和 `SendInput`；
- 正式 App 或 Explorer HWND 接线；
- Admission、Explicit Surface 与真实文件移动/复制/删除；
- 自动截图、录制、原始输入流、路径、标题或用户身份采集；
- 自动写证据文件或把人工结果声明为 Pass。

物理输入是否真实发生只能由操作员按手册复核。程序退出摘要固定输出 `FinalResultStatus: PendingManualEvidence` 和 `PhysicalDeviceInputAutomaticallyVerified: false`。

普通 HWND 消息本身不能可靠证明物理来源或识别外部消息注入；B6c5 只在受控人工会话中把 probe 自有窗口回调视为来源证明。B6c3 adapter 仍会拒绝显式携带 `IsInjected=true` 的通知，但 B6c5 声明 `detectsNativeInjection=false`，不得把该 adapter 合同误写成原生注入检测能力。B6C3-04 的注入标记负向项继续由自动化合同覆盖，人工窗口只执行物理自动重复子项。

## 4. 自动化与人工证据边界

CI 继续运行 Stage 121 的 `--native-input-forwarding --json`，用同步消息验证归一化、拒绝、前台稳定和资源清理。CI 还验证人工启动器的 schema v2 合同与 B6c5 源码负向约束，但不会启动可见人工窗口，也不会模拟物理输入或 Narrator。

本机开发验证包括 Release 编译、旧 B6c4 自动探针、启动器 `-ValidateOnly`、UI 源码合同和完整仓库门禁。B6C3-01 至 B6C3-04 和 B6C3-08 的关闭路径尚未由真实操作员执行，因此总体结论仍为 **PendingManualEvidence**。B6C3-05 至 B6C3-07 依赖失焦、Win+D、全屏、锁屏/RDP、Explorer 与显示 generation 的系统事件会话，本来源窗口不能验证；启动器会明确拒绝用 B6c5 冒充这些场景。

本机自动化结果：Release 构建 0 warning / 0 error；873/873 测试通过；行覆盖率 91.09%（23034/25286），分支覆盖率 80.64%（7304/9058）；锁定还原、格式、依赖漏洞、启动器正负向门禁、UI 源码合同及旧 B6c4 自动探针均通过。RC 交付集必须在本阶段形成干净提交后单独复核。

## 5. 需求对齐

本切片推进了最初需求中的桌面方格直接交互基础：鼠标、键盘和辅助技术现在有可人工执行的隔离来源，而不是只存在无输入的可见原型。它仍不是 iTop/Fences 等竞品的可发布桌面层：用户尚不能在正式 DesktopHost 方格内完成点击或拖放，任务栏美化、小组件/Long助手插件和窗口特效也没有因此开放。

安全顺序保持不变：先完成人工矩阵和证据复核，再单独评审正式产品来源与 Explicit Surface；真实桌面文件操作继续后置。

## 6. 下一阶段

下一阶段 B6c6 先执行并复核 B6c5 能承载的人工矩阵：物理鼠标、Enter/Space、Narrator/UIA、物理自动重复和关闭恢复；同时为 B6C3-05 至 B6C3-07 建立独立的系统事件人工会话。只有两组匿名证据完整、恢复结果通过且负责人批准后，才能规划正式 App 输入来源；在此之前不得把本阶段标记为产品交互完成。
