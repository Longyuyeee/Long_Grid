# Stage 134：正式产品激活源与交互入口审计

审计日期：2026-08-14

## 1. 判定

E2b1 的工程实现已闭合，判定为 **Engineering Pass / Manual Evidence Pending**。Long方格现在由每显示器最多一个产品自有 activation HWND 接收首次激活动作；窗口 Region 只覆盖未锁定方格标题右侧的有限按钮，既有 M1 Passive 整窗穿透宿主没有被改造成输入窗口。

pointer、App keyboard command 和 UIA Invoke 都生成来源可证明、全局单调 action sequence 与唯一 action id，并统一进入既有 forwarding → preparation → atomic consumption 链。成功动作至多一次进入 Explicit；未开放桌面项目选择，也未开放任何桌面文件写入、移动、重命名或删除能力。

E2b 尚未整体完成：下一步固定为 E2b2 项目 pointer/keyboard/UIA 选择与同源可访问状态。物理鼠标、Narrator、动态系统表面及关闭残留仍需按新增人工会话手册执行，不能由自动化代替。

## 2. 需求与实现对齐

| 合同 | 当前实现 | 判定 |
| --- | --- | --- |
| 独立输入所有权 | 新增每显示器 activation source；App 只看到 `CanRequestKeyboardInteraction` 与无句柄命令 | 通过 |
| 有限命中区域 | Region 仅合并未锁定方格的 30 DIP 标题按钮；窗口非 Topmost、无 owner、初始 NoActivate | 通过 |
| pointer | 只处理 activation HWND 内的 `WM_LBUTTONDOWN`；使用 `GetCurrentInputMessageSource` 标记注入来源 | 通过 |
| keyboard | 设置页标准按钮与 `Alt+I` 形成显式用户命令；不注册全局热键、不发送模拟输入 | 通过 |
| UIA | activation HWND 暴露 Button/Invoke provider，调用进入同一消费链 | 通过 |
| 去重与时效 | 复用既有 action id/sequence、重放、auto-repeat、来源、时间窗和 Passive 复读门禁 | 通过 |
| 生命周期 | 投影创建时创建；系统表面事件共同隐藏/恢复；投影替换、故障与关闭时先释放激活源再释放宿主 | 通过 |
| 焦点撤销 | MainWindow 仅豁免当前且重新证明归属的 activation HWND；未知或陈旧窗口继续报告 FocusLost | 通过 |
| 安全边界 | 无 Hook、Raw Input、`SendInput`、`RegisterHotKey`、Explorer/Progman/WorkerW 或文件操作 API | 通过 |

## 3. 审计修正

1. 不能在 M1 只读宿主上直接增加鼠标/键盘消息；本切片用独立 HWND 保留原整窗穿透合同。
2. activation source 创建失败、Region 复读失败或部分隐藏/恢复失败统一进入 lifecycle Faulted，并逆序释放所有输入遮罩和宿主资源。
3. App 的键盘入口没有接收句柄、坐标或 prepared intent，只能请求生命周期控制器执行一次当前可用交互。
4. UIA 激活入口只负责进入 Explicit，不虚构 E2b2 的 `SelectionItem`、项目 Invoke、焦点或选中项事件。
5. 自动化 provider 调用只证明工程链路；Narrator 与真实物理输入继续保持 `PendingManualEvidence`。

## 4. 自动化与人工证据

本地收口结果：locked restore 与 format 通过；Release build 为 0 warning / 0 error；890/890 测试通过；line 90.39%（24806/27444）、branch 79.24%（7834/9886）；启动链、E2b1 人工会话合同、143 项 UI automation id 合同、三项原生 DesktopHost probes 与依赖漏洞门禁通过。禁止 API 扫描未发现 Hook、Raw Input、`SendInput`、`RegisterHotKey`、Explorer/Progman/WorkerW 或桌面文件操作入口。

新增 `Start-DesktopInteractionActivationSession.ps1` 与人工手册，覆盖真实鼠标、App `Alt+I`、Narrator Invoke、系统表面隐藏恢复和进程关闭残留。当前没有执行者证据，因此这些结果明确保持 Pending。

## 5. 剩余方向

下一代码切片是 E2b2：

1. Explicit surface 内的项目 pointer 命中、activation source 的有限键盘命令和 UIA `SelectionItem`/项目 Invoke 映射到同一 `ProductDesktopSelectionRequest`；
2. `GetSelection`、`IsSelected`、`SelectionContainer`、键盘焦点、视觉状态与 UIA 事件只读取同一 selection transaction snapshot；
3. lease、投影或代次变化立即拒绝并恢复安全状态；
4. E2b2 完成且 PR/main CI 通过后，才重新裁决 E2/M2 工程状态。

M3、M4-ready、Phase 0、内部 RC 和公开发布均未因本切片提前完成。
