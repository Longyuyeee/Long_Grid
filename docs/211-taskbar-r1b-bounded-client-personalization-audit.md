# Stage 211：TASKBAR-R1B 有界客户端与个性化状态审计

日期：2026-08-27

开发基线：`origin/main@b1c240fe403cdcf92ae82906d3088d771f3d6f81`

状态：`EngineeringComplete / RealProcessPass / TaskbarMutationStillDenied`

## 1. 阶段目标与结论

本阶段只关闭 `TASKBAR-R1B`：正式 App 通过有界协议调用独立 Worker，并在“个性化”页显示真实、可理解的兼容状态。它没有实现透明、着色、材质或恢复预设，也没有对 Explorer、任务栏窗口或注册表执行写入。

完成结果：

- 新增版本化 `TaskbarWorkerResponse`，请求使用随机 ID，响应必须回显同一 ID；
- App 每次检测启动一个 `LongGrid.TaskbarWorker.exe`，绑定父进程 PID，3 秒超时，最大响应 64 KiB；
- 客户端拒绝未知 JSON 字段、畸形 JSON、错误协议版本、错误请求 ID、超大响应、异常退出和声称修改系统的报告；
- 超时或取消时终止 Worker 故障树，Worker 也会在绑定父进程退出后自行退出；
- 个性化页新增真实“任务栏”状态卡和“重新检测”，使用“版本尚未认证 / 检测到冲突工具 / 环境未通过 / 检测失败”等用户语言；
- 当前没有任何可点击任务栏样式预设；未认证 build 继续失败关闭；
- Worker 的证据故障入口只有显式环境变量和命令行同时存在时才可使用，正式 App 不暴露该入口。

## 2. 初始需求与竞品对齐

| 初始/竞品需求 | 本阶段实际结果 | 对齐判定 |
|---|---|---|
| iTop 式低门槛任务栏入口 | 个性化页能自动显示真实兼容结果，并可重新检测 | 部分对齐；尚无样式预设 |
| 独立故障域 | 正式 App 不直接执行原生探测；Worker 超时、崩溃、坏响应均失败关闭 | R1 对齐 |
| Windows build 风险说明 | 显示真实 build，并明确“尚未认证”而不是假装可用 | 对齐 |
| 冲突检测 | 读取已知第三方任务栏工具并拒绝准入 | 对齐 |
| 安全、可恢复、零惊吓 | 本阶段完全只读，`ModifiedSystemState=false`；无预设、无隐式应用 | 对齐本阶段边界 |
| 透明/颜色/材质与一键恢复 | 未实现 | R2/R3 Pending |
| 现代软件 UI | 状态卡进入现有产品化“个性化”页，提供加载、警告、错误、重试和无障碍 live status | 对齐 R1B；最终视觉矩阵仍待 M1/M2 验收 |

没有发生产品方向偏移：本阶段直接推进“任务栏美化”核心支柱的安全入口，没有插入自动整理、Widget、插件协议或教程型页面。

## 3. 真实测试：预期、实际、差异与修正

### 3.1 开发过程差异

| 检查 | 预期 | 首次实际 | 修正后实际 |
|---|---|---|---|
| Worker 父进程监控编译 | 监控任务返回退出码 72 | 首次把 `Task<int>` 声明成 `Task`，Release 编译 2 个类型错误 | 保留 `Task<int>`，退出码可传播；全解决方案 0 warning / 0 error |
| 客户端静态分析 | 不降低分析规则 | CA1512、CA1822 各 1 个错误 | 使用 `ThrowIfLessThanOrEqual`，无状态客户端改为静态类；0 warning / 0 error |
| Worker 正式部署 | App/Test 输出旁包含 exe、dll、deps、runtimeconfig | 初次普通 ProjectReference 只复制 exe/deps/runtimeconfig，真实启动报缺少 DLL | 改为非程序集 Content ProjectReference；四个运行文件均存在，真实进程可启动 |

### 3.2 独立进程协议与故障测试

权威命令：

```powershell
dotnet test tests/LongGrid.Core.Tests/LongGrid.Core.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~TaskbarCompatibility"
```

| 场景 | 预期效果 | 实际效果 | 差异 |
|---|---|---|---|
| 正常独立进程 | 返回匹配版本/请求 ID 的只读报告；未认证 build 不准入 | `Completed`、`Diagnostic=None`、`ModifiedSystemState=false`、非 `Allowed` | 无 |
| Worker 挂起 | 350 ms 内失败关闭并终止 Worker | `TimedOut / WorkerTimeout` | 无 |
| Worker 异常退出 | App 不崩溃，无可信报告 | `WorkerExited / WorkerExit71` | 无 |
| 畸形 JSON | 拒绝响应 | `ProtocolError / MalformedResponse` | 无 |
| 错误协议版本 | 拒绝响应 | `ProtocolError / ProtocolVersionMismatch` | 无 |
| 超过 64 KiB | 读取期间立即拒绝，不无界分配 | `ProtocolError / ResponseTooLarge` | 无 |
| 绑定父进程退出 | Worker 自行退出，不遗留常驻进程 | `WorkerExited / WorkerExit72` | 无 |
| 运行文件 | exe/dll/deps/runtimeconfig 均在客户端输出旁 | 4/4 存在 | 无 |

本阶段筛选测试实际 `13/13` 通过，其中 8 个为新增的真实子进程/部署测试，5 个为 R1A 策略测试。

### 3.3 当前 Windows 与正式 App

只读实机脚本：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng/Test-LongGridTaskbarCompatibilityProbe.ps1 -Configuration Release -NoBuild
```

实际为 Windows `10.0.26200.0`、build `26200`、session `2`；1 个 `Shell_TrayWnd` 和 1 个 `Shell_SecondaryTrayWnd` 均由 `explorer` PID `6932` 拥有；无已知冲突进程；连续探测窗口身份不变；`ProbeOutcome=Pass`、`RuntimeAdmission=DeniedNoCertifiedBuild`、`Difference=None`。

正式 App 真实窗口检查：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng/Test-LongGridWindowSmoke.ps1 -Configuration Release -NoBuild -StabilitySeconds 5
```

预期为显式控制中心 1 个、冷启动 ≤10 秒、首次桌面宿主 1 个、单实例重定向成功、稳定 5 秒、退出无残留、临时配置零写入；实际分别为 `1`、`6044 ms`、`1`、退出码 `0`、稳定 `5 s`、残留 `0`、写入 `0`，`Difference=None / Outcome=Pass`。这证明接入检测后 App 仍可真实启动和退出，但不替代个性化页的物理鼠标/Narrator 验收。

UI 合同实际 `191` 个唯一 AutomationId，新增状态、详情和重新检测入口全部存在；状态不可关闭、使用 Polite live announcement，代码合同确认只调用 3 秒有界客户端且不包含任务栏写入 API。

回归门禁预期 locked restore、格式、Release 构建、完整核心测试和 UI 合同全部通过；实际 `dotnet restore --locked-mode` 退出码 0、`dotnet format --verify-no-changes` 退出码 0、全解决方案 `0 warning / 0 error`、核心测试 `1285/1285`、UI 合同 `Outcome=Pass`，差异为无。

## 4. 安全与架构审计

- `Core` 只拥有协议、兼容报告和准入策略；
- `TaskbarWorker` 只拥有 Windows 只读枚举与协议服务；
- `Infrastructure` 负责进程生命周期、边界、解析和失败关闭；
- `App` 的任务栏呈现拆到 `MainWindow.Taskbar.cs`，没有继续把业务接线堆入巨型主 code-behind；
- App 不引用 Worker 内部类型，不把测试故障模式暴露为产品设置；
- 本阶段没有 `SetWindowLong`、`SetWindowCompositionAttribute`、Explorer 注入、Hook、注册表写入或任务栏进程替换。

仍有一个边界需要后续加强：当前协议证明响应完整性和关联性，但尚未建立 Worker 二进制签名/哈希认证；这属于发布与供应链门禁，不能用来宣称任务栏写入已安全。

## 5. 当前开发状态与下一步

三根核心支柱的准确状态：

1. 桌面盒子：工程链较完整，M1 物理 UIA/鼠标/Narrator 证据仍受已知运行时与环境阻塞；
2. 文件夹绑定：真实文件系统、身份、刷新和恢复工程链存在，物理 Picker/可见旅程证据仍 Pending；
3. 任务栏美化：R1A/R1B 已完成只读探测、正式 App 接线和独立故障域；R2 样式、R3 回退恢复、R4 实机矩阵均未完成。

下一唯一开发项为 `TASKBAR-R2A`，但必须保持以下顺序：

1. 先定义“系统默认”快照、一次应用事务、15 秒确认和自动回退状态机；
2. 原生适配器仅在明确认证的 build/会话中准入，默认仍拒绝；
3. 先交付“系统默认 + 清透”最小预设，不一次扩张全部材质；
4. 用真实 Explorer 重启、Worker/App 崩溃和进程退出验证回退；若当前 build 未认证，不得为了测试绕过准入，而应在隔离 Windows 矩阵环境取得证据；
5. 通过后再增加深色/浅色半透明和受支持时的 Acrylic，并进入 R4 多显示器、全屏、高对比、卸载恢复矩阵。

M1 的物理证据债务保持可见，不因任务栏推进被标记完成；一旦安全 WinUI 运行时和专用测试环境可用，应优先恢复集中验收。
