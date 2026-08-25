# Stage 164：PF-002E 创建保存与可见发布补偿审计

- 日期：2026-08-20
- 分支：`codex/pf002d-create-preview`
- 目标：保存失败时撤回同一次桌面创建的配置投影、DesktopHost 窗口和 UIA 可见结果，禁止幽灵方格
- 结论：**PF-002E Engineering Complete；PF-002D 实机证据与 PF-002 总验收继续 Pending**

## 1. 开发前审计与目标差异

旧链在 Preview 确认后依次执行：创建 reducer、提交异步保存、立即替换 App read model、立即刷新 DesktopHost。保存控制器失败时只显示“更改尚未保存”，不会撤回新容器。因此用户在当前运行中可看到一个未落盘方格，重启后它又消失。

| 检查项 | 预期 | 开发前实际 | 差异 |
| --- | --- | --- | --- |
| 保存成功 | 配置与可见方格一致 | 一致 | 无 |
| 同次保存失败 | 新方格撤回，明确说明 | 新方格仍在，只显示通用失败 | P0 幽灵状态 |
| 失败后已有新编辑 | 不得用旧快照覆盖 | 没有补偿策略 | 缺少并发边界 |
| 重试 | 保存权威安全状态 | 重试保留的创建快照 | 可能把已撤回意图重新发布 |

## 2. 实现

新增 `ProductDesktopWorkspaceCreatePublicationToken`，精确记录：

- 创建容器 ID；
- 创建成功后的工作区 edit revision；
- 本次异步保存 revision。

保存快照只产生四种有限判断：`AwaitingSave`、`Published`、`RollbackRequired`、`Superseded`。只有容器仍存在、工作区修订相同、保存修订相同且状态为 `Failed` 时允许补偿。补偿清除发布令牌后，复用既有 `CommitContainer(Remove)` 和唯一保存控制器；App read model、DesktopHost 投影和 UIA 因而沿同一正式路径同步撤回。修订或事实不匹配时返回 `Superseded`，不会覆盖后续编辑。

控制中心显示有限机器状态 `WorkspaceCreateRolledBack:<failure>:Revision=<n>:Motion=Static`，用户文案为“新方格未保存，已撤回”。补偿保存若继续失败，仍进入现有有限重试；解除故障后重试的是撤回后的空/旧状态，而不是失败的创建快照。

## 3. 真实失败注入：预期与实际

专项测试没有伪造 workflow 返回值，而是在系统临时目录创建名为 `store` 的真实文件，再把同一路径交给 `ProductConfigurationStore` 作为目录。Windows 的真实 `Directory.CreateDirectory`/文件系统调用产生 `IoFailure`。

| 时点 | 预期 | 实际 | 判定 |
| --- | --- | --- | --- |
| 创建提交后、保存失败 | 复现旧问题，内存有 1 个新容器 | 1 个；`IoFailure` | Reproduced |
| 发布策略评估 | 仅匹配同次失败要求回滚 | `RollbackRequired` | Pass |
| 补偿提交后 | 内存/投影为 0 个容器 | 0 个 | Pass |
| 故障仍存在 | 补偿保存失败且可有限重试 | revision 2 为 `Failed` | Pass |
| 删除阻塞文件并重试 | 撤回状态写入真实配置 | `Saved` | Pass |
| 从真实 store 重载 | 0 个容器 | 0 个 | Pass |

测试只使用唯一 GUID 临时沙箱，未读取、移动、修改或删除用户桌面文件；结束后精确删除该沙箱。

## 4. 门禁结果

- 专项真实故障测试：5/5 Pass；
- Release 全量测试：994/994 Pass；
- Release 全解决方案构建：0 warning / 0 error；
- `eng/Test-LongGridUi.ps1 -ContractOnly -NoBuild`：Pass，146 个必需 AutomationId；
- `git diff --check`：Pass；
- 正式 App 物理输入：本切片未伪造通过。Stage 163 的外部激活 `0x8001010e` 仍 Pending。

## 5. 需求对齐与边界

- 已关闭 PF-002E 的“保存失败不留幽灵方格”工程缺口；
- 补偿不建立第二套存储或桌面宿主通路，也不新增桌面文件权限；
- 后续编辑优先于旧失败补偿，避免数据回退；
- 当前实现是失败后撤回，而不是等待磁盘成功后才首次显示，保留即时创建反馈；
- 通用控制中心直接创建不属于 PF-002 的 DesktopHost Preview 发布令牌，本切片只关闭桌面直接创建链；
- PF-002D 的真实预览交互、拖画矩形、已选引用创建、触控/无障碍/DPI 证据仍未关闭，因此 PF-002 不得标记 Complete。

## 6. 下一步

1. 在无并发用户输入的 Windows 会话补跑 PF-002D 打开—编辑—取消—确认矩阵；
2. 开发桌面 pointer down/move/up 拖画矩形，仅更新内存预览，抬起后复用现有 Preview/提交/补偿链；
3. 增加“使用 Long方格已选引用创建”，保持配置引用语义且不擅自移动真实文件；
4. 执行 PF-002F 鼠标、键盘、触控、UIA、Narrator、高对比、文本缩放和多 DPI 正式证据。
