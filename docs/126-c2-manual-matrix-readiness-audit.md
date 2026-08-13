# Stage 126：C2 输入、系统表面与无障碍实机矩阵就绪审计

- 日期：2026-08-13
- 审计基线：`main@1d6e817`（PR #175；main CI 31709194139 通过）
- 阶段：Stage 125 / C2 工程准备
- 结论：**Ready to execute / PendingManualEvidence / 不得关闭 Issue #19**

## 1. 需求对齐

C2 只复核已经冻结的真实输入、无障碍、系统表面、显示拓扑和清理恢复边界，不增加新的交互能力。目标是回答以下问题：

- probe 自有来源能否把物理指针、键盘和 UIA 动作归一化为一次 Prepared Intent，而不进入 Explicit；
- 失焦、Win+D、全屏、会话/RDP、Explorer 与显示拓扑变化能否立即失效旧 Intent，并在稳定后只回到 Passive；
- 正式只读 DesktopHost 能否在 Narrator、Win+D、全屏、Explorer、会话与关闭路径中保持不抢焦点、无孤儿表面；
- 正式 App 批量选择在键盘、Narrator、高对比、200% 文本和紧凑布局下是否可理解、可操作、可恢复；
- Issue #19 的键鼠、触控/笔、拖放与系统表面场景能否获得真实人工结果。

本阶段不接入正式输入源、不执行 `ApplyExplicit`、不修改桌面文件、不扩展 UI/Provider，也不把 probe 结论描述成正式产品能力。

## 2. 入口与证据等级

| 会话族 | 场景 | 被测边界 | 证据等级 |
| --- | --- | --- | --- |
| 隔离输入转发 | B6C3-01～04、08 | probe 自有来源 → Prepared Intent | 人工 probe 证据 |
| 系统表面/拓扑 | B6C3-05～07 | Prepared 失效、隐藏和稳定恢复 | 人工 probe 证据 |
| DesktopHost A5 | A5-01～06 | 正式 App 的只读 Passive 表面 | 正式产品人工证据 |
| BSA | BSA-01～05 | 正式 App 批量选择无障碍 | 正式产品人工证据 |
| Issue #19 | I19-01～10 | Phase 0 输入与系统表面出口 | 汇总门禁 |

证据不能自动重复计算。只有场景动作、commit、环境、首次结果和恢复判定完全一致时，I19 汇总才可引用 B6C3/A5/BSA 的对应记录；否则必须单独执行或记为 `Inconclusive`。B6C3 的 Pass 不能证明正式 App 输入消费，A5 的 Pass 也不能证明 Explicit 交互。

## 3. 本轮自动预检

在 `main@1d6e817` 依次执行以下入口的 `-ValidateOnly`：

- `Start-Issue19ManualMatrixSession.ps1`；
- `Start-DesktopInteractionInputForwardingSession.ps1`；
- `Start-DesktopInteractionSystemSurfaceSession.ps1`；
- `Start-DesktopHostProductSessionMatrix.ps1`；
- `Start-LongGridBatchAccessibilitySession.ps1`。

五项均成功，并保持以下安全合同：

- 最终状态为 `PendingManualEvidence`；
- 不发送合成输入、不主动改变系统/显示状态、不进入 Explicit；
- 不修改桌面文件、不截图、不写结果文件；
- 操作员只允许 O1～O9 匿名标签；
- launcher 不能替代人工视觉、Narrator 听读、物理输入和恢复确认。

## 4. 审计发现与修正

BSA launcher 和当前权威 UI 合同要求 142 个 AutomationId，实际预检也输出 `requiredAutomationIds: 142`；旧 BSA 手册仍写 140，会导致操作员把正确预检误判为失败。本切片只把手册修正为 142，不改变 UI、launcher 或测试范围。

未发现需要在人工矩阵前继续开发的交互缺口。Stage 124 的停止规则继续生效：先执行真人矩阵，只有真实失败才能准入最小修复。

## 5. 固定执行顺序

1. 在 Windows 11 x64 专用测试账户准备匿名数据、恢复通道，并记录唯一 commit；
2. 执行 B6C3-01、02、03、04、08，每次全新 probe 进程；
3. 执行 B6C3-05、06、07，每次只制造一个受控系统/拓扑变化；
4. 执行 A5-01～06，明确记录这是正式 App Passive 表面；
5. 执行 BSA-01～05，禁止点击会保存配置的批量动作；
6. 按 I19-01～10 汇总或补跑未覆盖场景，尤其是触控/笔和沙箱拖放；
7. 逐项复核匿名性、首次失败、恢复确认、缺陷链接和证据等级；
8. 只有全部阻断场景已处理后，才进入 C3 的正式 DesktopHost 被动表面收口。

同一启动不得填写多个结果。发生代码变更后，旧 commit 的受影响记录不得与新 commit 拼成完整矩阵。

## 6. 环境准入与停止条件

执行者必须能确认：专用测试账户无个人内容；匿名桌面/工作区已准备；Narrator、主题、文本缩放、Explorer、显示和会话均有恢复方案；没有需要保留的 LongGrid 进程。

出现以下任一情况立即停止当前场景并记录 `Fail` 或 `Inconclusive`：

- 发现个人路径、文件名、窗口标题或设备身份将进入证据；
- 桌面文件发生非预期改变；
- 焦点、透明输入层、窗口、Hook、DComp/UIA 资源或进程无法恢复；
- 显示、Narrator、主题、文本缩放、Explorer 或会话无法恢复；
- 环境不具备触控/笔、多显示器、RDP 或对应人工判断能力。

## 7. 验收目标与当前判定

| 验收项 | 当前状态 |
| --- | --- |
| 五个入口及安全合同可复读 | Pass |
| BSA 手册与 142-ID 权威合同一致 | 本切片修正，等待 PR/main CI |
| B6C3-01～08 真实结果 | PendingManualEvidence |
| A5-01～06 真实结果 | PendingManualEvidence |
| BSA-01～05 真实结果 | PendingManualEvidence |
| I19-01～10 真实结果 | PendingManualEvidence |
| Issue #19 关闭 | Not eligible |

本轮结论只能写 **Conditional Pass（工程就绪）**。下一动作不是继续加代码，而是在满足环境准入后按第 5 节执行人工矩阵。
