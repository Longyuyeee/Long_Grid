# Stage 184：PF-004B 桌面标题栏直接折叠/锁定命令审计

- 日期：2026-08-24
- 分支：`codex/pf002d-create-preview`
- 起始基线：`d74aaa0`
- 对齐编号：`PF-004B`
- 结论：`EngineeringComplete`；PF-004 顶层仍为 `InProgress`

## 1. 目标与冻结边界

本阶段把 Stage 183 已显示的折叠/锁定状态变成桌面就近可操作能力，但不提前实现更多菜单、删除或统一撤销：

- 每个正式桌面方格标题右侧保留“进入交互”，新增“折叠/展开”和“锁定/解锁”，三者均为有限 32 DIP 目标；
- 指针与 UIA Invoke 进入同一产品自有 activation HWND，不监听全局输入、不向 Explorer 注入消息；
- 命令必须携带 container、display、workspace revision、topology generation、来源证明、注入和自动重复事实；
- 陈旧代次、错误显示器、未证明来源、注入、自动重复、只读状态和并发未发布命令均失败关闭；
- 锁定方格拒绝折叠和布局，但锁定按钮必须可用于解锁；
- 只通过既有唯一配置提交/保存链改变状态；保存失败必须恢复内存原值，并把补偿版本作为可重试候选保存；
- App 经 DispatcherQueue 消费命令，避免在 activation HWND 的窗口过程内重建并销毁同一来源。

## 2. 实现审计

### 2.1 有限原生标题命令面

`WindowsProductDesktopInteractionActivationSource` 现在为每个方格生成三个互不重叠的区域，按显示器 DPI 从 32 DIP 换算像素。视觉符号、UIA Name、Enabled 和 Invoke 都来自同一 `ActivationRegion` 状态：

- `进入 {名称} 交互`；
- `折叠/展开 {名称}`；
- `锁定/解锁 {名称}`。

进入 Explicit 选择后，命令面按既有互斥策略撤销；锁定方格的“进入交互”禁用，但“解锁”保持可用。UIA 不拥有焦点，不扩大原生窗口的有限命中区域。

### 2.2 生命周期与 App 事实绑定

DesktopHost 生命周期只接收仍属于当前 batch、当前 display 且唯一命中 container 的来源命令，并在边界内补上当前 workspace revision 与 topology generation。正式 App 始终把回调排入 UI DispatcherQueue，再由 `ProductDesktopContainerHeaderCommandController` 二次复核当前会话、拓扑、显示器和来源事实。

这避免了两类错误：旧 HWND 把命令应用到新工作区，以及在当前 HWND 的 UIA/窗口消息回调中同步释放该 HWND。

### 2.3 唯一提交与失败补偿

命令控制器复用 `ProductWorkspaceCommitCoordinator` 的 `SetCollapsed`/`SetLocked` 和 `ProductWorkspaceSaveController`：

1. 接受时只提交一次目标布尔值并记录原值、edit revision 和 save revision；
2. Saved 且 revision 完全一致才发布完成；
3. 保存失败时用同一正式提交器写回原值，同时保留首次失败原因；
4. 补偿保存仍失败时，释放外部写租约后可用正式 Retry 把原值落盘；
5. 状态或 revision 被外部替代时标记 Superseded，不对未知新状态执行补偿。

桌面真实文件、路径、内容、Explorer 窗口和任务栏均不在本阶段权限范围内。

## 3. 真实 Expected / Actual

### 3.1 原生 HWND 与 UIA Invoke

测试创建真实非零 activation HWND，从 Windows UI Automation 枚举并调用正式按钮。

| 项目 | Expected | Actual | 差异 |
| --- | --- | --- | --- |
| 有限按钮数 | 每方格 3 个 | 3 | 无 |
| 进入按钮 Bounds（96 DPI） | `32×32` | `32×32` | 无 |
| 折叠按钮 Bounds（96 DPI） | `32×32` | `32×32` | 无 |
| 锁定按钮 Bounds（96 DPI） | `32×32` | `32×32` | 无 |
| UIA 名称 | 进入/折叠/锁定 + 方格名 | 一致 | 无 |
| 折叠/锁定请求 | container 精确且来源已证明 | 一致 | 无 |
| 注入/自动重复事实 | `false/false` | `false/false` | 无 |

### 3.2 真实配置存储

| 场景 | Expected | Actual | 差异 |
| --- | --- | --- | --- |
| 折叠成功 | 内存和重载配置均 `Collapsed=true` | `true/true`，Published | 无 |
| 写租约冲突 | `WriteLeaseUnavailable` | 一致 | 无 |
| 失败后内存 | 恢复 `IsLocked=false` | `false`，Compensated | 无 |
| 冲突期间磁盘 | 保持原值 | `false` | 无 |
| 释放租约后 Retry | 补偿原值成功落盘 | `false`，Saved | 无 |

两个真实 Store 测试均输出结构化 `Expected`、`Actual` 和 `Difference=None`。

## 4. 测试差异与修正

| 轮次 | 预期 | 实际差异 | 处理 |
| --- | --- | --- | --- |
| 首次测试构建 | UIA 测试编译 | 可用性委托已从整体布尔值变成逐区域判断 | 更新旧测试为逐按钮委托 |
| 首次 UIA 聚焦 | 旧通用按钮名保持兼容 | 有标题的正式按钮已输出更明确的 `进入 工作 交互`；人工构造的空标题产生空格 | 正式测试对齐语义名；实现为空标题保留旧通用名称 |
| 首次真实三按钮调用 | 三条命令连续可测 | 先“进入交互”会按设计撤销命令面，后续折叠被 UIA 拒绝 | 保留产品互斥策略，按折叠→锁定→进入顺序验证 |
| App 接线复审 | 保存完成一定有后续通知 | 极快 Store 可能在控制器登记 publication 前完成 Saved 通知 | App 应用新文档后立即复读当前保存快照，正常异步通知路径保持不变 |

最后一项不是产品缺陷：Explicit 期间撤销标题命令可防止选择事务与配置事务并发。测试没有放宽该安全边界。

## 5. 门禁结果

- Release 全量：`1094/1094`；
- Release App 构建：`0 warning / 0 error`；
- 153-ID UI 合同及新增 PF-004B 源码合同：通过；
- 真实原生 HWND/UIA Invoke：通过；
- 真实 Store 成功/失败/补偿/重试：`Difference=None`；
- 正式 Release App：DesktopHost `1,776 ms` 就绪、持续响应 20 秒、重定向后控制中心 1 个、退出后零活进程、零临时配置写入，`Difference=None`；
- 未发送输入、未截图、未跨进程读取 WinUI UIA、未修改桌面文件。

## 6. 需求对齐与下一步

PF-004B 关闭了“状态可见但桌面不能直接折叠/锁定”的偏移，入口、状态、提交和失败恢复均位于正式产品链。由于 PF-004 仍缺更多菜单、重命名/外观/排序入口、删除确认及统一撤销，顶层继续为 `InProgress`，30 个 PF 仍为 `0 Complete`。

下一切片固定为 **PF-004C：更多菜单与安全管理入口**。菜单先接入已有正式重命名、外观和排序能力，逐项根据锁定/只读/保存状态启用或禁用；Esc、失焦、投影替换和系统表面事件必须关闭且零提交。删除只提供通往 PF-004D 确认流程的入口，不得在 PF-004C 直接删除。PF-004D 再完成“只删 Long方格配置、不删真实文件”的默认取消确认和统一撤销。
