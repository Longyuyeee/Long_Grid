# Stage 246：M1 可见窗口就绪与自包含 Runtime 真实审计

日期：2026-08-31

输入基线：`origin/main@98b53f03807d61bb1d274919e5b99f6f34e6187e`

状态：`ImplementationAndLocalVerificationPass / PullRequestAndMainVerificationPending / PhysicalJourneyPending`

## 1. 接续条件与测试边界

#23 与 #274 仍为 OPEN，最后更新时间分别为 `2026-08-28T03:51:37Z` 与 `2026-08-28T02:43:13Z`。受保护环境、许可证、Publisher、托管签名和安装/分发批准没有新增输入。系统 Runtime 真实预检仍读取 Framework `2.4.0.0`、XAML `3.2.3.0`、Singleton `8002.4.0.0`，缺 Main.2 `>=2.3.1.0` 与精确 DDLM `2.3.1.0-x6`，且 `knownUnsafePairAbsent=false`，因此正式 ExternalAutomation 继续在启动前返回 `BlockedByIncompleteRuntime`。

本阶段只使用当前本机。没有使用云电脑，没有安装或卸载系统 Runtime，也没有修改包注册。官方 Windows App SDK 文档确认稳定版 2.3.1 x64 安装器及 Framework/Main/Singleton/DDLM 的部署职责，但在当前电脑上补装 2.3.1 不能保证 Bootstrap 避开已经存在的更高 Framework/XAML 组合，因此没有把系统级写入当作无风险试验。参考：[Windows App SDK downloads](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads)、[Deploy unpackaged apps](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps)。

## 2. Expected / Initial Actual / Difference / Correction

| 检查 | Expected | Initial Actual | Difference / Correction |
|---|---|---|---|
| 当前源码的自包含交付 | ZIP 必须来自精确源码、内部哈希完整，并声明 .NET/Windows App SDK self-contained | `LongGrid-0.1.0-stage246-win-x64.zip` 来自 `98b53f0`，802 个 payload 文件、内部哈希 0 失败、ZIP SHA-256 `ac885af00a9b68e795397683095df819fdf01aa0abaefeaf680ea1bbd4b9e4c0`，两项 self-contained 均为 true | None；产物仍 `signed=false / installer=false / distributionApproved=false` |
| M1 会话首启策略 | 有 M1 证据会话时必须显式激活产品控制中心 | `InitializeDesktopFirstStartupAsync` 固定传入 `EvidenceSession: false`，虽已建立隔离会话，仍可能按普通 desktop-first 路径保持隐藏 | 改为 `m1ManualEvidenceSession is not null`，复用既有首启策略的 evidence-session 激活语义 |
| Ready 的产品含义 | 只有托管 App 已构造、产品窗口实际激活且存在非空标题，才能声明可接收物理输入 | 自包含真实启动记录 `InstanceKeyResolved / AppInstanceCurrent / ConfigurationIsolationAccepted / AppConstructed`，但 15 秒内标题始终为空、窗口不可见；旧启动器会在 `AppConstructed` 后返回 Ready | 新增 `ProductWindowActivated` 证据阶段；启动器同时要求 AppConstructed、ProductWindowActivated 与非空 `MainWindowTitle`，否则终止自有进程、清理自有会话并失败 |
| 不安全 Runtime 上的真实结果 | 不得把 XAML 启动崩溃伪装成产品可操作 | 修正首启策略后的临时自包含发布仍在 AppConstructed 后退出；Application Error 1000 精确指向 `Microsoft.UI.Xaml.dll 3.2.3.0`、exception `0xc000027b`、offset `0x00000000003a9c5d`。此前一次自包含等待复测具有同一指纹 | 自包含只能绕开系统包缺失，不能消除当前 XAML 风险；本阶段验收改为“当前电脑失败关闭且不误报 Ready”，M1 正向物理旅程继续 Pending |
| 副作用 | 测试后既有 LongGrid 进程、普通配置、临时提取目录与 M1 证据目录均不得变化 | 两次有效自包含启动均只处置自有 PID；既有进程集合与普通配置指纹不变，临时提取和证据目录均已清理 | None |

第二次窗口等待包装器首次运行时，指纹函数发生 PowerShell 运算符优先级错误，产品进程尚未启动，但已创建临时提取目录与证据会话。精确确认目标位于 `%TEMP%` 和产品证据根目录后，证据会话由产品 cleanup 合同清理，提取目录在绝对路径校验后清理；既有进程保持不变。修正包装器后的运行才作为上表有效 Actual，不把编排错误计入产品证据。

## 3. 本机真实验证

| 门禁 | Expected | Actual | Difference |
|---|---|---|---|
| M1 ValidateOnly | 新的窗口就绪合同可被静态验证，零启动 | `Pass / startsProcess=false` | None |
| 策略与真实进程专项 | 首启策略、证据阶段和失败关闭源码合同进入回归 | `27/27`、0 skipped | None |
| Format | 无格式差异 | 绝对 SDK host，attempts=1、无 transient retry | None |
| Release build | 0 warning / 0 error | 0 warning / 0 error | None |
| 完整测试 | 基线不得退化 | `1,397/1,397`、0 skipped、28 秒 | None |
| Coverage | lines >=90%、branches >=75% | lines `90.46% (47096/52064)`；branches `76.16% (15456/20294)` | None |
| UI 合同 | 正式可访问性/产品合同保持完整 | ContractOnly `198` IDs，Pass | None |
| 依赖与分发 | 漏洞 0；未批准前禁止分发 | 漏洞 0；20 项目/30 包；`PendingOwnerReviewAndNotice / distributionApproved=false` | None |
| ExternalAutomation | 不完整/不安全 Runtime 必须零启动、零建会话 | `BlockedByIncompleteRuntime / startsProcess=false / createsEvidenceSession=false` | None |

覆盖率前先确认 `TestResults` 无 tracked file，只清理工作区内该生成目录，再关闭 build server 并生成唯一结果。本阶段没有复用历史 coverage，也没有降低门槛。

## 4. 开发目标与需求对齐审计

开发目标审计：本阶段关闭了两个既有误判点——M1 会话未进入 evidence-session 首启路径，以及 `AppConstructed` 被错误等同于可见可操作窗口。代码、专项、完整本机门禁和当前不安全 Runtime 的真实失败关闭均已完成；PR 与合并后 main 证据仍 Pending。

需求对齐审计：本阶段没有安装 Runtime、调用 UIA、发送物理输入、修改用户普通配置、开放任务栏写入、签名或分发。当前电脑上的正向 M1 结果仍是 Pending，不能用 `AppConstructed` 或自包含产物替代。严格产品完成度仍为 M1/M2 `0/2 Complete`、30 项 PF `0 Complete`。

下一唯一接续点：先在具备完整兼容 Runtime、受保护签名包和独占可丢弃 Windows 会话的环境完成 BOX-R1-C/D；随后使用新 Ready 合同取得 `ProductWindowActivated + 非空标题`，再执行 M1 完整物理旅程。若仍出现 `Microsoft.UI.Xaml.dll 3.2.3.0 / 0xc000027b / 0x3a9c5d`，必须继续失败关闭并处理 Runtime，而不是修改就绪判据绕过。
