# Stage 180：PF-003D5 真实输入证据准入纠偏审计

- 审计日期：2026-08-24
- 开发分支：`codex/pf002d-create-preview`
- 起始基线：`b754896`
- 对应目标：让 PF-003D5 真实物理输入矩阵可由正式入口执行，并防止 SendInput/截图被误记为物理证据
- 结论：**PF-003D5 会话准入和证据口径纠偏完成；物理鼠标、键盘、触控、截图及 UIA Bounds 仍为 `PendingManualEvidence`，PF-003 保持 `InProgress`。**

## 1. 开发目标与偏移根因

Stage 179 已在人工手册定义 `PF003D5-01`～`PF003D5-05`，但正式启动器 `Start-DesktopHostProductSessionMatrix.ps1` 的 `ValidateSet` 仍只允许 `A5-01`～`A5-06`。因此手册宣称可执行、入口却会在参数绑定阶段拒绝，属于流程实现偏移。

同时，Stage 153 两处仍把已完成的 PF-003D4 写成“尚未实现/下一步”，README 的建议下一步仍停留在早期 B6C3 探针。若不先修正，后续开发和证据记账会从过期状态领取任务。

## 2. 修正内容

1. 启动器显式接受五个 PF003D5 场景，保留匿名操作者、受控环境和恢复计划确认；
2. 合同增加 `physicalDeviceInputAutomaticallyVerified=false`、`visibleScreenshotAutomaticallyCaptured=false` 和触控/笔仅在设备可用时要求；
3. 启动器继续不发送输入、不改变显示/会话状态、不截屏、不写结果，最终状态固定 `PendingManualEvidence`；
4. UI 合同静态拒绝在启动器引入 `SendInput`、显示设置修改、额外进程控制或自动 Pass；
5. 手册把可见 SendInput 结果限定为 `VisibleSyntheticInputEvidence`，禁止写入物理设备列；
6. Stage 153 和 README 当前顺序更新为 PF-003D5 → PF-001 桌面优先收口 → PF-004～PF-010。

## 3. Expected / Actual / Difference

| 检查 | Expected | 修正前 Actual | 修正后 Actual |
| --- | --- | --- | --- |
| 五个 PF003D5 场景 | 正式入口全部可接受 | `ValidateSet` 只含 A5-01～06，PF003D5 在启动前拒绝 | 五个场景逐一通过参数绑定和 ValidateOnly |
| 真实证据口径 | 自动化不等于物理输入 | 手册有纪律说明，但启动合同没有机器可检字段 | 三个有限字段与静态拒绝合同同时生效 |
| 文档当前阶段 | 下一步为 PF-003D5 | Stage 153 仍指向 D4，README 仍指向 B6C3 | 两处权威入口均指向 D5，D4 状态与 Stage 179 一致 |
| 可见自动输入 | 获取正式 App 截图并继续动作 | 两次在截图观察阶段收到操作者物理 Escape，中止 | 按控制规范停止；零输入动作、零证据升级，仍 Pending |
| 正式 App 回归会话 | 启动前零 LongGrid 进程 | 首轮发现被中止会话遗留 PID，evidence 以退出码 1 安全拒绝 | 只读核对可执行路径精确属于本工作区 Release 后终止；重跑 Pass、`Difference=None` |
| 产品和桌面副作用 | 本切片不改变配置或桌面文件 | 未执行产品动作 | 仅修改仓库合同/文档；没有创建、移动或删除桌面文件 |

这里的修正对象是“测试能否合法执行及如何记账”，不是用合同测试替代真实鼠标。两次 Windows 自动界面控制都在正式 Release App 唯一窗口选定后、截图观察阶段被物理 `Esc` 中止；按工具安全规范不能继续复用坐标或声称完成。

## 4. 验收门禁

- PF003D5-01～05：逐项 `ValidateOnly` 参数绑定与安全合同通过；
- 非法场景：参数绑定有限拒绝；
- 153-ID UI/结构合同：Pass；
- Release 全量：`1075/1075`，0 skipped；
- Release solution build：`0 warning / 0 error`；
- 正式 App/Store 双屏回归：Pass；192→240 DPI，目标显示器重载一致，X/Y 差值 0 DIP，外部 `Difference=None`；
- 真实窗口生命周期：1,037 ms 就绪、连续响应 20 秒、退出码 0，`Difference=None`；
- 153-ID UI/结构合同：Pass；
- NuGet 全项目漏洞检查：无已知易受攻击包；
- `dotnet format --verify-no-changes` 与 `git diff --check`：Pass；
- 真实物理设备证据：未执行，保持 `PendingManualEvidence`；
- live 跨进程 UIA：已知 Windows App Runtime 2.4.0.0 / Microsoft.UI.Xaml 3.2.3.0 组合继续在启动前安全拒绝。

## 5. 需求对齐与下一步

本切片纠正了测试流程和权威计划漂移，没有把验证基础设施折算为新的 PF 完成项。PF-003 仍为 `InProgress`，30 个顶层 PF 项仍为 `0 Complete`。

下一步仍是 PF-003D5 产品证据执行：

1. 在不被 Escape 中止的受控会话中，用正式 Release App 执行可见 SendInput，记录候选前/后截图、Bounds、保存重载与 `Difference`，结果单列为合成输入；
2. 由操作者通过同一入口执行真实物理鼠标跨屏拖动、物理键盘微调和可用设备上的触控/笔；
3. UIA 只在上游安全运行时或独立安全机器执行，不使用危险确认绕过已知崩溃预检；
4. PF-003 证据收口后先复核 PF-001 桌面优先启动，再进入 PF-004 标题栏与就近操作。
