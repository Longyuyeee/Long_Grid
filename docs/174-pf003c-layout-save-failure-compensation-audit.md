# Stage 174：PF-003C 布局保存失败补偿审计

- 审计日期：2026-08-21
- 开发分支：`codex/pf002d-create-preview`
- 对应目标：PF-003 保存失败时完整恢复旧位置和尺寸，并禁止旧失败覆盖后续编辑
- 结论：**PF-003C 工程门通过；真实写租约故障下已闭环旧盘保持、内存补偿、补偿重试和重载误差。正式 DesktopHost 输入、键盘微调、跨显示器与物理/UIA 产品证据仍未完成，PF-003 保持 `InProgress`。**

## 1. 开发前目标审计

Stage 173 已实现手势完成后的唯一配置提交，但异步保存失败时，内存状态仍会暂时保留新 placement，磁盘保持旧 placement。若没有补偿，用户当前看到的位置与重启后位置不一致；若直接套用 PF-002E 的“删除新容器”，又会错误删除整个方格。

本切片要求：

1. 布局提交签发绑定 operation、容器、工作区 revision、保存 revision、topology 和原/新 placement 的发布凭据；
2. 只在同次保存失败、同一工作区 revision、同一保存 revision 且当前 placement 仍为本次新值时允许补偿；
3. 工作区、保存代次或 placement 任一变化都返回 `Superseded`，不得覆盖后续用户编辑；
4. 补偿必须复用现有 reducer、projection 和 save controller，不建立旁路写盘；
5. 补偿保存仍失败时保留有限重试，解除真实故障后把旧 placement 写回磁盘；
6. 不读取真实桌面，不移动、删除或修改桌面文件。

## 2. 实现

新增 `ProductWorkspaceContainerLayoutPublication`：

- 有限决策为 `AwaitingSave / Published / CompensationRequired / Superseded`；
- 失败判定同时核对工作区 revision、保存 revision 和完整 placement；
- placement 比较包含显示器、X/Y/宽/高和扩展字段；
- 保存成功只在 `SavedRevision` 精确匹配时认定 `Published`。

统一 `ProductWorkspaceCommitCoordinator` 现在：

- 在布局提交被保存控制器接受后签发发布凭据；
- 保存协调器内部保留同一凭据对象和独立的原/新 placement 深拷贝；补偿时执行对象身份核对，字段相同但外部复制/伪造的 token 返回 `InvalidRequest`，公开 token 的扩展字典即使被原地修改也不会改变私有恢复事实；
- `CompensateContainerLayoutGesture` 直接读取权威 `saves.Snapshot`，调用者不能注入旧的失败快照；
- 只有 `CompensationRequired` 才把 placement 恢复为冻结原值并提交一个新的保存 revision；
- 补偿接受后凭据立即失效，重复调用返回 `Superseded`；
- `Published` 或事实已变化时清除旧凭据，不保留可晚到执行的恢复意图。

## 3. 真实测试：预期—实际—差异

专项测试先用真实 `ProductConfigurationStore` 保存 X=100/Y=100 的旧配置，再对正式 `WriteLeasePath` 建立 `FileShare.None` 独占句柄。正式 workflow/controller 因真实 Windows 文件锁进入 `WriteLeaseUnavailable`，不是 fake workflow 返回失败。

| 时点 | 预期 | 实际 | 差异/处理 |
| --- | --- | --- | --- |
| 布局提交后 | 内存为 200/150 | X=200、Y=150 | 无 |
| 首次保存失败 | 失败有限且旧盘不变 | `WriteLeaseUnavailable`；磁盘 100/100 | 无 |
| 发布判定 | 同次失败要求补偿 | `CompensationRequired` | 无 |
| 伪造同字段 token | 不得恢复 | `InvalidRequest`，零额外修订 | 无 |
| 修改公开 token 扩展字段 | 不得污染恢复状态 | 补偿后的 `marker` 仍为 `original` | 末轮审计补齐私有深拷贝 |
| 补偿提交 | 内存恢复 100/100 | X=100、Y=100 | 无 |
| 锁仍占用 | 补偿保存失败但磁盘仍是旧值 | 保存 revision 2 为 Failed；磁盘 100/100 | 无 |
| 重复补偿 | 不二次恢复/写盘 | `Superseded` | 无 |
| 解除锁并重试 | 正式保存恢复状态 | revision 2 Saved | 无 |
| 真实重载 | 位置误差 ≤1 DIP | X/Y 误差均 0 DIP | 无 |
| 桌面文件安全 | 哨兵内容不变 | `must-not-change` | 无 |

首轮 30/30 聚焦测试通过后，安全复审发现仅校验公开 token 字段仍允许构造字段相同的伪造凭据。实现随即增加协调器内部待发布对象身份核对和伪造凭据断言。末轮复审进一步发现同一公开 token 内的扩展字段字典仍可原地变异，因此私有原/新 placement 快照与公开 token 完全分离，补偿只读取私有深拷贝，并增加 `tampered` 不得替代 `original` 的真实断言。最终格式门还真实报告新增测试文件的导入顺序不符合仓库规则，调整为 System-first 后重跑。最终聚焦测试仍为 30/30。上述差异均在提交前修正，没有把首轮通过等同于最终安全通过。

## 4. 需求对齐

| PF-003 要求 | 当前状态 |
| --- | --- |
| 移动/八向缩放、吸附与预览性能 | Engineering Pass（Stage 172） |
| begin/update/cancel/complete 与唯一提交 | Engineering Pass（Stage 173） |
| 保存失败完整恢复旧位置和尺寸 | Real Failure Pass |
| 后续编辑优先、旧补偿不覆盖 | Engineering Pass |
| 补偿重试后真实重载误差 ≤1 DIP | Real Store Pass：0 DIP |
| 正式 DesktopHost 标题栏和八向命中 | Pending PF-003D |
| 方向键 1 DIP / Shift 大步微调 | Pending |
| 跨显示器目标 DPI 转换 | Pending |
| 视觉 P95、真实鼠标/触控、UIA Bounds | PendingProductEvidence |

PF-003 仍为 `InProgress`。Core/Infrastructure 事务闭环不等于用户已经能在桌面拖动方格。

## 5. 验证结果

- PF-003A/B/C 聚焦合同：`30/30`；
- Release 全量测试：`1040/1040`；
- Release solution build：`0 warning / 0 error`；
- 100 方格生产规模预检：2,000 次布局预览 P95 `0.060 ms < 16.7 ms`，真实保存/恢复沙箱清理完成，`readsRealDesktop=false / realFileOperationsAllowed=false`；
- 153-ID 静态 UI 合同：Pass；
- PF-002 正式 App 回归：Pass，外部 Expected/Actual 合同 `Difference=None`，桌面与用户配置元数据不变，临时证据已删除；
- 正式窗口生命周期：两轮 20 秒 Pass，就绪 `2,310 / 2,077 ms`，退出码均为 0，未查询跨进程 UIA；
- 完整跨进程 UIA：已知 WinUI 上游组合继续安全阻断，不强行执行、不伪报 Pass；
- 漏洞、格式和差异检查：Pass，无已知易受攻击包。

## 6. 下一切片

PF-003D 接入正式 DesktopHost 原生布局输入：

1. 标题栏只承载移动，四边和四角提供有限 resize 命中区域；
2. pointer capture 生命周期映射到 Stage 173 会话，move 期间只更新可见内存候选；
3. pointer up 进入唯一提交和本轮补偿链，capture lost/Escape 恢复原 placement；
4. 锁定、revision/topology/host generation 变化显示有限拒绝原因；
5. 同步实现方向键 1 DIP 与 Shift 大步微调的同一事务入口；
6. 先做正式窗口进程内/生命周期证据；跨进程 UIA 继续等待安全运行时，跨显示器另设后续切片。
