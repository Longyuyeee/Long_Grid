# Stage 182：PF-001 桌面优先启动收口与真实窗口审计

- 日期：2026-08-24
- 分支：`codex/pf002d-create-preview`
- 起始基线：`da83082`
- 对齐编号：`PF-001`
- 结论：`EngineeringComplete / ProductEvidencePending`

## 1. 本阶段目标与非目标

本阶段关闭 PF-001 最后一个已知工程缺口：正常启动不再无条件弹出控制中心，而是先加载用户开关、配置、桌面目录和显示拓扑，再由 DesktopHost 状态决定是否保持桌面优先。

本阶段没有把 PF-001 标为产品 `Complete`，也没有借机实现 PF-033 托盘、PF-004 标题栏、PF-005 项目图标、任务栏美化或 Widget 运行时。Narrator、跨进程 UIA 子树和物理设备证据仍受 Stage 181 的已知 WinUI 上游问题约束，签名、安装和分发门禁也没有放宽。

## 2. 冻结后的启动决策

| 启动事实 | 控制中心 | 桌面结果 |
| --- | --- | --- |
| 方格开启，Host 为 `ReadyReadOnly` | 保持隐藏 | 直接使用已有方格 |
| 方格开启，Host 为 `AwaitingWorkspace` | 保持隐藏 | 显示既有桌面首建入口 |
| 系统表面临时挂起 | 保持隐藏 | 不抢前台，等待系统表面恢复 |
| 方格被用户关闭 | 激活 | 给出恢复入口 |
| 安全策略禁用、拓扑不安全、Host 故障或超时 | 激活 | 显示有限安全状态 |
| 配置损坏、备份恢复或安全模式需要用户注意 | 激活 | 显示恢复说明 |
| 用户再次启动 Long方格 | 激活现有控制中心 | 不创建第二个产品实例 |

`ProductDesktopFirstStartupPolicy` 是不依赖 WinUI 的有限状态决策；App 只负责收集正式控制器事实、等待 Host 有限就绪并执行 `ActivateMainWindow`。因此“桌面优先”不是简单删掉 `Window.Activate()`，故障和显式用户启动仍有可恢复入口。

## 3. 实现边界

1. `App.OnLaunched` 移除正常路径的无条件 `window.Activate()`；PF-002 正式证据会话仍显式显示控制中心，不改变既有证据合同。
2. 启动并行等待方格设置、配置、桌面目录和显示拓扑；Host 最多等待 5 秒，超时即安全激活控制中心。
3. 首次启动、已有方格和空工作区共用同一 DesktopHost 生命周期，不建立第二套桌面实现。
4. 新增临时、空目录、非 reparse-point 的隔离证据会话及唯一 AppInstance key；真实测试不会读取或覆盖用户配置。
5. 第二进程继续通过 Windows App SDK `RedirectActivationToAsync` 转发，主实例只在 UI Dispatcher 上激活控制中心。

## 4. 真实 Expected / Actual 证据

正式 `Release/win-x64` 产物由 `eng/Test-LongGridDesktopFirstStartup.ps1` 直接启动。测试只使用 Win32 顶层窗口枚举和 `WM_CLOSE`，不读取跨进程 UIA、不截图、不发送输入，也不改变显示状态。

| 项目 | Expected | Actual | 差异 |
| --- | ---: | ---: | --- |
| 首次启动控制中心可见 | `false` | `false` | 无 |
| 首次启动 DesktopHost 可见 | `true` | `1` 个 | 无 |
| 冷启动 DesktopHost 就绪 | 15 秒内 | `1,378 ms` | 无 |
| 首次桌面优先稳定 | 20 秒持续响应 | 20 秒持续响应 | 无 |
| 第二进程退出码 | `0` | `0` | 无 |
| 重定向后控制中心数量 | `1` | `1` | 无 |
| 退出后活进程 | `0` | `0` | 无 |
| 临时配置写入 | `0` | `0` | 无 |

Stage 153 中“开启后 1 秒内出现已有方格或明确空状态”继续解释为运行期由关到开的呈现目标；本阶段的 `1,378 ms` 是包含 Windows App Runtime/WinUI 冷启动的独立进程指标，未篡改为 1 秒通过。冷启动性能后续仍按轻量常驻原则持续优化。

## 5. 测试发现的差异与修正

| 轮次 | 预期 | 首次实际 | 修正后实际 |
| --- | --- | --- | --- |
| 策略测试编译 | 12 个测试可运行 | CA1707 拒绝带下划线的方法名 | 改为语义化 PascalCase，12/12 |
| Win32 证据编译 | PowerShell `Add-Type` 可编译 | 默认旧 C# 编译器拒绝属性初始化和 `out uint` | 改为兼容语法，真实 App 通过 |
| 兼容包装器 | 产品通过即脚本通过 | 历史 `$LASTEXITCODE` 把成功误报为失败 | 以异常和 JSON Outcome 判定 |
| 干净会话合同 | 与当前 UI 合同一致 | 旧脚本硬编码 146，实际为 153 | 更新为 153，ValidateOnly 通过 |

这些修正没有降低产品断言，也没有把 probe 或源代码存在性冒充用户功能证据。

## 6. 最终门禁

- PF-001 启动策略：`12/12`；
- Release 全量：`1087/1087`；
- Release App 构建：`0 warning / 0 error`；
- 153-ID UI 源码合同：通过；
- 单实例源码合同：通过；
- 干净会话 ValidateOnly：通过；
- 正式 App 真实进程/窗口 20 秒 Expected/Actual：`Difference=None`；
- 临时配置、活进程和跨进程 UIA 残留：均为 `0`。

## 7. 需求对齐与下一步

PF-001 的用户开关、关闭释放、恢复、空工作区入口和桌面优先启动工程链现已闭合，状态调整为 `EngineeringComplete / ProductEvidencePending`，不是 `Complete`。PF-001、PF-002、PF-003 都仍欠各自明确的物理/无障碍产品证据，顶层继续为 `0/30 Complete`。

下一产品切片严格进入 PF-004：桌面方格标题栏与就近操作。先形成用户可见标题、选中/悬停状态、锁定/折叠/更多操作及键盘焦点的最小闭环，再进入 PF-005 正式项目呈现。任务栏美化、小组件/Long助手插件运行时和广泛窗口特效仍按原路线保留，不抢占 PF-004～PF-010 日常桌面核心闭环。
