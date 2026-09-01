# Stage 262：PF-008C 自定义引用顺序与恢复审计

日期：2026-09-01

输入基线：`origin/main@c40bc81`（PF-008B / PR #345 已全绿合入）

状态：`PF008C EngineeringComplete / RealFilesystemPass / ProductEvidencePending`

## 1. 本阶段交付结论

PF-008C 已把 Long方格持久化引用的相邻上移/下移接入正式 reducer、edit revision、配置投影、原子保存和最近一次编辑撤销链。控制中心只在用户单选一个可编辑的持久化引用且存在相邻持久化引用时启用动作；点击后先展示移动方向、相邻项目和保存后位置，用户确认才提交。刷新后按本次目标位置保持所选项目，不会错误选择旧序号上的相邻引用。

排序只交换 `ProductContainerState.Items` 中的配置引用，不调用文件移动、删除、重命名或绑定目录写入。绑定文件夹直属内容仍是 `BoundFolder` 运行时投影，不进入自定义顺序候选，也不被写入配置。

本阶段复用 `ProductWorkspaceContainerEditUndo`，新增有限 `ReferenceOrder` 种类。提交获得一次 undo token；真实保存失败时 App 复用既有 pending edit 补偿，立即提交恢复状态。补偿保存若也受同一写租约阻断，释放租约后由保存控制器重试恢复文档，不把失败后的预览顺序留在内存或磁盘。

## 2. Expected、Initial Actual、Difference、Correction、Final Actual

| 检查 | Expected | Initial Actual | Difference | Correction | Final Actual |
|---|---|---|---|---|---|
| 自定义顺序重载 | `item-1, item-3, item-2` | `item-1, item-2, item-3` | 当前只能按原配置次序保存，没有引用重排操作 | 新增相邻移动 reducer 与正式 commit action | 重启重载与 Expected 完全一致 |
| 用户入口 | 单选后预览上/下移目标，确认才保存 | 无自定义顺序入口 | 用户不能建立自己的配置顺序 | 新增两个默认禁用、边界感知的 UIA 按钮和确认预览 | 200-ID 合同通过；取消零提交 |
| 保存失败 | 写入失败后恢复原顺序并保留可重试文档 | 不存在可失败的顺序提交 | 自定义顺序尚未进入保存/补偿链 | 复用 `ReferenceOrder` undo token 与 pending edit 补偿 | 真实独占写租约得到 `WriteLeaseUnavailable`；恢复和重试后磁盘为原顺序 |
| 一次撤销 | 成功排序后可撤销一次，第二次失败关闭 | 无排序 undo token | 最近动作无法描述或恢复排序 | `ReferenceOrder` 纳入统一 ContainerEdit undo | 首次撤销 Accepted，第二次 Unavailable |
| 真实文件 | 路径、数量、内容 SHA-256 全不变 | 原顺序往返本就不修改文件 | 需证明新增操作没有旁路文件写入 | 两组真实 Unicode 文件分别覆盖成功与失败补偿 | 前后 inventory 完全一致 |

## 3. 真实测试结果

- 初始真实差异测试：`1/1` 精确失败，xUnit 输出 `Expected=[item-1,item-3,item-2]`、`Actual=[item-1,item-2,item-3]`。
- 修正后 reducer、提交、重载、撤销、失败补偿和最近撤销专项：`53/53`。
- 真实成功旅程：三个 Unicode 文件作为正式引用，经原子提交、真实配置 Store 保存和重新加载得到自定义顺序；路径、数量、SHA-256 不变。
- 真实失败旅程：另三个 Unicode 文件和真实配置 Store；独占 `.lock` 产生 `WriteLeaseUnavailable`，磁盘保持旧顺序；补偿恢复内存，释放租约后重试保存旧顺序；文件 inventory 不变。
- 完整解决方案测试：`1,416/1,416`，0 failed，0 skipped。
- Release 构建：`0 warning / 0 error`；格式验证和 `git diff --check` 通过。
- UI 工程合同：`200` 个唯一 AutomationId，PF-008C 预览、来源排除、正式提交和补偿合同通过。正式跨进程 UIA 仍在启动前因本机缺完整兼容 Windows App Runtime Main/DDLM 包而失败关闭，没有用工程合同冒充物理产品证据。

## 4. 开发目标与需求对齐审计

开发目标审计：PF-008C 要求的配置内自定义顺序、确认前预览、原子保存、真实保存失败补偿和一次撤销均已实现；没有把顺序写入绑定目录，没有移动真实文件，也没有提前实现 PF-009。

需求对齐审计：本阶段直接补齐用户可操作的排序能力，复用成熟事务链而没有扩张权限或安全工程。上移/下移比只支持鼠标拖动更适合键盘与辅助技术；未来可增加拖动手势，但必须调用同一 reducer/commit，不得再建旁路事务。

完成度审计：PF-008A/B/C 均达到 `EngineeringComplete / RealHwndPass or RealFilesystemPass / ProductEvidencePending`；PF-008 工程切片收口，但正式 App 的物理鼠标、键盘、Narrator、触控、DPI 与截图证据仍 Pending，因此 30 项 PF 继续为 `0 Complete`，M1/M2 继续为 `0/2 Complete`，产物不可公开分发。

## 5. 唯一接续开发点

下一步只进入 **PF-009A：桌面搜索共用查询模型与正式结果列表**：

1. 复读现有控制中心方格搜索、正式 workspace read model、DesktopHost 项目投影和打开/定位入口；
2. 建立方格名、已批准项目显示名、有限类型与健康状态的共用查询模型，不读取文件内容、快捷方式参数或 URL 页面；
3. 用 500 项正式数据和 Unicode/组合字符建立查询 Expected / Initial Actual / Difference，并记录首屏更新时间；
4. 在控制中心提供所属方格明确的正式结果列表、空查询、无结果、离线和陈旧结果状态；
5. 结束时审计需求、更新文档、执行真实测试并推送独立 PR。

PF-009A 完成前不并行展开 PF-010、PF-011 或新的安全邻接工作；BOX/M1 与 TASKBAR Guest 继续作为并行外部门禁。
