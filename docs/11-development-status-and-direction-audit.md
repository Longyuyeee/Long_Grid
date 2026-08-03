# Long Grid 当前开发状态与后续方向审计

审计日期：2026-08-03（增量复审）

审计基线：`main` / `853a606`（PR #73 已合入）+ Issue #19 人工矩阵会话工具分支

审计范围：代码、测试、技术探针、架构/产品/交互文档、GitHub PR 与 CI

结论：**Phase 0 收尾阶段 / 产品方向对齐，但执行顺序偏向技术轨，尚未达到 MVP 开工门槛**

## 1. 执行摘要

Long Grid 已经越过“空仓库”和“只写方案”的阶段，形成了可复现的 .NET 工程基线、纯 Core 决策逻辑、真实 Win32/Shell/DirectComposition/UI Automation 探针以及自动化测试。桌面发现、稳定身份、Shell 变化对账、图像提取、DesktopHost 窗口模型、显示拓扑、布局恢复、事务补偿、首个可交互宿主切片、配置持久化、文件操作安全和缩略图进程隔离均有代码与报告证据。PR #18、#25–#61、#63–#69 已合入 `main`；配置探针通过了 20 类场景、1,000 次重复写入、四检查点共 1,000 次真实进程强杀及 ACL 生效期间 10 次强杀；文件操作和缩略图隔离探针均保持 Conditional Pass，视觉品牌需求、图标 RC1 和只读 UI Shell 也已进入主线。

但这些成果大多仍是 **Conditional Pass 的风险探针**，不是可发布产品能力。仓库现已建立开发期只读 `LongGrid.App` UI Shell、Design Token、品牌 RC1 与一键启动链；该切片只显示匿名示例数据，不枚举真实桌面、不连接 DesktopHost、不执行文件操作、不写产品配置，也没有安装承诺，因此证据等级仍是 E2/E3 开发集成，而不是 E4 产品切片。`LongGrid.DesktopHost`、`LongGrid.Infrastructure`、产品配置 schema、安装包和端到端产品流程仍不存在。真实键鼠/触控/拖放、Narrator、Win+D、全屏、Explorer 重启、动态显示硬件矩阵、文件移动撤销、真实卷故障、真实 Provider 性能矩阵和可用性测试尚未关闭。

### 2026-08-03 增量结论

- Issue #19 已具备 I19-01–I19-10 的匿名单场景启动器、恢复纪律和 CI `PendingManualEvidence` 合同；该工具不合成输入、不改变系统设置、不重启 Explorer，也不替代真实人工结果；
- `LongGrid.App` 使用 .NET 8、Windows App SDK 2.3.1 Stable、WinUI 3 和 x64 开发目标；ADR-0001 继续保持 `Proposed`；
- 新 UI Shell 是为解除“只有正式 App 存在后才能验证启动、主题和关闭”的循环门槛而建立的受控开发切片，不代表跳过 Issue #19–#24；
- `eng/Start-LongGrid.ps1` 默认只执行依赖恢复、构建和开发启动，不提权、不扫描桌面；CI 使用 `-ValidateOnly` 验证启动链而不打开窗口；
- 本机 Release 构建和窗口启动/存活/正常关闭通过；进程句柄 UIA 冒烟已验证导航发现、键盘焦点、选择、内存态深浅主题往返和安全页可见性；
- 当前分支新增 760 DIP 断点、紧凑纵向流与按 XAML RasterizationScale/当前工作区计算的默认窗口；真实 UIA 完成宽→720 px 紧凑→宽往返和三张状态卡几何复读；
- 当前切片把三张状态卡从 XAML 常量改为 `LongGrid.Core.Runtime.RuntimeStatusSnapshot` 单向驱动；快照只表达开发期只读、桌面目录未连接、文件操作被安全策略关闭和 DesktopHost 未连接，不含用户数据或执行能力；
- 当前首次整理原型提供一键建议/从空白开始、安全引用/真实移动和预览后果；真实移动只能进入阻断预览，所有状态仅驻留内存并通过 UIA 复读；
- 当前分支新增单个匿名容器创建、40 字符名称边界、可见预览、立即撤销和 `Ctrl+Z` 合同；它不建立 Core 实体、不持久化，也不触碰桌面文件；
- 当前分支继续加入三个匿名引用、添加引用/改变归属/移动阻断三种动作徽标语义和最近动作优先的两步撤销；结构门禁明确禁止把该练习伪装成真实 Explorer Drop Target；
- 当前分支新增独立恢复预览页，复用 Core `Automatic/ReviewRequired/Blocked` 词汇并覆盖过期与取消；它不读取显示拓扑、不调用规划器/事务协调器，也不移动窗口；
- 自动截图工具仍因错误归属未打包窗口而无法取得稳定句柄，因此视觉截图、高对比、Narrator、文本缩放和 DPI 人工矩阵继续保持 `Inconclusive/Pending`；
- 详细范围、供应链、验证和停止规则见 [`17-ui-shell-readonly-slice-audit.md`](17-ui-shell-readonly-slice-audit.md)与[`18-ui-theme-automation-contract-audit.md`](18-ui-theme-automation-contract-audit.md)。

当前最优先问题不是继续堆功能，而是：

1. 先按 Issue #23 恢复初始计划要求的体验轨和负责人决策，再推进 #19–#20 人工/硬件矩阵；
2. 仅按已批准支持范围收口 #21–#22，不再机会主义扩展已经充分举证的探针族；
3. 按 #24 确认生产合同，把只有 App/DesktopHost 存在后才能完成的接线验证移到首片验收；
4. 更新 ADR-0001 为 Accepted、Revised 或 Rejected；
5. 只在上述门禁通过后，建立第一个生产化、只读且不移动文件的 MVP 垂直切片。

与 2026-07-30 初始计划的逐项比较、偏移证据和纠偏规则见[初始计划对齐与偏移审计](13-original-plan-alignment-audit.md)。

任务栏美化、LongBar、小组件和 Long助手插件兼容继续保留为实验/后续能力，不应抢占核心桌面整理的可靠性闭环。

## 2. 审计方法与证据等级

本次审计不按文档篇幅或功能名称计算完成度，而按证据等级判断：

| 等级 | 定义 | 当前可否视为产品完成 |
|---|---|---|
| E0 方案 | PRD、交互、架构或协议已定义 | 否 |
| E1 纯逻辑 | Core 实现并有单元测试 | 否，只证明决策逻辑 |
| E2 隔离探针 | 在自有 HWND、临时目录或当前机器验证真实 API | 否，只能给 Conditional Pass |
| E3 受控矩阵 | 在明确系统/硬件矩阵完成正常、失败和恢复验证 | 可支持技术决策，仍非完整产品 |
| E4 产品切片 | 正式模块、持久化、诊断、Feature Flag、端到端验收齐全 | 可作为 MVP 增量 |
| E5 发布证据 | 安装/升级/回滚、性能、稳定性、安全和可用性门禁通过 | 可进入对应发布渠道 |

当前仓库主体位于 E1-E2；少量工程基线达到 E3 的自动化部分；尚无 E4 产品切片。

## 3. 当前工程事实

### 3.1 已建立的基线

| 领域 | 当前事实 | 判定 |
|---|---|---|
| 工程 | .NET 8 SDK 锁定、集中包版本、nullable、警告即错误、确定性构建 | 已建立 |
| 解决方案 | `main` 有 1 个 Core、1 个测试和 9 个探针项目 | 已建立，但不是产品分层 |
| 代码 | 当前分支有 20 个 Core、13 个测试和 55 个探针 C# 源文件 | 以风险验证为主；本切片新增 3 个 Core 状态合同文件和 1 个测试文件 |
| 测试 | 当前全量 90 项测试通过 | Core 回归基线有效；新增只读状态快照安全形状与值等价测试 |
| CI | 单一 Windows workflow 执行 restore、format、build、启动/UI 结构合同、test、覆盖率门禁、配置/文件安全/缩略图 worker 探针、依赖漏洞门禁并上传 TRX/Cobertura | PR 基线有效；行覆盖率最低 90%、分支覆盖率最低 75%，尚无 CodeQL 或发布流水线 |
| 文档 | PRD、架构、质量、竞品、交互、协议、流程、ADR、22 份 Spike 报告 | 覆盖较完整 |
| GitHub | `main` 已合入至 PR #69；当前首次整理原型使用短生命周期分支；Phase 0 Exit 里程碑跟踪 #19–#24 六项工作 | 治理基线已闭环；当前切片仍须 PR/CI |
| 主干保护 | `main` 要求严格的 `build-test`，对管理员生效，禁止强推和删除 | 已建立最小可信门禁 |
| 许可证 | GitHub 未识别许可证，仓库根目录无 LICENSE | 阻断公开分发与外部贡献 |
| 覆盖率 | 90 项 Core 测试通过；本地 Cobertura 为行 91.43%（2412/2638）、分支 77.25%（584/756），待 PR CI 复核 | Phase 1 关键 Core 分支目标 ≥80%，当前先跟踪趋势、不用总百分比替代风险测试 |
| 发布状态 | 公开仓库无 tag、release、安装包和 LICENSE；六个打开 Issue 已纳入 Phase 0 Exit | 不可公开分发，剩余需求已有正式队列 |

数量是 2026-08-02 审计快照，不作为长期质量指标。

### 3.2 已有代码证据

| 能力 | 证据 | 等级 | 仍缺什么 |
|---|---|---|---|
| 桌面目录与 Shell Namespace | 用户/Public 目录、Shell Namespace 枚举和差异对账 | E2 / Conditional Pass | 重定向、OneDrive、离线/权限矩阵 |
| 稳定身份 | 文件对象身份、快捷方式双重身份、重命名跟踪 | E1-E2 | 跨卷、云占位符和长时间运行 |
| Shell 变化 | 通知合并、恢复与最终全量对账 | E1-E2 | Explorer 重启和高频真实桌面压力 |
| 图标/缩略图 | 异步加载、取消、句柄闭环；真实零 Capability AppContainer worker、协议 v6 的受控副本/最小路径 ACL 对照、八样本父进程基线与双 build 十六组合、脱敏 handler 与 HEVC/AV1 decoder 健康、共享内存像素、未代理读写阻断、正常 ACL 恢复、硬超时/父退出/Profile 清理；合成 500 项预算 | E2 / Conditional Pass | 正式渲染集成、HEIC/AVIF 成功环境、Office/PDF、云/网络、受控第三方 provider/ARM64 矩阵、干净 TIFF 环境、异常 ACL 修复、安全确认和最终预算批准 |
| DesktopHost 规划 | 每显示器 HWND、显式 Region、被动显示、输入门 | E1-E2 | 系统表面和真实输入人工矩阵 |
| DComp/UIA | Root 提交、Fragment 树、Selection/Invoke Pattern 和事件 | E2 / Conditional Pass | Narrator、高对比、缩放和最终渲染栈 |
| 显示恢复 | 拓扑指纹、CCD 映射、稳定采样、恢复计划 | E1-E2 | 真实旋转、拔插、投影、睡眠和 RDP |
| 事务补偿 | Bounds/Region/DComp/UIA 快照、代次门禁、逆序回滚、紧急隐藏 | E1-E2 | 正式宿主集成和故障注入矩阵 |
| 配置持久化 | 版本化 JSON、原子替换/备份/安全模式、四检查点共 1,000 次及 ACL 生效期间强杀、跨进程单写租约、有界退避、具备入队快照/有界排空的 latest-wins 保存协调、确定性迁移回滚、只读/磁盘满/权限恢复 | E2 / Conditional Pass | Phase 0 仍缺真实卷空间耗尽/只读、替换内部失败、跨进程公平性和正式 schema；真实应用关闭/完整单实例激活列入首片验收 |
| 交互切片 | 一个可见 List 容器和三个进程内演示项 | E2 / Conditional Pass | 文件语义、拖放、正式持久化和用户测试 |
| 正式 App UI 壳层 | WinUI 导航、Design Token、内存态主题、AutomationId/访问键、760 DIP 响应式流、DPI 感知窗口、Core 只读状态、首次整理、匿名容器/项目、拖放语义、两步撤销和恢复差异三态/过期/取消的真实 UIA 冒烟 | E2 / Conditional Pass | 5 人测试、真实拖放与硬件恢复、负责人决策、经批准的真实只读数据适配器、Narrator/高对比/系统文本缩放/多 DPI 视觉矩阵 |

### 3.3 仍只是设计或协议的能力

- 设置中心、托盘、开机启动和正式 DesktopHost 进程；
- 生产容器 CRUD、布局持久化、快照、跨动作撤销和活动中心；当前只有单个匿名内存练习，不计入生产能力；
- 规则引擎执行、模拟、冲突处理和真实文件操作；
- 任务栏 ThemeCoordinator、实验 Helper 和 LongBar；
- Widget Host、插件进程、权限执行端和 LPWP 互操作；
- MSIX、升级/降级/卸载、签名、Nightly 和诊断包。

这些能力的文档较完整，但不能标记为“已开发”。

## 4. 关键审计发现

### 4.1 GitHub 治理基线已闭环

PR #18、#25–#61、#63–#69 已进入 `main`，当前主干基线为合并 PR #69 后的 `0d793f6`。Core 只读状态、覆盖率、配置、文件安全、缩略图 worker、UI 自动化合同和依赖漏洞门禁已在 PR #69 CI `30790480972` 一次通过。PR #2–#17 已关闭；当前新切片建立前远端仅保留 `main`。

`main` 已要求严格的 `build-test` 状态检查，规则对管理员生效，同时禁止强推和删除。Phase 0 Exit 里程碑以 #19–#24 跟踪人工输入与系统表面、动态显示、文件安全、缩略图隔离与 500 项预算、产品决策和持久化生产边界。W0 因此关闭；这只证明主干治理可信，不代表产品已达到发布条件。

### 4.2 “探针已通过”与“产品已实现”边界总体正确，入口状态已重新对齐

路线图和 Spike 报告普遍保留了 Conditional Pass，ADR-0001 也仍为 Proposed，这是正确的。本轮以最新 `main` 修正 README、路线图、开发流程和本审计中的配置完成度与治理计数。仍需持续注意：

- 原始仓库审计仍以“空仓库 / Greenfield”为主结论，只适合作为历史基线；
- 任何入口状态都必须以最新 `main` 的代码、测试、报告和 CI 为共同证据；
- 探针的 Conditional Pass 不得因进入主干自动升级为产品完成；
- 后续每个风险切片继续从最新 `main` 建立短生命周期分支，并以 Issue、报告和 CI 共同回写状态。

### 4.3 技术轨领先，体验轨和产品决策明显落后

Windows 技术探针已经深入到事务补偿和 UIA Provider，但以下体验证据尚无：

- 真实拖入容器、规则模拟和真实硬件恢复；首次整理、匿名添加引用、拖放语义、两步撤销及恢复差异心智模型已有自动化原型；
- 用户能否准确区分“添加引用”和“移动文件”；
- 至少 5 人无提示任务测试；
- 触控、Narrator、键盘焦点顺序和高对比人工检查；
- 文件冲突、失败、部分成功和撤销边界的用户理解。

继续加深底层窗口机制的边际收益已经下降。下一轮必须恢复 Phase 0 双轨平衡。

### 4.4 三项负责人决策仍阻断正式 MVP

1. 许可证与商业/分发模式；
2. 最低 Windows build、首发 CPU 架构和 MSIX/离线渠道；
3. 首版整理模式：仅安全引用，还是同时提供托管目录；Folder Portal 是否进入 MVP。

这些不是工程团队可用默认值代替的技术细节。未确认前可继续只读和隔离验证，但不得承诺公开分发或真实文件整理范围。

### 4.5 正式产品架构尚未落地

当前只有 `LongGrid.Core` 和探针。架构文档规划的 App、DesktopHost、Infrastructure、Contracts 与集成/UI 测试尚未创建。过早复制探针代码会把测试假设、演示状态和原生资源所有权带入生产。

生产化时必须：

- 从合同和状态机开始，而不是把单个探针改名为产品；
- 把 Win32/COM 资源所有权集中到 Infrastructure/DesktopHost；
- 让 Core 保持无 UI、无 Shell 依赖；
- 把演示数据、故障注入和人工探针入口与发布二进制隔离；
- 配置、日志、Feature Flag 和回滚路径与第一片功能一起交付。

### 4.6 实际 worker 已降权，但受限 token 已被排除为独立文件保密边界

PR #35 已把缩略图 worker 从“只返回状态/尺寸”推进到协议 v2 的有界 BGRA32 像素负载。PR #37 创建 `DISABLE_MAX_PRIVILEGE` 受限令牌并验证 Low Integrity 读/写边界。本轮进一步以该主令牌通过 `CreateProcessAsUserW` 启动实际 worker：进程先挂起，只继承协议标准流三个句柄，加入生命周期 Job 后才恢复；父进程逐个查询 worker token 均为 Low，worker 自身 write-up 探针被阻断，同时自有 BMP 提取、500/500、硬超时、恢复、父进程退出、像素协议与预算继续通过。

这关闭了“worker 仍与主进程同权限”和“入 Job 前抢跑”的原型风险。新增实际 worker 读探针又证明：父进程未通过 broker 授予的中完整性标记文件仍可读取，而 write-up 仍被 MIC 阻断。因此不能把 Low Integrity 的 no-write-up 写成文件保密边界：

PR #39 合入后的首轮主干 CI `30690663507` 识别出父进程退出探针的 ready-file 发布竞态。PR #40 让子宿主通过“写完并关闭临时文件 → 同目录原子重命名”发布就绪信号，本地连续三轮完整矩阵和修复后的主干 CI `30690919941` 均通过；这属于测试证据可靠性修复，不改变隔离结论或预算。

- ADR-0002 建议生产采用无宽泛 Capability 的 AppContainer + 单请求父进程 broker；受限 Low Integrity 模式只保留为诊断基线；
- 必须验证 AppContainer 启动、未授权读取阻断、文件句柄/受控副本/最小路径 ACL、缓存与共享内存边界；
- 本轮已关闭上述第一项并验证最小路径 ACL 的边界语义；仍需把真实 worker 和 Shell 提取迁入同一隔离模型，不能用控制进程结果代替 provider 兼容性；
- 正式产品配置 schema、Infrastructure 接线和安装范围必须等待 #23 确认首版模式、最低系统、架构、渠道与许可证。

### 4.7 显式共享内存句柄 broker 已跑通，文件访问 broker 仍未定义

目标进程获得的映射句柄不可继承且只请求 `FILE_MAP_WRITE`；父进程保留原始映射并只读取结果，避免把父进程的完整映射权限复制给 worker。

协议 v4 不再把正常 BGRA32 字节编码进 JSON。父进程创建匿名、不可执行、最大 262,144 bytes 的页文件映射，并用 `DuplicateHandle` 把不可继承句柄复制到当前 Low Integrity worker；协议只传目标进程中的句柄值、固定容量和像素元数据。Worker 映射写入后关闭目标句柄，父进程只读映射并复核 transport、格式、尺寸、步幅、声明长度、容量和非零内容。缺失句柄、错误容量、错误格式/尺寸/步幅/长度、畸形旧 inline 编码、未请求负载和超限尺寸均被拒绝，9/9 后续恢复成功。v4 新增实际 worker 只读暴露探针并将其纳入报告，不向协议输出标记内容。

这证明了“主进程按请求授予一个有界内核对象能力”的机制。协议 v6 把文件输入明确分为父进程受控副本和 comparison-only 的最小路径 ACL；`DirectPath` 仍被拒绝。两种方式都能让 worker 直接读取已授权输入并阻断相邻文件，但 26100 上 Shell 都安全返回 `E_ACCESSDENIED`。当前 `IShellItemImageFactory` 依赖由 parsing name 创建的 `IShellItem`，原始文件句柄不能直接塞进这条 API；handle-backed 输入需要新的 provider/stream/decoder 合同，而不是再加一个枚举值。

在上述决定前，可继续准备专用环境与实机执行，但不应默认某种权限模型或创建承诺兼容性的正式 schema。

### 4.8 零 Capability AppContainer 边界已得到正向证据

父进程创建随机临时 AppContainer Profile，`SECURITY_CAPABILITIES` 的 CapabilityCount 为 0；三个控制进程均以 `CREATE_SUSPENDED` 启动，只继承父进程显式打开的 `NUL` 标准流句柄，加入 `KILL_ON_JOB_CLOSE` Job 并通过 `TokenIsAppContainer` 复核后才恢复。No-op 控制退出码为 0，排除了进程无法执行造成的假阳性。

父进程只在探针自有 broker 子目录为该随机 AppContainer SID 增加只读/遍历 ACL：控制文件退出码为 0，同级未授权标记退出码为 1；结束后 Profile 和文件沙箱均删除。首版控制曾因没有可用标准输出使读命令退出失败，第二版直接把命令文件重定向为 stdin 又因 `cmd /c` 语法和无命令等待产生失败/超时；最终沿用显式标准流句柄白名单，让退出码只反映访问结果，判定阈值未放宽。

这证明了 AppContainer + 精确对象授权可以表达 ADR-0002 所需的“显式授权可读、相邻未授权拒绝”。当前真实 `IShellItemImageFactory` worker 已迁入相同的零 Capability 启动模型，并完成受控副本与最小路径 ACL 对照；下一切片应转向 build/provider 兼容矩阵和不同 decoder 合同，而不是扩大文件系统 Capability。

### 4.9 真实缩略图 worker 已进入零 Capability AppContainer

父进程为每个 client 创建随机临时 Profile，把受限于 128 MiB 的 worker 运行时暂存到该 Profile 私有目录；所有 worker 通过 `SECURITY_CAPABILITIES` 以零 Capability 挂起启动，只继承 stdin/stdout/stderr，先加入 `KILL_ON_JOB_CLOSE` Job，并由父进程查询 `TokenIsAppContainer` 后恢复。AppContainer 内不再打开父进程句柄，异常父退出由内核 Job 回收；独立宿主把 Profile 名写入原子 ready 信号，主探针在确认孤儿退出后删除遗留 Profile。

父进程对真实提取输入执行 32 MiB 上限和重解析点拒绝。协议 v6 默认使用 `ControlledCopy`，同时用 `MinimumPathAcl` 给探针自有文件/父目录增加精确无继承 Read/Traverse ACE，并在请求后删除、复核随机 SID 无显式残留；worker 仍拒绝直接路径 transport。Windows `10.0.22621` 本地矩阵两种方式都可提取，默认副本 500/500；Windows `10.0.26100` GitHub runner 上两种输入都可直接读，但 Shell 都稳定返回 `E_ACCESSDENIED`、无像素。CI 将后者作为 `ProductFallbackRequired`，不允许回退到主进程或 Low Integrity 现场提取。结论仍为 Conditional Pass：ACL 方案没有解决 26100 兼容性，还会短时修改 DACL，异常退出残留修复尚未实现。最新多格式证据见 4.11。

### 4.10 最小路径 ACL 对照已完成，但不应提升为默认

这轮对照区分了文件系统授权和 Shell provider 兼容性：22621 上 AppContainer 可直接读取原路径并完成 `IShellItemImageFactory` 提取；26100 CI 上同一授权可直接读，但 Shell 仍返回与副本路径相同的 `0x80070005`。因此当前失败不能归因于 Profile 副本本身不可读，更可能是 build/runner 下 Shell provider 与 AppContainer 的兼容限制。

安全代价同样明确：最小 ACL 在请求期间修改文件和父目录 DACL，正常 Dispose 已复核 ACE 清理，但父进程崩溃、并发 ACL 修改和遗留 ACE 修复未覆盖。该方案只保留为探针对照；下一优先级是 provider/build 矩阵、正式类型图标/缓存回退接线，以及另一种受控 stream/decoder 合同评估。

### 4.11 首轮 build × format/provider 矩阵已建立

探针现在只在随机临时沙箱生成自有 BMP、PNG、GIF、JPEG、TIFF，并对每个格式分别执行 `ControlledCopy` 与 `MinimumPathAcl`。每项必须先证明授权输入可直接读取，再只接受 Shell 提取成功、精确 `E_ACCESSDENIED` 或精确 `ERROR_MOD_NOT_FOUND`；同一格式的两种策略必须得到相同分类与 HRESULT，两套 client 还必须全部为 AppContainer、使用目标授权策略、恢复 ACL 并删除 Profile。首版 read-control 漏填 transport 时，六次 Shell 提取虽成功但六个直接读控制全部失败，矩阵正确返回 Fail；修正为显式 transport 后才通过，没有放宽判定。

Windows 22621 本地十个组合全部输入可读且同格式两种策略完全一致：BMP/PNG/GIF/JPEG 八组合提取成功，TIFF 两组合精确返回 `0x8007007E`；默认副本压力 500/500、p95 30.60 ms。五格式首次运行错误地要求跨格式结果一致，因此在 TIFF 暴露 provider/module 缺失时返回 Fail；修正后保留 `UniformOutcomeAcrossFormats=false` 作为证据，只把同格式策略一致性作为门禁，没有扩大精确 HRESULT 白名单。Windows 26100 PR CI `30708856264` 中十个组合全部输入可读，却全部返回 `0x80070005`，说明该环境的更早 Shell/AppContainer/build 访问限制覆盖了 22621 可观察到的 TIFF 差异。该证据仍是 E2：它不覆盖 HEIF、Office/PDF、真实第三方 provider、云/网络水合、ARM64 或 500 个不同项目，也不授权读取用户文件。

### 4.12 父进程对照定位 provider 与隔离边界

矩阵进一步加入普通父进程 Shell 基线，并把 TIFF 分成自生成的未压缩 RGB 与 LZW 两个样本。父进程也只允许成功或精确 `0x8007007E`；扩展级 handler 检查只输出“已注册、模块存在、陈旧注册”三个布尔值，不输出 handler 身份、CLSID、厂商或路径。worker 是否逐格式匹配父进程作为诊断证据，不跨隔离级别强求相同结果。

Windows 22621 本地：BMP/PNG/GIF/JPEG 在父进程和两种 worker 策略均成功；两种 TIFF 在三条路径都精确返回 `0x8007007E`，且都观察到扩展级 handler 已注册但模块缺失，因此上一轮 TIFF 结果被纠正为机器级陈旧 provider 注册，而不是 AppContainer 特有失败。worker 十二组合全部安全分类并逐格式匹配父进程；500/500、p95 48.19 ms。Windows 26100 PR CI `30709902919`：父进程六项全部成功且未观察到陈旧扩展 handler，两个 AppContainer 策略的十二个输入全部可读但 Shell 全部返回 `0x80070005`；`WorkersMatchParentPerFormat=false` 在此是预期诊断证据，明确说明失败出现在隔离边界之后。该结论不授权主进程回退，也不修改用户注册表。

### 4.13 HEVC HEIC 暴露了注册健康与实际提取能力的差异

矩阵新增一个静态嵌入、2×2、自有 HEVC 压缩 HEIC 样本，不增加产品或探针运行时依赖。父进程和 worker 继续只输出固定格式标签、精确 HRESULT 与注册健康布尔值。初次运行因未分类的 `0x8004B200` 正确返回 Fail；审计 Windows SDK 后确认该值为 `WTS_E_FAILEDEXTRACTION`，随后只对“HEIC + 该精确 HRESULT”增加 `ShellExtractionUnavailableSafely`，没有接受任意提取失败。

Windows 22621 本机的 HEIC 在父进程和两种 worker 策略中均精确 `0x8004B200`，handler 已注册且模块存在；七个父进程样本和十四个 worker 组合全部安全分类且逐格式一致，500/500、p95 33.43 ms。Windows 26100 PR #58 CI `30710720683` 中，父进程前六项成功但 HEIC 同样精确 `0x8004B200`；两个 AppContainer 策略的十四个输入全部可读，却统一返回 `0x80070005`。因此注册健康不能替代能力探测，HEIC 当前只能安全降级；同时 26100 的 worker 失败仍位于输入授权之后的 AppContainer/Shell 边界。Microsoft 的 HEIF codec 文档也说明 HEVC/AV1 codec 不保证存在，后续必须在受控成功解码环境补正向路径，并继续覆盖 AVIF。

### 4.14 AVIF 与 decoder 能力预检补齐三层证据

矩阵新增一个静态嵌入、2×2、自有 AV1 压缩 AVIF 样本，并在父进程提取前通过 Media Foundation `MFTEnumEx` 查询 HEVC/AV1 video decoder。报告只保留查询成功、HRESULT 和 decoder 是否可枚举，不输出实现身份。首轮 AVIF 因未分类的 `0x8004B200` 正确返回 Fail；确认它与固定 HEIC 样本相同后，只把精确分类扩展到 HEIC/AVIF，没有接受其他格式的通用提取失败。

Windows 22621 本机可枚举 HEVC decoder、不可枚举 AV1 decoder；但 HEIC 与 AVIF 在父进程和两种 worker 策略均精确 `0x8004B200`，说明 decoder 存在仍不能替代 Shell 端到端能力验证。八个父进程样本和十六个 worker 组合全部安全分类且逐格式一致，500/500、p95 29.60 ms。Windows 26100 PR #60 CI `30711608583` 中 HEVC/AV1 decoder 均不可枚举；父进程前六项成功、HEIC/AVIF 精确 `0x8004B200`，两个 AppContainer 策略的十六个输入全部可读并统一 `0x80070005`。下一兼容切片转向受控 HEIC/AVIF 成功环境和 Office/PDF；本轮不安装系统 codec，也不扩大权限或回退路径。

## 5. 后续开发方向

### Gate A：主干集成与治理收尾已完成

目标：让 `main` 成为唯一可信、可构建、可回归的工程基线。

完成条件：

- [x] 串联开发提交已进入 `main`；
- [x] 最新 `main` 全量 CI 通过；
- [x] PR #2–#17 的分支均无 `main` 之外的独立提交；
- [x] PR #18 经审核后合入 `main`；
- [x] PR #2–#18、#25–#61、#63–#67 已合并或关闭，远端无长期串联草稿分支；
- [x] `main` 已启用禁止强推、禁止删除和必需状态检查；
- [x] Phase 0 Exit 里程碑和 #19–#24 已建立；
- [x] 后续 PR 从最新 `main` 创建。

Gate A 已完成。后续继续保持一个独立风险切片对应一个短生命周期 PR，并在合入后删除远端功能分支。

### Gate B：先补齐体验轨和负责人决策

体验轨交付：

- 首次启动 → 扫描摘要 → 建议预览 → 创建容器 → 添加引用 → 撤销；
- 拖放前、中、后的引用/移动语义；
- 布局恢复差异和失败恢复；
- 规则模拟、冲突说明和活动记录；
- 5 人无提示任务测试及问题严重度清单。

负责人必须签字确认：

- 许可证和发行模式；
- 支持矩阵和安装渠道；
- MVP 整理模式、Folder Portal 范围和明确不做项；
- 空闲内存/CPU、500 项响应和恢复成功率预算。

Gate B 必须先于相邻格式、provider 或架构矩阵扩张，因为负责人支持范围决定哪些矩阵属于真正的 Phase 0 阻断项。

### Gate C：关闭 Phase 0 剩余人工与技术风险

优先顺序：

1. **P0-04/P0-05b2 人工矩阵**：键盘、鼠标、触控、拖放、Narrator、Win+D、全屏、Alt+Tab、任务视图、Explorer 重启；
2. **P0-07b2b2b2b4b2 动态显示矩阵**：缩放、旋转、拔插、投影、睡眠、RDP、`WM_DPICHANGED`；
3. **文件安全探针**：安全引用、托管目录、冲突/取消/部分成功和 `IFileOperation` 撤销边界；
4. **隔离与性能**：只补已批准首发范围内的缩略图兼容性，并保留 500 项渲染/内存/空闲 CPU 基准；
5. **持久化剩余边界**：正式产品 schema、迁移/回滚和可用专用环境中的真实卷空间耗尽/只读；断电与云/网络文件系统进入专用环境矩阵；
6. **安装验证**：最低系统版本、x64/ARM64 选择、MSIX 开发包和卸载恢复。

每项必须有固定环境、原始证据、Pass/Conditional Pass/Fail 和 ADR/路线图回写。应用关闭排空、完整单实例激活和正式渲染表面只有在 App/DesktopHost 存在后才能验证，因此列入 Gate E 的首片验收，不再作为创建生产项目之前的循环门槛。

已有 Conditional Pass 的技术族仅在 CI 回归、安全缺陷、明确支持决策或现有退出场景失败时继续深化。

### Gate D：完成 ADR-0001 Go/No-Go

只有 Gate A-C 证据齐全后，ADR-0001 才可从 Proposed 变更为：

- **Accepted**：WinUI 3 设置端 + 独立原生 DesktopHost；
- **Revised**：例如设置端保留 WinUI 3，宿主采用更窄的 C++/Win32/DComp 边界；
- **Rejected**：关键系统行为、性能或无障碍门禁失败，需要回退方案。

ADR 必须同时记录最低系统版本、架构、安装方式、宿主窗口模型、渲染栈和已接受限制。

### Gate E：第一个生产化 MVP 垂直切片

建议第一片严格限制为“只读桌面目录 + 一个视觉容器 + 持久化引用”，不执行真实文件移动：

1. 建立 `LongGrid.Contracts`：版本化配置、容器、引用、布局和错误合同；
2. 建立 `LongGrid.Infrastructure`：只读 Shell catalog、原子配置存储和脱敏诊断；
3. 建立 `LongGrid.DesktopHost`：每显示器 HWND、显式 Region、统一输入/UIA 状态机；
4. 建立 `LongGrid.App`：启动、托盘、设置和恢复入口；
5. 打通创建容器、添加/移除引用、自动保存、重启恢复；
6. 加入 Feature Flag、错误恢复、集成测试和 UIA smoke；
7. 用 PRD 场景 3、4、7、8、10 验收这一片。

真实移动、自动规则执行和 Folder Portal 在这条只读链稳定后单独立项，不与第一片捆绑。

## 6. 后续优先级

| 优先级 | 现在做 | 暂不做 |
|---|---|---|
| P0 | 先完成 #23，再推进 #19–#20，并按批准范围收口 #21–#22 和 #24 合同 | 新增视觉特效和无范围依据的探针扩张 |
| P1 | 只读 MVP 垂直切片、持久化、诊断、性能和安装 | 真实自动移动 |
| P2 | 规则预览、可撤销移动、Folder Portal、搜索 | 任务栏替换 |
| P3 | 任务栏实验、LongBar、小组件、Long助手插件运行时 | 默认启用实验能力 |

LPWP 协议可以继续做兼容性维护和 Golden Fixture，但 Widget Host 不进入当前 MVP 主路径。

## 7. 近期交付建议

不按日历承诺，在每个退出条件满足后顺序推进：

| 顺序 | 工作包 | 交付物 | 退出条件 |
|---|---|---|---|
| W0（完成） | GitHub 治理 | PR #18、#25–#61、#63–#73 已合入；PR #2–#17 关闭；旧远端分支删除；`main` 受保护；Phase 0 Exit 建立 | 主干 CI 通过、当前切片前无打开 PR、远端只保留 `main`，保护规则要求严格 `build-test` |
| W1（进行中） | 产品与体验决策（#23） | 首次整理、匿名容器/项目、拖放语义、两步撤销和恢复差异原型已完成；待 5 人测试、许可证、支持矩阵、首版模式 | 负责人签字确认范围与预算，并限定后续技术矩阵 |
| W2（工具就绪） | 人工输入与系统表面（#19） | 匿名单场景入口和恢复手册已完成；待 P0-05b2 键鼠/触控/拖放/Narrator/Win+D/全屏/Alt+Tab/任务视图/Explorer 重启记录 | 每个场景有环境、原始证据、Pass/Fail/Inconclusive 和缺陷 |
| W3 | 动态显示（#20） | 缩放、旋转、拔插、投影、睡眠、RDP、`WM_DPICHANGED` 受控矩阵 | 稳定器、布局事务、资源闭环和恢复结果全部可复读 |
| W4 | 必要安全、隔离与持久化收口（#21、#22、#24） | 已批准范围内的文件/Provider/真实卷证据和生产合同 | 不误移动，安全边界明确，生产合同可迁移；集成接线列入首片验收 |

W0 已关闭。W1 先限定产品和支持范围；W2 与 W3 可在不同受控环境并行采证；W4 不应与真实产品文件移动混做。配置持久化合同由 #24 跟踪，只在具备专用环境时补真实卷/断电边界；关闭排空、单实例和正式渲染接线在首个 App/DesktopHost 切片中验收。Phase 0 的必要证据关闭并更新 ADR 后，创建第一条只读垂直切片。

## 8. 审计判定

### 可以继续

- 继续 Phase 0 的受控验证和交互原型；
- 修复已有探针、测试和文档的不一致；
- 开展负责人决策、可用性测试与主干治理收尾；
- 设计 MVP 合同，但不提前承诺发布能力。

### 暂不可开始

- 完整 MVP 功能冲刺；
- 默认启用真实文件移动或自动整理；
- 将探针二进制作为产品发布；
- 任务栏替换、插件市场、小组件生态或云同步；
- 对外宣称已支持完整多显示器、无障碍或 Explorer 恢复矩阵。

最终判断：**方向正确，底层风险控制质量较高，但执行顺序已偏向技术探针。当前应先补 #23 的体验与负责人决策，再完成 #19–#20 人工/硬件矩阵，只按批准范围收口 #21–#22，并把 #24 的产品接线类验证移入首个生产切片，消除循环门槛。完成这些纠偏后再开始只读 MVP，是风险和交付效率更优的路径。**
