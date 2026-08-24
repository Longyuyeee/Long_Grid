# Stage 181：PF-003D5 正式 App 可见捕获上游阻断审计

- 审计日期：2026-08-24
- 开发分支：`codex/pf002d-create-preview`
- 起始基线：`f5ddcaa`
- 对应目标：在正式 Release App 上执行可见 SendInput/截图并核对 PF-003 工程完成口径
- 结论：**真实 Windows Capture 两次稳定复现已知 Microsoft.UI.Xaml 上游崩溃，零点击、零拖动、零配置提交；PF-003 工程范围已由 Stage 172–179 完成，状态调整为 `EngineeringComplete / ProductEvidencePending`。**

## 1. 真实执行范围

本轮使用仓库 Release `LongGrid.App.exe`、实际双显示器会话、真实 Win32/WinUI 窗口和 Windows Graphics Capture。Windows 应用控制只按截图坐标工作，不请求 UIA 文本树；每次动作前要求唯一 Long方格窗口，发现捕获内容不是产品界面时立即停止，避免误点下层应用。

本轮没有读取、创建、移动或删除桌面文件，没有修改显示设置，也没有把 SendInput 称为物理设备输入。

## 2. Expected / Actual / Difference

| 检查 | Expected | Actual | Difference / 处置 |
| --- | --- | --- | --- |
| 正式窗口启动 | 唯一可见 Long方格主窗口 | 新实例 PID 存活并短暂报告 Long方格窗口 | 启动事实成立 |
| Windows Capture | 返回 Long方格可见内容 | 捕获只显示透明 Surface 下层内容，随后目标窗口消失 | 立即停止，未使用坐标 |
| 进程生命周期 | 捕获后继续响应 | 两次均在捕获后退出 | 读取 Windows Application Error/WER 定位 |
| 故障模块 | 无进程崩溃 | `Microsoft.UI.Xaml.dll 3.2.3.0` | 与 Stage 169 已知组合一致 |
| 异常 | 无 | Application Error `0xc000027b`；WER P7=`8001010e` | `RPC_E_WRONG_THREAD` fail-fast，上游阻断 |
| 输入与配置 | 截图后再执行 SendInput | 实际未执行点击、拖动或键盘输入，零配置提交 | 不产生伪证据，保持 Pending |
| 全量门禁编排 | Release test/build 均通过 | 首轮并行执行竞争 `obj/Release`，测试以 CS2012 文件占用失败 | 等待 build/format 后以 `--no-build` 顺序重跑，1075/1075 通过 |

两次独立报告 ID 不同，但故障应用、模块版本、异常代码、偏移和 WER 签名一致。日志中的本机路径和报告位置只在本地核对，文档不保存设备身份、用户路径或下层应用内容。

## 3. 为什么不修改布局生产代码

Stage 168–169 已证明当前框架依赖应用实际选择 Windows App Runtime 2.4.0.0 / Microsoft.UI.Xaml 3.2.3.0，跨进程 UIA/捕获可在应用代码无法捕获的线程边界 fail-fast。本轮签名与既有签名一致，没有证据指向 Stage 179 的目标 DPI、Surface 路由、唯一提交或保存补偿。

危险地绕过 `Test-LongGridUi.ps1` 预检、删除无障碍语义或继续对透明下层窗口发送坐标，都不能成为修正。本轮正确处置是停止输入、保留 Product Evidence blocker，并在上游安全运行时或独立安全机器复测。

## 4. PF-003 工程完成审计

| PF-003 工程范围 | 证据 |
| --- | --- |
| 移动/八向缩放、网格/边缘吸附、Shift 反转 | Stage 172 |
| 冻结事实会话、累计预览、取消、唯一完成凭据 | Stage 173 |
| 正式保存失败补偿与真实写租约故障 | Stage 174 |
| Surface 九向原生 capture 输入合同 | Stage 175 |
| 正式 App 候选、提交、保存和补偿组合根 | Stage 177 |
| 标题焦点、1/8 DIP 键盘移动和 Alt 缩放 | Stage 178 |
| 跨显示器、混合 DPI、负坐标和真实 Store 重载 | Stage 179 |
| 真实设备/截图证据准入与误标防护 | Stage 180 |

PF-003 的计划生产工程链已完成，剩余项全部属于 Product Evidence：物理鼠标/键盘/触控、可见截图、Narrator/UIA Bounds、动态热插拔人工矩阵。因此状态由 `InProgress` 调整为 `EngineeringComplete / ProductEvidencePending`，但不能标记 `Complete`，30 个顶层 PF 项仍为 `0 Complete`。

## 5. 门禁与下一步

- 本轮 Windows Capture：2/2 复现相同上游签名；
- 实际 SendInput 动作：0；配置提交：0；
- live UIA 安全预检：预期在 App 启动前拒绝该精确组合；
- Release 全量：`1075/1075`，0 skipped；
- Release build：`0 warning / 0 error`；
- 153-ID UI/结构合同：Pass；
- live UIA 安全预检：在 App 启动前以退出码 1 拒绝 2.4.0.0 / 3.2.3.0，符合预期；
- `dotnet format --verify-no-changes`：Pass；
- PF003D5-01～05：继续 `PendingManualEvidence`。

下一开发项转为 PF-001 桌面优先启动收口：普通启动不应强制激活控制中心；DesktopHost 有方格时应保持桌面优先，空工作区应给出可发现但不抢前台的首建入口，控制中心由用户明确打开。PF-003 物理/无障碍矩阵在上游安全环境并行补证，不再反复触发已知崩溃组合。
