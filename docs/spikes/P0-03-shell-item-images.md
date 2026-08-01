# P0-03：Shell 图标、缩略图与原生句柄稳定性

执行日期：2026-07-30

结果：**Conditional Pass（主进程资源闭环通过；P0-03b 已验证工作进程硬超时与合成 500 项预算）**

前置：P0-01a/P0-01b/P0-01c/P0-02

## 1. 目标

验证 Long Grid 能否在不修改真实桌面的前提下：

1. 使用公开 Shell API 获取文件和目录图标；
2. 以缓存优先方式探测缩略图，不在缓存未命中时请求现场提取；
3. 将慢请求移出 UI 线程并限制并发；
4. 取消尚未开始的请求；
5. 对每个返回的 `HBITMAP` 执行确定性释放；
6. 在 500 次连续提取后不产生 GDI 句柄增长；
7. 默认不输出桌面项目名称、路径、扩展名、Shell 身份或错误明细。

## 2. 官方行为与所有权结论

[`IShellItemImageFactory::GetImage`](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitemimagefactory-getimage) 返回调用方拥有的 `HBITMAP`，调用方必须使用 `DeleteObject` 释放。微软同时明确说明提取可能耗时，不应在 UI 线程执行；UI 线程只适合使用 `SIIGBF_INCACHEONLY` 的无阻塞探测。

探针通过 [`SHCreateItemFromParsingName`](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-shcreateitemfromparsingname) 创建 Shell 项目，并在每个执行线程上调用 `CoInitializeEx`/`CoUninitialize`。`HBITMAP` 立即交给 `SafeHandle`，所有成功路径、失败路径和异常路径都由同一个所有权对象收口。

关键边界：

```text
排队中的请求       可以用 CancellationToken 取消
已进入 GetImage    不能安全地在线程内强制中断
真正的硬超时       必须终止并重建独立工作进程
```

因此产品文档中的“可取消、有超时”不能解释为强杀 COM 线程。应用内取消只表示结果不再需要、后续请求不再启动；不受信任或可能卡死的缩略图提供程序必须由低权限、可回收的工作进程承载。

## 3. 实现

新增 `LongGrid.Spikes.ShellItemImages`：

- 读取用户与 Public Desktop 顶层项目，真实桌面全程只读；
- 先预热 Shell 类型和提供程序，再采集 GDI 基线；
- 并发上限固定为 4；
- 图标压力阶段使用 `SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK`；
- 缓存图标阶段使用 `SIIGBF_ICONONLY | SIIGBF_INCACHEONLY`；
- 缓存缩略图阶段使用 `SIIGBF_THUMBNAILONLY | SIIGBF_INCACHEONLY`；
- 每个成功 `HBITMAP` 在读取尺寸元数据后立即 `DeleteObject`；
- Core 的 `BoundedAsyncExecutor` 负责排队、公平释放和合作式取消；
- 报告只包含聚合计数、延迟、并发峰值和句柄差值。

缓存未命中属于正常结果，不代表项目没有图标或缩略图提供程序。首次 UI 展示应使用通用/类型图标占位，后台成功后再替换，不允许因等待缩略图阻塞桌面。

## 4. 安全与隐私

- 未创建、移动、重命名或删除任何真实桌面项目；
- 未把 `HBITMAP` 或缩略图写入磁盘；
- 缩略图只使用缓存模式，缓存未命中时不请求现场提取；具体云提供程序是否发生额外 I/O 仍需兼容矩阵验证；
- 未输出名称、路径、扩展名、PIDL、文件身份或逐项 HRESULT；
- 未使用 Explorer 注入、Hook、内部 XAML、`Progman` 或 `WorkerW`；
- COM 对象、COM apartment、`HBITMAP` 和进程句柄均对称释放。

生产缓存也必须保持本地、按容量和时间淘汰，并使用不包含原始路径的键；诊断默认只记录结果分类和耗时分桶。

## 5. 实测环境

| 项目 | 值 |
|---|---|
| OS | Microsoft Windows NT `10.0.26200.0` |
| 架构 | x64 |
| .NET SDK | `8.0.419` |
| Target Framework | `net8.0-windows` |
| 物理桌面项目 | 96 |
| 文件项目（缓存缩略图探测） | 13 |
| 后台图标压力请求 | 每轮 500 |
| 并发上限 | 4 |

## 6. 三轮结果

| 指标 | 第 1 轮 | 第 2 轮 | 第 3 轮 |
|---|---:|---:|---:|
| 预热图标成功 | 96/96 | 96/96 | 96/96 |
| 缓存图标命中 | 17/96 | 17/96 | 17/96 |
| 缓存缩略图命中 | 1/13 | 1/13 | 1/13 |
| 压力图标成功 | 500/500 | 500/500 | 500/500 |
| 压力阶段最大并发 | 4 | 4 | 4 |
| 压力阶段 p50 | 19.33 ms | 24.16 ms | 17.74 ms |
| 压力阶段 p95 | 77.28 ms | 135.54 ms | 43.67 ms |
| 成功/释放 `HBITMAP` | 518/518 | 518/518 | 518/518 |
| GDI 句柄基线 → 结束 | 41 → 41 | 42 → 42 | 41 → 41 |
| GDI 净增量 | 0 | 0 | 0 |
| 排队取消被观察 | 是 | 是 | 是 |
| 已取消任务未启动 | 是 | 是 | 是 |

预热前后 Shell 会一次性创建约 41–42 个进程级 GDI 对象，因此泄漏基线必须在提供程序预热后采集。预热后连续三轮净增量均为 0，且所有 1,554 个被计入的成功图像句柄均已释放。

## 7. 结论

### 已通过

- 公开 Shell API 可以覆盖当前真实桌面的图标提取；
- 500 次后台请求三轮全部成功；
- 并发峰值始终等于配置上限 4，没有越界；
- 排队取消可靠，异常和取消后信号量许可不会丢失；
- `HBITMAP` 成功数与释放数严格一致；
- 预热后的 GDI 句柄三轮净增量均为 0；
- 缓存缩略图探测不会因未命中转入内容提取。

### 条件与未验证范围

- `GetImage` 原生调用无法被 `CancellationToken` 硬中断；
- P0-03b 已实现并压力验证受限 Low Integrity 可回收工作进程、挂起后先入 Job、最小句柄继承、有界 BGRA32 像素 IPC 和 worker 写阻断；broker、共享内存/渲染集成和真实 provider 矩阵仍未完成；
- 缩略图只验证了缓存命中路径，没有读取文件内容或强制生成；
- 尚未覆盖 ARM64、Windows 10、网络目录、OneDrive 在线/离线矩阵、恶意或崩溃的第三方缩略图提供程序；
- 尚未验证 alpha、色彩、主题、DPI、缩放质量、内存缓存淘汰和 UI 滚动时序；
- 500 次重复提取验证的是资源生命周期，不等同于 500 个同时可见项目的渲染性能。

所以本探针为 **Conditional Pass**。它足以支持下一阶段的占位图标和后台加载架构，但不授权把第三方缩略图提供程序直接放进长期常驻主进程。

## 8. 产品实现约束

1. 首屏只依赖内存/磁盘缓存和通用类型图标；
2. UI 线程不得执行可能缓存未命中的 `GetImage`；
3. 主进程使用有界队列，默认并发从 2–4 起调；
4. 滚出视口、项目删除、主题/DPI/尺寸变化时取消排队请求并丢弃过期结果；
5. 缓存未命中提取进入低权限工作进程，超时后回收整个工作进程；
6. 工作进程返回复制后的像素/受控共享内存，不跨进程传递裸 `HBITMAP` 所有权；
7. OneDrive 占位和网络项目默认保持缓存模式，只有明确策略或用户动作允许取内容；
8. 所有失败统一降级为类型图标，并保留重试退避，不能形成刷新风暴；
9. 持续记录队列长度、命中率、p95/p99、超时、工作进程重启和 GDI/USER 句柄趋势；
10. 真正产品代码需把当前探针互操作层迁入 Windows Infrastructure，并保持 Core 无 Win32/COM 类型。

## 9. 后续

1. P0-04/P0-05：比较每容器 HWND 与每显示器 HWND；
2. 继续 P0-03b：比较 AppContainer/受限 token 与 broker 边界，再推进受控共享内存/渲染表面集成；实际 Low Integrity worker、有界 BGRA32 IPC、写阻断与父进程 PID/Job Object 双重退出清理已有探针证据；
3. OneDrive/网络/第三方提供程序兼容矩阵；
4. 500 个不同项目的内存缓存、滚动取消、主题/DPI 失效和渲染预算；
5. 在完整低权限工作进程和真实 provider 矩阵通过前，MVP 可以发布类型图标和缓存缩略图，但不得承诺任意文件的即时缩略图。

工作进程隔离、硬超时恢复和首轮预算详见[P0-03b 缩略图工作进程隔离报告](P0-03b-thumbnail-worker-isolation.md)。
