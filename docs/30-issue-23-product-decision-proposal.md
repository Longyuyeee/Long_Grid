# Issue #23 首发产品决策提案

审计日期：2026-08-04

基线：`main` / `69036df`（PR #78 已合入）

状态：**Proposal / Owner approval required / Usability results pending**

## 1. 目的与约束

本提案把 Issue #23 分散在 PRD、ADR、配置合同、人工矩阵和探针报告中的待决项收敛成稳定编号。它提供工程建议和影响分析，但不代表负责人批准，也不伪造 P1–P5 结果。

在负责人确认和五人测试完成前：

- Issue #23 保持 OPEN；
- ADR-0001 保持 `Proposed`；
- 不添加 `LICENSE`、不发布安装包、不声明正式支持范围；
- 不把 `ConditionalPass`、UIA 或 CI 写成真实可用性通过；
- 不扩展未经批准的文件移动、Provider、Windows build 或 CPU 架构矩阵。

## 2. 建议决策

| ID | 决策 | 工程建议 | 当前证据 | 批准前状态 |
|---|---|---|---|---|
| D23-01 | 首版整理模式 | **仅安全引用**；托管移动和真实文件自动整理后移 | Core v1 只允许 `reference`；删除容器与改变归属不触碰文件；#21 的真实移动专用矩阵未完成 | Pending owner approval |
| D23-02 | 本地与账户 | **完全本地、无账户、无持续网络轮询** | PRD 与隐私基线一致；现有 App、Core 和探针不需要账户 | Pending owner approval |
| D23-03 | Folder Portal | **移至 P1**；首版只提供文件夹引用入口 | Portal 权限、导航和真实目录边界尚无产品切片证据 | Pending owner approval |
| D23-04 | Windows 范围 | **首个技术预览先声明 Windows 11 x64**；Windows 10 在 #19/#20 和安装矩阵通过前不对外承诺 | 当前主要实证来自 22621/26100 x64；项目虽声明更低 TargetPlatformMinVersion，但声明不等于运行证据 | Pending owner approval and matrix |
| D23-05 | CPU 架构 | **首发 x64；ARM64 移至 P1** | App 和 CI 当前均为 `win-x64`；ARM64 无真实构建、安装、Shell 或性能证据 | Pending owner approval |
| D23-06 | 安装渠道 | **MSIX 作为首选正式渠道，同时保留企业离线 MSIX 验证；当前 unpackaged 启动器仅用于开发** | 架构文档推荐 MSIX；仓库尚无签名、升级、卸载或回滚证据 | Pending owner approval and packaging evidence |
| D23-07 | 文件图像范围 | **所有项目保底类型图标；只有隔离 Worker 在当前机器成功时显示实时缩略图，失败必须安全回退**；首发不承诺 Office/PDF、HEIC/AVIF、云/网络或第三方 Provider | #22 已证明 AppContainer、安全拒绝和回退，但成功格式受 build/provider 影响 | Pending owner approval and approved matrix |
| D23-08 | 原生桌面融合 | **接受原生图标可能继续存在，不依赖 undocumented WorkerW/Progman 嵌入** | 架构与 ADR 已禁止未文档化嵌入；安全引用不会替换 Explorer | Pending owner approval |
| D23-09 | 性能预算 | **保留 PRD 目标作为首片门禁，不升级为发布 SLA**：空闲 CPU `<0.2%`/5 分钟、空闲专用内存 `<120 MB`、唤起 P95 `<300 ms`、100 项恢复 P95 `<1 s`；缩略图探针预算仅作隔离回归 | 当前 500 请求只覆盖合成样本；正式渲染、真实 500 项和长时间常驻尚未验证 | Pending owner approval and product measurement |
| D23-10 | ADR-0001 | **继续 Proposed；目标方向为 WinUI 3 App + 独立原生 DesktopHost，待 #19/#20/安装/可用性证据后再决定 Accepted 或 Revised** | 自动探针支持独立宿主方向，但真实输入、Narrator、动态显示和安装仍 Pending | Pending evidence and owner decision |
| D23-11 | 许可证与商业模式 | **必须由负责人选择并完成法律复核；工程侧不代填** | 仓库根目录无 `LICENSE`，GitHub 未识别许可证；当前不得视为可分发开源软件 | Blocking owner decision |

## 3. 对后续 Issue 的影响

若负责人批准 D23-01“首版仅安全引用”：

- #21 的托管移动、跨卷、Explorer 撤销和真实文件取消矩阵移出首版发布阻断项，但保持后续里程碑，不得改写为 Pass；
- v1 配置继续只允许 `reference`，无需为未发布行为提前创建 v2；
- UI 仍可解释真实移动概念，但必须保持禁用且不得暗示即将执行。

若负责人批准 D23-04、D23-05 和 D23-07：

- #22 只复跑 Windows 11 x64 的批准 build/渠道矩阵；
- AppContainer 提取失败时显示类型图标属于合格回退，不允许主进程现场提取；
- Windows 10、ARM64、Office/PDF、HEIC/AVIF 成功路径、云/网络和第三方 Provider 转入后续兼容里程碑。

这些范围变化是重新定义首发出口，不是把未执行证据伪装成通过。

## 4. 不可由自动化完成的证据

- 五位未参与设计的 P1–P5 在同一 commit 上完成无提示任务；
- 负责人对 D23-01–D23-11 逐项给出 `Approve`、`Revise` 或 `Reject`；
- D23-11 记录许可证/商业模式选择和法律复核责任人；
- #19/#20 记录批准支持范围内的真实输入、Narrator、显示和会话结果；
- 安装、升级、卸载、回滚以及正式产品性能矩阵产生原始证据。

## 5. 批准记录模板

不得在没有负责人明确回复时填写本表。

| ID | 判定 | 修订内容或理由 | 日期 | 批准人代号 |
|---|---|---|---|---|
| D23-01–D23-10 | Pending | Pending | Pending | Pending |
| D23-11 | Pending | 需明确许可证与商业模式 | Pending | Pending |

批准后应在独立 PR 中同步 PRD、路线图、ADR-0001、Issue #21–#24 和安装/支持矩阵；本提案本身不能关闭 Issue #23。
