# P0-07b2b2b2b2：DirectComposition 提交、UIA Provider 与代次发布

日期：2026-07-30

结果：**Conditional Pass（隐藏自有 HWND 的 DComp Commit/Wait、真实 UIA 客户端读取和代次失效补偿通过；可见内容、Fragment 树与硬件动态矩阵未验证）**

## 1. 验证目标

验证 DesktopHost 能否在同一显示拓扑 generation 下完成以下顺序：

1. 保留旧 HWND Bounds、DirectComposition Root 和 UIA 快照；
2. 更新探针自有 HWND 的物理屏幕 Bounds；
3. 切换真实 `IDCompositionTarget` 的 Root Visual；
4. 调用 `Commit` 并等待 `WaitForCommitCompletion`；
5. 再次检查 generation；
6. generation 有效时一次替换不可变 UIA 快照；
7. generation 已失效时重新提交旧 Root、恢复旧 Bounds，且不发布新 UIA generation。

这不是把四个 Windows 子系统描述成一个系统原子事务，而是验证 Long Grid 所需的应用层门禁和补偿顺序。

## 2. 官方契约

- [`CreateTargetForHwnd`](https://learn.microsoft.com/windows/win32/api/dcomp/nf-dcomp-idcompositiondevice-createtargetforhwnd) 只允许绑定调用进程拥有的 HWND；同一窗口的同一层最多存在一个 target。
- [`IDCompositionTarget::SetRoot`](https://learn.microsoft.com/windows/win32/api/dcomp/nf-dcomp-idcompositiontarget-setroot) 可替换或清空 visual tree 的 Root。
- [`IDCompositionDevice::Commit`](https://learn.microsoft.com/windows/win32/api/dcomp/nf-dcomp-idcompositiondevice-commit) 把同一 device 的待处理命令作为一个原子事务提交；多个 device 不共享这个原子边界。
- [`WaitForCommitCompletion`](https://learn.microsoft.com/windows/win32/api/dcomp/nf-dcomp-idcompositiondevice-waitforcommitcompletion) 等待上一次 Commit 被合成引擎处理完成。
- [`WM_GETOBJECT`](https://learn.microsoft.com/windows/win32/winauto/handling-the-wm-getobject-message) 是 UIA 客户端请求 HWND Provider 的标准入口。
- [`UiaReturnRawElementProvider`](https://learn.microsoft.com/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiareturnrawelementprovider) 返回 `IRawElementProviderSimple`，并要求窗口销毁时通知 UIA 清理 Provider 映射。
- UIA BoundingRectangle 使用物理屏幕坐标；它是客户端读取事实，不参与 DirectComposition 的系统事务。

结论是：DComp 内部可以原子 Commit，但 Bounds、Region、DComp 与 UIA 没有共同的 Windows 原子 API。生产实现必须以 generation、输入关闭、最终复读和补偿组成应用层事务。

## 3. 探针实现

探针创建一个始终隐藏、非激活、ToolWindow、非 Topmost 的自有顶层 HWND，并建立：

- 一个真实 `IDCompositionDevice`；
- 一个绑定 HWND 的 `IDCompositionTarget`；
- 初始 Root Visual 和每次提议创建的新 Root Visual；
- 一个处理 `WM_GETOBJECT` 的真实 `IRawElementProviderSimple`；
- 一个由 `AutomationElement.FromHandle` 驱动的真实 UIA 客户端；
- 一个不可变 `UiaGenerationSnapshot`，同时发布 generation、AutomationId、ItemStatus 和 Bounds。

发布顺序：

```text
capture old Bounds / Root / UIA snapshot
  → SetWindowPos(hidden owned HWND)
  → SetRoot(proposed visual)
  → Commit
  → WaitForCommitCompletion
  → recheck topology generation
  → valid: publish immutable UIA snapshot
  → stale: SetRoot(old visual) + Commit/Wait + restore old Bounds
```

客户端以 `ItemStatus=generation:n` 和 `AutomationId=LongGrid.Generation.n` 校验代次，并读取物理屏幕 BoundingRectangle。生产 Fragment 客户端也必须拒绝与当前布局 generation 不一致的数据。

## 4. 审计中发现并修正的问题

首轮实现没有通过门禁，而是发生 `AccessViolationException`。原因是探针为 `IDCompositionVisual` 手写了包含原生重载的 COM 接口，并直接调用 Offset 属性槽；这类不完整的手写 vtable 声明不适合作为可信证据。

修正为：

- 不再调用非必要的 Visual 属性槽；
- 用两个由真实 `CreateVisual` 返回的对象切换 `SetRoot`；
- 正常路径和补偿路径都执行真实 `Commit/Wait`；
- 只有 Wait 完成且 generation 仍有效才发布 UIA；
- 窗口销毁时通过原生 `UiaReturnRawElementProvider(hwnd, 0, 0, NULL)` 清理 Provider 映射；
- 保留零警告、精确 USER/GDI 基线和句柄稳定门禁。

第二次运行正确返回 `Fail`：UIA 根 Provider 的 BoundingRectangle 由 HWND Host Provider 反映真实窗口位置，而探针只更新了自定义快照，没有移动 HWND。修正为事务内同步移动隐藏自有 HWND，并在代次失效时一并恢复。没有伪造或放宽 UIA Bounds 判定。

## 5. 场景与结果

正式测量前执行完整成功路径预热。每个独立进程正式运行执行：

1. 建立 target、初始 Root，并完成首次 Commit/Wait；
2. generation 1 下提交 generation 2 的 Bounds 和新 Root；
3. Wait 完成后发布 UIA generation 2；
4. 由真实 `AutomationElement` 读取 generation、AutomationId 和 BoundingRectangle；
5. generation 2 下提交 generation 3 的 Bounds 和新 Root；
6. 在真实 Commit/Wait 后注入 generation 失效；
7. 重新提交 generation 2 Root、恢复 generation 2 Bounds；
8. 再次由 UIA 客户端确认 generation 3 从未发布；
9. 清空 Root、Commit/Wait、释放 COM、销毁 HWND、注销窗口类。

当前机器正式运行结果：

| 指标 | 结果 |
|---|---|
| DComp Target / Root | 创建并提交成功 |
| 业务 Commit / Wait | `4 / 4`，全部成功 |
| 正常发布 | generation 2 |
| UIA 客户端 | generation、AutomationId、BoundingRectangle 一致 |
| 代次失效 | generation 3 未发布 |
| 补偿 | Root 与 HWND Bounds 恢复到 generation 2 |
| 可见性 / 前台 | 全程隐藏，前台保持 |
| USER | `2 → 4 → 2` |
| GDI | `0 → 0 → 0` |
| 进程句柄 | `329 → 329 → 329` |
| 外部状态 | 未改变显示状态或外部窗口 |

正式运行连续三次均为 `Conditional Pass`；资源绝对值只代表当前进程和机器，验收以每次回到预热后基线为准。

## 6. 已通过

- 真实 DComposition device、target、visual 和 Root；
- 正常与补偿路径的真实 Commit/Wait；
- Commit 后 generation 复核；
- 失效 generation 不进入 UIA Provider 快照；
- 真实 `WM_GETOBJECT` / `IRawElementProviderSimple`；
- 真实 `AutomationElement` 客户端读取；
- 物理屏幕负坐标 BoundingRectangle；
- 隐藏、非激活和外部状态不变；
- COM、UIA 映射、HWND、USER/GDI 和句柄清理。

## 7. 尚未通过

- 有实际 Surface/SwapChain 内容的可见渲染和帧级撕裂观察；
- 每显示器 Root + 容器/项目 `IRawElementProviderFragment` 树；
- UIA Fragment 导航、RuntimeId、焦点、事件和 Narrator；
- 跨线程 HWND；
- 100%–300% DPI 热切换、旋转、拔插、投影、睡眠唤醒、Explorer 重启和 RDP；
- x64 之外的 ARM64 实机验证。

## 8. 决策

**Conditional Pass**

DirectComposition Commit/Wait 和真实 HWND UIA Provider 可进入生产原型，但不能声称四层更新具有系统级原子性。P0-07b2b2b2b3 已继续验证 Bounds、Region、DComp、UIA 强制失败编排；后续 b4a 自动验证可见输入/UIA Fragment，b4b 在人工受控机器执行 Narrator、真实输入和显示动态矩阵。在这些门禁关闭前，无人确认的自动恢复和可见桌面接管继续保持关闭。
