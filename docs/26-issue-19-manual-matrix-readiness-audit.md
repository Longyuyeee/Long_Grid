# Issue #19 人工输入与系统表面矩阵就绪审计

审计日期：2026-08-04（Unicode 窗口边界增量复审）

基线：`main` / `19e2359` + Issue #19 Unicode 窗口边界分支

结论：**Ready to execute / Pending manual evidence / 不得关闭 Issue #19**

## 1. 已有证据

- P0-04/P0-05b1 已提供不读取真实桌面数据的可见 DesktopHost 交互切片；
- 自动 smoke 已验证 UIA 树、SelectionItem/Invoke Pattern、事件和资源闭环；
- P0-07b2b2b2b4a 已验证输入 Region 开/关/重开和 UIA Fragment 边界；
- Phase 0 出口手册已定义 I19-01–I19-10、通过条件和统一证据模板。

这些证据只建立人工测试对象和技术下界，不证明真实输入、Narrator 或系统表面体验通过。

## 2. 本阶段补齐

- `eng/Start-Issue19ManualMatrixSession.ps1` 固定场景 ID、匿名操作员标签、commit 和环境清单；
- 正常模式为每个场景启动全新的 `--interactive-slice` 进程；
- 启动器不合成输入、不修改设置、不重启 Explorer、不采证、不写结果；
- 主持手册把十个场景拆为单场景卡，并要求逐项恢复系统状态；
- CI 只运行 `-ValidateOnly`，结果固定为 `PendingManualEvidence`。

## 3. 证据边界

| 能力 | 自动门禁可证明 | 仍需人工证明 |
|---|---|---|
| 会话链 | 文件、项目、参数和隐私合同存在 | 测试人员按同一 commit 执行 |
| 输入 | 不自动发送输入 | 键鼠、触控、笔、拖放手感与首次失败 |
| 无障碍 | 原型和现有 UIA 下界可启动 | Narrator 听读、焦点合理性、高对比和缩放 |
| 系统表面 | 启动器不会主动改变系统 | Win+D、全屏、Alt+Tab、任务视图、Explorer 恢复 |
| 结果 | 固定保持 `PendingManualEvidence` | Pass/Fail/Inconclusive、缺陷和恢复确认 |

## 4. 尚未完成

- I19-01–I19-10 尚未在受控环境逐项执行；
- 触控/笔、Narrator、高对比、文本缩放和系统表面仍无人工原始证据；
- 现有原型可能暴露未实现能力；应如实记录 Fail/Inconclusive，不在工具层掩盖；
- 自动化通过不得更新 ADR-0001 的人工矩阵勾选项。

## 5. 2026-08-04 I19-01 尝试与缺陷收口

匿名操作员 O1 按同一 `main` commit 启动 I19-01。第一次启动的进程正常响应，但外部窗口标题只有首字符，且通用 Windows 窗口控制工具无法返回该 ToolWindow；未发送任何键盘或鼠标输入，结果记为 `Inconclusive`。

审计确认窗口类和创建函数使用 Unicode，但默认窗口过程与消息循环未显式绑定 `W` 入口。修复将它们统一为 `DefWindowProcW`、`GetMessageW`、`DispatchMessageW`，并在 interactive smoke 中通过 `GetWindowTextW` 要求完整标题。修复后 Release smoke 为 `NativeWindowTitleUnicodeVerified=true`，UIA、Pattern、事件、初始不激活和资源清理仍全部通过。

第二次启动已能从进程回读完整标题，但通用控制工具仍按其普通窗口策略过滤 `WS_EX_TOOLWINDOW`。没有移除 ToolWindow 样式、猜测坐标或使用内部自动化冒充人工输入；未发送输入，I19-01 继续保持 `Inconclusive/Pending`。需要真人直接操作可见原型，或使用能显式定位 ToolWindow 且不改变其窗口语义的受控工具，才能产生最终人工证据。

## 6. 下一动作

按[Issue #19 运行手册](manual-testing/issue-19-input-system-surface-runbook.md)一次只执行一个场景，将脱敏结果写回 Issue #19。十项完成前，路线图中的 P0-04/P0-05b2 保持未勾选。
