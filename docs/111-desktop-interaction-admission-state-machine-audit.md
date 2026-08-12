# Stage 111：桌面交互准入与模式状态机审计

日期：2026-08-12
阶段：B1（工程边界完成，尚未接入真实桌面输入）

## 1. 本阶段目标

Stage 110 只证明正式 DesktopHost 能以被动、只读、不可聚焦的方式显示方格。B1 的目标不是立刻让窗口可点击，而是在任何原生输入接线之前建立可独立验证的准入边界：

- 桌面交互必须拥有区别于 `LONGGRID_ENABLE_DESKTOP_HOST` 的独立开关；
- 默认模式保持 `Passive`，不得因为 DesktopHost 可见而自动进入交互；
- 每次显式交互只获得短时、单目标、generation 绑定的 lease；
- 宿主、辅助功能、被动窗口、workspace、显示拓扑、窗口注册表或目标状态变化时立即回到 `Passive`；
- 本切片不得连接 `App`、HWND 输入控制器、真实文件、拖放、任务栏或插件入口。

## 2. 需求对齐结论

| 最初需求/竞品体验 | B1 对齐结果 | 当前边界 |
|---|---|---|
| 桌面方格可直接操作 | 已建立 `Passive → ExplicitInteraction → Passive` 的安全状态骨架 | 尚无真实点击、框选、拖动、缩放 |
| 不打断正常桌面使用 | 默认关闭；即使只读宿主已启用，也必须再次精确 opt-in | 不改变 HWND 样式，不抢焦点 |
| 动态显示器与 Explorer 变化可恢复 | lease 同时绑定 workspace、topology、window registry generation | B2 才接入真实事件源 |
| 锁定方格不可误操作 | 目标锁定时拒绝准入；交互过程中被锁定则取消 | 只验证策略，不修改配置 |
| 现代化平滑交互 | 状态边界为后续 hover、命中、焦点与取消动画提供确定语义 | 本阶段没有视觉变化 |
| 桌面文件整理 | 不读取内容，不移动、不复制、不删除任何文件 | 仍维持只读引用模型 |
| 任务栏美化/自定义窗口/Widget 插件 | 未扩大权限，也未进入当前 MVP 交互路径 | 继续按独立后续阶段实施 |

## 3. 实现审计

### 3.1 双重默认关闭策略

`ProductDesktopInteractionFeaturePolicy` 只在以下两个条件同时成立时返回开发启用：

1. DesktopHost 已由精确值 `LONGGRID_ENABLE_DESKTOP_HOST=1` 启用；
2. 桌面交互再由精确值 `LONGGRID_ENABLE_DESKTOP_INTERACTION=1` 启用。

`true`、带空格的 `1`、空值及其他值全部关闭。第二个开关不是用户许可，也不是发布默认值；当前 `App` 刻意不读取它。

### 3.2 有限显式意图

准入意图包含：

- 非空随机 `IntentId`；
- 单个目标方格 ID；
- workspace revision；
- display topology generation；
- product-owned window registry generation；
- 签发与到期 UTC 时间。

意图最长有效期为 5 秒，未来签发、空目标、非正 generation、倒置时间或过长寿命均作为无效意图拒绝。活跃 lease 不允许被另一个意图静默替换。

### 3.3 准入证据

进入显式交互前必须同时证明：

- 原生 DesktopHost 仍连接且生命周期为只读就绪；
- read-only UIA 已证明；
- passive window contract 已证明；
- 三个 revision/generation 与意图完全一致；
- 目标仍存在且未锁定；
- 可用/锁定目标集合有效。

缺失、空适配集合或陈旧证据全部 fail closed，不生成 lease。

### 3.4 持续复核与取消

活跃 lease 每次复核都会检查到期、宿主连接、两项证明、三项 generation 和目标状态。任何变化都清除 lease 并回到 `Passive`，同时保留明确取消原因。显式 `Cancel()` 同样有限收敛，不产生 revision 或写任务。

### 3.5 接线隔离

工程 UI 合同新增静态门禁，证明：

- 独立精确 opt-in、有限寿命和全部 generation/attestation 字段存在；
- `LongGrid.App` 没有实例化状态机，也没有读取交互开关；
- 原生 `EnableWindow` 控制器没有引用准入状态机。

因此本阶段不会把 Stage 110 的不可点击正式 HWND 提前变成输入窗口。

## 4. 测试与证据

新增确定性单元测试覆盖：

- 两层开关及所有非精确值；
- 成功准入与完整 lease 绑定；
- 宿主未就绪、证明缺失、三类陈旧 generation、目标消失/锁定；
- future、expired、overlong 和空证据；
- 活跃 lease 不可替换；
- 到期或任一证据变化自动取消；
- 显式取消和禁用控制器零 lease。

合并前必须执行锁定恢复、格式、Release build/test、覆盖率、UI 合同和仓库既有安全/启动/会话门禁；PR 与 main CI 均需绿灯。真实会话交互仍为未生成证据，不能用这些纯策略测试代替。

## 5. 风险与未完成项

- 当前状态机没有时钟/事件订阅器；调用方未来必须在每次输入前复核，并在拓扑、workspace、registry、锁定、会话和焦点变化时主动复核。
- 当前锁定目标一律拒绝显式交互。若以后允许“锁定但只读查看”，必须增加能力分级，不能复用可编辑 lease。
- lease 不是授权令牌，不得持久化、跨进程传递或作为文件系统权限依据。
- 还没有真实 hit-test、pointer capture、keyboard focus、Esc、Win+D、全屏、锁屏/RDP 或 Explorer 重启证据。
- A5 人工矩阵仍是 `PendingManualEvidence`，Stage A 尚未最终人工验收。

## 6. 下一切片

下一步为 **B2：产品命中区域与焦点/取消适配器**：

1. 复用 `ProductDesktopHostSurfaceLayout` 计算只读命中目标，不复制几何算法；
2. 把 pointer/keyboard 意图转换成最长 5 秒的 B1 意图；
3. 在进入任何输入处理前复核 lifecycle 与三个 generation；
4. 定义 Esc、失焦、Win+D、显示器变化、Explorer 变化和 shutdown 的统一取消语义；
5. 先以隔离适配器和自动化 probe 验证，仍不开放真实文件移动/复制/删除。

B2 通过后再进入 B3（选择模型与 UIA Selection 语义），之后才是方格拖动/缩放和安全引用拖放；真实文件变更继续保持关闭。
