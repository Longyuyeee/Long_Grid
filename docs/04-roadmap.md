# Long Grid 路线图与验收门槛

路线图采用“风险先行”，时间只在团队规模确定后估算。每阶段结束都需要可演示产物和量化证据。

每个阶段内的需求进入、风险分级、Phase 0 双轨验证、技术探针、PR、测试、Definition of Done 和发布流程统一遵循[开发流程与交付规范](10-development-workflow.md)。

Phase 0 剩余实机矩阵、专用环境验证和负责人签字统一使用[Phase 0 出口执行手册](12-phase-0-exit-runbook.md)，未执行的场景保持 Pending/Inconclusive。

当前状态、证据等级、集成风险和建议顺序见[当前开发状态与后续方向审计](11-development-status-and-direction-audit.md)，与 2026-07-30 初始计划的逐项对比见[初始计划对齐与偏移审计](13-original-plan-alignment-audit.md)。路线图中的勾选表示对应子问题已有代码和报告，不表示完整产品能力已经完成。

## Phase 0：立项与技术验证

目标：证明 Windows 桌面集成可行。

交付：

- 仓库、解决方案、代码规范、测试框架和 CI。
- ADR-0001 的探针结果。
- 一个不可发布的透明容器原型。
- 用户/Public/重定向桌面枚举、Shell 变更监听与图标/缩略图实验。
- “每容器 HWND”与“每显示器 HWND”两种 DesktopHost 对比原型。
- 多显示器/DPI、Win+D、Explorer 重启、拖放和原子配置实验。
- 安全引用、托管目录和原生图标位置三种整理模式的可行性报告。
- `IFileOperation` 移动/冲突/取消/撤销边界实验。
- 首次启动、桌面编辑、拖放语义、规则模拟和恢复差异的低保真交互原型。
- 至少一次 5 人可用性测试，重点验证用户能否区分“引用”和“移动”。
- 最低系统版本、x64/ARM64 和安装方式决策。

进度：

- [x] 基础 .NET 8 解决方案、Core、测试和 PR CI。
- [x] 开发期只读 `LongGrid.App` UI Shell、Design Token、品牌 RC1 与统一启动链（Conditional Pass；仅匿名示例数据，不接真实桌面/DesktopHost/文件操作，不代表正式 MVP 开工）。
- [x] 正式 App 的 12 个 AutomationId、导航访问键、内存态系统/浅色/深色主题、CI 结构合同与真实窗口 UIA 冒烟（Conditional Pass；Narrator、高对比、缩放、DPI 和视觉矩阵仍未关闭）。
- [x] 正式 App 的 760 DIP 响应式断点、纵向紧凑流、DPI 感知默认窗口、工作区 90% 上限及 720 px 宽/紧凑/宽真实 UIA 往返（Conditional Pass；系统文本缩放与多 DPI 视觉矩阵仍未关闭）。
- [x] 开发期 App 对 Core 不可变运行状态快照的单向接线，区分 Core 合同、桌面目录、文件操作和 DesktopHost 状态，并通过 UIA 暴露机器可判定值（Conditional Pass；不枚举真实桌面、不启动宿主、不执行或开放文件操作）。
- [x] Issue #23 核心低保真链路：一键建议/空白、安全引用/移动阻断、匿名容器与三个项目、拖放动作语义、两步撤销、布局恢复三态/过期/取消，并完成真实 UIA 自动化（Partial；5 人无提示测试、负责人产品决策、真实拖放与硬件恢复仍 Pending）。
- [x] Issue #23 匿名五人测试会话入口、主持人手册和 CI `ResultsPending` 隐私合同（Ready to schedule；P1–P5 真实结果尚未产生）。
- [x] Issue #23 D23-01–D23-10 首发范围已由负责人批准：仅安全引用、本地无账户、Windows 11 x64 技术预览、MSIX 目标渠道、类型图标安全回退；D23-11 许可证延期，不阻挡当前开发但继续阻挡正式分发/外部贡献。
- [x] P0-01a 用户/Public 物理桌面目录只读发现。
- [x] P0-01b Shell Desktop Namespace 枚举与差异对账。
- [x] P0-01c 稳定文件身份、快捷方式双重身份与重命名跟踪。
- [x] P0-02 Shell 变化通知、事件合并恢复与最终一致性。
- [x] P0-03a 图标/缩略图异步加载、排队取消与句柄稳定性；P0-03b 零 Capability AppContainer 真实 worker、挂起启动后先加入 Job、显式继承句柄列表、32 MiB 受控只读输入、协议 v6 的 `ControlledCopy`/`MinimumPathAcl` 对照、八个自有格式/编码样本的父进程与双 build 十六组合矩阵、脱敏 handler 健康与 HEVC/AV1 decoder 能力分类、250 ms 硬超时、父进程退出/Profile 清理、连续超时退避、有界共享内存 BGRA32 IPC、未代理读写阻断及合成 500 项预算（Conditional Pass；22621 TIFF 为陈旧 handler；两 build 的 HEIC/AVIF 父进程均精确 `WTS_E_FAILEDEXTRACTION`；26100 AppContainer 十六组合全部安全拒绝；正式渲染和 HEIC/AVIF 成功环境、文档、云网络、受控第三方 provider/ARM64 矩阵未关闭）。
- [x] P0-04/P0-05a 每容器/每显示器 HWND 的原生命中与资源对比（Conditional Pass；下一原型采用每显示器 HWND + 显式交互区域）。
- [x] P0-04/P0-05b1 可见容器/项目、选择/调用、UIA SelectionItem/Invoke Pattern 与事件垂直切片（Conditional Pass）。
- [ ] P0-04/P0-05b2 键盘/鼠标/触控/拖放、Narrator、Win+D、全屏、Alt+Tab、任务视图和 Explorer 重启人工矩阵。
- [x] Issue #19 匿名人工矩阵会话入口、单场景运行手册和 CI `PendingManualEvidence` 安全合同（Ready to execute；I19-01–I19-10 真实人工结果仍 Pending）。
- [x] Issue #19 原生 Unicode 窗口标题边界：默认窗口过程/消息循环统一使用 `W` 入口，interactive smoke 增加完整标题回读门禁（I19-01 外部工具尝试仍 Inconclusive，不替代真人输入）。
- [x] P0-06 版本化配置、同目录暂存、落盘校验、原子替换、备份恢复、安全模式、四检查点共 1,000 次及 ACL 生效期间子进程强杀、跨进程单写租约、有界可取消退避与具备入队快照/有界排空的 latest-wins 保存协调、确定性 v1→v2 示例迁移回滚、四类只读文件恢复、受控磁盘满注入及权限语义（Conditional Pass；Phase 0 仍缺断电、真实卷空间耗尽/只读和跨进程公平性）。
- [x] Issue #24 正式 Core 配置 v1 合同、只读 reference 行为、未知字段保留、有限验证错误和 JSON 资源预算（Partial；正式存储/排空/单实例见后续项，真实卷仍 Pending）。
- [x] Issue #24 首个正式 Infrastructure 配置存储切片：同目录 `.new`、落盘 flush、正式 schema 复读、原子发布、上一版备份、主损坏恢复、安全模式、损坏证据保护和跨进程写租约（此切片当时为 Partial；恢复 UI 已由后续切片完成，真实卷矩阵仍 Pending）。
- [x] Issue #24 正式 latest-wins 与 App 关闭排空：入队深快照、等待批次合并、取消隔离、失败后继续、完成拒绝新请求，App 关窗 5 秒有界等待/超时保留窗口/重试；只读 Shell 零入队、启动关闭零写入。
- [x] Issue #24 完整单实例激活：自定义 WinUI 入口在 XAML 初始化前注册固定 key，第二进程转发完整 `AppActivationArguments` 后退出，主实例排队早到激活并在 UI 线程恢复/激活窗口；真实双进程最小化恢复通过（关闭排空竞态、恢复 UI 与文件/URI/插件 payload 合同仍 Pending）。
- [x] Issue #24 配置恢复状态 UI：App 只读加载正式 Store，有限启动状态不携带 Document/路径/原始合同错误；概览 InfoBar 区分无配置、主配置有效、备份只读恢复和安全模式，损坏状态零自动覆盖；后续已接入经确认的备份接受与 SafeMode 空白重置（本机 UIA 环境证据仍 Inconclusive）。
- [x] Issue #24 已验证备份接受：仅在备份恢复态显示入口，二次确认默认取消；锁内复检后原子接受备份、保留原备份并归档损坏主配置，有限结果不泄露路径。
- [x] Issue #24 SafeMode 空白安全重置：标准无用户状态 v1 配置、锁前/锁内复检、主备损坏证据分别归档、发布失败备份回滚、中断恢复标记与默认取消确认（受限导入和证据生命周期基础已由后续切片完成；自动策略与真实卷结果仍 Pending）。
- [x] Issue #24 受限外部配置导入：系统 picker 明确授权、本地 `.json`/非重解析点/4 MiB/current-v1 边界、有限预览、存储修订冲突、默认取消确认、双暂存中断标记、四态原子发布与证据回滚（导出和证据生命周期基础已完成；迁移等待真实 v2，自动策略和真实卷结果仍 Pending）。
- [x] Issue #24 受限配置导出与匿名证据清单：有效主/备份有限预览、确认后 FolderPicker、本地非重解析点目录、唯一文件名、写穿复读、不覆盖发布、存储冲突阻断，以及精确归档名称/4096 项扫描上限/256 项返回上限/无路径无内容只读清单（迁移、证据原文导出/保留/清理和真实卷结果仍 Pending）。
- [x] Issue #24 单项原始证据显式导出：匿名单选、敏感内容警告、确认后 FolderPicker、来源目录/名称/元数据复检、64 MiB 上限、流式 SHA-256 复读、唯一 `.bin` 不覆盖发布和原证据保留（选择性单项删除已由后续切片完成；自动保留/容量策略仍 Pending）。
- [x] Issue #24 证据生命周期基础：有界观察条数/总容量/最早时间、匿名单选、默认取消永久清理确认、清理前导出提示、锁前/锁内复检、有界写租约和单文件原子删除（自动保留阈值、批次日志、中断恢复和失败重试仍 Pending）。
- [x] Issue #24 真实产品保存与显式重试合同：有限 Saved/Failed/NoRetryAvailable/Completed 结果、无效 schema 收敛、仅保留最新可重试深快照、新保存取代旧重试、取消不制造重复重试、关闭接收竞态封闭；匿名演示 UI 继续零入队（产品状态/v1 投影已由后续切片完成，配置恢复解析和保存状态 UI 仍 Pending）。
- [x] Issue #24 产品工作区状态与 v1 投影：UI 无关 profile/container/appearance/placement/reference 状态、已解析 Catalog Entry 输入、filesystem/绝对 canonical target/可选稳定身份成对复核、四类型确定映射、未知字段深快照及工作区直达有限保存入口；匿名 UI 继续零入队（配置到 Catalog 的有限恢复已由后续切片完成，产品 reducer 和保存状态 UI 仍 Pending）。
- [x] Issue #24 配置到 Catalog 有限解析：统一身份策略、resolved/missing/type-changed/ambiguous/unsupported-target 状态、无效配置/Catalog 有限失败、重复候选零自动选择、未解析领域 ID/kind/target/扩展字段无损重投影和匿名分类计数；不枚举文件系统且匿名 UI 继续零入队（产品 reducer、连续编辑保存状态机和保存 UIA 仍 Pending）。
- [x] Issue #24 产品工作区 reducer 与连续保存纯状态：不可变创建/重命名/外观/放置/锁定/引用操作、锁定保护、未解析引用显式删除确认、重新选择保留领域 ID/未知字段、正式 v1 双重校验，以及 revision 驱动的最新防抖、保存/失败/重试和陈旧结果隔离；计时控制器、工作流映射、关闭竞态、保存 UIA 与普通 UI 入队仍 Pending。
- [x] Issue #24 产品工作区连续保存控制器：接受时 v1 深快照、默认 400 ms/最大 10 秒可替换防抖、新编辑取消旧等待但不撤销已接受保存、正式错误与重试不一致有限映射、关闭强制刷新、最新失败阻止关闭、超时恢复接收和安全异步释放；App 所有权、保存 presenter/UIA、Reduced Motion 与普通 UI 入队仍 Pending。
- [x] Issue #24 App 控制器所有权与保存状态 UIA：后台快照 UI 线程封送、Clean/Waiting/Saving/Retrying/Saved/Failed 隐私安全映射、5 个新 AutomationId、重试有限委托、静态 Reduced Motion、controller 关闭/失败阻断/安全释放；匿名 UI 和普通 SaveAsync/EnqueueAsync 继续为零（干净会话真实 UIA 复跑、正式产品会话加载与首次普通入队仍 Pending）。
- [x] Issue #24 正式产品会话加载：配置加载/恢复/导入复读统一进入 App 会话，Unavailable Catalog 与权威空 Catalog 明确分离，Loading/NoSaved/AwaitingCatalog/Ready/BackupReadOnly/SafeMode/Failed 有限状态、匿名解析计数及 4 个新 AutomationId；当前 Catalog 仍断开且普通提交为零（只读 Catalog 适配器、未解析引用 UI、干净会话 80-ID UIA 与首次普通入队仍 Pending）。
- [x] Issue #24 只读物理 Desktop Catalog：用户/公共桌面第一层 reader、双来源完整性权威门禁、generation/latest-wins/取消/关闭排空 controller、App 自动首刷与显式刷新、ConnectedReadOnly runtime、5 个新 AutomationId；Partial 结果不参与 Missing 判断，Shell 虚拟项和普通提交仍关闭（未解析引用 UI、generation+revision 编辑门禁及干净会话 85-ID UIA 仍 Pending）。
- [x] Issue #24 未解析引用审查与双版本门禁：稳定匿名审查列表、默认保留、显式候选重选、移除确认、Catalog generation + edit revision + 状态/锁定/候选唯一性复核、对话框期间旧 token 捕获及 8 个新 AutomationId；所有动作只返回 reducer 预演，普通 submit、配置修改和磁盘文件操作仍为零（干净会话 93-ID UIA 与首次单项真实入队仍 Pending）。
- [x] Issue #24 首次引用编辑正式提交：Infrastructure commit coordinator 固定 gate/project/单次 Submit/revision 顺序，外部加载推进 revision，接受后 App 更新内存 Document 并重建 session/review，Waiting/Saving/Failed 禁用导入导出；真实临时 Store 重载验证配置引用移除且被引用文件原样保留（正式容器视图、干净会话 93-ID UIA、真实卷与更多编辑仍 Pending）。
- [x] Issue #24 正式工作区只读视图：session 经 v1 校验投影为不含路径/内部 ID 的容器与引用快照，已解析名称用于可见/辅助功能内容，未解析引用保持匿名，UIA 机器状态仅含计数；98-ID 源码合同通过，容器 CRUD、桌面文件操作与干净会话真实 UIA 仍 Pending。
- [x] Issue #24 容器创建/重命名提交：首次空配置创建和已有容器序号重命名与引用编辑共享统一 coordinator/revision/投影/保存链，锁定、旧 revision、NoChange 与失败均零提交；104-ID 源码合同和真实 Store 文件零变化测试覆盖，锁定/折叠、外观、布局、删除/撤销仍 Pending。
- [x] Issue #24 容器锁定/折叠提交：显式锁定/解锁与折叠/展开复用统一 coordinator/revision/投影/保存链；锁定阻止重命名和折叠，解锁始终显式可用，折叠不改变引用；106-ID 合同与真实 Store 文件零变化测试覆盖，外观数值、布局、删除/撤销仍 Pending。
- [x] Issue #24 容器受限外观提交：5 个固定颜色与 4 个透明度档位通过有限枚举进入统一 coordinator/revision/投影/保存链；历史自定义合法值不被擅自归一化，锁定阻止外观编辑；109-ID 合同与真实 Store 文件零变化测试覆盖，自由输入继续关闭，布局、删除/撤销仍 Pending。
- [x] Issue #24 容器受限布局预设提交：4 个位置与 4 个尺寸 DIP 预设进入统一 coordinator/revision/投影/保存链，保留 DisplayKey 且不调用 DesktopHost；历史自定义值不自动归一化，锁定阻止布局编辑；112-ID 合同与真实 Store 文件零变化测试覆盖，拓扑解析/恢复预览、真实窗口提交、删除/撤销仍 Pending。
- [x] Issue #24 产品布局恢复配置级确认合同：正式 session、v2 保存时拓扑、权威当前拓扑三方门禁；ReviewRequired 时签发绑定双拓扑/配置指纹、topology generation 与 edit revision 的令牌，确认复核后只更新配置并进入统一保存控制器；117-ID 合同覆盖，真实窗口提交仍 Pending。
- [x] Issue #24 布局恢复一次性配置撤销：成功恢复后保留同会话恢复前状态，令牌绑定操作 ID、恢复 revision、恢复前后配置指纹和容器计数；取消/保存拒绝不消费，其他成功编辑使其失效，撤销接受后单次清除；118-ID 合同覆盖，真实窗口与桌面文件操作仍为零。
- [x] 真实窗口恢复准入与收口计数：绑定 plan/configuration/topology generation/edit revision/配置撤销的计划令牌，以及窗口所有权、复合事务、回滚故障和三类人工证据的多 blocker 评估已建立；App 零接线。布局恢复主线随后剩产品自有窗口注册/只读桥、复合事务、RC 硬化 3 个工程阶段，外部 #19/#20/#23/#24 继续 Pending。
- [x] 产品自有窗口注册表与只读 DesktopHost 桥：内部绑定容器、宿主实例/线程、宿主与窗口 generation、实例标识和最近 Bounds；有限拒绝销毁、句柄复用、宿主重启、重复容器/句柄；App 只可读取匿名计数且当前零接线，不移动窗口。布局恢复主线随后剩复合事务、RC 硬化 2 个工程阶段，外部 #19/#20/#23/#24 继续 Pending。
- [x] Issue #24 产品当前显示拓扑只读适配器：CCD/Monitor 数量、强身份、Bounds、target 可用性、rotation、WorkArea 全对账才权威；SHA-256 身份、8 次竞态重试、generation/latest-wins、取消/关闭排空和 App 双门禁接线完成。版本化保存时拓扑与真实窗口提交仍 Pending。
- [x] Issue #24 I24-01/I24-02 专用环境会话入口、独立卷标记/双确认安全合同和 CI `PendingDedicatedEnvironmentEvidence` 预检（Ready to schedule；真实卷结果仍 Pending）。
- [x] P0-07a 静态双屏拓扑、混合 DPI、隐私安全指纹与资源稳定性（Conditional Pass）。
- [x] P0-07b1 QueryDisplayConfig 活动路径、virtual-mode 索引、rotation 与 monitor 一一关联（Conditional Pass）。
- [x] P0-07b2a 拓扑精确/相似映射、歧义阻断、DIP 重映射与最小可见性恢复计划（Conditional Pass）。
- [x] P0-07b2b1 显示变化静默合并、连续稳定采样、暂停/恢复、超时与代次失效（Conditional Pass）。
- [x] P0-07b2b2a 隐藏顶层消息窗口、WTS 生命周期、后台 CCD 调度和启动稳定链（Conditional Pass）。
- [x] P0-07b2b2b1 Core 批量提交、代次门禁、提交后验证与补偿回滚协调器（Conditional Pass）。
- [x] P0-07b2b2b2a 隐藏测试 HWND、Win32 批量适配、负坐标复读、代次/部分失败补偿与资源闭环（Conditional Pass）。
- [x] P0-07b2b2b2b1 Window Region 捕获、所有权转移、部分失败/代次回滚与 GDI 闭环（Conditional Pass）。
- [x] P0-07b2b2b2b2 DirectComposition Root Commit/Wait、真实 HWND UIA provider、客户端读取与代次失效补偿（Conditional Pass）。
- [x] P0-07b2b2b2b3 Bounds/Region/DComp/UIA 固定顺序、全层快照、四层失败逆序补偿、最终复读和紧急隐藏（Conditional Pass）。
- [x] P0-07b2b2b2b4a 短时可见宿主输入开/关/重开、跨进程穿透和真实 UIA Raw View Fragment 树（Conditional Pass）。
- [x] P0-07b2b2b2b4b1 显示/设备/电源/会话动态矩阵只读采证工具、脱敏场景判定与无事件防假阳性（Conditional Pass）。
- [x] P0-08a 安全引用/托管移动纯计划、Shell 同卷移动、冲突预阻断、回调取消、部分成功和隐私安全报告（Conditional Pass；Explorer 撤销、跨卷、ACL/真实卷、云/网络/重解析点矩阵未关闭）。
- [x] Issue #21–#22 已按 D23 批准范围关闭：托管移动矩阵移出首发阻断项，缩略图以 Windows 11 x64、安全拒绝和类型图标回退收口；未执行项保留后续里程碑而不伪造 Pass。
- [x] PR #2–#18、#25–#61、#63–#80 已收口，相关代码进入 `main`；当前新切片继续使用短生命周期分支。
- [x] `main` 严格要求 `build-test`，管理员同样受约束，禁止强推和删除；CI 强制执行配置/文件安全/缩略图 worker 探针，行覆盖率门禁 90%、分支门禁 75%，当前本地实测 91.37%/77.55%。
- [x] 建立 `Phase 0 Exit` milestone 和 Issue #19–#24；#21–#22 已关闭，剩余门禁由 #19、#20、#23、#24 跟踪。
- [ ] P0-07b2b2b2b4b2 在受控实机执行缩放、旋转、拔插、投影、睡眠、RDP 和 WM_DPICHANGED 动态矩阵。
- [x] Issue #20 匿名动态矩阵入口、I20/observer 映射、恢复确认和 CI `PendingManualEvidence` 合同（Ready to execute；I20-01–I20-08 真实硬件/会话结果仍 Pending）。
- [ ] 其余探针与交互验证。

当前阶段门禁顺序：

1. 按 Issue #23 使用已批准范围和现有原型执行 5 人测试；
2. 按 Issue #19 完成 P0-04/P0-05b2 人工输入、无障碍与系统表面矩阵；
3. 按 Issue #20 完成 P0-07b2b2b2b4b2 动态显示硬件矩阵；
4. 保持 Issue #21–#22 已关闭范围；不再机会主义扩展相邻格式、provider 或故障组合，后续能力通过独立里程碑重新准入；
5. 按 Issue #24 复用已建立的正式配置存储；应用关闭排空、单实例、恢复状态 UI、显式恢复动作、当前 v1 受限导入/导出、匿名证据清单、单项原始证据导出和单项清理已完成首片验收。正式 schema 迁移等待真实 v2 字段准入；真实状态入队、获批后的自动保留/容量策略与真实卷证据继续按独立切片验收；
6. 更新 ADR-0001 后，进入只读 MVP 垂直切片。

执行停止规则：已有 Conditional Pass 的探针族，除 CI 回归、安全缺陷、明确支持决策或现有退出场景失败外，不再增加深度；每个新 PR 必须对应一个未满足的阶段退出条件。

退出门槛：

- 所有 P0 探针有明确 Pass/Fail。
- 无需进程注入或内核驱动。
- 文件默认不移动的产品原则得到确认。
- 明确首版整理模式，并向用户展示引用与真实移动的差异。
- DesktopHost 不依赖 `Progman`/`WorkerW` 等未文档化桌面嵌入。
- 技术范围得到负责人批准；许可证在正式分发或接受外部贡献前处理。
- Phase 0 成果已集成到 `main`，不存在依赖长期串联草稿分支的完成项。
- `main` 禁止强推并要求 CI 门禁，覆盖率报告可追踪但不以单一百分比替代实机验证。

## Phase 1：MVP 内测

目标：可靠完成桌面容器、引用、规则建议和布局恢复。

交付：

- 容器 CRUD、拖放引用、外观和锁定。
- 布局自动保存、手动快照、恢复与回退。
- 基础规则、预览、冲突处理和撤销。
- 托盘、快捷键、设置、引导和诊断。
- Long方格正式 WinUI UI Shell、Design Token、现代扁平视觉、平滑可降级动效和“L + 方格”品牌图标资产。
- `eng/Start-LongGrid.ps1` 一键开发启动入口和 `eng/Pack-LongGrid.ps1` 一键验证/打包入口。
- MSIX 开发包及自动化测试。

退出门槛：

- PRD 中 10 个 MVP 验收场景全部通过。
- Desktop Passive/Editing/Preview/Peek 状态、Toast 撤销和活动中心交互完整。
- 关键 Core 逻辑分支覆盖率 ≥ 80%；总体覆盖率不作为唯一质量指标。
- 24 小时稳定性测试无崩溃、无句柄持续增长。
- 500 项目基准达到已核准的性能预算。
- 完成键盘、屏幕阅读器和高对比度冒烟测试。
- 浅色/深色/高对比/文本缩放/减少动画矩阵通过，16–256 px 图标与 Windows Shell/开始菜单实机显示通过。
- 从干净克隆执行一键启动和一键打包成功；失败时返回非零退出码且不生成伪成功产物。

## Phase 2：公开 Beta

目标：验证真实机器兼容性和用户留存。

交付：

- Folder Portal。
- 更强的布局拓扑匹配和恢复报告。
- 规则模拟器、搜索、标签页/卷起。
- 签名安装包、更新与回滚。
- 经授权的崩溃反馈和诊断包。

退出门槛：

- 至少覆盖 Intel/AMD、x64/ARM64（若首发支持）和主流显卡组合。
- 7 日无崩溃会话率 ≥ 99.8%。
- 无已知数据丢失、文件误移动或高危漏洞。
- 安装、升级、降级和卸载矩阵通过。

## Phase 3：1.0

目标：形成可信赖、可长期维护的桌面整理产品。

交付：

- 稳定 API/schema 与迁移策略。
- 完整本地化、无障碍和隐私说明。
- 发布说明、用户帮助、问题反馈与支持流程。
- 商店/官网分发与自动更新。

## Phase 4：工作空间差异化

目标：从“桌面盒子”升级为“项目上下文”。

候选：

- 捕获和启动应用窗口布局。
- 工作空间模板、命令参数和最近项目。
- Widget Host、小组件布局和实例生命周期。
- 与 Long助手共享的插件合同、`.lpak` 验证及命令动作卡兼容。
- 声明 Widget Surface 的 Web 插件兼容。
- 本地智能建议。

这些能力必须经过新的 PRD、威胁模型和性能预算，不自动进入 1.0。

## Backlog 优先级

| 优先级 | 主题 |
|---|---|
| Must | 容器、引用、布局、规则预览、撤销、恢复、性能 |
| Should | Folder Portal、搜索、标签页、工作空间 |
| Could | 插件、小组件、端到端加密同步 |
| Won't now | 壁纸商城、天气、截图、系统清理、自研保险箱 |

## 决策节奏

- 每个高风险技术选择写 ADR。
- 每两周审查性能和兼容性基线，不把它们留到发布前。
- 每个里程碑只允许一个核心体验目标。
- 新功能若增加常驻资源、文件权限或网络访问，必须附带预算和威胁分析。
