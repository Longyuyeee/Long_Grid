# Stage 196：PF-006B2B2 权威重试与安全 Explorer 定位审计

- 日期：2026-08-24
- 分支：`codex/pf002d-create-preview`
- 起始基线：`0133a3e`
- 对齐编号：`PF-006B2B2 / PF-006B2B / PF-006`
- 结论：PF-006 `InProgress`；PF-006B2B2 工程切片完成

## 1. 开始审计与权限边界

Stage 195 已关闭默认双击/显式单击差距，但打开失败仍只有有限文本，用户只能再次按 Enter，无法发现重试和定位动作。直接把路径或旧解析结果交给 DesktopHost 会破坏既有隐私与权威边界；根据旧失败状态直接启用 Explorer 也会产生 TOCTOU 风险。

本轮把动作放在失败项目的原生右键菜单中：`重新验证并重试` 和 `在资源管理器中定位`。Surface 只持有 container、`item:ordinal`、有限状态和两个可用动作位；动作再次进入生命周期，由当前 batch 补齐 display、workspace revision 和 topology generation，再由 App 的唯一 `ProductDesktopItemOpenController` 从当前 workspace/Catalog/现场文件系统重新解析。旧路径、Shortcut 解析目标和参数均不缓存到 Surface。

## 2. 正式实现与安全收敛

### 2.1 有限反馈动作

- `ProductDesktopItemOpenResult` 增加 `CanRetry`、`CanLocateInExplorer`，生命周期只把布尔动作位和无路径中文提示发给 HWND/UIA；
- 失败项目右键使用标准 Win32 `HMENU`，只显示当前权威结果允许的动作；标准原生菜单提供系统键盘/辅助功能基础，Narrator 真人证据仍 Pending；
- 打开菜单前重新命中同一 container/item 并先同步共享选择；输入必须由 `GetCurrentInputMessageSource` 证明、不得为 Injected；
- `FeedbackRetry` 不复用旧目标，完整重跑既有 File/Folder/Shortcut/URL 权威验证与 Shell 提交；
- `FeedbackLocateInExplorer` 是独立来源，不能借重试路径绕过定位专用检查。

### 2.2 Explorer 定位

- 再次验证 request、当前 workspace revision、权威 topology generation、唯一 container/display/item；
- 引用必须为 Resolved、File/Folder/Shortcut/URL、filesystem provider，Catalog/Persisted kind 一致，身份字段成对且 Catalog canonical target 与 persisted target 完全一致；
- 父目录必须现场存在且自身不是 ReparsePoint；现存目标也不得为 ReparsePoint；
- 现存目标使用 `/select,"target"`，缺失目标只打开已验证父目录，不把不存在路径交给 `/select`；
- 只调用 `%WINDIR%\explorer.exe` 的绝对系统路径，不依赖 PATH 查找；参数有界于 Windows 路径且拒绝引号；
- 返回 `ExplorerLocateAccepted/ParentUnavailable/ParentUnsafe/LaunchFailed` 等有限状态，UIA/HWND 消息不含路径。

## 3. Expected / Actual / Difference 与修正

| 场景 | Expected | Actual | Difference / 修正 |
| --- | --- | --- | --- |
| 第一轮 Release 编译 | 0 warning/error | 测试证据常量数组触发 CA1861，build 失败 | 有；改为有限稳定字符串后重新编译，旧二进制测试不计入结果 |
| 真实缺失文件首次打开 | `TargetUnavailable`、零 launch、可重试 | 完全一致 | None |
| 同一路径真实文件出现后重试 | 重新读当前状态并只 launch 1 次 | `LaunchAccepted`，记录目标 1 次 | None |
| 真实安全父目录、目标缺失 | 绝对系统 Explorer，只打开父目录，不 `/select` 缺失目标 | 完全一致 | None |
| 父目录缺失 | launcher 前 `ExplorerParentUnavailable` | 完全一致，launch 0 次 | None |
| 真实系统 ReparsePoint 父目录 | launcher 前 `ExplorerParentUnsafe` | `C:\Users\All Users` 现场为 ReparsePoint，launch 0 次 | None |
| 真实原生 HWND 动作 | 只提交 Retry/Locate 两种可信来源 | 两种各 1 次 | None |
| 非可信/Injected HWND 动作 | 全部拒绝 | 返回 false，提交数不增加 | None |
| 显式可见真实 Explorer | 系统接受现存 `where.exe` 定位请求 | `ExplorerLocateAccepted` | None |
| HWND/UIA 文本 | 有动作提示但无路径/参数 | 只含有限中文和动作位 | None |

真实文件系统测试先引用不存在的临时文件，确认零 launch 后创建真实文件，再从 `FeedbackRetry` 入口重新验证；定位测试使用真实临时目录和生产控制器记录提交给系统的绝对 Explorer/参数。另以 `LONGGRID_RUN_VISIBLE_EXPLORER_EVIDENCE=1` 单独运行一次可见副作用测试，真实 `ShellExecuteExW` 打开 Explorer 并定位系统 `where.exe`，该环境开关默认关闭，避免日常全量测试反复弹窗。真实 HWND 测试创建生产原生 Surface 并走与菜单共用的动作提交核心；物理右键、真人 Narrator 和高对比截图仍 Pending。

## 4. 最终门禁

- Release 全量：1163/1163，0 failed，0 skipped；
- Release 全解决方案：0 warning、0 error；
- 真实文件出现前后权威重试：`Difference=None`；
- 真实安全/缺失/ReparsePoint 父目录 Explorer 判定：`Difference=None`；
- 显式可见真实 Explorer `ShellExecuteExW`：`Difference=None`；
- 真实 DesktopHost HWND Retry/Locate/非可信/Injected：`Difference=None`；
- UI 合同：157 AutomationId，PowerShell 7 `ContractOnly`，`Outcome=Pass`；
- `dotnet format --verify-no-changes` 与 `git diff --check`：通过；
- 零桌面文件移动、删除或重命名；测试临时文件已清理；零路径/参数进入 HWND/UIA 文本。

## 5. 需求对齐与下一步

PF-006B1、B2A、B2B1、B2B2 已关闭 File/Folder/Shortcut/URL 打开、有限反馈、默认双击/显式单击、失败重试和安全定位的正式工程链。PF-006 仍为 `InProgress`，因为跨视口 PageUp/PageDown、鼠标框选以及物理/高对比/Narrator 证据尚未关闭。

下一工程切片固定为 **PF-006C1：PageUp/PageDown 跨视口键盘导航**。它必须在同一显式租约和真实 HWND 中推动 viewport、选择、焦点与 UIA 快照原子收敛，页边界不越界，auto-repeat 和陈旧来源失败关闭。随后再进入 PF-006C2 框选；PF-001～PF-005 的产品证据、签名安装和公开分发门禁继续 Pending。
