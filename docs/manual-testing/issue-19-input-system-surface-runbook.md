# Issue #19 输入、无障碍与系统表面人工矩阵运行手册

状态：**Ready to execute / Pending manual evidence**

本手册为 Issue #19 的 I19-01–I19-10 提供安全、可复读的执行入口。自动探针、CI 和 `-ValidateOnly` 只能证明入口可用，不能替代键鼠手感、Narrator 听读、视觉判断或系统表面恢复结论。

## 1. 安全边界

- 仅在专用测试账户或无个人内容的受控环境执行；
- 操作员只使用匿名标签 `O1`–`O9`，仓库内不得保存身份映射；
- 每次只执行一个场景，失败后先恢复基线，再决定是否继续；
- 不在仓库保存用户名、路径、文件名、窗口标题、录音、原始截图或设备标识；
- 拖放只使用专用沙箱中的自有测试文件；任何误移动立即判 `Fail`；
- 启动器不发送输入、不修改系统设置、不重启 Explorer、不采集证据、不写结果文件。

## 2. 会话预检

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-Issue19ManualMatrixSession.ps1 `
  -ValidateOnly
```

正确输出必须包含 `PendingManualEvidence`。预检不得打开窗口或创建场景结果。

## 3. 启动单场景

例如由匿名操作员 O1 执行键盘场景：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-Issue19ManualMatrixSession.ps1 `
  -Scenario I19-01 `
  -OperatorId O1 `
  -Configuration Release
```

启动后只按本场景清单操作，按 `Esc` 关闭原型。不得把同一轮操作同时填写到多个场景。

## 4. 场景卡

| ID | 场景 | 必须人工确认 | 恢复确认 |
|---|---|---|---|
| I19-01 | 键盘 | Tab/Shift+Tab、方向键、Enter、Space、Esc；焦点顺序和可见性 | 焦点回到安全位置，原型可关闭 |
| I19-02 | 鼠标 | 单击、双击、框选、滚轮、边界命中与 Passive 穿透 | 指针/前台窗口正常 |
| I19-03 | 触控/笔 | 点击、滚动、长按、拖动；无幽灵点击 | 输入门关闭后无残留动作 |
| I19-04 | 拖放 | 内部重排、沙箱文件拖入/拖出、取消；引用/移动语义 | 测试文件位置和内容复核 |
| I19-05 | Narrator | Name、角色、状态、位置、操作反馈和顺序 | Narrator 恢复测试前状态 |
| I19-06 | 高对比/文本缩放 | 焦点、选择、禁用状态可区分且无裁切 | 主题和缩放恢复 |
| I19-07 | Win+D/Peek | 显隐、焦点、遮挡和恢复策略 | 桌面与前台窗口正常 |
| I19-08 | 全屏 | 进入/退出受控视频或应用全屏 | 全屏退出且宿主恢复 |
| I19-09 | Alt+Tab/任务视图 | 不出现普通应用项、不抢前台 | 切换器关闭、焦点正常 |
| I19-10 | Explorer 重启 | 监听、层级、交互恢复且无孤儿窗口 | Explorer、任务栏和桌面正常 |

若当前原型不具备场景所需能力，记录 `Fail` 或 `Inconclusive` 及原因，不得把“未实现”改写为 Pass。

## 5. 结果纪律

1. 使用 Phase 0 出口手册中的单轮证据模板，记录 commit、环境、实际步骤、首次结果、缺陷和恢复确认。
2. 结果只能是 `Pass`、`Fail` 或 `Inconclusive`；重试成功不能覆盖首次失败。
3. Narrator 结论必须由人工听读；UIA 属性存在不等于可访问体验通过。
4. 若证据需要截图或录像，先脱敏并存入访问受控位置，只在 Issue 中提供受控引用；默认不提交仓库。
5. I19-01–I19-10 全部具备可复读证据、恢复确认且无开放阻断缺陷后，才允许请求关闭 Issue #19。
