# Stage 177：PF-003D2 App 布局会话、可见候选与提交补偿审计

- 审计日期：2026-08-24
- 开发分支：`codex/pf002d-create-preview`
- 起始基线：`12ccba1`
- 对应需求：PF-003 正式 App 消费 DesktopHost 布局输入、Surface 内存候选、pointer-up 唯一提交与保存失败补偿
- 结论：**PF-003D2 工程链通过；物理鼠标、键盘微调、跨显示器和 UIA Bounds 仍未完成，PF-003 保持 `InProgress`。**

## 1. 开发目标与边界

Stage 175 已让每显示器原生 Surface 产生 `Begin / Update / Complete / Cancel` 请求，但正式 App 只绑定了 `BindWorkspaceCreate`，没有绑定 `BindContainerLayout`。Core 会话、提交和补偿分别存在，用户链仍是断开的。

本轮只关闭以下组合根：

1. App 绑定正式布局请求；
2. Begin 创建 Stage 173 会话；
3. Update 只更新 Surface 内存候选，不更新配置、不写盘；
4. Cancel 清除候选并保持原状态；
5. Complete 复用 Stage 173 唯一提交和 Stage 174 发布/补偿；
6. Surface 候选精确绑定 display、workspace revision 和 topology generation；
7. 正式 App 证据执行 32/16 DIP 位移并真实保存、重载。

本轮不合成全局物理鼠标，不查询已知不安全组合下的跨进程 UIA，也不实现方向键、跨显示器拖动或最终视觉动效。

## 2. 实现结果

新增 `ProductDesktopContainerLayoutInteractionController`，集中管理：

- 唯一 active gesture；
- 请求身份与 workspace/topology 事实匹配；
- Core begin/update/cancel/complete；
- complete 后唯一 `CommitContainerLayoutGesture`；
- 保存快照的 Awaiting/Published/Superseded/CompensationRequired 判定；
- 真实失败时调用可信 token 的 `CompensateContainerLayoutGesture`。

正式 App：

- 调用 `BindContainerLayout(RequestDesktopContainerLayout)`；
- Update 把候选发送到 lifecycle，不重建配置；
- Surface 拒绝候选时立即 `HostInvalidated` cancel；
- Complete 先清候选，再应用已接受 document；
- 保存事件观察布局 publication，补偿成功后重新发布恢复 document；
- 将安全的 DesktopHost 投影刷新从可能触发已知 WinUI 缺陷的主窗口动态 UI 刷新中拆出，证据模式也保持宿主 revision 最新。

DesktopHost：

- lifecycle 只接受唯一显示器且 revision/topology 精确匹配的候选；
- Surface 以独立内存 projection 绘制候选和焦点轮廓；
- 清除候选后恢复原 projection；
- Passive、Hidden、Dispose 清除未完成候选；
- 极端有限值在进入 GDI 绘制前进行像素换算验证，溢出安全拒绝。

## 3. Expected / Actual / Difference

| 检查 | Expected | 首次 Actual | 修正后 Actual |
| --- | --- | --- | --- |
| 测试初始保存状态 | 使用产品有限枚举 | 测试误写不存在的 `Idle`，CS0117 | 改为真实 `Clean`，聚焦通过 |
| 静态 UI 合同 | 新 App 接线可判定 | 新正则从绑定点起算，无法匹配更早声明的类型 | 锚定正式方法和实例调用，Pass |
| 正式 App Update/Complete | Begin/Update/Complete 均接受 | Begin=true，Update/Complete=false，位移与保存均为 0 | 拆分安全 DesktopHost 投影刷新后全部 true |
| App 内存位移 | X/Y 增加 32/16 DIP | 0/0 DIP | 32/16 DIP |
| 真实 Store 重载 | X/Y 增加 32/16 DIP | 0/0 DIP，save revision=3 | 32/16 DIP，save revision=4 |
| 候选极端值 | 绘制前失败关闭 | 安全复审发现公开 Surface 方法可接收极大有限 double | 先执行 checked 像素换算，拒绝且原 bounds 不变 |
| 桌面/用户配置副作用 | 元数据不变 | 无变化 | 无变化 |

首次 App 失败不是被改成宽松断言，而是保留实际输出并定位到证据模式跳过完整 `ApplyProductWorkspaceSessionViews`，导致 DesktopHost 仍绑定旧 revision。修正后只刷新安全宿主投影，仍不触碰已知不安全的动态 WinUI 可访问树。

## 4. 真实测试证据

### 4.1 真实配置 Store

生产交互控制器在临时正式 Store 中执行：

- Begin 后 revision 不变；
- Update 候选由 100/100 变为 200/150，save revision 仍为 0；
- Complete 后只提交一次，真实 Store 重载为 200/150；
- 桌面哨兵文件内容不变。

真实独占 `.lock` 文件后：

- 布局提交内存进入 200/150；
- 保存真实失败为 `WriteLeaseUnavailable`；
- `ObserveSave` 经 Stage 174 token 恢复内存 100/100；
- 锁占期间磁盘仍为 100/100；
- 解锁重试后正式 Store 重载仍为 100/100，哨兵不变。

### 4.2 真实 Win32 Surface

测试实际创建非零 HWND，把候选 bounds 从 `100,100,200,160` 更新到 `220,180,240,200`，清除后恢复原 bounds。`double.MaxValue` 候选返回 false，bounds 保持原值。

### 4.3 正式 App 外部 Expected/Actual

专用临时配置启动真实 Release App，沿正式 App 方法执行创建、保存、删除、最近撤销后，再驱动布局 Begin/Update/Complete：

- `LayoutBegin/Update/Complete=true`；
- 内存 `LayoutDeltaX/Y=32/16 DIP`；
- 重载 `LayoutPersistedDeltaX/Y=32/16 DIP`；
- `LayoutSavedRevision=4`；
- 外部合同 `Difference=None`；
- 桌面与真实用户配置元数据不变，临时证据已删除。

该证据通过真实 App、正式组合根和真实 Store，但仍是已知不安全 WinUI 组合下隐藏主窗口的进程内驱动，不等于物理鼠标、可见截图或 Narrator 通过。

## 5. 完整门禁

- PF-003 布局聚焦合同：`52/52`；
- Release 全量测试：`1063/1063`；
- Release solution build：`0 warning / 0 error`；
- 100 方格、500 项、2,000 次布局预览 P95：`0.083 ms < 16.7 ms`；
- 153-ID UI 静态合同：Pass，布局合同已加入正式输出；
- 正式 App 外部 Expected/Actual：Pass，`Difference=None`；
- 真实窗口生命周期：`1,744 ms` 就绪、稳定 20 秒、退出码 0；
- 漏洞、格式与 `git diff --check`：Pass；
- 跨进程 UIA：已知上游组合继续失败关闭，未执行、未伪报。

## 6. 需求对齐

| PF-003 要求 | 状态 |
| --- | --- |
| Core 预览/吸附/性能 | Pass（Stage 172） |
| 会话、唯一提交、真实重载 | Pass（Stage 173） |
| 保存失败补偿 | Pass（Stage 174） |
| Surface 九向命中与 capture | Pass（Stage 175） |
| App 消费 begin/update/cancel/complete | Engineering Pass |
| Surface 动态内存候选 | Engineering Pass |
| pointer-up 唯一提交与补偿接线 | Engineering Pass |
| 正式 App 真实保存/重载 | Engineering Pass |
| 方向键 1 DIP / Shift 大步微调 | Pending PF-003D3 |
| 跨显示器 DPI | Pending |
| 物理鼠标/触控、可见截图、UIA Bounds | Pending Product Evidence |

PF-003 仍为 `InProgress`，30 个 PF 项仍为 `0 Complete`。本轮减少了 Stage 176 指出的“底座存在但 App 未接线”偏移，没有改变任务栏和 Widgets 的 P1/P2 顺序。

## 7. 下一切片

PF-003D3：

1. 标题栏焦点下方向键移动 1 DIP，Shift 使用有限大步；
2. Alt+方向键进入八向有限缩放语义，并复用同一事务/补偿入口；
3. revision/topology/锁定变化时清候选并给出有限拒绝；
4. 建立正式 App 键盘 Expected/Actual 与真实重载证据；
5. 随后独立进入跨显示器 DPI 和物理鼠标/UIA Bounds 门。
