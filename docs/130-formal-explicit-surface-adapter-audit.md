# Stage 130：正式 Explicit Surface Adapter 审计

更新日期：2026-08-14

## 1. 目标与需求对齐

本切片执行 Stage 125/129 的 E1/M1，只闭合正式 DesktopHost 表面模式切换：

- `ProductDesktopHostPassiveSurfaceModeAdapter.ApplyExplicit` 可以对产品自有 HWND 执行代际绑定的 Explicit 变更；
- Passive、Explicit、Hidden 都必须由终态复读证明，不能以 API 返回值冒充成功；
- 任一显示器表面失败时，全部表面按逆序补偿到 Hidden；
- 默认关闭、Host/Interaction 双 opt-in 和 `LONGGRID_DISABLE_DESKTOP_INTERACTION=1` 的最高优先级保持不变。

非目标保持为：不接入正式 Windows 输入源，不让 App 消费 Prepared Intent，不执行选择业务动作，不读取文件内容，不写入、移动或删除桌面文件，也不改变外部证据状态。

## 2. 实现审计

### 2.1 正式表面状态

正式 `IProductDesktopHostReadOnlySurface` 新增 Explicit 应用和证明合同。Windows surface 使用单一有限模式作为事实源：

- Passive：窗口可见、命中穿透、UIA 不可聚焦且无 Selection pattern；
- Explicit：窗口可见、`WM_NCHITTEST` 返回 client、根 UIA 暴露空的 Selection provider 并标记可聚焦；
- Hidden：窗口隐藏且区域为空；
- 三态继续保留 ToolWindow、NoActivate、非 Topmost、无 owner、非前台窗口策略。

Explicit UIA provider 目前只声明表面可选择并返回空选择，不接收或消费任何输入；实际键盘、鼠标和辅助技术业务结果属于 E2/M2。

### 2.2 事务与补偿

- `ApplyExplicit` 只接受与当前 window registry generation 相同的 lease；过期代际零变更拒绝。
- 多表面应用会尝试每一项并统一复读；任一返回失败、异常、状态冲突或复读失败，按逆序隐藏全部表面。
- Passive/Hidden 仍复用同一复读路径；非 Hidden 操作失败同样收敛到 Hidden。
- `Restore` 只恢复可重建的 Passive/Hidden 基线；捕获证据本身不含 lease，因而拒绝用 Explicit evidence 无授权重建 Explicit，并立即隐藏。
- 生命周期释放仍先使准备输入失效、隐藏并解绑 adapter，再注销窗口和销毁 HWND；紧急禁用继续取消 admission、隐藏并断开表面。

## 3. 自动化验收

新增/更新的自动化覆盖：

- 正确代际的正式 adapter 执行 Passive → Explicit → Passive；
- 过期代际拒绝且保持 Hidden；
- 双表面部分失败后按逆序补偿并最终全部 Hidden；
- 无 lease 的 Explicit evidence 不可被 Restore 重建；
- 真实产品 HWND 的 UIA Selection/Focusable 状态随 Explicit → Passive 切换，Hidden 终态可复读，且窗口不取得前台；
- 既有 Core surface transaction 继续覆盖 admission 拒绝、重复、取消、验证失败、恢复失败和 emergency hide failure；
- 源码合同继续证明 App、Intent preparation bridge 和 input forwarding adapter 没有接入正式 Explicit 消费或文件操作。

本地验收结果：

- Release solution build：0 warning / 0 error；
- 自动化：881/881 通过；
- 覆盖率：line 91.11%（11730/12874），branch 80.64%（3694/4581）；
- UI 源码合同、14 个启动/人工会话/CI 合同入口和 3 个原生探针通过；
- 原生 B5 探针保持 `Conditional Pass`，因为物理设备、Narrator、触控等仍属外部人工证据。

远端验收：PR #180；首个实现提交 `c3a99fd`；PR CI run 31720374196 成功；squash 合并为 `main@78c853fea3d5d08071a0cab19af18dc4db4f5446`；main CI run 31720966794 成功。

## 4. 安全、隐私与外部证据

- 默认启动仍不创建 DesktopHost；单独 Host opt-in 仍只有只读 Passive 表面。
- Interaction emergency disable 高于 opt-in，不能附着正式 adapter。
- 本切片没有新增环境变量、全局 Hook、Raw Input、`SendInput`、文件 API、遥测或证据采集。
- #19、#20、#23、#24 继续 OPEN；X1～X5、ADR-0001 与所有人工/硬件/真实卷结果继续 Pending。
- E1 自动化只能证明工程链闭合，不能替代物理输入、Narrator、系统表面或多显示器人工证据。

## 5. 结论与下一步

E1/M1 工程验收为 `Pass`，PR 与合并后 main 的完整 CI 均通过。产品仍不可分发，且不得进入内部 RC；这不是任何外部人工/硬件证据的 Pass。

下一切片固定为 E2/M2：在新的独立 PR 中设计并接入正式输入源、Prepared Intent 消费与可访问交互；必须继续保留默认关闭、显式动作、去重/取消和零桌面文件写入边界。
