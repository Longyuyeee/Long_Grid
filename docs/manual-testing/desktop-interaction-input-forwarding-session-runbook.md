# B6c3 隔离输入转发人工会话手册

本手册只验证“归一化输入 → Intent 准备”的隔离边界。正式 DesktopHost HWND 仍为 `HTTRANSPARENT`；会话不安装全局 Hook、不调用 `SendInput`、不进入 Explicit、不修改真实桌面文件，也不自动生成通过证据。

Stage 121 的 `--native-input-forwarding` 自动探针只验证同步 Win32 消息和真实 HWND UIA Provider 的归一化合同，结果为 Conditional Pass；它明确没有验证物理设备或 Narrator，因此不能替代本手册的人工执行。Stage 122 起，下面的启动器只创建 probe 自有可见短会话窗口，不再启动正式 App；窗口显示有限计数，按 Escape 或关闭窗口会销毁来源，最终状态仍固定为 `PendingManualEvidence`。

## 前置条件

- 在隔离测试账户或可恢复虚拟机中执行；关闭无关应用并保存工作。
- 使用匿名操作员编号 O1–O9，不记录账户名、路径、窗口标题或文件名。
- 准备任务管理器和恢复方案；`LONGGRID_DISABLE_DESKTOP_INTERACTION=1` 是紧急停止边界。
- 每次只执行一个场景；启动器退出后必须确认六个进程级开关恢复原值。

## 场景矩阵

| 场景 | 动作 | 预期 |
|---|---|---|
| B6C3-01 | 已证明来源的一次主指针按下 | 只形成一次 Prepared；不进入 Explicit |
| B6C3-02 | Enter/Space 键盘激活 | 与指针形成相同的有界 Intent 语义 |
| B6C3-03 | Narrator/UIA Invoke | 与鼠标、键盘共享目标与 generation 约束 |
| B6C3-04 | 重复序号、重复 ActionId、自动重复或注入标记 | 拒绝，不形成第二次准备 |
| B6C3-05 | 失焦、Win+D、全屏切换 | 立即失效准备并隐藏；稳定复核后仅回 Passive |
| B6C3-06 | 锁屏、注销候选、RDP 切换 | Fail-closed；不遗留输入捕获或前台占用 |
| B6C3-07 | Explorer 重启与显示器 generation 变化 | 旧准备失效；新证据完成前拒绝输入 |
| B6C3-08 | 紧急禁用和应用关闭 | 退出有限、Surface/订阅释放、开关恢复 |

## 启动

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\eng\Start-DesktopInteractionInputForwardingSession.ps1 `
  -Scenario B6C3-01 -OperatorId O1 `
  -AcknowledgeControlledEnvironment `
  -AcknowledgeIsolatedSource `
  -AcknowledgeNoExplicitInteraction `
  -AcknowledgeRecoveryPlan
```

启动后只对可见的“Long Grid manual input source”窗口执行当前场景。B6C3-01 可点击窗口，B6C3-02 可先点击再按 Enter/Space，B6C3-03 使用 Narrator 或独立 UIA 客户端执行 Invoke，B6C3-04 只执行真实按键自动重复子项，B6C3-08 只执行关闭与清理子项。原输入启动器会继续拒绝 B6C3-05 至 B6C3-07；这些场景必须使用下方独立系统表面启动器，不能用普通来源窗口替代。不要把程序显示的 Prepared 计数当作人工 Pass；仍须按场景记录预期、实际和恢复结果。

该窗口不具备原生注入检测能力；普通 HWND 消息不能证明物理来源。B6C3-04 中显式 `IsInjected=true` 的拒绝由 adapter 自动化合同覆盖，人工会话不得声称验证了注入检测。

### B6c6 系统表面会话

B6C3-05、B6C3-06 和 B6C3-07 的 Explorer 子项使用独立启动器。它不主动改变系统状态；操作员必须在受控环境中准备恢复通道，并且每次只执行一个场景：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\eng\Start-DesktopInteractionSystemSurfaceSession.ps1 `
  -Scenario B6C3-05 -OperatorId O1 `
  -AcknowledgeControlledEnvironment `
  -AcknowledgeSystemStateChange `
  -AcknowledgeNoDisplayTopologyEvidence `
  -AcknowledgeNoExplicitInteraction `
  -AcknowledgeRecoveryPlan
```

先点击窗口或按 Enter/Space，确认显示 `Prepared`；再执行当前场景。危险事件后来源窗口应隐藏，`PreparedIntentInvalidationCount` 应增加；连续两个安全样本后窗口只以非激活方式恢复，必须重新操作才能产生新 Prepared。关闭窗口后确认无残留进程和输入层。

`B6C3-07-EXPLORER` 只验证 Explorer Shell 身份变化，不验证显示器 generation。显示热插拔、旋转、DPI/WorkArea 和拓扑代次继续使用 Issue #20/A5 手册，不得在本会话记为 Pass。

## 记录规则

每项仅记录匿名操作员、Windows build、DPI/显示器类别、场景编号、预期/实际有限状态和清理结果。不得记录原始输入流、键值、文件路径、窗口标题或用户标识。代码与 CI 只能证明合同和模拟状态；在真实矩阵完成并人工复核前，总结果必须保持 `PendingManualEvidence`。
