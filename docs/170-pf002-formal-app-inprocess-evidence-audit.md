# Stage 170：PF-002 正式 App 进程内证据与 WinUI 安全预览审计

- 审计日期：2026-08-21
- 开发分支：`codex/pf002d-create-preview`
- 对应目标：PF-002 正式 App 的 Preview 取消—确认—保存—重载证据
- 结论：**正式 Release App 的进程内 UI 线程证据连续两次通过；可见 Preview、可见视图发布、物理输入、UIA 与 Narrator 仍被当前 WinUI 上游缺陷阻断，因此 PF-002 保持 `EngineeringComplete / ProductEvidencePending`**

> 后续状态：本文件审计时尚未执行的“最近撤销正式 App 证据”已由 [Stage 171](171-pf002-formal-app-latest-undo-evidence-audit.md) 关闭；本文件记录的可见交互、物理输入与无障碍阻断仍然有效。

## 1. 本轮交付

新增默认关闭的 `LONGGRID_PF002_APP_EVIDENCE_SESSION=<32 位 GUID>` 证据入口。它只接受已由外部测试创建、位于 `%TEMP%\LongGridEvidence\<guid>`、为空且不是重解析点的目录，并使用独立 AppInstance key。普通启动继续使用 `LongGrid.Main` 与 `%LOCALAPPDATA%\LongGrid`，不会进入证据链。

证据会话在正式产品进程和 UI 线程中依次执行：

1. 创建正式 `MainWindow` 并等待真实 XAML、配置、显示拓扑和 DesktopHost readiness；
2. 在已知不安全运行时下隐藏窗口，避免外部 UIA 客户端对动态树执行跨进程查询；
3. 使用预加载的正式安全 Preview XAML 驱动一次取消；
4. 复读内存状态与正式配置存储，证明仍为 0 个方格和 `Missing`；
5. 再次驱动确认，名称为“PF-002 证据方格”；
6. 通过正式 commit/save controller 完成保存，并从正式 store 重载；
7. 原子写出 Expected、Actual、Difference、Outcome JSON；
8. 正常关闭 App，由外部脚本复核退出码、桌面元数据、用户配置元数据和临时目录清理。

证据模式不扫描桌面 Catalog，不写用户配置，也不操作桌面文件。测试前后只对目录项的名称、属性、长度和时间元数据做哈希比较，输出不泄露文件名。

## 2. 产品缺陷与修正

### 2.1 首个空配置无法进入创建态

首次运行没有保存配置时，控制中心可正确呈现 `NoSavedConfiguration`，但 DesktopHost 投影收到空状态并停在 `AwaitingHost`，导致桌面创建入口无法开始。

修正后，控制中心仍保留“未保存配置”语义；DesktopHost 单独使用正式默认值解析出的临时空工作区，只用于允许第一个方格进入 `AwaitingWorkspace`。该状态不会提前保存，也不会伪造已有配置。

### 2.2 第二顶层 WinUI Preview 的销毁崩溃

真实轨迹证明预览窗口的激活、presenter、切换器和定位均成功；`Window.Close()` 返回 `0x800710DD`，改用官方 `AppWindow.Destroy()` 后仍在稍后触发：

- `Microsoft.UI.Xaml.dll 3.2.3.0`；
- `0xc000027b`；
- WER 内层 `combase 0x802b000a`。

这不是保存控制器死锁。崩溃发生后进程退出，外部脚本只能看到最后发布的 `CompletingFormalSave`。

产品新增精确运行时安全门：只有 `Microsoft.WindowsAppRuntime.2 2.4.0.0 + Microsoft.UI.Xaml 3.2.3.0` 使用主窗口内预加载、持久存在的现代 Preview 面板；其他运行时仍保留候选桌面位置的原生无边框 Preview。门禁无法枚举运行时时不扩大黑名单。

### 2.3 当前机器的动态可见 UIA 树仍会崩溃

真实复核继续发现，即使不创建第二窗口，只要让 ContentDialog、持久 Preview 面板或确认后的新方格列表成为可见动态 UIA 子树，当前机器上的外部可访问性客户端仍会触发同一上游 fail-fast。隐藏主窗口后，仅更新可访问列表也会触发。

因此证据模式在 XAML readiness 后隐藏 AppWindow，并在提交时抑制**证据专用的可见视图发布**。提交、workspace session、save controller、正式磁盘写入和重载均不绕过。普通产品路径不抑制视图发布。

该边界在 JSON 中固定记录：

- `PreviewActivatedCount = 0`；
- `PreviewDrivenCount = 2`；
- `VisibleInteractionStatus = BlockedByKnownUpstream`；
- `VisibleViewPublication = BlockedByKnownUpstream`。

不得把本轮 Pass 描述为可见鼠标、UIA 或 Narrator Pass。

## 3. 真实测试的预期、实际、差异

最终连续两次运行：

```powershell
.\eng\Test-LongGridPf002AppEvidence.ps1 -NoBuild
.\eng\Test-LongGridPf002AppEvidence.ps1 -NoBuild
```

两次实际结果一致：

| 检查项 | 预期 | 实际 | 差异 |
| --- | --- | --- | --- |
| 初始状态 | 0 个方格，磁盘 Missing | `0 / Missing` | 无 |
| 取消 | 0 个方格，磁盘 Missing | `0 / Missing` | 无 |
| 确认 | 1 个方格，名称正确 | `1 / PF-002 证据方格` | 无 |
| 正式保存 | Completed | `Completed` | 无 |
| 正式重载 | 1 个方格，LoadedPrimary | `1 / LoadedPrimary` | 无 |
| Preview XAML | 两次真实加载并驱动 | `VisualTree=2 / Driven=2` | 无 |
| 可见激活 | 当前上游缺陷下不得声称通过 | `0 / BlockedByKnownUpstream` | 按阻断口径 |
| 桌面元数据 | 不变 | 不变 | 无 |
| 用户配置元数据 | 不变 | 不变 | 无 |
| 临时证据目录 | 删除 | 删除 | 无 |
| 进程退出 | 0 | 0 | 无 |

测试开发期间的真实差异还包括：静态 JSON options 分析器失败、PowerShell 多行条件解析失败、Windows PowerShell 缺少 `Convert.ToHexString`、安全 Preview 子节点预期数 3/实际 4、保存阶段误判为死锁，以及运行时文件版本字符串不适合精确比较。所有差异均先保留失败，再修正实现或测试口径并重跑；没有降低产品断言。

最终窗口冒烟还暴露一个测试缺陷：首个空工作区投影修正后，DesktopHost 可在稳定区间内创建自己的顶层 HWND。旧脚本反复 `Process.Refresh()` 后使用漂移的 `MainWindowHandle`，把 `WM_CLOSE` 发给了 DesktopHost，而不是启动时已验证标题为“Long方格”的控制中心；产品 Closing 处理器从未进入。测试现冻结首次已验证 HWND，稳定区间用 `IsWindow` 复读，结束时只向该句柄发送 `WM_CLOSE`。基于错误诊断临时加入的产品资源排空改动已撤回。修正后两轮 20 秒实际启动时间为 1,655 ms 和 1,068 ms，均正常退出 0。

## 4. 安全与清理

外部脚本在启动前拒绝仍持有句柄的 LongGrid 实例；只给子进程设置证据和 DesktopHost 环境变量，随后恢复原值。证据目录清理要求：

- 绝对路径必须位于固定临时根下；
- 叶名称必须与 GUID 完全一致；
- 拒绝重解析点；
- 最多 20 次、每次 250 ms 的锁释放重试；
- 清理后再次确认路径不存在。

调试崩溃遗留的 `97f8b0528bd740d787cc34f6a31f9588` 已通过新增的精确 `-CleanupSessionId` 安全入口删除，结果为 `Removed=true / Outcome=Pass`。

## 5. 需求对齐与剩余差距

| 需求 | 状态 |
| --- | --- |
| 正式 App 而非独立 probe | Pass |
| 真实 XAML 与 UI 线程 | Pass |
| Preview 取消—确认 | Pass（进程内驱动、不可见） |
| 正式保存与重载 | Pass |
| 预期—实际—差异 | Pass |
| 零桌面文件/用户配置副作用 | Pass |
| 可见打开—编辑—取消—确认 | BlockedByKnownUpstream |
| 确认后的可见列表发布 | BlockedByKnownUpstream |
| 物理鼠标/键盘/触控 | PendingManualEvidence |
| UIA/Narrator | BlockedByKnownUpstream |
| 最近撤销的正式 App 证据 | Pending；本轮未执行 |

PF-002 的下一安全步骤不是继续在当前运行时强行查询 UIA，而是：在包含上游修复的稳定 Windows App SDK/Runtime 或独立无有害客户端机器上，重跑可见 Preview、视图发布、物理输入和无障碍矩阵；随后补最近撤销的正式 App 证据。工程开发可继续 PF-003，但这些门禁继续阻止 PF-002 `Complete` 和公开分发。

## 6. 提交门禁

最终提交 SHA 与远端 CI 在推送后补录。本地结果：

- PF-002 App 证据连续两次 Pass；
- 153-ID 静态 UI 合同 Pass；
- Release 全量测试 1010/1010；
- Release 构建 0 warning、0 error；
- 正式窗口生命周期连续两次 20 秒 Pass，启动 1,655/1,068 ms，退出码 0；
- `dotnet format --verify-no-changes` 与 `git diff --check` Pass。
