# Stage 178：PF-003D3 键盘布局事务与真实重载审计

- 审计日期：2026-08-24
- 开发分支：`codex/pf002d-create-preview`
- 起始基线：`92f24dc`
- 对应需求：标题焦点键盘移动/缩放、焦点可见、复用 PF-003D2 唯一事务与保存补偿
- 结论：**PF-003D3 工程链通过；真实物理按键、Narrator/UIA Bounds 与跨显示器 DPI 仍未完成，PF-003 保持 `InProgress`。**

## 1. 偏移审计与交互决策

现有显式桌面交互已经把方向键用于项目选择。如果直接改成移动方格，会破坏 Stage 135 已通过的选择与无障碍合同。本轮因此增加独立的“方格标题焦点”，不覆盖原有项目焦点：

1. 进入 Explicit 后，方向键仍按原逻辑导航项目；
2. Tab 进入方格标题焦点，Surface 在标题区绘制焦点框；
3. 标题焦点下，方向键以 1 DIP 移动；Shift 使用 8 DIP 有限大步；
4. Alt+左/右缩小或扩大宽度，Alt+上/下缩小或扩大高度；连续水平/垂直按键可组合对角尺寸变化；
5. 再按 Tab 或 Shift+Tab 返回项目焦点，原选择状态机不变；Esc 仍退出显式交互；
6. Passive、Hidden、Dispose 和交互退出都清除标题焦点。

键盘精调要求数值精确，因此不经过网格吸附。请求仍保留 Shift 事实：普通步使用 `SnapEnabled=false / ShiftPressed=false`，大步使用 `true / true`，依据 Stage 172 的异或策略，两者都明确关闭吸附。

## 2. 实现链

原生 activation HWND 同时处理 `WM_KEYDOWN` 与 Alt 组合实际使用的 `WM_SYSKEYDOWN`，并继续通过 `GetCurrentInputMessageSource` 拒绝注入来源。有限键盘适配器只接受 Tab 和四个方向键，输出 Move、ResizeRight 或 ResizeBottom 与 1/8 DIP 增量；未知键、非标题焦点的方向键继续交给项目选择适配器。

lifecycle 在每一枚已接受按键上重新核对：

- activation source 仍属于当前生命周期；
- Explicit transaction 与请求 container 完全一致；
- display 唯一、方格存在且未锁定；
- 当前 workspace revision 与 topology generation 来自权威 batch；
- delta 有限且不为零。

通过后，单枚键被展开为同一条 `Begin → Update → Complete` 请求。Update 仍只发布 Surface 内存候选，Complete 仍进入 Stage 173 唯一提交、正式 Store 保存和 Stage 174 失败补偿；没有新增坐标直写或第二套保存入口。

## 3. Expected / Actual / Difference 与修正

| 检查 | Expected | 首次 Actual | 修正后 Actual |
| --- | --- | --- | --- |
| 键盘映射编译 | 1/8 DIP 均为 double | 元组混合 double 与 int 零值，CS8506/CS8131 | 零值显式为 double，聚焦 24/24 通过 |
| 原有项目方向导航 | 非标题焦点不被布局适配器消费 | 审计发现方向键合同冲突风险 | 用 Tab 明确切换标题焦点，项目适配器保持原样 |
| Alt+方向键 | 真实 Windows 消息可到达 | 首轮只监听 `WM_KEYDOWN`，Alt 组合通常为 `WM_SYSKEYDOWN` | 两类消息进入同一来源校验和有限映射 |
| 单枚键提交 | 同一 Begin/Update/Complete、同 revision/topology | 未接线 | 三阶段依次接受，非 Shift 吸附 false，大步吸附事实 true/true |
| 正式 App 1 DIP 微移 | 内存和磁盘均 +1 DIP | 未执行 | 内存 +1、重载 +1，save revision=5 |
| 正式 App 8 DIP 扩宽 | 内存和磁盘均 +8 DIP | 未执行 | 内存 +8、重载 +8，save revision=6 |
| 外部副作用 | 桌面/用户配置不变 | 未执行 | 两者元数据不变，临时证据删除，`Difference=None` |

编译差异与 Win32 消息差异都通过修正生产实现解决，没有放宽断言或把模拟结果标成物理输入。

## 4. 真实证据

### 4.1 正式 App 与真实 Store

`Test-LongGridPf002AppEvidence.ps1` 启动真实 Release App 和临时正式 Store。原有创建、删除、最近撤销与 32/16 DIP 布局完成后，继续执行 PF-003D3 键盘语义事务：

- `KeyboardMoveBegin/Update/Complete=true`；
- `KeyboardFineMoveDeltaXDip=1`；
- `KeyboardMoveSavedRevision=5`；
- `KeyboardResizeBegin/Update/Complete=true`；
- `KeyboardLargeResizeDeltaWidthDip=8`；
- 重载差值分别为 X `+1`、Width `+8`；
- 最终 save revision=6，外部 `Difference=None`。

这是正式 App、正式事务控制器和真实磁盘重载证据；驱动的是与键盘映射相同的有限语义请求，不等同于操作者按下物理键。

### 4.2 原生窗口与生命周期

生产 Surface 测试实际创建非零 HWND；标题焦点只能应用到 Explicit 状态下唯一且未锁定的方格。生命周期测试证明标题焦点被路由到对应显示器 Surface，单枚 Move 键产生严格三阶段请求，并保留 revision=7、topology=11。真实窗口 smoke 为 `2,329 ms < 10 s` 就绪、稳定 20 秒、退出码 0，`Difference=None`。

### 4.3 失败边界

注入来源继续在 activation source 被拒绝；未知键不进入布局；非标题焦点方向键继续选择项目；锁定、目标不一致、非有限/零 delta、陈旧生命周期均拒绝。Update 或 Complete 被拒绝时补发 `HostInvalidated` Cancel，避免残留 active gesture。保存失败继续复用 Stage 174 已通过的真实 `.lock` 写租约失败补偿，不复制新的恢复逻辑。

## 5. 完整门禁

- PF-003D3 聚焦：`27/27`；
- Release 全量：`1068/1068`；
- Release solution build：`0 warning / 0 error`；
- 正式 App/Store Expected/Actual：Pass，`Difference=None`；
- 100 方格、500 项、2,000 次布局预览 P95：`0.055 ms < 16.7 ms`；
- 153-ID UI/结构合同：Pass；
- 真实窗口：2,329 ms 就绪、稳定 20 秒、退出码 0；
- `dotnet format --verify-no-changes` 与 `git diff --check`：Pass；
- live 跨进程 UIA：本机已知 Windows App Runtime 2.4.0.0 / Microsoft.UI.Xaml 3.2.3.0 崩溃组合在启动前安全拒绝；ContractOnly 通过，不升级为真人/UIA Pass。

## 6. 需求对齐与下一步

| PF-003 要求 | 状态 |
| --- | --- |
| 预览/吸附、手势会话、唯一提交、补偿 | Pass（Stage 172–174） |
| Surface 九向 pointer 输入与 App 候选/提交 | Engineering Pass（Stage 175/177） |
| 标题焦点 1 DIP / Shift 8 DIP 移动 | Engineering Pass |
| Alt 方向有限缩放 | Engineering Pass |
| 正式 App 保存/重载 | Engineering Pass |
| 跨显示器 DPI 与边界迁移 | Pending PF-003D4 |
| 物理鼠标/键盘/触控、截图、Narrator/UIA Bounds | Pending Product Evidence |

PF-003 仍为 `InProgress`，顶层 30 个 PF 项仍为 `0 Complete`。下一切片固定为 PF-003D4：冻结跨显示器进入/退出规则、Per-Monitor DPI 换算、目标显示器工作区夹取与 revision/topology 失效取消，并用真实双显示器或明确标注的可控拓扑证据验证 Expected/Actual。物理输入和 UIA/Narrator 继续保持独立发布门，不能由本轮自动证据替代。
