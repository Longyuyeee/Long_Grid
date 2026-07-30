# P0-07b2b2b2b3：DesktopHost 四层复合事务与紧急隐藏

日期：2026-07-30

结果：**Conditional Pass（隐藏自有 HWND 的 Bounds/Region/DComp/UIA 顺序提交、四层强制失败补偿、代次回滚和紧急隐藏通过；可见输入与硬件动态矩阵未验证）**

## 1. 目标

把已经分别通过的四层能力纳入一个明确的应用层事务：

1. Win32 HWND Bounds；
2. Window Region；
3. DirectComposition Root；
4. UI Automation Provider 快照。

Windows 没有覆盖这四层的共同原子 API，因此本探针验证的是 Long Grid 编排契约，而不是声称操作系统提供了跨子系统原子性。

## 2. Core 契约

新增 `DesktopHostCompositeTransactionCoordinator`，强制层顺序为：

```text
Bounds → Region → Composition → UI Automation
```

正常路径：

```text
检查 generation
  → 关闭输入门
  → 捕获全部四层快照
  → 再次检查 generation
  → 逐层 Apply + Verify + generation 检查
  → 四层最终复读
  → 重新开放输入
```

失败路径：

```text
失败层可能已经部分改变
  → 从失败层开始逆序 Restore
  → 所有 Restore 完成后，再正序 VerifyRestored
  → 全部恢复且复读一致：重新开放输入并返回 RolledBack
  → 任一恢复/复读/输入重开失败：保持输入关闭并隐藏受影响宿主
```

构造器拒绝缺层、重复层或错误顺序，防止生产接入时把 UIA 提前发布，或在 Bounds/Region 尚未稳定时开放输入。

## 3. 真实探针

探针使用一个始终隐藏、同线程、探针自有、非激活、非 Topmost 的顶层 HWND：

- Bounds：真实 `SetWindowPos` 和 `GetWindowRect`；
- Region：真实复杂 HRGN、`SetWindowRgn`、`GetWindowRgn` 和 `EqualRgn`；
- Composition：真实 device、target、visual、`SetRoot`、`Commit` 和 `WaitForCommitCompletion`；
- UIA：真实 `WM_GETOBJECT`、`IRawElementProviderSimple` 和 `AutomationElement` 客户端；
- 输入门：真实 Core 门禁状态；紧急路径调用 `ShowWindow(SW_HIDE)` 并复读可见性。

所有层在事务前完成快照。Region 快照包含两个独立副本：一个用于恢复所有权转移，另一个在全部层恢复后用于 `EqualRgn` 验证。

## 4. 场景

每轮执行：

1. 建立 generation 9 的初始 HWND、Region、Root 和 UIA；
2. 在当前显示 generation 10 下正常提交 generation 10 的四层状态；
3. 最终复读四层并重新开放输入；
4. 分别在 Bounds、Region、Composition、UIA 完成真实改变后注入 Apply 失败；
5. 每次都从失败层逆序恢复，并确认四层回到正常提交状态；
6. 在 Composition 真实 Commit/Wait 后改变显示 generation；
7. 确认 UIA 尚未发布，前三层逆序恢复；
8. 在 UIA 真实发布后注入失败，并在真实恢复完成后合成一次 Bounds 恢复验证失败；
9. 确认协调器返回 `RollbackFailed`、输入保持关闭且宿主被隐藏；
10. 探针独立复读确认底层状态实际已恢复，以免测试结束时留下损坏状态；
11. 清空 Region 和 DComp Root，释放 COM/HRGN，销毁 HWND 和窗口类。

## 5. 审计发现

### 5.1 逐层恢复后立即验证会产生错误结论

首轮真实预热只完成 `3/4` 个强制失败回滚。UIA 失败后，协调器先恢复 UIA 并立即读取 BoundingRectangle；此时逆序回滚尚未执行到 Bounds，HWND Host Provider 合法地返回失败态窗口坐标，于是 UIA 恢复被误判失败。

修正为严格两阶段补偿：

1. 先把全部已触碰层逆序 Restore；
2. 再从 Bounds 到 UIA 正序 VerifyRestored。

这不是测试特例，而是 UIA BoundingRectangle 对 HWND Bounds 的真实跨层依赖。Core 新增专门测试，要求第一次恢复验证必须发生在最后一次 Restore 之后。

### 5.2 仅逐层验证不足以开放输入

后一层可能间接改变前一层可观察状态。因此正常提交在四次局部验证后还必须执行一次四层最终复读。只有最终复读和 generation 检查全部通过，输入门才可重新开放。

### 5.3 失败层必须包含在补偿范围

适配器返回失败不代表没有改变。四个强制失败都在真实改变完成后注入，因此补偿从当前失败层开始，而不是只恢复此前声称成功的层。

## 6. 三轮正式结果

环境：Windows `10.0.26200.0`、x64、Per-Monitor V2 请求成功。

| 指标 | Run 1 | Run 2 | Run 3 |
|---|---:|---:|---:|
| 正常事务 | Applied | Applied | Applied |
| 四层强制失败 | `4/4` RolledBack | `4/4` RolledBack | `4/4` RolledBack |
| Commit 后代次失效 | RolledBack | RolledBack | RolledBack |
| 紧急路径 | RollbackFailed + hidden | RollbackFailed + hidden | RollbackFailed + hidden |
| 底层最终状态 | 四层匹配 | 四层匹配 | 四层匹配 |
| DComp Commit / Wait | `10 / 10` | `10 / 10` | `10 / 10` |
| USER | `2→4→2` | `2→4→2` | `2→4→2` |
| GDI | `6→6→6` | `6→6→6` | `6→6→6` |
| 进程句柄 | `340→340→340` | `340→340→340` | `340→340→340` |
| 前台/可见性 | 保持/全程隐藏 | 保持/全程隐藏 | 保持/全程隐藏 |

三轮均为 `Conditional Pass`。进程句柄绝对基线可以因 UIA 系统连接而变化，验收条件是同一独立进程内精确回到预热后基线。

## 7. Core 测试

Core 测试总数由 62 增至 82。新增覆盖：

- 固定四层顺序；
- 提交前代次失效；
- 输入关闭失败；
- 任一层快照失败；
- 四层分别在真实语义上的 Apply 失败；
- 局部验证失败；
- 四层最终复读发现跨层漂移；
- 层提交后代次失效；
- 逆序 Restore；
- 全部 Restore 后才执行恢复验证；
- Restore 失败仍继续尽力恢复其他层；
- 恢复复读失败；
- 输入重开失败；
- 紧急隐藏成功与失败；
- Capture、Apply 和 Restore 抛出异常时仍收敛到失败、补偿或紧急隐藏；
- 快照释放。

## 8. 已通过

- 固定 Bounds/Region/DComp/UIA 顺序；
- 输入关闭后才捕获和改变状态；
- 全层快照先于第一层 Apply；
- 失败层包含在补偿范围；
- 四层真实改变后的独立故障注入；
- Commit 后 generation 失效；
- 逆序恢复、恢复后统一复读；
- 正常路径四层最终复读；
- 回滚失败保持输入关闭并隐藏宿主；
- 真实 UIA 客户端和复杂 Region 验证；
- USER/GDI/进程句柄闭环；
- 不改变显示状态、外部窗口或前台窗口。

## 9. 尚未通过

- 可见 DesktopHost 的真实鼠标、键盘、触控、拖放与 UIA 操作路由关闭；
- 有实际 Surface/SwapChain 内容的帧级视觉验证；
- 每显示器 Root + 容器/项目 UIA Fragment 树和 Narrator；
- 自然发生的 Win32/GDI/DComp/UIA 资源失败；
- 多 HWND、跨线程和跨显示器同时提交；
- DPI 热切换、旋转、拔插、投影、睡眠唤醒、Explorer 重启和 RDP；
- ARM64 实机。

## 10. 决策

**Conditional Pass**

四层复合编排可进入 DesktopHost 垂直切片，但无人确认的自动恢复和可见桌面接管仍保持关闭。后续拆分为 `P0-07b2b2b2b4a` 安全自动化的可见输入/UIA Fragment 验证，以及 `P0-07b2b2b2b4b` 人工受控的 Narrator、真实输入和显示动态矩阵。
