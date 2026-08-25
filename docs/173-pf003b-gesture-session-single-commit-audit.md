# Stage 173：PF-003B 手势会话与唯一完成提交审计

- 审计日期：2026-08-21
- 开发分支：`codex/pf002d-create-preview`
- 对应目标：PF-003 的 begin/update/cancel/complete 生命周期与结束时一次配置提交
- 结论：**PF-003B 工程门通过；连续预览零写盘、取消恢复、陈旧状态失败关闭、完成唯一提交和真实重载误差已经闭环。保存失败后的可见布局补偿、DesktopHost 原生输入、键盘微调、跨显示器和物理/UIA 产品证据仍未完成，PF-003 保持 `InProgress`。**

## 1. 开发目标与边界

本切片承接 Stage 172 的纯内存布局策略，不扩大系统权限，不直接操作 Explorer 桌面文件，也不把 Core 会话当成可见产品完成。目标固定为：

1. begin 冻结容器、原 placement、edit revision、topology generation 和 display；
2. update 使用相对初始位置的累计 delta，只发布内存候选，不调用保存；
3. Esc/捕获丢失可显式 cancel，revision/topology/display/锁定变化自动取消并恢复原候选；
4. complete 重新验证冻结事实，只产生一个不可由外部构造的完成凭据；
5. 统一提交协调器验证原 placement 未被并发修改，再通过现有 reducer、projection 和 save controller 提交；
6. 重复提交依靠同一权威 edit revision 失败关闭，不建立第二套修订号。

本轮不接 DesktopHost 指针命中，不宣称视觉 P95、真实鼠标或 UIA Bounds；保存异步失败后的可见状态补偿也留给 PF-003C。

## 2. 实现事实

新增 `ProductWorkspaceContainerLayoutGestureSession` 状态机：

- `Begin` 复用 Stage 172 生产策略做零 delta 准入；
- `Update` 接收累计 delta，成功时只更新会话快照和计数；
- 输入事实陈旧或无效时转为 `Cancelled` 并恢复冻结 placement；
- `Cancel` 幂等，完成后不能再次取消为其他状态；
- `Complete` 再执行同一生产策略，零变化不产生提交凭据，完成或取消后再次 complete 返回 `Unavailable`；
- 完成凭据构造器为程序集内部，携带唯一 operation ID、冻结事实、原/最终 placement 和 update 数。

现有 `ProductWorkspaceCommitCoordinator` 新增唯一布局手势提交入口：

- 必须匹配当前 edit revision、唯一容器、显示器和非空完成事实；
- 当前 placement（含扩展字段）必须与冻结原 placement 一致，避免覆盖并发修改；
- 只调用一次 `ProductWorkspaceReducer.UpdatePlacement` 和一次 `saves.Submit`；
- 接受后 edit revision 仅推进一次，并清除其他旧撤销令牌；
- 同一完成凭据的第二次提交因 revision 陈旧而拒绝，保存修订不再增加。

## 3. 真实测试：预期—实际—差异

测试使用真实 `ProductConfigurationStore`、`ProductConfigurationSaveCoordinator`、`ProductConfigurationSaveWorkflow` 和 `ProductWorkspaceSaveController`，配置路径为独立系统临时目录。不是 mock 写盘，也没有读取真实桌面目录。

| 检查项 | 预期 | 实际 | 差异/修正 |
| --- | --- | --- | --- |
| 连续预览 | 1,000 次 update，零保存修订 | update=1,000，保存 revision=0 | 无 |
| 中间磁盘状态 | complete 前配置不存在 | `ProductConfigurationLoadStatus.Missing` | 无 |
| 完成提交 | 只接受一次并推进一个保存修订 | 首次 `Accepted`，保存 revision=1 | 无 |
| 重复提交 | 拒绝且不二次写盘 | `StaleEditRevision`，revision 仍为 1 | 无 |
| 重启/重载误差 | 最终 X/Y 误差 ≤1 DIP | X/Y 均为 0 DIP 误差 | 无 |
| 桌面文件安全 | 测试哨兵文件保持不变 | 内容仍为 `must-not-change` | 无 |
| 陈旧会话 | 自动取消并恢复 100/100/200/160 | 返回 `StaleEditRevision`，原 placement 完整恢复 | 无 |
| 显式取消/重复取消 | 幂等且不能完成 | 两次快照相同，complete=`Unavailable` | 无 |
| 并发布局变化 | 不覆盖较新 placement、零保存 | `StateChanged`，保存 revision=0 | 无 |
| complete 后拓扑变化 | 正式提交前再次拒绝 | `StaleTopology`，保存 revision=0 | 末轮审计补齐提交间竞争窗口 |

开发中的真实差异：加入扩展字段一致性检查后首次编译发现模型公开类型是 `IDictionary`，而实现按 `IReadOnlyDictionary` 接收，产生 CS1503。修正签名为模型的真实合同。末轮复审又发现 complete 与正式 commit 之间仍存在 topology 变化窗口，因此提交入口增加当前 topology generation 二次校验和专项零保存测试；最终聚焦 24/24 通过。这些差异没有被删去或写成首轮通过。

## 4. 需求对齐

| PF-003 要求 | 当前状态 |
| --- | --- |
| 移动/八向缩放与吸附计算 | Engineering Pass（Stage 172） |
| begin/update/cancel/complete 生命周期 | Engineering Pass |
| 指针移动期间只更新内存 | Real Persistence Pass：1,000 次零保存/零配置文件 |
| 拓扑或 revision 变化取消并恢复 | Engineering Pass |
| 结束时一次提交 | Real Persistence Pass |
| 重启后误差 ≤1 DIP | Real Store Pass：0 DIP |
| 不覆盖并发 placement | Engineering Pass |
| 保存失败完整恢复旧位置/尺寸 | Pending PF-003C |
| 正式 DesktopHost 标题栏/八向命中 | Pending |
| 方向键/Shift 大步微调 | Pending |
| 跨显示器目标 DPI 转换 | Pending |
| 视觉 P95、物理输入、UIA Bounds | PendingProductEvidence |

PF-003 仍为 `InProgress`。本轮只关闭生命周期和成功持久化语义，不能用测试数量折算整个功能完成。

## 5. 验证结果

提交前门禁实际结果：

- PF-003A/B 聚焦合同：`24/24`；
- Release 全量测试：`1034/1034`；
- Release solution build：`0 warning / 0 error`；
- 100 方格生产规模预检：2,000 次布局预览 P95 `0.052 ms < 16.7 ms`，真实保存/恢复沙箱清理完成，`readsRealDesktop=false / realFileOperationsAllowed=false`；
- 153-ID 静态 UI 合同：Pass；
- PF-002 正式 App 回归：Pass，外部 Expected/Actual 合同 `Difference=None`，临时证据已删除；
- 正式窗口生命周期：两轮 20 秒 Pass，就绪 `1,717 / 1,948 ms`，退出码均为 0，未查询跨进程 UIA；
- 完整跨进程 UIA：已知 WinUI 上游组合继续安全阻断，不强行执行、不伪报 Pass；
- 漏洞、格式和差异检查：Pass，无已知易受攻击包。

## 6. 下一切片

PF-003C 实现保存失败后的布局事务补偿：

1. 完成提交生成与保存修订绑定的旧/新 placement 补偿事实；
2. 同次保存失败且没有后续编辑时，恢复旧 placement 并排空失败状态；
3. revision、保存代次、容器或 placement 已变化时返回 `Superseded`，不得覆盖后续编辑；
4. 使用真实不可写/锁占配置路径验证预期旧盘状态、实际内存状态、恢复后重载状态和差异；
5. 再进入 DesktopHost 标题栏移动、八向命中与键盘微调；跨显示器与物理/UIA 证据继续作为后续独立门。
