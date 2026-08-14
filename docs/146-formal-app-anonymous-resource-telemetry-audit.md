# Stage 146：正式 App 匿名资源遥测审计

- 审计日期：2026-08-15
- 开发基线：`main@243c2f33401d24dfdf66322ecbfedb0660fb6a18`
- 切片：M4c2b1
- 当前判定：**M4c2b1 Engineering Pass / 正式 worker Pending / 真实 24 小时证据 Pending**

## 1. 需求对齐与拆分

M4c2a 已冻结 24 小时资源预算，但正式 App 缺少匿名状态修订遥测，外部采样器无法判断窗口/资源变化是否伴随 workspace、catalog、topology 或 DesktopHost 状态漂移。对独立缩略图 probe 的审计同时确认，它把 probe CLI、测试样本、压力矩阵、AppContainer/Job/IPC runtime 耦合在一个可执行项目中；正式 App 直接引用该项目会把测试入口和非产品代码带入交付集。

因此 M4c2b 继续拆分：

- **M4c2b1（本切片）**：只建立正式 App 的同用户、只读、匿名、按需资源遥测；
- **M4c2b2（下一切片）**：把 worker runtime 提取成独立受限产品组件，让 probe 与 App 共同复用；
- **M4c2c**：会话入口同时观察状态遥测和正式 worker/Profile/Job 后，才执行完整 24 小时证据。

## 2. 准入与传输安全

遥测默认不存在。只有以下条件同时成立才创建服务：

1. `LONGGRID_ENABLE_DESKTOP_HOST=1`；
2. `LONGGRID_ACKNOWLEDGE_RESOURCE_STABILITY_SESSION=1`；
3. `LONGGRID_RESOURCE_TELEMETRY_PIPE` 精确匹配 `LongGrid.ResourceTelemetry.` + 32 位小写十六进制随机后缀。

正式 App 创建单实例 `NamedPipeServerStream`，使用 `PipeOptions.CurrentUserOnly`、单连接、异步字节管道和有界逐行请求。协议只接受 `snapshot` 与 `complete`；未知请求只返回固定拒绝 JSON，不改变产品状态。管道不接收路径、项目、配置或操作命令，App 关闭时先取消/释放服务再释放 DesktopHost、目录和保存控制器。

## 3. 匿名白名单

每个快照固定 schema v1 和服务端单调 sequence，只包含：

- 保存状态与 current/saved revision；
- 目录状态、generation、条目计数；
- 拓扑状态、generation、显示器计数；
- DesktopHost 状态、generation、OwnedWindow/RenderedContainer 计数、workspace/topology/selection revision、UIA/Passive/Explicit 布尔值；
- 交互状态与 revision；
- worker 是否正式接入、worker/Profile 有界计数；当前必须诚实输出 `false/0/0`。

快照禁止路径、名称、内容、容器/项目/显示器身份、原始句柄、PID、账户或机器名。服务端在序列化前复核所有计数和 revision 非负、schema/sequence 正确、敏感数据声明为 false；在 M4c2b2 完成前，任何伪造 `formalThumbnailWorkerIntegrated=true` 或非零 worker/Profile 计数都会使服务失败。

## 4. 会话入口更新

`Start-LongGridResourceStabilitySession.ps1` 现在为每轮生成随机同用户管道名，在正式 App 主窗口建立后连接，并在每个 60 秒进程样本前请求一次产品快照。前 30 分钟仍为预热；之后对保存、目录、拓扑、DesktopHost、选择和交互有限状态生成匿名签名，受控空闲会话的意外变化次数预算固定为 0。

入口与证据状态更新为：

- `slice=M4c2b1`；
- `formalStateRevisionTelemetryAvailable=true`；
- blocker 从三个收敛为 `FormalThumbnailWorkerNotIntegrated` 与 `Real24HourEvidenceNotCollected`；
- live 完成后仍为 `PendingFormalThumbnailWorkerIntegration`、`canProduceM4cPass=false`。

脚本结束先发送 `complete` 并释放客户端，再只关闭自己创建的正式 App；DesktopHost、交互紧急关闭、pipe 与 session acknowledgement 环境变量全部原样恢复。

## 5. 自动化验收

- policy 覆盖 DesktopHost 未开启、session 未确认、非法/合法 pipe 名；
- 真实同用户 named-pipe round trip 覆盖未知请求拒绝、schema/sequence、字符串枚举、有限计数和敏感内容缺失；
- 会话 `ValidateOnly` 覆盖固定预算、趋势助手、revision 漂移助手、worker blocker 与 `canProduceM4cPass=false`；
- CI 继续执行完整格式、构建、924+ 测试、M4a/M4b/M4c1、文件/worker/依赖和 unsigned RC 门禁。

本地复验结果：遥测定向测试 5/5、Core 全量 929/929、PowerShell 解析、`ValidateOnly`、定向 whitespace format 与 `git diff --check` 均通过。正式 App 本机构建仍被已记录的 Windows SDK 引用包缓存差异阻断（项目要求 `Microsoft.Windows.SDK.NET.Ref 10.0.19041.38`，本机离线缓存解析为 `.34`）；这不是本切片代码失败，App 编译与完整门禁必须由干净的 GitHub Windows runner 给出权威结果。

## 6. 远端证据与判定

- PR [#207](https://github.com/Longyuyeee/Long_Grid/pull/207) run `31822678611`：929/929，lines 90.85%（28320/31172），branches 77.97%（8758/11232）；格式、正式 App Build、匿名遥测合同、全部安全/worker 隔离与 unsigned RC 交付集门通过；
- squash 合并：`main@5f4a5c20653af18715ed30fc6d5a4f77cd99840c`；合并后 main run `31823246761` 再次为 929/929、lines 90.85%、branches 77.97%，完整门禁通过；
- 因此 M4c2b1 判定 Engineering Pass；这只关闭匿名 revision telemetry 缺口，不关闭 `FormalThumbnailWorkerNotIntegrated` 或 `Real24HourEvidenceNotCollected`，也不改变 M4c/M4-ready/RC 的 Pending 状态。

## 7. 非目标与下一步

- 本切片不读取文件内容、不启动 worker、不创建 Profile、不显示缩略图、不开放 Explicit 或文件操作；
- 本切片不把同用户匿名遥测提升为诊断 API、远程接口、持续日志或公开产品功能；
- 本切片不产生真实 24 小时 Pass；
- 下一步 M4c2b2：提取最小 worker runtime、保持零 Capability/Job kill-on-close/受控副本/有界 IPC，让 probe 反向复用并为正式 App 提供只读生命周期计数。

后续进展：M4c2b2 已在本地把 runtime 提取为独立 `LongGrid.ThumbnailWorker` 产品可执行组件，probe 反向复用，正式受控会话匿名计数提升为 worker/Profile `1/1`；远端门禁与真实 24 小时证据仍 Pending。详见 [Stage 147](147-formal-restricted-thumbnail-worker-integration-audit.md)。
