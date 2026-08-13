# Desktop Interaction Intent 准备会话运行手册

本手册仅用于 B6c2 受控开发会话。它验证第三重门禁和“只准备 Intent”边界，不启用正式 HWND 输入、Explicit、Selection、拖放或真实文件操作。

## 前置条件

- 使用可恢复的专用 Windows 11 x64 测试会话；
- 关闭无关应用并确认可以从任务管理器结束 LongGrid；
- 记录匿名操作员标签 O1–O9，不记录账户、设备、窗口标题或路径；
- 确认 `LONGGRID_DISABLE_DESKTOP_INTERACTION` 未精确设为 `1`；
- 一次只执行一个场景，结束后恢复环境并关闭 Long方格。

## 场景

| 场景 | 操作 | 预期边界 |
|---|---|---|
| B6C2-01 | 使用受控启动器启动，观察产品方格与控制中心 | 方格保持 Passive/穿透，不抢焦点；没有 Explicit 或文件动作 |
| B6C2-02 | 普通切换焦点、显示桌面，再等待稳定恢复 | 系统变化先 Hidden；稳定复核后只恢复 Passive，旧 Intent 准备必须失效 |
| B6C2-03 | 关闭应用并检查残留 | 订阅、Timer、准备状态和产品 HWND 释放；没有残留输入层或进程 |

当前 App 没有把鼠标、键盘或 UIA 激活转送给 Intent bridge，因此本阶段不能通过人工点击证明 Intent 已准备。自动化只证明策略、唯一命中、代次、超时和失效合同；真实产品输入转接属于下一切片。

## 启动

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-DesktopInteractionIntentSession.ps1 `
  -Scenario B6C2-01 `
  -OperatorId O1 `
  -AcknowledgeControlledEnvironment `
  -AcknowledgeNoExplicitInteraction `
  -AcknowledgeRecoveryPlan
```

启动器不会执行系统动作、生成输入、捕获证据或写结果文件。观察结果必须人工记录在受控审计位置；在三项场景完成并复核前，最终状态保持 `PendingManualEvidence`。
