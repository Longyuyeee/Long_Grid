# Stage 104：CI 内部 RC 运行时恢复确定性审计

日期：2026-08-12

触发证据：Stage 103 文档 PR #154 的全部格式、构建、636 项测试、覆盖率、持久化、文件安全、缩略图隔离与依赖漏洞门禁通过，但内部 RC 步骤以 `NETSDK1112` 失败：`Microsoft.NETCore.App.Runtime.win-x64` 未下载。

## 1. 根因

CI 先执行 `dotnet restore LongGrid.sln --locked-mode`，随后给聚合 RC 入口传入 `-NoRestore`。普通 solution restore 足以支持框架依赖构建与测试，但不保证准备 `win-x64`、`.NET self-contained` 和 `WindowsAppSDKSelfContained=true` 发布所需的专用 runtime pack。

此前成功运行依赖 runner/NuGet 缓存中恰好已有 runtime pack。缓存未命中后，同一流程立即失败，因此原合同不是干净检出的确定性发布链。Stage 103 只修改 Markdown，与该失败没有代码因果关系。

## 2. 修复

- CI 聚合 RC 调用移除 `-NoRestore`；
- `-SkipQualityGates` 仍复用同一 job 已完成的格式、构建、测试、覆盖率、探针和漏洞检查；
- `-NoToolRestore` 仍复用前置固定工具恢复；
- `Pack-LongGrid.ps1` 继续在 publish 前执行带 `--runtime win-x64` 和 `WindowsAppSDKSelfContained=true` 的专用恢复，并为 publish 使用 `--no-restore`；
- 新增 `Test-LongGridCiReleaseRestore.ps1`，检查 CI 不得跳过 RC 专用恢复，并检查专用恢复必须先于 publish。

## 3. 边界

该修复不改变应用代码、功能、包内容、签名状态、发布权限或依赖版本。PR/main 仍不持有证书、签名、安装和分发权限；生成物仍为内部 unsigned Developer Preview。

`-NoRestore` 参数继续保留给已经由调用者明确完成等价 RID/self-contained 恢复的受控本地诊断，但 CI 的普通 solution restore 不满足这一前提。

## 4. 需求对齐

修复直接支持“一键打包必须在干净机器可复现并在缺少依赖时明确失败”的原始要求，也避免用缓存偶然性掩盖发布缺口。它不改变 Stage 103 的后续产品方向；PR/main CI 恢复绿灯后，下一产品切片仍为 A1 DesktopHost composition root 与默认关闭 Feature Flag。

## 5. 收口条件

1. 本地 RC 恢复源码合同通过；
2. PR #154 从干净 runner 完成完整内部 RC；
3. 合并后的 main CI 完成相同门禁；
4. 任何失败按新日志重新审计，不以重跑作为修复。
