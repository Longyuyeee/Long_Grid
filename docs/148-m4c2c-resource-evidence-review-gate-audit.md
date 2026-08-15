# Stage 148：M4c2c 资源长稳证据复审门禁审计

- 审计日期：2026-08-15
- 开发基线：`main@5e3e560acc7f65d92e17786a1c9e657eb5f73482`
- 切片：M4c2c review readiness
- 当前判定：**Review Gate Engineering Pass / 真实 24 小时证据 Pending**

## 1. 需求对齐与审计发现

Stage 147 已让正式 App 在受控资源会话中持续证明 worker/Profile `1/1`，并在 App 退出后检查 worker 进程零孤儿；但原合同没有返回临时 AppContainer Profile 的释放终态，也没有独立复审器重新计算样本覆盖、资源趋势和状态漂移。直接执行 24 小时会留下两项不可判定风险：

1. `ownedProfileCountAtEnd=0` 只有预算声明，没有 live 终态证据；
2. 复审只能信任采集脚本写入的 `summary`，无法独立拒绝篡改预算、错 commit、缺样本或遥测断序。

本切片只关闭复审就绪缺口，不执行或冒充真实 24 小时会话，不产生 M4c Pass。

## 2. 受控结束握手

`ProductResourceTelemetryServer` 的 `complete` 请求现在按固定顺序执行：

1. 释放正式 `ProductThumbnailWorkerLifecycleController`；
2. 复读新的匿名 telemetry sequence；
3. 只在正式 worker/Profile 均为 `0/0` 且系统删除 API 已确认临时 Profile 删除时返回终态快照；
4. 结束同用户、单连接的受控管道会话。

该命令只存在于 DesktopHost 与资源会话 acknowledgement 双 opt-in 的临时 telemetry server。普通启动没有该 server；命令不进入文件、Shell、DesktopHost Explicit、系统设置或用户配置路径。App 正常关闭继续幂等释放 worker。

采集脚本把终态快照写入 `cleanupTelemetry`，并把 `ownedProfileCountAtEnd` 纳入既有 `partialProcessBudgetsWithinLimits`。结束握手失败不会跳过 App、worker、管道或环境变量清理，只会留下不可通过复审的空终态。

## 3. 独立复审器

新增 `eng/Review-LongGridResourceStabilityEvidence.ps1`。它只读取显式 JSON，并要求调用者提供期望 commit；可选输出只能写入已存在目录中的新文件，不能覆盖来源证据。

复审器不信任来源 `summary` 作为资源结论，而是从原始样本重新计算并复核：

- schema、purpose、slice、匿名 operator、commit 和 Pending blocker；
- 24 小时时间戳、1441 预期样本、至少 98% 覆盖和最大 180 秒间隔；
- 单调 elapsed time 与 telemetry sequence；
- 每个运行样本 worker/Profile `1/1`、无敏感字段声明；
- 预热后状态修订零漂移；
- App/worker private bytes、handle、thread、窗口和 UIA 固定预算；
- worker 零重启、零孤儿，以及结束快照 worker/Profile `0/0` 和临时 Profile 删除确认；
- 来源仍为 `PendingReal24HourEvidenceReview` 且不能自批 M4c Pass。

满足全部条件只输出 `EligibleForM4cDecision`，仍要求人工审计裁决；任何缺口输出 `RejectedEvidence` 和有限失败代码。`-ValidateOnly` 使用合成的有效/无效 24 小时样本自测接受与拒绝路径，不启动产品进程、不读取现场证据、不写文件。

## 4. 验收结果与边界

- telemetry 专项：5/5 通过；覆盖未知请求、运行快照和 complete 后 `0/0` 终态；
- 会话合同 `-ValidateOnly`：通过，继续声明 live evidence Pending、`canProduceM4cPass=false`；
- 复审合同 `-ValidateOnly`：通过，有效合成证据可判定、23 小时和 worker 计数异常被拒绝；
- 当前终端首个 `dotnet test` 因 PATH 优先命中无 SDK 的 x86 host 未执行；改用已安装的 x64 SDK 后专项通过，未把首次未运行记录成通过；
- 真实 24 小时会话尚未执行，M4c、M4-ready、RC、外部 Issue 和 ADR 状态全部不变；
- 不读取桌面文件内容，不写入、移动、重命名或删除桌面文件，不接入缩略图 UI，不扩大 AppContainer Capability。

## 5. 下一步

从本切片合并后的同一 `main` commit，在专用测试账户执行完整 24 小时会话；不得在运行中修改代码、预算或配置。完成后用独立复审器核对同一 commit。只有输出 `EligibleForM4cDecision` 且人工复核来源环境与异常记录后，才能另开文档切片决定 M4c；否则保留原始失败事实并从修复后的新 commit 重新运行。
