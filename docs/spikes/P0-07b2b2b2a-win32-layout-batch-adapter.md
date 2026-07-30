# P0-07b2b2b2a：隐藏 HWND Win32 批量适配与真实补偿回滚

日期：2026-07-30

结果：**Conditional Pass（隐藏同线程 HWND 的真实批量与补偿链通过；可见渲染、同代视觉/UIA 和硬件动态矩阵未验证）**

## 1. 假设

在不触碰第三方窗口、不显示探针窗口、不改变系统显示状态的前提下，可以验证：

1. `BeginDeferWindowPos`/`DeferWindowPos`/`EndDeferWindowPos` 能批量移动 Long Grid 自有顶层 HWND；
2. 适配器能在 Per-Monitor V2 上下文用 `GetWindowRect` 复读并验证物理像素 Bounds；
3. 负 X/Y 坐标不会被 Win32 适配器截断到主显示器；
4. 提交后显示代次失效时，Core 协调器能通过同一 Win32 适配器恢复原 Bounds；
5. 一扇窗口已经真实移动而适配器返回失败时，补偿链能恢复整批；
6. 全过程不显示、不激活、不置顶、不改变第三方窗口，资源最终回到基线。

## 2. 官方 API 边界

- [`BeginDeferWindowPos`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-begindeferwindowpos) 按窗口数预分配内部多窗口位置结构；提前给出完整数量可更早发现资源不足。
- [`DeferWindowPos`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-deferwindowpos) 返回的句柄可能不同，后续调用必须使用新句柄。任一排队调用返回 NULL 后必须放弃该序列，不调用 `EndDeferWindowPos`。
- [`EndDeferWindowPos`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enddeferwindowpos) 在一次屏幕刷新周期更新一组窗口，并发送窗口位置变化消息；它不提供 Long Grid 业务层审批、代次或补偿承诺。
- [`GetWindowRect`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowrect) 返回屏幕坐标且可能受 DPI 虚拟化影响。探针先请求 Per-Monitor V2，并使用无边框 `WS_POPUP`，避免把不可见 resize border 混入本次精确 Bounds 比较。
- [`IsWindowVisible`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-iswindowvisible) 检查 `WS_VISIBLE` 状态。探针从不设置 `WS_VISIBLE`，也不使用 `SWP_SHOWWINDOW`。

## 3. 安全边界

探针只创建两扇：

```text
WS_POPUP
+ WS_EX_TOOLWINDOW
+ WS_EX_NOACTIVATE
- WS_VISIBLE
- WS_EX_TOPMOST
```

两扇窗口由当前 STA 线程创建，parent 均为 NULL，窗口标题为空。所有目标坐标均为负数，窗口始终隐藏。探针不调用显示配置、DPI 设置、投影、电源、RDP、输入合成或第三方 HWND API。

批量位置 flags：

```text
SWP_NOACTIVATE
| SWP_NOZORDER
| SWP_NOOWNERZORDER
```

## 4. 场景

每轮执行：

1. 创建两扇负坐标隐藏窗口；
2. Core 协调器通过真实 Win32 适配器提交两个目标 Bounds；
3. 再次提交同一计划，验证 `NoChanges` 不调用原生批量 API；
4. 提交第二组 Bounds，在 `EndDeferWindowPos` 成功后递增 generation；
5. 协调器检测旧代次并用真实批量 API 恢复上一步 Bounds；
6. 对第三组 Bounds 先用 `SetWindowPos` 真实移动第一扇隐藏窗口，再故意返回失败；
7. 协调器通过真实批量 API 恢复两扇窗口，并复读验证；
8. 验证隐藏、焦点、样式、负坐标和资源，销毁 HWND 并注销窗口类。

第 6 步是明确标记的合成失败注入：它证明“已经发生部分变更”的补偿能力，不冒充真实观察到的 `DeferWindowPos` 或 `EndDeferWindowPos` 资源失败。

## 5. 三轮结果

环境：Windows `10.0.26200.0`、X64、Per-Monitor V2 请求成功。

| 指标 | Run 1 | Run 2 | Run 3 |
|---|---:|---:|---:|
| HWND | 2 | 2 | 2 |
| Bounds 捕获 | 8 | 8 | 8 |
| 成功原生批量调用 | 4 | 4 | 4 |
| 正常提交 | Applied | Applied | Applied |
| 幂等提交 | NoChanges | NoChanges | NoChanges |
| 代次失效 | RolledBack / verified | RolledBack / verified | RolledBack / verified |
| 部分失败 | RolledBack / verified | RolledBack / verified | RolledBack / verified |
| 真实部分移动 | 成功 | 成功 | 成功 |
| 负坐标往返 | 通过 | 通过 | 通过 |
| 全程隐藏/焦点保持 | 通过 | 通过 | 通过 |
| USER | `2→5→2` | `2→5→2` | `2→5→2` |
| GDI | `0→0→0` | `0→0→0` | `0→0→0` |
| 进程句柄 | `259→259→259` | `259→259→259` | `259→259→259` |

三轮均为 `Conditional Pass`。

## 6. 实现审计

通过：

- 每个 `DeferWindowPos` 都接收上一次返回的新 HDWP；
- 排队失败路径立即返回，不调用 `EndDeferWindowPos`；
- 批量提交不带 show/activate/Z-order flags；
- `GetWindowRect` 失败、未知容器或无面积 Bounds 均返回 Capture/Apply 失败；
- 正常提交与回滚都由 Core 逐窗复读；
- 幂等路径没有额外原生批量调用；
- 负坐标完整保留；
- 探针窗口始终隐藏，前台窗口在每个事务阶段采样保持；
- ToolWindow/NoActivate 存在，Topmost 不存在；
- HWND 和窗口类成对销毁/注销，USER/GDI/句柄回到预热后基线；
- JSON 不输出 HWND、窗口标题、外部窗口身份或桌面内容。

未通过或未覆盖：

- 真实内存压力导致的 Begin/Defer/End 失败；
- 跨线程 HWND 与 owner/parent 不一致；
- 可见窗口的刷新撕裂、DWM 帧和动画；
- Window Region、Composition visual 与 UIA Bounds 的同代提交；
- 窗口 min/max 约束导致的真实提交偏差；
- `WM_DPICHANGED` 建议矩形；
- 缩放、旋转、显示器插拔、投影、睡眠与 RDP。

## 7. 结论

**Conditional Pass**

隐藏、同线程、自有顶层 HWND 的 Win32 批量适配边界可以进入下一原型。生产实现仍必须保留 Core 事务协调器，不能把 `EndDeferWindowPos` 成功直接当成业务提交成功。

P0-07b2b2b2b1 已继续验证 Window Region 的真实所有权、部分失败补偿和代次回滚；P0-07b2b2b2b2 又验证了 DirectComposition `Commit/WaitForCommitCompletion`、真实 UIA provider、客户端复读和失效 generation 不发布。P0-07b2b2b2b3 仍需完成四层复合故障编排，并在人工受控环境执行显示动态矩阵。在该门禁完成前，无人确认的自动窗口恢复继续关闭。
