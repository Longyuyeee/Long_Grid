# P0-07b2b1：显示变化事件合并、稳定采样与代次失效

执行日期：2026-07-30

结果：**Conditional Pass（Core 稳定器通过；真实 Win32 消息接入与窗口事务仍需 P0-07b2b2）**

前置：P0-07b1、P0-07b2a

## 1. 风险

显示器旋转、缩放、拔插、投影、睡眠恢复和 RDP 切换不会以单一原子事件完成。若收到任意一条通知后立即恢复布局，可能发生：

- 在 CCD 中间态上生成错误映射；
- 连续多次搬动容器；
- 旧查询覆盖较新拓扑；
- 会话锁定或睡眠期间创建窗口；
- 两次紧邻查询被误判为“稳定”；
- 持续抖动无限延长恢复流程。

本子阶段建立纯 Core 状态机，只在稳定证据满足时授权创建 P0-07b2a 恢复计划。

## 2. 官方消息边界

[`WM_DISPLAYCHANGE`](https://learn.microsoft.com/windows/win32/gdi/wm-displaychange) 在显示分辨率变化时广播，只提供色深与屏幕宽高，不能作为完整拓扑快照。

[`WM_DPICHANGED`](https://learn.microsoft.com/windows/win32/hidpi/wm-dpichanged) 在窗口有效 DPI 变化时发送，并携带建议矩形。DesktopHost 必须先采用建议矩形；该消息同时把拓扑标记为脏，但不能单独证明整个显示系统已稳定。

[`WM_POWERBROADCAST`](https://learn.microsoft.com/windows/win32/power/wm-powerbroadcast) 提供挂起与恢复事件。挂起后稳定器进入 `Paused`，恢复事件开始新代次。

[`WM_WTSSESSION_CHANGE`](https://learn.microsoft.com/windows/win32/termserv/wm-wtssession-change) 覆盖控制台/远程连接、断开、锁定、解锁和桌面就绪。生产窗口必须先注册通知；不可用会话暂停恢复，重新可用后重新采样。

上述消息都是“重新确认事实”的触发器，而不是事实本身。最终事实仍来自 P0-07b1 的只读 CCD + monitor + DPI 快照。

## 3. 状态机

```text
Idle
  └─ change/resume → WaitingQuietPeriod
       ├─ new change → 延长静默期、保留首次截止时间
       ├─ quiet elapsed → Sampling
       ├─ suspend/session unavailable → Paused
       └─ maximum wait → TimedOut

Sampling
  ├─ identical sample after interval → Ready
  ├─ different sample → 重新累计
  ├─ new change → WaitingQuietPeriod
  └─ maximum wait → TimedOut

Ready
  ├─ same sample → Ready
  ├─ new signal → 新代次
  └─ fingerprint changed without signal → 合成新代次

Paused
  └─ resume/session available → 新代次
```

## 4. 默认门槛

| 参数 | 默认值 | 目的 |
|---|---:|---|
| QuietPeriod | 750 ms | 合并连续系统通知 |
| SampleInterval | 250 ms | 防止两次紧邻查询假稳定 |
| RequiredIdenticalSamples | 2 | 要求连续一致拓扑 |
| MaximumWait | 10 s | 防止持续抖动永久等待 |

这些是 Phase 0 初始安全值，不是最终性能预算。P0-07b2b2 必须用真实事件时间线测量后调整。

## 5. 代次和过期结果

每个外部信号都会递增 `Generation`。同一未稳定突发：

- 保留首次事件时间；
- 合并所有 `DisplayChangeReason`；
- 更新最后事件时间以延长静默期；
- 不延长 10 秒总截止时间；
- 清除已累计的候选样本。

恢复计划必须携带生成它的代次。应用前再次比较稳定器当前代次；不同则丢弃计划。`Ready` 后即使没有收到系统通知，只要下一只读快照指纹不同，也会用 `TopologySampleChanged` 合成新代次。

## 6. 暂停与超时

`PowerSuspend` 和 `SessionUnavailable` 进入 `Paused`。暂停期间拓扑样本不会推进状态。只有 `PowerResume` 或 `SessionAvailable` 才能开始新的静默与采样周期。

连续事件或不一致快照达到 10 秒时进入 `TimedOut`：

- 不创建恢复计划；
- DesktopHost 保持隐藏/安全状态；
- UI 显示“显示器仍在变化”；
- 等待下一次明确变化或用户重试开始新代次。

## 7. 隐私

稳定器只比较进程内拓扑指纹：

- 结果不返回指纹；
- 不记录单屏身份或硬件字段；
- 对外只暴露状态、代次、原因集合、连续样本数和下一动作时间；
- 默认诊断只能记录原因类别和耗时，不能记录可关联拓扑散列。

## 8. 自动化结果

新增 12 个测试：

1. 静默期前拒绝采样；
2. 连续信号延长静默期并合并原因；
3. 两次按间隔一致采样进入 Ready；
4. 采样过近不重复计数；
5. 不同指纹重置连续计数；
6. 新系统信号使 Ready 代次失效；
7. 未收到信号但指纹变化时自我失效；
8. 不稳定拓扑达到总超时；
9. 持续系统信号不能无限延长总超时；
10. 暂停期间忽略样本，恢复后新建代次；
11. 时钟倒退被拒绝；
12. 不安全的间隔/样本配置被拒绝。

解决方案测试由 38 增至 50，全部通过。

## 9. 结论

已验证：

- Core 不依赖 Win32、UI 或真实定时器；
- 静默期与采样间隔语义分离；
- 同一事件突发不会无限重置总超时；
- Ready 结果会被新信号或漏通知后的指纹变化作废；
- 睡眠和会话不可用状态不会继续恢复；
- 输出不泄露拓扑指纹。

尚未验证：

- 真实消息窗口与 `WTSRegisterSessionNotification` 生命周期；
- 消息风暴中的实际时间分布；
- CCD 查询失败/缓冲不足与稳定器的重试调度；
- `WM_DPICHANGED` 建议矩形的窗口级应用；
- 负坐标、旋转、缩放、拔插、投影、睡眠和 RDP；
- Region、Composition、UIA Bounds 的事务提交和失败回滚。

## 10. P0-07b2b2

下一子阶段：

1. 建立只接收公开 Windows 消息的隐藏顶层窗口；
2. 将消息转换为稳定器原因，不在 WindowProc 内同步枚举；
3. 后台执行 P0-07b1 快照并校验代次；
4. Ready 后生成 P0-07b2a 计划；
5. 实现可回滚的 Window Region、Composition、UIA 同代事务；
6. 在受控实验室执行完整动态矩阵。
