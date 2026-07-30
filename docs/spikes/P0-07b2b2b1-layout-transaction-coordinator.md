# P0-07b2b2b1：布局批量提交、代次门禁与补偿回滚

日期：2026-07-30

结果：**Conditional Pass（Core 事务协调合同通过；真实 Win32 批量适配和动态显示矩阵未验证）**

## 1. 假设

在不创建或移动真实窗口的前提下，可以先证明布局恢复的业务事务边界：

1. Blocked 和未获用户确认的 ReviewRequired 计划不会触碰窗口；
2. 旧显示代次不会提交；
3. 应用前保存全部容器原位置；
4. 批量应用失败、应用结果不一致或应用期间代次变化时，恢复全部原位置；
5. 提交和回滚都必须重新读取实际 Bounds 验证，不能只相信 Win32 返回值；
6. 负坐标保持为有符号物理像素，不按主屏原点裁剪。

## 2. 官方 API 事实

- [`BeginDeferWindowPos`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-begindeferwindowpos) 分配多窗口位置结构；预先给出最大窗口数可以更早发现资源不足。
- [`DeferWindowPos`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-deferwindowpos) 返回的句柄可能变化，下一次调用必须使用新句柄；任一排队调用失败后应放弃本次序列，不调用 `EndDeferWindowPos`。
- [`EndDeferWindowPos`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enddeferwindowpos) 在一次屏幕刷新周期更新多个窗口并向各窗口发送位置变化消息，但文档没有承诺业务层原子提交或失败自动回滚。
- [`GetWindowRect`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowrect) 返回屏幕坐标且受 DPI 虚拟化影响；生产适配器必须在 Per-Monitor V2 上下文中读取，并明确是否接受不可见 resize border。
- [`WM_DPICHANGED`](https://learn.microsoft.com/windows/win32/hidpi/wm-dpichanged) 携带新的 DPI 和建议矩形；窗口应先用 `SetWindowPos` 采用建议矩形，再进入稳定拓扑后的布局事务。
- [Virtual Screen](https://learn.microsoft.com/windows/win32/gdi/the-virtual-screen) 明确主显示器不必位于虚拟屏幕左上方，应用必须支持负坐标。

因此 `DeferWindowPos` 只解决“同刷新周期批量移动”，不替代 Long Grid 的审批、代次、验证和补偿语义。

## 3. 实现

新增 `LayoutRecoveryTransactionCoordinator`，Core 不持有 HWND，也不依赖 UI 框架。Windows 层只需实现：

```text
ILayoutRecoveryWindowBatchAdapter
├─ Capture(containerIds) → 每个容器的实际 PixelRect
└─ Apply(placements)     → 批量尝试应用
```

协调顺序：

```text
检查 Plan/审批
  → 检查 generation
  → 捕获全部原 Bounds
  → 再检查 generation
  → 幂等判断
  → 批量 Apply
  → 再检查 generation
  → 复读并逐项验证 proposed Bounds
  → 最终检查 generation
  → Applied

任一 Apply/验证/代次失败
  → 批量恢复原 Bounds
  → 复读并逐项验证
  → RolledBack 或 RollbackFailed
```

`Apply` 返回失败时也假设可能已经发生部分变化，因此仍执行完整补偿。协调器会立即防御性复制适配器返回的原位置字典，避免适配器内部实时状态在 Apply 时污染回滚快照。零容器计划在审批和代次有效后返回 `NoChanges`，避免空桌面被误判为错误。

## 4. 状态合同

| 状态 | 含义 | UI/调用方动作 |
|---|---|---|
| `Applied` | 提交后 Bounds 验证一致，代次仍有效 | 可记录成功 |
| `NoChanges` | 空计划或窗口已经位于目标位置 | 不移动、不提示 |
| `Rejected` | Blocked 或缺少 ReviewRequired 审批 | 返回预览/映射 |
| `Superseded` | 提交前代次已变化 | 丢弃并等待新计划 |
| `CaptureFailed` | 原位置快照失败或不完整 | 不提交 |
| `RolledBack` | 提交失败，原位置已恢复并验证 | 记录失败，不显示成功 |
| `RollbackFailed` | 无法证明原布局已恢复 | 停止自动恢复并保留现场 |

只有 `Applied` 和 `NoChanges` 的 `KeepsProposedLayout` 为真。

## 5. 自动化证据

12 个 Core 测试覆盖：

1. 两窗口完整批量提交与逐项验证；
2. 已在目标位置时幂等；
3. 零窗口计划；
4. Blocked 在适配器调用前拒绝；
5. ReviewRequired 必须显式批准；
6. 捕获前代次失效；
7. 捕获期间代次失效；
8. Apply 返回失败且已经产生部分变更，适配器还复用了内部实时 Bounds 字典；
9. 应用后实际 Bounds 被窗口约束改变；
10. Apply 后代次变化；
11. 补偿 Apply 失败；
12. 原位置快照缺项。

当前测试包含负 X 坐标的原位置和目标位置，证明 Core 不会把坐标截断到零。

## 6. 审计结论

**Conditional Pass**

已证明：

- 审批与 Blocked 全量阻断；
- 提交前后代次门禁；
- 原位置全量快照；
- 批量应用后复读验证；
- 部分失败假设下的完整补偿；
- 回滚复读验证和显式失败状态；
- 空计划与已应用计划幂等；
- 负坐标在 Core 合同中保持。

尚未证明：

- `Begin/Defer/EndDeferWindowPos` 在 Long Grid HWND 上的真实行为；
- 跨线程窗口、窗口 min/max 约束和 DPI 虚拟化影响；
- Window Region、Composition visual 和 UIA Bounds 的同代提交；
- 真实 `WM_DISPLAYCHANGE`/`WM_DPICHANGED`、旋转、缩放、插拔、投影、睡眠与 RDP；
- 回滚期间再次发生显示变化时的 Windows 层安全策略；
- 多窗口刷新撕裂、焦点、Z-order、任务栏和无障碍副作用。

## 7. P0-07b2b2b2

P0-07b2b2b2a 已创建完全隔离且始终隐藏的临时 Long Grid 测试 HWND，完成同线程 Win32 批量适配、负坐标、代次失效补偿和真实部分变更补偿。后续 P0-07b2b2b2b 仍需：

1. 验证跨线程窗口拒绝/调度策略；
2. 对真实排队失败、`EndDeferWindowPos` 失败和窗口约束偏差保留诊断；
3. 把 Window Region、Composition 和 UIA 更新纳入同一 generation；
4. 先完成合成失败注入，再由人工受控执行 DPI、旋转、插拔、投影、睡眠和 RDP 矩阵；
5. 分别记录 Windows build、GPU、DPI、拓扑、焦点、Z-order、资源和恢复结果。

在 P0-07b2b2b2b 完成前，不启用无人确认的真实窗口自动恢复。
