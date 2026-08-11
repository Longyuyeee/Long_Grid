# Phase 0 出口执行手册

日期：2026-08-01

状态：**Approved product scope / 实机与五人测试未完成**

关联：Issue #19–#24、ADR-0001

## 1. 用途与判定规则

本手册把 Phase 0 剩余工作整理为可复读的执行入口。自动化探针通过不代替人工体验、真实硬件或负责人决策；没有原始证据时只能记录 `Inconclusive`，不得填写 `Pass`。

每轮记录必须包含：

- 执行日期、测试人员和对应 Issue；
- Windows 版本、架构、GPU 类别、显示器数量和应用提交；
- 场景前置状态、操作步骤、预期结果和恢复步骤；
- `Pass`、`Fail` 或 `Inconclusive`；
- 脱敏截图/录像/JSON 路径和缺陷链接；
- 是否恢复显示、Explorer、文件及辅助功能设置。

证据不得包含用户名、完整路径、文件名、显示器 PNP ID、EDID、设备序列号、窗口标题或云账号。测试文件只能放在新建专用沙箱或专用测试账户中。

## 2. 执行前检查

```powershell
git switch main
git pull --ff-only origin main
dotnet restore LongGrid.sln --locked-mode
dotnet build LongGrid.sln --configuration Release --no-restore
dotnet test LongGrid.sln --configuration Release --no-build
```

开始实机操作前：

- 关闭包含个人内容的窗口，准备专用测试账户和测试文件；
- 记录当前显示布局、缩放、投影方式和默认音频/输入状态；
- 确保能恢复 Explorer、显示设置、Narrator 和远程会话；
- 不在唯一工作设备上执行真实卷耗尽、ACL 破坏或恶意 Provider 测试；
- 每个场景单独执行，失败后先恢复基线再继续。

## 3. Issue #19：输入、无障碍与系统表面

前置条件：使用 P0-04/P0-05b1 可见交互切片；Narrator 结论必须人工听读确认。

执行前先调用 `eng/Start-Issue19ManualMatrixSession.ps1 -ValidateOnly`；随后按[Issue #19 单场景运行手册](manual-testing/issue-19-input-system-surface-runbook.md)为每个 ID 启动全新进程。预检只证明入口与隐私合同可用，输出必须保持 `PendingManualEvidence`。

| ID | 场景 | 最低操作 | 通过条件 | 状态 |
|---|---|---|---|---|
| I19-01 | 键盘 | Tab/Shift+Tab、方向键、Enter、Space、Esc | 焦点顺序与视觉顺序一致；选择、调用和退出可逆 | Pending |
| I19-02 | 鼠标 | 单击、双击、框选、滚轮、边界命中 | 命中仅发生在显式交互区域；Passive 区域穿透 | Pending |
| I19-03 | 触控/笔 | 点击、滚动、长按、拖动 | 无幽灵点击；输入门关闭时不触发动作 | Pending |
| I19-04 | 拖放 | 内部重排、Explorer 拖入、拖出 | 明确显示“引用”或“移动”；取消不改变文件 | Pending |
| I19-05 | Narrator | 浏览容器/项目并调用操作 | Name、角色、状态、位置和操作语义正确 | Pending |
| I19-06 | 高对比/文本缩放 | 切换高对比和文本缩放 | 焦点、选择、禁用状态仍可区分且不裁切 | Pending |
| I19-07 | Win+D/Peek | 显示桌面并恢复 | 宿主显隐符合设计，不抢焦点、不残留遮挡 | Pending |
| I19-08 | 全屏 | 进入/退出游戏或视频全屏 | 宿主按策略隐藏并可靠恢复 | Pending |
| I19-09 | Alt+Tab/任务视图 | 多次切换和关闭预览 | 宿主不出现为普通应用窗口，不抢前台 | Pending |
| I19-10 | Explorer 重启 | 结束并重启 Explorer | 监听、层级和交互可恢复，无孤儿窗口 | Pending |

任一误移动、焦点锁死、系统表面遮挡或 Narrator 不可操作均为 `Fail`，不得以重试成功覆盖首次失败；应建立缺陷并保留复现率。

### 3.1 正式工作区批量选择专项矩阵

I19 原型矩阵不能代替正式 `LongGrid.App` 的产品交互。执行前调用 `eng/Start-LongGridBatchAccessibilitySession.ps1 -ValidateOnly`，随后按照[批量选择无障碍人工矩阵运行手册](manual-testing/batch-selection-accessibility-runbook.md)逐项启动；真实会话必须确认专用测试账户和恢复计划。预检只复核 136-ID、8 个关键控件、单次 live-region 播报和紧凑断点合同，输出固定保持 `PendingManualEvidence`。

| ID | 场景 | 最低人工结论 | 状态 |
|---|---|---|---|
| BSA-01 | 纯键盘 | 焦点顺序、标准多选、有限批量入口、清除选择和无焦点陷阱 | Pending |
| BSA-02 | Narrator | Name/角色/状态可理解，每次动作只播报一次最终数量，清除播报 0 | Pending |
| BSA-03 | 高对比度 | 焦点、选择、禁用和按下状态可区分且语义不变 | Pending |
| BSA-04 | 200% 文本缩放 | 无关键裁切、遮挡或不可达控件，无强制水平滚动 | Pending |
| BSA-05 | 紧凑宽度 | 两组按钮纵向重排，可见/Tab/听读顺序一致，宽窄往返可逆 | Pending |

五项均须记录首次结果、`DesktopFilesChanged=False` 和恢复确认；缺少听读、视觉或恢复证据时记为 `Inconclusive`。

## 4. Issue #20：动态显示与会话

先按[P0-07b2b2b2b4b1 报告](spikes/P0-07b2b2b2b4b1-controlled-dynamic-matrix-harness.md)启动对应 `--matrix-scenario`。每次变化后等待稳定采样，再恢复原布局并复读窗口、Region、Composition、UIA 和资源状态。

执行前先调用 `eng/Start-Issue20DisplayMatrixSession.ps1 -ValidateOnly`；随后按[Issue #20 单场景运行手册](manual-testing/issue-20-dynamic-display-session-runbook.md)启动 observer。`Observed Pass` 不是最终人工 Pass，最终状态在视觉、输入和恢复确认完成前保持 `PendingManualEvidence`。

| ID | 场景 | 最低矩阵 | 通过条件 | 状态 |
|---|---|---|---|---|
| I20-01 | DPI 缩放 | 100%→150%→100%，跨屏移动 | 收到公开事件；稳定后 DIP/像素映射正确 | Pending |
| I20-02 | 旋转 | 横向→纵向→横向 | 拓扑指纹更新；窗口可见且输入区域一致 | Pending |
| I20-03 | 热插拔 | 拔出/接回副屏或扩展坞 | 歧义时阻断；恢复后位置可解释 | Pending |
| I20-04 | 投影 | 仅电脑/复制/扩展/仅第二屏 | 每代只提交一次有效计划，不提交旧代次 | Pending |
| I20-05 | 睡眠恢复 | 睡眠→唤醒 | 会话稳定后恢复，无循环重排和资源增长 | Pending |
| I20-06 | 锁屏 | 锁定→解锁 | 暂停期间不提交，恢复后重新采样 | Pending |
| I20-07 | RDP | 本地→RDP→本地 | 会话/显示变化完整，回本地后安全恢复 | Pending |
| I20-08 | WM_DPICHANGED | 跨混合 DPI 屏拖动 | 建议矩形、窗口复读和 UIA Bounds 一致 | Pending |

设备或策略没有产生预期事件时记录 `Inconclusive`；禁止手工补写事件或用静态快照冒充动态场景。

## 5. Issue #21–#22：剩余安全与隔离矩阵

自动 CI 已覆盖安全引用、同卷受控移动、冲突预阻断、回调取消/部分成功，以及零 Capability AppContainer Worker 500 项预算、受控输入副本、有界 BGRA32 像素协议与故障矩阵、硬超时、Job Object 父退出/Profile 清理和连续超时退避。以下仍须专用环境：

逐项关闭条件和范围依赖见[Issue #21–#22 关闭就绪审计](29-issue-21-22-closure-readiness-audit.md)。D23 范围批准后，首版只支持安全引用；托管移动专用环境项转入后续里程碑。缩略图首发范围为 Windows 11 x64、隔离 Worker 安全分类与类型图标回退，未批准的格式、架构和能力不得自动扩张为 Phase 0 必需项。

| Issue | 剩余项 | 安全限制 | 状态 |
|---|---|---|---|
| #21 | Explorer UI 撤销/Explorer 重启 | 仅专用账户和自有文件 | 后续托管移动里程碑；非首发阻断 |
| #21 | 跨卷复制→校验→删除→补偿 | 两个可清空测试卷，不使用用户卷 | 后续托管移动里程碑；非首发阻断 |
| #21 | ACL、共享占用、只读卷、磁盘满 | VM/可还原快照；不得破坏系统目录 | 后续托管移动里程碑；非首发阻断 |
| #21 | OneDrive、网络、重解析点、真实取消 | 专用账号/共享；默认阻断优先 | 后续兼容里程碑；非首发阻断 |
| #22 | 受限 Low Integrity 对照 | Low worker 可读取未授权文件，证明 MIC no-write-up 不能承担文件保密边界 | Decision evidence；不得作为生产回退 |
| #22 | AppContainer 与访问 broker | 真实 worker 全部为零 Capability AppContainer；协议 v6 对照受控副本与最小路径 ACL；八个自有格式/编码样本逐项验证父进程基线、输入可读、同格式策略一致、安全分类和 Profile 清理，正常路径复核随机 SID ACE 已恢复 | Conditional Pass（自动探针）；ACL 只作比较，异常退出 ACE 修复 Pending |
| #22 | 有界共享内存句柄 broker | 匿名映射、单请求复制句柄、最大 262,144 bytes；缺失句柄/错误容量/元数据错误全部阻断并恢复 | Conditional Pass（自动探针） |
| #22 | 正式渲染表面集成 | 保持已验证的 transport、长度、格式、尺寸和容量上限 | Phase 1 首片验收；不阻断创建生产宿主 |
| #22 | 真实 Provider、x64/ARM64、Windows 矩阵 | 22621：TIFF-RGB/TIFF-LZW 精确模块缺失并识别陈旧 handler；HEVC 可枚举、AV1 不可枚举，HEIC/AVIF 均精确提取失败；26100：HEVC/AV1 均不可枚举，AppContainer 16/16 可读但 Shell 全部 `E_ACCESSDENIED`；仅输出固定标签/HRESULT/健康布尔值 | Partial；仅把 #23 批准的首发格式、架构和环境纳入 Phase 0 必需项 |

Phase 0 必须关闭安全边界和已批准首发范围的兼容风险；正式产品渲染接线在生产宿主存在后验收。未批准支持的 Office/PDF、更多 codec、第三方 provider 或 ARM64 不得自动扩张阶段范围。产品默认只能使用安全引用和缓存内图像。

## 6. Issue #23：负责人决策记录

以下范围已由负责人于 2026-08-04 以 `ProjectOwner` 代号批准；许可证按要求延期。

| 决策 | 批准范围 | 后续项 | 状态 |
|---|---|---|---|
| 首版整理模式 | 仅安全引用；真实移动保持关闭 | 托管移动转后续里程碑 | Approved |
| Folder Portal | 不进入首个 MVP 切片 | P1 以后 | Approved |
| 最低系统 | Windows 11 技术预览 | Windows 10 后续验证 | Approved; matrix pending |
| 架构 | x64 | ARM64 移至 P1 | Approved |
| 安装渠道 | MSIX 目标渠道，保留企业离线验证 | 当前 unpackaged 仅开发 | Approved; packaging pending |
| 许可证 | 当前开发跳过 | 正式分发/外部贡献前重开 | Deferred by owner |
| 性能预算 | PRD 目标作为首片门禁，不作为发布 SLA | 正式产品矩阵继续测量 | Approved; measurement pending |

5 人无提示测试必须覆盖：首次扫描说明、创建容器、添加安全引用、识别原生图标仍存在、区分引用与移动、撤销/恢复。每位参与者记录任务成功率、误解点、严重度和是否需要主持人提示；不能收集真实文件名或桌面截图。首次整理模式原型的执行步骤和匿名结果表见[Issue #23 五人可用性测试计划](usability/issue-23-first-organization-test-plan.md)，会话隔离、隐私和主持纪律见[主持人运行手册](usability/issue-23-facilitator-runbook.md)；当前代码覆盖起点、模式、预览、单个匿名容器、三个匿名引用、拖放语义、两步撤销及布局恢复三态/过期/取消语义，真实扫描、Explorer/DesktopHost 拖放、显示硬件恢复、持久化与文件操作撤销仍必须保持 Pending。

## 7. Issue #24 与 ADR-0001

Phase 0 必须确认正式 schema、迁移/回滚、原子存储、安全恢复、生产模块边界，并在可用的专用环境补真实卷只读/空间耗尽证据。这些是把探针合同迁入 `LongGrid.Infrastructure` 的前置条件。

执行真实卷场景前先调用 `eng/Start-Issue24PersistenceBoundarySession.ps1 -ValidateOnly` 验证安全会话链，再按照[Issue #24 专用环境运行手册](manual-testing/issue-24-persistence-boundary-runbook.md)分别执行 I24-01 与 I24-02。预检固定保持 `PendingDedicatedEnvironmentEvidence`，不写卷、不填盘、不改变卷状态、不运行配置探针，也不产生最终结论；详细边界见[Issue #24 专用环境就绪审计](31-issue-24-dedicated-environment-readiness-audit.md)。

`LongGrid.Core.Configuration` v1 已完成纯模型、验证、未知字段保留和 JSON 资源边界，当前仅允许安全引用；详见[正式产品配置合同审计](28-product-configuration-contract-audit.md)。该进展只关闭 schema 形状前置，不关闭真实卷、Infrastructure 原子存储或应用生命周期证据。

应用关闭排空、完整单实例激活和正式渲染表面属于首个生产切片才能形成的集成证据，迁入 Phase 1 首片验收，不再要求它们在创建 `LongGrid.App`/`LongGrid.DesktopHost` 之前完成。对应 Issue 可以在 Phase 0 合同项关闭后保留明确的首片子任务，但不得形成循环门槛。

D23 产品范围已经批准；只有 #19、#20、#23、#24 的其余必要证据齐全后，才能把[ADR-0001](adr/0001-windows-technology-stack.md)从 `Proposed` 改为 `Accepted`、`Revised` 或 `Rejected`。ADR 决定前不得创建公开安装承诺，也不得把探针项目改名冒充产品模块。

## 8. 单轮证据模板

```text
Issue / 场景 ID：
提交：
测试人 / 日期：
Windows / 架构 / GPU 类别 / 显示数量：
前置状态：
实际步骤：
预期：
实际：
结果：Pass | Fail | Inconclusive
脱敏证据：
缺陷：
恢复确认：
备注：
```

完成一轮后把模板作为对应 Issue 评论提交；只在所有必需场景有可复读证据且无开放阻断缺陷时关闭 Issue。
