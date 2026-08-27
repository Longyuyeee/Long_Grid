# Stage 210：TASKBAR-R1A 只读兼容性探测与独立故障域审计

日期：2026-08-27

开发基线：`origin/main@f8368758929ea476ae3076b4266a670e7977041b`

## 1. 阶段结论

本阶段开始第三根 Core 支柱“任务栏美化”，但只完成 `TASKBAR-R1A`：独立进程中的只读兼容性快照和失败关闭准入。它不是任务栏透明、着色或材质功能，不能把 M2 或 TASKBAR-R1 整体标为完成。

当前状态为：

- `LongGrid.TaskbarWorker.exe` 与主 App 分离，探测器崩溃不会直接进入 Explorer 或主 UI 故障域；
- 只读取真实 Windows build、当前会话、`Shell_TrayWnd` / `Shell_SecondaryTrayWnd`、窗口所有进程和已知冲突进程；
- 不调用 `SetWindowCompositionAttribute`、`SetWindowRgn`、Explorer 注入、Hook、注册表写入或 Explorer 重启；
- R1A 的认证 build 集合故意为空。探测成功仍返回 `DeniedNoCertifiedBuild`，只有 R4 的应用、回退、Explorer 重启和卸载实机矩阵完成后才能增加认证项；
- M1 仍是 `ProductEvidencePending`。本机 WinUI 2.4.0 / XAML 3.2.3.0 的跨进程 UIA 组合仍被上游 `RPC_E_WRONG_THREAD` fail-fast 阻断，不能用禁用辅助功能或绕过预检伪造通过。

因此本阶段没有改变产品方向：M1 的证据债没有被宣布完成，只是在无法安全取得物理证据时，受控推进另一项原始核心能力。

## 2. 需求与实现对齐

| 原始要求 | 本阶段实现 | 状态 |
|---|---|---|
| 任务栏美化是 Core，而非附属功能 | 新增正式 `src/LongGrid.TaskbarWorker` 工程，不再只停留在竞品文档 | `R1A EngineeringComplete` |
| 不注入、不 Hook Explorer | 仅枚举顶层窗口并读取其 PID/进程名 | 对齐 |
| Windows 更新必须失败关闭 | 用真实 build 建立准入输入；未认证 build 一律拒绝 | 对齐 |
| 与其他美化软件避免冲突 | 有界识别 TranslucentTB、RoundedTB、Start11、StartAllBack、ExplorerPatcher、Windhawk 进程；发现后拒绝准入 | R1A 对齐；后续还需签名/路径和误报矩阵 |
| 独立故障域 | 探测运行在单独 EXE；主 App 尚未连接 Worker | 部分完成，R1B 补超时、协议和生命周期 |
| 一键恢复系统默认 | 本阶段不修改系统，因此无状态可恢复 | R2/R3 Pending |
| 像 iTop 一样提供简单预设 | 本阶段无预设 UI，也不修改个性化页占位文案 | R2 Pending |

## 3. 真实测试：预期、实际、差异与修正

### 3.1 编译差异

| 检查 | 预期效果 | 首次实际效果 | 修正后实际效果 |
|---|---|---|---|
| x64 Release 编译 | 0 warning / 0 error | `LibraryImport` 对版本结构和 `StringBuilder` 生成封送失败，共 6 个错误 | 改用目标框架稳定支持的 `DllImport`，字符缓冲改为有界 `char[256]`；最终 0 warning / 0 error |
| 静态分析 | 互操作返回值和集合类型无告警 | 首次修正后发现 4 个错误：未使用线程 ID 返回值、`StringBuilder` P/Invoke、两个可具体化返回类型 | 显式检查线程 ID、使用字符数组、返回数组；最终 0 warning / 0 error |
| 策略测试 | 未认证、冲突、无主任务栏、非 Explorer 所有者、意外修改均失败关闭 | 5 项全部通过 | 无剩余差异 |
| CI 锁定还原 | 新工程引用必须与测试项目 lock file 一致 | PR 首次 CI 在 `dotnet restore --locked-mode` 返回 `NU1004`，指出测试项目新增 Worker 引用未进入 lock file | 用 `--force-evaluate` 更新测试项目 lock file，再以 `--locked-mode` 真实复验通过；不放宽 CI |
| CI 格式工作区 | GitHub Windows runner 能加载完整解决方案 | lock file 修正后的 CI 在 Format 报 `Unable to locate dotnet CLI`；项目图中测试程序集直接引用可执行 Worker | 将纯快照/准入策略移入 `LongGrid.Core`，改为 Worker → Core、Tests → Core 的单向依赖；等待 CI 复验，不把本机通过替代 runner 结果 |
| 依赖重排编译 | Core/Worker 重排后仍为零告警 | 静态分析要求仅内部使用的 JSON source context 可密封 | 改为 `sealed partial`，不禁用规则；随后重新构建 |

### 3.2 当前 Windows 真实只读运行

权威命令：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng/Test-LongGridTaskbarCompatibilityProbe.ps1 -Configuration Release
```

| 检查 | 预期效果 | 实际效果 | 差异 |
|---|---|---|---|
| 系统识别 | 读取真实版本和会话 | `10.0.26200.0`，build `26200`，session `2` | 无 |
| 主/副任务栏 | 1 个主任务栏；允许存在副任务栏 | 发现 `Shell_TrayWnd` 1 个、`Shell_SecondaryTrayWnd` 1 个 | 无 |
| 所有权 | 所有任务栏窗口由 Explorer 拥有 | 两个窗口均由 `explorer` PID `6932` 拥有 | 无 |
| 冲突 | 当前会话无已知冲突进程 | 空集合 | 无 |
| 零修改 | 连续两次探测前后窗口身份不变，探测报告不修改系统 | 窗口 handle/class/PID/name 完全一致，`ModifiedSystemState=false` | 无 |
| 准入 | 未经 R4 认证的 build 必须拒绝 | `ProbeOutcome=Pass`，`RuntimeAdmission=DeniedNoCertifiedBuild` | 无；成功探测不等于允许改色 |

脚本最终输出 `Outcome=Pass / Difference=None`。这里的 Pass 只证明 R1A 的只读探测行为符合预期。

全量回归：`dotnet format LongGrid.sln --verify-no-changes --no-restore` 退出码为 0；Release 全解决方案构建预期零警告/零错误，实际 `0/0`；完整核心测试预期零失败，实际 `1277/1277` 通过，其中本阶段新增策略测试 `5/5`。

## 4. M1 阻塞复核

2026-08-27 再次运行 `eng/Test-LongGridWinUiUiaRuntime.ps1`，预期安全组合应允许外部自动化；实际仍为 Windows App Runtime `2.4.0.0`、XAML `3.2.3.0`，结果 `BlockedByKnownUpstream / KnownUnsafeCrossProcessUiaRuntimePairPresent`。微软 WinUI issue [#11139](https://github.com/microsoft/microsoft-ui-xaml/issues/11139) 仍在 Backlog 且无里程碑；Windows App SDK [2.4.0 稳定版公告](https://github.com/microsoft/WindowsAppSDK/discussions/6687) 的修复列表也未包含该问题。

当前还存在一个本会话无权检查或终止的既有提升权限 `LongGrid.App` 进程。因此本轮没有启动 DesktopHost 实机证据、没有驱动 UI，也没有安装未签名包。M1 继续保持 Pending。

## 5. 下一阶段

当前唯一编码项改为 `TASKBAR-R1B`：

1. 主 App 通过有界 JSON 协议调用 Worker，加入父进程绑定、超时、输出上限、协议版本和畸形响应拒绝；
2. 个性化页以用户语言显示“可检测 / 有冲突 / 当前版本未认证”，不得显示可点击但无效果的预设；
3. Worker 异常、超时和崩溃不能影响 App，也不能触碰 Explorer；
4. 用真实独立进程测试正常、超时、畸形、错误版本和父进程退出；记录预期与实际差异。

R1B 完成后才进入认证策略与 R2 预设应用；M1 在获得安全运行时和专用环境后立即恢复物理证据，不因本阶段推进而降级出口。
