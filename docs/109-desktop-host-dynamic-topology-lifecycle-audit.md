# Stage 109：DesktopHost 动态拓扑生命周期加固审计

日期：2026-08-12

范围：阶段 A 的 A4 自动化子切片。基线为 Stage 108 每显示器只读批次；本切片不扩大输入、文件、Explorer、任务栏或插件权限。

结论：**动态投影边界已从含义不明的可空批次升级为有类型、代次有序、失败关闭的更新状态机。** 拓扑刷新或失去权威性时立即销毁所有产品宿主表面；迟到的 workspace revision 或 topology generation 被忽略；同一代次的重复证据幂等，同一终态代次的冲突证据整批关闭；`Refreshing` 可以在同一拓扑代次合法晋升到最终 `Ready`。默认启动仍为零 DesktopHost HWND。

## 1. 审计发现与决策

Stage 108 的 `Build(...) -> batch?` 把以下状态都压缩成 `null`：

- 工作区为空；
- 拓扑正在刷新；
- 拓扑降级、失败、取消或不可用；
- 输入快照不一致或超过显示器预算。

生命周期控制器因此无法记录原因，也不能判断迟到结果。A4 引入 `ProductDesktopHostProjectionUpdate`，显式区分：

- `Ready`：携带元数据完全一致的有限批次；
- `EmptyWorkspace`：权威拓扑下没有正式方格；
- `TopologyRefreshing`：新拓扑代次正在采集；
- `TopologyUnavailable`：当前结果不可作为坐标依据；
- `Invalid`：快照、主显示器或预算合同不成立。

安全策略选择“拓扑不权威即隐藏”，不保留上一代 HWND。虽然刷新期间可能短暂隐藏，但不会让旧坐标窗口覆盖新显示布局、任务栏区域或已断开的显示器。无闪烁保留只有在未来能证明旧 WorkArea 仍有效时才可另行开放。

## 2. Latest-wins 与补偿状态机

生命周期新增 `AwaitingWorkspace` 与 `SuspendedUnsafeTopology`，并记录最近接收的 workspace revision、topology generation 和完整更新：

1. 任一维度小于已接收值：迟到更新，保持当前表面和快照不变；
2. 两个代次和内容均相同：幂等，不重建 HWND；
3. 同一拓扑代次由 `TopologyRefreshing` 晋升到 `Ready`/不可用终态：允许收敛；
4. 同一终态代次出现不同批次或处置：证据冲突，销毁全部表面并发布 `Faulted`；
5. 更新更晚且为 `Ready`：先排空旧注册和表面，再按 A3 全有或全无规则重建；
6. 更新更晚但为空、刷新、不可用或无效：零 HWND，并发布可诊断的有限状态；
7. 关闭：逐显示器注销、释放所有表面、断开 bridge，重复关闭保持幂等。

WinUI composition root 已改用 `BuildUpdate`/`ApplyProjectionUpdate`。控制中心会区分“等待桌面方格”和“宿主已安全隐藏”，不再把所有未连接状态解释为尚未创建宿主。

## 3. 自动化证据

- Release build：0 warning / 0 error；
- 定向 DesktopHost 测试：25/25；
- Release 全量测试：671/671；
- 覆盖率：行 91.23%，分支 81.41%，通过 90%/75% 门禁；
- 100 次连续 workspace revision：100 个模拟表面中前 99 个均释放，仅最终 revision 保持活动，关闭后最后一个也释放；
- 拓扑 `Refreshing -> Ready` 同代次恢复、迟到 topology 拒绝、同终态冲突闭合均有确定性测试；
- 142-ID UI 合同、干净会话、单实例、CI hang/restore 合同通过；
- 原生 DesktopHost 交互探针为 Conditional Pass：未抢前台，USER 46→48→46，GDI 80→80→80，进程 handle 628→628→628，清理通过；
- 配置 100 次持久化、文件操作安全、缩略图隔离与依赖漏洞门禁保持既定 Pass/Conditional Pass。

## 4. 需求对齐与权限差异

本切片直接支撑“桌面分组在显示器变化和快速编辑时稳定收敛”的 MVP 要求，并学习 iTop/Fences 的常驻桌面体验，但没有通过猜测 Explorer 内部窗口来换取视觉连续性。

权限变化为零：

- 不读取文件内容，不写入、移动、重命名或删除真实桌面文件；
- 不接收点击、键盘、触摸或拖放；
- 不使用 `Progman`、`WorkerW` 或 Explorer 注入；
- 不启用任务栏美化、Widget Host 或 Long 助手插件；
- `LONGGRID_ENABLE_DESKTOP_HOST` 仍必须精确为 `1`，且只属于开发审计开关。

## 5. 尚未完成与下一步

自动化 A4 已完成，但以下真实环境证据不能由模拟测试替代：显示器物理拔插、旋转、100%/150%/200% DPI、不同任务栏位置、锁屏、睡眠、RDP、会话切换、Explorer 重启及 24 小时 USER/GDI/handle 趋势。这些继续记录为 #20/#24 人工矩阵，不得写成已通过。

下一开发切片进入 **A5：DesktopHost UIA 与真实会话产品矩阵**：建立产品表面的 UIA Fragment/只读语义、Win+D/显示桌面/全屏/Z-order 行为证据，并准备阶段 B 的显式输入准入；在 A5 验收前仍不开放桌面直接编辑或真实文件操作。

后续结果：Stage 110 已完成 A5 自动化 UIA、被动窗口复读和受控产品会话合同；A5-01..A5-06 的最终结果仍保留为人工证据。详见[Stage 110 审计](110-desktop-host-readonly-uia-session-contract-audit.md)。
