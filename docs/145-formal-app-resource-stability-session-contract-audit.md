# Stage 145：正式 App 24 小时资源长稳会话合同审计

- 审计日期：2026-08-15
- 开发基线：`main@b5bb94c06b574182ad740f23cd8d73dd53576f30`
- 切片：M4c2a
- 当前判定：**M4c2a Session Contract Engineering Pass / Live Evidence Pending**

## 1. 需求对齐与真实差距

M4c2 必须回答正式 App 在真实 HWND、UIA、目录、DesktopHost 和缩略图 worker 长时间运行时，private bytes、handle、thread、窗口、worker/Profile 与状态修订是否持续增长。审计后的代码事实是：

- 正式 App 已拥有主窗口、UIA、只读桌面目录、配置/保存控制器和受双 opt-in 约束的 DesktopHost Surface；
- M4c1 已证明合成生命周期、目录控制器和系统表面状态机的确定性释放；
- AppContainer 缩略图 worker、Job/Profile/孤儿清理仍位于独立 `LongGrid.Spikes.ShellItemImages` 探针，**没有进入正式 App**；
- 正式 App 尚无对外的匿名状态修订遥测，外部会话无法可靠区分预期修订与无输入状态漂移。

因此不能直接执行一个只看 `Process` 计数的 24 小时脚本并宣称 M4c 通过。M4c2 被拆为：

1. **M4c2a（本切片）**：冻结时长、采样、预算、隐私、停止条件和 blocker；实现正式 App 部分资源会话入口；
2. **M4c2b**：把受限缩略图 worker 接入正式产品链，并提供只读、匿名、有界的 worker/Profile 与状态修订遥测；
3. **M4c2c**：从同一新 commit 在支持设备执行完整 24 小时会话并复核证据。

## 2. 运行前冻结的预算

这些阈值从本切片进入主线后不得根据实测结果放宽。任何调整必须独立 PR，写明依据，并使旧证据失效。

| 项目 | 固定合同 |
| --- | --- |
| 会话与采样 | 正式 App 连续 24 小时；60 秒一次；前 30 分钟预热不判趋势；首尾各 60 分钟取中位数 |
| 完整性 | 样本覆盖率至少 98%；相邻样本最大间隔 180 秒；App 退出或重启次数必须为 0 |
| Private bytes | 末窗中位数相对首窗不超过 +64 MiB；预热后线性斜率不超过 +2 MiB/小时 |
| Handle | 末窗中位数不超过 +32；斜率不超过 +1/小时 |
| Thread | 末窗中位数不超过 +4；斜率不超过 +0.25/小时 |
| 顶层窗口 | 末窗中位数不增加；相对首窗最大瞬时增加不超过 2，且结束无孤儿窗口 |
| UIA | 主根连续不可用不得超过 2 个样本；最终必须可用 |
| worker/Profile | 必须观察正式 worker 活动；结束 orphan worker=0、产品自有临时 Profile=0、活动 Job=0 |
| 状态修订 | 必须有匿名 revision telemetry；无操作窗口的意外漂移为 0，所有预期变更单调且来源可解释 |

单一阈值通过不构成最终 Pass。任一完整性、安全、隐私、worker/Profile、状态修订或进程存活条件缺失，结果必须是 Fail 或 Inconclusive。

## 3. M4c2a 会话入口

`eng/Start-LongGridResourceStabilitySession.ps1` 提供两个模式：

- `-ValidateOnly`：CI 只验证固定合同；不启动 App、不枚举桌面、不写证据；输出保持 `PendingLiveEvidence`；
- live：要求匿名 operator、专用测试账户、匿名工作区、恢复计划、DesktopHost opt-in 和显式空证据目录；启动并只管理自己创建的正式 App，启用 DesktopHost 且强制关闭 Explicit interaction，采集不含路径、名称、内容、句柄值或 PID 的有限进程指标。

live 入口即使完成 24 小时，也固定返回 `PendingProductTelemetryIntegration`，因为当前正式 worker 和状态修订遥测 blocker 尚未关闭。该部分结果可用于发现明显趋势，但不能升级 M4c。

## 4. 安全、隐私与停止条件

- 会话只读桌面第一层元数据，不读文件内容，不移动、重命名、删除或写入桌面文件；
- 证据只写入操作者显式提供的现有空目录；JSON 不记录路径、文件名、桌面项目、内容、原始句柄值、PID、账户或机器名；
- 发现既有 `LongGrid.App` 时有限拒绝，绝不终止非本入口创建的进程；
- 正式 App 提前退出、主窗口未建立、证据目录不为空或依赖缺失时立即停止，不把短会话判成通过；
- 正常结束先请求关闭本入口创建的窗口，15 秒未排空时只强制终止该准确进程；
- 断电、休眠、系统更新、采样中断、账户内容污染或 operator 无法解释的状态变化均使本轮 Inconclusive，并从新 commit/新空目录重跑。

## 5. 自动化验收

- CI 新增 `Validate 24-hour resource stability session contract`；
- 验证入口固定输出 24 小时、60 秒、30 分钟预热、60 分钟比较窗和全部预算；
- 验证入口必须声明 `formalThumbnailWorkerIntegrated=false`、`formalStateRevisionTelemetryAvailable=false`、`canProduceM4cPass=false`；
- M4c1 加速预检和独立缩略图 worker 隔离门继续执行，不能被本入口替代。

## 6. 验收与下一步

- 本切片验收：脚本解析/合同验证通过，CI 完整门禁通过，文档与代码 blocker 一致；
- 本切片不得产生：M4c Pass、M4-ready、RC 可分发或真实 24 小时证据；
- 下一步：M4c2b 正式 worker 与匿名状态修订遥测设计审计，先关闭可观测性缺口，再安排 M4c2c 真实 24 小时执行。

## 7. 远端轨迹

- 实现 PR：[#205](https://github.com/Longyuyeee/Long_Grid/pull/205)，head `8ecf08913127a5c02698f3fe181bba8f95488b25`；
- PR run `31819228748`：924/924，lines 90.83%、branches 78.22%；格式、构建、新会话合同、M4c1、缩略图隔离、安全、依赖和内部 unsigned RC 交付集审计全部通过；
- squash 合并：`main@8e7ee34dd71e583f58294f83244983779ab37ebe`；合并后 main run `31819825057` 为 924/924，lines 90.83%、branches 78.22%，相同合同与完整门禁通过；
- 远端合同仍明确输出 `formalThumbnailWorkerIntegrated=false`、`formalStateRevisionTelemetryAvailable=false` 和 `canProduceM4cPass=false`，因此 M4c2a 关闭不改变 M4c/M4-ready/RC 状态。

后续进展：M4c2b1 已增加默认关闭的同用户匿名状态遥测，`formalStateRevisionTelemetryAvailable` 在受控会话合同中提升为 `true`；正式 worker 与真实 24 小时证据仍 Pending。详见 [Stage 146](146-formal-app-anonymous-resource-telemetry-audit.md)。
