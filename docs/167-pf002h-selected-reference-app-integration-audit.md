# Stage 167：PF-002H 已选引用创建方格正式 App 接线审计

- 审计日期：2026-08-20
- 开发基线：`codex/pf002d-create-preview@fff20f2`
- 对应需求：PF-002H“使用 Long方格当前已选引用创建方格”
- 本切片结论：**正式 App 工程链完成，真实磁盘与文件边界通过；正式 UIA 会话被可复现的基线 WinUI 崩溃阻断，PF-002H 记 `EngineeringComplete / ProductEvidencePending`，PF-002 保持 `InProgress`**

## 1. 目标与修正后的事务边界

Stage 166 已完成“新方格 + 引用改归属”的原子 reducer 和保存协调器，但没有用户入口。原计划若直接复用 PF-002E 的“保存失败删除新方格”会产生严重差异：删除新方格时也会删除刚改归属过去的引用配置，不能恢复提交前来源方格。

本切片因此冻结以下完整闭环：

1. 用户只能从 Long方格“管理已加入的引用”列表选择同一方格内 1–256 项；
2. 点击“使用选择创建新方格”后捕获来源序号、有限项目 ID 和完整配置指纹；
3. 请求进入现有唯一 Desktop Create Admission 与 Preview Session，不另建对话框或保存链；
4. Preview 只显示匿名项目数量，不显示路径、目标或持久化身份；
5. 名称编辑、确认前和最终提交前均复核 workspace revision、topology、来源和当前 UI 选择；
6. 选择变化时以 `StaleSelection` 取消，零提交；
7. 确认后只调用一次 `CommitSelectedReferenceContainer`；
8. 同次保存失败使用完整状态恢复令牌，不能退化为只删除新方格；
9. 最近动作显示“撤销使用选择创建方格”，撤销同时恢复方格和引用归属；
10. 全链只改 Long方格配置，不移动、删除、重命名或写入真实文件。

## 2. 正式产品接线

### 2.1 可发现入口和有限快照

正式控制中心已增加 `ProductWorkspaceSelectedReferenceCreateButton`。按钮仅在以下条件同时满足时启用：会话可编辑、选择非空、全部来自同一未锁定方格且数量不超过 256。

`ProductWorkspaceSelectedReferenceCreateSnapshots` 负责捕获和复核：

- 0、重复序号、越界、锁定来源和 257 项均失败关闭；
- 快照只保存本次提交所需的来源、项目 ID 和 SHA-256 配置指纹；
- 当前配置或选择集合与快照不一致时返回 `SelectionChanged`；
- 顺序变化不被误判为集合变化。

### 2.2 唯一 Preview

选择创建使用 `ProductDesktopWorkspaceCreateInputKind.SelectedReferences` 进入已有 admission、候选显示器、原生就地 Preview 和控制中心 fallback。Preview 文案只显示“包含 N 个 Long方格引用”。取消、失焦、替换、窗口关闭、revision/topology/selection 变化均不提交。

### 2.3 保存失败完整补偿与撤销

`ProductDesktopWorkspaceCreatePublicationToken` 可携带完整恢复令牌。匹配的保存失败发生时：

- 普通空方格创建继续执行“删除新方格”补偿；
- 已选引用创建必须调用完整状态恢复，不允许在恢复失败后继续删除新方格；
- revision、save revision 或容器事实变化时仍按 `Superseded` 保护后续编辑；
- 最新撤销选择器把该令牌区分为 `SelectedReferenceContainer`，用户文案不再错误显示为“撤销批量加入”。

## 3. 真实测试：预期、实际、差异与修正

### 3.1 257 个真实文件边界

测试在真实临时目录创建 257 个文本文件，使用正式 `ProductConfigurationStore` 和正式保存控制器。

| 检查项 | 预期 | 实际 | 差异 |
| --- | --- | --- | --- |
| 257 项请求 | 整批拒绝、修订不推进、磁盘保持来源 257 项 | 真实重载为单一来源方格 257 项 | 无 |
| 随后 256 项请求 | 同一事务成功，来源剩 1 项、新方格 256 项 | 真实磁盘重载完全一致 | 无 |
| 真实文件 | 257 个文件内容均保持 `original-N` | 逐文件读取全部一致 | 无 |

既有真实双文件成功/撤销和真实 `.lock` 独占故障测试继续通过：写租约失败时磁盘保持提交前状态，释放后完整恢复，真实文件内容不变。

### 3.2 快照、取消和最近撤销

- 选择快照可检测配置变化、锁定和超限；
- `SelectedReferences` admission 必须携带合法 1–256 项快照，其他输入类型禁止夹带；
- Preview `UserCancelled` 保持快照但不进入提交状态；
- 已选引用创建只暴露专用最近撤销种类，撤销后专用令牌清空。

### 3.3 首次静态合同差异

新增按钮发布一次状态 live-region 事件后，旧门禁把整个文件内同类事件总数写死为 2，首次实际失败。该断言无法区分“选择变化事件”和“动作结果事件”。门禁已修正为至少保留两条选择合同，并对新按钮、唯一 Preview、选择复核和完整恢复路径建立专项静态合同；修正后 147-ID 合同通过。

### 3.4 正式 App 实机差异与基线对照

| 检查项 | 预期 | 实际 | 结论 |
| --- | --- | --- | --- |
| Release 窗口启动 | 出现 Long方格控制中心 | Windows 捕获获得完整现代界面截图 | 渲染通过 |
| UIA 首元素 | 5 秒内发现 `ResponsiveStatusText` | 连续两次未发现 | 失败 |
| UIA 树枚举 | 返回可访问树 | `E_UNEXPECTED`，随后 App 崩溃 | 失败 |
| WER | 无崩溃 | `Microsoft.UI.Xaml.dll`，`c0000005` 或 `0xc000027b / 8001010e` | 阻断 |
| 上一步 `fff20f2` 隔离基线 | 若本次回归，基线应通过 | 基线同样能截图，但读取可访问树后以相同模块/异常崩溃 | 非本切片回归 |

隔离基线在独立 worktree 构建并验证后已清理。此结果只能证明本次接线没有引入该 UIA 崩溃，不能把正式按钮点击、Narrator 或 UIA 证据记为通过。

## 4. 验证结果

- PF-002H/Admission/最新撤销聚焦测试：`21/21` 通过；
- Release 全量测试：`1010/1010` 通过；
- Release 构建：`0` 警告、`0` 错误；
- 静态 UI 合同：`147` 个 AutomationId，`Pass`；
- `dotnet format --verify-no-changes`：`Pass`；
- `git diff --check`：`Pass`；
- 正式 App 截图渲染：`Pass`；
- 正式 App UIA：`Fail / BaselineReproduced`，不得标记 Pass。

## 5. 需求对齐和下一步

| PF-002H 验收项 | 当前状态 |
| --- | --- |
| Long方格自有显式选择 | 已接入正式列表与按钮 |
| 0/1/256/257、重复、锁定 | 自动化及 257 个真实文件边界通过 |
| 唯一 Preview、匿名数量、取消 | 工程链和合同通过；实机 UIA 待补 |
| 选择/revision/topology 变化取消 | 工程链和自动化通过 |
| 原子创建和改归属 | 真实磁盘重载通过 |
| 保存失败完整撤回 | 真实故障恢复 + App 完整令牌接线通过；正式点击故障注入待补 |
| 一次撤销及正确文案 | 工程链通过；正式 UIA 待补 |
| 不移动真实文件 | 257 个真实文件逐项验证通过 |

下一步先建立不触发当前 WinUI UIA 崩溃的可复核正式 App 会话，或升级/隔离 Windows App Runtime 后重跑按钮—Preview—取消—确认—保存失败—撤销全链。PF-002D/拖画的物理输入证据与 PF-002F Narrator、高对比、缩放和 DPI 矩阵一并收口。证据未取得前，PF-002H 只能记 `EngineeringComplete / ProductEvidencePending`，PF-002 不得升级为 Complete，也不得进入 PF-003。
