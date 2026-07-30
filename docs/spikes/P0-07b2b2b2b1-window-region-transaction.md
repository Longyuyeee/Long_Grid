# P0-07b2b2b2b1：Window Region 所有权、部分失败与补偿事务

日期：2026-07-30

结果：**Conditional Pass（隐藏自有 HWND 的 Region 捕获、所有权和补偿链通过；DirectComposition/UIA 与可见交互未验证）**

## 1. 假设

Window Region 没有多 HWND 原子批量 API，但 Long Grid 可以通过完整快照和补偿做到：

1. 每次提交前捕获所有宿主的现有 Region；
2. 成功调用 `SetWindowRgn` 后立即放弃调用方 HRGN 所有权；
3. 部分窗口已经应用后发生失败，恢复全部窗口；
4. 全部窗口应用后 generation 失效，恢复全部窗口；
5. 回滚使用独立验证副本，不读取已经转交系统的 HRGN；
6. 清理后 USER/GDI/进程句柄回到稳定基线。

## 2. 官方事实

- [`SetWindowRgn`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowrgn) 使用相对窗口左上角的坐标；成功后系统取得 HRGN 所有权且不复制，调用方不得继续调用或删除该句柄。
- [`GetWindowRgn`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowrgn) 把窗口 Region 的副本写入调用方提供的 HRGN；返回值区分空、简单、复杂和错误 Region。
- [`IDCompositionDevice::Commit`](https://learn.microsoft.com/windows/win32/api/dcomp/nf-dcomp-idcompositiondevice-commit) 只保证同一 DirectComposition device 的待处理命令作为一个事务提交。
- [`WaitForCommitCompletion`](https://learn.microsoft.com/windows/win32/api/dcomp/nf-dcomp-idcompositiondevice-waitforcommitcompletion) 等待上一次 Composition Commit 被引擎处理完成。
- [`IRawElementProviderFragment::BoundingRectangle`](https://learn.microsoft.com/windows/win32/api/uiautomationcore/nf-uiautomationcore-irawelementproviderfragment-get_boundingrectangle) 是 provider 返回的只读屏幕坐标事实，并不参与 Region 或 Composition 的系统事务。

因此 Bounds、Region、Composition 和 UIA 不存在一个共同的 Windows 原子提交 API，必须由 Long Grid 编排、验证和补偿。

## 3. 实现

新增只在探针中的 `Win32WindowRegionAdapter`：

```text
Capture
  CreateRectRgn(empty)
  → GetWindowRgn(hwnd, copy)
  → 调用方拥有 copy

Apply
  Create/CombineRgn
  → SetWindowRgn
  → 成功：系统拥有 HRGN，调用方立即失去所有权
  → 失败：调用方 DeleteObject

Rollback
  转移前 Clone(snapshot) 作为验证副本
  → SetWindowRgn(snapshot)
  → GetWindowRgn(new copy)
  → EqualRgn(new copy, verification clone)
```

探针 Region 都是两个矩形组成的复杂交互岛，坐标相对无边框隐藏窗口左上角。

## 4. 场景

每轮在两个隐藏、同线程、探针自有 HWND 上执行：

1. 设置初始复杂 Region；
2. 捕获两窗 Region；
3. 逐窗应用新 Region 并复读验证；
4. 再次捕获，逐窗应用第三组 Region；
5. 全部 `SetWindowRgn` 成功后递增 generation；
6. 使用捕获快照恢复两窗并以独立副本验证；
7. 再次捕获，真实应用第一窗 Region 后注入失败；
8. 恢复两窗并验证最终均为第 3 步 Region；
9. 设为 NULL Region、销毁 HWND、注销窗口类。

失败注入发生在真实所有权转移之后，但不是操作系统自然返回的 GDI 失败。

## 5. 审计中发现的问题

首版探针正确返回 Fail：

1. 回滚时把捕获 HRGN 转给系统后，又用同一句柄执行 `EqualRgn`，违反官方所有权合同；
2. 简单预热没有覆盖 Region 复制、两类回滚与验证路径，GDI 计数从 `6` 增至 `8`，无法区分延迟初始化和泄漏。

修正：

- 在任何回滚所有权转移前复制独立验证 HRGN；
- 转移后原句柄不再读取、不再删除；
- 预热执行与测量相同的成功、代次回滚和部分失败路径；
- 窗口销毁前显式 `SetWindowRgn(NULL)`；
- GDI 必须精确回到完整预热后的基线，不放宽阈值。

## 6. 三轮结果

环境：Windows `10.0.26200.0`、X64、Per-Monitor V2 请求成功。

| 指标 | Run 1 | Run 2 | Run 3 |
|---|---:|---:|---:|
| HWND | 2 | 2 | 2 |
| Region 捕获 | 7 | 7 | 7 |
| 事务应用 | 6 | 6 | 6 |
| 所有权转移 | 11 | 11 | 11 |
| 正常提交 | Applied | Applied | Applied |
| 代次失效 | RolledBack / verified | RolledBack / verified | RolledBack / verified |
| 部分失败 | 1 窗已应用后 RolledBack | 1 窗已应用后 RolledBack | 1 窗已应用后 RolledBack |
| 最终 Region | 匹配 | 匹配 | 匹配 |
| 全程隐藏/焦点 | 通过 | 通过 | 通过 |
| USER | `2→5→2` | `2→5→2` | `2→5→2` |
| GDI | `8→8→8` | `8→8→8` | `8→8→8` |
| 进程句柄 | `258→258→258` | `258→258→258` | `258→258→258` |

三轮均为 `Conditional Pass`。

## 7. 已通过

- 复杂 Region 构造与复读；
- 捕获副本由调用方释放；
- Set 成功后系统所有权路径；
- 转移前独立验证副本；
- 两窗正常提交；
- 全部应用后 generation 失效补偿；
- 第一窗真实应用后的部分失败补偿；
- 回滚逐窗 `EqualRgn`；
- 最终 Region 一致；
- 隐藏和焦点保持；
- 完整路径预热后的 USER/GDI/句柄闭环；
- 不输出 HWND、外部窗口、标题或桌面内容。

## 8. 未通过或未覆盖

- 系统自然产生的 Create/Combine/Get/Set Region 资源失败；
- 可见每显示器宿主的真实命中、拖放和动画中间态；
- Region 变化期间输入路由的关闭/重开；
- 跨线程 HWND；
- DirectComposition visual tree、Commit 和 Wait；
- 真实 UIA Fragment provider 与客户端读取；
- DPI、旋转、插拔、投影、睡眠和 RDP。

## 9. 结论

**Conditional Pass**

Window Region 可进入下一原型，但它是逐窗立即生效的操作，不能被描述成系统原子事务。生产 DesktopHost 必须在复合事务前关闭输入，全部 Bounds/Region/Composition/UIA 验证后再开放；补偿失败时隐藏宿主。

P0-07b2b2b2b2 已建立真实 DirectComposition target/visual 的 Commit/Wait 探针和真实 UIA provider；P0-07b2b2b2b3 已完成 Bounds/Region/DComp/UIA 四层编排，P0-07b2b2b2b4a 已完成可见输入/UIA Fragment 自动验证。下一步 P0-07b2b2b2b4b 进行 Narrator、真实输入与受控硬件动态矩阵。
