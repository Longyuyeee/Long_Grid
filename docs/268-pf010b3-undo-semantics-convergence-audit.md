# Stage 268：PF-010B3 统一撤销用户语义与规则依赖纠偏审计

日期：2026-09-01

输入基线：`origin/main@bc74a5b`（PF-010B2 / PR #351 已全绿合入）

状态：`PF010B3 EngineeringComplete / RealRegressionPass / ProductEvidencePending`；PF-010 工程范围收口，产品物理证据仍 Pending

## 1. 本阶段交付结论

PF-010B3 已将正式 App 的撤销用户语义收敛到唯一的 50 步会话历史。两个 `LatestUndo` 聚合按钮以及布局恢复、方格删除、批量引用加入、引用移除/改归属四个深层单步撤销按钮已从 XAML 产品表面移除；用户现在只通过操作历史的撤销/重做按钮或 Ctrl+Z/Ctrl+Y 导航。正式文案也统一说明批量动作可在操作历史中整体撤销或重做。

旧的一次性 token、selector、内部提交方法和失败保存补偿没有删除。它们继续服务真实 App 的保存失败回滚和既有内部证据链，但不再形成第二套用户命令。权威 UI 合同从 213 个必需 AutomationId 收敛为 207 个。

真实代码审计同时确认仓库没有正式规则模型、规则编辑器、规则预览或规则应用用户动作；只有早期 `FileOrganizationPlanner` 安全计划器、匿名首次整理原型和仍标注“创建规则（后续功能）”的禁用菜单项。PF-010 文档此前要求在 PF-020/021 之前把“规则应用”接入历史，属于依赖倒置。该条不以假 API 或测试桩补齐，而是回归 PF-021 的既有交付门槛：正式规则应用交付时，整次应用必须形成一个事务和一个统一历史项，并完成 apply→undo→redo→undo、保存失败补偿和真实文件零变化测试。

## 2. Expected、Initial Actual、Difference、Correction、Final Actual

| 检查 | Expected | Initial Actual | Difference | Correction | Final Actual |
|---|---|---|---|---|---|
| 唯一撤销语义 | 用户只看到统一历史 Undo/Redo | 新增真实 XAML 合同失败：六个旧按钮仍存在 | 用户面对深层一次撤销和统一历史两套入口 | 删除六个正式按钮及 click handler，保留内部 token/补偿 | 207-ID 合同通过；只有统一历史按钮可见 |
| 内部失败补偿 | 移除按钮不能破坏保存失败恢复 | 首次 Release 构建因删除了内部共用失败文案映射而报 `CS0103` | 用户入口与内部补偿共用了一段纯映射 | 恢复纯失败映射，不恢复按钮或 handler | Release 0 warning / 0 error；内部 token 回归通过 |
| 规则应用依赖 | 只给真实存在的用户动作写历史 | 代码中正式规则动作数量为 0，菜单明确为后续功能 | PF-010 验收早于 PF-020/021 要求规则接线 | PF-010 收口现有动作；规则原子历史保持为 PF-021 强制门槛 | 不制造假规则能力；PF-021 范围和验收均明确保留 |
| 真实文件边界 | 撤销语义变化不触碰用户文件 | 旧内部 token 已有真实文件回归，但 UI 存在旁路语义 | 需证明入口收敛不破坏配置级恢复 | 复跑历史和全部旧 token 回归及完整套件 | 相关 36/36、完整 1,460/1,460；无文件操作新增 |
| 产品物理入口 | 正式 App 可验证 Ctrl+Z/Ctrl+Y 和 Narrator | 本机启动前缺 Main.2 与 DDLM | 不能把源码合同当物理证据 | 真实运行并记录精确缺失组件 | `ProductEvidencePending`，未伪报通过 |

## 3. 真实测试结果

- Initial Actual：`Test-LongGridUi.ps1 -ContractOnly` 首次失败，精确信息为 `Legacy LatestUndo user buttons must be removed after unified session-history convergence.`。
- 修正后的正式 UI 源码合同：`outcome=Pass`，必需 AutomationId `207`；明确要求六个旧撤销 ID 全部不存在，统一历史仍具备 50 步、Undo/Redo、分支截断和保存补偿合同。
- 历史与内部旧 token 真实回归：`36/36`；覆盖 apply→undo→redo、方格删除、布局恢复、引用移除/改归属和 selector 的有限失败。
- 完整 Release：`1,460/1,460`，0 failed，0 skipped。
- Release 全解决方案：`0 warning / 0 error`。
- 本机正式跨进程 UIA：在 App 启动前因缺少 `MicrosoftCorporationII.WinAppRuntime.Main.2 >= 2.3.1.0` 与 `Microsoft.WinAppRuntime.DDLM.2.3.1.0-x6` 失败关闭；没有进程级产品证据。

## 4. 开发目标与需求对齐审计

开发目标审计：PF-010A/B1/B2 已交付统一 50 步历史、动作广度和重启安全恢复点；PF-010B3 已删除所有正式单步撤销用户按钮，统一用户语义，同时保留失败保存需要的内部 token。PF-010 当前可实现的工程范围已经闭环。

需求对齐审计：本阶段直接改善核心整理旅程，没有扩张权限或安全工作。规则能力没有被虚假标记为完成；真实规则模型、编辑器和应用仍按 PF-020/021 开发，PF-021 必须复用本统一历史。PF-010 保持 `EngineeringComplete / ProductEvidencePending`，不升级为产品 `Complete`；M1/M2 仍为 `0/2 Complete`，30 项 PF 仍为 `0 Complete`，产物不可公开分发。

## 5. 唯一接续开发点

下一步只进入 **PF-011A：Quick Start 真实只读建议预览与原子提交**：

1. 审计当前匿名首次整理原型与正式 catalog/workspace 的隔离点；
2. 以真实桌面第一层只读元数据生成可解释建议，不读取文件内容；
3. 未确认前零配置提交，确认后把创建方格和引用作为一个原子提交；
4. 用真实 Unicode 目录/文件验证 Expected、Initial Actual、Difference、Correction、Final Actual，文件位置和 SHA-256 不变；
5. 保存失败、catalog/revision 变化、中途取消和重启必须不误应用；
6. 阶段结束继续完成目标审计、需求对齐、文档、推送和 CI 收口。

PF-011A 收口前不提前开发 PF-020/021 规则系统，也不新增与当前用户旅程无关的权限或安全工作。
