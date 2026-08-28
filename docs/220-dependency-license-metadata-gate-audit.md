# Stage 220：依赖许可证元数据门禁审计

> 日期：2026-08-28
> 结论：**20 个解决方案项目 / 30 个唯一锁定 NuGet 包的许可证元数据与 NOTICE 文件指纹已进入 CI 和内部 unsigned RC；法律兼容性、NOTICE 内容和分发批准仍为 Pending**

## 1. 开发目标与边界

上一阶段真实盘点发现：Microsoft SBOM Tool 已能验证最终 MSIX 的组件和 805 个文件哈希，但 SPDX package 的 license 字段仍全部为 `NOASSERTION`；一次性读取 NuGet `.nuspec` 得到 30/30 元数据完整，也不能保证后续依赖升级不会静默改变许可证或 NOTICE。Stage 220 的目标是把这个工程差异固化为确定性、fail-closed 门禁，而不是替负责人选择仓库许可证或作法律兼容性判断。

门禁覆盖 `LongGrid.sln` 全部 20 个项目的真实 `obj/project.assets.json`，所以同时包含产品、测试、工具和探针依赖；它比最终 MSIX SBOM 的 payload 范围更宽，两者不能互相替代。

## 2. 实现

- `packaging/release/dependency-license-contract.json` 固定项目数、包数、包身份指纹和许可证元数据指纹；任何依赖集合、license expression/file/URL、`requireLicenseAcceptance`、文件型许可证内容或 NOTICE/third-party 文件变化都会要求显式复核基线。
- `eng/Test-LongGridDependencyLicenses.ps1` 从已恢复的真实 assets 定位 NuGet 包，使用禁用 DTD/外部解析的 XML reader 读取实际 `.nuspec`；文件声明必须保持在包目录内并真实存在。
- 报告按包身份排序，记录许可证类型/值、文件 SHA-256、补充 URL、接受标记和 NOTICE/third-party 文件 SHA-256；UTF-8/LF 输出不含时间与机器路径，可重复生成相同哈希。
- `eng/Test-LongGridDependencyLicenseGate.ps1` 连续执行两次真实扫描验证报告确定性，再把预期包数从 30 人为改为 31，要求非零退出、精确差异且不得写出负向成功报告。
- PR/main CI 在漏洞门禁之后独立运行该测试；`Build-LongGridReleaseCandidate.ps1` 在 portable/MSIX/SBOM 可能触发的 RID restore 全部结束后再次扫描最终 restored assets，把报告文件、SHA-256、20/30 计数和 clearance 状态绑定进聚合 evidence。

报告明确固定 `copiesLicenseFiles=false`、`decidesCompatibility=false`、`clearanceStatus=PendingOwnerReviewAndNotice`、`distributionApproved=false`。它不生成 `LICENSE`、不复制第三方条款、不生成最终 NOTICE，也不根据 MIT/Apache/Microsoft 条款名称自动批准分发。

## 3. Expected / Actual / Difference / Correction

Expected：20 个项目和 30 个唯一锁定包均能从真实缓存复读许可证元数据；连续报告哈希相同；任何基线漂移阻断；RC 复读报告哈希且继续不可分发。

Actual：真实分类为 expression 15、file 13、URL 2，6 个包携带 NOTICE/third-party 文件；两次报告 SHA-256 均为 `b2ab09763fb2c38fc04292bd18347092fdac12b426225040cdb9c45e30399cfe`。初次以全零占位指纹执行时按预期报告 package identity 与 license metadata 两项真实差异；写入审计后的实际基线后通过。负向测试返回 `packageCount expected=31 actual=30` 且不写报告。Release build 为 0 warning / 0 error，1,381/1,381 测试通过、0 跳过。

从干净提交 `48be0bc` 执行完整 RC：portable ZIP、原生 ExplorerCommand 200 次 COM、unsigned MSIX、SPDX 2.2 和许可证报告全部通过；SBOM 官方验证 805/805 文件，聚合 evidence 复读许可证报告 SHA-256，并继续输出 `PendingOwnerReviewAndNotice / signed=false / installable=false / distributionApproved=false`。最终 PR CI 和合并结果以 PR 记录为准。

Difference：没有产品行为差异；发现并关闭的是“依赖许可证事实只存在于人工盘点、后续漂移不能自动阻断”的供应链差异。Correction：新增确定性合同、真实/负向测试、CI 门禁和 RC 哈希绑定；没有把元数据完整误写成许可证兼容或 NOTICE 清算完成。

## 4. 目标与需求审计

开发目标审计：依赖许可证元数据漂移已能在 CI/RC fail-closed，且成功证据可复读，完成。负责人对仓库许可证、商业模式、版权主体、第三方 NOTICE 和实际分发权的决策仍未完成。

需求对齐审计：实现遵守锁定依赖、确定性报告、失败不产出成功标记、普通 CI 无签名/分发权限和真实测试要求；没有修改产品运行时、用户文件、包缓存、Publisher、证书或签名环境。#19/#20/#24 人工/专用环境证据、#23 D23-11 和 #274 托管签名输入继续保持 Open/Pending。
