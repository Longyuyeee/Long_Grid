# Long方格 DesktopHost 调度器测试确定性审计

> 审计日期：2026-08-11
>
> 范围：`ProductDesktopHostThreadDispatcherTests` 的线程调度竞态、失败有界性与 CI 复现证据
>
> 结论：测试基础设施稳定性修复；不修改生产调度器、产品行为、权限或发布边界

## 1. 远端证据与根因

PR #143 的相同代码树完成 566/566 测试，但合并提交 `fed75df015fff251999976a99bcf197f8064ccc6` 的 `main` CI 运行 `31462725178` 再次在 Test 阶段无输出。Stage 92 的 2 分钟 VSTest 无活动诊断按设计终止 testhost，并上传 TRX、覆盖率和 `Sequence_b702ef8cb3744f248b71af2f900e1c48.xml`，没有生成内存 dump。

Sequence 共记录 561 个已开始测试：560 个 `Completed=True`，唯一的 `Completed=False` 是 `LongGrid.Core.Tests.DesktopHost.ProductDesktopHostThreadDispatcherTests.StartedOperationIsAwaitedInsteadOfReportedAsTimeout`。TRX 同样记录 560 项通过、0 项断言失败，运行因 testhost 被有界终止而中止。

该测试原来使用 20ms 排队超时，并通过第二个 `Task.Run` 启动目标上下文。runner 繁忙时，目标任务可能在 20ms 后才取得线程池线程；此时生产调度器正确取消仍未开始的 work item，而测试随后无限等待只会由被取消 work item 设置的 `entered` 信号。根因是测试调度和等待设计的竞态，不是生产调度器把已开始操作误报为超时。

## 2. 有限修复

- 目标上下文改由 `TaskCreationOptions.LongRunning` 的专用线程执行，避免与待测调用争抢同一线程池；
- 排队超时从 20ms 调整为 1 秒，覆盖 CI runner 的正常调度抖动；
- 操作进入后等待 1.1 秒，再断言调用仍未完成，从而继续严格证明“已开始操作超过排队窗口后仍被等待”；
- `post`、`entered`、调用结果和目标线程任务全部使用统一 5 秒测试上限；
- `finally` 无条件释放操作，并有界等待目标任务，失败路径不遗留阻塞线程；
- 同一测试类另外两条异步用例也增加 5 秒等待上限，避免任何测试基础设施等待无限化。

修复不触及 `SynchronizationContextProductDesktopHostThreadDispatcher`。生产合同保持：排队阶段可在有限时间取消；一旦 work item 已进入 Running，就等待操作完成，不能把已产生副作用的操作伪装成排队超时。

## 3. 验证与收口条件

提交前必须完成目标测试连续压力、同类测试、格式、Release 全量测试、覆盖率、安全探针、依赖漏洞和内部 unsigned RC。最终收口必须同时满足：

1. PR CI 全链路通过；
2. 合并后的新 `main` runner 完成 566/566 测试及所有后续门禁；
3. Stage 92 的 blame-hang 与 Sequence always-upload 合同继续保留，不能因本次修复移除诊断能力。

若 main 再次失败，必须以新的 Sequence/TRX 为准重新审计，不允许仅凭本地或 PR 通过判定问题消失。

提交前本地验证结果：

- 目标测试使用 10 秒 blame-hang 连续运行 20 次：20/20 通过；
- `ProductDesktopHostThreadDispatcherTests`：8/8 通过；
- `dotnet format --verify-no-changes`：Pass；
- Release 构建：0 警告、0 错误；
- 使用 CI 的 2 分钟 blame-hang 合同完成全量测试：566/566，通过耗时约 4 秒；
- 覆盖率：行 91.34%（16464/18024），分支 80.73%（4734/5864），高于 90%/75% 门槛；
- CI 挂起诊断源码合同：Pass，继续禁用内存 dump 并保留 Sequence 证据；
- 配置持久化 20 个场景全部通过；文件操作安全与缩略图隔离：`ConditionalPass`，保持既有受控限制；
- 依赖漏洞门禁：未发现已知漏洞包。

## 4. 需求对齐

本切片恢复开发流程的可信绿灯，不增加桌面文件写入、任务栏修改、窗口控制、小组件/Long助手插件或外部通信能力。Issue #19、#20、#23、#24、BSA、动态显示、五人无提示测试和专用卷证据状态不变；后续产品阶段仍按既有路线推进。
