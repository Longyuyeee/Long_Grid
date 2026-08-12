# Stage 113：桌面交互选择、焦点与 UIA Selection 合同审计

日期：2026-08-12
阶段：B3（纯状态机与隔离辅助功能合同完成，正式 DesktopHost 仍不可交互）

## 1. 阶段目标

Stage 111/112 建立了交互 lease、共享几何命中和统一取消语义。Stage 113 补齐真正接线前的选择语义：

- 单选、Ctrl 切换、Shift 连续区间、Ctrl+Shift 合并区间；
- Previous/Next/Home/End 导航以及 Ctrl 仅移动焦点；
- 选择、键盘焦点和 range anchor 相互分离；
- 每个状态实例绑定同一个 B1 lease、三类 generation、目标方格和有序可见 ID 集；
- Passive UIA 保持无 Selection pattern、不可聚焦；Explicit 隔离合同才允许多选和 SelectionItem 动作映射；
- 不修改正式 UIA provider，不接入 App、HWND 或文件系统。

## 2. 需求对齐

| 原始/竞品需求 | Stage 113 对齐 | 当前边界 |
|---|---|---|
| 框选、单选、Ctrl/Shift 多选 | 完成确定性单项与连续区间选择语义 | 鼠标框选几何和正式输入接线尚未开放 |
| 键盘方向导航 | Previous/Next/Home/End，边界钳制不循环 | PageUp/PageDown 与二维空间导航留待后续 |
| 焦点清楚且不抢前台 | 焦点与选中分离；Ctrl+方向只移动焦点 | 正式 DesktopHost 继续不可聚焦 |
| Narrator 与鼠标语义一致 | UIA Select/Add/Remove 映射到同一选择请求 | 只完成隔离合同，尚未发布真实 provider |
| 显示器/Explorer 变化安全 | lease 和三类 generation 全绑定；可见 ID 顺序变化拒绝 | 真实事件源仍未接入 |
| 锁定/文件安全 | 状态机只处理匿名 item ID，不含路径、名称和文件动作 | 不读取内容，不移动、复制或删除文件 |

任务栏美化、普通窗口特效、Widget 与 Long 助手插件兼容不属于 B3 权限范围，仍按后续独立阶段推进。

## 3. 选择状态机审计

### 3.1 有界且匿名的模型

单个控制器只服务一个交互 lease 和一个方格，最多接受 256 个非空、Ordinal 唯一的可见 item ID。它不接受显示名、路径、PIDL、Shell identity 或文件内容。创建时复制 ID 顺序，调用方后续修改输入集合不会影响内部状态。

每个快照记录 lease intent ID、container ID、workspace revision、topology generation、window registry generation、有序可见/选中 ID、focused ID、anchor ID 和单调 `SelectionRevision`。

### 3.2 操作语义

- 普通 Select：仅选目标，focus/anchor 都设为目标；
- Ctrl+Select：保留其他项并切换目标，focus/anchor 更新到目标；
- Shift+Select：以稳定 anchor 替换为连续区间；没有 anchor 时使用旧 focus，否则使用目标；
- Ctrl+Shift+Select：把 anchor 到目标区间加入现有集合；
- 普通导航：移动 focus，并用目标替换选择/anchor；
- Ctrl+导航：只移动 focus，不改变选择和 anchor；
- Shift 导航：从稳定 anchor 扩展连续选择；
- Clear：清空选择、focus 和 anchor；
- 边界导航采用钳制，不从末尾循环到开头，避免桌面空间中的意外跳跃。

重复操作如果状态没有变化，不增加 `SelectionRevision`。

### 3.3 陈旧状态拒绝

每次操作必须重新提供完整 lease、有序可见 ID 和当前 UTC。intent、container、任一 generation 或 expiry 字段不同返回 `LeaseMismatch`；当前时间到达 expiry 返回 `LeaseExpired`；可见 ID 的内容或顺序变化返回 `VisibleItemsChanged`。拒绝结果不改变选中、焦点、anchor 或 revision。

## 4. UIA Selection 隔离合同

Passive 快照明确：

- `SelectionPatternAvailable=false`；
- `CanSelectMultiple=false`；
- 全部节点 `IsKeyboardFocusable=false`；
- 没有选中或焦点。

Explicit 快照才允许：

- Selection pattern；
- `CanSelectMultiple=true`、`IsSelectionRequired=false`；
- item 的 `IsSelected` 与 `HasKeyboardFocus` 分离；
- Select 映射普通选择；AddToSelection/RemoveFromSelection 映射 Ctrl 切换；已经满足的 Add/Remove 返回 `AlreadySatisfied`，不伪造其他键盘动作。

适配器拒绝 foreign selected/focus ID、重复 visible ID、空 identity、非正 generation，以及 `LeaseMismatch/Expired` 等拒绝快照。它只输出语义快照和状态机请求，不持有 HWND，也不调用 UIAutomation 原生 API。

## 5. 接线隔离

工程 UI 门禁证明：

- 状态机包含 256 上限、全部 lease/generation、expiry、visible order、anchor 和 Ctrl/Shift 语义；
- Passive UIA 明确 pattern-free/nonfocusable，Explicit 明确多选；
- `LongGrid.App` 没有引用 B3；
- 正式 `WindowsProductDesktopHostUiaProvider` 没有实现 `ISelectionProvider/ISelectionItemProvider`，仍是 Stage 110 的不可聚焦只读树；
- 正式 surface 仍由 Stage 112 门禁保持 `HTTRANSPARENT`。

因此 Stage 113 没有用户可见行为变化，也不会抢占前台或改变桌面文件。

## 6. 自动化证据与人工限制

新增确定性测试覆盖：模型上限/拷贝/唯一性、普通/Ctrl/Shift/Ctrl+Shift、focus-only 导航、anchor 稳定、clear、非法请求、六类 lease mismatch、expiry、visible reorder、revision no-op、Passive/Explicit UIA、一致性拒绝与 UIA Select/Add/Remove 映射。

这些测试不能替代真实鼠标框选、键盘、触摸、笔、Narrator、焦点环、高对比、200% 文本和 Reduced Motion 会话证据。A5、Issue #19/#20、BSA 等人工结果继续保持 Pending。

## 7. 下一切片

下一步建议为 **B4：隔离交互 surface probe 与输入模式切换事务**：

1. 构造仅使用内存匿名项目的 product-shaped probe；
2. 事务性验证 Passive `HTTRANSPARENT` → Explicit 可命中/可聚焦 → Cancel 后恢复 Passive；
3. 每次切换复读 NoActivate/非 Topmost/Owner/前台与窗口 registry generation；
4. 连接 B2 命中、B3 选择与 UIA Selection 语义，验证鼠标/键盘/Narrator 等价；
5. 异常、超时、Explorer/显示器/session 变化必须隐藏或恢复 Passive；
6. 仍不连接真实桌面文件和正式 App 默认路径。

B4 隔离证据稳定后，才评估受控正式 DesktopHost 输入接线；方格拖动/缩放、框选几何和安全引用拖放分别继续拆分，真实文件变更保持默认关闭。
