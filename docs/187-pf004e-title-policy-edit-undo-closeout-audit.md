# Stage 187：PF-004E 标题策略、编辑撤销与 PF-004 工程收口审计

- 日期：2026-08-24
- 分支：`codex/pf002d-create-preview`
- 起始基线：`95f5f6c`
- 对齐编号：`PF-004E`
- 结论：`EngineeringComplete`；PF-004 顶层转为 `EngineeringComplete / ProductEvidencePending`

## 1. 目标与边界

本阶段只关闭 Stage 153 明确留下的 PF-004 验收项：标题显示策略、标题双击行为，以及重命名、锁定、折叠、外观和布局编辑的统一最近撤销。所有操作仍只修改 Long方格配置，不移动、删除或重命名真实桌面文件。

标题显示使用有限枚举 `Always / Hover / Hidden`，标题双击使用 `ToggleCollapsed / None`。策略写入正式配置 Schema，经 validator、projector、resolver、只读模型和 DesktopHost 投影完整往返；控制中心只有有限 ComboBox，不接受脚本或任意命令。

## 2. 实现与交互审计

### 2.1 真实桌面标题交互

只读桌面 HWND 改为精确命中：标题文字区和空态创建入口返回 `HTCLIENT`，其余区域继续 `HTTRANSPARENT`；标题右侧 140 DIP 控件仍由既有有限 activation HWND 拥有。窗口类启用双击消息，悬停通过 `TrackMouseEvent`/`WM_MOUSELEAVE` 控制显示，双击只在策略为 `ToggleCollapsed`、方格未锁定且输入来源可信/非注入时提交绑定 container/display/revision/topology 的正式折叠请求。

`Hidden` 只隐藏视觉标题，不删除 UIA 标题事实或控制中心管理入口，因此不会让方格失去键盘/无障碍管理能力。

### 2.2 统一撤销与保存失败补偿

新增 `ProductWorkspaceContainerEditUndoToken`，包含 operation id、edit revision、编辑种类、编辑后与恢复目标的完整配置指纹。Coordinator 对 Rename、SetLocked、SetCollapsed、SetAppearancePreset 和 SetPlacementPreset 生成单一最近撤销令牌；任何后续成功编辑都会清理旧令牌，token、revision 或当前配置指纹不一致时失败关闭。

控制中心和桌面标题命令均绑定 save revision。异步保存失败时使用同一个撤销令牌补偿内存状态，补偿本身重新进入正式保存队列；自动补偿会消费令牌，避免重复撤销。唯一“最近撤销”按钮现在能显示并执行重命名、锁定状态、折叠状态、外观或布局撤销。

### 2.3 审计发现并修正 PF-004D 偏差

复审发现 PF-004D 原生菜单虽然把删除显示为 Enabled，但生命周期动作可用性 switch 遗漏 `DeleteContainerConfiguration`，实际选择会在进入 App 前被拒绝。本阶段补齐 `CanDeleteContainerConfiguration` 路由，并在生命周期真实调用链测试中同时验证外观和删除请求。该问题说明“菜单状态测试”不能替代“选择后完整请求链测试”。

## 3. 真实 Expected / Actual

| 场景 | Expected | Actual | 差异 |
| --- | --- | --- | --- |
| 真实 HWND Hover：移入前/移入/离开 | false / true / false | false / true / false | 无 |
| 真实 HWND Hidden 标题可见 | false | false | 无 |
| Rename → 撤销 → 真实 Store 重载 | 原配置指纹 | 原配置指纹 | 无 |
| Lock → 撤销 → 真实 Store 重载 | 原配置指纹 | 原配置指纹 | 无 |
| Collapse → 撤销 → 真实 Store 重载 | 原配置指纹 | 原配置指纹 | 无 |
| Appearance + 两项标题策略 → 撤销 → 重载 | 原配置指纹 | 原配置指纹 | 无 |
| 真实 Store 独占写租约失败 | 自动补偿并恢复原配置 | `Compensated`，重试后原配置 | 无 |
| 生命周期删除选择 | 请求进入 App | 请求进入 App | 无 |
| 真实文件操作 | 0 | 0 | 无 |

结构化原生 HWND 和 Store 测试输出均记录 `Expected / Actual / Difference=None`。

## 4. 差异与修正记录

| 轮次 | 预期 | 实际差异 | 修正 |
| --- | --- | --- | --- |
| PF-004D 复审 | Enabled 删除可执行 | 生命周期遗漏 Delete 分支 | 补齐 availability 路由并增加完整请求链回归 |
| 首轮全量测试 | 既有 UIA 文本通过 | 新策略事实使 3 个旧精确字符串不同 | 更新精确期望，未删除策略事实 |
| 首轮真实撤销测试 | 编辑保存后还能在同一 save controller 提交撤销 | `CompleteAsync` 是关闭排空，后续提交按设计拒绝 | 改为编辑与撤销进入同一真实控制器队列，再排空并重载验证最终磁盘状态 |
| 首轮 UI 合同 | 155-ID 合同通过 | 新选择器使既有静态代码跨度超过 2400 字符 | 仅把同一语义搜索上限调到 4000；功能断言未放宽 |
| Live UIA | 运行跨进程 UIA | 本机 Windows App Runtime 2.4 / WinUI 3.2 命中已审计 fail-fast 组合 | 遵循门禁使用 `-ContractOnly`；不伪报 Live UIA 通过 |

## 5. 门禁结果

- 全解决方案测试：`1102/1102`；
- 全解决方案构建：`0 warning / 0 error`；
- UI 静态合同：`155` 个 AutomationId，`Outcome=Pass`；
- 真实原生 HWND 标题显示策略：`Difference=None`；
- 真实 Store 四类编辑/撤销/重载：`Difference=None`；
- 真实写租约失败补偿：`Difference=None`；
- Live 跨进程 UIA：因已知上游 fail-fast 组合被安全门禁阻止，保持 Pending；
- 未执行键鼠注入、Explorer Hook 或真实桌面文件写操作。

## 6. 需求对齐与下一步

PF-004A～E 的正式工程链已经覆盖标题事实、直接折叠/锁定、原生更多菜单、安全删除确认、标题策略、失败补偿和统一最近撤销，因此 PF-004 可转为 `EngineeringComplete / ProductEvidencePending`。仍缺真实物理鼠标菜单/双击、触控目标截图和 Narrator 顺序证据，故不得标记 `Complete`；30 个顶层 PF 仍为 `0 Complete`。

下一工程切片按 Stage 153 进入 **PF-005：正式项目图标、缩略图与有限状态**。优先复用隔离 ThumbnailWorker 和现有安全身份，不在 PF-005 扩展任务栏、小组件、插件或窗口特效；每个缩略图状态必须有真实文件/真实 worker 的 Expected、Actual 和差异修正证据。
