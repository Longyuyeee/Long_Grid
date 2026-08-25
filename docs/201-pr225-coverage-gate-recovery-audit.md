# Stage 201：PR #225 覆盖率门禁恢复审计

日期：2026-08-25
开发项：Gate A / 长期功能分支主线集成
结论：**PR CI Pass / Main Integration Required**

## 1. 远端真实差异

PR #225 第三次 CI run `32798585694` 已通过 Windows PowerShell 5.1 编码、157-ID 批量无障碍、157-ID clean-session、构建和 1163/1163 测试，但在既有覆盖率门禁停止：

| 项目 | Expected | CI Actual | Difference |
| --- | ---: | ---: | ---: |
| 行覆盖率 | ≥90.00% | 87.89%（39558/45008） | -2.11 pp / Fail |
| 分支覆盖率 | ≥75.00% | 73.22%（12720/17372） | -1.78 pp / Fail |

覆盖率附件按 package、class、method 和源码行重新解析后确认：这不是报告缺失或阈值计算错误。分支相对 `main` 累计 42 个提交、约 1.8 万行 Core/Infrastructure/测试变更，新增正式 DesktopHost 窗口、系统事件、激活、布局、缩略图与安全打开路径没有在每个切片重新执行完整覆盖率门，形成了真实的长期分支质量债务。

## 2. 修正原则

- 不降低 90% 行、75% 分支门槛；
- 不排除正式 Windows 原生适配器，也不删除失败断言；
- 测试执行真实 HWND、真实 Win32 消息分派和真实 Windows 系统采样；
- 对无法稳定伪造的系统回调，把来源观察与有限业务映射拆开，使用受控 sampler/显式 evidence 入口验证同一生产逻辑；
- evidence 入口只为 `InternalsVisibleTo` 测试程序集可见，不进入 App 公共 API，不绕过运行时的来源校验。

## 3. 实现与测试增量

1. `WindowsProductDesktopInteractionSystemSurfaceEventSource` 支持受控采样周期，并把 SessionSwitch/PowerMode 映射抽成同一生产方法；测试覆盖启动幂等、采样、恢复、会话/远程/电源/焦点、sampler 失败、订阅者异常、释放和真实 Windows sampler。
2. `WindowsProductDesktopHostReadOnlySurface` 增加内部消息 evidence 入口；真实 HWND 依次执行 passive/explicit/hidden 的 hit-test、鼠标、滚轮、双击、取消、捕获、热键、绘制和键盘布局焦点矩阵。
3. `WindowsProductDesktopInteractionActivationSource` 将已通过原生来源门的键盘命令路由抽为同一核心方法；真实激活 HWND 覆盖项目导航、Enter 打开、Tab 标题焦点、Alt/Shift 布局、Escape 取消、Ctrl+A 和未知键。
4. 没有改变桌面文件、用户配置、输入注入边界、缩略图 1.5 秒预算、签名或分发权限。

## 4. 逐轮 Expected / Actual / Difference

| 轮次 | Expected | Actual | Difference |
| --- | --- | --- | --- |
| CI 基线 | ≥90% / ≥75% | 87.89% / 73.22% | Fail |
| 系统事件矩阵 | 比基线提高 | 88.67% / 73.89% | +0.78 / +0.67 pp，仍 Fail |
| DesktopHost HWND 消息矩阵 | 比上一轮提高 | 89.66% / 74.79% | +0.99 / +0.90 pp，仍 Fail |
| 激活键盘矩阵 | 分支通过、行继续提高 | 89.93% / 75.22% | 分支 Pass；行 -0.07 pp |
| 最终真实系统采样与布局焦点 | ≥90% / ≥75% | 90.05%（40576/45058）/ 75.34%（13094/17380） | +0.05 / +0.34 pp，Pass |

该轮同一次 Release collector 为 1170/1170 通过；不是把多次失败报告相加后制造百分比。远端 headless-runner 差异和后续余量修正见第 7 节。

## 5. 缩略图真实测试差异复核

一次全量覆盖率运行中，`RealProductQueueRequestsOwnedBitmapThenDisablesAndCleansProfile` 预期 `ReadyThumbnail`、实际 `FailedFallback`，该运行明确记为 1168/1169 Fail，未计作通过。随后：

- 同用例独立覆盖率复现：1/1 Pass，实际约 884 ms；
- 连续五个全新测试进程、每次启用 XPlat Coverage：5/5 Pass，实际测试耗时 830～908 ms；
- 最终完整 Release collector：1170/1170 Pass。

因此当前证据把单次差异保留为未复现的瞬时 worker fallback，不修改 1.5 秒预算，也不以重跑删除失败事实。若 PR runner 再现，必须增加匿名有限 failure kind / round-trip 诊断并按真实差异修正，不能继续靠重跑。

## 6. 需求对齐与下一步

本切片只恢复 Gate A 的质量门，没有扩张任务栏、小组件、文件移动或窗口特效范围。PF-001～PF-005 仍为 `EngineeringComplete / ProductEvidencePending`，PF-006 仍为 `InProgress`，下一功能切片仍是 PF-006C1 PageUp/PageDown。

推送后必须由 PR #225 的全新 Windows runner 重新执行完整流水线。只有 PR 全绿、合入 `main` 且 main CI 通过，Gate A 才能关闭并进入 PF-006C1。

## 7. 首次覆盖率修正后的远端差异与余量

PR run `32800465632` 在最新 SHA 上通过格式、构建、全部合同和 1170/1170 测试，但 lines 为 89.68%（40410/45058），branches 为 75.20%（13070/17380）：

| 项目 | Expected | Remote Actual | Difference |
| --- | ---: | ---: | ---: |
| 行覆盖率 | ≥90.00% | 89.68% | -0.32 pp / Fail |
| 分支覆盖率 | ≥75.00% | 75.20% | +0.20 pp / Pass |

下载远端 Cobertura 并与本机同 SHA 逐文件比较后，76 个唯一覆盖行差异集中于 `WindowsDisplayTopologySource.cs`：本机有完整 CCD/Monitor 成功路径，GitHub headless runner 只覆盖有限路径；激活源另差 6 行，遥测差 1 行，其余主要产品文件一致。因此 90.05% 的本机结果只有 0.05 pp 余量，不能代表 headless runner。

第二增量没有排除 Windows topology，也没有伪造显示器，而是补充不依赖硬件的确定性产品合同：

- active/no-active 布局取消和全部 layout result 状态；
- 18 个安全打开终态的有限、无路径反馈与 Accepted 映射；
- projection Ready、missing authority、revision/generation/disposition/batch 元数据拒绝；
- 初始 Explorer 不可用、未知全屏、远程与 session unavailable 组合及稳定恢复。

最终本机同一次 Release collector 为 **1198/1198、lines 90.41%（40738/45058）、branches 75.71%（13158/17380）**。按 run `32800465632` 的逐文件环境差异回算，headless runner 已超过 90% 行门并保留约 10 个唯一覆盖行余量；最终结论仍以新 run 为准。

## 8. 远端真实快捷方式启动差异与修正

PR run `32801490556` 在 SHA `44e1c3c` 上通过格式、Release 构建、启动链、DesktopHost、输入、UI Automation、clean-session、单实例和运行时恢复合同，但完整测试为 1197/1198：

| 项目 | Expected | Remote Actual | Difference |
| --- | --- | --- | --- |
| 真实 `.lnk` 目标及参数进入 Shell | 目标进程 5 秒内退出 | `where.exe cmd.exe` 在 runner 中 5 秒未退出 | 1 个真实测试 Fail；后续门禁未执行 |

原断言只能证明 `where.exe` 的进程寿命，不能直接证明参数产生了预期效果；命令输出及 runner 会话环境也会影响寿命。因此修正为更强的可观测行为：真实 COM `.lnk` 指向系统 `cmd.exe`，参数要求命令处理器把固定标记写入临时文件，测试等待真实 Shell 进程并核对标记内容。若参数没有经过 `.lnk` 解析和 `ShellExecuteEx` 到达目标，标记文件不会生成。

本机以五个全新 VSTest 进程连续执行该真实测试，Expected 为 `LongGridPf006b2`，Actual 五次均为 `LongGridPf006b2`，Difference 为 `None`，5/5 Pass。此次只增强测试证据与 runner 稳定性，没有放宽产品状态、超时门禁或安全边界；仍须由新 PR run 验证完整 1198 测试、覆盖率及后续 preflight。

## 9. 重复全量测试发现的缩略图超时与菜单挂起

在推送前重复完整 XPlat Coverage 时，真实产品队列缩略图测试再次出现 `ReadyThumbnail -> FailedFallback`。新增的有限、匿名 evidence 不记录路径，只记录失败类、HRESULT 和 round trip；第三轮复现得到 `TimedOut / 0 / 1507.37 ms`，相对 1500 ms 产品预算超出约 7.37 ms。因此此前第 5 节“瞬时且未复现”的判断已被新证据推翻并修正。

修正没有延长产品预算：受限 AppContainer worker 对 `.bmp` 优先使用 Windows `LoadImageW` 创建 DIBSection并复用既有受限共享内存像素链；原生加载失败时仍回退 `IShellItemImageFactory`。路径授权、受控副本、AppContainer、kill-on-job-close、像素尺寸和容量校验均保持。修正后的真实产品队列首轮约 664 ms，真实 worker/产品队列/文件版本缓存/真实 HWND 像素链 4/4 Pass；完整覆盖率前两轮均为 1198/1198。

第三轮完整测试随后触发另一个独立 hang 门：完成 1109 个测试后，`NativeActivationSourceExposesFiniteInvokeAndHideRestoreContract` 的真实弹出菜单没有被窗口 `WM_TIMER` 关闭，2 分钟 blame-hang 正确中止并生成 sequence 证据。为保证 evidence 自身有限化，菜单定时器改用受根引用的原生 `TIMERPROC` 在弹出菜单模态循环内直接调用 `EndMenu`；`SetTimer` 失败会立即抛出 Win32 错误，不再进入无限等待。隔离进程连续 5/5 通过，仍须再跑完整覆盖率与远端 PR CI。

修正后最终同一次本机 Release collector 为 **1198/1198、lines 90.38%（40774/45116）、branches 75.64%（13162/17400）**，格式门与 `git diff --check` 同时通过。相较要求，Actual 分别保留 +0.38 pp / +0.64 pp，Difference 为 `None`；最终 Gate A 结论仍以新的 headless PR run 为准。

## 10. 最终 PR headless CI 验收

PR run [`32803174900`](https://github.com/Longyuyeee/Long_Grid/actions/runs/32803174900) 在 SHA `10cdb25` 上用全新 Windows runner 完成 6 分 29 秒的完整流水线：

| 项目 | Expected | Remote Actual | Difference |
| --- | ---: | ---: | ---: |
| Release 测试 | 1198/1198 | 1198/1198，0 Failed | None / Pass |
| 行覆盖率 | ≥90.00% | 90.01%（40610/45116） | +0.01 pp / Pass |
| 分支覆盖率 | ≥75.00% | 75.51%（13138/17400） | +0.51 pp / Pass |
| 全部后续门 | 全部通过 | 配置、500 项规模、恢复、生命周期、资源稳定、匿名遥测、文件安全、正式缩略图隔离、漏洞、内部未签名 RC 全部通过 | None / Pass |

格式、构建、启动链、DesktopHost、输入、157-ID 无障碍、UI Automation、clean-session、单实例、hang 诊断和运行时恢复合同也全部通过。PR #225 当前 `mergeStateStatus=CLEAN`；Gate A 仍需合入 `main` 并核验 main CI，不能把 PR 绿灯提前记作主线关闭。
