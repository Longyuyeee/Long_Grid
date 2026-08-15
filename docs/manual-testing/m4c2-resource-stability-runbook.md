# M4c2 正式 App 资源长稳会话运行手册

当前入口已进入 **M4c2c 复审就绪**。匿名状态遥测、正式 idle worker/Profile 和结束时 `0/0` 清理快照已接入，但真实 24 小时证据尚未执行和复审；即使运行满 24 小时，也只能得到 `PendingReal24HourEvidenceReview`，不得直接登记为 M4c Pass。

## 运行前

1. 使用无个人桌面内容的专用 Windows 测试账户，并确认用户与 Public Desktop 均为空；
2. 准备匿名 Long方格工作区并确认可恢复；
3. 关闭所有既有 `LongGrid.App`；
4. 创建一个专用于本轮、现有且为空的证据目录；
5. 保持设备供电，关闭会自动重启或休眠的计划；不要改变系统安全策略；
6. 记录当前 commit；任何代码、配置或预算变化都必须从头重跑。

先验证合同：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\eng\Test-LongGridResourceStabilityEnvironment.ps1 `
  -ValidateOnly

powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\eng\Start-LongGridResourceStabilitySession.ps1 `
  -ValidateOnly

powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\eng\Review-LongGridResourceStabilityEvidence.ps1 `
  -ValidateOnly
```

创建空白专用证据目录后，先执行只读环境预检。它不会创建账户、改变电源/更新设置、清理桌面、启动 VM 或产品进程，也不输出路径、名称、内容、PID 或身份：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\eng\Test-LongGridResourceStabilityEnvironment.ps1 `
  -EvidenceDirectory C:\LongGrid-M4c2-Evidence\Run-001 `
  -DedicatedTestAccountConfirmed `
  -PreparedAnonymousWorkspaceConfirmed `
  -RecoveryPlanConfirmed `
  -ContinuousPowerConfirmed `
  -NoAutomaticRestartConfirmed
```

只有输出 `ReadyForM4c2cSession` 才能启动正式会话。`RejectedEnvironment` 会返回有限失败码；操作者必须在会话外修复环境并重新预检，不得通过编辑脚本、隐藏桌面条目或放宽判定继续。

在 M4c2c 正式复审之前，如需采集待审趋势：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\eng\Start-LongGridResourceStabilitySession.ps1 `
  -OperatorId O1 `
  -EvidenceDirectory C:\LongGrid-M4c2-Evidence\Run-001 `
  -DedicatedTestAccountConfirmed `
  -PreparedAnonymousWorkspaceConfirmed `
  -RecoveryPlanConfirmed `
  -ContinuousPowerConfirmed `
  -NoAutomaticRestartConfirmed `
  -DesktopHostOptInConfirmed
```

## 运行中

- 不打开个人文件或导入个人配置；
- 不启动第二个 Long方格实例；
- 不手动终止脚本创建的 App；
- 记录断电、休眠、更新、RDP、显示器变化或人工交互的时间，但不要把身份、路径或内容写入证据；
- App 退出、采样停止或环境失去控制时，本轮立即视为 Inconclusive。

## 结束与复核

- 入口只关闭自己创建的 App；如发现其他 Long方格进程，不得由本会话清理；
- 检查证据 JSON 的 commit、24 小时持续时间、样本完整性和固定预算；
- 当前版本必须看到 `formalStateRevisionTelemetryAvailable=true`、`formalThumbnailWorkerIntegrated=true`、worker/Profile `1/1`，且样本中只有一个正式 worker；
- 会话结束握手必须返回 `cleanupTelemetry`，其中正式 worker/Profile 为 `0/0` 且 `OwnedProfileDeletionConfirmed=true`；App 退出后 worker 孤儿也必须为 0；
- 删除 blocker、放宽预算或手工把结果改为 Pass 都会使证据无效。

使用合并后执行会话的同一 commit 复审，不覆盖原证据：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\eng\Review-LongGridResourceStabilityEvidence.ps1 `
  -EvidencePath C:\LongGrid-M4c2-Evidence\Run-001\long-grid-m4c2b2-<session>.json `
  -ExpectedCommit <same-main-commit> `
  -ReviewOutputPath C:\LongGrid-M4c2-Evidence\Run-001\review.json
```

`EligibleForM4cDecision` 只表示证据结构、趋势和固定预算可进入人工裁决，不等于 M4c Pass。`RejectedEvidence` 必须保留失败代码；修复后从新 commit 重跑完整 24 小时。
