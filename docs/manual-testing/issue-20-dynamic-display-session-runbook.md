# Issue #20 动态显示与会话矩阵运行手册

状态：**Ready to execute / Pending manual evidence**

本手册把 I20-01–I20-08 映射到现有只读 `--matrix-scenario` observer。observer 只记录公开事件、稳定采样和资源闭环；它不会改变显示、设备、电源或会话状态，也不能独立证明窗口视觉、输入区域或恢复体验正确。

## 1. 执行前安全条件

- 仅在有备用恢复路径的专用测试机或可还原环境执行；
- 保存工作并关闭系统更新、安装、文件操作和包含隐私内容的窗口；
- 记录原缩放、方向、主屏、投影模式和会话状态，但不把设备 ID、EDID、窗口标题或账户写入仓库；
- 操作员使用匿名标签 `O1`–`O9`；
- 每次只执行一个变化和对应恢复；失败后先恢复基线；
- 热插拔、睡眠和 RDP 必须准备本地恢复或备用控制通道。

## 2. 会话预检

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-Issue20DisplayMatrixSession.ps1 `
  -ValidateOnly
```

正确输出包含 `PendingManualEvidence`，且不会打开 observer 窗口、改变系统状态或写结果文件。

## 3. 启动示例

例如 O1 执行 100%→150%→100% 缩放往返：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-Issue20DisplayMatrixSession.ps1 `
  -Scenario I20-01 `
  -OperatorId O1 `
  -WatchSeconds 120 `
  -AcknowledgeControlledEnvironment `
  -AcknowledgeRecoveryPlan `
  -Configuration Release
```

I20-03 必须分开执行 `-HotPlugAction Detach` 和 `-HotPlugAction Attach`，不能用单次结果替代完整拔出/接回往返。

## 4. 场景与 observer 映射

| ID | 人工场景 | observer | 必须人工复核 |
|---|---|---|---|
| I20-01 | 100%→150%→100% | `scale` | DIP/像素、布局、Region、焦点及恢复 |
| I20-02 | 横向→纵向→横向 | `rotate` | 窗口可见、输入区域和原方向恢复 |
| I20-03 | 拔出/接回副屏或扩展坞 | `detach` + `attach` | 歧义阻断、项目不丢失、位置可解释 |
| I20-04 | 电脑/复制/扩展/第二屏 | `projection` | 旧代次不提交、每种模式均可恢复 |
| I20-05 | 睡眠→唤醒 | `sleep-resume` | 无循环重排、资源增长或永久隐藏 |
| I20-06 | 锁定→解锁 | `lock-unlock` | 暂停期无提交，解锁后重新采样 |
| I20-07 | 本地→RDP→本地 | `remote-session` | 回本地后的显示、窗口、输入与资源 |
| I20-08 | 跨混合 DPI 屏拖动 | `scale` | `WM_DPICHANGED` 建议矩形、Bounds 和 UIA 一致 |

I20-01 与 I20-08 共用事件 observer，但人工目标不同，结果不得互相复用。

## 5. 结果纪律

1. observer 的 `Observed Pass` 只表示预期公开事件、最终 `Ready` 和资源闭环满足；最终状态仍是 `PendingManualEvidence`。
2. observer exit 4 / `Inconclusive` 表示设备、策略或通知不足；不得手工改成 Pass。
3. 使用 Phase 0 出口模板记录首次结果、实际步骤、脱敏证据、缺陷和恢复确认。
4. 任何布局越界、输入遮挡、焦点抢占、持续震荡、旧代次提交或无法恢复均为 `Fail`。
5. I20-01–I20-08 全部有可复读证据且无开放阻断缺陷后，才允许请求关闭 Issue #20。
