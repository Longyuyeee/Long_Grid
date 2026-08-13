# Stage 119：产品 Intent 准备与人工会话门禁审计

日期：2026-08-13
阶段：B6c2（只准备 Intent；正式输入、Explicit 与真实文件操作仍关闭）

## 1. 目标与结论

Stage 118 已让系统表面变化可靠驱动 Hidden/Passive，但正式产品仍没有从明确用户动作形成 B1 Intent 的受控桥。B6c2 的目标是把已有命中与 Intent Factory 接到产品投影和 registry 证据上，同时保证生成的 Intent 不被 App、Admission 或 Surface 消费。

本阶段结论为 **Conditional Pass**：第三重精确门禁、人工会话确认、单调用户动作、唯一命中、锁定拒绝、5 秒 Intent、四类失效与关闭收口由自动化覆盖；正式 HWND 继续穿透，App 没有输入转送入口，因此真实鼠标、键盘、触控、笔和 Narrator 激活仍为 PendingManualEvidence，不能宣称 Explicit 可用。

## 2. 三层开发门禁与人工会话确认

Intent 准备要求以下条件同时成立：

1. `LONGGRID_ENABLE_DESKTOP_HOST=1`；
2. `LONGGRID_ENABLE_DESKTOP_INTERACTION=1`；
3. emergency-disable 未精确等于 `1`；
4. `LONGGRID_ENABLE_DESKTOP_INTENT_BRIDGE=1`；
5. `LONGGRID_ACKNOWLEDGE_DESKTOP_INTENT_SESSION=1`。

所有值都按 `StringComparison.Ordinal` 精确判断；空值、`true`、大小写变化和空格都不能启用。后两个值只代表受控开发会话的范围确认，不是最终用户许可，不得持久化到产品配置，也不得由普通启动入口自动设置。

专用启动器 `Start-DesktopInteractionIntentSession.ps1` 还要求匿名操作员标签、受控环境、无 Explicit 边界和恢复计划三项显式确认。启动器临时设置四个 enable/acknowledge 值，退出后逐项恢复原进程环境；emergency-disable 已启用时拒绝启动。

## 3. 用户动作合同

`ProductDesktopInteractionIntentPreparationRequest` 必须同时满足：

- 非空随机 `UserActionId`；
- 正数且严格递增的进程内 `UserActionSequence`；
- UTC 观察时间不晚于当前时间，且动作年龄不超过 1 秒；
- `ExplicitUserActionConfirmed=true`；
- 激活类型只能是既有 Pointer/Keyboard/AssistiveTechnology 三种有限枚举；
- 显示器 ID 非空，客户端坐标非负。

缺少逐动作确认会撤销已有准备；迟到或重复动作不能生成新 Intent。重复旧序号不会替换当前准备；更新且有效的动作会先递增 bridge generation，使旧准备失效。

## 4. 唯一命中与证据

生命周期只在 `ReadyReadOnly`、产品 Passive adapter 已连接且交互控制器完整复核时调用 bridge。Bridge 使用当前 `ProductDesktopHostProjectionBatch`：

1. 精确选择唯一 `DisplayId`；
2. 复用正式 `ProductDesktopInteractionHitTestAdapter`；
3. 空白、边界外和重叠歧义全部拒绝；
4. 目标必须存在于当前 available container 集合；
5. 产品投影新增只读 `IsLocked`，锁定方格拒绝 Intent；
6. workspace revision、topology generation 和 window-registry generation 必须与当前证据一致；
7. Host ReadyReadOnly、只读 UIA 和 Passive window contract 必须仍成立。

命中成功后复用 B1 `ProductDesktopInteractionIntentFactory`，Intent 从用户动作观察时间起最多 5 秒，并绑定唯一目标和三类 generation。

## 5. 只准备、不消费

准备结果包装为 `ProductDesktopInteractionPreparedIntent`，额外绑定 bridge generation 与用户动作序号。快照固定：

- `ExplicitInteractionEntered=false`；
- `RealFileOperationsAllowed=false`；
- 不调用 `ProductDesktopInteractionAdmissionController.TryEnterExplicitInteraction`；
- 不构造 `ProductDesktopInteractionSurfaceModeTransaction`；
- 不调用 adapter `ApplyExplicit`；
- 不创建 Selection、焦点、拖放或文件操作对象。

App 只构造 policy 与 bridge 并交给 DesktopHost 生命周期；App/MainWindow 没有 `PrepareInteractionIntent` 调用，也没有引用 hit-test 或 Intent Factory。因此普通 UI、原生 HWND 与辅助技术都不能在本阶段触发准备。

## 6. 失效与关闭

以下变化都会使准备失效并递增 bridge generation：

- 新的有效用户动作；
- Stage 118 任一系统表面危险信号；
- Surface 释放、替换、故障或 topology/workspace 更新；
- Host/UIA/Passive 证明或三类 generation 漂移；
- Intent 到达 5 秒边界；
- shutdown/dispose。

生命周期未 Ready 时只返回 `AwaitingPassiveSurface`，不会伪造命中或 Intent。`Complete` 幂等且终态，之后不能再次准备。

## 7. 自动化与静态合同

测试覆盖：

- 精确桥接值和人工会话值的全组合；
- 上游 Interaction 关闭拥有优先级；
- 新鲜、确认、唯一、未锁定命中准备成功；
- 准备结果绑定目标、三类 generation 和 5 秒期限；
- 缺少逐动作确认、迟到、重放、无显示器、空白命中和锁定目标拒绝；
- 新动作、系统事件、代次漂移、超时、Surface 未 Ready 与关闭失效；
- 产品投影保留锁定状态；
- 生命周期 Passive → Prepared → Hidden/Invalidated → Passive → Prepared；
- 专用启动器 ValidateOnly 保持 `PendingManualEvidence`、零输入合成、零 Hook、零文件操作和零证据写入；
- 142-ID 静态合同确认 App 不调用准备/Factory/HitTest/Admission，正式 HWND 仍 `HTTRANSPARENT`，产品 adapter 仍拒绝 Explicit。

Release 构建为 0 warning / 0 error；857/857 自动化通过；Cobertura 行覆盖率 91.02%（22518/24740），分支覆盖率 80.48%（7142/8874），高于 90%/75% 门槛。RC 哈希与 PR/main CI 结果在提交收口时复核。

## 8. 需求对齐与下一切片

本阶段为桌面分组的真实交互建立了可撤销、可审计的最小意图边界，也保持性能稳定：无轮询增加、无窗口枚举、无新常驻线程，准备只对当前内存投影做有界查找。

未完成项保持明确：

- 正式 HWND 仍不接收鼠标、键盘、触控、笔或 UIA 激活；
- 没有 Explicit Surface、焦点/Selection 或拖放；
- 没有桌面文件移动、复制、删除、重命名或内容读取；
- 没有任务栏美化、小组件或 Long助手插件运行时权限。

下一切片 B6c3 应先建立隔离的产品输入转送适配器与“一次动作一次准备”人工证据，仍让产品 adapter 拒绝 Explicit。只有真实键鼠/触控/Narrator、Win+D、全屏、RDP、Explorer 与紧急退出矩阵通过后，才可单独评审正式 Explicit Surface；真实文件操作继续后置。
