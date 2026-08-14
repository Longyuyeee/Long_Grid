# M4c2 正式 App 资源长稳会话运行手册

当前入口已进入 **M4c2b1 匿名状态遥测**。正式缩略图 worker 尚未接入，因此即使运行满 24 小时，也只能得到 `PendingFormalThumbnailWorkerIntegration`，不得登记为 M4c Pass。

## 运行前

1. 使用无个人桌面内容的专用 Windows 测试账户；
2. 准备匿名 Long方格工作区并确认可恢复；
3. 关闭所有既有 `LongGrid.App`；
4. 创建一个专用于本轮、现有且为空的证据目录；
5. 保持设备供电，关闭会自动重启或休眠的计划；不要改变系统安全策略；
6. 记录当前 commit；任何代码、配置或预算变化都必须从头重跑。

先验证合同：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\eng\Start-LongGridResourceStabilitySession.ps1 `
  -ValidateOnly
```

在 M4c2b 关闭 blocker 之前，如需采集部分趋势：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\eng\Start-LongGridResourceStabilitySession.ps1 `
  -OperatorId O1 `
  -EvidenceDirectory C:\LongGrid-M4c2-Evidence\Run-001 `
  -DedicatedTestAccountConfirmed `
  -PreparedAnonymousWorkspaceConfirmed `
  -RecoveryPlanConfirmed `
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
- 当前版本必须看到 `formalStateRevisionTelemetryAvailable=true` 和 `FormalThumbnailWorkerNotIntegrated`；worker blocker 缺失反而是合同失败；
- 删除 blocker、放宽预算或手工把结果改为 Pass 都会使证据无效。
