# Stage 269：PF-011A Quick Start 真实建议预览与原子提交审计

日期：2026-09-01

输入基线：`origin/main@d427dff`（PF-010B3 / PR #352 已合入）

状态：`PF011A EngineeringComplete / RealFilesystemPass / ProductEvidencePending`；PF-011 继续由 PF-011B 收口 Customize、跳过/返回与完成态恢复

## 1. 本阶段交付结论

PF-011A 已把正式首次整理主路径从“4 个匿名引用、仅内存练习”升级为真实桌面第一层目录建议。Core 计划器只消费已有 `ProductDesktopCatalogSnapshot` 元数据，不打开或读取文件内容；预览包含真实显示名、项目类型、候选总数和 256 项上限。正式 App 只在空工作区显示首次整理，用户先查看真实项目列表，再点击“确认并创建”。

确认后，创建“桌面项目”方格与加入全部建议引用只调用一次 reducer 和一次保存提交，并形成一个 `QuickStart` 统一历史项。提交协调器同时绑定工作区 fingerprint、edit revision、catalog generation 和所选目录 fingerprint；目录或工作区在预览后变化时有限拒绝，不猜测应用。保存失败可复用内部批量加入 token 整单补偿，撤销/重做仍走唯一的 50 步历史。

“从空白开始”只关闭当前首次整理表面，不创建方格、不提交配置。真实文件始终保持原路径和 SHA-256；本阶段没有新增文件移动、改名、删除、账户、联网或权限开发。

## 2. Expected、Initial Actual、Difference、Correction、Final Actual

| 检查 | Expected | Initial Actual | Difference | Correction | Final Actual |
|---|---|---|---|---|---|
| 正式 Quick Start 能力 | 存在真实目录建议计划器 | 首个红测 `Assert.NotNull()` 失败，目标类型为 `null`，`0/1` | 生产代码只有匿名 UI 原型 | 新增只读建议快照、真实目录选择与有限状态 | 真实 Unicode 文件建议专项通过 |
| 预览内容 | 显示真实项目且不读正文 | 原型固定显示“4 个匿名引用” | 与真实 Catalog 隔离 | App 复用权威 Catalog，列表只显示名称与类型 | 两个真实文件精确显示，正文与 SHA-256 不变 |
| 原子提交 | 一个方格和全部引用只形成一次编辑/保存/历史 | 原型只改进程内 UI，没有配置提交 | 用户无法得到真实方格 | 一次 `CreateContainer` 写入方格和引用，一次 SaveController submit，一条 `QuickStart` 历史 | 真实 Store 重载为 1 个方格、2 个引用、save revision=1 |
| 陈旧与取消 | 未确认零保存，确认时拒绝旧预览 | 没有可提交 token，也没有代次复核 | 无法区分取消与过期 | 绑定 revision、generation 和双 fingerprint | 取消、旧 catalog、旧 revision、内容变化均零提交且有限返回 |
| 撤销与失败补偿 | 整次首次整理一步撤销/重做，失败不留假成功 | 首次真实测试在撤销前错误调用 controller `CompleteAsync`，Redo `Actual=False` | 测试终止了保存生命周期，不是产品事务失败 | 先等待 `Saved`，完成 undo/redo 后再排空；失败保存走整单补偿 | undo→redo 通过；fail-once 后恢复空工作区且历史为空 |
| 产品入口 | 空工作区直接出现真实选择，不再停在匿名练习 | 导航处理器固定 `FirstRunPanel=Collapsed` | 原型永久不可见 | 空工作区 + 有限 Catalog 状态驱动首次整理；匿名练习卡退出可见表面 | Release 编译通过，209-ID 合同通过；物理键盘/Narrator 仍 Pending |

## 3. 真实测试结果

- Initial Actual 红测：`ProductQuickStartSuggestionPlanner` 反射结果为 `null`，精确失败 `Assert.NotNull() Failure: Value is null`，`0/1`。
- 真实文件专项：系统临时目录实际创建 `实际桌面/项目甲.txt`、`项目乙.txt`、`演示文稿.pptx`；验证真实名称、配置 Store 落盘/重载、取消、三类陈旧拒绝、统一历史 undo/redo 和 fail-once 整单补偿，`5/5`。
- 文件效果：每个场景均以真实字节计算 SHA-256；Expected 为不变，Final Actual 全部严格相等，正文也保持不变。
- 完整 Release：`1,465/1,465`，0 failed，0 skipped。
- 正式 App Release：`0 warning / 0 error`。
- UI 源码合同：`outcome=Pass`，必需 AutomationId 由 207 增至 `209`，新增真实建议列表和确认按钮。
- 格式与差异：`dotnet format --verify-no-changes`、`git diff --check` 通过。

本阶段没有把源码合同冒充物理产品证据。首次启动 10 秒预算、真实点击、键盘、Narrator、100%～400% DPI、窄窗口以及 5 人任务成功率仍属于 PF-011B/M1 产品证据。

## 4. 开发目标与需求对齐审计

开发目标审计：PF-011A 的四个核心结果均已落到真实代码：权威目录只读建议、确认前零写入、确认后单次原子配置提交、统一历史和失败补偿。实现复用了现有 Catalog、Reducer、SaveController 与 History，没有建立第二套存储或撤销系统。

需求对齐审计：本阶段直接推进“核心工程实现 → 核心用户旅程”，没有把精力扩张到新权限或安全设施。Quick Start 只建立安全引用，不移动真实文件，符合零惊吓产品承诺。PF-011A 标为 `EngineeringComplete / RealFilesystemPass / ProductEvidencePending`，不能据此把 PF-011 或产品里程碑标为 Complete；M1/M2 仍为 `0/2 Complete`，30 项 PF 仍为 `0 Complete`。

## 5. 唯一接续开发点

下一步只进入 **PF-011B：Customize、跳过/返回与完成态恢复**：

1. 将“从空白开始”从仅当前进程 dismiss 升级为可恢复的 Customize 路径，直接复用正式创建第一个方格与添加引用能力；
2. 完成跳过、返回、取消和“以后重新运行”，不自动创建方格；
3. 定义并持久化首次旅程完成态，崩溃/重启不重复创建同一建议；
4. 收敛仍隐藏在 XAML/code-behind 中的匿名练习遗留，不保留第二套产品语义；
5. 用真实空配置、真实 Unicode 桌面项目和真实重启验证 10 秒选择、2 分钟首个方格/三个引用、Expected/Initial Actual/修正后 Actual 及文件 SHA-256 不变；
6. 阶段结束继续完成目标审计、需求对齐、文档、推送和 CI 收口。

PF-011B 收口前不提前进入 PF-020/021 规则系统，也不新增与首次启动旅程无关的权限或安全工作。
