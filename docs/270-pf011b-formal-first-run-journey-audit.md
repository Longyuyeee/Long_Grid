# Stage 270：PF-011B 正式首次旅程、恢复与匿名遗留退出审计

日期：2026-09-01

输入基线：`origin/main@850a63c`（PF-011A / PR #353 已合入）

状态：`PF-011 EngineeringComplete / RealFilesystemPass / ProductEvidencePending`；下一主开发项为 PF-020A 规则模型、解释性预览与安全引用分配

## 1. 本阶段交付结论

PF-011B 已把 PF-011A 的进程内 dismiss 收敛为正式、可恢复的首次旅程。`ProductBoxesSettings` 现在持久化 `NotStarted / CustomizeInProgress / Skipped / Completed` 四个有限状态；旧 v1 设置缺少该字段时保持 `NotStarted` 且加载时不重写，损坏设置使用 `Skipped`，避免反复弹出无法可靠保存的引导。

用户现在可以查看真实目录建议、返回选择、自定义创建或以后再说。自定义不建立第二套练习数据，而是保存 `CustomizeInProgress` 后直接导航到正式方格管理并聚焦正式名称编辑器；跳过和重新运行均通过同一设置 Store 原子保存。首次真实方格只有在工作区保存成功后才写入 `Completed`；已有持久化方格的旧用户会迁移到完成态，重启不会重复创建 Quick Start 建议。

隐藏匿名方格、匿名三项引用和拖放语义练习的 XAML、事件处理与 UI 契约已删除。正式入口继续只建立安全引用，不新增账户、联网、文件移动、权限或安全邻接阶段。

## 2. Expected、Initial Actual、Difference、Correction、Final Actual

| 检查 | Expected | Initial Actual | Difference | Correction | Final Actual |
|---|---|---|---|---|---|
| 可恢复旅程 | 跳过、自定义、完成和重新运行跨重启保持 | 首个红测 `Assert.NotNull()` 失败，状态属性不存在，`0/1` | `_quickStartDismissed` 只在当前进程有效 | 设置模型与 Controller 增加有限状态和原子变更 | 真实 Store 多次重启依次恢复四种状态 |
| Customize | 不创建练习数据，进入正式创建链 | “从空白开始”只隐藏页面 | 没有真实接续动作且重启丢失 | 先持久化 `CustomizeInProgress`，再导航并聚焦正式创建编辑器 | UI 契约固定正式导航；保存失败留在原页且零创建 |
| 跳过/返回/重跑 | 都可理解、可逆且不误提交 | 没有正式跳过、返回或重跑 | 用户无法控制首次旅程生命周期 | 增加以后再说、预览返回、重新运行 | 跳过/重跑持久化；返回只改 UI、零配置提交 |
| 完成态 | 第一个真实方格保存成功后完成，失败不假完成 | 没有完成态；早期实现审计又发现若在提交接受时完成会早于真实保存 | 保存失败可能导致空工作区却永久跳过引导 | 用 save revision 等待 `Saved`，补偿为空时清除等待；已有持久化方格直接迁移 | 成功保存后 `Completed`，重启不重复建议 |
| 匿名遗留 | 不保留第二套产品语义 | 隐藏 XAML/code-behind 仍包含匿名方格、三引用与拖放练习 | 正式能力与练习状态并存 | 删除匿名控件、处理器和旧 UIA 断言 | 194-ID 契约通过且源代码无该遗留 |
| 真实副作用 | 只写设置，不改变无关用户文件 | 尚无旅程状态真实磁盘测试 | 仅接口存在性不能证明重启与文件效果 | 临时目录真实落盘，中文旁路文件逐字节哈希 | `NotStarted → CustomizeInProgress → Skipped → NotStarted → Completed`，旁路文件 SHA-256 不变 |

## 3. 真实测试与未冒充的证据

- PF-011B 聚焦真实测试 `4/4`：真实 `settings.json` 落盘、多次全新 Store 重启、重复写一次、IO 失败回滚、旧 v1 不重写、损坏配置不重开引导；Expected/Actual 的差异为 `None`，实测链路约 `277 ms < 10 s`，中文旁路文件未变化。
- 正式 App Release：`0 warning / 0 error`。
- UI 源码合同：`outcome=Pass`，必需 AutomationId 从 209 收敛为 `194`；减少来自匿名练习退出，不是能力回退。
- 完整 Release 首轮实际为 `1,468/1,469`：既有 `RealWorkerPixelsFlowIntoRealDesktopHostHwnd` 在 3 秒等待处失败；独立三次为 `Pass / Fail / Pass`，最终全量复跑 `1,469/1,469`。判定为既有真实 worker/HWND 时序不稳定；本阶段未放宽无关预算，且保留首次失败而不是只报告末次通过。
- 正式跨进程 UIA 在产品启动前因本机缺少 Windows App Runtime Main/DDLM 包而失败关闭。源码合同通过不等于物理键盘、Narrator、DPI 或真人任务通过。
- `dotnet format --verify-no-changes` 与 `git diff --check` 通过。

PF-011 的 10 秒选择由真实设置链路和静态产品入口覆盖，但当前机器无法完成跨进程 UIA；“5 位新用户中至少 4 位在 2 分钟内完成首个方格和三个引用”、5/5 安全引用理解、Narrator、物理键盘、窄窗口与 100%～400% DPI 仍为 `ProductEvidencePending`，因此不标记产品 `Complete`。

## 4. 开发目标与需求对齐审计

开发目标已对齐：Quick Start 和 Customize 现在都进入真实工作区；跳过、返回、重跑和完成态有明确恢复语义；匿名练习退出。设置写入复用既有原子 Store，正式创建复用既有 reducer、SaveController 和统一历史，没有新增第二套工作区或撤销实现。

需求优先级已对齐“核心工程实现 → 核心用户旅程 → 功能广度对标”。本阶段没有用权限、安全或证据探针替代功能；文件零变化、保存失败回滚和产品证据诚实性继续作为功能底线。严格完成度仍为 M1/M2 `0/2 Complete`、30 项 PF `0 Complete`；PF-011 只能标记 `EngineeringComplete / RealFilesystemPass / ProductEvidencePending`。

## 5. 唯一接续开发点

下一步进入 **PF-020A：规则模型、解释性预览与安全引用分配**：

1. 建立版本化规则模型，首批条件为类型、扩展名和名称匹配，动作只分配 Long方格安全引用；
2. 在正式 UI 中编辑、启用、排序规则，并在保存前展示真实匹配、冲突和目标方格；
3. 规则预览必须消费真实 Catalog 元数据，不读取文件正文，不自动移动或删除文件；
4. 先验证预览零提交，再以一次原子配置事务应用，并预留 PF-021 的统一历史接线；
5. 使用真实 Unicode 临时目录记录 Expected、Initial Actual、Difference、Correction、Final Actual 及文件 SHA-256；
6. 阶段结束继续做开发目标审计、需求对齐、相关文档更新、提交、推送和 CI 收口。

PF-011 的真人/物理证据继续作为并行门禁；环境不满足时保持 Pending，不再追加同类邻接探针，也不冻结 PF-020A 功能开发。
