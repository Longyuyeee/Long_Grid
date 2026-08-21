# Stage 175：PF-003D1 DesktopHost 布局输入合同审计

- 审计日期：2026-08-21
- 开发分支：`codex/pf002d-create-preview`
- 对应目标：PF-003 正式 DesktopHost 的标题栏移动、八向缩放命中与 capture 输入协议
- 结论：**PF-003D1 Surface 工程合同通过；正式 App 会话消费、可见候选和提交/补偿接线尚未完成，PF-003 保持 `InProgress`。**

## 1. 开发前审计与切片边界

当前 DesktopHost 不是每个方格一个窗口，而是每个显示器一个只读绘制 Surface；正式交互由独立有限激活窗口进入 Explicit 模式。旧 Surface 在 Explicit 模式只处理项目选择和桌面空白拖画创建，没有容器布局输入协议。直接从 App 调用 Stage 173 提交器会绕过 pointer capture、display/revision/topology 和 host invalidation，因此本轮先关闭 Surface 合同，不能直接宣称用户已可拖动。

PF-003D1 范围：

1. 标题栏映射 Move，四边和四角映射八种 Resize；
2. 锁定、重叠歧义、内容区和 Surface 外输入失败关闭；
3. 原生 `WM_LBUTTONDOWN/MOUSEMOVE/LBUTTONUP` 形成 begin/update/complete；
4. `WM_CAPTURECHANGED/WM_CANCELMODE/Escape` 和 host 隐藏/释放形成有限 cancel；
5. 输入携带 display、workspace revision、topology generation、累计 DIP delta、吸附和 Shift 事实；
6. update 被上层拒绝时立即释放 capture 并以 `HostInvalidated` 取消，不再发送 complete。

本轮不包括 App 的 `ProductWorkspaceContainerLayoutGestureSession` 消费、不绘制动态候选、不提交配置，也不提供方向键微调；这些进入 PF-003D2。

## 2. 实现

新增 `ProductDesktopContainerLayoutHitTestAdapter`：

- 8 DIP resize 边框随有效 DPI 缩放，最小 4 px；
- 角命中优先于边命中，边命中优先于标题栏 Move；
- 使用与正式绘制相同的 `ProductDesktopHostSurfaceLayout.GetContainerBounds`；
- 返回有限 `Hit / OutsideSurface / NoTarget / AmbiguousTarget / Locked`；
- 96/144/192/288/384 DPI 下保持相同 DIP 语义。

新增正式 Surface 输入合同：

- `Begin / Update / Complete / Cancel` 四阶段；
- `CaptureLost / CancelMode / EscapePressed / HostInvalidated` 有限取消原因；
- Surface 只接受 Explicit 模式、唯一未锁定目标和非 injected 的当前输入消息；
- delta 以按显示器 DPI 换算的累计 DIP 表示，避免增量漂移；
- lifecycle 将 Surface 输入绑定到创建该 Surface 的 display ID、workspace revision 和 topology generation；
- Surface 回调异常转换为拒绝，不穿透 Win32 window procedure；
- Passive、Hidden 和 Dispose 都会取消未完成布局输入。

## 3. 预期—实际—差异

| 检查项 | 预期 | 实际 | 差异/处理 |
| --- | --- | --- | --- |
| 标题栏 + 八向边框 | 9 种唯一手势 | 9/9 参数化命中通过 | 无 |
| 锁定方格 | 不开始布局 | `Locked` | 无 |
| 重叠方格 | 不猜测目标 | `AmbiguousTarget` | 无 |
| 内容区/Surface 外 | 不触发布局 | `NoTarget / OutsideSurface` | 无 |
| 100%–400% DPI | 右下角语义一致 | 5/5 DPI 合同通过 | 无 |
| lifecycle 事实 | 精确保留 display/revision/topology | `display-primary / 8 / 12` | 无 |
| 真实 Win32 Surface | 自有 handle、Explicit 合同、有限序列 | handle 非零，合同通过，Begin/Update/Complete 顺序一致 | 无 |
| 回调异常 | 有限拒绝、进程不崩溃 | 返回 false | 无 |
| update 陈旧 | 立即 cancel，不再 complete | 末轮状态机复审已修正 | 首轮缺口已处理 |

首次编译真实暴露两个实现差异：新命中适配器遗漏 `PixelRect` 所在命名空间，且 foreach 局部名与后续解构变量冲突，产生 CS0246/CS0136。补充真实 using、重命名局部变量后重新构建通过。聚焦测试首轮通过后又发现 rejected update 仍可能等待 pointer-up，修正为立即 `HostInvalidated` cancel。完整门禁随后发现 switch expression 有 19 行多余缩进；运行仓库格式化器修正后，格式验证和 48 项聚焦合同复验均通过。上述差异均保留记录，没有写成一次通过。

## 4. 真实测试边界

专项测试实际创建并销毁 `WindowsProductDesktopHostReadOnlySurface` 原生窗口，验证非零 HWND、Explicit window contract、回调顺序和异常隔离；它没有跨进程查询 UIA，也没有合成全局鼠标输入。九向命中和 lifecycle 事实使用生产适配器与生产绑定路径。

因此本轮证明“正式原生 Surface 已具备有限输入合同”，不证明物理鼠标已经驱动 App 布局、视觉候选已更新或配置已保存。完整物理输入仍需 PF-003D2 App 接线后在合规交互会话执行。

## 5. 需求对齐

| PF-003 要求 | 当前状态 |
| --- | --- |
| Core 预览/吸附/性能 | Pass（Stage 172） |
| 会话、唯一提交、重载 | Pass（Stage 173） |
| 保存失败补偿 | Pass（Stage 174） |
| 标题栏与八向 Surface 命中 | Engineering Pass |
| capture begin/update/complete/cancel 合同 | Engineering Pass |
| App 会话消费与可见候选 | Pending PF-003D2 |
| pointer-up 唯一提交与补偿接线 | Pending PF-003D2 |
| 方向键 1 DIP / Shift 大步微调 | Pending PF-003D2 |
| 跨显示器和物理/UIA Bounds | Pending 后续切片 |

## 6. 验证结果

- PF-003 布局与本轮输入聚焦合同：`48/48`；
- Release 全量测试：`1058/1058`；
- Release solution build：`0 warning / 0 error`；
- 100 方格生产规模预检：2,000 次布局预览 P95 `0.107 ms < 16.7 ms`，真实保存/恢复沙箱已清理，`readsRealDesktop=false / realFileOperationsAllowed=false`；
- 153-ID 静态 UI 合同：Pass；
- PF-002 正式 App 回归：Pass，外部 Expected/Actual 合同 `Difference=None`，桌面与用户配置元数据不变，临时证据已删除；
- 正式窗口生命周期：两轮 20 秒 Pass，就绪 `2,208 / 2,449 ms`，退出码均为 0，未查询跨进程 UIA；
- 跨进程 UIA：已知上游组合继续安全阻断，不强行执行；
- 漏洞、格式与差异检查：Pass，无已知易受攻击包，`git diff --check` 通过。

## 7. 下一切片

PF-003D2：

1. App 绑定 `BindContainerLayout`，begin 创建 Stage 173 会话；
2. update 返回候选并让 Surface 局部重绘，不重建原生窗口或写盘；
3. cancel 恢复原候选，complete 进入 Stage 173 唯一提交；
4. 保存快照接入 Stage 174 Published/CompensationRequired；
5. 标题栏键盘焦点下方向键 1 DIP、Shift 大步调整复用同一事务入口；
6. 建立正式进程内 App evidence session 和真实配置重载，再申请物理鼠标矩阵。
