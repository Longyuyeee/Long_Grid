# Stage 135：正式项目选择与可访问交互审计

审计日期：2026-08-14

## 1. 判定

E2b2 的本地工程实现已闭合，当前判定为 **Local Engineering Pass / PR 与 main CI Pending / Manual Evidence Pending**。主 DesktopHost surface 的项目 pointer、activation source 的有限键盘命令，以及 UIA `SelectionItem`/`Invoke` 全部调用既有 `ProductDesktopSelectionRequest` 与同一个 selection transaction；没有新增第二份选择状态。

本切片只选择当前投影中的匿名项目身份，不读取文件内容，不公开路径，也不移动、重命名、删除或写入桌面文件。E2/M2 只有在本 PR 与合并后 main CI 均通过后才能升级为 Engineering Pass；物理鼠标、Narrator、高对比、文本缩放和动态系统表面的人工证据仍保持 Pending。

## 2. 需求对齐

| 合同 | 实现 | 当前判定 |
| --- | --- | --- |
| pointer 项目命中 | Explicit 主 surface 只在当前 lease 目标方格的可见项目行处理真实 `WM_LBUTTONDOWN`；Ctrl/Shift 映射到现有 modifiers | 本地通过 |
| 有限键盘代理 | 初次激活消费成功后，产品自有 activation HWND 临时移除 `NOACTIVATE` 并取得焦点；只处理方向键、Home/End、Space、Escape | 本地通过 |
| UIA 项目模式 | 仅当前 Explicit 目标项目暴露 ListItem、SelectionItem 与 Invoke；Passive 继续为不可聚焦 Text | 本地通过 |
| 同源状态 | pointer、keyboard、UIA 均进入 lifecycle 来源证明后调用 `ApplyInteractionSelection`；selected/focused/anchor/revision 只来自 transaction snapshot | 本地通过 |
| UIA 查询与事件 | `GetSelection`、`GetFocus`、`IsSelected`、`SelectionContainer` 读取同一 snapshot；选择变化发送 IsSelected property 与 SelectionItem 事件 | 本地通过 |
| 可见反馈 | 选中高亮与焦点框分别绘制，刷新由事务成功路径统一触发 | 本地通过 |
| 取消与失效 | Escape 调用既有 cancellation adapter；系统隐藏、投影释放和关闭恢复 `NOACTIVATE`、撤销 Pattern/焦点并清空选择 | 本地通过 |
| 安全边界 | 无全局 Hook、Raw Input、`SendInput`、`RegisterHotKey`、Explorer/WorkerW 集成或文件操作 | 本地通过 |

## 3. 审计修正与兼容性

1. UIA 根 Selection provider 必须在 surface 原子切换期间即可被合同复读；项目 SelectionItem/Invoke 则必须等真实 Explicit transaction snapshot 存在后才开放，两层条件不能混为一个。
2. 主 surface 继续永久 `NOACTIVATE` 并用 `MA_NOACTIVATE` 接收项目 pointer；只有独立有限 activation HWND 可以在用户显式进入后成为键盘代理。
3. 键盘 Space 在没有焦点时选择首项；有焦点时选择该项，Ctrl+Space 复用现有 Ctrl 切换语义。方向/Home/End 直接复用选择控制器的焦点、锚点和范围规则。
4. 生命周期绑定使用具体 surface/source 实例和显示器投影复读目标，不接受 App 传入 HWND、container lease 或伪造 selection snapshot。
5. UIA 文本继续不公开真实路径；Invoke 当前等价于匿名选择，不打开文件或外部内容。

## 4. 自动化与人工证据

本地 Release build 为 0 warning / 0 error；907/907 测试通过；最终本地覆盖率 line 90.75%（12800/14105）、branch 79.40%（4140/5214），门禁通过。新增证据覆盖键盘命令映射、pointer hit-test 纯适配器、pointer/keyboard/UIA 同项目结果一致、Space/Ctrl+Space、surface/source 绑定路径、Escape 恢复，以及原生 UIA 项目的 SelectionItem/Invoke/GetSelection/GetFocus 同源状态。三项原生 DesktopHost probe、143 项 UI automation id 合同、依赖漏洞门禁和人工 launcher 合同通过；人工会话 runbook 已扩展到 E2B2-01～05，但尚无执行者结果。

PR CI、合并 SHA、main CI 与覆盖率在远端完成后回填。

## 5. 下一步

1. 完成本切片 PR CI、合并与 main CI，并回填远端覆盖率和提交证据；
2. 若远端全绿，裁决 E2/M2 Engineering Pass，但明确保留 Manual Evidence Pending；
3. 进入 M3 集成差距审计：只补 DesktopHost 交互与既有配置、恢复、保存链的缺口，不重建工作区，不开启真实文件移动；
4. M3 后执行正式核心旅程、500 项规模、故障恢复和资源长稳预检，推进到 M4-ready；
5. 到达 ReadyForExternalValidation 后停止功能扩张，按 X1～X5 汇合外部证据。

Phase 0、内部 RC、许可证、签名和公开分发均未因本切片完成。
