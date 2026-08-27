# Stage 212：TASKBAR-R2A1 可恢复应用事务基础审计

日期：2026-08-27

开发基线：`origin/main@67eb4f9232fdfac65ca6861e0d24f35396e24721`

状态：`EngineeringComplete / RealDiskAndKillPass / NativeTaskbarMutationNotStarted`

## 1. 阶段结论

本阶段实现 TASKBAR-R2A 的第一半 `R2A1`：在任何任务栏外观写入之前，先建立系统默认基线、15 秒确认、失败回退和跨进程强杀恢复所需的正式状态与持久化凭据。

- 定义 `SystemDefault / Clear` 有限预设；系统默认是唯一恢复基线；
- 未认证 build、探测失败、冲突或探测声称修改系统时，在创建事务前拒绝；
- 状态固定为 `ReadyToStage → AwaitingConfirmation → Confirmed` 或 `RollbackRequired → RolledBack/RollbackFailed`；
- 应用失败、验证失败、15 秒到期、用户拒绝、父进程退出和启动恢复都要求恢复系统默认；
- 回滚失败保留恢复凭据，不能伪装成已经恢复；
- 凭据只保存有限枚举、build、Explorer PID、任务栏窗口类和时间界限，不保存标题、路径或 HWND；
- 使用同目录 `.new`、WriteThrough、Flush-to-disk、复读和原子发布；阶段只允许 `Staged → Applied → Confirmed`；
- 已有凭据不能被第二事务覆盖；未知字段、畸形、超大或重解析点输入失败关闭；
- 用户确认后仍保留 `Confirmed` 凭据，只有系统默认恢复且验证成功后才清理。

本阶段没有调用任务栏写 API，没有认证 Build 26200，也没有展示可点击但无效果的预设。

## 2. 技术路线复核

微软将 `DwmSetWindowAttribute` 定义为窗口 DWM 非客户区属性；`DWMWA_SYSTEMBACKDROP_TYPE` 是窗口的系统绘制背景材质，文档没有承诺可稳定换肤 Explorer 任务栏：[DwmSetWindowAttribute](https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmsetwindowattribute)、[DWMWINDOWATTRIBUTE](https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)。

TranslucentTB 当前公开实现仍对任务栏 HWND 使用 `SetWindowCompositionAttribute`，并在新路径引入更深的 Taskbar Appearance Service；正常恢复也显式返回任务栏默认外观：[taskbarattributeworker.cpp](https://github.com/TranslucentTB/TranslucentTB/blob/release/TranslucentTB/taskbar/taskbarattributeworker.cpp)。这说明清透可实现，但自有窗口 DWM 成功不能等同于任意 Windows build 的任务栏兼容性。

路线维持：不注入/Hook Explorer；独立 Worker；build 白名单；冲突拒绝；先写凭据再应用；15 秒回退；Explorer 重启和卸载恢复必须进入真实矩阵。

## 3. 需求对齐

| 需求 | 当前实际 | 判定 |
|---|---|---|
| iTop 式简单预设 | 已定义系统默认和清透的有限语义 | 部分对齐；原生应用 Pending |
| 15 秒确认/回退 | 状态机覆盖确认边界和全部回退原因 | 工程对齐 |
| 崩溃恢复 | 凭据真实写穿，子进程强杀后仍可读回 Applied | R2A1 对齐 |
| Explorer 重启/卸载 | 凭据不保存 HWND，可面向新 Explorer 恢复默认 | 结构准备；实机 Pending |
| 不支持版本拒绝 | Build 26200 仍为 `DeniedNoCertifiedBuild` | 对齐 |
| 现代 UI | R1B 显示真实兼容状态；本阶段不增加假预设 | 对齐诚实性 |

没有发生方向偏移：本阶段只建设任务栏核心的恢复前置，没有扩展 Widget、插件、自动整理或教程页。

## 4. 真实测试与修正

```powershell
dotnet test tests/LongGrid.Core.Tests/LongGrid.Core.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~TaskbarAppearanceRecoveryTransactionTests"
```

| 场景 | 预期 | 实际 | 差异 |
|---|---|---|---|
| Build 26200 | 不创建应用事务 | `AdmissionDenied / None` | 无 |
| 确认窗口 | 精确 15 秒，到点回退 | `Now+15s / ConfirmationExpired` | 无 |
| 应用/验证失败 | 恢复系统默认 | `ApplyFailed / VerificationFailed` | 无 |
| 父进程退出/启动恢复/拒绝 | 全部回退 | 三种原因均为 `RestoreSystemDefault` | 无 |
| 回退验证失败 | 保留凭据 | `RollbackFailed / PreserveRecoveryJournal` | 无 |
| 真实磁盘阶段 | 只允许单向阶段并原子复读 | 乱序拒绝；合法更新成功；无 `.new` | 无 |
| 第二事务 | 不覆盖未恢复事务 | 第二次 Stage=false，首个 ID 不变 | 无 |
| 未知字段 | 拒绝并保存证据 | `Invalid / MalformedJson`，文件保留 | 无 |
| 真实强杀 | Applied 写穿后强杀仍可恢复 | `RecoveryRequired / Phase=Applied`，ID 一致 | 无 |

首次专项 `14/14`。审计发现初版 Confirm 会清理凭据，与崩溃/卸载恢复目标冲突；修正为确认后保留凭据并增加 `Staged/Applied/Confirmed` 单向更新，修正后 `16/16`。没有放宽断言。

PR 首轮 CI 又发现真实时序差异：父测试最初只等待 `RecoveryRequired`，因此 GitHub runner 在子进程完成 `Staged → Applied` 原子更新前就可能强杀，实际读到安全但不符合该场景目标的 `Staged`；本机较快时已是 `Applied`。修正为父进程明确复读到 `Phase=Applied` 后才强杀，没有把两种阶段混为一次“应用后崩溃”，也没有放宽期望。

第二轮 CI 的 1301 项测试全部通过，但覆盖率实际 lines `89.98%`，低于 `90.00%` 门槛约 9 行；branches `75.47%` 已通过。没有降低门槛，补充“验证成功后清理、无故障保持、无效凭据、超大凭据与空清理”边界测试；本地隔离结果目录重新得到 `1305/1305`、lines `90.28%`、branches `75.81%`。

回归门禁预期格式无差异、Release 构建零告警/零错误、完整测试零失败；实际 `dotnet format --verify-no-changes` 退出码 0、全解决方案 `0 warning / 0 error`。补齐覆盖边界后完整核心测试 `1305/1305`，覆盖率 lines `90.28%`、branches `75.81%`，均高于 `90% / 75%` 门槛。

## 5. 安全边界与下一步

- 冲突工具在准入前拒绝，因此恢复基线固定为系统默认，不序列化第三方私有状态；
- 不持久化 HWND，Explorer 重启后必须重新发现任务栏；
- JSON 上限 16 KiB并拒绝重解析点和未知字段；
- 当前 Store 假定唯一 Worker 串行拥有，跨进程写租约与恢复执行器留给 R2A2；
- 强杀测试只证明恢复意图不丢，不证明任务栏已改色或已恢复；Build 26200 继续不得认证。

下一唯一项 `TASKBAR-R2A2`：独立 Worker 启动先处理遗留凭据；取得唯一事务租约；Stage 写穿后才允许原生调用；应用后验证；超时、拒绝、App/Worker 退出、Explorer 重启均恢复并验证系统默认；个性化页只在认证 build/会话启用清透；必须在可丢弃 Windows 矩阵取得真实任务栏像素、窗口身份和强杀前后证据。R2A2 前，TASKBAR-R2 与 M2 均保持 Pending。
