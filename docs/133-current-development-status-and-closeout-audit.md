# Stage 133：当前开发状态与收尾方向审计

审计日期：2026-08-14

审计基线：`main@272dad6288c5d92d1f76e6f810afe45609c5cb9a`（PR #184；main CI 31727572596 成功）

## 1. 总体结论

项目仍处于 **Phase 0 外部证据待汇合、桌面 MVP 工程轨道推进中**，尚不是内部 RC。正式配置、Catalog、工作区编辑/保存/恢复、权威显示拓扑、DesktopHost 生命周期以及 Passive/Explicit/Hidden 表面已经进入产品程序集；E2a 又关闭了 Prepared Intent 原子至多一次消费和选择事务入口。当前最近的唯一工程阻断是 E2b：正式产品输入来源、键盘焦点代理、项目 pointer/UIA 选择尚未实现。

这不是方向偏移。Stage 129 已批准在无法安排参与者和专用环境时，工程轨道先推进到 M4-ready，再强制汇合外部证据。#19、#20、#23、#24、ADR-0001、许可证、签名和安装生命周期仍是不可伪造门禁；任何自动化成功都不能把它们升级为 Pass。

## 2. 可复读基线

| 领域 | 当前事实 | 判定 |
| --- | --- | --- |
| GitHub | 无开放 PR；远端只保留 `main`；最近五次 main CI 成功 | 分支与主线卫生通过 |
| 自动化 | 888/888；line 90.52%（23836/26332），branch 80.12%（7552/9426）；依赖漏洞门禁通过 | 工程基线通过 |
| 产品数据链 | 正式配置 Store、Catalog、Session、ReadModel、统一 Commit/Save、恢复/导入导出与有限证据生命周期已接 App | 成熟，但真实卷证据仍 Pending |
| 显示与宿主 | 权威只读显示拓扑、每显示器产品 HWND、UIA Fragment、Passive/Explicit/Hidden、系统表面失败关闭已接线 | M1 工程完成 |
| Intent | 四重门禁、输入归一化适配器、Prepared Intent、原子消费、admission/surface/selection transaction 已存在 | E2a 工程完成 |
| 正式输入 | App 只构造消费器；未调用 forward/consume/select；无产品 activation HWND 或 pointer/keyboard message source | E2b 未实现 |
| 可访问选择 | 产品 UIA 根仅在 Explicit 暴露空 Selection；`GetSelection()` 返回空，项目没有 SelectionItem/Invoke | E2b2 未实现 |
| 文件权限 | 桌面内容读取、写入、移动、删除与自动整理没有随交互链开放 | 安全边界保持 |
| 外部验证 | #19/#20/#23/#24 均 OPEN；物理输入、Narrator、硬件动态显示、五人测试、真实卷仍 Pending | Phase 0 未退出 |
| 发布 | 可重复便携包、unsigned MSIX、SBOM 和 CI 审计工具已存在；许可证、受保护签名、真实安装/升级/卸载/回滚未完成 | 不可分发 |

## 3. 当前架构与需求对齐

产品依赖方向仍为 Core → Infrastructure → App。Core 持有 Intent、admission、选择与事务规则；Infrastructure 持有 Win32、配置 Store、Catalog、拓扑和 DesktopHost；App 只做组合、UI 线程封送和有限状态展示。E2a 没有把 HWND 或文件能力泄露到 App，也没有复制选择状态机。

Stage 132 对 E2b 的设计是必要修正：M1 Passive 整窗穿透，直接添加 `WM_LBUTTONDOWN` / `WM_KEYDOWN` 会形成不可达的伪输入链。正式方案必须用 Region 受限的产品 activation HWND 接受首次 pointer/UIA；keyboard-only 从 App 标准命令经生命周期复核后聚焦来源；进入 Explicit 后三条路径统一调用现有 selection transaction。

## 4. 与原计划的偏移审计

1. **外部证据顺序改变但已批准**：最初计划先执行 C1～C6，再推进 MVP；Stage 129 明确批准双轨。工程可前进，M4 后必须停止并执行 X1～X5，因此不是静默偏移。
2. **M3 的大量基础提前存在**：配置、正式工作区 UI、编辑、保存、恢复和证据工具早于 M2 完成。它们通过独立门禁保持零桌面文件操作，后续 M3 应做“交互链集成差距审计”，不能重复建设或把既有配置能力误写成桌面直接交互已完成。
3. **RC 工具提前、RC 资格未提前**：便携包、unsigned MSIX、SBOM 和交付集审计属于开发准备；无许可证、签名、真实安装生命周期和外部证据时不得创建 RC 标签或分发。
4. **旧状态文档有时间性漂移**：Stage 125 顶部仍记录早期 SHA、873 测试及“尚未消费 Intent”。本审计同步修正当前基线；历史切片中的旧数字继续作为当时证据，不批量改写。

## 5. 风险排序

### P0：E2b 输入与焦点所有权

activation HWND 必须受四重门禁、同进程/线程/instance/generation 复读和有限 Region 约束。MainWindow Deactivated 只能对精确验证的当前来源放行；未知或陈旧 HWND 必须按 FocusLost 取消。部分创建、Region 应用、聚焦、消费或销毁失败都要回到 Hidden/Passive，并且不能留下输入遮罩。

### P0：三路径业务一致性

pointer、keyboard、UIA 必须得到同一 selection revision、focused/anchor/selected 结果。UIA Pattern、事件和视觉状态必须来自同一事务快照，不能分别维护影子选择状态。

### P1：外部证据债务

物理输入/Narrator、动态显示、五人任务和真实卷均无法由当前环境替代。M4-ready 后必须停止新增功能，执行 X1 → X5；真实失败需要最小修复并重跑受影响证据。

### P1：发布声明

unsigned 包和 SBOM 只能称为 CI 审计产物。许可证、签名、安装/升级/卸载/回滚、支持渠道未关闭前，README、Release 或 Issue 不得使用“RC 可用”“可安装发布”等表述。

### P2：维护性债务延后处理

`App.xaml.cs`、`MainWindow.xaml.cs` 和部分协调器已经较大，但当前仍处于输入链闭合和证据基线变化期。按 Stage 125，只有 M4 基线稳定后才做等价拆分；现在的大范围重构会扩大 E2b 回归面，不应与输入能力混入同一 PR。

## 6. 后续唯一工程顺序与验收目标

### 第一步：E2b1 activation source 与激活闭环

- 每显示器最多一个产品自有 ToolWindow，Region 仅覆盖有限激活按钮；区域外穿透；
- 首次显示不激活，pointer/UIA 与 App keyboard 命令生成来源可证明、单调且去重的 action；
- 生命周期统一创建、隐藏、恢复、取消和逆序释放；系统事件、投影替换、Escape、关闭均失效 Prepared/Explicit；
- 禁止 Hook、Raw Input、SendInput、RegisterHotKey、Explorer/WorkerW 和文件 API；
- 成功、门禁关闭、陈旧/重放、auto-repeat、部分失败、焦点丢失和资源释放均有自动化。

### 第二步：E2b2 选择与可访问交互

- 主 surface pointer、来源键盘命令和 UIA SelectionItem/Invoke 统一映射为 `ProductDesktopSelectionRequest`；
- 项目使用当前投影的匿名 `item:N`，目标/lease/代次变化立即拒绝；
- UIA GetSelection、IsSelected、SelectionContainer、焦点与事件读取同一 selection snapshot；
- 三路径结果一致，取消后 Pattern/焦点/命中恢复安全态；
- E2b1/E2b2 PR CI、合并后 main CI 均通过后，才可写 E2/M2 工程 Pass。

### 第三步：M3 集成差距与 M4-ready

- 只补 DesktopHost 交互与既有配置/恢复/保存链之间的缺口，不重建现有产品工作区；
- 保持安全引用为默认，不自动移动真实文件；
- 运行正式产品核心旅程、500 项自动规模、故障恢复和资源长稳自动预检；
- 到达 `ReadyForExternalValidation` 后停止功能开发，执行 X1 五人测试、X2 输入/Narrator、X3 动态显示、X4 真实卷、X5 ADR/Phase 0 裁决。

## 7. 本次审计判定

- M1：工程 Pass；
- E2a：工程 Pass；
- E2b：设计完成、实现未开始；
- M2/M3/M4：不得标记完成；
- Phase 0、内部 RC、公开发布：均未通过。

下一代码切片固定为 `E2b1 activation source`。若实现需要扩大文件权限、全局输入、Explorer 集成或发布范围，必须停止并先更新决策记录。
