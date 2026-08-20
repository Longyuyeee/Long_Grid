# Stage 160：PF-002D1 可编辑创建预览工程审计

- 日期：2026-08-20
- 开发基线：`main@b5af34c41204b590fcc5c1acff1f0ad7fbcb5f11`
- 分支：`codex/pf002d-create-preview`
- 范围：PF-002D1——把桌面创建请求从“立即提交”改为“先预览、编辑、明确确认或取消”
- 结论：**PF-002D1 工程实现完成，实机交互证据阻断；PF-002D 与 PF-002 均保持 `InProgress`**

## 1. 开发前真实差异

| 检查项 | 预期行为 | 开发前实际行为 | 差异 |
| --- | --- | --- | --- |
| 创建入口 | 先显示名称、位置、尺寸，再由用户确认 | admission 通过后立即提交默认创建 | 不符合 |
| 名称 | 默认名称可编辑，非法名称就地修正 | 桌面入口固定使用默认名称 | 不符合 |
| 取消 | 取消、退出或状态变化均零提交 | 没有可取消的 Preview Session | 不符合 |
| 陈旧状态 | revision/topology/display/host 变化使预览失效 | 仅提交前 admission，没有预览期对象 | 部分符合 |
| 桌面文件 | 创建过程不读取、移动或修改桌面文件 | 已保持零文件操作 | 符合 |

本轮目标是先关闭“立即创建”缺口，同时保留唯一配置提交协调器，不建立第二套创建或保存路径。

## 2. 本轮实现

### 2.1 有限预览状态

新增 `ProductDesktopWorkspaceCreatePreviewSession`，快照固定包含 session ID、原创建请求、状态、有限失败原因、名称和权威候选位置。状态只允许 `Editing`、`Submitting`、`Rejected`、`Cancelled`；只有 `Editing + None + 有候选位置` 可以提交。

- 默认值仍由 Stage 156 的 `ProductWorkspaceContainerCreationDefaults` 产生；
- 名称编辑实时复用同一规则，不复制重名、长度、控制字符或容量判断；
- submit 前再次比较 workspace revision 和 topology generation；
- cancel 为终态且幂等，旧 session 不可复活；
- Preview 本身不修改配置、不推进 edit revision、不建立正式容器，也不接触桌面文件。

### 2.2 正式 App 预览链

所有 DesktopHost 创建输入仍先经过原有 admission，然后统一进入异步预览链：

`DesktopHost 输入 -> admission -> Preview Session -> 名称编辑/校验 -> 确认 -> 二次状态复核 -> 唯一 Commit Coordinator`

正式 WinUI `ContentDialog` 提供：

- 默认名称全选的名称编辑框；
- 目标显示器候选位置和 DIP 尺寸摘要；
- 非法名称的有限错误反馈和 disabled 确认按钮；
- “创建并保存”与“取消”两个明确结果；
- `DesktopWorkspaceCreatePreviewDialog`、`NameEditor`、`PlacementSummary`、`Validation` 动态 UIA 标识；
- 确认前明确声明配置与桌面文件均未改变。

新请求会替换旧预览；workspace revision、显示拓扑、目标显示器、DesktopHost 可用性、Explicit 交互、窗口关闭等变化会取消当前预览。确认后仍要重读权威 display/host/revision/topology，全部一致才调用原提交器，并显式使用用户确认后的名称。

## 3. 失败与零副作用矩阵

| 场景 | 状态/结果 | 配置/桌面文件副作用 |
| --- | --- | --- |
| 默认名称和候选位置有效 | `Editing`，允许确认 | 零 |
| 空白、过长、控制字符或重名 | 有限失败，确认禁用 | 零 |
| 容量满或无候选位置 | `Rejected` | 零 |
| 用户取消 | `Cancelled/UserCancelled` | 零 |
| 新请求到达 | 旧会话 `Cancelled/Replaced` | 零 |
| workspace revision 改变 | `Cancelled` 或 `Rejected/StaleWorkspace` | 零 |
| topology generation 改变 | `Cancelled` 或 `Rejected/StaleTopology` | 零 |
| 目标显示器或 DesktopHost 失效 | 有限取消/拒绝 | 零 |
| App 关闭 | `Cancelled/WindowClosing` | 零 |
| 二次复核通过且用户确认 | `Submitting` 后调用唯一提交器一次 | 只提交配置；仍不操作桌面文件 |

## 4. 真实测试：预期与实际

### 4.1 自动化与构建

| 验证 | 预期 | 实际 | 判定 |
| --- | --- | --- | --- |
| 新增预览状态测试 | 合法/非法转换、修正、陈旧提交、取消幂等全部通过 | `11/11` 通过 | 通过 |
| Release 解决方案构建 | 0 warning / 0 error | 0 warning / 0 error | 通过 |
| UI 源码合同 | 请求必须经过 Preview 再到 Commit，动态 UIA/有限状态存在 | `146` 个既有 XAML ID 与新增动态合同通过 | 通过 |
| 修改文件格式 | 无格式差异 | 针对本轮 C# 文件的 whitespace verify 通过 | 通过 |
| 全量测试 | `983/983` | `982/983`；既有原生 UIA activation Invoke 独立重跑仍抛出 `ElementNotEnabledException` | **未通过** |

全量失败位于 `WindowsProductDesktopHostUiaProviderTests.NativeActivationSourceExposesFiniteInvokeAndHideRestoreContract`，不是新增 Preview 测试，但仓库整体因此不得标记为全绿。该真实差异保留为合入前质量阻断，不能用新增测试通过数覆盖。

**2026-08-20 后续修正**：Stage 161 证明失败来自 UIA 激活被错误绑定到键盘代理前台切换，且前台切换失败后缺少 Passive 恢复。修正后 UIA 使用无前台依赖路径，键盘代理受拒时恢复 NoActivate/CanActivate；原失败聚焦测试和全量 `983/983` 均通过。原始 982/983 结果作为发现证据保留，当前质量状态以 [Stage 161](161-native-uia-activation-recovery-audit.md) 为准。

### 4.2 正式 App 实机交互

Release App 已真实启动成功；预期继续验证：从 DesktopHost 创建入口打开预览、默认名称选中、非法名称禁用确认、合法名称恢复、取消零提交。实际 Windows 自动化控制器连续三次在枚举窗口时超时，按工具安全流程停止，没有取得可复核窗口树、点击或输入证据。

因此当前只可以声明“正式 App 可启动、实现和合同存在”，**不可以声明真实打开—编辑—取消已经通过**。下一次可用会话必须补跑：

1. 从空态和非空态 DesktopHost 入口分别打开同一种预览；
2. 默认名称获得焦点并全选；
3. 空白/重名使确认禁用，合法 Unicode 名称恢复；
4. Cancel/Escape 后配置 revision、容器数、DesktopHost Surface 均不变；
5. 确认只创建一个方格；
6. 预览期间改变 revision/topology/host 后自动取消且旧回调不能提交。

## 5. 对标与范围审计

| 对标能力 | 当前结果 | 状态 |
| --- | --- | --- |
| 创建前命名和确认 | 正式 App 对话框已实现 | 工程部分对齐 |
| 非法名称即时修正 | 复用权威规则、禁用确认 | 工程对齐 |
| 取消零残留 | 有限 session 与 App 取消链 | 工程对齐，实机待证 |
| 桌面候选位置内原生就地编辑 | 当前会激活控制中心并显示 `ContentDialog` | **未对齐** |
| 预览区外保持桌面穿透 | 当前对话框不位于 DesktopHost Surface | **未验收** |
| 保存失败不留幽灵方格 | 尚未实现发布补偿事务 | PF-002E |

本轮没有安装 Explorer 扩展、全局 Hook 或输入模拟，没有读取、移动或隐藏 Windows 桌面文件。当前对话框是安全的工程中间态，不得描述成 iTop/Fences 级桌面原生就地创建体验。

## 6. 需求对齐结论

- 最初“桌面多入口创建、提交前命名/预览、零惊吓”方向没有偏移；
- 已把最危险的“输入即提交”改成明确确认，且所有输入仍汇入同一状态和提交链；
- PF-002D 只完成 D1 和控制中心承载的可编辑闭环，D2 DesktopHost 原生就地 Surface 与真实 UI 证据仍未完成；
- PF-002E 保存/可见发布补偿、拖画矩形、使用已选引用创建与 PF-002F 正式证据仍是 PF-002 的阻断项；
- PF-002 未完成前不进入 PF-003，PF-001 的桌面优先启动也仍需单独收口。

## 7. 严格下一步

1. **先修复或隔离并证明既有 UIA activation `ElementNotEnabledException` 的根因**，恢复全量测试全绿；
2. 在可用 Windows 自动化/干净人工会话补齐本页 6 项真实预览矩阵；
3. **PF-002D2**：把编辑框、轮廓、确认/取消和 UIA 放到候选 DesktopHost 区域，保持区域外 `HTTRANSPARENT`，控制中心只作为失败恢复入口；
4. **PF-002E**：建立保存成功后发布、失败时撤回配置/read model/DesktopHost/UIA 的补偿事务；
5. 再推进拖画矩形、使用 Long方格已选引用创建和 PF-002F 物理输入/无障碍/DPI 证据。

只有上述门禁产生真实通过证据后，才能把 PF-002D 或 PF-002 升级为完成。
