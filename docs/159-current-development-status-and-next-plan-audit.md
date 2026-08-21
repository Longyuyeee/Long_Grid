# Stage 159：当前开发状态、计划对齐与后续验收审计

- 审计日期：2026-08-16
- 审计基线：`main@1f2294de974261f93dd07eb39183da0e87aa94fe`
- 远端状态：本地 `main` 与 `origin/main` 一致，无开放 PR
- 最近主分支 CI：run `31951221413`，`success`
- 权威功能计划：[Stage 153](153-product-feature-parity-development-plan.md)
- 本轮性质：只读代码/计划/远端事实审计与后续开发排程，不把未执行的人工场景记为通过
- 总结论：**产品功能方向未偏移；PF-001、PF-002 均仍为 `InProgress`，严格下一开发项是 PF-002D**

## 1. 执行摘要

Stage 153 把开发主线从“继续增加环境验证”纠正为“逐项交付对标软件的用户功能”。从该基线到本次审计，仓库连续合并了 PF-001、PF-002A、PF-002B、PF-002C1 和 PF-002C2 五个功能实现 PR；桌面方格已从默认关闭的只读工程宿主，推进到默认启用、可在空/非空工作区通过产品自有按钮、右键菜单、`Ctrl+Alt+N` 和 UIA 持续创建方格。

但是，当前不能描述为“桌面方格核心闭环已经完成”：

1. PF-001 的用户开关、关闭释放和恢复主链已完成，但 App 启动仍直接激活控制中心，桌面优先呈现尚未收口；
2. PF-002 当前输入会直接提交默认创建，没有提交前就地预览与命名；
3. 接受创建后会立即把新文档应用到工作区投影，而保存由异步控制器处理，尚缺“保存成功后确认发布 / 保存失败撤回可见结果”的补偿事务；
4. 桌面拖画矩形创建、使用 Long方格已选引用创建、物理连续 20 次和完整无障碍/DPI 证据仍未完成；
5. PF-003 及之后的拖动、缩放、项目图标、打开、拖放、规则、Portal、Tab、快照、工作空间等对标能力尚未进入完整用户闭环。

因此，本轮判定为：**方向正确、工程基础扎实、首个用户闭环尚在收口，不能跳项。**

## 2. 审计方法与证据范围

### 2.1 已核查事实

- `git fetch --prune` 与 `git pull --ff-only` 确认本地和远端主分支相同；
- GitHub 当前无开放 PR；
- 开放 Issue 为 #19、#20、#23、#24，均属于人工矩阵、专用环境或产品决策证据，不等于四个新代码功能；
- Stage 153 至当前共 6 个合并提交（含计划文档），净变化为 47 个文件、约 4,607 行新增和 158 行删除；
- 当前仓库约 598 个受跟踪文件、19 个项目、142 个产品源码文件、85 个测试源码文件和 200 个 Markdown 文档；
- `TODO/FIXME/NotImplementedException` 未暴露新的静默占位实现；检出的 `PlatformNotSupportedException`/`NotSupportedException` 是 Windows 平台或输入失败关闭边界，不作为已交付功能；
- 主分支 CI `31951221413` 通过 972/972 测试，lines 90.45%、branches 77.30%，交付清单 800/800；
- 内部产物仍 `signed=false`、`installable=false`、`distributionApproved=false`。

### 2.2 本文不声称的证据

- CI 和 Core/Surface 合同不等于真实鼠标、触控、Narrator、文本缩放或 100%～400% DPI 人工通过；
- 环境预检为绿色不等于 24 小时长稳、真实卷耗尽或显示/会话矩阵已经执行；
- 产品自有 Surface 上的右键菜单不等于 Explorer 任意桌面空白区右键集成；
- 20 次 reducer/revision 合同不等于用户物理连续创建 20 次且焦点全程正确；
- unsigned MSIX 和便携 ZIP 通过清单审计不等于可安装或可分发版本。

## 3. 与 Stage 153 最初计划的偏移审计

| 对齐问题 | 最初计划 | 当前事实 | 判定 |
| --- | --- | --- | --- |
| 是否继续以用户功能为主线 | 从 PF-001 开始逐项对标 | 最近五个实现 PR 全部服务 PF-001/PF-002 | 对齐 |
| 是否把验证脚本当产品进度 | 验证只作为功能与发布证据 | 新增测试均锁定开关、创建、输入、生命周期和失败边界 | 对齐 |
| PF-001 是否完整结束再进入 PF-002 | 顺序表将 PF-001 列为第一项 | PF-001 主链完成，但桌面优先呈现未结束；为补空状态而进入 PF-002 | 受控重叠，不是方向偏移 |
| PF-002 是否按多入口创建推进 | 按钮/空态、右键、键盘、绘制矩形、选择引用、预览、事务 | A/B/C1/C2 已完成；预览、事务、矩形、选择引用未完成 | 部分对齐 |
| 是否可以开始 PF-003 | PF-002 验收关闭后进入拖动缩放 | PF-002 仍有明确阻断项 | **不可跳项** |
| 是否可以准备公开分发 | 功能与 G0 门禁同时满足后才允许 | 签名、安装、外部证据仍未满足 | **不可分发** |

PF-001 与 PF-002 的重叠是依赖驱动：空工作区若没有创建入口，默认启用只会得到空白桌面，因此先补 PF-002A 是合理的。但这种重叠不能继续扩大；PF-003 不应在 PF-002D/E 和剩余创建入口未形成闭环前启动。

## 4. 当前顶层完成度

Stage 153 共冻结 30 个顶层 `PF-*` 功能。按“正式 App/DesktopHost 可发现、可操作、可保存、可恢复，并满足该项验收”这一严格口径：

| 状态 | 数量 | 说明 |
| --- | ---: | --- |
| `Complete` | 0 | 当前没有任何顶层 PF 已满足全部用户功能与验收证据 |
| `InProgress` | 2 | PF-001、PF-002 |
| 尚未进入完整产品闭环 | 28 | 部分项目有 Core/探针/控制中心底座，但不等于桌面产品功能 |

该数字不是代码完成率，也不否定已有工程资产。PF-002 内已有 A/B/C1/C2 四个 Engineering Pass 切片；它说明创建主链已经成形，但顶层功能仍需 D/E、剩余入口和正式证据才能完成。

## 5. 已完成的用户可感知能力

### 5.1 PF-001 已完成部分

- 用户级“显示桌面方格”开关默认开启；
- 设置采用独立原子保存和备份，失败时回滚 UI 与生命周期；
- 关闭时释放 DesktopHost、输入 region、UIA 和快捷键，同时保留配置；
- 关闭期间缓存最新权威布局，重新开启后恢复；
- 紧急禁用高于用户设置，不允许设置绕过安全门；
- 空工作区已由 PF-002 提供可理解的首建入口。

### 5.2 PF-002 已完成部分

- 空工作区主显示器显示“创建第一个方格”；
- 非空工作区在每个权威显示器寻找不覆盖现有方格的安全空位显示“新建方格”；
- 主点击、产品自有右键菜单、主显示器 `Ctrl+Alt+N` 和标准 UIA Invoke 进入同一种请求；
- 请求携带显示器、workspace revision、topology generation、来源证明、注入和自动重复事实；
- App 在入队前和执行时二次检查陈旧 revision、拓扑、显示器和生命周期；
- 默认名称稳定生成“新方格”“新方格 2”……，显式名称对空白、超长、控制字符和重名失败关闭；
- 默认位置/尺寸按显示器 work area 和 DPI 有限级联，避免完全重叠；
- Explicit 选择期间关闭创建入口、UIA 导航和快捷键，返回 Passive 后恢复；
- Surface 更新后旧 revision 请求失效，旧窗口和快捷键被释放。

## 6. 当前关键缺口与根因

### 6.1 P0：PF-002D 提交前预览与命名不存在

`RequestDesktopWorkspaceCreate` 当前在二次 admission 后直接调用 `CommitProductWorkspaceContainerActionCore(Create, ...)`，传入空名称和 `useDefaultName=true`。Surface 只提交输入事实，没有独立的 Preview Session、可编辑名称、确认或取消状态。

用户影响：

- 用户无法在桌面确认名称、显示器、位置和尺寸；
- 非法名称只能由控制中心路径验证，桌面入口没有就地错误与修正；
- Esc、失焦、第二个请求、显示变化等事件没有一个可取消的“创建中”对象；
- 多输入虽然结果一致，但一致的是“立即创建”，尚未达到竞品的就地创建体验。

### 6.2 P0：PF-002E 保存与可见发布未形成补偿事务

容器编辑被 coordinator 接受后，App 立即 `ApplyAcceptedProductWorkspaceDocument`，从而刷新正式工作区与 DesktopHost；实际配置保存由带 debounce/retry 的异步 `ProductWorkspaceSaveController` 执行。当前保存失败能显示状态并允许重试，但没有为新创建方格建立发布令牌、旧投影快照和自动撤回路径。

用户影响：

- 保存失败时本次进程内可能已经看到新方格；
- 重启后该方格可能不存在，形成“幽灵方格”体验；
- 重试、后续编辑和新 revision 之间缺少创建级结果归属；
- PF-002 的“保存失败不留下幽灵方格”验收不能通过。

### 6.3 P0：PF-002 剩余创建入口未完成

- 桌面按住指针绘制矩形创建尚未实现；
- “使用当前 Long方格已选引用创建”尚未实现；
- 当前右键只发生在产品自有创建入口，不是 Explorer 任意空白区集成；
- 不应为追求表面一致而安装 Shell Extension、全局鼠标 hook 或接管 Explorer 选择，除非后续单独完成安全和维护成本决策。

### 6.4 P1：PF-001 桌面优先启动未收口

`App.OnLaunched` 仍直接执行 `window.Activate()`。在没有托盘或其他稳定恢复入口前，贸然隐藏控制中心会造成用户无法找回设置。因此需要先定义可靠恢复入口，再决定首次启动、日常启动和显式“打开设置”激活的差异。

### 6.5 P1：日常桌面使用闭环仍与竞品差距很大

- PF-003：方格不能直接拖动、缩放和吸附；
- PF-004：正式桌面标题栏缺少重命名、锁定、折叠、外观和删除就近操作；
- PF-005：正式项目图标/缩略图 UI 未消费既有隔离 worker；
- PF-006：正式项目选择底座存在，但项目打开、完整键盘导航和物理输入闭环未完成；
- PF-007：Explorer 拖入和方格间安全改归属未进入正式桌面闭环；
- PF-008～PF-010：视图、滚动、桌面搜索、统一撤销/重做尚未达到竞品日用水平。

### 6.6 P2：竞品宽度能力基本未进入实现

规则与自动整理、Roll-up/Quick-hide/Peek、托盘与全局入口、Folder Portal、Tab、命名快照、多显示器场景、应用工作空间、完整外观、Private Box、Widgets 和安装更新仍位于后续 PF 阶段。现阶段不应并行铺开这些能力，否则会稀释桌面核心闭环。

## 7. 严格后续开发顺序

### Step 1：PF-002D——提交前就地预览与命名

#### D1. 创建预览领域状态

- 定义唯一 `ProductDesktopWorkspaceCreatePreviewSession`，包含 session ID、来源、目标显示器、初始 workspace revision、topology generation、默认名称、候选 bounds 和状态；
- 状态至少包含 `Inactive`、`Editing`、`Submitting`、`Rejected`、`Cancelled`；
- 同一时刻全工作区最多一个预览；新请求替换旧请求前必须先取消旧 session；
- Preview 不修改配置、不推进 edit revision、不建立正式容器 ID。

验收：纯状态测试覆盖所有合法/非法迁移，重复取消幂等，陈旧 session 不能提交。

#### D2. DesktopHost 就地预览表面

- 在候选位置显示方格轮廓、名称编辑框、显示器/尺寸有限摘要、确认与取消操作；
- 空态与非空态、按钮/右键/键盘/UIA 都打开同一种预览；
- 默认焦点落在名称编辑框并选中可编辑文本；
- UIA 暴露 Name、AutomationId、Edit/Invoke、HelpText 和确定导航顺序；
- 预览以产品自有 Surface 实现，不抢占预览区域之外的桌面输入。

验收：不同入口产生等价 session；键盘 Enter/UIA 确认与按钮确认结果相同；预览之外仍 `HTTRANSPARENT`。

#### D3. 名称与候选布局反馈

- 实时复用 Stage 156 名称策略，不复制校验规则；
- 空白、超长、控制字符和重名在提交前显示有限错误；
- 名称错误时确认按钮禁用，错误通过 HelpText 提供但不逐键 live 播报；
- 显示器、位置和尺寸来自权威候选，不允许 UI 任意构造越界 bounds。

验收：Unicode、空白、边界长度、控制字符、重名和自动编号形成确定矩阵。

#### D4. 取消与失效

- Esc、显式取消、失焦、进入 Explicit/Hidden、总开关关闭、安全门关闭、App 退出均取消；
- workspace revision 或 topology generation 改变、目标显示器消失/失去权威性时取消；
- 取消后零配置提交、零正式窗口、零快捷键/region/UIA 残留；
- 取消原因只显示有限状态，不泄露路径、句柄或桌面内容。

验收：每一种失效都有状态、Surface、UIA 和零副作用合同；旧 session 回调不能复活。

#### D5. PF-002D 完成门

- 聚焦测试、全量测试、Release 构建、format、UI 合同和 `git diff --check` 全绿；
- 文档记录对标行为、失败矩阵、自动化证据与未覆盖物理证据；
- PF-002 继续 `InProgress`，不得因预览完成提前升级。

### Step 2：PF-002E——保存与可见发布补偿事务

#### E1. 两阶段结果模型

- 创建确认先生成待发布事务，保存成功后才升级为 `Published`；
- 事务绑定 create session、旧/新 edit revision、容器 ID、目标显示器和保存 generation；
- 后续无关保存不能错误确认或撤回本次创建；
- 同时只允许一个创建发布事务，或证明并发顺序可确定。

#### E2. 保存失败补偿

- 保存失败时撤回新容器投影和 DesktopHost 窗口，恢复提交前权威文档；
- 显示有限“未保存，已撤回”与重试动作；
- 重试必须重新校验 revision、拓扑、容量、名称和目标显示器；
- App 关闭 drain 超时、外部配置变化和新提交竞争时失败关闭，不保留半确认结果。

#### E3. 验收目标

- 保存成功：配置、App read model、DesktopHost Surface 和 UIA 同时收敛到新方格；
- 保存失败：四者都回到旧状态，重启后不出现新方格；
- worker/显示器/Surface 发布失败不会损坏已保存配置，并给出确定恢复路径；
- 自动化覆盖 debounce、retry、乱序 completion、关闭 drain、陈旧回调和连续创建；
- PF-002 的幽灵方格阻断项标记工程通过。

### Step 3：PF-002G——有限桌面拖画矩形创建

- 只在经过 admission 的产品交互模式捕获手势，不安装全局 hook；
- pointer down/move/up 只更新内存预览，提交前复用 PF-002D；
- 最小/最大尺寸、工作区裁剪、DPI、负坐标和跨显示器行为确定；
- Esc、捕获丢失、revision/topology 变化和安全门关闭零残留；
- 触控与鼠标使用同一几何结果，真实文件和 Explorer 图标不被移动。

完成门：矩形创建与按钮/右键/键盘共享预览、名称和 E 事务，不建立第二套创建实现。

### Step 4：PF-002H——使用 Long方格已选引用创建

- 只消费 Long方格自己的显式选择快照，不读取或接管 Explorer 原生选择；
- 预览显示有限项目数量，不泄露完整路径；
- 选择、revision、拓扑或目录身份变化时取消；
- 创建成功后引用归属与新容器在同一配置事务提交；
- 保存失败同时撤回容器与引用改归属，不移动真实文件。

完成门：0、1、256、257 项边界、重复引用、锁定来源、取消、保存失败和一次撤销均有合同。

### Step 5：PF-002F——正式证据收口

- 在可用的无个人内容 Windows 测试环境执行鼠标、键盘、触控和 UIA；
- 连续创建 20 个方格，核对唯一 ID、名称、焦点、位置、保存和资源终态；
- 覆盖 Narrator、高对比、文本缩放和 100%/150%/200%/300%/400% DPI；
- 覆盖快捷键冲突、Win+D、Alt+Tab、全屏、Explorer 重启、显示器变化和 App 退出；
- 没有可安排环境时保持 `PendingManualEvidence`，不得用预检替代结果。

PF-002 只有在 D/E/G/H 的工程门和 F 的产品证据达到已批准完成口径后，才允许从 `InProgress` 升级。

### Step 6：PF-001 桌面优先呈现收口

- 明确首次启动、日常启动、外部激活和用户主动打开设置四种入口；
- 提供稳定可发现的设置恢复入口后，日常启动不强制抢前台显示控制中心；
- DesktopHost 失败或安全禁用时自动退回可理解的控制中心状态；
- 验证单实例激活、关闭/重启、Narrator、启动时序和 1 秒呈现目标。

### Step 7：进入 PF-003～PF-010 桌面日常核心闭环

严格顺序为：

1. PF-003 拖动、缩放与吸附；
2. PF-004 标题栏与就近操作；
3. PF-005 项目图标、缩略图与状态；
4. PF-006 选择、键盘导航与打开；
5. PF-007 Explorer 拖入与方格间安全拖放；
6. PF-008 视图、排序、滚动与间距；
7. PF-009 桌面搜索、筛选与定位；
8. PF-010 统一撤销、重做与历史。

每个 PF 必须完成正式桌面可发现入口、唯一状态/提交链、失败补偿、自动化、审计文档和远端主分支 CI，才进入下一项。规则、Portal、Tab、快照、工作空间和外观宽度能力在核心闭环稳定后再排入。

## 8. 每一步统一审计与推送门

每个切片按以下顺序执行：

1. 从最新绿色 `main` 建立 `codex/<pf-id>-<scope>` 分支；
2. 在编码前记录对标行为、Long方格安全边界和本切片不做的能力；
3. 实现最小用户闭环，不新增只为通过测试的旁路；
4. 先跑聚焦测试，再跑 Release build、全量测试、format、UI 合同和差异检查；
5. 更新 Stage 153 状态、路线图、README 和该切片审计文档；
6. 只暂存本切片文件，形成语义清晰的提交并推送独立分支；
7. 创建 PR，等待 PR CI 全绿，复核提交 SHA 与检查 SHA 一致；
8. squash 合并后拉取 `main`，等待主分支 CI 再次全绿；
9. 确认本地 `HEAD == origin/main`、工作区干净且无遗留开放 PR；
10. 若任何门失败，记录真实失败并修复，禁止把 Pending 或预检写成 Pass。

## 9. 下一次开发的直接输入

下一次开发不需要重新选择方向，直接领取 **PF-002D**：

> 把当前“输入通过 admission 后立即创建”改为“输入统一打开一个可取消、可编辑、绑定 revision/topology/display 的就地预览；用户明确确认后才进入唯一创建提交器”。

PF-002D 的最小交付不是视觉稿，也不是新增验证脚本，而是可从当前四种入口真实打开、编辑、确认和取消的产品预览闭环。PF-002E 在其后接管保存与可见发布一致性。

## 10. 发布与外部证据边界

开发可以继续推进安全范围内的 PF 功能，但以下条件继续阻止 RC/公开分发：

- Issue #19：真实输入、系统表面与无障碍矩阵；
- Issue #20：动态显示、会话、RDP 与 DPI 矩阵；
- Issue #23：五人可用性测试与产品/渠道决策；
- Issue #24：专用真实卷耗尽/只读边界；
- M4c2c：正式 App/worker 24 小时资源证据；
- 经批准的发布证书、可安装签名包和最终分发授权。

这些门禁不应抢占 PF-002D/E 的产品开发主线，也不能被静默关闭；它们在具备合规环境时独立执行并如实记录。

## 11. 2026-08-20 增量复审

从最新 `main@b5af34c4` 建立的 PF-002D1 分支，已把 DesktopHost 请求后的“立即默认创建”改为唯一 Preview Session、实时名称校验、明确确认/取消和 submit 前 revision/topology/display/host 二次复核。正式 App 使用可访问的 WinUI 对话框承载该闭环，确认前不提交配置、不建立正式容器且不操作桌面文件。

严格结论没有升级为 PF-002D 完成：当前预览会激活控制中心，并非 Stage 159 D2 要求的候选 DesktopHost 区域原生就地表面；Windows 自动化窗口枚举连续超时，正式 App 虽真实启动但没有取得打开—编辑—取消的可复核实机证据；全量测试实际为 `982/983`，既有 activation UIA Invoke 的 `ElementNotEnabledException` 独立重跑仍失败。详细预期—实际差异、零副作用矩阵和后续门禁见 [Stage 160](160-pf002d1-editable-create-preview-audit.md)。

因此下一顺序细化为：先恢复全量测试和真实预览证据，再完成 PF-002D2 原生就地表面，然后进入 PF-002E。PF-002、PF-001 和顶层 `0/30 Complete` 口径保持不变。

## 12. 2026-08-20 PF-002D2 增量复审

Stage 161 已恢复 UIA 无前台激活与键盘代理受拒后的 Passive，Release 全量回到 983/983。Stage 162 随后增加候选 DIP 到目标显示器绝对像素的有限映射，并让正式 App 优先创建位于候选方格位置的现代 WinUI 原生编辑窗口；窗口支持名称即时校验、Enter/Escape、失焦取消、六个动态 UIA 标识和控制中心安全回退。新增几何测试后全量为 989/989，Release 构建和正式 App 8 秒启动通过。

严格口径仍不变化：实机控制器在窗口状态抓取时检测到最小化/并发用户输入，按安全流程停止，未取得原生窗口打开—编辑—取消—确认证据。因此 PF-002D2a 只记 Engineering Pass，PF-002D、PF-002、PF-001 和顶层 `0/30 Complete` 均保持不变。下一门禁先补完整 App 真实交互，再进入 PF-002E 保存与可见发布补偿，详见 [Stage 162](162-pf002d2-native-inline-preview-audit.md)。

## 13. 2026-08-20 PF-002E 增量复审

Stage 164 已把桌面创建的可见结果绑定到创建容器 ID、工作区修订和保存修订。真实文件系统故障注入先确认旧链在保存失败后仍保留 1 个内存方格，再确认新链只在同次保存失败且没有后续编辑时经唯一提交协调器撤回；解除故障并重试后，真实磁盘重载为 0 个方格。若修订、保存代次或容器事实已变化，补偿判定为 `Superseded`，不会覆盖后续用户编辑。

PF-002E 记为 Engineering Complete；PF-002D 的真实打开—编辑—取消—确认证据仍 Pending，PF-002/PF-001 和顶层完成口径不变。下一产品切片为 PF-002 的桌面拖画矩形创建，随后是使用已选引用创建和 PF-002F 正式物理/无障碍证据。

## 14. 2026-08-20 拖画矩形增量复审

Stage 165 已让 `PointerDrag` 请求携带绝对像素矩形，真实 Win32 Surface 在 Explicit 模式使用 capture、move、button-up 和 capture-cancel 生命周期绘制内存 outline；App 在同一 admission、Preview、提交和 PF-002E 补偿链中保留该矩形。显示器工作区、负坐标、DPI、最小尺寸和越界均由正式创建策略验证。真实 Win32 Surface 没有取得前台，真实配置文件重载保留精确 DIP 几何。

本切片记 Engineering Pass，不记物理交互 Pass：当前会话没有执行真实鼠标 down/move/up 到正式 App 的全链，因此 PF-002 继续 `InProgress`。下一开发项为“使用 Long方格已选引用创建”；物理拖画与 PF-002D 预览输入矩阵在合规 Windows 会话并行补证。

## 15. 2026-08-20 PF-002H 原子事务基础增量复审

Stage 166 已把“建新方格”和“已选引用从来源改归属到新方格”收敛为一个 reducer 状态转换、一个配置投影和一次保存提交。请求最多 256 项，只接受非空、唯一、存在且已解析的 Long方格引用；来源锁定、陈旧修订或任一无效项均整批拒绝，零保存、零修订推进。该链不读取 Explorer 选择，也不移动真实文件。

真实临时目录中创建的两个文本文件经正式配置保存、重载和一次撤销后内容保持不变；实际独占 `.lock` 文件令保存进入 `Failed/WriteLeaseUnavailable`，磁盘重载仍为旧状态，解除租约后完整状态恢复成功。首次静态合同因旧的全协调器提交次数硬编码真实失败，已改为检查新方法范围内恰好一次提交并重新通过。全量测试为 1005/1005。

严格结论是 PF-002H 的**原子事务基础通过**，不是用户闭环完成。下一切片须从正式 Long方格选择捕获带 revision/topology/source/fingerprint 的有限快照，复用唯一 Preview Session，并将完整恢复令牌接入 PF-002E 同次保存失败自动补偿；同时补 256/257、选择变化、取消、连续编辑和 UIA/Narrator。PF-002H、PF-002、PF-001 与顶层完成口径均保持不变，详见 [Stage 166](166-pf002h-selected-reference-atomic-transaction-audit.md)。

## 16. 2026-08-20 PF-002H 正式 App 接线增量复审

Stage 167 已增加正式“使用选择创建新方格”入口，并把同一方格 1–256 项选择快照接入已有 admission、候选显示器、原生/fallback Preview、原子提交、PF-002E 发布事务和最近撤销。快照绑定配置指纹，选择、revision 或 topology 变化均取消；保存失败使用完整旧状态恢复令牌，不能只删除新方格。最近撤销也已从错误的“撤销批量加入”区分为“撤销使用选择创建方格”。

真实测试创建 257 个文本文件：257 项请求拒绝后磁盘仍为旧 257 项，随后 256 项请求真实保存并重载为来源 1 项、新方格 256 项，所有文件内容逐项不变。Release 全量为 1010/1010，147-ID 静态合同通过。首次合同因旧的 live-region 全文件次数硬编码失败，修成新入口专项合同后通过。

正式 App 结果必须分开记录：Windows 捕获获得完整控制中心截图，说明 Release 界面真实渲染；但 UIA 连续两次找不到首元素，整树读取导致 `Microsoft.UI.Xaml.dll` 崩溃。对 `fff20f2` 建立独立 worktree、重新构建并执行相同操作，得到相同截图成功和相同 UIA 崩溃，证明不是本切片回归。Stage 168 完成上游缺陷对齐和真实窗口生命周期测试；Stage 169 又证明当前控制器的无文本截图路径也触发同一 fail-fast，并把已知 `2.4.0.0 + 3.2.3.0` 组合改为 App 启动前失败关闭。因此 PF-002H 仍为 `EngineeringComplete / ProductEvidencePending`；下一步以专用临时配置的进程内 UI evidence session 补正式 App 接线证据，上游修复后再补物理输入/UIA/Narrator，详见 [Stage 169](169-winui-uia-fail-closed-preflight-audit.md)。

## 17. 2026-08-21 PF-002 正式 App 进程内证据增量复审

Stage 170 新增默认关闭、GUID 限定、专用临时配置且拒绝重解析点的正式 App evidence session。真实 Release App 先加载 MainWindow/XAML、正式配置、显示拓扑和 DesktopHost，再在 UI 线程驱动取消与确认，通过正式保存控制器写入并由正式 store 重载。连续两次实际均为：初始/取消 `0 + Missing`，确认 `1 + PF-002 证据方格`，保存 `Completed`，重载 `1 + LoadedPrimary`；桌面元数据、用户配置元数据与临时清理均无差异。

真实失败不能被忽略：当前 `WindowsAppRuntime 2.4.0.0 + Microsoft.UI.Xaml.dll 3.2.3.0` 下，第二顶层窗口、ContentDialog、可见持久 Preview 面板以及确认后的动态可访问列表都能触发同一 WinUI fail-fast。产品因此对该精确组合选择主窗口持久 Preview；证据进程在 XAML readiness 后隐藏窗口，并明确输出 `PreviewActivatedCount=0`、`VisibleInteractionStatus/VisibleViewPublication=BlockedByKnownUpstream`。这证明正式 App 的不可见 UI 线程接线、提交、保存和重载，不证明可见点击、UIA 或 Narrator。PF-002 保持 `EngineeringComplete / ProductEvidencePending`；下一门禁为上游修复/独立机器的可见与物理矩阵，以及最近撤销的正式 App 证据。详见 [Stage 170](170-pf002-formal-app-inprocess-evidence-audit.md)。

## 18. 2026-08-21 PF-002 正式 App 最近撤销证据增量复审

Stage 171 在同一正式 App evidence session 中补齐最近撤销工程证据。普通创建没有通用撤销令牌，因此没有伪造“撤销创建”；真实链先让创建保存修订 1 落盘，再通过 App 正式容器提交委托删除并让保存修订 2 落盘，随后由主窗口统一最近撤销选择器认定唯一种类为 `ContainerRemoval`，执行与真实按钮相同的分派与正式恢复委托，最终 `CompleteAsync` 排空保存并从正式 store 重载为原方格。

两轮最终实际均为创建 `1/LoadedPrimary`、删除 `0/LoadedPrimary`、最近撤销选择与执行 `ContainerRemoval`、恢复 `1/LoadedPrimary`，名称仍为“PF-002 证据方格”；外部脚本对每一字段独立复核，桌面与用户配置元数据不变、临时目录删除、进程退出 0。开发中先真实发现工作区编辑修订与保存修订被混用，再发现 Windows PowerShell 5.1 对无 BOM UTF-8 中文常量解码不一致；分别改为读取保存控制器权威目标修订和以 Unicode 码点构造期望名称后重跑通过。

此证据关闭“最近撤销的正式 App 工程证据”缺口，但仍是当前已知不安全 WinUI 组合下隐藏窗口的进程内 UI 线程驱动，不等同于可见按钮点击、物理输入、UIA 或 Narrator。PF-002 继续 `EngineeringComplete / ProductEvidencePending`；下一主门禁仍是上游修复或独立安全机器上的可见 Preview/发布、鼠标/键盘/触控和无障碍矩阵。详见 [Stage 171](171-pf002-formal-app-latest-undo-evidence-audit.md)。

## 19. 2026-08-21 PF-003A 布局预览与吸附策略增量复审

官方 WinUI 上游问题 #11139 仍为 Open/Backlog，且明确说明跨进程 UIA fail-fast 不能由应用代码捕获或规避；本机框架依赖 App 虽引用 Windows App SDK 2.3.1，实际选择已安装 Runtime 2.4.0.0。强制 UIA、删除辅助语义或猜测回退运行时都不能成为真实修正。PF-002 因此保持 `EngineeringComplete / ProductEvidencePending`，同时按本文“外部证据不阻断安全功能编码”的规则进入 PF-003A。

PF-003A 新增正式 Core 的移动/四边四角缩放预览策略。每次请求绑定容器 ID、edit revision、topology generation 和显示器；锁定、陈旧、显示身份不唯一、跨显示器未准入、非有限或极端 delta 均零预览失败关闭。策略只计算 DIP 内存候选，支持 8 DIP 网格、工作区和同显示器方格边缘吸附，Shift 对默认吸附开关取反，最终候选约束在 DPI 换算后的工作区并保留最小 160×120 DIP。

真实规模预检在正式 100 方格状态上预热 100 次后执行 2,000 次生产预览，首轮 P95 为 `0.067 ms`，预期 `<16.7 ms`，差异为无；工具同时证明不读取真实桌面、无真实文件操作且临时保存沙箱已清理。该结果只关闭 Core 计算和性能门，不证明视觉延迟、物理鼠标、UIA Bounds、一次保存或失败补偿。下一切片固定为 PF-003B 手势会话和唯一结束提交，详见 [Stage 172](172-pf003a-layout-preview-snap-policy-audit.md)。

## 20. 2026-08-21 PF-003B 手势会话与唯一提交增量复审

Stage 173 已把 Stage 172 的无状态预览收敛为一次 begin/update/cancel/complete 会话。begin 冻结容器、原 placement、edit revision、topology generation 和 display；update 使用累计 delta 且不接触保存；陈旧事实自动取消并恢复；complete 再验证后只产生一个内部构造完成凭据。现有统一提交协调器检查当前原 placement（含扩展字段）未变，通过既有 reducer/projection/save controller 提交，并以权威 edit revision 阻止凭据重复消费。

真实临时配置测试执行 1,000 次 update，实际保存 revision=0、配置 `Missing`；complete 首次接受后保存 revision=1，重复提交返回 `StaleEditRevision`，最终重载 X/Y 误差均为 0 DIP，桌面哨兵文件内容不变。陈旧 update、显式/重复取消、并发 placement 改变和 complete 后 topology 改变均返回有限状态并零保存。首次扩展字段合同编译暴露 `IDictionary`/`IReadOnlyDictionary` 类型差异；末轮审计又补齐 complete—commit 间的 topology 竞争窗口，修正后聚焦 24/24 通过。

该结果关闭 PF-003B 工程门，不代表正式桌面可操作。PF-003 保持 `InProgress`；下一切片固定为 PF-003C 保存失败补偿，随后才接 DesktopHost 标题栏移动、八向缩放命中和键盘微调。跨显示器、视觉/物理输入和 UIA Bounds 仍为后续独立证据，详见 [Stage 173](173-pf003b-gesture-session-single-commit-audit.md)。
