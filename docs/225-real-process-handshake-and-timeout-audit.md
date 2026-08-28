# Stage 225：真实进程握手与超时清理审计

日期：2026-08-28

开发基线：`origin/main@2f5e5ff9df42a7f4cda7a9cf1cf88c9360d89c31`

状态：`LocalFullPass / PullRequestPending / MainVerificationPending`

## 1. 开发目标

Stage 223 的 main CI 首轮在繁忙执行环境中同时暴露任务栏恢复 worker 和可丢弃环境预检的时序失败。重跑后来通过，但重跑不能关闭差异。本阶段不放宽生产客户端的 4 秒期限、不延长环境预检的 10 秒总期限，也不把真实进程替换为 mock；目标是把测试前置条件变成可观测握手，并保证超时后终止测试自己创建的进程树、排空输出并给出有限诊断。

## 2. Expected / Actual / Difference / Correction

| 场景 | Expected | 首次真实 Actual | Difference | Correction 与当前 Actual |
|---|---|---|---|---|
| 启动恢复 worker 绑定父进程 | worker 已建立父进程监控后，测试释放父进程；客户端在 4 秒内得到 `WorkerExited / WorkerExit72` | GitHub runner 上曾在 4 秒后得到 `TimedOut`；旧测试把 PowerShell 启动和固定 500ms sleep 都放进同一个期限，无法证明 worker 已开始监控 | 被测握手与测试父进程启动竞争，繁忙 runner 可消耗全部预算 | evidence-only `hang` 路径在建立 monitor 后写固定 readiness 文件；测试读到 `ParentMonitorReady` 才释放受控父进程。生产正常路径不传故障参数，不生成该证据。当前定向与 5 轮压力测试全部得到 `WorkerExited / WorkerExit72` |
| 兼容性 worker 绑定父进程 | 父进程已真实启动并可控退出，worker 观察退出后返回 | 旧测试同样依赖 `Start-Sleep -Milliseconds 500`，父进程启动时间和存活窗口没有独立边界 | 固定 sleep 不是 readiness 合同 | 公用 `TaskbarReadyParentProcess` 先从真实 Windows PowerShell stdout 读取固定 marker，再启动客户端并由 stdin 显式释放；退出最多等待 5 秒，只清理测试自有进程树 |
| 可丢弃环境真实预检 | 10 秒内输出有限 JSON；超时必须结束子进程并保留阶段、PID 和输出长度诊断 | GitHub runner 曾在 10 秒后对 stdout `ReadToEndAsync` 抛出 `OperationCanceledException`；stderr 未排空，超时路径未终止进程树；两次 CIM 查询没有各自上限 | 总期限无法约束单次 CIM，异常也无法定位阶段或确认无残留 | 两次 CIM 分别使用 2 秒 operation timeout；stdout/stderr 从启动起并发排空；统一等待进程退出，超时则 kill entire process tree、等待退出、排空两路输出，并抛出包含 purpose、PID、期限及输出长度的有限异常 |
| 超时清理负向证明 | 真实挂起进程超过 500ms 后被终止，PID 不再存活 | 旧套件没有受控挂起子进程负向用例 | “写了 Kill”不等于真实证明清理有效 | 新测试真实启动 `powershell.exe` 睡眠 60 秒；实际约 725ms 返回超时异常，随后按 PID 复读为不存活 |

## 3. 本机真实验证

- Release 定向 build：`0 warning / 0 error`。
- 四项真实进程矩阵单轮：`4/4`，包括当前宿主实际 CIM/Sandbox 预检和真实进程树清理。
- 同一矩阵连续 5 轮：`20/20`，0 失败、0 跳过；每轮约 4～5 秒。
- 生产客户端 4 秒超时、预检总期限 10 秒及结果断言均未放宽。
- locked restore、格式与 Release 全解决方案 build 通过，`0 warning / 0 error`；关闭 build server 后，按 CI 的 coverage 与 blame-hang 参数执行完整套件为 `1,382/1,382`、0 跳过。
- coverage lines `90.43% (47090/52072)`、branches `76.16% (15458/20298)`，通过 90%/75% 门槛。
- Action 固定、Dependabot 与 CodeQL workflow 合同通过；依赖漏洞为 0，许可证元数据门禁覆盖 20 projects / 30 packages，状态仍为 `PendingOwnerReviewAndNotice`。
- signing 与 RC ValidateOnly 通过，但 `liveSigningImplemented=false`、`signed=false`、`installable=false`、`distributionApproved=false`。
- PR/main CI 与 CodeQL 结果仍须按真实结果补录，不提前写成 Pass。

## 4. 安全与范围审计

所有进程终止只作用于测试代码直接创建并持有 PID/handle 的 PowerShell 进程，未枚举或终止用户已有进程。新增 readiness 文件只在显式测试故障 `hang` 且调用方提供隔离证据目录时写入；产品启动恢复不使用该故障参数。脚本仍只读系统信息、生成隔离配置与 JSON 证据，不执行任务栏 mutation，不启用 Windows Sandbox，不提权、不重启，也不增加网络、secret、签名、安装或分发权限。

开发目标审计：旧的“重跑后通过”差异已转化为确定性握手、有限外部调用、真实超时清理和可定位诊断；在 PR/main 远端证据完成前阶段状态仍为 Pending。需求对齐审计：修正直接服务任务栏恢复可靠性和真实测试要求，不扩大产品能力，也不把本机工程验证冒充 `TASKBAR-R2B1-B` 原生效果完成。

## 5. 接续条件

本阶段需经最新 PR 完整 CI、双语言 CodeQL、合并后 main CI 和 Code Scanning API 复核后关闭。关闭后，任务栏原生效果仍只能在 Stage 216 定义的可丢弃 Windows 环境准入全部满足后继续；若外部环境仍未提供，项目接续点保持 #19/#20/#24 的真实人工证据、#23 的许可证决定和 #274 的 Publisher/托管签名输入，不在日常宿主上执行任务栏写入。
