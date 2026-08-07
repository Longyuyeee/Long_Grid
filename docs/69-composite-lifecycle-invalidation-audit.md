# Long方格复合事务生命周期失效与恢复矩阵审计

> 审计日期：2026-08-07
>
> 范围：显示拓扑变化、DesktopHost 注册表变化、应用关闭、一次性撤销、输入重开失败与紧急隐藏
>
> 结论：RC 硬化切片 4 已建立生产生命周期 binding guard 和组合故障矩阵；App 继续零接线

## 1. 本轮发现的两个缺口

上一切片已经让事务协调器在多个检查点比较 current binding，但生产 binding 只会被配置发布/补偿推进。显示拓扑开始刷新、DesktopHost 断开或窗口注册表换代、应用开始关闭时，它不会自动失效，因此旧恢复令牌仍可能被判断为 current。

第二个缺口位于无 mutation 的收口路径：输入关闭并完成两侧 capture 后，如果 binding 发生变化且输入无法重新打开，协调器会报告输入仍关闭，却没有调用紧急隐藏。这会留下“输入不可用但宿主仍可见”的不完整安全状态。

## 2. 生命周期 binding guard

新增 `ProductWorkspaceCompositeLifecycleGuard`，同时实现配置适配器需要的 binding compare-and-exchange，并订阅正式 `ProductDisplayTopologyController` 和产品窗口注册表的快照事件。初始构造只接受以下精确证据：

- 当前显示拓扑必须为 authoritative `Ready`；
- topology generation 必须等于令牌 binding；
- 窗口注册表必须 ownership-attested；
- registry generation 必须等于令牌 binding；
- binding 的 DesktopHost 实例与 generation 必须保持不变。

guard 只有 `Ready` 状态允许读取 current binding 或交换 Before/After/Undo。以下事件永久终止当前 guard：

| 事件 | 状态 | 行为 |
| --- | --- | --- |
| 拓扑进入 Refreshing/Degraded/Cancelled 或 generation 改变 | `TopologyChanged` | current 读取有限失败，旧事务/撤销令牌失效 |
| 注册表断开、降级、增删/复核导致 generation 改变 | `DesktopHostChanged` | 禁止继续取得或恢复陈旧窗口集 |
| App 开始关闭 | `ShuttingDown` | 禁止新提交、补偿推进和撤销 |
| guard 释放 | `Disposed` | 解除事件订阅，状态不可恢复 |

普通 `TryExchange` 只能改变 edit revision 与配置指纹，不能把旧 guard 偷换到新的 topology、registry 或 DesktopHost 身份。新一代证据必须重新经过恢复审查与准入并构造新的 guard。

## 3. 组合故障矩阵

矩阵继续使用真实配置 Store、配置事务适配器、窗口桥、verified-window 批处理适配器、线程 dispatcher 和复合协调器：

| 注入点 | 结果 | 安全不变量 |
| --- | --- | --- |
| 窗口 apply 后拓扑刷新 | `RollbackFailed/BindingChanged` | 窗口恢复 Before，配置保持 Before，输入关闭并隐藏宿主 |
| 窗口 apply 后 DesktopHost 断开 | `RollbackFailed/WindowRestoreFailed` | 不访问陈旧注册表，配置保持 Before，输入关闭并隐藏宿主 |
| 窗口 apply 后开始关闭 | `RollbackFailed/BindingChanged` | 窗口恢复 Before，不在 shutdown 中重新开放输入 |
| Applied 后、Undo 前拓扑刷新 | Undo `Superseded` | 不改变已提交配置或窗口，不使用旧撤销令牌 |
| capture 后 binding 漂移且 reopen 失败 | `RollbackFailed/InputReopenFailed` | 零 mutation，必须调用紧急隐藏 |

DesktopHost 断开后测试中的假窗口 Bounds 仍保持最后一次值，这是刻意的：注册表已经失去所有权证据，系统宁可隐藏宿主并要求重建，也不会凭陈旧 HWND 做“尽力恢复”。

## 4. 需求对齐与边界

| 初始需求 | 本切片对齐 |
| --- | --- |
| 桌面分组和布局恢复 | 防止显示器/宿主变化时把旧布局写入新环境 |
| 桌面文件整理 | 不读取、不移动、不删除桌面文件 |
| 任务栏美化 | 未进入本切片，继续属于 MVP 后续独立模块 |
| 自定义窗口效果 | 只保护产品自有窗口 Bounds 事务，不改变 Region、激活或 Z-order |
| 现代 UI 与平滑动效 | 既有 UI Shell、Design Token、Reduced Motion 不变 |
| 小组件/Long助手插件 | LPWP 1.0 不变，运行时仍未接入 |

App 当前仍不构造 lifecycle guard、原生窗口 mutator 或复合协调器，不会移动真实窗口。自动化矩阵证明内部状态机和补偿边界，不替代 #19/#20 的真实输入、显示、Explorer 与会话证据。

## 5. 自动化与后续方向

本地 Debug/Release 均为 500/500 测试通过，构建均为零警告零错误。两份独立 Cobertura 报告分别为：Debug 行覆盖率 90.85%（8047/8857）、分支覆盖率 82.85%（1942/2344）；Release 行覆盖率 91.68%（6863/7486）、分支覆盖率 81.76%（1918/2346），均通过仓库 90%/75% 门槛。首次合并执行两份覆盖率时，命令观察窗口被一个未产出附件的 collector 会话占满；精确终止该孤立测试树后，带 hang 诊断的全量测试 500/500 通过，Debug/Release collector 拆分重跑均正常产出报告，因此没有把观察超时记为产品通过证据。格式、118-ID UI 合同、启动链、单实例、配置持久化、文件安全、缩略图隔离和依赖漏洞门禁均通过；DesktopHost 与缩略图探针仍按既有边界记为 `Conditional Pass`，#19/#20/#23/#24 真实结果继续为 Pending。`eng/Test-LongGridUi.ps1 -ContractOnly` 新增强制项：生命周期事件订阅、五态有限状态机、shutdown 终止、生命周期身份不可换代、输入无法重开时紧急隐藏，以及 App 零引用；AutomationId 保持 118。

后续收口顺序：

1. 为真实 DesktopHost 定义并验证生产输入关闭/重开/隐藏适配器和有界 shutdown drain；
2. 清理残留单实例后复跑真实 118-ID UIA，并执行 #19/#20 人工与硬件矩阵；
3. 在干净克隆完成一键启动、一键打包、MSIX 安装/卸载/回滚和 RC 审计；
4. 外部证据、许可证与分发决策清零后，才评审 App 真实执行入口。

任务栏美化、小组件/插件运行时和广泛窗口特效仍不计入本轮收口。
