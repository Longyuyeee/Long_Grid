# Stage 248：Windows App SDK 2.4 升级与真实启动对照审计

日期：2026-08-31

输入基线：`origin/main@15dd67d93bf02b0cbdccd64b397d272d96f20d87`

状态：`Complete / UpgradeRejected / PhysicalJourneyPending`

## 1. 开发目标

Stage 247 已关闭 M1 启动异常清理缺口，但当前电脑的正向物理旅程仍被 `Microsoft.UI.Xaml.dll 3.2.3.0 / 0xc000027b / 0x3a9c5d` 阻断。本阶段不安装系统 Runtime、不使用云电脑，也不放宽 Runtime/UIA 门禁；只审计项目从锁定的 `Microsoft.WindowsAppSDK 2.3.1` 升级到官方当前 Stable `2.4.0`，是否能让精确源码的 self-contained 产物在本机形成真实可见窗口。

微软官方 [Windows App SDK 下载页](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)确认 `2.4.0` 于 2026-08-13 发布为 Stable；[2.0 系列发布说明](https://learn.microsoft.com/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0)列出了输入、Storage Picker、MRT Core、运行时隔离、Composition 和构建工具修复，但没有声明修复本项目的 XAML 启动指纹。因此本轮把升级作为可撤回实验，而不是预设修复。

## 2. Expected / Initial Actual / Difference / Correction

| 检查 | Expected | Initial Actual | Difference / Correction |
|---|---|---|---|
| 依赖恢复 | Stable 2.4.0 可被中央版本管理与锁文件唯一解析 | 顶级包 `2.4.0`，Runtime `2.4.0`，WinUI `2.3.6`；Release build `0 warning / 0 error` | 依赖和编译层面无差异，但不代表运行时通过 |
| 可追溯 self-contained 产物 | 来自精确提交、双 self-contained、哈希与清单完整 | 临时提交 `482d61b` 生成 805 文件 ZIP；SHA-256 `4b65df771a782019b9ff3caf7fb4badd8e084de076b02ec50209809fa1471172`；`dotNetSelfContained=true / windowsAppSdkSelfContained=true` | None；仍为 `signed=false / installer=false / distributionApproved=false` |
| 真实窗口 Ready | 达到 `AppConstructed + ProductWindowActivated + 非空标题`，进程保持存活 | 本机隔离 M1 启动退出 1；产品 exit code `-1073741189`，标题为空；Application Error 1000 指向 `Microsoft.UI.Xaml.dll 3.2.3.0 / 0xc000027b / offset 0x3a9c5d` | 与 Stage 246 完全相同的运行时指纹，升级没有恢复窗口 |
| 二进制对照 | 新版若修复，应体现为行为差异 | Stage 246/248 XAML SHA-256 分别为 `f711dc80...27e81` 与 `b241f72b...55b5`，二进制不同，但文件版本和崩溃指纹相同 | 不能说“DLL 未更新”；准确结论是“新版二进制仍未修复该路径” |
| 失败副作用 | 启动器只回收本次进程和精确 marker 会话 | 新增 M1 证据会话 `0`，无遗留产品进程 | Difference=`None` |

修正不是继续猜测产品线程模型，也不是保留无收益升级。`2.4.0` 依赖变更已由提交 `1499631` 完整撤回，最终树继续锁定 `Microsoft.WindowsAppSDK 2.3.1`；仓库对系统 Runtime `2.4.0.0 + XAML 3.2.3.0` 的已知风险门禁保持不变。

## 3. 真实测试边界

本轮真实测试使用本机 Windows、精确提交 self-contained ZIP、真实 `LongGrid.App.exe`、真实 M1 隔离配置/夹具/marker 和 Windows Application Error/WER，不以 mock、静态 XAML 合同或进程存在代替窗口 Ready。没有安装/卸载系统包、调用跨进程 UIA、发送点击/键盘输入、修改普通用户配置或终止既有 LongGrid 进程。

最终锁定树的真实门禁如下：

| 门禁 | Expected | Actual | Difference |
|---|---|---|---|
| Locked restore / format / Release build | 锁文件可复现、零格式差异、0 warning/error | 全部通过；Release `0 warning / 0 error` | None |
| 完整测试 | 基线 1,398 项全部真实执行 | `1,398/1,398`、0 failed、0 skipped、42 秒 | None |
| UI 合同 | 产品可访问性合同不退化 | ContractOnly `198` IDs，Pass | None |
| 漏洞与许可证 | 已知漏洞 0；未审批时禁止分发 | 漏洞 0；20 项目/30 包，metadata complete；`PendingOwnerReviewAndNotice / distributionApproved=false` | None |
| Runtime 合同 | 锁定目标仍为 2.3.1，九场景失败关闭 | `projectRuntimeMinimumVersion=2.3.1.0 / scenarios=9 / Pass` | None |
| 本机 Runtime / M1 | 包集合不完整或已知风险存在时零启动 | Framework `2.4.0.0`、XAML `3.2.3.0`，缺 Main.2 与 DDLM；`BlockedByIncompleteRuntime`；M1 `startsProcess=false / createsEvidenceSession=false` | None |

许可证入口第一次被误传了不存在的 `-ContractOnly` 参数，PowerShell 在脚本主体执行前拒绝调用；没有产品启动或状态修改。复读真实参数后以合法入口重跑，真实清单和正/负门禁均通过，上表记录最终有效结果。

PR #326 首个精确 head `8fa7ea4432b0bc2d8d9badea13edb9f6c82fb9a8` 的 CI run `33371019724` 与 CodeQL run `33371019597` 均成功：完整测试 `1,398/1,398`、0 skipped、34 秒；coverage lines `90.14% (46930/52064)`、branches `76.04% (15432/20294)`；漏洞 0；许可证继续 `PendingOwnerReviewAndNotice / distributionApproved=false`；artifact `9750264571`、1,003,016 bytes；C# / C++ CodeQL 均通过。PR 无评论、无 review 阻断且 mergeable，Difference=`None`。

本段补录后的最终 PR head 与合并后精确 main 仍必须再次通过远端 CI/CodeQL；在这些结果完成前不得把本阶段标记为远端合并完成。

## 4. 开发目标与需求对齐审计

开发目标审计：已回答“升级官方当前 Stable 是否能在本机恢复真实窗口”——不能。结论来自精确产物与系统事件，不来自版本号推断；无收益升级已撤回，产品源码没有被猜测性修补。

需求对齐审计：本阶段遵循本机真实测试、Expected/Actual/Difference、失败关闭和文档同步要求。M1/M2 继续 `0/2 Complete`，30 项 PF 继续 `0 Complete`，BOX-R1-C/D 与 M1 正向物理旅程仍 Pending，分发继续禁止。

下一接续点：停止对 `2.4.0` 重复升级/重测。只有微软发布明确覆盖该 XAML 指纹的新 Stable Runtime，或取得完整兼容 Runtime、受保护签名包和独占可丢弃 Windows 会话后，才重新执行 BOX-R1-C/D 与 M1 物理旅程；如果继续出现同一指纹，必须保留失败关闭并取得上游修复或可归因堆栈。
