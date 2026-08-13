# Stage 103：Long方格后续产品开发详细执行计划

日期：2026-08-12

基线：`main` / `043c7fb`（PR #153 已合入且 main CI 通过）

计划范围：从当前“开发期控制中心 + 正式配置工作区 + 只读桌面目录”推进到可供普通用户试用的桌面管理 MVP，并为任务栏、小组件和 Long助手插件兼容保留独立后续阶段。

## 1. 当前阶段判定

Long方格当前位于 **Phase 0 / 内部 RC 收尾与 Phase 1 产品接线之间**。

已经具备：

- WinUI 3 控制中心、Design Token、深浅主题、响应式布局与品牌 RC1；
- 一键启动、一键便携包、unsigned MSIX、SBOM 和内部 RC 构建链；
- 142-ID UIA 源码合同、单实例、关闭排空和配置恢复；
- 正式方格配置 CRUD、锁定/折叠、有限外观与布局预设；
- 只读用户桌面/Public Desktop 第一层目录；
- 引用加入、移除、批量改归属、搜索、筛选、排序和一次撤销；
- DesktopHost、显示拓扑、窗口批处理、输入关闭与恢复的底层工程合同；
- 连续保存、latest-document 准入、原子配置发布与失败恢复。

尚未形成的关键产品闭环是：**正式方格还没有作为稳定、可直接操作的桌面可见层接入 App**。桌面文件移动、自动整理、任务栏美化、Widget Host 和 Long助手插件运行时仍保持关闭。

## 2. 总体目标与完成定义

### 2.1 第一目标：桌面管理 MVP

用户可以在 Windows 桌面看到自己的方格，完成创建、定位、拖动、缩放、折叠、锁定、加入引用、移除引用、重启恢复和异常恢复。默认只管理 Long方格引用，不擅自移动真实桌面文件。

MVP 完成必须同时满足：

1. 至少一个正式方格真实显示在桌面层，而不只是控制中心列表；
2. 方格与控制中心读取同一份正式配置，不存在双写状态；
3. 鼠标、键盘、触控和辅助技术都可完成关键操作；
4. 重启、Explorer 重启、显示器变化和异常关闭后可以恢复；
5. 任意失败不会留下透明输入拦截层、错误 Z-order 或无法退出的宿主；
6. 默认不移动、重命名或删除真实桌面文件；
7. PRD 10 个 MVP 场景、24 小时稳定性和 500 项性能预算通过。

### 2.2 第二目标：竞品体验追平

在 MVP 稳定后，补齐 iTop/Fences 的低摩擦体验：桌面直接创建、拖放反馈、卷起、Peek、规则建议、外观预设、冲突预览和即时撤销。

### 2.3 第三目标：Long生态扩展

任务栏外观、LongBar、Widget Host 和 Long助手插件兼容必须作为独立可卸载模块推进，不得拖慢或扩大桌面管理核心进程权限。

## 3. 阶段 A：真实 DesktopHost 产品接线

优先级：P0

目标：把正式工作区配置投射为真实桌面方格窗口，形成第一个“看得见、重启能恢复”的产品闭环。

### 3.1 开发内容

- 在 App composition root 中创建 DesktopHost 生命周期所有者；
- 使用已选定的“每显示器 HWND + 显式交互 Region”模型；
- 把正式容器 ID、配置 revision、显示拓扑 generation 和 registry generation 绑定为不可混用的渲染 generation；
- 将容器名称、折叠状态、有限外观、DIP placement 和已解析引用投影到 DesktopHost；
- 建立 App→DesktopHost 的最小命令合同，以及 DesktopHost→App 的有限状态回报；
- HWND、原始路径、进程句柄和线程对象不得进入 WinUI presentation；
- 关闭、会话切换、显示变化和宿主失败时有界排空并释放所有原生资源；
- Feature Flag 默认仅在开发/内部 RC 开启，正式发布前由证据决定默认值。

### 3.2 可见交付

- 桌面显示一个或多个半透明方格；
- 方格标题、折叠、锁定、颜色和透明度与控制中心一致；
- 方格不会出现在 Alt+Tab 和普通任务栏按钮中；
- Win+D、显示桌面、全屏应用和普通窗口切换行为可预测；
- 控制中心关闭不导致桌面宿主遗留。

### 3.3 验收门槛

- DesktopHost registry 只包含本产品创建的窗口；
- 不使用 `Progman`/`WorkerW` 未文档化嵌入；
- Bounds、Region、Composition 和 UIA 在同一 generation 复读一致；
- 失败补偿后要么完整恢复，要么关闭输入并隐藏整个受影响宿主；
- 主/副显示器、100%/150%/200% DPI、负坐标和任务栏不同位置通过；
- 空闲 CPU、内存、GDI/USER handle 有基线且无持续增长。

### 3.4 建议 PR 切片

1. DesktopHost composition root 与 Feature Flag；
2. 单显示器只读方格渲染；
3. 多容器/多显示器 generation 投影；
4. 关闭、故障补偿和资源审计；
5. UIA 与真实会话矩阵。

## 4. 阶段 B：桌面直接交互与方格编辑

优先级：P0

目标：让高频操作发生在桌面，而不是要求用户持续返回控制中心。

### 4.1 开发内容

- 方格标题栏拖动、边缘缩放、折叠/展开和单向快速锁定；
- 框选、单选、Ctrl/Shift 多选和键盘方向导航；
- 标准右键菜单：打开、移除引用、查看详情、管理方格；
- 明确区分“从方格移除引用”与“删除真实文件”；
- 拖放命中预览，显示目标方格、动作类型和拒绝原因；
- 第一阶段只允许安全引用拖放；真实移动/复制保持不可达；
- Peek 快捷访问模式和 Esc/再次快捷键退出；
- Reduced Motion 下取消非必要位移和渐变动画。

### 4.2 交互状态机

`Passive → Selecting → Editing/Dragging → Preview → Commit/Cancel → Passive`

每次状态切换必须：

- 保留稳定焦点；
- 不抢占前台应用；
- 给鼠标、键盘和 Narrator 相同的动作语义；
- 在提交前说明“引用、配置改归属、复制或真实移动”；
- 取消后不产生 revision 和保存任务。

### 4.3 验收门槛

- 鼠标、键盘、触控、Narrator、高对比和 200% 文本缩放通过；
- 拖放离开目标、按 Esc、显示 generation 改变时安全取消；
- 任何陈旧 token、重复目标、锁定目标或未解析引用默认拒绝；
- 交互过程中不读取文件内容，不调用删除入口；
- 真实桌面文件 SHA-256/时间戳/位置在安全引用流程前后不变。

## 5. 阶段 C：自动整理建议、预览与撤销

优先级：P1（桌面直接交互稳定后进入）

目标：学习 iTop 的“一键整理”低门槛，但用可解释、可预览和可撤销机制降低误操作风险。

### 5.1 第一批规则

- 文件类型/扩展名分类；
- 名称前缀和受限通配符；
- 当前桌面来源；
- 创建/修改时间区间；
- 未分组项目建议。

不在首批规则中使用：文件内容、云端模型、窗口标题历史、用户行为遥测或未经授权的目录递归扫描。

### 5.2 必须具备的流程

1. 扫描只读候选；
2. 展示规则命中原因；
3. 展示加入、跳过、冲突和无法处理数量；
4. 默认进入预览，不自动提交；
5. 用户确认后生成单个有界事务；
6. 成功后签发一次性撤销令牌；
7. 失败时保留原配置并提供有限错误。

### 5.3 验收门槛

- 相同输入和规则产生确定性结果；
- 规则冲突有稳定优先级，不按执行时序碰运气；
- 500 项模拟满足性能预算；
- 预览和取消为零写入；
- 默认仍只改变 Long方格配置引用；
- 若以后开放真实移动，必须另写威胁模型、IFileOperation 事务和恢复审计。

## 6. 阶段 D：布局快照与窗口工作空间产品化

优先级：P1

目标：将已完成的拓扑、窗口批处理和复合事务工程转为用户可理解的布局恢复与项目工作空间。

### 6.1 开发内容

- 手动保存桌面方格布局快照；
- 显示器拓扑变化后的差异预览；
- 恢复前逐项显示目标位置、被跳过项和风险；
- 恢复中逐项状态和取消；
- 恢复失败后的逆序补偿和活动中心记录；
- 工作空间第二阶段才加入应用启动、启动参数和普通窗口恢复；
- 管理员窗口、跨完整性级别窗口和第三方窗口默认不控制。

### 6.2 验收门槛

- 配置、窗口、registry 和显示拓扑 generation 全部一致才提交；
- 不激活窗口、不改变 Z-order、不抢焦点；
- 部分失败不能显示“恢复完成”；
- 显示器拔插、旋转、睡眠、RDP 和 Explorer 重启矩阵通过；
- 一次撤销或恢复失败必须收敛为有限可操作状态。

## 7. 阶段 E：视觉、动效、性能和可用性精修

优先级：贯穿 A–D，MVP 冻结前集中收口

### 7.1 视觉与动效

- 对齐 iTop 的低门槛卡片式预设和 Fences 的桌面融合感；
- 方格材质使用自有窗口的 DWM/Composition 能力；
- 统一 4/8 px 间距网格、圆角、阴影、色彩和层级；
- 动效以 120–220 ms 为主，必须可降级并尊重 Reduced Motion；
- 不用动画掩盖非原子窗口跳动或迟到保存。

### 7.2 性能预算

- 记录冷启动、首屏、桌面宿主出现时间；
- 记录空闲 CPU、工作集、Private Bytes、线程、GDI/USER handle；
- 500 项搜索、筛选、排序、拖放预览和规则模拟基准；
- 缩略图继续在隔离 worker 中执行并保持有界缓存；
- 不启用的任务栏、Widget、插件模块不得进入核心常驻路径。

### 7.3 人工证据

- 完成 #19 输入/系统表面矩阵；
- 完成 #20 动态显示硬件矩阵；
- 完成 #23 五人首次整理可用性测试；
- 完成 #24 专用卷持久化矩阵；
- 完成 BSA-01–BSA-05；
- 执行 24 小时稳定性和崩溃恢复测试。

## 8. 阶段 F：安装、签名与内部 MVP 发布

优先级：P1

### 8.1 开发内容

- 确定正式 Publisher、证书、包身份和版本策略；
- 在受保护 Release environment 中签名；
- 安装、升级、降级、修复、卸载和用户数据保留矩阵；
- 便携包与 MSIX 的边界说明；
- 崩溃诊断必须显式授权、脱敏并可关闭；
- 完成 LICENSE、第三方通知、隐私说明和用户帮助。

### 8.2 发布门槛

- main 与 release tag 对应同一审计提交；
- 签名、哈希、SBOM 和构建清单一致；
- 无已知数据丢失、误移动或高危漏洞；
- 干净 Windows 11 x64 设备完成安装和卸载；
- 未满足门槛时只能称为 Internal Preview，不得公开分发。

## 9. MVP 后独立阶段：任务栏、LongBar、小组件与插件

### 9.1 任务栏外观实验

- 第一版仅透明、着色、Blur/Acrylic 预设和最大化窗口时自动恢复实色；
- 独立 helper、独立开关、崩溃/退出/卸载自动恢复系统默认；
- 不注入 Explorer，不承诺完整任务栏换肤；
- Windows build、主副任务栏、DPI、自动隐藏和 Explorer 重启矩阵必须通过。

### 9.2 LongBar

- 使用 Long方格自有 WinUI/Composition AppBar/Dock 窗口；
- 与系统任务栏并存，不复制系统托盘、通知中心和输入法职责；
- 展示固定工作空间、常用应用和可选小组件入口。

### 9.3 Widget Host 与 Long助手插件

- 实现 LPWP 1.0 manifest/schema 校验；
- `.lpak` 签名、来源、版本和权限审计；
- 每插件/每 Widget 实例生命周期、崩溃隔离和资源配额；
- 网络、文件、剪贴板、通知、窗口和命令能力默认关闭、逐项授权；
- Long助手插件只有声明 Widget Surface 且通过兼容校验后才能作为桌面小组件；
- 插件失败不得拖垮 DesktopHost 或阻止 Long方格启动。

这些能力不进入当前桌面管理 MVP 的发布阻断项。

## 10. 推荐执行顺序与阶段出口

| 顺序 | 阶段 | 用户可见结果 | 出口条件 |
|---|---|---|---|
| 1 | A：DesktopHost 接线 | 桌面真正出现方格 | 单/多显示器、关闭和资源矩阵通过 |
| 2 | B：桌面直接交互 | 可拖动、缩放、折叠和安全拖放引用 | 输入、UIA、取消和陈旧状态矩阵通过 |
| 3 | C：规则建议 | 一键扫描、解释、预览、确认和撤销 | 500 项预算、冲突和零写入预览通过 |
| 4 | D：布局/工作空间 | 保存、预览和恢复布局 | 动态显示与复合补偿矩阵通过 |
| 5 | E：体验/性能收口 | 现代、平滑、稳定且可访问 | 24 小时、BSA、#19/#20/#23/#24 通过 |
| 6 | F：内部 MVP 发布 | 可签名安装和卸载 | 发布、许可证和安装矩阵通过 |
| 7 | MVP 后生态 | 任务栏、LongBar、Widget/插件 | 独立 PRD、权限和兼容矩阵通过 |

## 11. 每一步固定审计与推送流程

每个切片必须执行：

1. 从最新 `main` 建立 `codex/<slice>` 分支；
2. 在代码前写明需求、权限和失败边界；
3. 先完成纯策略/状态机，再接 Infrastructure，最后接 UI；
4. 增加确定性测试，不使用毫秒竞速碰运气；
5. 执行定向测试、Release 全量、覆盖率、格式和相关 UI/安全合同；
6. 新增阶段审计并同步路线图、开发状态和 README；
7. 显式检查 diff，只提交本切片文件；
8. 推送草稿 PR，等待完整 PR CI；
9. 合并后等待同提交 main CI；
10. main 绿灯后才把阶段标记为完成。

## 12. 下一切片建议

> 2026-08-13 / Stage 117 更新：B6b「产品 Hidden/Passive Surface 生命周期」已完成代码切片。双 opt-in 路径先创建隐藏、空 Region 的产品自有 HWND，待所有窗口完成 registry 所有权注册并形成 Host/UIA/workspace/topology/registry 证明后，才通过产品 adapter 恢复 Region 并发布 Passive。暂停、证明失败、拓扑替换、紧急禁用与 shutdown 都先隐藏再 detach/注销/销毁；adapter 固定拒绝 Explicit 与陈旧 generation。Host-only 只读预览语义不变，正式 `WM_NCHITTEST` 继续 `HTTRANSPARENT`，文件/任务栏/插件权限仍为零。下一切片 B6c 只建立产品意图转接、失焦/系统表面事件来源与 Explicit 前人工会话门禁，不直接开放真实文件操作。详见 [Stage 117 审计](117-product-hidden-passive-surface-lifecycle-audit.md)。

> 2026-08-13 / Stage 116 更新：B6a「受控开发态交互 Composition Root 基础」已完成代码切片。正式 App 现在同时评估 DesktopHost 与 Interaction 两个精确 opt-in，并让精确 emergency-disable 拥有最高优先级；双开关成立后只创建 Passive 开发控制器。失焦、Win+D、全屏、会话、RDP、Explorer 与 shutdown 统一进入 fail-closed 隐藏要求，只有完整 Host/UIA/Passive/generation 证明才可恢复；运行时紧急禁用不可逆。该切片刻意没有显式交互入口、没有构造产品 Surface adapter、没有改变正式 HWND 的 `HTTRANSPARENT`，也没有真实文件操作。下一切片 B6b 将在相同门禁后接入仅支持 Hidden/Passive 的产品 adapter 生命周期，先证明创建、暂停、恢复、紧急隐藏和销毁，再单独审计 Explicit 输入。详见 [Stage 116 审计](116-controlled-development-interaction-composition-root-audit.md)。

> 2026-08-13 / Stage 115 更新：B5「受控真实 HWND 交互适配器探针」已完成。一个默认隐藏、匿名且仅由探针拥有的 HWND 实现 B4 adapter，真实复核 ToolWindow/NoActivate、非 Topmost、无 Owner、前台不变、Window Region、`WM_NCHITTEST`、`WM_MOUSEACTIVATE` 与 HWND UIA Selection；成功往返、Apply 后失败、证据复核失败、恢复失败隐藏、隐藏失败报告及 generation 漂移全部通过。UIA 首次测量后进程驻留 1 个 GDI 对象和 2 个句柄，随后三个完整创建/查询/销毁周期严格保持平台期。正式 App、正式 read-only HWND、Explorer 和桌面文件仍未接线。下一切片为 B6「受控开发 opt-in composition root 与双重关闭开关」，先建立默认 Passive、超时/崩溃/会话恢复和紧急关闭路径，不开放真实文件操作。详见 [Stage 115 审计](115-native-interaction-surface-adapter-probe-audit.md)。

> 2026-08-12 / Stage 114 更新：B4「隔离交互 surface probe 与输入模式切换事务」已完成。协调器在内存匿名项目上原子复核 Passive、Explicit 和 Hidden 合同，绑定 B1 lease、workspace/topology/window-registry generation，并把 B3 selection/UIA snapshot 接入同一事务；窗口策略强制 ToolWindow、NoActivate、非 Topmost、无 Owner、不拥有前台。Apply/验证失败恢复精确 Passive，恢复失败则隐藏，隐藏失败单独报告，不能冒充安全状态。正式 App、正式 read-only HWND、真实文件和 Explorer 对象仍未接线。下一切片为 B5「受控真实 HWND 交互适配器探针」，只在探针自有隐藏窗口中验证样式/Region/消息/UIA/恢复，不开放真实桌面文件操作。详见 [Stage 114 审计](114-desktop-interaction-surface-mode-transaction-audit.md)。

> 2026-08-12 / Stage 113 更新：B3「选择模型、焦点语义与隔离 UIA Selection 合同」已完成。单方格最多 256 个匿名 ID 支持普通/Ctrl/Shift/Ctrl+Shift、Previous/Next/Home/End、focus-only 导航和稳定 anchor；状态严格绑定 lease、三类 generation、expiry 与可见 ID 顺序。Passive UIA 仍无 Selection 且不可聚焦，Explicit 隔离合同才映射 Select/Add/Remove。正式 UIA provider、HWND、App 和文件系统未接线。下一切片为 B4「隔离交互 surface probe 与输入模式切换事务」。详见 [Stage 113 审计](113-desktop-interaction-selection-focus-uia-contract-audit.md)。

> 2026-08-12 / Stage 112 更新：B2「产品命中区域与焦点/取消适配器」隔离切片已完成。命中复用正式 SurfaceLayout，区分 header/content/visible item，对重叠目标拒绝猜测 Z-order；三种明确激活生成固定最长 5 秒、绑定三类 generation 的 B1 意图；Esc、失焦、Win+D、全屏、会话/RDP、Explorer 和 shutdown 统一取消，evidence/timer 继续走 B1 复核。正式 HWND 保持 `HTTRANSPARENT`，App 与文件系统未接线。下一切片为 B3「选择模型、焦点语义与只读 UIA Selection 合同」，先完成纯状态机和隔离 probe。详见 [Stage 112 审计](112-desktop-interaction-hit-test-cancellation-audit.md)。

> 2026-08-12 / Stage 111 更新：B1「桌面交互准入与模式状态机」工程切片已完成。交互拥有独立精确 opt-in，显式意图最长 5 秒，并绑定 workspace revision、topology generation、window registry generation、只读 UIA/被动窗口证明与单一未锁定目标；任一证据变化均回到 Passive。该状态机仍与 App、真实 HWND 输入及文件系统隔离。下一切片为 B2「产品命中区域与焦点/取消适配器」，复用统一 SurfaceLayout，先完成 hit-test、Esc/失焦/Win+D/拓扑变化取消和隔离 probe，不开放真实文件移动、复制或删除。详见 [Stage 111 审计](111-desktop-interaction-admission-state-machine-audit.md)。

Stage 110 已完成 **A5 自动化子切片：DesktopHost 只读 UIA 与产品会话合同**。正式 HWND 通过 `WM_GETOBJECT` 公开 Root→方格→当前可见项目的只读 Fragment，视觉、Region 与 UIA 共用布局计算；节点不可聚焦且不提供 Selection/Invoke。生命周期还强制复读 ToolWindow/NoActivate/非 Topmost/无 Owner/未占前台，并新增 A5-01..A5-06 受控会话启动器和手册。

A5 真实 Narrator、Win+D、全屏、Explorer 重启、锁定/RDP 与 24 小时资源证据继续保持 PendingManualEvidence，阶段 A 不能标记最终验收。下一代码切片是 **B1：桌面交互准入与模式状态机**，先建立与只读模式隔离的默认关闭策略、命中/焦点/取消和陈旧 generation 拒绝，不直接开放真实文件、拖放、任务栏或插件权限。A5 自动化证据与人工限制见[Stage 110 审计](110-desktop-host-readonly-uia-session-contract-audit.md)。
