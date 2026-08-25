# Stage 189：PF-005B1 缩略图按需队列与缓存审计

- 日期：2026-08-24
- 分支：`codex/pf002d-create-preview`
- 起始基线：`8a6cc24`
- 对齐编号：`PF-005B1`
- 结论：`EngineeringComplete`；PF-005 顶层仍为 `InProgress`

## 1. 开始审计与切片边界

PF-005A 后复审发现，仓库虽然已有零 Capability AppContainer worker、协议 v6、32 MiB 受控副本、共享内存 BGRA32、250 ms 硬超时、Job 和 Profile 清理，但提取请求仍是 worker 程序集内部接口。正式 App 的生命周期控制器只在资源遥测受控会话启动 idle worker，DesktopHost 没有产品级按需队列、缓存或像素消费者。

因此 PF-005B 被拆为两个能分别验收的切片：

- **PF-005B1（本阶段）**：产品提取门面、懒启动、关闭零请求、首屏有界队列、缓存失效、有限失败和退出清理；
- **PF-005B2（下一阶段）**：从权威 workspace 建立候选、BGRA 帧进入 DesktopHost、持久化开关、过期请求取消/忽略与视觉回归。

本阶段不声称用户已经看到图片缩略图。

## 2. 实现审计

`RestrictedThumbnailWorkerRuntime` 新增串行化 `ExtractAsync` 产品门面。它只接受 1～256 像素、正超时和完整路径，通过现有受控副本、匿名共享内存和协议 v6 返回 BGRA32；结果明确区分 Success、TimedOut、WorkerExited、ProtocolError、HRESULT 和有限像素帧。并发调用由单一 `SemaphoreSlim` 串行，避免一个标准流协议出现乱序读取。

`ProductDesktopThumbnailRequestController` 新增以下产品合同：

- `enabled=false` 先停止既有 runtime，再返回零候选、零启动、零请求；
- 每轮只接受首 12 个可见候选，不因 500 项配置提交 500 个请求；
- runtime 首个合法图片 cache miss 才懒启动，并复核 1 worker、1 Profile、零 Capability AppContainer 和 Kill-on-Job-close；
- 只允许批准的图片扩展、普通文件、非重解析点、1～32 MiB；
- 缓存最多 64 项，键含规范化路径的 SHA-256 安全身份、长度、最后写入 ticks、像素尺寸和主题；完整路径只作为单次 worker 调用的局部变量，不进入缓存或返回结果；
- 250 ms 未完成、退出、协议错误、访问失败统一进入 `FailedFallback`，不回到主进程 Shell 提取；
- 关闭或 Dispose 时停止 worker 并确认自有 Profile 删除。

PF-005B2 可以消费匿名 item key、有限状态和 BGRA 帧，不需要把目标路径写入 DesktopHost 投影或 UIA。

## 3. 真实 Expected / Actual

| 场景 | Expected | Actual | 差异 |
| --- | --- | --- | --- |
| 图片开关关闭 | worker factory 0、请求 0 | 0 / 0 | 无 |
| 20 个真实 BMP 候选 | 最多 12 请求 | 12 | 无 |
| 同版本/尺寸/主题第二次读取 | 请求 0、cache hit 1 | 0 / 1 | 无 |
| 真实 BMP 长度/mtime 改变 | 重新请求 1 | 1 | 无 |
| 主题 light→dark | 重新请求 1 | 1 | 无 |
| 真实产品队列请求自有 BMP | ReadyThumbnail 或 FailedFallback | FailedFallback | 无；本机 250 ms 内未取得像素，按合同安全回退 |
| 随后关闭产品队列 | 请求 0、Profile 删除 | 0 / true | 无 |
| 真实 worker 自有 BMP | 像素成功或有限回退 | TimedOut=true、WorkerExited=true、278～280 ms、有限回退 | 无；未伪造像素成功 |
| 真实 Hang 100 ms | worker 被杀且总耗时 ≤500 ms | WorkerExited=true、约 102～103 ms | 无 |
| 真实 Exit | 观察到退出 | true | 无 |
| 最终自有 Profile | 已删除 | true | 无 |

真实测试在随机临时沙箱生成 BMP，启动真实 `LongGrid.ThumbnailWorker.exe` 和随机 AppContainer Profile；不是进程 mock。队列/缓存机械测试使用真实文件元数据和可计数 runtime，以精确证明请求数和失效原因。当前机器没有在 250 ms 预算内返回 BMP 像素，因此实际结果诚实保留为 `FailedFallback`；这满足稳定性合同，但不能替代 PF-005B2 的成功像素环境和视觉证据。

## 4. 差异与修正

| 轮次 | 预期 | 实际差异 | 修正 |
| --- | --- | --- | --- |
| 范围复审 | 直接把现有 worker 接入 UI | worker 只有内部协议，正式 App 无请求门面/缓存 | 先交付 B1 产品队列合同，再做 B2 HWND 消费 |
| 首轮证据 | 回退能由 HRESULT 分类 | 真实提取 `HRESULT=0` 但 `TimedOut/WorkerExited=true` | 补录传输级布尔事实与提取耗时，不把 0 误写为 Shell 成功 |
| 证据输出编译 | 同时记录提取/故障耗时 | 匿名对象出现两个 `RoundTripMilliseconds`，构建失败；随后旧二进制测试仍能运行 | 分别命名 Extraction/Timeout 字段，重新成功构建后再运行，拒绝使用陈旧测试结果 |
| 隔离复审 | 任意 runtime factory 均可使用 | 初版控制器懒启动后未再次复核隔离快照 | 增加 AppContainer、Job、worker/Profile 数量硬复核，失败立即 Dispose 并有限回退 |
| 全仓格式审计 | `dotnet format --verify-no-changes` 通过 | 既有标题策略 presentation 有两处属性声明换行不符合格式器；隐私收紧后新增 using 顺序不符 | 仅执行机械换行和 using 排序，行为不变；重跑格式门禁通过 |
| 缓存隐私复审 | 缓存使用安全身份 | 初版私有 cache key 仍直接持有完整路径 | 改为规范化路径 SHA-256；路径只在单次请求局部变量存在 |

## 5. 门禁与需求对齐

- 全量测试：1117/1117 通过；
- 全解决方案构建：0 warning、0 error；
- 静态 UI 合同：155 个 AutomationId，`Outcome=Pass`，并新增 PF-005B1 队列/缓存合同字段；
- 全仓格式门禁：`dotnet format --verify-no-changes --no-restore` 通过；
- 关闭零启动、12 请求上限、64 缓存、文件版本/尺寸/主题失效已有自动化证据；
- 真实 worker Hang/Exit、Profile 删除和当前机器提取回退已有结构化 Expected/Actual/Difference；
- 无桌面文件写入、无 Explorer Hook、无主进程缩略图提取、无路径进入 UIA/返回结果；
- 本阶段没有把 BGRA 帧接入正式 HWND，也没有设置页图片开关，因此 PF-005 保持 `InProgress`。

下一步固定为 **PF-005B2：像素帧正式绘制、持久化图片开关与过期请求处理**。验收必须包含成功像素环境或确定性受控帧的真实 HWND 绘制、开关关闭零请求、revision/主题/mtime 变化不接受过期结果、失败继续显示 PF-005A 类型图标，以及 UIA 路径隐私不回退。
