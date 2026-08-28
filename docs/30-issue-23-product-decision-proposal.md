# Issue #23 首发产品决策记录

审计日期：2026-08-04

基线：`main` / `fa40012`（PR #79 已合入）

状态：**D23-01–D23-10 owner-approved / D23-11 deferred / D23-12 engineering-unblock approved / Usability results pending**

## 1. 目的与约束

本记录把 Issue #23 分散在 PRD、ADR、配置合同、人工矩阵和探针报告中的待决项收敛成稳定编号。项目负责人于 2026-08-04 批准 D23-01–D23-10，并明确要求当前开发跳过许可证选择；D23-11 因此延期到正式分发或接受外部贡献之前。本记录不伪造 P1–P5 结果。

在五人测试完成前：

- Issue #23 保持 OPEN；
- ADR-0001 保持 `Proposed`；
- 不添加 `LICENSE`；当前可以继续开发和未签名内部验证，但正式分发与外部贡献入口继续受 D23-11 阻断；
- 不把 `ConditionalPass`、UIA 或 CI 写成真实可用性通过；
- 不扩展未经批准的文件移动、Provider、Windows build 或 CPU 架构矩阵。

2026-08-13 新增 D23-12：负责人确认当前无法安排五人测试，批准把该证据从“工程编码入口”延期到“内部 RC 前证据汇合门”。这不产生任何 P1～P5 结果，不关闭 Issue #23，不接受可用性风险，也不允许 M4/RC/分发绕过五人测试；工程只可按 Stage 129 的默认关闭和可回滚边界继续。

## 2. 建议决策

| ID | 决策 | 已批准范围 | 当前证据 | 批准状态 |
|---|---|---|---|---|
| D23-01 | 首版整理模式 | **仅安全引用**；托管移动和真实文件自动整理后移 | Core v1 只允许 `reference`；删除容器与改变归属不触碰文件；#21 的真实移动专用矩阵未完成 | Approved 2026-08-04 |
| D23-02 | 本地与账户 | **完全本地、无账户、无持续网络轮询** | PRD 与隐私基线一致；现有 App、Core 和探针不需要账户 | Approved 2026-08-04 |
| D23-03 | Folder Portal | **移至 P1**；首版只提供文件夹引用入口 | Portal 权限、导航和真实目录边界尚无产品切片证据 | Approved 2026-08-04 |
| D23-04 | Windows 范围 | **首个技术预览先声明 Windows 11 x64**；Windows 10 在 #19/#20 和安装矩阵通过前不对外承诺 | 当前主要实证来自 22621/26100 x64；项目虽声明更低 TargetPlatformMinVersion，但声明不等于运行证据 | Approved 2026-08-04; matrix pending |
| D23-05 | CPU 架构 | **首发 x64；ARM64 移至 P1** | App 和 CI 当前均为 `win-x64`；ARM64 无真实构建、安装、Shell 或性能证据 | Approved 2026-08-04 |
| D23-06 | 安装渠道 | **MSIX 作为首选正式渠道，同时保留企业离线 MSIX 验证；当前 unpackaged 启动器仅用于开发** | 架构文档推荐 MSIX；仓库尚无签名、升级、卸载或回滚证据 | Approved 2026-08-04; packaging pending |
| D23-07 | 文件图像范围 | **所有项目保底类型图标；只有隔离 Worker 在当前机器成功时显示实时缩略图，失败必须安全回退**；首发不承诺 Office/PDF、HEIC/AVIF、云/网络或第三方 Provider | #22 已证明 AppContainer、安全拒绝和回退，但成功格式受 build/provider 影响 | Approved 2026-08-04 |
| D23-08 | 原生桌面融合 | **接受原生图标可能继续存在，不依赖 undocumented WorkerW/Progman 嵌入** | 架构与 ADR 已禁止未文档化嵌入；安全引用不会替换 Explorer | Approved 2026-08-04 |
| D23-09 | 性能预算 | **保留 PRD 目标作为首片门禁，不升级为发布 SLA**：空闲 CPU `<0.2%`/5 分钟、空闲专用内存 `<120 MB`、唤起 P95 `<300 ms`、100 项恢复 P95 `<1 s`；缩略图探针预算仅作隔离回归 | 当前 500 请求只覆盖合成样本；正式渲染、真实 500 项和长时间常驻尚未验证 | Approved 2026-08-04; product measurement pending |
| D23-10 | ADR-0001 | **继续 Proposed；目标方向为 WinUI 3 App + 独立原生 DesktopHost，待 #19/#20/安装/可用性证据后再决定 Accepted 或 Revised** | 自动探针支持独立宿主方向，但真实输入、Narrator、动态显示和安装仍 Pending | Approved 2026-08-04; evidence pending |
| D23-11 | 许可证与商业模式 | **当前开发跳过许可证选择；正式分发或接受外部贡献前重新决策** | 仓库根目录无 `LICENSE`，GitHub 未识别许可证 | Deferred by owner 2026-08-04 |
| D23-12 | 外部证据排期 | **五人测试延期到内部 RC 前证据汇合门；工程可继续到 M4-ready** | 当前无法安排参与者；P1 未执行且无结果 | Approved 2026-08-13 |

## 3. 对后续 Issue 的影响

D23-01“首版仅安全引用”批准后：

- #21 的托管移动、跨卷、Explorer 撤销和真实文件取消矩阵移出首版发布阻断项，但保持后续里程碑，不得改写为 Pass；
- v1 配置继续只允许 `reference`，无需为未发布行为提前创建 v2；
- UI 仍可解释真实移动概念，但必须保持禁用且不得暗示即将执行。

D23-04、D23-05 和 D23-07 批准后：

- #22 只复跑 Windows 11 x64 的批准 build/渠道矩阵；
- AppContainer 提取失败时显示类型图标属于合格回退，不允许主进程现场提取；
- Windows 10、ARM64、Office/PDF、HEIC/AVIF 成功路径、云/网络和第三方 Provider 转入后续兼容里程碑。

这些范围变化是重新定义首发出口，不是把未执行证据伪装成通过。

## 4. 不可由自动化完成的证据

- 五位未参与设计的 P1–P5 在同一 commit 上完成无提示任务；
- D23-11 在正式分发或接受外部贡献前记录许可证/商业模式选择和必要复核；
- #19/#20 记录批准支持范围内的真实输入、Narrator、显示和会话结果；
- 安装、升级、卸载、回滚以及正式产品性能矩阵产生原始证据。

## 5. 批准记录模板

| ID | 判定 | 修订内容或理由 | 日期 | 批准人代号 |
|---|---|---|---|---|
| D23-01–D23-10 | Approve | 按第 2 节范围执行；未完成矩阵继续保留 Pending | 2026-08-04 | ProjectOwner |
| D23-11 | Defer | 当前开发跳过；正式分发/外部贡献前重新处理 | 2026-08-04 | ProjectOwner |

本轮同步 PRD、路线图、ADR-0001、Issue #21–#24 和安装/支持矩阵边界。范围批准本身不能关闭 Issue #23；P1–P5 真实结果仍是独立出口条件。

## 6. D23-11 许可证决策事实包（2026-08-28）

本节只减少负责人决策所需的信息差，不构成法律意见，也不替负责人选择许可证或商业模式。GitHub 官方说明：公开仓库若无许可证，默认版权规则适用；SPDX 2.2.2 官方规范说明 `NOASSERTION` 可能表示没有尝试判断、无法客观判断或有意不提供信息，不能把它解释成“已完成许可证清算”。参考：[GitHub Licensing a repository](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository)、[SPDX 2.2.2 Package Information](https://spdx.github.io/spdx-spec/v2.2.2/package-information/)。

真实盘点结果：

- GitHub License API 返回 404；根目录没有 `LICENSE`、`COPYING` 或 `NOTICE`，与 D23-11 Deferred 一致；
- 仓库有 782 个 tracked files、0 个 Git submodule；GitHub Contributors API 只返回 `Longyuyeee`，Git 历史存在同一项目负责人的两个 author identity 表示，但这不能替代版权归属确认；
- App/Test 的锁定 `project.assets.json` 共解析 30 个唯一 NuGet 包，逐一读取全局包缓存中的实际 `.nuspec`，缺失 license metadata 为 0：8 个 MIT expression、7 个 Apache-2.0 expression、1 个 xUnit license URL、1 个 Windows SDK license URL、12 个包内 `LICENSE.txt/license.txt` 和 1 个 `sdk_license.txt`；文件型 Microsoft 条款必须按实际再分发内容和正式渠道由负责人/专业人员复核，不能只按包名归类；
- 固定 SBOM Tool `microsoft.sbom.dotnettool 4.1.5` 的包元数据为 MIT；当前生成的 SPDX 2.2 SBOM 虽已验证 805/805 文件和 17 个 package，但 17 个 package 的 `licenseDeclared` 与 `licenseConcluded` 全部为 `NOASSERTION`，因此当前 SBOM 是组件/哈希清单，不是已完成的许可证扫描；
- tracked 源码/探针/资产中未发现 vendored LICENSE/NOTICE 或第三方版权头；品牌资产文档声明为原创概念且商标检索仍 Pending。两者都只是仓库扫描结果，不等于法律清算结论。

D23-11 必须由负责人明确选择并记录以下一种路径，而不是由工程脚本推断：

1. 继续 Deferred：保持无公开分发、无外部贡献入口，当前 unsigned artifact 只作内部 CI 审计；
2. 开源发布：批准具体 SPDX license expression、版权主体/年份、外部贡献规则，以及第三方 NOTICE/再分发材料；
3. 专有或 source-available：由专业人员提供实际条款、分发授权、隐私/支持边界和第三方 NOTICE 方案。

无论选择哪条路径，正式分发前都必须把负责人批准、根许可证/条款、第三方 notice、SBOM license 字段或配套 license report、品牌/素材权利和 signed lifecycle evidence 一并复核。当前状态继续为 `Deferred / DistributionBlocked`，不得因 30/30 包元数据可读或 SBOM validation Pass 自动升级。

2026-08-28 Stage 220 后续工程更新：30 包盘点已固化为覆盖全解决方案 20 个项目的确定性 CI/RC 门禁，锁定包身份、license expression/file/URL、文件型许可证哈希和 NOTICE/third-party 文件哈希；实际报告仍明确 `PendingOwnerReviewAndNotice / distributionApproved=false`。该门禁降低后续依赖漂移风险，但不改变本节三条负责人决策路径，也不替代版权归属、兼容性或最终 NOTICE 审核。
