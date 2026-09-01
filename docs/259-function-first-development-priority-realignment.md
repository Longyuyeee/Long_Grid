# Stage 259：功能优先开发基准与当前队列重对齐

日期：2026-09-01

输入基线：`origin/main@d4d0032239d680db5dd81e719efd121f34097d56`

状态：`PriorityRealigned / DocumentationComplete / PF008Ready`

## 1. 重对齐结论

从本阶段开始，Long方格的开发优先级固定为：

1. 核心工程实现；
2. 核心用户旅程；
3. 功能广度对标；
4. 权限、安全、证据与发布门禁作为不退化底线，而不是连续开发主线。

Stage 252～258 修正了真实存在的 M1 证据生命周期和所有权缺陷，但连续七个阶段没有增加用户可见功能。后续禁止因为外部环境暂时不可用，就继续增加相邻 marker、cleanup、Runtime、证据目录或准入探针来代替产品进度。

安全规则没有取消。涉及用户文件移动/删除、系统任务栏写入、Explorer 集成、签名安装、凭据和权限提升时，现有失败关闭、恢复、隔离和分发门禁继续生效；只有真实复现的回归、可能造成数据损坏或系统状态不可恢复的缺陷，才允许中断功能队列。

## 2. Stage 258 远端收口事实

Stage 258 的实现已通过 [PR #342](https://github.com/Longyuyeee/Long_Grid/pull/342) 合入 `main@d4d0032`。PR CI、C# CodeQL 和 C/C++ CodeQL 均成功；精确 main CI run `33425442177` 与 CodeQL run `33425442163` 成功。

精确 main Actual：Release `0 warning / 0 error`，完整测试 `1,405/1,405`、0 failed、0 skipped，coverage lines `90.14% (46,932/52,064)`、branches `76.04% (15,432/20,294)`，198 required Automation IDs，已知依赖漏洞 0；许可证与分发继续为 `distributionApproved=false`。

因此 Stage 258 的代码与远端门禁已经关闭，不再继续围绕同一 M1 marker 邻接扩展。

## 3. 当前机器真实边界

本阶段只读复测得到：

| 入口 | Expected | Actual | 开发影响 |
|---|---|---|---|
| Windows App Runtime | 完整兼容包集合且已知危险组合不存在 | 缺 Main.2 与 DDLM 2.3.1 x64；XAML 3.2.3.0 危险组合仍存在 | M1 外部自动化不启动产品，但不阻塞无系统副作用的功能编码 |
| M1 ExternalAutomation | 准入后创建隔离会话并启动 | `BlockedByIncompleteRuntime / startsProcess=false / createsEvidenceSession=false` | BOX-R1-C/D 物理旅程保持 Pending |
| TASKBAR Host | Host ReadyToLaunch，Guest Ready | `Blocked / mutationAllowed=false / modifiedSystemState=false` | 不在宿主试写，R2B1-B 保持 Pending |
| 现有产品进程 | 独占可丢弃会话 | 检测到既有 LongGrid PID `45524`，复测前后不变 | 不执行第二 DesktopHost 或伪造独占证据 |

这些条件只阻断对应的真实安装、跨进程 UIA、任务栏写入和物理完成声明，不再冻结 Core、正式 App、DesktopHost 内部安全边界内的功能开发。

## 4. 新的唯一主开发队列

当前主开发项改为 **PF-008：方格内视图、排序、滚动与间距**，按可独立演示的用户结果拆分：

1. **PF-008A：视图密度与连续滚动**。用户可以在方格内容中选择至少两档有限视图密度，滚动跨越当前首屏，重启后保留选择；真实文件和引用归属不变化。
2. **PF-008B：扩展排序**。在已有名称排序基础上增加类型与修改时间排序，并明确文件夹优先和稳定次序；真实目录内容不变化。
3. **PF-008C：自定义顺序与恢复**。只改变 Long方格配置中的引用顺序，提供预览、保存失败补偿和一次撤销；不得移动真实文件。

每个子项必须先复读真实实现，冻结用户入口、状态和非目标，再通过正式产品链实现。若当前 Runtime 阻断可见 WinUI 旅程，仍应使用真实 Core/配置/文件系统/DesktopHost HWND 测试完成工程闭环；只能标记 `EngineeringComplete / ProductEvidencePending`，不得伪报产品完成。

PF-008 后的功能顺序为 PF-009 搜索/筛选、PF-010 撤销与操作历史、PF-011 首次启动体验，再进入自动整理、Portal、Tab 和布局场景。不得用新的纯安全阶段插队。

## 5. 外部门禁并行队列

以下事项持续跟踪，但不占用唯一主开发项：

- BOX-R1-C/D 与 M1：等待 #23/#274、完整兼容 Runtime、受保护签名包和独占可丢弃 Windows 会话；条件满足时优先安排完整两分钟物理旅程。
- TASKBAR-R2B1-B：只有 Stage 216 Host `ReadyToLaunch` 且 Guest `GuestReady` 时，才在 Guest 执行 Clear/SystemDefault 原生效果；宿主始终禁止试写。
- 发布：签名、许可证、安装、升级和卸载门禁继续阻止公开分发，但不阻止安全边界内的功能工程开发。

## 6. 功能优先测试规则

每个功能切片必须记录 Expected、Initial Actual、Difference、Correction 和 Final Actual。测试优先证明用户结果，而不是增加测试数量：

1. 正式 Core/配置/Infrastructure 行为测试；
2. 真实文件系统或真实 HWND/DesktopHost 测试；
3. 当前机器可安全运行时的正式 App 可见操作；
4. 自动化、覆盖率、漏洞和既有安全门禁作为回归底线；
5. 只有功能实际涉及权限、系统写入、用户文件或发布时，才增加对应专项安全测试。

禁止用 Mock、静态字符串、AutomationId 或文档数量声明功能完成；也禁止为了补齐非当前功能的安全覆盖率而扩大本轮范围。

文档新鲜度合同首轮 Actual：PowerShell 7 通过，但 Windows PowerShell 5.1 将脚本中新加入的中文检查常量按 ANSI 误读，返回 `readme-continuation:missing`；这不是文档内容缺失。Correction：合同继续保持纯 ASCII，以 `FunctionFirst` 作为机器令牌，中文文档同时保留“功能优先（FunctionFirst）”。修正后 Windows PowerShell 5.1、PowerShell 7 和 Action-pin 聚合入口均为 Pass；旧 README Stage 226 和旧计划 Stage 258 负向变体均被精确拒绝，`modifiesSystemState=false`。

## 7. 开发目标与需求对齐审计

开发目标审计：Stage 258 的精确 main 结果已收口；当前主队列已从无限等待外部环境和连续 M1 安全邻接修正，转为 PF-008 用户可见功能开发。

需求对齐审计：产品三根核心支柱、零惊吓、本地优先、不注入 Explorer、任务栏可恢复和不可公开分发的边界没有降低。变化只发生在开发优先级：安全从连续目标变为功能实现过程中的验收底线。

完成度审计：M1/M2 仍为 `0/2 Complete`，30 项 PF 仍为 `0 Complete`；PF-001～PF-007 仍为 `EngineeringComplete / ProductEvidencePending`，PF-008 进入 `InProgress`。本阶段没有修改产品代码、用户文件、Runtime、任务栏或系统设置。
