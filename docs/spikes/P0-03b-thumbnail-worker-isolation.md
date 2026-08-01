# P0-03b 缩略图工作进程隔离与 500 项预算

执行日期：2026-08-01

结果：**Conditional Pass（可回收进程、有界 BGRA 像素 IPC、硬超时恢复、父进程退出清理、连续超时退避和合成 500 项预算通过）**

关联：Issue #22

## 1. 目标

关闭 P0-03a 中“已进入 `IShellItemImageFactory.GetImage` 的调用无法被线程内取消”的主要风险：

1. 把可能不合作的缩略图调用放入短命工作进程；
2. 单请求硬超时后终止整个工作进程，而不是强杀 COM 线程；
3. 超时后自动启动新进程并继续服务；
4. 按请求数主动回收，限制第三方提供程序的长期资源累积；
5. 检测超长输入/输出、畸形响应、协议错配和 worker 异常退出，并在回收后继续服务；
6. 父进程退出后由 worker 自行终止，避免卡死调用遗留孤儿进程；
7. 连续硬超时按 50/100/200 ms 指数退避，成功响应后清零；
8. 将复制后的 BGRA32 像素以有界协议返回，拒绝格式、尺寸、步幅、长度、编码和请求上限错误；
9. 建立 500 请求延迟、CPU、内存、句柄和空闲 CPU 的首个预算；
10. 不对真实桌面、云文件、网络文件或第三方提供程序触发现场提取。

## 2. 安全范围

父进程在 `%TEMP%` 的随机专用目录生成一个 2×2 BMP，全部 500 次压力请求只访问该自有文件。路径通过重定向 stdin 的逐行 JSON 发给 worker，不出现在命令行、进程列表参数、报告或日志中。

工作进程返回成功、尺寸和耗时，不传递裸 `HBITMAP`。每个 worker 完成 100 项后关闭并重建。硬超时场景使用确定性的测试请求让 worker 在进入原生提取前挂起；父进程在 250 ms 后终止整个进程树，再用新 worker 提取同一自有 BMP。这样可以证明强制回收链路，而无需在用户机器上故意触发恶意或卡死的真实 provider。

IPC 请求/响应携带协议版本和有界 request ID、路径长度、尺寸；JSON 拒绝未知字段，并校验响应版本与 request ID。stdin 请求保持 64 KiB 上限，stdout 响应按像素最大负载设为 400,000 字符，两端都由有界逐行读取器在 JSON 解析前拒绝超限输入。故障矩阵覆盖畸形 JSON、错误协议版本、超长响应、超长请求和 worker 主动异常退出；父进程分别识别协议错误或 EOF，回收后均由新 worker 成功恢复。

协议 v2 增加可选 BGRA32 像素负载。Worker 通过 `GetDIBits` 把自有 `HBITMAP` 复制为 top-down、每像素 4 字节的托管缓冲，再销毁原生位图；父进程只接受最大 256×256、262,144 bytes 的负载，并复核响应尺寸、像素尺寸、格式、`stride == width × 4`、声明长度、解码长度及请求是否明确要求像素。逐行响应上限为 400,000 字符，以容纳最大负载的 base64 表示，同时仍在 JSON 解析前限制分配。故障矩阵分别拒绝错误格式、尺寸、步幅、长度、非法 base64、未请求的像素以及超过 256 的像素请求，每次回收后均恢复成功。

父进程为所有 worker 创建启用 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` 的 Windows Job Object，并在进程启动后立即分配；父进程退出导致 Job 句柄关闭时，内核回收仍存活的 worker。父进程 PID 同时由受控参数传入 worker，worker 打开父进程句柄并异步等待退出，形成托管监视与操作系统 Job 的双重兜底。矩阵另起一个父进程测试宿主，确认其在不执行 `Dispose` 的情况下退出后，卡死 worker 会在 10 秒门限内消失。连续三次强制超时分别触发 50、100、200 ms 退避，总计 350 ms，随后正常提取成功并清零超时连续计数。

## 3. 实测环境与命令

| 项目 | 值 |
|---|---|
| OS | Microsoft Windows NT `10.0.22621.0` |
| 架构 | x64 |
| 运行时 | .NET 8 / `net8.0-windows` |
| 输入 | 临时沙箱内自有 2×2 BMP |
| 预热 | 首次提取成功后再采空闲 CPU |
| 压力请求 | 500 |
| 每进程请求预算 | 100 |
| 普通请求硬超时 | 2,000 ms |
| 强制超时测试 | 250 ms |
| 空闲采样 | 750 ms |

```powershell
dotnet run --project probes/LongGrid.Spikes.ShellItemImages --configuration Release -- --worker-matrix --json
```

## 4. 结果

| 指标 | 实测 | 暂定预算 | 结果 |
|---|---:|---:|---|
| 成功请求 | 500/500 | 500/500 | Pass |
| worker 预热 | 2/2 轮成功 | 必须成功 | Pass |
| p50 往返 | 7.05 ms | 仅记录 | Pass |
| p95 往返 | 9.54 ms | ≤250 ms | Pass |
| 500 项总墙钟 | 6,843.12 ms | ≤30,000 ms | Pass |
| 预算回收 | 5 | 每 100 项一次 | Pass |
| 强制超时终止 | 1 | 必须终止 | Pass |
| 超时后恢复 | 成功 | 必须成功 | Pass |
| 连续超时退避 | 3 次超时、3 次退避、350 ms | 指数增长且可恢复 | Pass |
| 父进程退出/孤儿清理 | 父进程无清理退出，卡死 worker 随后退出 | 不遗留 worker | Pass |
| Job Object 兜底 | `KILL_ON_JOB_CLOSE` 配置并为每个 worker 分配 | 必须配置成功 | Pass |
| 受限令牌创建 | `DISABLE_MAX_PRIVILEGE` | 必须成功 | Pass |
| Low Integrity 复核 | SID RID `0x1000` | 必须为 Low | Pass |
| MIC 读/写边界 | 可读自有 BMP；不可在中完整性沙箱新建文件 | 读允许、write-up 阻断 | Pass |
| 父进程写控制组 | 可写入并清理控制文件 | 必须成功 | Pass |
| BGRA32 像素复制 | 2×2、stride 8、16 bytes | 格式/尺寸/长度一致 | Pass |
| 最大 BGRA32 负载 | 256×256、262,144 bytes | 必须完整通过 | Pass |
| 像素故障检测 | 格式/尺寸/步幅/长度/base64/未请求负载 | 全部拒绝并回收 | Pass |
| 像素尺寸请求上限 | 257 拒绝，随后恢复 | 最大 256 | Pass |
| 畸形响应检测/恢复 | 1/成功 | 必须检测并恢复 | Pass |
| 错误协议版本检测/恢复 | 1/成功 | 必须检测并恢复 | Pass |
| 超过 64 KiB 响应检测/恢复 | 1/成功 | 必须检测并恢复 | Pass |
| 超过 64 KiB 请求检测/恢复 | 1/成功 | 必须检测并恢复 | Pass |
| 异常退出检测/恢复 | 1/成功 | 必须检测并恢复 | Pass |
| worker 总 CPU | 562.5 ms | 首轮记录 | Pass |
| 750 ms 空闲 CPU | 0 ms | ≤50 ms | Pass |
| 峰值工作集 | 42,672,128 bytes | ≤268,435,456 bytes | Pass |
| 峰值句柄 | 417 | ≤512 | Pass |
| 沙箱清理 | 成功 | 必须成功 | Pass |

扩展像素与生命周期矩阵后的代表轮次由主探针共启动 22 个 worker：初始/预算回收进程、强制超时及恢复、通用协议错误、七类像素负载/请求错误及逐项恢复、异常退出，以及连续三次超时和最终恢复；另由独立父进程宿主启动一个卡死 worker 验证孤儿清理。报告只输出聚合指标，不输出路径、文件名、图像字节、HRESULT 或 Shell 身份。

首版测量曾在启动 worker 后立即采空闲窗口，其中一次得到 62.5 ms/750 ms 并触发预算失败。审计确认该窗口混入了进程启动、JIT、COM 和 Shell provider 初始化，不是稳定空闲状态。实现因此增加一次成功提取预热，再在同一进程采样；修正后连续两轮均为 0 ms。阈值没有因失败结果而放宽。

## 5. 结论

### 已证明

- 不合作调用可以通过终止整个工作进程获得确定性硬超时；
- 超时不会卡死父进程，下一请求由新 worker 成功处理；
- 超长输入/输出、畸形 JSON、错误协议版本和 worker 异常退出不会被接受为结果，下一 worker 可恢复；
- 父进程未执行清理而退出时，即使 worker 正在 Hang，worker 也会检测父进程句柄并自行退出；
- Windows Job Object 已启用 `KILL_ON_JOB_CLOSE`，每个 worker 启动后必须成功加入，否则父进程立即强杀该 worker 并使请求失败；
- 可以从当前进程令牌创建 `DISABLE_MAX_PRIVILEGE` 受限令牌，将其 Mandatory Integrity Level 设置并复核为 Low（RID `0x1000`）；
- 在该令牌的模拟上下文中，自有 BMP 仍可读取，但向默认中完整性/未标记沙箱执行 write-up 会被 MIC 阻断；退出模拟后父进程写控制组成功，排除了目录自身不可写造成的假阳性；
- Worker 不跨进程传递裸 `HBITMAP`，复制后的 BGRA32 缓冲具有明确格式、尺寸、步幅、长度和 256×256/262,144-byte 上限；
- 父进程会拒绝错误像素元数据、非法编码、未请求负载和超限请求，协议错误后新 worker 可恢复；
- 连续硬超时会指数退避，成功响应后恢复正常启动节奏；
- 协议版本、响应 request ID 和未知 JSON 字段受到校验；
- 正常 worker 可按固定请求预算回收；
- 500 次合成缩略图提取在本机落入暂定延迟、墙钟、内存、句柄和空闲 CPU 预算；
- 路径不需要进入命令行或默认诊断；
- 临时输入和工作目录在结束后完整清理。

### 尚未证明

- 正式 worker 仍使用调用者 token；当前只验证了进程内模拟的受限 Low Integrity 访问边界，尚未把该令牌接到 worker 创建、匿名管道与 Job Object 链路；
- 尚未决定 AppContainer、受限 Low Integrity worker 及文件访问 broker/句柄传递的最终产品边界；
- 当前像素负载使用匿名管道中的 base64，尚未验证共享内存、零拷贝或正式渲染表面集成；
- 500 次是对同一自有 BMP 的隔离/生命周期压力，不等于 500 个不同真实项目的 provider、缓存和渲染预算；
- 尚未覆盖 OneDrive、网络路径、第三方 provider、恶意文件、x64/ARM64 与支持的 Windows build 矩阵；
- 暂定预算仅来自当前机器，不能直接升级为发布 SLA。

因此 P0-03b 为 Conditional Pass，Issue #22 继续保持打开。它允许架构从“必须有独立进程”推进到“已有可回收硬超时原型”，但仍不允许把任意真实文件缩略图生成放入长期常驻、同权限的产品进程。

## 6. 下一切片

1. 用受限 Low Integrity token 启动实际 worker，并保持重定向管道、Job Object、硬超时和父进程退出清理；
2. 比较 AppContainer 与受限 token，明确文件访问 broker/句柄传递、Capability 和缓存边界；
3. 评估受控共享内存并接入正式渲染表面，保持相同长度/格式/尺寸上限；
4. 在合成不同格式集和专用云/网络/provider 环境执行 500 个不同项目矩阵；
5. 把支持矩阵实测结果提交负责人批准最终 CPU/内存/响应预算。

本轮安全语义依据 Microsoft 的 [Mandatory Integrity Control](https://learn.microsoft.com/windows/win32/secauthz/mandatory-integrity-control)、[`CreateRestrictedToken`](https://learn.microsoft.com/windows/win32/api/securitybaseapi/nf-securitybaseapi-createrestrictedtoken) 和 [`SetTokenInformation`](https://learn.microsoft.com/windows/win32/api/securitybaseapi/nf-securitybaseapi-settokeninformation) 文档。MIC 默认执行 no-write-up；本探针不把该结论扩大为完整沙箱或 broker 已完成。
