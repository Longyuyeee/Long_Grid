# Stage 110：DesktopHost 只读 UIA 与产品会话合同审计

日期：2026-08-12

范围：阶段 A 的 A5 自动化子切片。基线为 Stage 109 动态拓扑生命周期；本切片只增加辅助技术可读性、被动窗口复读和受控人工矩阵，不开放桌面输入或文件操作。

结论：**正式 DesktopHost HWND 已具备与实际投影一致的只读 UI Automation Fragment，并在进入 Ready 前复读非激活窗口合同。** UIA 客户端能读取 Root、方格和当前实际可见项目；折叠或被高度裁掉的项目不进入树；所有节点不可聚焦且不提供 Selection、SelectionItem、Invoke 或编辑 Pattern。任一表面无法证明 UIA 或被动窗口合同，整个批次失败关闭。

## 1. 需求与竞品体验对齐

iTop/Fences 类产品不能只“画出来”，还必须让 Narrator/Inspect 理解桌面分组，同时避免只读阶段被辅助技术误认为可以打开、选择或移动真实文件。本切片采用以下语义：

- 显示器表面：`Pane`，“Long方格桌面只读区域”；
- 正式方格：`Group`，名称包含标题、可见项目数、折叠状态和只读状态；
- 可见项目：`Text`，只公开正式读模型已经允许展示的名称；
- AutomationId 使用有限序号，不公开 ContainerId、路径或 Shell 身份；
- RuntimeId 绑定产品表面实例与容器/项目序号，在同一 HWND 树内唯一；
- Bounds 使用与 GDI 绘制、Window Region 相同的 `ProductDesktopHostSurfaceLayout`，避免视觉/UIA 坐标分叉；
- 折叠方格、被 WorkArea 裁掉的行不暴露隐藏子节点；
- `GetFocus` 永远为空，`SetFocus` 无动作，所有节点 `IsKeyboardFocusable=false`；
- 所有 `GetPatternProvider` 返回空，不提前引入 Selection/Invoke。

## 2. 正式 HWND 接线与失败补偿

正式窗口在创建后建立 UIA Root，并通过 `WM_GETOBJECT`/`AutomationInteropProvider.ReturnRawElementProvider` 返回服务端 Provider；销毁前撤销 UIA provider。生命周期表面合同增加：

- `ReadOnlyAccessibilityAttested`；
- `PassiveWindowContractAttested`。

被动窗口复读要求同时满足 `ToolWindow`、`Layered`、`NoActivate`、`Transparent`，且没有 `Topmost`、没有 Owner、不是当前前台窗口。生命周期只有在每个显示器表面两项均为真并通过既有所有权复读后才发布 `ReadyReadOnly`；否则整批注销、销毁并报告零 HWND。控制中心在 Ready 状态显示“UIA 只读树与非激活窗口合同已复读”。

为使用 Windows UI Automation Provider 合同，`LongGrid.Infrastructure` 与测试程序集明确目标为 `net8.0-windows` 并引用 WindowsDesktop/WPF Framework Reference；Core 和不依赖 Infrastructure 的探针继续保持原目标。锁定依赖文件已同步，启动链的 `--locked-mode` 仍通过。

## 3. 产品会话矩阵

新增 `Start-DesktopHostProductSessionMatrix.ps1` 和运行手册，覆盖：

- A5-01：Narrator/Inspect 正式树；
- A5-02：Win+D/显示桌面往返；
- A5-03：独占或无边框全屏覆盖与恢复；
- A5-04：Explorer 正常重启；
- A5-05：锁定/RDP/本地会话往返；
- A5-06：关闭、重复启动和资源释放。

启动器只设置精确开发开关并启动 App，不发送输入、不改变显示/电源/会话状态、不截屏、不写结果。匿名 OperatorId、受控环境和恢复方案是 live session 的强制前置条件。ValidateOnly 与 CI 只能证明流程合同存在，所有最终结果保持 `PendingManualEvidence`。

## 4. 自动化证据

- locked restore：通过；
- Release build：0 warning / 0 error；
- Release 全量测试：674/674；
- 覆盖率：行 91.31%，分支 80.28%，通过 90%/75% 门禁；
- 真实产品 HWND UIA 测试：读取 2 个方格、2 个展开项目，折叠方格无子项；名称、AutomationId、Bounds、不可聚焦和无 Pattern 均通过；
- UIA/原生窗口测试串行组：避免多个真实 HWND 的进程级 UIA 注册相互干扰；
- UI 源码合同：142-ID、只读 Provider、无 Selection/Invoke、被动窗口复读通过；
- 启动、Issue #20、A5 产品会话、干净会话和单实例合同通过。

配置持久化、文件操作安全、缩略图隔离、依赖漏洞和内部 unsigned RC 门禁继续由完整 CI 复核；本地结果不替代远端 PR/main CI。

## 5. 权限与数据边界

- 文件内容读取：零新增；
- 桌面文件写入、移动、重命名、删除：零新增；
- 点击、键盘、触摸、拖放：仍关闭；
- Selection/Invoke/UIA Value：未提供；
- Explorer 注入、`Progman`/`WorkerW`：未使用；
- 任务栏、Widget、Long 助手插件：未加载；
- 默认启动：仍为零 DesktopHost HWND，只有 `LONGGRID_ENABLE_DESKTOP_HOST=1` 才进入开发审计路径。

## 6. 阶段判断与下一步

阶段 A 的代码与自动化子切片 A1–A5 已形成闭环，但 A5-01..A5-06、Issue #20 和 24 小时资源趋势仍缺少受控人工证据，因此不能宣称 DesktopHost 产品阶段已经最终验收。

在不伪造外部证据的前提下，下一代码切片进入 **B1：桌面交互准入与模式状态机**：设计 Passive→ExplicitInteraction 的独立默认关闭开关、命中区域、焦点/取消语义及陈旧 generation 拒绝；第一步只建立策略和可测试边界，不直接开放真实文件、拖放或发布默认交互。人工矩阵可以与 B1 工程并行执行。
