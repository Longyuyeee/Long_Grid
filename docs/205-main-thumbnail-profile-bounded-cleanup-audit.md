# Stage 205：main 缩略图 AppContainer Profile 有界清理审计

- 日期：2026-08-25
- 基线：`main@973db66`
- 触发证据：GitHub Actions run `32812105456`
- 状态：`EngineeringComplete / Integrated`

## 1. 目标与真实差异

Stage 204 修正后的 main 已通过原先失败的真实原生 Surface 资源门，但全量测试在 `RealRestrictedWorkerReturnsPixelsOrFiniteFallbackAndRecoversFaults` 以 1199/1200 停止。

| 项目 | Expected | Actual | Difference |
| --- | --- | --- | --- |
| Worker Hang | 100 ms 超时并终止 | `TimedOut=true / WorkerExited=true` | None |
| Worker Exit | 显式退出被观察 | `WorkerExited=true` | None |
| Profile 清理 | `OwnedProfileDeletionConfirmed=true` | false | 未删除临时 AppContainer Profile |

失败位于 Worker 和 Job 已释放后的 Profile 删除。代码已有 parent-exit 场景使用的 `20 × 50 ms` 受控删除重试，但正常 `ThumbnailAppContainerProfile.Dispose` 仍只调用一次 Windows `DeleteAppContainerProfile`。远端并发与令牌释放时序使单次调用偶发失败；本机旧基线 10/10 通过也不能否定远端真实失败。

## 2. 修正边界

- 正常 Dispose 复用同等上限的 20 次删除尝试，间隔 50 ms；通常首轮成功，不引入固定等待；
- 只允许删除带 `LongGridThumbnailWorker` 前缀和严格 GUID 后缀的自有 Profile；
- 20 次仍失败时保持 `OwnedProfileDeletionConfirmed=false`，测试和产品遥测继续失败关闭；
- 新增 `OwnedProfileDeletionAttempts` 与 `OwnedProfileDeletionHResult`，Expected/Actual 不再只有布尔值；
- 不延长缩略图提取的 1.5 秒产品预算，不放宽 Hang 的 500 ms 完成门，也不保留孤儿 Worker。

## 3. 本机真实验证

同一个真实测试以独立 VSTest 进程连续执行 20 次；每次均创建真实零能力 AppContainer、启动受 Job 约束的 Worker、读取真实 BMP、执行真实 Hang/Exit 故障并删除 Profile。

| 验证 | Expected | Actual | Difference |
| --- | --- | --- | --- |
| 修正前本机复现 | 观察非确定性 | 10/10 通过，远端 1 次失败 | 环境相关竞态已由远端证实 |
| 修正后独立进程 | 20/20 | 20/20 | None |
| 删除尝试 | 1～20 | 每次落入范围 | None |
| 删除 HRESULT | 成功为非负 | 每次非负 | None |
| 删除失败语义 | 20 次失败仍报 false | 代码保持硬失败 | None |
| Release 全量与覆盖率 | 全量通过，≥90%/75% | 1202/1202，90.36%/75.72% | None |
| 正式 Worker matrix | 清理、隔离、500 次压力均通过 | `ConditionalPass`；500/500、P95 1.9254 ms、全部 Profile deleted | None |

## 4. 需求与方向对齐

本切片直接修复“临时资源必须可证明清理”的稳定性目标，没有增加功能宽度或偏离桌面整理主线。PF 状态不变；只有 PR/main 全链重新绿色后才恢复 PF-006C2 开发。

## 5. 集成结果

PR #231 run `32813091668` 完整通过并以 `9dda47d` 合入。main run `32813543652` 为 1202/1202、lines 90.03%（40826/45348）、branches 75.58%（13284/17576）；正式 Worker matrix 为 `ConditionalPass`、全部 Profile deleted、`CleanupSucceeded=true`，依赖漏洞门和内部 RC 800/800 文件审计通过。Expected/Actual/Difference 已收敛，本切片状态为 `EngineeringComplete / Integrated`，下一工程切片恢复为 PF-006C2。
