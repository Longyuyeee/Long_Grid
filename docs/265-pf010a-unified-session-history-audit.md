# Stage 265：PF-010A 统一会话历史与 50 步撤销/重做审计

日期：2026-09-01

输入基线：`origin/main@3ccaca8`（PF-009B / PR #348 已全绿合入）

状态：`PF010A EngineeringComplete / RealStorePass / ProductEvidencePending`

## 1. 本阶段交付结论

PF-010A 已把创建、重命名、锁定/解锁、折叠/展开和外观调整接入统一会话历史。历史以 cursor 表示当前已应用位置，最多保留 50 个成功动作；撤销后发生新动作会截断 redo 分支，失败和 NoChange 不写入成功历史。每项记录包含动作、方格目标、数量、时间及已应用/已撤销/可撤销/可重做状态。

控制中心“桌面概览”增加正式操作历史列表、撤销和重做按钮。Ctrl+Z / Ctrl+Y 只绑定正式历史，旧匿名练习不再占用 Ctrl+Z。按钮和 Narrator 文案始终说明只修改 Long方格配置，不删除或移动真实文件。

历史导航在执行前比较当前配置指纹；revision 或历史之外的状态变化会给出有限失效原因，不执行模糊补偿。undo/redo 提交保存失败时，App 使用绑定到 operation、cursor、配置指纹、edit revision 和 save revision 的 token 恢复导航前状态并重新进入保存队列。

## 2. Expected、Initial Actual、Difference、Correction、Final Actual

| 检查 | Expected | Initial Actual | Difference | Correction | Final Actual |
|---|---|---|---|---|---|
| 连续编辑 | 重命名后再折叠，两个动作都保留 | 精确 xUnit 失败：Expected 为 `Rename / revision 2` token，Actual 为 `Collapsed / revision 3` token | 协调器只有一个 `pendingContainerEditUndo`，新动作覆盖旧动作 | 引入统一 history entries、cursor 和前后状态/指纹 | 两步均保留，可按顺序连续撤销与重做 |
| redo | undo 后可恢复刚撤销动作 | 原实现没有 redo 模型或入口 | 单次 token 只能消费一次 | history cursor 向前/向后导航，UI 提供 Ctrl+Y | apply→undo→redo→undo 最终状态精确一致 |
| 50 步上限 | 保留最近 50 个成功动作 | 没有多步历史 | 无容量与淘汰规则 | 第 51 步只淘汰最旧条目 | `Name-2`～`Name-51` 共 50 项，cursor=50 |
| redo 分支 | undo 后的新动作删除旧 redo 尾部 | 没有分支语义 | 无 cursor | 新动作前 `RemoveRange(cursor, tail)` | 折叠 redo 被新外观动作替换，CanRedo=False |
| 外部变化 | 明确失效，不补偿未知状态 | 旧 token 仅报告一次 gate 失败 | 无历史级原因 | 比较当前配置与 cursor 期望指纹 | UI 显示“当前配置已在历史之外发生变化” |
| 保存失败 | undo/redo 写入失败时回到导航前状态 | 原实现没有 redo 保存失败恢复链 | 导航状态可能领先持久化结果 | 失败 revision 触发 token 绑定补偿 | 第 3 次保存注入 IO 失败，补偿恢复折叠前状态，第 4 次保存成功 |
| 真实文件 | 历史导航只改配置 | 多步旅程此前不存在 | 需证明不会触碰引用文件 | 真实 Store + Unicode 引用文件执行完整旅程并比较 SHA-256 | 配置最终重载为 `Before`，文件内容与哈希不变 |

## 3. 真实测试结果

- Initial Actual：现有正式提交链连续执行“重命名→折叠”，xUnit 明确失败；Expected 是 Rename token，Actual 是后写入的 Collapsed token。
- 首批五类动作分别完成 apply→undo→redo→undo，最终配置 fingerprint 与操作前完全一致。
- 51 次真实协调器提交后只保留最近 50 项，最旧成功动作按固定策略淘汰。
- undo 后提交外观动作，旧折叠 redo 分支被截断；NoChange 与 reducer 拒绝均不写成功历史。
- 保存失败测试让第 3 次真实保存工作流调用返回 `IoFailure`；补偿恢复 undo 前折叠状态，第 4 次保存成功。
- 真实配置 Store：对包含 `用户资料.txt` 引用的配置执行 apply→undo→redo→undo，完成后重新加载 Store；文件内容和 SHA-256 不变。
- PF-010A 专项：`12/12`；协调器、旧最近撤销及历史相关回归：`41/41`。
- 完整 Debug / Release：`1,438/1,438`，0 failed，0 skipped。
- Release 全解决方案：`0 warning / 0 error`。
- UI 工程合同：`211` 个唯一 AutomationId；正式历史列表、有限默认状态、Ctrl+Z/Ctrl+Y、50 步、分支截断、保存补偿和文件零修改声明通过。
- 正式跨进程 UIA：真实入口在 App 启动前失败关闭；本机仍缺 `MicrosoftCorporationII.WinAppRuntime.Main.2 >= 2.3.1.0` 与 `Microsoft.WinAppRuntime.DDLM.2.3.1.0-x6`。没有把源码合同冒充物理 App 证据。

## 4. 开发目标与需求对齐审计

开发目标审计：PF-010A 的统一 history item、cursor、50 步容量、undo/redo、分支截断、首批五类动作、正式 UI、键盘入口、失效原因、真实 Store 和保存失败补偿均已形成闭环。旧单步 token 仍保留供尚未迁移动作使用，不在本阶段一次性重写所有 reducer。

需求对齐审计：本阶段直接增强“放心整理”的核心用户旅程，未扩张权限或安全邻接工作。成功历史只记录配置动作；删除、移动和修改真实文件不属于历史执行面。批量引用等尚未迁移动作没有被虚假标为统一历史完成。

完成度审计：PF-010A 达到 `EngineeringComplete / RealStorePass / ProductEvidencePending`。PF-010 整体仍未 Complete，因为删除、布局、文件夹绑定、引用批量动作、规则应用、重启后安全恢复点及物理键盘/Narrator 证据尚未全部接入。M1/M2 仍为 `0/2 Complete`，30 项 PF 仍为 `0 Complete`，产物不可公开分发。

## 5. 唯一接续开发点

下一步只进入 **PF-010B：统一历史动作广度与重启恢复点**：

1. 把删除、布局、文件夹绑定/解绑接入统一历史；
2. 把引用加入、移除、改归属和自定义顺序按批量原子单元接入；
3. 把已有 LatestUndo 独立入口逐步收敛到同一历史列表，避免两套用户语义；
4. 为 App 重启后最近一次安全恢复点定义最小持久化范围，不持久化含真实解析路径的运行时状态；
5. 每个动作继续执行 apply→undo→redo→undo，并覆盖目录外部变化与保存失败；
6. 环境满足时补正式 App 物理 Ctrl+Z/Ctrl+Y、列表焦点和 Narrator 证据；环境不满足则保持 Pending；
7. 结束时继续完成目标审计、需求对齐、文档、推送和 CI 收口。

PF-010B 收口前不并行展开 PF-011 或新的安全邻接探针。
