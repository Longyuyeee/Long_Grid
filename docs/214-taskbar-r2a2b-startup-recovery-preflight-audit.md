# Stage 214：TASKBAR-R2A2b 启动恢复预检审计

日期：2026-08-27

开发基线：`origin/main@cbf32e4827d8df23f87cc5cd5bcfdd599417c426`

状态：`EngineeringComplete / RealWorkerAndExplorerPass / NativeRestorePending`

## 1. 阶段目标与结论

本阶段只关闭 R2A2b 的启动恢复预检，不宣称任务栏样式已经可用。正式 App 正常启动时会先启动独立 `LongGrid.TaskbarWorker.exe`；Worker 取得唯一恢复租约、读取遗留恢复凭据并重新探测当前 Explorer。所有结果通过有界、版本化、请求绑定的协议返回。

- 正式路径固定为 `%LOCALAPPDATA%\LongGrid\TaskbarRecovery`，App 不从命令行或测试环境接收恢复目录；
- 测试目录覆盖必须同时使用隐藏参数与 `LONGGRID_TASKBAR_WORKER_EVIDENCE=1`，无开关时退出 65；
- 恢复日志存储从 Infrastructure 下沉到 Core，日志 schema、原子写入和租约要求不变，避免 Worker 反向引用 Infrastructure 形成循环依赖；
- 无日志时返回 `NoRecoveryRequired`，不创建 JSON；租约竞争、畸形日志、I/O 失败、build 变化和兼容性拒绝均保留可能存在的原日志；
- 当前 build 未认证时返回 `RecoveryDeferredCompatibility / DeniedNoCertifiedBuild`，不调用任何任务栏写 API；
- 即使未来兼容性准入为 Allowed，在原生恢复适配器交付前也只能返回 `RecoveryDeferredAdapterUnavailable`，不能清除凭据或伪报恢复成功；
- Worker 仍绑定父进程、响应大小上限和超时；协议拒绝 `ModifiedSystemState=true`。

## 2. 真实测试：预期、实际与差异

测试启动测试输出目录旁的正式 Worker EXE，使用真实临时目录、真实 Windows 文件租约和当前 Explorer 的真实任务栏 HWND，不使用 mock Worker、mock 文件系统或伪造 Windows build。

| 场景 | 预期效果 | 实际效果 | 差异与修正 |
|---|---|---|---|
| 空恢复目录 | 启动恢复正常结束，不产生恢复 JSON 或 `.new` | `NoRecoveryRequired`，`JournalPreserved=false`，目录中无 JSON/`.new` | 无 |
| 遗留 Applied 凭据 | 当前未认证 build 必须拒绝写入并逐字节保留凭据 | `RecoveryDeferredCompatibility / DeniedNoCertifiedBuild`；JSON 前后 byte[] 完全一致，无 `.new` | 无 |
| 任务栏系统状态 | 预检前后不得改变任务栏窗口或报告系统修改 | 前后 `HWND:class:PID` 完全一致；两份报告及恢复响应均 `ModifiedSystemState=false` | 无 |
| 唯一租约被占用 | 竞争 Worker 不等待、不读写、不覆盖凭据 | `LeaseContended`；已写 JSON 字节完全不变 | 无 |
| 畸形日志 | 失败关闭并保留现场，不能自动删除“修复” | `RecoveryJournalInvalid / MalformedJson`；原始 `{malformed` 字节完全不变 | 无 |
| 当前 Windows/Explorer | 真实只读探测通过，但未认证版本不能进入写路径 | Windows `10.0.26200.0`；`Shell_TrayWnd` 与 `Shell_SecondaryTrayWnd` 均归属 Explorer；`ProbeOutcome=Pass`，`RuntimeAdmission=DeniedNoCertifiedBuild` | 无；继续拒绝 |
| 正式 App 启动接线 | Recovery Worker 不得破坏桌面优先、单实例或退出清理 | Release App 首次 DesktopHost `1`、控制中心 `0`，冷启动 `1090 ms`；二次启动退出码 0 并激活唯一控制中心；3 秒响应、最终存活进程 0、临时配置写入 0 | `Difference=None` |

新增专项真实进程测试 4/4，通过后顺序执行全量核心测试为 **1319/1319**。Release 全解决方案构建为 **0 warning / 0 error**；按 CI 的 TRX、coverage 与 2 分钟 blame-hang 参数隔离重跑仍为 1319/1319，覆盖率 lines **90.16% (46412/51476)**、branches **75.85% (15196/20034)**，没有降低 90%/75% 门槛。最终远端覆盖率与完整门禁以本阶段 PR 的 GitHub Actions 为准。

## 3. 需求对齐与偏移审计

本阶段直接服务三项 Core 中的“任务栏美化可可靠恢复”，没有新增教程页、Widget、插件、自动整理、Tab 或工作空间功能。启动恢复在普通产品启动链执行，但当前拒绝结果不弹窗、不打断桌面优先启动；用户主动进入“个性化 → 任务栏”时仍由既有只读检测展示有限原因。

没有发生目标偏移，但必须保持以下口径：

- 已完成的是“真实启动恢复预检和失败关闭”，不是“恢复系统默认已经执行”；
- `SystemDefault / Clear` 原生适配器仍不存在；
- 没有任务栏像素变化、Explorer 重启后恢复、强杀应用后原生恢复、禁用或卸载恢复证据；
- Build 26200 未加入认证白名单；
- 正式发布仍不可开启任务栏样式。

## 4. 下一唯一开发项

下一步为 `TASKBAR-R2A2c`：建立**默认无可用实现**的原生适配器边界及可丢弃认证环境证据入口。必须先在专用 Windows build 矩阵取得应用前/应用后/恢复后的任务栏像素与 HWND 身份、Explorer 重启、Worker/App 强杀和卸载恢复结果，且 Expected、Actual、Difference 均可复核；只有该 build 全部通过，才能把它加入白名单并允许真实 `Clear` 临时应用。当前 Build 26200 继续只验证拒绝链，不调用写 API。
