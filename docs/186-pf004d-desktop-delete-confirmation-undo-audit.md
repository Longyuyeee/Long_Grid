# Stage 186：PF-004D 桌面删除确认、失败补偿与统一撤销审计

- 日期：2026-08-24
- 分支：`codex/pf002d-create-preview`
- 起始基线：`4fac96c`
- 对齐编号：`PF-004D`
- 结论：`EngineeringComplete`；PF-004 顶层仍为 `InProgress`

## 1. 目标与范围冻结

本阶段关闭桌面方格“更多”菜单中的删除入口，但删除始终只改变 Long方格配置：

- 原生菜单把“删除方格配置…”作为动态有限命令；锁定、只读、保存失败或已有删除发布待定时预先禁用；
- 请求继续绑定 container、display、workspace revision、topology generation 和可信来源事实；
- 正式 App 激活唯一控制中心并显示 ContentDialog，明确真实桌面文件不会被删除、移动或重命名；
- 默认按钮为取消；取消、Esc、窗口冲突、关闭排空和陈旧状态均零提交；
- 用户确认后使用同一请求重新复核方格、显示器、修订、拓扑、锁定和保存状态，再调用正式 Remove 提交链；
- 删除产生现有 `ContainerRemoval` 最近撤销令牌，由唯一统一撤销入口恢复方格和其中引用；
- 异步持久化失败时自动用同一撤销令牌补偿内存状态，重试只持久化补偿后的原状态。

本阶段没有实现真实文件删除/移动，没有加入 Explorer Hook，也没有提前实现规则、Portal 或 Tab。

## 2. 实现审计

### 2.1 原生菜单与确认会话

`ProductDesktopContainerMenuAction` 新增 `DeleteContainerConfiguration`，可用性合同新增 `CanDeleteContainerConfiguration`。原生 `#32768` 菜单使用独立有限命令 ID；只有正式 availability 允许时才启用。

导航控制器接受删除请求时返回经过验证的 container/display/ordinal/edit revision/topology facts。App 第一次验证后显示正式确认窗口；确认完成后不信任旧 ordinal，而是用原始请求和当前产品快照再次调用控制器。任何事实变化均取消，不提交配置。

确认窗口 AutomationId 为 `DesktopContainerDeleteConfirmationDialog`，机器状态明确记录 `Default=Cancel`、revision/topology 和 `DesktopFilesChanged=False`。

### 2.2 删除发布与失败补偿

新增 `ProductDesktopContainerDeleteController`：

1. 只接受已确认且仍指向唯一未锁定方格的删除结果；
2. 复用 `ProductWorkspaceCommitCoordinator.CommitContainer(Remove)`；
3. 绑定删除 edit revision、save revision 和 removal undo token；
4. 保存成功后发布并清理阶段状态；
5. 保存失败时调用 `CommitContainerRemovalUndo` 恢复完整方格与引用；
6. 补偿提交失败时保留有限失败状态，不把磁盘旧值伪报为删除成功。

补偿完成后最近删除撤销令牌被正式消费，因此统一撤销显示 `Unavailable`，避免再次恢复同一个已自动回滚的方格。

## 3. 真实 Expected / Actual

### 3.1 默认取消、确认删除与统一撤销

测试在真实临时目录创建真实配置 Store 和真实 `keep.txt`，内容为 `keep-original`。

| 项目 | Expected | Actual | 差异 |
| --- | --- | --- | --- |
| 默认取消后配置字节变化 | false | false | 无 |
| 默认取消后写入时间变化 | false | false | 无 |
| 确认前二次复核 | `Accepted:Ordinal=1` | `Accepted:Ordinal=1` | 无 |
| 删除后方格数 | 0 | 0 | 无 |
| 统一撤销类型 | `ContainerRemoval` | `ContainerRemoval` | 无 |
| 撤销后方格/引用数 | 1 / 1 | 1 / 1 | 无 |
| 真实文件存在/内容 | true / `keep-original` | true / `keep-original` | 无 |

结构化测试输出：`Difference=None`。

### 3.2 真实写租约失败补偿

测试持有真实 Store 写租约，强制删除保存产生 `WriteLeaseUnavailable`，随后观察自动补偿；释放租约后重试正式保存补偿状态。

| 项目 | Expected | Actual | 差异 |
| --- | --- | --- | --- |
| 来源失败 | `WriteLeaseUnavailable` | `WriteLeaseUnavailable` | 无 |
| 发布状态 | `Compensated` | `Compensated` | 无 |
| 内存方格数 | 1 | 1 | 无 |
| 重试后磁盘方格/引用数 | 1 / 1 | 1 / 1 | 无 |
| 补偿后统一撤销 | `Unavailable` | `Unavailable` | 无 |
| 真实文件内容 | `keep-original` | `keep-original` | 无 |

结构化测试输出：`Difference=None`。

## 4. 测试差异与修正

| 轮次 | 预期 | 实际差异 | 修正 |
| --- | --- | --- | --- |
| 首轮 Release 编译 | 0 warning/error | availability 从三字段扩成四字段后 7 个旧测试构造失败 | 更新生命周期、导航和真实 HWND/UIA 菜单合同；未降低断言 |
| 首轮 UI 合同 | PF-004D 删除启用 | PF-004C 合同仍要求“下一阶段确认”且禁用 | 保留 PF-004C 安全未来项断言，新增独立 PF-004D 默认取消、二次复核、补偿和统一撤销合同 |
| 实现复审 | 保存失败恢复原方格 | 直接复用 Remove 只覆盖提交拒绝，不能证明异步 Store 失败补偿 | 新增绑定 save revision/undo token 的删除发布控制器，并用真实写租约失败验证 |

所有差异都通过实现或合同修正关闭，没有把“菜单可点击”冒充为删除闭环完成。

## 5. 门禁结果

- PF-004D 导航/Store/失败补偿聚焦：`5/5`，两组真实 Expected/Actual 均 `Difference=None`；
- PF-004 相关生命周期和真实 HWND/UIA 聚焦：`51/51`；
- Release 全量：`1099/1099`；
- Release 全解决方案构建：`0 warning / 0 error`；
- 153-ID UI 静态合同及新增 PF-004D 合同：通过；
- 真实原生 `#32768` 菜单：删除项实际显示且 Enabled；
- 已知漏洞门禁：0；
- 正式 Release App：DesktopHost `2,073 ms` 就绪、持续响应 20 秒、第二实例重定向后唯一控制中心、退出零残留进程/零临时配置写入，`Difference=None`；
- 未执行跨进程 WinUI UIA、键鼠注入或真实桌面文件操作。

## 6. 需求对齐与下一步

PF-004D 已满足高风险删除的默认取消、精确绑定、确认后二次复核、真实文件零修改、异步保存失败补偿和统一最近撤销。它对齐 iTop/Fences 的就近删除价值，同时保持 Long方格“删除组织关系不删除文件”的核心差异。

PF-004 顶层仍不能标记 `EngineeringComplete`：Stage 153 还要求重命名、折叠、锁定和外观具备一致撤销语义，并要求可配置的标题双击行为/标题显示策略；物理菜单选择和 Narrator 证据也仍 Pending。下一切片固定为 **PF-004E：标题策略与就近编辑统一撤销收口**，不得跳过这些显式验收项直接宣称 PF-004 完成。

PF-001～PF-003 产品证据继续并行 Pending；30 个 PF 仍为 `0 Complete`，不得用测试数量折算产品完成率。
