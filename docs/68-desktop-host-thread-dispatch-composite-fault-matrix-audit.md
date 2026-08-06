# Long方格 DesktopHost 线程封送与复合故障矩阵审计

> 审计日期：2026-08-06
>
> 范围：DesktopHost UI 线程封送、窗口注册表二次复核、配置绑定原子交换、双生产适配器故障注入与 App 安全边界
>
> 结论：RC 硬化切片 3 已完成代码和自动化；真实执行入口继续保持 App 零接线

## 1. 审计发现与修正

上一切片的窗口适配器在调用线程直接执行原生批处理。真实窗口属于 DesktopHost UI 线程，跨线程直接移动会破坏宿主串行语义；如果调用线程持有注册表锁再同步等待 UI 线程，而 UI 线程同时注册或注销窗口，则会形成锁反转死锁。另一方面，配置适配器此前只完成磁盘 CAS，没有同步推进复合事务的 current binding，真实组合执行会在配置发布后被协调器判定为版本未变化并回滚。

本切片完成两项修正：

1. 窗口桥先在调用线程生成只含自有 claim 的 prepared batch，随后由目标 DesktopHost 线程重新取得注册表锁，复核 bridge ID、registry generation、完整容器集合、宿主线程、句柄、进程、线程和实例标识，最后才执行原生 batch；调用线程不持锁等待 UI 线程。
2. 新增线程安全的 `ProductWorkspaceCompositeBindingState`。配置发布必须先匹配 Before binding，磁盘 CAS 成功后再执行 Before→After 原子交换；补偿和一次性撤销同样推进目标 binding。磁盘复读和 binding 必须同时匹配才算成功。

## 2. UI 线程超时为什么不能简单返回失败

`SynchronizationContextProductDesktopHostThreadDispatcher` 只对“尚未开始”的排队工作应用 1–5000 ms 的 queue timeout。超时时通过原子状态把 Pending 改为 Cancelled；即使 UI 消息稍后被处理，回调也不会执行，因此不存在返回失败后又迟到移动窗口的情况。

如果 UI 线程已经把工作从 Pending 改为 Running，调用方必须等待该次同步原生操作完成并取得真实结果。此时继续套用排队超时会制造更危险的假失败：上层开始补偿，而原操作仍可能随后改变窗口。dispatcher 对排队拒绝、有限 native/同步上下文异常和操作失败返回有限状态，不把异常传播到事务边界。

## 3. 双生产适配器故障矩阵

新增集成矩阵使用真实 `ProductConfigurationStore`、配置事务适配器、绑定状态、产品窗口注册表、verified-window 适配器和复合事务协调器；窗口检查器、原生 mutator、线程 dispatcher 与输入门仅作为可控故障点。

| 场景 | 预期结果 | 已验证的不变量 |
| --- | --- | --- |
| 正常提交 + 一次性撤销 | Applied → Undone | 配置、Bounds、binding 一同前进和恢复，输入重新打开 |
| 首次窗口原生批处理失败 | RolledBack | 配置保持 Before，窗口恢复 Before，无撤销令牌 |
| 磁盘发布后 binding 交换失败 | RolledBack | 适配器恢复磁盘，窗口恢复，binding 保持 Before |
| 窗口成功后出现外部配置写入 | RollbackFailed + 隐藏宿主 | 外部配置不被覆盖，窗口恢复，输入保持关闭 |
| dispatcher 排队超时 | 有限失败 | Pending 工作取消，消息稍后执行也不会调用 mutator |
| dispatcher 已开始但超过 queue timeout | 等待真实完成 | 不产生迟到 mutation 或错误补偿并发 |
| dispatch 间隙 registry generation 漂移 | 有限拒绝 | UI 线程二次复核失败，原生 mutator 零调用 |

该矩阵证明的是适配器组合的自动化一致性，不等同于真实 DesktopHost、显示器、Explorer、关机或安装环境已经通过。

## 4. 初始需求对齐

| 初始需求 | 当前对齐结论 |
| --- | --- |
| 桌面文件整理与分组 | 本切片只加固布局恢复事务；仍不移动、不删除桌面文件 |
| 任务栏美化 | 未进入当前 MVP 收口，继续作为后续独立受控模块 |
| 自定义窗口与平滑效果 | 仅允许对完整 verified 自有窗口集提交非激活、非 Z-order Bounds batch；不改变外观/Region |
| 现代扁平华丽 UI、动效、L+方格图标 | 既有 App Shell、Design Token、Reduced Motion 和品牌 RC1 不变 |
| 小组件及 Long助手插件兼容 | LPWP 1.0 文档不变；运行时仍在 MVP 后续，未接入当前事务 |
| 一键启动与一键打包 | 脚本已存在；正式发布候选仍需干净环境打包/安装复核 |

`eng/Test-LongGridUi.ps1 -ContractOnly` 现强制 prepared-batch 二次复核、目标线程一致、Pending-only 取消、Running 等待、binding 原子交换、短租约配置 CAS 和 App 零引用。AutomationId 仍保持 118。

## 5. 审计结论与剩余收口

本地 Debug/Release 均为 489/489 测试通过，构建均为零警告零错误。两份独立 Cobertura 报告分别为：Debug 行覆盖率 90.66%（7916/8732）、分支覆盖率 82.57%（1904/2306）；Release 行覆盖率 91.48%（6762/7392）、分支覆盖率 81.41%（1879/2308），均通过仓库 90%/75% 门槛。格式、118-ID UI 合同、单实例、启动链、配置持久化、文件操作安全、缩略图 worker 隔离和依赖漏洞门禁通过。DesktopHost 交互探针仍为 `Conditional Pass`；#19/#20/#23/#24 的真实结果继续为 `PendingManualEvidence`、`ResultsPending` 或 `PendingDedicatedEnvironmentEvidence`。远端 PR 与 main CI 同样必须通过后才算进入主干。当前不能宣称真实窗口恢复已可交付，因为 App 仍不构造 dispatcher、窗口 mutator、配置适配器或复合协调器，也没有真实移动用户窗口。

后续收口顺序：

1. 输入关闭/重开、显示拓扑变化、DesktopHost 注销/重建和 App 关闭排空的组合矩阵；
2. 清理残留单实例后复跑 118-ID 真实 UIA，并执行 #19 输入和 #20 动态显示实机矩阵；
3. 在干净克隆执行一键启动、一键打包、安装/卸载/回滚与 Release Candidate 审计；
4. #19、#20、#23、#24 真实证据及许可证/分发决定清零后，才评审真实执行入口接线。

任务栏美化、小组件/插件运行时和广泛窗口特效仍不计入这轮收口阶段。
