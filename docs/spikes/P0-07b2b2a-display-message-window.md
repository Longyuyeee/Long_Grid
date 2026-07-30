# P0-07b2b2a：隐藏消息窗口、后台 CCD 调度与生命周期闭环

执行日期：2026-07-30

结果：**Conditional Pass（真实消息基础设施与启动稳定链通过；动态事件矩阵和窗口事务仍需 P0-07b2b2b）**

前置：P0-07b1、P0-07b2a、P0-07b2b1

## 1. 目标

在不修改任何显示设置、不发送合成输入、不移动其他应用窗口的前提下，验证：

1. 创建真实但完全隐藏的顶层 Win32 消息窗口；
2. 使用公开消息接收显示、DPI、设备、电源和当前会话变化；
3. `WTSRegisterSessionNotification` 与注销严格成对；
4. 窗口类注册、HWND、Timer 和采样线程完整回收；
5. WindowProc 只标记状态和调度工作，不同步执行 CCD 查询；
6. CCD/monitor/DPI 快照在单一专用后台线程串行执行；
7. 后台结果通过私有 `WM_APP` 返回窗口线程；
8. 结果携带代次，旧代次完成结果不得进入稳定器；
9. 启动状态经过 P0-07b2b1 的静默期和两次一致采样进入 Ready；
10. 默认输出不包含硬件、窗口、会话或拓扑身份。

## 2. 官方 API 边界

[`WTSRegisterSessionNotification`](https://learn.microsoft.com/windows/win32/api/wtsapi32/nf-wtsapi32-wtsregistersessionnotification) 只向已注册窗口发送 `WM_WTSSESSION_CHANGE`，并明确要求窗口销毁前调用配对的 `WTSUnRegisterSessionNotification`。实现把注销置于 `DestroyWindow` 之前，并把注册/注销是否成功分别纳入通过条件。

[`WM_DEVICECHANGE`](https://learn.microsoft.com/windows/win32/devio/wm-devicechange) 通知硬件配置变化。探针只把它转换为 `Device` 脏标记，不读取或输出事件携带的设备路径。

[`SetTimer`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-settimer) 在窗口消息队列中产生近似的 `WM_TIMER`。它只用于检查稳定器的 `NextActionAt` 和安排后台采样；Timer 精度不作为性能预算。

`WM_DISPLAYCHANGE`、`WM_DPICHANGED`、`WM_POWERBROADCAST` 和 `WM_WTSSESSION_CHANGE` 的事实边界沿用 P0-07b2b1。`WM_DPICHANGED` 分支会先对本消息窗口采用建议矩形，再标记 DPI 变化；当前静态观察没有触发该分支。

## 3. 窗口模型

消息窗口：

- top-level；
- `WS_POPUP`；
- `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`；
- 1 × 1；
- 从不调用 `ShowWindow`；
- 不置顶；
- 标题为空；
- 不读取前台窗口、其他 HWND 或屏幕内容。

不能使用 message-only window 代替，因为显示和设备广播面向顶层窗口。该 HWND 仅用于官方通知和内部调度，不是 DesktopHost 呈现窗口。

## 4. 线程和代次

```text
Window thread
  Win32 messages
    → DisplayTopologyStabilizer
    → 到达 NextActionAt
    → enqueue(generation)

Dedicated snapshot thread
  QueryDisplayConfig
  EnumDisplayMonitors
  GetDpiForWindow
  Compute fingerprint
    → completion queue
    → PostMessage(private WM_APP)

Window thread
  completion generation == current generation
    → ObserveTopology
  else
    → stale count + discard
```

初版曾使用 `Task.Run`。实测发现线程池工作线程创建 DPI 探测 HWND 后，其线程消息队列会保留一个 USER 对象直到线程池线程退出。虽然不是 HWND 泄漏，但资源上限会随工作线程漂移。因此最终实现改用单一专用线程，并在关闭时 `CompleteAdding + Join`；线程退出后 USER 对象回到稳定基线。

## 5. 生命周期顺序

启动：

1. 预热窗口类、WTS 和消息队列的一次性初始化；
2. 采集稳定资源基线；
3. 注册窗口类；
4. 创建隐藏窗口；
5. 注册当前会话通知；
6. 创建 Window Timer；
7. 启动专用采样线程；
8. 记录 Startup 代次；
9. 进入标准消息循环。

关闭：

1. 不再安排新采样；
2. 等待当前采样完成并处理 completion；
3. `KillTimer`；
4. `WTSUnRegisterSessionNotification`；
5. `DestroyWindow`；
6. 停止并 Join 采样线程；
7. `UnregisterClass`；
8. 采集结束资源快照。

## 6. 三轮真实观察

命令：

```powershell
dotnet run --project probes/LongGrid.Spikes.DisplayTopology `
  --configuration Release -- `
  --watch-seconds 3 --json
```

每轮在当前真实双屏混合 DPI 会话中观察 3 秒，没有主动制造系统变化：

| 指标 | 第 1 轮 | 第 2 轮 | 第 3 轮 |
|---|---:|---:|---:|
| 最终状态 | Ready | Ready | Ready |
| 最终代次 | 1 | 1 | 1 |
| 原因 | Startup | Startup | Startup |
| 后台快照 | 2 | 2 | 2 |
| 快照失败 | 0 | 0 | 0 |
| stale completion | 0 | 0 | 0 |
| Ready 转换 | 1 | 1 | 1 |
| WTS 注册/注销 | 成功/成功 | 成功/成功 | 成功/成功 |
| 窗口类注册/注销 | 成功/成功 | 成功/成功 | 成功/成功 |
| DPI 建议矩形应用 | 0 | 0 | 0 |
| USER | 1 → 1 | 1 → 1 | 1 → 1 |
| GDI | 0 → 0 | 0 → 0 | 0 → 0 |
| 进程句柄 | 255 → 255 | 255 → 255 | 255 → 255 |

## 7. 隐私与安全

默认报告不输出：

- monitor name/friendly name；
- adapter LUID、source/target ID；
- PNP、EDID 或 device path；
- 拓扑或路径指纹；
- HWND、窗口标题或其他进程信息；
- WTS session ID；
- 设备变化载荷。

观察模式不调用 `SetDisplayConfig`、`ChangeDisplaySettingsEx`、输入注入、窗口枚举或截图 API。

## 8. 结论

已验证：

- 真实顶层消息窗口可以无显示、无激活运行；
- WTS 注册/注销和窗口类注册/注销成对成功；
- WindowProc 与 CCD 采样解耦；
- 两次启动快照能通过真实消息循环进入 Ready；
- 专用采样线程退出后 USER/GDI/进程句柄回到稳定基线；
- CLI 参数限制为 2–30 秒；
- 输出保持脱敏。

尚未验证：

- 真实 `WM_DISPLAYCHANGE` 和 `WM_DEVICECHANGE` 风暴；
- 真实 `WM_DPICHANGED` 建议矩形；
- 睡眠、唤醒、锁屏、解锁、RDP 连接/断开；
- 负坐标、旋转、缩放、拔插和投影；
- 动态事件期间确实产生并丢弃 stale completion；
- DesktopHost Window Region、Composition、UIA Bounds 的同代提交；
- 事务失败回滚。

所以 P0-07 整体继续保持 Conditional。

## 9. P0-07b2b2b

下一阶段在受控实验室执行：

1. 负坐标与主屏切换；
2. 100%–400% DPI 热切换；
3. 90/180/270° 旋转；
4. 拔插、投影/克隆和睡眠恢复；
5. RDP/控制台往返；
6. 消息风暴、查询失败和 stale completion；
7. Region、Composition、UIA 同代提交与强制失败回滚。
