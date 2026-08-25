# E2b 产品激活与项目选择人工会话手册

状态：`PendingManualEvidence`
适用范围：E2b1 activation source 与 E2b2 项目选择的物理输入、Narrator、键盘焦点和系统表面恢复验证。

## 安全边界

- 只在受控测试账户和可恢复桌面上执行；开始前记录桌面文件清单并关闭无关应用。
- 本会话会让产品表面从 Passive 进入 Explicit，但不得读取文件内容，不得移动、重命名、删除或写入桌面文件。
- 不使用输入模拟器、全局 Hook、Raw Input、`SendInput`、全局热键或 Explorer/WorkerW 注入。
- 一次只执行一个场景。异常、残留按钮窗口或无法恢复 Passive/Hidden 时立即关闭 Long方格并记录失败。
- 操作者只使用 `O1`～`O9` 匿名标签；本脚本不采集证据、不写结果文件，也不把人工结果升级为 Pass。

## 启动

以 E2B1-01 为例：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\eng\Start-DesktopInteractionActivationSession.ps1 `
  -Scenario E2B1-01 `
  -OperatorId O1 `
  -AcknowledgeControlledEnvironment `
  -AcknowledgeExplicitWithoutFileOperations `
  -AcknowledgeRecoveryPlan
```

每个场景使用一次全新进程，开始前确认桌面方格标题右侧只出现有限的“↗”激活按钮，按钮外区域仍可点击原桌面。

## 场景

| 场景 | 操作 | 预期结果 |
| --- | --- | --- |
| E2B1-01 | 使用真实鼠标单击一个未锁定方格的“↗”按钮 | 仅该有限区域接收输入；表面进入 Explicit；无桌面文件变化 |
| E2B1-02 | 在主窗口使用 `Alt+I` 或聚焦后按空格调用“进入桌面交互” | 经同一 forwarding→preparation→consumption 链进入 Explicit；App 不获得 HWND |
| E2B1-03 | 启动 Narrator，定位“进入桌面方格交互”按钮并调用 | UIA Invoke 可用并进入同一 Explicit 状态；项目选择只在进入后开放 |
| E2B1-04 | 进入 Explicit 后执行 Win+D、切换全屏应用或锁定/恢复会话中的一种 | Prepared/Explicit 失效，激活按钮与宿主一起隐藏；稳定复读后只恢复 Passive |
| E2B1-05 | 在 Passive、Explicit 和系统表面隐藏状态各关闭一次应用 | 每次都逆序销毁激活源与宿主；进程退出后无残留 Long方格窗口 |
| E2B2-01 | 进入 Explicit 后用真实鼠标依次单击两个匿名项目，并分别测试 Ctrl、Shift | 命中项目出现选中高亮和独立焦点框；单选、切换和范围选择符合 Windows 约定 |
| E2B2-02 | 进入 Explicit 后使用方向键、Home、End、Space、Ctrl+Space 和 Shift+方向键 | 有限焦点代理只处理这些命令；视觉结果与鼠标选择一致；不触发全局快捷键 |
| E2B2-03 | 使用 Narrator 定位项目，执行 SelectionItem Select/Add/Remove 与 Invoke | `GetSelection`、`IsSelected`、焦点和播报与视觉状态一致；动作只改变匿名选择状态 |
| E2B2-04 | 分别用鼠标、键盘和 Narrator 选择同一项目 | 三条路径得到相同的 focused、anchor、selected 结果，没有重复选择或修订跳变 |
| E2B2-05 | 在 Explicit 选择后按 Escape，再执行 Win+D、锁屏恢复和投影变化 | SelectionItem/Invoke 与焦点代理撤销，主表面恢复 Passive/Hidden；桌面文件清单不变 |
| E2B2-06 | 进入 Explicit 后按 Tab 切到方格标题焦点；依次按方向键、Shift+方向键、Alt+方向键，再按 Tab/Shift+Tab 返回项目 | 标题区出现可见焦点框；普通键每次移动 1 DIP、Shift 每次移动 8 DIP，Alt 左/上缩小宽/高、Alt 右/下扩大宽/高；每次只产生一个保存 revision；返回后方向键继续选择项目；Esc/隐藏/退出清除标题焦点 |

## 失败记录

以下任一情况均记录 `Fail`，不得用自动化结果覆盖：按钮外区域被遮挡、锁定方格出现激活按钮、Narrator 无法调用或选择、三条路径状态不同、Escape 后仍有项目 Pattern/焦点代理、一次动作进入两次、系统事件后仍保持 Explicit、关闭后残留窗口、桌面文件发生变化。

E2b 自动化通过只能记为 `EngineeringPass`；物理设备、Narrator、高对比、文本缩放和动态系统表面结果仍保持 `PendingManualEvidence`。
