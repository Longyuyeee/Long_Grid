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
- [x] P0-01a 用户/Public 物理桌面目录只读发现。
- [x] P0-01b Shell Desktop Namespace 枚举与差异对账。
- [x] P0-01c 稳定文件身份、快捷方式双重身份与重命名跟踪。
- [x] P0-02 Shell 变化通知、事件合并恢复与最终一致性。
- [x] P0-03a 图标/缩略图异步加载、排队取消与句柄稳定性；P0-03b 零 Capability AppContainer 真实 worker、挂起启动后先加入 Job、显式继承句柄列表、32 MiB 受控只读输入、协议 v6 的 `ControlledCopy`/`MinimumPathAcl` 对照、八个自有格式/编码样本的父进程与双 build 十六组合矩阵、脱敏 handler 健康与 HEVC/AV1 decoder 能力分类、250 ms 硬超时、父进程退出/Profile 清理、连续超时退避、有界共享内存 BGRA32 IPC、未代理读写阻断及合成 500 项预算（Conditional Pass；22621 TIFF 为陈旧 handler；两 build 的 HEIC/AVIF 父进程均精确 `WTS_E_FAILEDEXTRACTION`；26100 AppContainer 十六组合全部安全拒绝；正式渲染和 HEIC/AVIF 成功环境、文档、云网络、受控第三方 provider/ARM64 矩阵未关闭）。
- [x] P0-04/P0-05a 每容器/每显示器 HWND 的原生命中与资源对比（Conditional Pass；下一原型采用每显示器 HWND + 显式交互区域）。
- [x] P0-04/P0-05b1 可见容器/项目、选择/调用、UIA SelectionItem/Invoke Pattern 与事件垂直切片（Conditional Pass）。
- [ ] P0-04/P0-05b2 键盘/鼠标/触控/拖放、Narrator、Win+D、全屏、Alt+Tab、任务视图和 Explorer 重启人工矩阵。
- [x] Issue #19 匿名人工矩阵会话入口、单场景运行手册和 CI `PendingManualEvidence` 安全合同（Ready to execute；I19-01–I19-10 真实人工结果仍 Pending）。
- [x] P0-06 版本化配置、同目录暂存、落盘校验、原子替换、备份恢复、安全模式、四检查点共 1,000 次及 ACL 生效期间子进程强杀、跨进程单写租约、有界可取消退避与具备入队快照/有界排空的 latest-wins 保存协调、确定性 v1→v2 示例迁移回滚、四类只读文件恢复、受控磁盘满注入及权限语义（Conditional Pass；Phase 0 仍缺断电、真实卷空间耗尽/只读、跨进程公平性和产品 schema；真实应用关闭/单实例接线列入首片验收）。
- [x] Issue #24 正式 Core 配置 v1 合同、只读 reference 行为、未知字段保留、有限验证错误和 JSON 资源预算（Partial；Infrastructure 原子存储、真实卷、关闭排空和单实例仍 Pending）。
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
- [x] PR #2–#18、#25–#61、#63–#69 已收口，相关代码进入 `main`，旧远端功能分支已删除。
- [x] `main` 严格要求 `build-test`，管理员同样受约束，禁止强推和删除；CI 强制执行配置/文件安全/缩略图 worker 探针，行覆盖率门禁 90%、分支门禁 75%，当前本地实测 91.43%/77.25%，待 PR CI 复核。
- [x] 建立 `Phase 0 Exit` milestone 和 Issue #19–#24 跟踪剩余门禁。
- [ ] P0-07b2b2b2b4b2 在受控实机执行缩放、旋转、拔插、投影、睡眠、RDP 和 WM_DPICHANGED 动态矩阵。
- [x] Issue #20 匿名动态矩阵入口、I20/observer 映射、恢复确认和 CI `PendingManualEvidence` 合同（Ready to execute；I20-01–I20-08 真实硬件/会话结果仍 Pending）。
- [ ] 其余探针与交互验证。

当前阶段门禁顺序：

1. 按 Issue #23 使用已完成的首次整理/引用与移动语义原型执行 5 人测试，并完成负责人范围决策；
2. 按 Issue #19 完成 P0-04/P0-05b2 人工输入、无障碍与系统表面矩阵；
3. 按 Issue #20 完成 P0-07b2b2b2b4b2 动态显示硬件矩阵；
4. 按已批准的支持范围收口 Issue #21–#22；不再机会主义扩展相邻格式、provider 或故障组合；
5. 按 Issue #24 确认配置合同和独立存储证据；应用关闭、单实例与正式渲染接线放入首个生产切片验收，避免循环依赖；
6. 更新 ADR-0001 后，进入只读 MVP 垂直切片。

执行停止规则：已有 Conditional Pass 的探针族，除 CI 回归、安全缺陷、明确支持决策或现有退出场景失败外，不再增加深度；每个新 PR 必须对应一个未满足的阶段退出条件。

退出门槛：

- 所有 P0 探针有明确 Pass/Fail。
- 无需进程注入或内核驱动。
- 文件默认不移动的产品原则得到确认。
- 明确首版整理模式，并向用户展示引用与真实移动的差异。
- DesktopHost 不依赖 `Progman`/`WorkerW` 等未文档化桌面嵌入。
- 技术栈和许可证得到负责人批准。
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
