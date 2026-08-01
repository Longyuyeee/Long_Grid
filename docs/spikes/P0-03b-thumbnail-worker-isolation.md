# P0-03b 缩略图工作进程隔离与 500 项预算

执行日期：2026-08-02

结果：**Conditional Pass（真实零 Capability AppContainer worker、六个自有格式/编码样本的父进程与双 build 输入策略矩阵、有界共享内存 BGRA IPC、未代理读写阻断、ACL/Profile 清理和合成 500 项预算通过）**

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

父进程在 `%TEMP%` 的随机专用目录生成 BMP、PNG、GIF、JPEG、未压缩 RGB TIFF 和 LZW TIFF 六个确定性自有样本；500 次压力仍只重复访问自有 BMP。矩阵先在普通父进程中调用相同 Shell API，并只记录固定格式标签、HRESULT、尺寸及扩展级 handler 注册/模块健康布尔值；不输出 handler 身份、CLSID、厂商或路径。随后 Client 创建随机 AppContainer Profile，把当前 worker 运行时暂存到受 128 MiB 总量约束的 Profile 私有目录，并以两种授权策略逐项对照。

工作进程返回成功、尺寸和耗时，不传递裸 `HBITMAP`。每个 worker 完成 100 项后关闭并重建。硬超时场景使用确定性的测试请求让 worker 在进入原生提取前挂起；父进程在 250 ms 后终止整个进程树，再用新 worker 提取同一自有 BMP。这样可以证明强制回收链路，而无需在用户机器上故意触发恶意或卡死的真实 provider。

IPC 请求/响应携带协议版本和有界 request ID、路径长度、尺寸；JSON 拒绝未知字段，并校验响应版本与 request ID。stdin 请求保持 64 KiB 上限，stdout 响应按像素最大负载设为 400,000 字符，两端都由有界逐行读取器在 JSON 解析前拒绝超限输入。故障矩阵覆盖畸形 JSON、错误协议版本、超长响应、超长请求和 worker 主动异常退出；父进程分别识别协议错误或 EOF，回收后均由新 worker 成功恢复。

协议 v6 沿用 v5 的受控副本和 v4 的匿名页文件映射 BGRA32 transport，并增加 `MinimumPathAcl` 对照。`ControlledCopy` 仍为默认：源必须是普通文件、不得是重解析点、单文件最大 32 MiB、client 总计最大 64 MiB；副本放入随机 AppContainer Profile 私有存储并设为只读。最小 ACL 只用于探针自有文件：父进程给随机 AppContainer SID 在父目录增加无继承 Traverse ACE、在文件增加无继承 Read ACE，请求结束后精确删除并复核文件和目录均无该 SID 的显式规则。Server 拒绝 `DirectPath`，只接受这两种已声明 transport。最小 ACL 保留原路径但会短时修改 DACL，父进程异常退出时的 ACE 日志/修复和并发 DACL 变化尚未验证，因此不提升为默认。

父进程按像素请求创建最大 262,144 bytes、不可执行的映射，再用 `DuplicateHandle` 向目标 AppContainer worker 复制不可继承的单次句柄；请求只携带目标句柄值和固定容量。Worker 通过 `GetDIBits` 写入映射并关闭目标句柄；父进程只读并复核 transport、尺寸、格式、步幅、长度、容量和实际内容。故障矩阵继续覆盖九类像素/映射错误并逐项恢复。

父进程为所有 worker 创建启用 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` 的 Windows Job Object。worker 以零 Capability AppContainer 挂起启动，只继承 stdin/stdout 和指向 `NUL` 的 stderr；父进程先分配 Job、查询 `TokenIsAppContainer`，再恢复主线程。AppContainer 不需要也不尝试打开父进程句柄，异常父退出完全由内核 Job 回收。矩阵另起一个父进程测试宿主，确认其不执行 `Dispose` 退出后卡死 worker 消失，并由主探针按 ready 信号中的受限 Profile 名删除遗留 Profile。正常 `Dispose` 也在 worker/Job 释放后删除 Profile。

## 3. 实测环境与命令

| 项目 | 值 |
|---|---|
| OS | Microsoft Windows NT `10.0.22621.0` |
| 架构 | x64 |
| 运行时 | .NET 8 / `net8.0-windows` |
| 输入 | 临时沙箱内自有 BMP/PNG/GIF/JPEG/TIFF-RGB/TIFF-LZW；500 次压力使用 2×2 BMP |
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
| p50 往返 | 25.18 ms | 仅记录 | Pass |
| p95 往返 | 29.70 ms | ≤250 ms | Pass |
| 500 项总墙钟 | 16,319.89 ms | ≤30,000 ms | Pass |
| 预算回收 | 5 | 每 100 项一次 | Pass |
| 强制超时终止 | 1 | 必须终止 | Pass |
| 超时后恢复 | 成功 | 必须成功 | Pass |
| 连续超时退避 | 3 次超时、3 次退避、350 ms | 指数增长且可恢复 | Pass |
| 父进程退出/孤儿清理 | 父进程无清理退出，卡死 worker 随后退出 | 不遗留 worker | Pass |
| Job Object 兜底 | `KILL_ON_JOB_CLOSE` 配置并为每个 worker 分配 | 必须配置成功 | Pass |
| 实际 worker 隔离 | 所有 worker 的 `TokenIsAppContainer` 为 true | 必须全部为 AppContainer | Pass |
| 启动顺序 | `CREATE_SUSPENDED` → 加入 Job → `ResumeThread` | 不允许入 Job 前执行 | Pass |
| 句柄继承 | `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` 仅 stdin/stdout/stderr | 显式最小列表 | Pass |
| worker 未代理写入 | 实际 worker 新建父进程沙箱文件被拒绝 | 必须阻断 | Pass |
| worker 未代理读取 | 实际 worker 读取原始未授权标记文件被拒绝 | 必须阻断 | Pass |
| 受控输入副本 | 32 MiB 上限、拒绝重解析点、只读；协议 v6 默认 `ControlledCopy` | 不向 worker 发送原始路径 | Pass |
| 最小路径 ACL 对照 | 原路径直接读/提取、相邻拒绝、全部 worker AppContainer、lease 授予、显式 ACE 恢复、Profile 删除 | 仅探针自有文件；正常路径不得遗留随机 SID ACE | Pass |
| 自有格式矩阵 | 父进程基线 + BMP/PNG/GIF/JPEG/TIFF-RGB/TIFF-LZW × `ControlledCopy`/`MinimumPathAcl`；逐项复核输入可读、Shell 成功或精确安全拒绝、同格式策略一致、ACL/Profile 清理 | 父进程 6/6、worker 12/12 必须精确安全分类；不读取用户文件 | Pass |
| 实际 worker Profile 清理 | 正常 Dispose 与父进程无清理退出均删除随机 Profile | 不遗留 Profile | Pass |
| AppContainer Profile/Capability | 随机临时 Profile；CapabilityCount 0 | 不授予宽泛 Capability；结束删除 Profile | Pass |
| AppContainer 启动顺序/令牌 | 3 个进程挂起启动、先入 Job、`TokenIsAppContainer` | 全部为 AppContainer，入 Job 后恢复 | Pass |
| AppContainer 标准流 | `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` 仅继承父进程打开的 `NUL` 输入/输出 | 不继承其他父进程句柄 | Pass |
| AppContainer 文件边界 | no-op 0；精确 SID ACL 控制文件 0；相邻未授权文件 1 | 进程可运行、显式授权可读、未授权拒绝 | Pass |
| 受限令牌创建 | `DISABLE_MAX_PRIVILEGE` | 必须成功 | Pass |
| Low Integrity 复核 | SID RID `0x1000` | 必须为 Low | Pass |
| MIC 读/写边界 | 可读自有 BMP；不可在中完整性沙箱新建文件 | 读允许、write-up 阻断 | Pass |
| 父进程写控制组 | 可写入并清理控制文件 | 必须成功 | Pass |
| BGRA32 像素复制 | 2×2、stride 8、16 bytes | 格式/尺寸/长度一致 | Pass |
| 最大 BGRA32 负载 | 256×256、262,144 bytes | 必须完整通过 | Pass |
| 共享内存内容 | 2×2 与最大负载均观察到非零写入 | 必须由 worker 实际写入 | Pass |
| 映射句柄/容量 | 缺失句柄、262,143-byte 错误容量均拒绝并恢复 | 必须精确为 262,144 bytes | Pass |
| 像素故障检测 | 格式/尺寸/步幅/长度/旧 inline 编码/未请求负载 | 全部拒绝并回收 | Pass |
| 像素尺寸请求上限 | 257 拒绝，随后恢复 | 最大 256 | Pass |
| 畸形响应检测/恢复 | 1/成功 | 必须检测并恢复 | Pass |
| 错误协议版本检测/恢复 | 1/成功 | 必须检测并恢复 | Pass |
| 超过 64 KiB 响应检测/恢复 | 1/成功 | 必须检测并恢复 | Pass |
| 超过 64 KiB 请求检测/恢复 | 1/成功 | 必须检测并恢复 | Pass |
| 异常退出检测/恢复 | 1/成功 | 必须检测并恢复 | Pass |
| worker 总 CPU | 1,515.625 ms | 首轮记录 | Pass |
| 750 ms 空闲 CPU | 0 ms | ≤50 ms | Pass |
| 峰值工作集 | 41,852,928 bytes | ≤268,435,456 bytes | Pass |
| 峰值句柄 | 341 | ≤512 | Pass |
| 沙箱清理 | 成功 | 必须成功 | Pass |

### Windows build 兼容性分支

同一实现目前出现两个可复现结果：

| 环境 | 受控副本 | 最小路径 ACL | 判定 |
|---|---|---|---|
| Windows `10.0.22621` x64 本机 | 父进程 BMP/PNG/GIF/JPEG 成功；TIFF-RGB/TIFF-LZW 精确 `0x8007007E`，且两者均观察到扩展级 handler 已注册但模块缺失 | 两种 worker 策略与父进程逐格式完全一致；12/12 安全分类；500/500；p95 48.19 ms；ACL/Profile 清理 | `ExtractionSupported` + `StaleSpecificHandlerRegistration` |
| Windows `10.0.26100` x64 GitHub runner | 父进程 6/6 提取成功；六项均未观察到扩展级特定 handler 或陈旧注册 | 两种 worker 策略 12/12 输入可读，Shell 全部精确 `0x80070005`；ACL/Profile 清理 | `AccessDeniedSafely` / `ProductFallbackRequired` |

矩阵先要求普通父进程每项只能成功或精确返回 `ERROR_MOD_NOT_FOUND (0x8007007E)`，再要求每个 worker 授权输入可直接读取且 Shell 只能成功、精确 `E_ACCESSDENIED` 或精确 `ERROR_MOD_NOT_FOUND`。同一样本的两种授权策略必须得到完全相同的分类和 HRESULT；worker 是否逐格式匹配父进程、跨格式是否一致都作为诊断证据，不强制不同隔离级别或 provider 产生相同结果。22621 上两种 TIFF 编码在父进程和 worker 中都命中陈旧扩展 handler 注册并精确模块缺失，证明此前结果不是 AppContainer 特有；26100 父进程六项全成功，而 12 个 worker 调用全部输入可读又被 `0x80070005` 拒绝，反而把该环境的失败明确定位到 AppContainer/Shell 边界。两类安全失败都不允许回退到主进程或 Low Integrity 现场提取；产品只能显示类型图标或已验证缓存。

五格式首次运行时，探针错误地把“所有格式结果相同”设为成功条件，因此在 TIFF 精确返回 `0x8007007E` 时正确暴露 Fail。审计后没有放宽 HRESULT 白名单，而是纠正了错误假设：矩阵的职责是发现格式/provider 差异，同格式的两种输入策略一致才是本切片要验证的因果条件；`UniformOutcomeAcrossFormats=false` 继续保留在报告中。

扩展共享内存与生命周期矩阵后的代表轮次由主探针启动压力/预算回收、强制超时、协议/像素故障和恢复 worker；另由独立父进程宿主验证孤儿清理，并由父进程基线与两个短命 client 跑六个基线及十二个格式/策略组合。报告不输出路径、文件名、图像字节、句柄值、handler 身份、CLSID、厂商或模块路径；为区分跨 Windows build 的文件授权与 provider 失败，只保留固定格式标签、布尔分类和 HRESULT。

受限 worker 首次合入后的主干 CI 暴露了父进程退出测试的 ready-file 竞态：子宿主直接创建最终文件时，主探针可能在写句柄关闭前因“文件已存在”而读取，触发 sharing violation。修复后子宿主先完整写入同目录 `.pending` 文件并关闭句柄，再以原子重命名发布就绪信号；本地连续三轮完整矩阵均通过。该修复不放宽等待时间或性能预算。

首版测量曾在启动 worker 后立即采空闲窗口，其中一次得到 62.5 ms/750 ms 并触发预算失败。审计确认该窗口混入了进程启动、JIT、COM 和 Shell provider 初始化，不是稳定空闲状态。实现因此增加一次成功提取预热，再在同一进程采样；修正后连续两轮均为 0 ms。阈值没有因失败结果而放宽。

## 5. 结论

### 已证明

- 不合作调用可以通过终止整个工作进程获得确定性硬超时；
- 超时不会卡死父进程，下一请求由新 worker 成功处理；
- 超长输入/输出、畸形 JSON、错误协议版本和 worker 异常退出不会被接受为结果，下一 worker 可恢复；
- 父进程未执行清理而退出时，即使 worker 正在 Hang，`KILL_ON_JOB_CLOSE` 也会回收 worker；主探针随后删除异常路径遗留的临时 Profile；
- Windows Job Object 已启用 `KILL_ON_JOB_CLOSE`，每个 worker 启动后必须成功加入，否则父进程立即强杀该 worker 并使请求失败；
- 实际 worker 通过 `CreateProcessW` 与零 Capability `SECURITY_CAPABILITIES` 进入 AppContainer；主线程先挂起，在加入 Job Object 并复核 token 后才恢复；
- `STARTUPINFOEX` 的句柄列表只允许子进程继承 stdin、stdout 和指向 `NUL` 的 stderr，父进程管道端显式清除继承标记；
- 父进程查询所有实际 worker 均为 AppContainer；worker 能提取受控 BMP 副本，但对原始未授权标记文件的读写均被阻断；
- 协议 v6 明确区分 `DirectPath`、`ControlledCopy` 与 `MinimumPathAcl`，真实提取拒绝前者；父进程对源文件执行 32 MiB 上限和重解析点拒绝，副本仍为默认；
- 最小路径 ACL 能让 AppContainer 直接读取并在 22621 完成 Shell 提取，同时阻断相邻文件；正常请求结束后文件/目录的随机 SID ACE 均被复核清除；
- 22621 的 TIFF-RGB/TIFF-LZW 在父进程和两种 worker 策略下都精确返回 `ERROR_MOD_NOT_FOUND`，并观察到扩展级 handler 已注册但模块缺失；这不是 AppContainer 特有失败；
- 26100 父进程六样本全部成功且未观察到陈旧扩展 handler，两个 AppContainer 策略的十二次调用则全部输入可读并精确返回 `E_ACCESSDENIED`；
- 正常回收会在 worker/Job 释放后删除 Profile；无清理父退出由独立主探针在确认孤儿退出后删除 Profile；
- 可以用随机临时 Profile 和零 Capability `SECURITY_CAPABILITIES` 创建 AppContainer 进程；三个控制进程均在挂起状态加入生命周期 Job、复核 `TokenIsAppContainer` 后才恢复，且 Profile 在结束时删除；
- AppContainer no-op 控制成功；父进程只在探针自有 broker 子目录为该随机 AppContainer SID 增加只读/遍历 ACL 后，控制文件可读；同级未授权标记仍被拒绝。标准流仅继承显式列出的父进程 `NUL` 句柄，避免把缺失控制台误判为读取失败；
- 可以从当前进程令牌创建 `DISABLE_MAX_PRIVILEGE` 受限令牌，将其 Mandatory Integrity Level 设置并复核为 Low（RID `0x1000`）；
- 在该令牌的模拟上下文中，自有 BMP 仍可读取，但向默认中完整性/未标记沙箱执行 write-up 会被 MIC 阻断；退出模拟后父进程写控制组成功，排除了目录自身不可写造成的假阳性；
- Worker 不跨进程传递裸 `HBITMAP`，复制后的 BGRA32 缓冲具有明确格式、尺寸、步幅、长度和 256×256/262,144-byte 上限；
- 正常像素字节不再进入 JSON/base64；父进程通过 `DuplicateHandle` 向当前 worker 授予匿名映射句柄，worker 用后关闭，父进程只读复核；
- 目标 worker 的映射句柄不可继承且仅授予 `FILE_MAP_WRITE`，不复制父进程映射句柄的完整访问权；
- 映射句柄缺失、容量不等于 262,144 bytes 或共享内存未出现有效内容时矩阵失败；
- 父进程会拒绝错误像素元数据、非法编码、未请求负载和超限请求，协议错误后新 worker 可恢复；
- 连续硬超时会指数退避，成功响应后恢复正常启动节奏；
- 协议版本、响应 request ID 和未知 JSON 字段受到校验；
- 正常 worker 可按固定请求预算回收；
- 500 次合成缩略图提取在本机落入暂定延迟、墙钟、内存、句柄和空闲 CPU 预算；
- 路径不需要进入命令行或默认诊断；
- 临时输入和工作目录在结束后完整清理。

### 尚未证明

- ADR-0002 的 AppContainer + 父进程 broker 方向已有真实 worker、受控副本和最小路径 ACL 对照，但安全负责人尚未确认；缓存/水合政策与原路径语义边界尚未完成；
- 最小路径 ACL 的正常恢复已证明，父进程在 lease 存活时异常退出、并发 DACL 修改和遗留 ACE 修复尚未证明，因此不能用于任意用户文件；
- 当前 Shell item 通过 `SHCreateItemFromParsingName` 的路径/parsing name 创建，再调用 `IShellItemImageFactory.GetImage`；原始文件句柄不能直接替换该参数，handle-backed 方案必须另建 Shell item/provider、stream 或受控 decoder 合同；
- `CreateProcessW` + `SECURITY_CAPABILITIES` 的会话和支持矩阵目前只在本机验证，尚未覆盖标准用户、企业策略、Windows 10 最低 build 与 ARM64；
- 当前匿名映射仍复制到父进程托管缓冲，尚未验证正式渲染表面、跨 GPU 适配器资源或端到端零拷贝；
- 500 次是对同一自有 BMP 的隔离/生命周期压力；六个自有格式/编码样本也不等于 500 个不同真实项目的 provider、缓存和渲染预算；
- 尚未覆盖 HEIF、Office/PDF、OneDrive、网络路径、受控安装的第三方 provider、恶意文件、x64/ARM64 与完整支持 Windows build 矩阵；本轮只读识别陈旧注册，不修改用户关联或注册表；
- Windows `10.0.26100` GitHub runner 已证明受控副本和最小 ACL 原路径都可读，但 Shell 提取均返回 `E_ACCESSDENIED`；在确认具体 build/provider/runner 策略前必须安全回退，不能宣称该环境支持现场缩略图；
- 暂定预算仅来自当前机器，不能直接升级为发布 SLA。

因此 P0-03b 为 Conditional Pass，Issue #22 继续保持打开。真实 worker 已进入零 Capability AppContainer，受控副本关闭了探针内的直接路径读取暴露；最小 ACL 对照没有解决 26100 的 Shell 拒绝且增加 DACL 修改风险，故不作为默认。provider 矩阵和正式产品合同完成前，仍不允许对任意用户文件开放现场缩略图访问。

## 6. 下一切片

1. 在专用干净环境扩展 build × provider 矩阵到 HEIF、Office/PDF、OneDrive、网络路径和受控安装的第三方 provider，并复测 TIFF 默认 handler，保留类型图标/缓存安全回退；
2. 将 handle-backed 输入作为单独的 stream/decoder 或 Shell provider 合同实验；若重开最小 ACL，先补异常退出 ACE 日志/修复和并发 DACL 测试；
3. 把已验证的共享内存 transport 接入正式渲染表面，保持相同长度/格式/尺寸/容量上限；
4. 在标准用户、企业策略、Windows 10/11、x64/ARM64 环境复测进程创建与隔离；
5. 在合成不同格式集和专用云/网络/provider 环境执行 500 个不同项目矩阵，并提交负责人批准最终预算。

本轮安全语义依据 Microsoft 的 [Mandatory Integrity Control](https://learn.microsoft.com/windows/win32/secauthz/mandatory-integrity-control)、[AppContainer isolation](https://learn.microsoft.com/windows/win32/secauthz/appcontainer-isolation)、[AppContainer 对象 DACL 授权](https://learn.microsoft.com/windows/win32/secauthz/implementing-an-appcontainer)、[`CreateAppContainerProfile`](https://learn.microsoft.com/windows/win32/api/userenv/nf-userenv-createappcontainerprofile)、[`SECURITY_CAPABILITIES`](https://learn.microsoft.com/windows/win32/api/winnt/ns-winnt-security_capabilities)、[显式句柄继承](https://learn.microsoft.com/windows/win32/procthread/creating-processes)、[`AssignProcessToJobObject`](https://learn.microsoft.com/windows/win32/api/jobapi2/nf-jobapi2-assignprocesstojobobject)、[`SHCreateItemFromParsingName`](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-shcreateitemfromparsingname)、[`IShellItemImageFactory::GetImage`](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitemimagefactory-getimage)、[`DuplicateHandle`](https://learn.microsoft.com/windows/win32/api/handleapi/nf-handleapi-duplicatehandle)和[`CreateFileMapping`](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-createfilemappingw)文档。MIC 默认执行 no-write-up；AppContainer 可通过对象 ACL 获得精确资源访问，而 parsing-name Shell item 合同不能由原始句柄直接替换。通过六个自有格式/编码样本和脱敏注册健康矩阵仍不等于完整文件 broker 或真实 provider 兼容矩阵已完成。
