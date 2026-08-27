# Stage 215：TASKBAR-R2A2c 原生适配器边界与认证入口审计

日期：2026-08-27

开发基线：`origin/main@44c74a3fc9f4ad021cfc82c3ce58c2e7ef1a6878`

状态：`EngineeringComplete / LocalRealWindowsPass / GitHubEvidencePending / NativeEffectPending`

## 1. 阶段目标与结论

本阶段完成 R2A2c：建立默认不可用的原生任务栏恢复合同、Worker 私有适配器目录，以及可在可丢弃 Windows 环境复用的认证预检入口。它仍不实现清透、着色或系统默认恢复，不把当前机器加入认证白名单。

- Core 新增 `ITaskbarAppearanceNativeAdapter`、有限 Availability/Restore 状态、恢复请求与结果合同；
- `TaskbarNativeRestoreAdmissionPolicy` 在产生原生请求前依次验证恢复日志、Windows build、任务栏 class 集合、当前任务栏单一 Explorer 所有权、只读探测结果、认证准入和 AdapterId；
- 恢复日志中的 Explorer PID 与当前 PID 分开保存。PID 变化被记录为 `ExplorerRestartedSinceJournal=true`，不会把旧 PID 伪装成当前所有者；
- Worker 的 `TaskbarNativeAdapterCatalog` 精确为空，任何 build 默认都解析为 `null`；正式启动恢复沿合同返回 `RecoveryDeferredAdapterUnavailable`，不会触及任务栏写 API；
- 新增 `--native-adapter-certification` 真实 Worker 入口。只有 `LONGGRID_TASKBAR_WORKER_EVIDENCE=1` 时才输出版本化、请求绑定的适配器/系统报告；普通调用退出 65 且不输出信息；
- 全量测试通过真实 Worker 用例执行同一入口；`eng/Test-LongGridTaskbarNativeAdapterCertification.ps1` 另提供可丢弃环境的 Expected、Actual、Difference 独立证据入口；
- 没有加入 `SetWindowCompositionAttribute`、Explorer 注入、Hook、内部 XAML 修改或任务栏 HWND 写操作。

## 2. 真实测试：预期、实际与差异

本轮在 Windows `10.0.26200.0` 上启动正式 Release Worker EXE，读取真实 `Shell_TrayWnd` 与 `Shell_SecondaryTrayWnd`。文件、进程、窗口与协议均为真实边界；仅 Core 准入分支使用受控合同对象，不把假适配器结果当作系统效果证据。

| 场景 | 预期效果 | 实际效果 | 差异与修正 |
|---|---|---|---|
| 未授权认证入口 | 无证据开关时拒绝，不能泄露可被误当作认证的 JSON | Worker 实际退出 65，stdout 为空 | 无 |
| 默认适配器目录 | 未经 build 实机认证时必须无实现 | `AdapterAvailability=Unavailable`、`AdapterId=None` | 无 |
| 当前 build 准入 | Build 26200 不得进入原生调用 | 两次真实报告均为 `ProbeOutcome=Pass / RuntimeAdmission=DeniedNoCertifiedBuild` | 无；保持拒绝 |
| 真实任务栏身份 | 认证预检前后不得替换或改变任务栏窗口 | 主/副任务栏均归属 Explorer；前后 `HWND|class|PID|process` 完全一致 | 无 |
| 系统修改 | 预检和空目录不得产生系统变化 | Worker、认证报告和前后探测均为 `ModifiedSystemState=false` | 无 |
| 恢复目标复核 | build/class/所有权变化必须在适配器前拒绝 | build 变化得到 `WindowsBuildChanged`；class 集合或多 PID 得到 `TaskbarTargetChanged` | 无 |
| Explorer 重启表达 | 当前 PID 与日志 PID 不得混淆 | PID 相同为 false；PID 变化时请求保留旧/新 PID 并标记 restarted | 无 |
| 适配器身份 | null、Unavailable、空/超长/控制字符 ID 均不得 Ready | 五类输入全部为 `AdapterUnavailable`，无恢复请求 | 无 |
| 三项 Core 回归 | 新边界不得破坏盒子、文件夹绑定和既有恢复链 | Release 全量 1345/1345；UI 191-ID 合同与 RC restore 合同通过 | 无 |

定向任务栏合同、真实恢复 Worker 和真实认证入口为 30/30。Release 全解决方案构建为 0 warning / 0 error。按 CI 参数执行全量测试为 1345/1345；覆盖率 lines **90.40% (46730/51690)**、branches **76.00% (15284/20110)**，继续通过 90%/75% 门槛。格式与 `git diff --check` 通过。

## 3. 开发目标与原始需求对齐

本阶段直接服务原始第三根 Core：“任务栏美化必须能可靠恢复系统默认”。它解决的是进入真实写入前的所有权和准入边界，没有扩展小组件、插件、自动整理、Tab、工作空间、教程页或通用窗口特效。

没有发生产品方向偏移，但用户可见完成度没有被夸大：

- 桌面右键盒子、文件夹绑定的工程状态未被本阶段改变，物理产品旅程仍 Pending；
- 个性化页的任务栏预设继续不可用；
- `Clear`、半透明、Acrylic 和 SystemDefault 原生效果仍为零；
- 当前测试证明“没有误写且边界可审计”，不证明“任务栏已经美化”；
- 只有专用环境的像素、Explorer 重启、强杀、禁用和卸载恢复全部通过后，才能注册精确 build 适配器。

## 4. 审计发现与修正

复审发现两处文档状态陈旧：Stage 209 仍写“M2 尚未开始”，统一计划收口段仍写“R2～R4 未完成”。本阶段已将 Stage 209 标为历史状态，并把权威计划更新为 R1、R2A1、R2A2a/R2A2b/R2A2c 工程完成、R2B/R3/R4 Pending。README、架构、竞品任务栏审计、路线图和 UI 合同口径同步更新。

代码结构方面，本阶段没有继续扩大 `App.xaml.cs` 或 `MainWindow.xaml.cs`；新增合同位于 Core、适配器选择留在独立 Worker，符合故障域隔离目标。

## 5. 下一唯一开发项

下一步为 `TASKBAR-R2B1`：在可快照回滚的可丢弃 Windows 环境实现首个精确 build 的 `Clear → SystemDefault` 原生适配器实验，并通过现有认证入口记录：

1. 应用前、应用后、恢复后的真实任务栏像素与系统个性化状态；
2. 主/副任务栏 HWND、class、PID 与多显示器结果；
3. 正常确认、超时撤销、Worker/App 强杀后的启动恢复；
4. Explorer 正常重启与强杀重启后的恢复；
5. 禁用功能和卸载后的系统默认恢复；
6. 每个场景的 Expected、Actual、Difference 和失败后的 VM 回滚结果。

在专用环境不存在时不得在日常开发机试写，不得向空目录注册实现，也不得把 Build 26200 加入认证集合。R2B1 的真实效果证据未通过前，正式产品继续保持任务栏预设不可用。
