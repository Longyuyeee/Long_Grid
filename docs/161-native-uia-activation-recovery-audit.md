# Stage 161：原生 UIA 激活与前台拒绝恢复审计

- 日期：2026-08-20
- 分支：`codex/pf002d-create-preview`
- 目标：修复 PF-002D1 全量测试暴露的原生 UIA `ElementNotEnabledException`，并保证 Windows 拒绝前台切换时入口恢复安全状态
- 结论：**原生 HWND/UIA 激活合同通过；完整 App 预览交互证据继续 Pending**

## 1. 预期与开发前实际

| 检查项 | 预期 | 开发前实际 |
| --- | --- | --- |
| UIA Invoke | 辅助技术直接消费已证明的激活请求，不抢前台 | 与键盘代理共用 `SetForegroundWindow`，当前会话返回 `ElementNotEnabledException` |
| 键盘代理失败 | 恢复 Passive、`WS_EX_NOACTIVATE` 和再次激活能力 | 取消选择后未恢复窗口策略和 `activationAvailable` |
| 全量测试 | 983/983 | 982/983 |

根因不是 PF-002D Preview 状态，而是所有激活种类在 `Forward` 后都强制进入键盘代理。UIA 本身可以继续使用正式 Surface 的 UIA provider，不应依赖 Windows 前台授权。Pointer/keyboard 需要键盘代理，但操作系统可能因为 Foreground Lock 规则拒绝测试进程抢前台；这种拒绝必须失败关闭且可恢复。

## 2. 修正

- `AssistiveTechnologyActivation` 成功消费后保持 NoActivate，不调用键盘代理；
- pointer/keyboard 路径仍使用既有键盘代理，不降低物理输入门禁；
- `EnterKeyboardProxy` 失败后取消选择、恢复 `WS_EX_NOACTIVATE`、恢复 `activationAvailable`，并以 `SW_SHOWNOACTIVATE` 重新证明 Passive；
- 原生测试要求 UIA Invoke 后窗口仍不是前台；
- 键盘代理测试接受两种真实 Windows 结果：系统允许时进入前台；系统拒绝时必须恢复 `CanActivate + ContractAttested + 非前台`，禁止半失效状态。

## 3. 真实验证差异

| 验证 | 预期 | 修正后实际 | 结果 |
| --- | --- | --- | --- |
| 原失败聚焦测试 | UIA Invoke 成功且安全 | 1/1，通过真实 HWND 和 `AutomationElement/InvokePattern` | Pass |
| UIA 前台行为 | 不抢前台 | `OwnsForegroundWindow == false` | Pass |
| 键盘代理受拒恢复 | 可再次激活且合同有效 | 当前会话进入受拒分支，`CanActivate == true`、`ContractAttested == true` | Pass |
| Release 全量测试 | 983/983 | 983/983 | Pass |

这里的“真实”是当前 Windows 会话中的真实 HWND、窗口样式、UIA provider 和 Foreground Lock 行为，不是 mock。它仍不等于正式 App 的完整视觉旅程。

## 4. 正式 App 证据边界

正式 App 可以启动并被枚举为唯一“Long方格”窗口，但窗口状态抓取期间实机控制器先后报告窗口句柄更新、最小化和检测到用户输入；按自动化安全流程停止，没有继续使用陈旧句柄、坐标或直接 PowerShell UIA。因此以下结果继续 Pending：

- 从 DesktopHost 入口打开 Preview；
- 默认名称聚焦和全选；
- 非法/合法名称切换；
- Cancel/Escape 后配置 revision 与容器数不变；
- 确认只创建一个方格。

## 5. 需求对齐与下一步

本切片提升了 UIA 可访问性和运行稳定性，没有扩大文件权限、输入范围或前台控制能力；它关闭 Stage 160 的全量测试阻断，但不关闭 PF-002D。

下一步保持不变：完成 PF-002D2 DesktopHost 候选位置内的原生就地编辑表面，并在无并发用户输入的窗口会话中补跑完整 App 预期—实际矩阵。未经该证据，不得把 PF-002D 标记完成。
