# B6c3 隔离输入转发人工会话手册

本手册只验证“归一化输入 → Intent 准备”的隔离边界。正式 DesktopHost HWND 仍为 `HTTRANSPARENT`；会话不安装全局 Hook、不调用 `SendInput`、不进入 Explicit、不修改真实桌面文件，也不自动生成通过证据。

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

## 记录规则

每项仅记录匿名操作员、Windows build、DPI/显示器类别、场景编号、预期/实际有限状态和清理结果。不得记录原始输入流、键值、文件路径、窗口标题或用户标识。代码与 CI 只能证明合同和模拟状态；在真实矩阵完成并人工复核前，总结果必须保持 `PendingManualEvidence`。
