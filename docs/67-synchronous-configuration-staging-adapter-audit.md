# Long方格同步配置暂存适配器审计

> 审计日期：2026-08-06
>
> 范围：配置 capture、指纹 compare-and-exchange、原子发布、复读、补偿、跨进程冲突与 App 安全边界
>
> 结论：RC 硬化切片 2 已完成代码和定向测试；配置与窗口的生产适配器均已具备，但 App 继续零接线

## 1. 为什么不能直接复用 latest-wins 入队结果

`ProductWorkspaceSaveController` 面向连续编辑，返回的是请求已被接受、合并或等待最终保存的工作流结果。复合事务必须在窗口已经移动后立即知道配置是否完成落盘、复读是否匹配，以及失败时能否安全恢复。把异步入队成功解释成事务提交成功会产生“窗口已改变、配置尚未发布”的不可判定间隙。

本切片新增 `ProductWorkspaceCompositeConfigurationAdapter`，直接实现 Core 的配置事务层；它同步等待正式 Store 的异步 I/O 完成，Store 内所有等待均使用 `ConfigureAwait(false)`，不会依赖 UI SynchronizationContext。App 尚未构造或调用该适配器。

## 2. 为什么不长期持有写租约

复合事务成功后会保留原配置快照，直到用户使用一次性撤销或后续编辑使其失效。如果让 capture 快照同时持有 `.lock` 文件，写租约可能跨越整段用户交互时间，阻断正常保存、关闭排空和其他进程的有限公平竞争。

因此采用短租约 optimistic CAS：

1. capture 只接受有效 `LoadedPrimary` 并生成 detached 配置快照和 canonical SHA-256；
2. 窗口层工作期间不占用配置锁；
3. apply 在跨进程写租约内重新加载主配置；
4. 当前指纹必须等于 capture 指纹，否则返回 Conflict 且零写入；
5. 通过后写同目录 `.new`，执行 WriteThrough、异步 flush、`Flush(true)`、schema 复读和 `File.Replace`；
6. verify 再次从 Store 读取主配置并比较投影指纹。

备份恢复态和 SafeMode 不可 compare-and-exchange，必须先走已有显式恢复 UI。

## 3. 补偿与外部写入保护

适配器记录自己最后一次成功发布的指纹。restore 只在以下两种情况成功：磁盘已经等于目标快照；或者磁盘仍等于适配器最后发布的版本，并能用该版本作为 CAS 期望值原子换回快照。

如果另一个写入者在 apply 后、补偿前发布了不同配置，restore 明确失败，不覆盖外部状态。上层复合协调器随后保持输入关闭并隐藏受影响宿主，而不是把未知配置强行回滚。这比“尽力恢复”更保守，但避免数据丢失。

快照绑定适配器随机 owner ID；异源、已释放、错误目标指纹或非 canonical binding 均拒绝。快照只保存验证后的配置文档，不保存原始损坏字节、文件路径或锁句柄。

## 4. 自动化与需求对齐

定向测试覆盖：有效主配置 capture、Missing/备份恢复/SafeMode 拒绝、成功 CAS 与复读、binding 指纹不符、capture 后外部写入、无效 workspace、已释放/异源快照、成功补偿、外部变化保护、写租约超时、双 Store 并发只有一个赢家、主配置不可用和 malformed fingerprint。

`eng/Test-LongGridUi.ps1 -ContractOnly` 强制存在：短写租约、LoadedPrimary、canonical 指纹、`File.Replace`、事务接口、last-published 防护和 App 零引用。AutomationId 保持 118。

| 最初需求 | 本切片结果 |
| --- | --- |
| 桌面分组和布局自动保存 | 提供配置与真实窗口共同提交所需的同步、可复读持久化边界 |
| 桌面文件整理 | 不读取、不移动、不删除桌面文件 |
| 任务栏美化 | 不进入本切片，继续属于 MVP 后续 |
| 自定义窗口效果 | 不改变窗口外观；只为已批准 Bounds 事务提供配置侧一致性 |
| 小组件与 Long助手插件 | LPWP 协议不变，运行时继续隔离在 MVP 后续 |

本地全量结果为 473/473 测试通过。两份独立 Cobertura 附件完全一致：单份行覆盖率 91.34%（6556/7177）、分支覆盖率 80.98%（1806/2230），通过仓库 90%/75% 门槛；重复附件的聚合计数不作为更高覆盖率证据。Debug/Release `-warnaserror` 均为零警告零错误；格式、118-ID UI 合同、单实例、启动链、配置持久化、文件操作安全、缩略图 worker 隔离和依赖漏洞门禁通过。DesktopHost 交互探针仍为 `Conditional Pass`；#19/#20/#23/#24 的 ValidateOnly 状态继续为 `PendingManualEvidence`、`ResultsPending` 或 `PendingDedicatedEnvironmentEvidence`。远端 PR/main CI 在发布流程中复核。

下一切片必须把两个生产适配器放入同一故障注入矩阵，并解决 DesktopHost UI 线程封送；真实输入、动态显示、关机、干净会话 UIA、安装和 Release Candidate 仍未完成。#19、#20、#23、#24 保持 Pending，全部 blocker 清零前不接入 App。
