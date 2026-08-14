# Phase 0、桌面 MVP 与内部 RC 收尾执行计划

- 审计日期：2026-08-14
- 当前执行基线：`main@272dad6`（PR #184；main CI 31727572596 通过）
- 状态：当前唯一权威执行顺序；后续每个切片均从本文领取范围和验收目标
- 适用范围：Phase 0 退出、桌面 MVP 垂直切片、内部 RC 与发布准备

## 1. 计划权威与目标分层

本文把既有路线图、阶段审计和各专项运行手册收束为三个连续目标：

1. **Phase 0 退出**：关闭剩余外部证据和架构决策，不把探针误报为产品能力；
2. **桌面 MVP**：把已经验证的能力接入正式 App/Core/Infrastructure 产品链路；
3. **内部 RC**：在真实数据、持续运行、恢复与隐私边界上形成可安装、可回滚、可审计的候选版本。

专项文档仍负责场景细节，本文负责验收目标。2026-08-13 起，执行顺序由 [Stage 129 外部证据延期决策](129-external-evidence-deferment-decision.md)补充：工程轨道可以继续，但外部证据仍阻塞 M4 完成、内部 RC 和分发。若旧文档与 Stage 129 冲突，以 Stage 129 为准；旧审计快照保留作历史记录。

## 2. 当前基线审计

| 项目 | 当前事实 | 判定 |
| --- | --- | --- |
| 主线 | `main@272dad6`，PR #184 已合并，无开放 PR | 当前收尾基线 |
| 自动化门禁 | GitHub `build-test` 通过；888/888；行覆盖率 90.52%，分支覆盖率 80.12% | 自动化基线通过 |
| Phase 0 跟踪 | #19、#20、#23、#24 仍开放；#21、#22 已按批准范围关闭 | 尚未退出 Phase 0 |
| 五人测试 | 运行手册与证据结构已具备 | 等待真实参与者执行 |
| 输入/系统表面 | B6C3-01～08、系统表面/拓扑、A5 与 BSA 入口可校验 | 等待受控实机人工证据 |
| 显示动态矩阵 | I20-01～08 observer 入口已具备 | 等待真实缩放/旋转/拔插/投影/睡眠/RDP 证据 |
| DesktopHost | 正式 Passive/Explicit/Hidden 表面、UIA Fragment 与失败补偿已进入产品链；默认关闭 | M1 工程完成，外部证据 Pending |
| 输入链路 | E2a 已原子消费 Prepared Intent 并接入 admission/surface/selection transaction；无正式 activation HWND，App 尚未调用输入/消费/选择入口 | E2b 未实现，产品链路未闭合 |
| 配置恢复 | `ProductConfigurationStore` 与正式 Store 三阶段证据宿主已具备 | I24-01/I24-02 真实卷仍未执行，不能关闭 #24 |
| 架构决策 | ADR-0001 仍为 Proposed | Phase 0 退出前必须批准或明确否决 |
| 发布准备 | 已有便携包、unsigned MSIX、SBOM 与 CI 交付集审计；许可证、签名、真实安装生命周期和正式标签未完成 | 仅适合内部开发，不适合公开分发 |

### 2.1 当前不应提前做的工作

- 在外部矩阵未完成前继续加深同类探针；
- 把 `--interactive-slice`、observer 或证据 launcher 描述成正式 App；
- 在产品链路闭合前进行大规模 UI 美化、格式扩张或 provider 扩张；
- 为了“收尾”自动改写目标卷状态、填满磁盘或绕过只读保护；
- 在真实产品验证前重构大型文件，造成验证基线漂移。

## 3. 每一步统一开发、审计与推送流程

每个切片都必须单独完成以下闭环，不允许把多个未验收目标堆进一个长期分支：

1. 从最新 `main` 拉取并确认工作树干净；
2. 建立 `codex/<切片名>` 短生命周期分支；
3. 在对应 Issue/文档写清范围、非目标、风险和验收命令；
4. 按 Core → Infrastructure → App/DesktopHost 的依赖方向实现，避免从 UI 反向侵入领域模型；
5. 增加自动化测试；涉及 Win32、硬件或人工判断时，同时维护证据 launcher 和脱敏合同；
6. 更新当前计划、专项运行手册、状态审计及必要 ADR；
7. 执行目标测试、全量测试/构建、覆盖率门禁、脚本 `-ValidateOnly`、`git diff --check` 和文档链接检查；
8. 审计差异：无越界能力、无未声明写入、无隐私泄漏、无把 Pending/Conditional Pass 写成 Pass；
9. 使用语义明确的提交，推送分支并创建 PR；
10. 等待 PR CI，通过审查后合并；再确认 `main` CI，通过后回写 Issue/证据索引并删除短生命周期分支。

统一停止条件：若验收失败，保留原始失败事实，最小化修复后重新执行；若必须扩大权限、支持范围或数据写入，停止该切片并先更新决策记录，不得自行扩大范围。

## 4. Phase 0 退出步骤

### C0：治理、入口与基线冻结

**开发内容**

- 以本文作为唯一当前收尾入口，同步 README、路线图、状态审计和产品阶段计划；
- 复核开放 Issue、里程碑、PR 与远端分支的对应关系；
- 已合并分支只做清理候选清单，不在文档 PR 中删除；
- 明确探针、observer、正式产品适配器三种证据等级。

**验收目标**

- 所有导航入口指向本文；
- 主线 SHA、测试数、覆盖率和开放门禁与 GitHub 一致；
- 每个剩余门禁都有唯一负责人/Issue、执行入口、预期证据和退出判定；
- 文档无失效相对链接，全部 `-ValidateOnly` 入口通过。

### C1：Issue #23 五人可用性测试

**开发/执行内容**

- 使用已冻结的原型、任务脚本、主持人话术和匿名证据模板；
- 完成 5 名参与者的发现、理解、操作与恢复任务；
- 只修复阻断既定任务的 P0/P1 问题，新增需求进入后续 backlog。

**验收目标**

- 5/5 会话都有匿名记录、环境摘要、任务结果和问题严重度；
- 首次进入、整理模式差异、恢复入口和错误反馈均有可判定结果；
- 不保存姓名、原始路径、文件内容或可反推个人身份的信息；
- 所有阻断 Phase 0 的问题已修复并回归，或由负责人书面接受风险；
- #23 附证据索引后关闭。

### C2：输入、无障碍、系统表面与拓扑实机矩阵

**开发/执行内容**

- 按 Stage 124 执行 B6C3-01～08，不再添加同类工程深度；
- 同时执行系统表面/拓扑、DesktopHost A5 与 BSA 的受控实机矩阵；
- 明确记录“独立探针通过”“正式被动表面通过”“产品 Explicit 尚未接入”的边界。

**验收目标**

- 键盘、鼠标、焦点、穿透、UIA Raw View、缩放与多显示器场景均有 Pass/Fail；
- 关闭/重开、所有权转移、部分失败和紧急隐藏后无残留交互层；
- 无进程注入、无内核驱动、无依赖 `Progman`/`WorkerW`；
- 原始证据脱敏，失败可复现且不会被自动聚合成 Pass；
- Issue #19 中对应 P0-04/P0-05b2 证据齐全。

### C3：Issue #19 正式 DesktopHost 被动表面确认

**开发/执行内容**

- 用正式 App/DesktopHost 被动表面适配器验证只读接入；
- 把旧 `--interactive-slice` 证据保留为探针证据，不作为正式产品验收；
- 核对窗口生命周期、显示变化、资源释放与失败回退。

**验收目标**

- 正式 App 在显式开发配置下可启动/停止被动桌面表面，默认配置保持关闭；
- 异常与关闭路径不遗留窗口、Hook、DComp 或 UIA 资源；
- A5、BSA 和相关自动化回归通过；
- #19 对每项声明标注 Evidence Level，未完成的 Explicit 能力明确转入 M1/M2；
- Phase 0 范围内证据完成后关闭 #19，不把后续 MVP 能力伪装为已完成。

### C4：Issue #20 动态显示/硬件矩阵

**开发/执行内容**

- 执行 I20-01～08：缩放、旋转、拔插、投影、睡眠/唤醒、会话切换、RDP 及恢复组合；
- 使用 observer 捕获事件与状态恢复，不用 observer 代替正式产品；
- 每个场景记录初态、操作、事件、终态和恢复确认。

**验收目标**

- I20-01～08 均有真实硬件/会话结果，不允许空事件判 Pass；
- 拓扑变化后边界、DPI、可见性与输入区域一致，恢复后无幽灵表面；
- 失败场景有匿名、可复现记录，并有修复或书面风险接受；
- #20 附环境兼容矩阵和证据索引后关闭。

### C5：Issue #24 正式配置恢复与真实卷失败

#### C5a：正式产品存储证据宿主

**开发内容**

- 建立只面向开发/证据的真实卷会话宿主，直接使用 `ProductConfigurationStore`；
- 复用 `PrepareBaseline → AttemptFailure → RecoverAndRetry`，验证失败前状态、失败隔离与显式重试；
- launcher 只接受用户预先准备的专用测试卷/目录，不自动填盘、不切换只读、不修改卷属性；
- 证据仅输出散列、分类状态与脱敏环境摘要。

**验收目标**

- 自动化覆盖成功、容量不足、只读/拒绝访问、进程重启后恢复与显式重试；
- 失败不会覆盖最后有效配置，不会产生假成功或无限重试；
- launcher 明确显示 `writesTargetVolume=true`，但 `fillsTargetVolume=false`、`changesVolumeState=false`；
- `-ValidateOnly` 在无目标卷、错误卷和不安全路径上拒绝执行；
- CI 不依赖真实卷即可验证合同，真实写入只在受控人工环境执行。

#### C5b：I24-01/I24-02 实机执行

**执行内容**

- I24-01：在专用配额/容量受限卷上制造真实写入失败；
- I24-02：在用户预先设为只读或拒绝访问的专用卷上制造真实失败；
- 解除条件后执行显式恢复与重试，复读最终配置。

**验收目标**

- 两个场景都有失败分类、最后有效快照、恢复动作、重试结果和最终复读；
- 未写入系统卷、用户真实资料目录、网络共享或未经批准的重解析点；
- 运行日志不含原始路径、配置内容或机器身份；
- #24 的“正式产品存储真实卷证据”成立后关闭；自动保留/容量策略若未批准，继续留在后续里程碑而非 Phase 0。

### C6：ADR、里程碑与 Phase 0 退出审计

**开发/治理内容**

- 将 ADR-0001 从 Proposed 更新为 Accepted、Rejected 或 Superseded，并记录支持边界；
- 汇总 #19/#20/#23/#24 的证据、风险接受和未纳入项；
- 关闭 `Phase 0 Exit` milestone，建立桌面 MVP 里程碑。

**验收目标**

- 所有 Phase 0 探针均有明确 Pass/Fail/Accepted Risk，不存在模糊 Pending；
- 文件默认不移动，引用模式与真实移动差异在 UI/文档中清楚；
- 不依赖注入、驱动或未文档化桌面嵌入；
- 四个开放 Issue 均已关闭或由负责人明确重新分期，不能静默遗留；
- Phase 0 成果均在 `main`，`main` CI 绿色，审计文档更新到真实 SHA。

## 5. 桌面 MVP 垂直切片

### M1：正式 Explicit 表面适配器

**开发内容**

- 实现正式 `ProductDesktopHostPassiveSurfaceModeAdapter.ApplyExplicit`，用已有可补偿 surface pipeline 执行显式状态变更；
- 保持默认关闭、配置显式准入和紧急隐藏；
- 建立生命周期、幂等、部分失败、逆序补偿与终态复读测试。

**验收目标**

- Explicit 请求可真实改变正式表面，Passive 请求永不写入；
- 任一层失败均回滚至可判定安全状态；
- 关闭/崩溃恢复后无资源残留；
- 正式 App 的状态展示与实际 surface 终态一致。

### M2：正式输入源、Intent 消费与可访问交互

**开发内容**

- 接入产品输入源，把键盘/鼠标/焦点事件转换为 Core Intent；
- 建立 Intent 消费、去重、取消、焦点与 Explicit 权限边界；
- 将 UIA provider、视觉状态和可访问动作连接到同一产品状态机。

**验收目标**

- 键盘、鼠标与 UIA 三条路径产生一致业务结果；
- Passive/只读状态不会消费会产生写入的 Intent；
- 重复、乱序、取消和窗口重建不产生双重操作；
- Narrator/键盘-only 路径可完成 MVP 核心任务。

### M3：配置优先的桌面直接交互 MVP

**开发内容**

- 将正式配置存储、恢复状态 UI、显式恢复动作与桌面交互链路集成；
- 首版保持引用/配置为主，不自动移动真实文件；
- 提供状态、错误、重试、清理和最小匿名证据导出。

**验收目标**

- 用户能在正式 App 中完成发现、配置、桌面交互、失败恢复和撤销/清理闭环；
- 重启、显示变化和 Explorer 生命周期变化后状态一致；
- 错误可见、可恢复且不丢最后有效配置；
- 任何真实移动能力必须单独获得支持范围批准，不随 MVP 暗中进入。

### M4：正式产品验证

**开发/执行内容**

- 使用正式 App 重跑 PRD 核心旅程、5 人任务、显示/输入矩阵；
- 执行 500 项数据规模和至少 24 小时持续运行；
- 建立崩溃恢复、升级/降级、配置损坏和证据清理场景。

**验收目标**

- 所有核心旅程无 P0/P1 缺陷，P2 有明确处置；
- 500 项下交互和恢复满足已批准性能预算；
- 24 小时运行无持续资源增长、残留窗口或状态漂移；
- Phase 0 探针结论由正式产品复验，不再只依赖独立 harness。

## 6. 内部 RC 与发布准备

### R1：验证后维护性收敛

**开发内容**

- 在 M4 基线稳定后拆分大型热点文件（如 MainWindow、引用协调器、App 启动协调）；
- 只做等价重构，先加 characterization tests，再移动职责；
- 固化依赖方向、可观测性和错误分类。

**验收目标**

- 功能、证据格式和外部合同不变；
- 全量回归、覆盖率与正式产品矩阵不下降；
- 每个拆分 PR 可独立回滚，不与新能力混合。

### R2：内部 RC 候选

**开发/执行内容**

- 冻结功能，建立版本号、变更日志、已知问题、安装/卸载与回滚说明；
- 在干净 Windows 11 x64 环境执行安装、首次启动、升级、卸载和残留审计；
- 汇总安全、隐私、可访问性、性能和可靠性证据。

**验收目标**

- RC 构建可重复，制品与源码 SHA 可追溯；
- 干净安装、覆盖升级、卸载/回滚均通过；
- 无 P0/P1，已知问题有用户可理解的规避方式；
- 内部负责人签署 Go/No-Go，RC 标签只在 `main` 绿色后创建。

### R3：正式分发准入

**开发/治理内容**

- 选定并加入许可证，明确第三方依赖与素材许可；
- 建立代码签名、制品校验、发布说明和安全响应入口；
- 决定渠道、支持范围、遥测/诊断默认值和数据保留政策。

**验收目标**

- 许可证、NOTICE/SBOM、签名与校验流程通过法务/负责人确认；
- 安装包签名有效，发布物可验证且可回滚；
- 隐私说明与实际采集一致，默认不上传原始路径或内容；
- 发布清单全部通过后才从内部 RC 转为公开版本。

## 7. 外部依赖与不可伪造门禁

| 门禁 | 需要的外部条件 | 工程侧可先做 | 禁止做法 |
| --- | --- | --- | --- |
| 五人测试 | 5 名真实参与者 | 校验脚本、模板、修复已知阻断 | 用开发者自测冒充 5 人 |
| 显示矩阵 | 对应显示器、投影/RDP/睡眠环境 | observer、恢复断言、证据脱敏 | 无事件时判 Pass |
| 真实输入/UIA | 受控 Windows 会话与辅助技术 | 自动化合同和 launcher | 用单元测试冒充人工体验 |
| 容量/只读卷 | 用户预先准备的专用测试卷 | 安全校验、正式 store 接入 | 自动填盘或修改系统卷 |
| ADR/范围批准 | 产品/技术负责人决策 | 提供证据与选项 | 把 Proposed 自动改成 Accepted |
| 发布许可/签名 | 负责人、法务与证书 | 清单、SBOM、可重复构建 | 无许可或无签名公开分发 |

## 8. 每个切片的审计回写模板

每次合并后在本文或对应专项文档追加：

```text
切片：
Issue / PR / merge SHA：
范围与非目标：
实现摘要：
自动化验收：
人工/实机验收：
安全与隐私审计：
失败与剩余风险：
文档变更：
PR CI / main CI：
结论：Pass / Conditional Pass / Fail / Accepted Risk
下一步：
```

判定规则：只有验收目标全部满足才写 `Pass`；依赖外部证据时写 `Conditional Pass` 并列出唯一剩余条件；真实失败必须写 `Fail`；`Accepted Risk` 必须带负责人和决策记录。

## 9. 从当前基线开始的双轨顺序

1. **C0（已完成）**：计划与入口已由 PR #174 合并到 `main@508528b`，PR/main CI 均通过；
2. **C1（延期到 RC 前证据汇合门）**：等待 5 位未参与设计的真实参与者；Issue #23 与结果状态保持 OPEN/Pending；
3. **C2～C4（延期到 RC 前证据汇合门）**：Stage 126 工程就绪；物理输入、Narrator、系统表面、A5、BSA、I19/I20 仍 Pending；
6. **C5a（等待期安全准备已实现）→ C5b**：Stage 127 已接正式产品存储证据宿主；按序到达后执行真实卷矩阵并收口 Issue #24；
7. **C6**：批准 ADR、完成 Phase 0 退出审计；
8. **当前工程轨道 M1 → M4-ready**：闭合正式表面、输入和配置恢复，只能推进到等待外部验证；
9. **证据汇合 X1 → X5**：按 C1、C2/C3、C4、C5b、C6 顺序补齐外部证据；
10. **R1 → R3**：证据汇合后重构、内部 RC、许可/签名与正式分发。

Stage 129 已批准外部证据与工程实现双轨推进，但不允许把工程通过写成外部证据通过。E4 后必须停止并完成 X1～X5，才能进入内部 RC。

## 10. 切片审计回写

### C0：治理、入口与基线冻结

- **Issue / PR / merge SHA**：PR #174 / `508528b5e51cf9bcbc3c3a143372c972a49b3f5f`；
- **范围与非目标**：只统一执行顺序、验收目标和权威入口；不改变产品、探针、权限或发布行为；
- **实现摘要**：新增 Stage 125，并同步 README、路线图、状态审计、Stage 103 与 Phase 0 运行手册；
- **自动化验收**：相对 Markdown 链接、`git diff --check`、8 个关闭入口 `-ValidateOnly` 通过；
- **人工/实机验收**：不适用；没有把既有 Pending 外部证据升级为 Pass；
- **安全与隐私审计**：明确禁止用开发者自测冒充参与者、无事件判 Pass、自动填盘或修改系统卷；
- **失败与剩余风险**：C1–C6、M1–M4、R1–R3 仍按本文顺序待执行；
- **文档变更**：`README.md`、Stage 04/11/12/103/125；
- **PR CI / main CI**：PR CI 31707073510 成功；main CI 31707838547 成功；
- **结论**：Pass；
- **下一步**：C1 Issue #23 五人测试。

Stage 128 对 C0 遗留的已合并远端分支执行独立卫生审计：只允许删除 PR head、merge commit 和 tree 三者精确对应且无开放 PR 的 `codex/*`；删除结果通过 PR 轨迹记录，不改变任何阶段证据。

### M1：正式 Explicit 表面适配器（工程完成）

- **审计基线**：`main@d9b43bd`；
- **需求对齐**：只闭合正式 Passive/Explicit/Hidden 表面切换、复读和补偿；不接输入源、不消费 Intent、不操作桌面文件；
- **实现摘要**：代际匹配 lease 可使产品自有 HWND 进入 Explicit；根 UIA 暴露空 Selection provider；多表面失败逆序隐藏；
- **安全边界**：默认关闭、Host/Interaction 双 opt-in、emergency disable 最高优先级和关闭前隐藏保持不变；
- **外部证据**：不适用为通过依据；X1～X5 和四个开放 Issue 状态不变；
- **远端入口**：PR #180，首个实现提交 `c3a99fd`；
- **自动化验收**：Release 0 warning/0 error，881/881 测试，line 91.11% / branch 80.64%，UI 合同、14 个安全入口和 3 个原生探针通过；
- **PR CI / main CI**：PR run 31720374196 成功；合并 `main@78c853fea3d5d08071a0cab19af18dc4db4f5446`，main run 31720966794 成功；
- **结论**：E1/M1 工程 Pass；外部证据状态不变，不能据此进入 RC；
- **下一步**：进入独立 E2/M2 切片。详见 [Stage 130](130-formal-explicit-surface-adapter-audit.md)。

### M2/E2a：原子 Intent 消费边界（工程完成）

- **审计基线**：`main@271276e`；合并结果 `main@d51cc49a7b12d0d9d2fac212683c9b151f955f59`；
- **需求对齐**：只关闭 Prepared Intent 至多一次消费、Passive 即时复读、匿名项目选择身份和生命周期取消；不接正式 HWND 输入源、不操作桌面文件；
- **实现摘要**：桥锁内原子消费后进入既有 admission / Explicit / selection 事务；Explicit 期间后续输入不能再次准备；系统事件、投影释放和关闭统一取消；
- **安全边界**：Host/Interaction/Bridge/Forwarding 四重门禁缺一关闭，文件操作能力恒为 false，App 只组装而不调用消费入口；
- **外部证据**：不适用为通过依据；物理输入、Narrator、动态系统表面和四个开放 Issue 继续 Pending；
- **远端入口**：PR #182，实现提交 `b57ba07`；
- **自动化验收**：Release 0 warning/0 error，888/888；远端 line 90.52% / branch 80.12%，全部安全入口与交付集审计通过；
- **PR CI / main CI**：PR run 31724501907 成功；main run 31725122789 成功；
- **结论**：E2a 工程 Pass，但不等于 E2/M2 完成；
- **下一步**：进入独立 E2b 正式 pointer/keyboard/UIA 来源。详见 [Stage 131](131-atomic-intent-consumption-audit.md)。

E2b 实现前设计审计确认：M1 Passive 整窗穿透不能作为首次输入来源；正式方案使用产品自有、每显示器一个、Region 受限的 activation HWND，初始三类激活统一进入 E2a，Explicit 后 pointer/keyboard/UIA 选择统一进入现有 selection transaction。实现固定拆为 E2b1 来源/激活闭环与 E2b2 选择/UIA 闭环，详见 [Stage 132](132-formal-input-source-design-audit.md)。

### M2/E2b：正式输入与项目选择（工程完成）

- **E2b1**：PR #186，squash 合并 `main@ee9d20c6266335b09918afc3f0340e577cb881f6`；每显示器有限 activation HWND 统一接入 pointer、App keyboard command 与 UIA Invoke；PR/main CI 通过；
- **E2b2**：PR #187，squash 合并 `main@6a4878b0156aaacd5128fbe9e1dfabf681e58f4f`；主 surface pointer、有限键盘代理与 UIA SelectionItem/Invoke 统一调用既有 selection transaction；
- **自动化验收**：907/907；远端 line 90.19% / branch 79.13%；PR final run `31770324116` 全绿；main run `31770627835` 初次出现既有 dispatcher 测试单次 runner 超时，保留轨迹并重跑失败 job 后 34 步全绿；
- **安全边界**：没有全局 Hook、Raw Input、SendInput、RegisterHotKey、Explorer/WorkerW 或桌面文件操作；Passive 项目 Pattern 关闭，取消后恢复 NoActivate/Hidden/Passive；
- **结论**：E2/M2 Engineering Pass；物理输入、Narrator、高对比、文本缩放、动态系统表面与四个开放 Issue 继续 Pending；
- **下一步**：M3 集成差距审计，只补 DesktopHost 交互与既有配置、恢复、保存链之间的缺口。详见 [Stage 135](135-formal-item-selection-and-accessibility-audit.md)。

### M3a：目录—投影修订集成（工程完成）

- **审计基线**：`main@eef24097ac8d04f18886eca98ce970131a942e9d`；
- **需求对齐**：只补正式目录刷新与既有工作区/投影修订链断点，不重建配置、保存或 DesktopHost，不开启真实文件操作；
- **实现摘要**：目录 generation/status 变化先调用既有外部修订入口，清除过期令牌后重建投影；重复快照幂等，迟到旧代次拒绝，配置加载/恢复只重设基线；
- **本地自动化**：Release 0 warning/0 error，912/912；line 90.73%、branch 79.41%；格式、依赖漏洞、11 个安全 launcher、3 项原生 DesktopHost probe 与 UI/交付合同通过；
- **安全边界**：只处理状态、代次和修订，不持久化条目名称、路径、内容或身份，不修改配置文档；
- **PR CI / main CI**：PR #189 run `31773023830` 成功；squash 合并 `main@50c937ad543e112d56663463410da1db5c985cb3`；main run `31773310428` 全部 34 步成功；
- **结论**：M3a Engineering Pass / Manual Evidence Pending；M3、M4-ready 与外部证据状态不变；
- **下一步**：继续 M3 选择可观察性、保存/恢复组合旅程和匿名证据清理审计。详见 [Stage 136](136-catalog-projection-revision-integration-audit.md)。

### M3b：匿名桌面交互观察链（工程完成）

- **审计基线**：`main@447682250406c9b369142006e6beffff91dfc0ac`；
- **需求对齐**：只把唯一 selection transaction 的最小匿名摘要接入 App 状态，不复制选择控制器，不公开身份，不增加配置或文件命令；
- **实现摘要**：lifecycle 派生 Explicit、选中数量、焦点存在性和选择修订；进入、选择、Escape、系统表面和投影生命周期统一发布，值未变化不重复发布；
- **本地专项**：App Release 0 warning/0 error；DesktopHost lifecycle/UIA 50/50；143 项 UI automation 合同通过；覆盖率 line 90.52% / branch 79.05%；
- **本地完整门禁例外**：两次均为 911/912，既有原生 activation UIA 测试因测试进程未获 Windows 前台许可而安全拒绝；其余 launcher、probe、安全和交付合同通过，保留失败并要求 PR runner 完整复验；
- **安全边界**：快照及 Automation 状态不含 container/item ID、名称、路径或内容，并固定声明匿名观察和零文件操作；
- **PR CI / main CI**：PR #191 run `31775852179` 与 main run `31776200563` 均为 912/912、line 90.21%、branch 79.05%，全部 34 步成功；squash 合并 `main@93c77e59a28820a9aaff13fcc4cb2a59ec91dc4d`；
- **结论**：M3b Engineering Pass / Manual Evidence Pending；本机前台许可例外未在两次独立 runner 复现，M3、M4-ready 与外部证据状态不变；
- **下一步**：M3c 保存失败、显式重试、配置恢复、目录失败/取消与 DesktopHost 隐藏/恢复组合旅程。详见 [Stage 137](137-anonymous-desktop-interaction-observation-audit.md)。

### M3c：保存/恢复与 DesktopHost 组合旅程（工程完成）

- **审计基线**：`main@1c13966a23cf469601aa4389952d7eefe3ba9189`；
- **需求对齐**：只闭合失败保存重试与外部配置基线替换之间的过期意图，并复审既有目录失败/取消—投影隐藏—恢复链；不新增配置存储、目录枚举、DesktopHost 或文件操作系统；
- **实现摘要**：配置恢复/导入成功加载新基线时，仅在保存控制器处于有限失败态时同步清除工作流捕获文档和 UI 重试能力；Clean/Waiting/Saving/Saved 不被该入口打断。目录不可用继续令含引用会话进入 `AwaitingCatalog`、投影进入空工作区并释放 Surface，后续权威目录快照按既有修订链恢复；
- **本地专项**：Release 0 warning/0 error；保存、会话加载与 DesktopHost lifecycle 组合专项 86/86；完整测试 914/915，唯一失败为 Stage 137 已记录的本机 Windows 前台许可安全拒绝；line 90.64%、branch 79.41%，其余本地门禁通过，远端复验见 [Stage 138](138-save-recovery-desktop-host-journey-audit.md)；
- **安全边界**：重置入口只清除内存重试快照和有限状态，不读取/移动桌面文件，不记录配置内容、项目身份或路径；
- **PR CI / main CI**：PR #193 run `31779220650` 与 main run `31780154798` 均为 916/916，line 90.25%/90.26%、branch 79.09%/79.11%，全部 34 步成功；squash 合并 `main@109521212672c0de50b8dad79a530a8b79afdbe4`；
- **结论**：M3c Engineering Pass / Manual Evidence Pending；M3 与 M4-ready 状态不因本切片单独改变；
- **下一步**：M3d 最小匿名交互证据导出与确认清理，复用既有证据库并保持零项目身份。详见 [Stage 138](138-save-recovery-desktop-host-journey-audit.md)。

### M3d：按需匿名交互证据（工程完成）

- **审计基线**：`main@78384d724339d2df41535b955fea1648438d3e17`；
- **需求对齐**：只允许用户确认后冻结一条当前交互的最小匿名快照，复用既有证据清单、逐条导出和单条确认清理；不持续记录、不建立第二个日志目录、不写项目身份或输入明细；
- **实现摘要**：快照固定为 11 个白名单字段，含有限 Host 状态、四类修订/代次、选中数量和三个安全布尔值；`Anonymous=true` 与 `RealFileOperationsAllowed=false` 不可被调用者改写。精确文件名进入既有 256 项/4096 扫描上限、重解析点拒绝、变更复核、64 MiB 导出与写租约清理链；
- **交互边界**：新增“保存匿名交互快照”二次确认；取消零写入，成功后清单仍只显示类型、角色、大小和时间，导出与永久清理继续要求明确单选和独立确认；
- **自动化**：Release 0 warning/0 error；匿名证据/配置导出专项 35/35；首次完整测试 920/920，覆盖率复跑 919/920，唯一失败为 Stage 137/138 已记录的本机 Windows 前台许可安全拒绝；line 90.55%、branch 79.05%；144 项 UI contract、clean-session/batch-accessibility 预检、依赖、启动、持久化和文件安全探针通过；远端复验见 [Stage 139](139-anonymous-interaction-evidence-audit.md)；
- **安全边界**：不含 container/item ID、名称、路径、内容、按键、坐标或输入来源；不持续采样，不读取/移动桌面文件；
- **PR CI / main CI**：PR #195 run `31785742249` 与 main run `31786827386` 均为 920/920，line 90.23%/90.20%、branch 79.05%/79.01%，全部 34 步成功；squash 合并 `main@43b40243cd686298d655eb925ef2f851390f99c1`；
- **结论**：M3 Engineering Pass / Manual Evidence Pending；M4-ready、Phase 0 外部证据、内部 RC 和公开分发状态不变；
- **下一步**：进入 500 项规模、故障恢复和资源长稳自动预检，按独立切片推进 M4-ready。

### M4a：正式产品 500 项规模预检（PR 通过，等待 main 复验）

- **审计基线**：`main@cd4a3ff9a9cc80964be0275d9a1d7a5bf3f062b5`；
- **需求对齐**：使用 100 个方格、500 个不同安全引用覆盖正式 JSON 合同、目录解析、读模型、DesktopHost 投影、搜索/筛选/排序、逐项选择和正式配置保存/恢复；不读取真实桌面，不把缩略图重复探针冒充产品规模，不进入故障矩阵或 24 小时长稳；
- **实现摘要**：新增可复用规模预检和独立 JSON 工具；20 轮内存链、5 轮持久化链均校验有限计数，任一合同或回归预算失败返回非零；CI 增加独立步骤；
- **预算边界**：core pipeline P95 `<1,000 ms`、save P95 `<3,000 ms`、500 项 recovery P95 `<1,000 ms` 仅为共享 CI 自动回归上限，不替代 D23-09 正式 App/支持设备预算；
- **本地自动化**：目标 Release 0 warning/0 error；专项 1/1；完整 Core 921/921；代表运行 core/save/recovery P95 为 9.562/11.570/1.749 ms，沙箱清理和零真实桌面/文件操作声明通过；
- **安全边界**：500 个不同身份均为合成内存引用；只在随机临时目录复用正式配置存储，运行结束清理；报告不含路径、名称或内容；
- **PR CI / main CI**：PR #197 head `3c871b0`，run `31806277821` 为 921/921、line 90.38%、branch 78.82%，完整门禁成功；main 等待合并后运行；
- **结论**：PR Validation Passed / Main Validation Pending；不据此宣称真实设备性能、M4-ready 或 RC；
- **下一步**：远端闭合后进入 M4b 故障恢复矩阵。详见 [Stage 141](141-product-500-item-scale-preflight-audit.md)。

### C1：工程就绪复审（结果尚未执行）

- **审计基线**：`main@508528b`；
- **需求对齐**：继续验证建议/空白、安全引用/真实移动、匿名方格、三项加入、拖放语义、两步撤销和恢复三态；不把当前正式工作区新增能力加入冻结任务；
- **工程就绪**：匿名练习区仍存在；`Start-Issue23UsabilitySession.ps1 -ValidateOnly` 保持 `ResultsPending`，且不打开窗口、不枚举桌面、不写结果或采集身份；
- **现有材料**：任务计划、P1–P5 记录表、通过门槛、主持人逐字纪律和停止检查表齐全；
- **剩余条件**：招募 5 位未参与本功能设计的真实参与者，在同一审计 commit 上完成独立会话；
- **结论**：Conditional Pass（仅工程就绪）；任何 P1–P5 结果仍为 Pending，Issue #23 不得关闭；
- **下一步**：排期并执行 P1–P5。若出现 Critical/High，先以独立修复 PR 回归，再从同一新 commit 重跑完整五人样本。

2026-08-13 负责人确认当前无法安排参与者。P1 launcher 曾启动但无人参与、无任务、无结果、无证据并已退出，因此不计入样本。Stage 129 将 C1 延期到 RC 前证据汇合门；这不是 Pass、Fail、Inconclusive 或 Accepted Risk，Issue #23 继续 OPEN。

### C2：工程就绪复审（结果尚未执行）

- **审计基线**：`main@1d6e817`；
- **需求对齐**：冻结为 probe 输入准备/失效、正式 Passive DesktopHost、正式 App 批量选择无障碍和 Issue #19 汇总；不进入 Explicit、不修改桌面文件；
- **自动化验收**：Issue #19、B6C3 输入转发、B6C3 系统表面/拓扑、A5、BSA 五个 `-ValidateOnly` 入口通过，状态均保持 `PendingManualEvidence`；
- **审计发现**：BSA 手册的 AutomationId 期望仍为 140，实际权威合同为 142；Stage 126 修正该执行阻断文档偏差；
- **人工/实机验收**：尚未执行；不得根据 launcher、UIA、CI 或计数写 Pass；
- **安全与隐私**：只允许专用测试账户、O1～O9、匿名数据和可恢复环境；不主动改变系统状态、不采集证据、不写桌面文件；
- **结论**：Conditional Pass（工程就绪）；Issue #19 保持 OPEN；
- **下一步**：按 Stage 126 的固定顺序执行人工矩阵，真实失败才准入最小修复。

### C5a：等待外部门禁期间的独立安全准备

- **审计基线**：`main@2ea1f6c`；
- **顺序依据**：C1/C2 等待真实参与者/专用人工环境；本切片不依赖其结论，也不进入 C3/C4/C5b；
- **实现摘要**：Stage 127 新增直接使用正式 `ProductConfigurationStore` 的三阶段真实卷证据宿主；
- **安全边界**：固定专用目录、Prepare 拒绝覆盖；不填盘、不切只读、不改 ACL、不输出目标标识/配置内容；
- **自动化验收**：基线、拒绝覆盖、错误环境防假阳性、恢复重试均有测试；Issue #24 `-ValidateOnly` 保持零写入；
- **人工/实机验收**：I24-01/I24-02 未执行，继续 `PendingDedicatedEnvironmentEvidence`；
- **结论**：C5a 工程实现 Pass；C5b 不得提前标记或关闭 Issue #24；
- **下一步**：继续等待 C1/C2 实际结果；按序到达 C5b 后，在专用卷执行三个阶段。
