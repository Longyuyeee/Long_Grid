# Stage 147：正式受限缩略图 worker 接线审计

- 审计日期：2026-08-15
- 开发基线：`main@da45970cc5be9fade7a0fcebae196eb6518816f7`
- 切片：M4c2b2
- 当前判定：**Engineering Pass（本地）/ 远端证据 Pending / 真实 24 小时证据 Pending**

## 1. 需求对齐与真实缺口

M4c2b1 已让正式 App 能匿名报告状态修订，但 `LongGrid.Spikes.ShellItemImages` 仍同时承担探针 CLI、真实桌面枚举、自有样本、故障注入、500 请求矩阵和 AppContainer worker runtime。正式 App 直接引用原探针会把测试入口与非产品枚举路径带入交付集，也无法证明 24 小时入口观察的是正式组件。

M4c2b2 因此只关闭以下缺口：

1. 建立独立产品可执行项目 `LongGrid.ThumbnailWorker`；
2. 把协议、有限逐行 IPC、Shell 提取、受控副本、零 Capability AppContainer、显式句柄白名单、Job kill-on-close、共享内存和父侧 client 迁入产品组件；
3. 让既有 probe 反向引用同一产品程序集并继续执行全部隔离/故障/预算矩阵；
4. 正式 App 仅在 M4c2 受控资源会话双 opt-in 生效时创建一个 idle restricted worker 和一个临时 Profile；普通启动仍为零 worker/零 Profile；
5. 匿名遥测只报告正式接线布尔值以及 `1/1` 或 `0/0` 的有界 worker/Profile 计数。

本切片不接入缩略图 UI，不读取真实桌面文件内容，不开启 Explicit 交互，不改变文件，不扩大 Capability，也不产生 M4c Pass。

## 2. 产品组件边界

产品 worker 是独立 `net8.0-windows` 可执行项目，不依赖 probe 或 WinUI，并与正式 App 一致锁定 `win-x64` RID，使 startup/pack 的 `--no-restore` 链使用已恢复的同一资产图。Profile 暂存只复制以下四个固定文件：

- `LongGrid.ThumbnailWorker.exe`；
- `LongGrid.ThumbnailWorker.dll`；
- `LongGrid.ThumbnailWorker.deps.json`；
- `LongGrid.ThumbnailWorker.runtimeconfig.json`。

暂存拒绝缺失文件和重解析点，并继续受 128 MiB 总预算约束；不再复制 App/probe 输出目录中的任意文件。worker 仍以挂起状态创建，先加入 `KILL_ON_JOB_CLOSE` Job、复核 `TokenIsAppContainer`、仅继承 stdin/stdout/NUL stderr，再恢复线程。输入默认仍为最大 32 MiB 单文件、64 MiB client 总量的只读受控副本；没有现场用户文件请求时，正式 worker 保持 idle，不收到路径或内容。

## 3. 正式 App 生命周期

`ProductThumbnailWorkerLifecycleController` 复用 M4c2 匿名遥测的 DesktopHost + session acknowledgement 双 opt-in。关闭策略下不调用 runtime factory；开启后必须同时证明：

- worker 已启动且进程计数精确为 1；
- 活跃自有 Profile 计数精确为 1；
- worker 是零 Capability AppContainer；
- Job 已配置 kill-on-close。

任何启动失败、证明不完整或运行中 worker 消失都会 fail closed：匿名状态变为 `FailedClosed`、计数归零并释放 runtime/Profile，不在主进程回退到现场 Shell 提取。App shutdown 先关闭遥测连接，再释放 worker/Profile，最后释放 DesktopHost、目录和保存控制器。

## 4. 会话合同更新

`Start-LongGridResourceStabilitySession.ps1` 更新为 `M4c2b2`：

- `formalThumbnailWorkerIntegrated=true`、`formalStateRevisionTelemetryAvailable=true`；
- 基线 blocker 只剩 `Real24HourEvidenceNotCollected`，但 `canProduceM4cPass` 继续固定为 `false`；
- 每个样本要求匿名遥测连续 sequence、正式 worker `true/1/1`，并从唯一 `LongGrid.ThumbnailWorker` 进程采集 private bytes、handle 和 thread；
- App 与 worker 分别执行首尾中位数和每小时斜率预算，worker 重启数和 App 退出后的孤儿数预算均为 0；
- 证据不写入 PID、路径、名称、内容、句柄值、账户或机器名；
- live 入口即使运行满 24 小时，也只输出 `PendingReal24HourEvidenceReview`，必须由 M4c2c 从同一提交审计后才能决定 M4c。

## 5. 自动化验收

- 新生命周期单测覆盖关闭策略不创建 runtime、证明完整时 `1/1`、证明失败时 fail closed、运行中退出转移和 dispose 后 `0/0`；
- 匿名 named-pipe 测试改为要求正式 worker `true/1/1`，继续验证 schema/sequence、未知请求拒绝和敏感内容缺失；
- 既有 worker matrix 新增 `FormalProductRuntimeReused=true` 硬门，500 请求、故障恢复、父退出、AppContainer、Profile/ACL、共享内存与预算全部继续执行；
- worker 中迁入的 Windows 原生隔离路径不计入 XPlat 单元覆盖率汇总，避免把原 probe 代码改变所属程序集后虚假拉低 Core/Infrastructure 门；覆盖阈值不降低，且 CI 必须继续通过上述真实 500 请求产品程序集矩阵；
- 本地复验：生命周期/遥测专项 11/11、Core 全量 935/935；产品 worker、Infrastructure 与 probe Release build 均为 0 warning / 0 error；真实隔离矩阵 500/500、`FormalProductRuntimeReused=true`、`ConditionalPass`；M4c2b2 `ValidateOnly` 通过；远端正式 App/交付门待本切片收尾复验。
- 本机正式 App Release build 仍被既有 NuGet 缓存不一致阻断：Windows App SDK 要求 `Microsoft.Windows.SDK.NET.Ref 10.0.19041.38`，本机解析到 `.34`；本次改动文件格式验证通过，全仓格式门只命中 3 个未触碰的既有文件。两项均不冒充通过，正式 App 与全量交付集以干净 Windows runner 为权威门禁。

## 6. 非目标与下一步

- 本切片不把受控副本兼容性实验升级为任意用户文件授权；provider 不支持时仍只能安全回退类型图标/已有缓存；
- 本切片不批准 ADR-0002，不代替安全负责人、Windows build/provider/企业策略矩阵或真实设备证据；
- 本切片不把 worker 像素接入正式渲染表面；
- 下一步 M4c2c：从 M4c2b2 合并提交执行并审计真实 24 小时会话，核对样本覆盖、App/worker 趋势、零重启/孤儿/Profile、UIA、窗口和状态漂移，再决定 M4c。
