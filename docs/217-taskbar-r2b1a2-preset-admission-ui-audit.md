# Stage 217：TASKBAR-R2B1-A2 预设准入与正式界面审计

日期：2026-08-27

开发基线：`origin/main@cb032b02e882a95f2c539cc7037bcdd50ee11ce8`

状态：`EngineeringComplete / RealProbePass / ContractPass / GitHubPrPass / Integrated / VisibleUiEvidencePending / NativeEffectPending`

## 1. 阶段目标与结论

R2B1-B 的原生 `Clear → SystemDefault` 实验仍因当前宿主没有 Windows Sandbox 启动器、SLAT 未获证明而阻断。为避免在日常宿主试写，同时纠正“个性化 → 任务栏”只有说明文字、看起来不像正常软件的问题，本阶段只交付正式预设界面和有限准入模型：

- 个性化页新增“系统默认”和“通透”两张预设卡片；
- Core 新增 `TaskbarPresetAvailabilityPolicy`，统一处理探测缺失、探测失败、第三方冲突、Build 未认证、适配器缺失和 Ready 六种状态；
- `通透` 只有只读兼容性准入为 `Allowed` 且原生适配器为 `Available` 时才可开放；
- `恢复系统默认` 还要求存在恢复事务；
- 当前 App 明确传入 `TaskbarNativeAdapterAvailability.Unavailable`，两个按钮默认禁用且没有 Click 写入处理器；
- UIA `ItemStatus` 暴露有限状态、两个按钮是否开放和 `Mutation=Disabled`，不公开 HWND 或本机路径。

本阶段让任务栏核心拥有正常产品界面的形态，但不把预设卡片冒充原生效果。当前 Build 26200 的实际用户结果是：能看到两种预设及不可用原因，不能应用或恢复任务栏样式。

## 2. 真实测试：预期、实际、差异与修正

| 检查 | 预期效果 | 当前实际效果 | 差异与修正 |
|---|---|---|---|
| 策略矩阵 | 六种状态有限、任何未证明条件均关闭写入 | 8 个策略用例覆盖 null/失败/冲突/未认证/适配器缺失/Ready/恢复事务/异常 mutation | `Difference=None` |
| 正式 Worker/Explorer | 两次只读探测通过，窗口身份不变，当前 Build 不得误获准入 | Windows `10.0.26200.0` / Build 26200；主/副任务栏由同一 Explorer PID 6932 拥有；身份不变；无冲突；`DeniedNoCertifiedBuild` | `Difference=None` |
| 系统安全 | 探测和 UI 不修改任务栏 | `ModifiedSystemState=false`；卡片按钮无 Click；App 固定 adapter unavailable | `Difference=None` |
| 专项测试 | 新策略与真实进程断言通过 | 15/15 | `Difference=None` |
| Release 构建 | 0 warning / 0 error | 0 warning / 0 error | `Difference=None` |
| UI 合同 | 两个预设、状态文本和有限禁用合同存在 | 195 个 AutomationId，`ContractOnly` Pass | `Difference=None` |
| 可见 UIA | 安全启动正式 App 并读取两个卡片的真实可见状态 | 当前 Windows App Runtime / Microsoft.UI.Xaml 组合在启动前命中已审计的跨进程 `RPC_E_WRONG_THREAD` 风险门 | `VisibleUiEvidencePending`；未使用风险确认开关绕过，也未把合同测试写成可见 Pass |
| 全量回归 | 所有桌面盒子、文件夹和任务栏测试通过 | 1353/1353 | `Difference=None` |
| 覆盖率 | lines ≥ 90%，branches ≥ 75% | lines 90.42% (46808/51768)，branches 76.03% (15310/20138) | `Difference=None` |

首轮错误地直接运行 `Test-LongGridUi.ps1`，实际在正式 App 启动前被已知 UIA 风险门阻断。修正不是关闭门禁，而是按脚本合同改用 `-ContractOnly`，并保留真实 Worker/Explorer 进程测试作为运行时证据。物理可见卡片仍需安全 WinUI 运行时后补证。

PR #264 的干净 Windows runner run `33053629094` 完整通过：1353/1353，coverage lines 90.10% (46644/51768)、branches 75.91% (15286/20138)，195-ID UI 合同、格式、构建、启动链、恢复与资源预检、文件安全、受限缩略图 Worker、依赖漏洞和内部未签名 RC 交付集全部成功。远端与本机没有功能差异；覆盖率数值差异仍高于 90%/75% 门槛。

PR #264 已 squash 合入 `main@36b583f`；main run `33054899086` 为 1353/1353，coverage lines 90.10% (46642/51768)、branches 75.91% (15286/20138)，全部下游门通过。本阶段因此由 IntegrationPending 提升为 Integrated；可见跨进程 UIA 和原生效果状态不变。

## 3. 需求对齐与偏移审计

本阶段直接服务第三根 Core“任务栏美化”，并落实 iTop 值得学习的“视觉预设优先、兼容状态清晰、恢复入口可发现”。没有扩展自动整理、Tab、小组件、Long助手插件、工作空间或窗口特效。

产品边界保持：

- 桌面盒子与文件夹绑定代码和权限没有变化；
- 任务栏原生目录仍为空，当前 App 没有写 API；
- 未认证 Build、冲突、探测失败和缺少适配器均明确失败关闭；
- 界面进展与原生效果进展分别记录，R2B1-B/R3/R4 继续 Pending。

## 4. 下一开发项

唯一原生下一项仍为 `TASKBAR-R2B1-B`：只在 R2B1-A Host/Guest 双层准入通过的可丢弃 Windows Guest 内实现并验证首个 `Clear → SystemDefault` 适配器。当前宿主继续 `EnvironmentBlocked`，不得在宿主试写。

若阻断持续，允许继续收口的范围仅限与三项 Core 直接相关、无需新增系统权限的正式产品呈现或现有安全缺陷；不得再增加教程型页面或外围功能宽度。获得安全 WinUI 运行时后，还需用真实窗口补齐本阶段两张预设卡片的浅色、深色、紧凑、高对比、键盘和 Narrator 证据。
