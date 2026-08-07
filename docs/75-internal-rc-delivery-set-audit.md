# Long方格内部 RC 交付集合与干净检出审计

> 审计日期：2026-08-07
>
> 范围：一键内部 RC 入口、ZIP/MSIX/SBOM 聚合、同提交与哈希绑定、失败失效、干净检出 CI、发布边界
>
> 结论：**内部 unsigned Developer Preview 已具备单一聚合入口和最终成功标记；它不是签名 RC、安装器或公开 Release**

## 1. 缺口与决策

此前三个底层入口已经分别验证便携 ZIP、unsigned MSIX 和 SPDX 2.2，但调用者仍需自行判断：

- 三份产物是否来自同一 Git 提交；
- sidecar 是否仍匹配实际文件；
- SBOM subject hash 是否指向这一份 MSIX；
- 生命周期和签名门禁是否仍处于 Pending/Blocked；
- 是否有任何一层错误地变成 signed/installable/distribution-approved。

本阶段新增 `eng/Build-LongGridReleaseCandidate.ps1` 作为内部 RC 交付集合的唯一推荐入口。它编排既有、已审计的底层脚本，不复制打包实现，也不创建新的包格式。

## 2. 执行链

默认本地调用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Build-LongGridReleaseCandidate.ps1 `
  -PortableVersion 0.1.0-rcdev `
  -PackageVersion 0.1.0.0
```

入口执行：

1. 要求 Windows、干净 Git 工作树和可解析的 40 位源码提交；
2. 运行 MSIX lifecycle `-ValidateOnly`，要求不启动进程、不改包状态、不信任 unsigned 包，并保持 `PendingSignedPackageAndDisposableWindowsProfile`；
3. 运行 protected signing `-ValidateOnly`，要求 PR/main 无签名权、真实签名未实现且分发未批准；
4. 在开始构建前删除当前版本旧的聚合成功标记和 sidecar；
5. 调用 `Pack-LongGrid.ps1` 生成便携 ZIP；默认执行其 restore/format/Release build/test/coverage/vulnerability 门禁；
6. 调用 `Pack-LongGridMsix.ps1` 复用同提交 ZIP 并生成/验证 unsigned MSIX；
7. 调用 `New-LongGridSbom.ps1` 复用同提交 MSIX 并生成/官方验证 SPDX 2.2；
8. 重新计算 ZIP、MSIX、SBOM SHA-256，并逐字复核各自 sidecar；
9. 复读包内 manifest、MSIX 外部 manifest 和 SBOM evidence，要求版本、源码提交、MSIX subject hash 与否定性状态全部一致；
10. 最后才输出聚合 evidence 和其 SHA-256 sidecar。

`-ValidateOnly` 只复核编排合同，不构建、不启动、不安装。`-SkipQualityGates` 只允许调用者已经完成等价或更强门禁时使用；聚合 evidence 会如实记录 `prevalidated-by-caller`，不会声称本次调用重新执行了质量门禁。

## 3. 聚合成功标记

输出：

- `artifacts/LongGrid-<packageVersion>-win-x64-internal-rc-evidence.json`
- 同名 `.sha256`

核心字段包括：

| 字段 | 语义 |
| --- | --- |
| `candidateType` | 固定 `internal-unsigned-developer-preview` |
| `sourceCommit` | 三类产物共同绑定的 Git 提交 |
| `execution.qualityGateMode` | 本次执行门禁，或由 CI/调用者预先完成 |
| `artifacts.portable.sha256` | 确定性便携 ZIP 的实际哈希 |
| `artifacts.msix.sha256` | 当前 unsigned MSIX 的实际哈希 |
| `artifacts.msix.deterministicLayout` | 固定为已验证的语义布局确定性 |
| `artifacts.msix.byteReproducible` | 如实记录 MakeAppx 容器是否字节相同 |
| `artifacts.sbom.sha256` | 当前 SPDX 文件实际哈希 |
| `artifacts.sbom.inventoriedFileCount` | 官方验证的 MSIX 布局文件数 |
| `gates.lifecycleEvidence` | 必须仍等待签名包和可抛弃 Windows profile |
| `gates.signingState` | 必须仍等待正式 Publisher/证书/环境 |
| `signed/installable/distributionApproved` | 全部强制 `false` |

SBOM 带生成时间和 namespace 标识，MakeAppx 容器也可能带 ZIP 元数据差异，因此聚合 evidence 不声称整个集合逐字节可重复；它分别记录便携 ZIP 字节确定性、MSIX 解包布局确定性和 SBOM 的官方内容验证。

## 4. 失败与陈旧证据处理

聚合 evidence 是唯一“整套链路通过”标记。脚本在任何构建前先删除该版本的旧 evidence/sidecar；此后任一子步骤、hash、版本、提交或状态断言失败，都不会重新生成成功标记。

底层 ZIP/MSIX/SBOM 文件可能因调试而存在，但不能仅凭文件存在判定 RC 集合通过。调用者必须复核聚合 evidence 的 SHA-256、`sourceCommit` 和否定性状态。

脚本只删除 `artifacts/` 下两个精确的、自有成功标记文件，不清空目录，不接触用户文件或证书。

## 5. 干净检出 CI

PR/main CI 使用 GitHub 托管 Windows runner 的全新 checkout，先执行完整工程门禁和 Windows 探针，再以：

```powershell
./eng/Build-LongGridReleaseCandidate.ps1 `
  -PortableVersion 0.1.0-ci `
  -PackageVersion 0.1.0.0 `
  -SkipQualityGates `
  -NoRestore `
  -NoToolRestore
```

真实构建整套集合。这里的 skip 只复用同一 job 已完成的 restore/format/build/test/coverage/probes/vulnerability 和工具恢复；聚合入口仍重新验证全部包、哈希、提交与安全边界。

CI 只上传 TRX/Cobertura。ZIP、MSIX、SBOM 和聚合 evidence 不作为 Actions artifact 或 GitHub Release 上传，避免把内部 unsigned 产物误当成可分发构建。

## 6. 需求对齐

| 原始需求 | 本切片对齐 | 当前边界 |
| --- | --- | --- |
| 一键打包 | 一个命令生成并交叉验证 ZIP/MSIX/SBOM/证据集合 | 仍是内部 unsigned Developer Preview |
| 审计与可追溯 | 源码提交、三份 SHA-256、版本和门禁状态统一复读 | 没有签名信任链和安装证据 |
| 现代 UI/L+方格品牌 | 聚合真实现有 self-contained WinUI/MSIX payload | 不代表视觉人工矩阵完成 |
| 桌面管理安全 | 明确记录 DesktopHost execution disabled、零安装状态修改 | 真实文件/窗口执行仍未开放 |
| 推送与主干对齐 | PR 与 main 都在干净 runner 执行唯一入口 | 不上传 Release |

## 7. 后续方向

没有正式 Publisher、证书、许可证和可抛弃 Windows 环境时，交付机械链到这里应停止扩张。下一步应优先执行：

1. #19 键鼠/触控/拖放/Narrator/Win+D/全屏/Explorer 重启人工矩阵；
2. #20 多显示器/DPI/旋转/拔插/投影/睡眠/RDP 硬件矩阵；
3. #23 五名参与者首次整理可用性测试；
4. #24 独立真实卷持久化边界；
5. 获得正式发布输入后再建立受保护签名 job 和 signed install/upgrade/uninstall/rollback 矩阵。

这些外部证据和正式发布输入未关闭前，不得把 `internal-rc-evidence.json` 解读为产品 RC 已完成。
