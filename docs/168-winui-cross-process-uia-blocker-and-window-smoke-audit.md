# Stage 168：WinUI 跨进程 UIA 阻断与真实窗口冒烟审计

- 审计日期：2026-08-20
- 开发基线：`codex/pf002d-create-preview@d4d2207`
- 对应目标：PF-002 正式 App 证据收口
- 结论：**真实进程/窗口生命周期通过；跨进程 UIA 崩溃确认为 WinUI 3 上游 P0 缺陷，PF-002 正式 UIA/Narrator 证据继续 Pending，但不再把它误归因于 Long方格业务回归**

## 1. 本轮判断

Stage 167 已证明当前版本和 `fff20f2` 隔离基线均在读取 WinUI 可访问树时崩溃。本轮进一步完成版本、事件和上游缺陷对齐：

1. Long方格引用 `Microsoft.WindowsAppSDK 2.3.1`，使用框架依赖部署；
2. 崩溃进程实际加载系统 `Microsoft.WindowsAppRuntime.2_2.4.0.0_x64` 中的 `Microsoft.UI.Xaml.dll 3.2.3.0`；
3. Windows Application Error 为 `0xc000027b`，WER P7 为 `8001010e`；
4. Microsoft WinUI 官方仓库问题 [#11139](https://github.com/microsoft/microsoft-ui-xaml/issues/11139) 描述了同一跨进程 UIA 查询、同一 `RPC_E_WRONG_THREAD`、同一 fail-fast 结果，并明确指出应用代码无法捕获或规避；截至本次审计该问题仍为 Open；
5. 因此升级业务线程调度、吞异常、删除 AutomationProperties 或把 UIA 测试改成 Pass 都不是有效修复。

这个判断只适用于当前 `Microsoft.UI.Xaml` 跨进程 UIA 崩溃。它不能证明 Long方格已经满足 Narrator、键盘、高对比或完整 UIA 需求。

## 2. 新增真实窗口冒烟测试

新增 `eng/Test-LongGridWindowSmoke.ps1`，直接启动正式构建产物，不读取 UIA 树，验证：

- 测试前不存在仍持有句柄的 LongGrid.App 实例；
- 真实进程在 10 秒内发布标题为“Long方格”的主窗口；
- Windows 持续报告进程可响应；
- 通过主窗口正常关闭后，应用在 10 秒内完成关闭排空；
- 退出码为 0；
- 输出同时记录 Expected、Actual 和 Difference，不用“脚本退出码 0”代替产品事实。

该脚本不验证按钮、焦点、Preview 或可访问树，不得替代 PF-002 的交互矩阵。

## 3. 真实测试：预期、实际、差异与修正

### 3.1 首轮脚本执行

| 检查项 | 预期 | 首次实际 | 修正 |
| --- | --- | --- | --- |
| PowerShell 运行时兼容 | Windows PowerShell 5.1 可执行 | `[nint]` 类型不可用，测试在启动产品前失败 | 改为兼容的 `[IntPtr]::Zero` |
| 既有实例判定 | 拒绝真实运行实例，不被已终止的零句柄残留误阻断 | 系统保留一个 `HandleCount=0` 的已终止进程表项 | 只把仍持有句柄的进程视为活实例 |

这两个差异均发生在测试基础设施，不能记为产品通过或失败。修正后必须重新运行真实产品。

### 3.2 修正后真实 Release 执行

修正验证先以 5 秒区间通过；正式提交前再以默认 20 秒区间执行：

```powershell
.\eng\Test-LongGridWindowSmoke.ps1 -NoBuild
```

| 检查项 | 预期 | 实际 | 差异 |
| --- | --- | --- | --- |
| 窗口标题 | `Long方格` | `Long方格` | 无 |
| 窗口发布 | 10 秒内 | 1,026 ms | 无 |
| 可响应稳定区间 | 20 秒 | 20 秒 | 无 |
| 正常关闭 | 10 秒内排空 | 已排空 | 无 |
| 退出码 | 0 | 0 | 无 |
| 跨进程 UIA | 本测试不得查询 | 未查询 | 无 |

默认 20 秒稳定区间已通过；5 秒预跑只用于证明脚本修正有效，不作为最终稳定性证据。

## 4. 需求对齐

| 需求 | 当前证据 | 状态 |
| --- | --- | --- |
| 软件有真实可启动界面 | Release 进程和“Long方格”窗口真实出现 | Pass |
| 普通运行稳定 | 不查询 UIA 时持续响应 20 秒并正常退出 | Pass |
| 正式按钮—Preview—取消—确认—撤销 | 跨进程 UIA 路径会触发上游 fail-fast；本轮界面控制被用户按 Escape 停止 | Pending |
| 自动化/屏幕阅读器不会让 App 崩溃 | 当前运行时不能满足 | BlockedByUpstream |
| 不伪造测试 | 失败、首次脚本差异、外部阻断均单独记账 | Pass |
| 不移动或修改桌面文件 | 本轮只启动/关闭产品窗口 | Pass |

## 5. 后续执行顺序

1. 保留 PF-002 为 `EngineeringComplete / ProductEvidencePending`，不把 UIA 崩溃标记为产品 Pass；
2. 在 Microsoft 官方修复可用后升级到包含修复的稳定 Windows App SDK/Runtime，并首先重跑 `Test-LongGridUi.ps1 -NoBuild`；
3. 在当前运行时可使用真实鼠标/键盘或坐标截图完成按钮—Preview—取消—确认—保存失败—撤销矩阵，但每条证据必须明确“未查询 UIA”；
4. Narrator、Accessibility Insights 和跨进程 UIA 验收必须等待上游修复，不允许通过关闭可访问性或删除语义规避；
5. PF-002 工程链不再因这个已隔离外部缺陷停摆；下一开发切片仍优先完成 PF-002 真实输入证据，完成后再进入 PF-003。

## 6. 提交前验证

| 门禁 | 结果 |
| --- | --- |
| 真实 Release 窗口生命周期（默认 20 秒） | Pass；1,026 ms 发布窗口，连续响应 20 秒，退出码 0 |
| Release 全量测试 | 1,010/1,010 Pass，0 跳过 |
| Release App 构建 | Pass；0 warning、0 error |
| 静态 UI 合同 | Pass；147 个 AutomationId |
| `dotnet format --verify-no-changes` | Pass |
| `git diff --check` | Pass |
| 跨进程 UIA 实机门禁 | Fail / BlockedByUpstream；不得计入 Pass |
