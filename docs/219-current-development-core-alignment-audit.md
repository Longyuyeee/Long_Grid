# Stage 219：当前开发情况与核心需求对齐审计

日期：2026-08-27

审计基线：`origin/main@d069f11f5fdf47d57b18316efab7a2d55f8e83e2`

状态：`AuditComplete / RequirementsAligned / CoreJourneyIncomplete / GitHubIntegrationPending`

## 1. 结论

当前开发没有偏离最初确定的产品方向。代码、正式导航和统一执行计划仍围绕三项 Core：桌面右键创建并管理盒子、盒子绑定真实文件夹、任务栏美化；自动整理、小组件和 Long助手插件没有抢占当前执行队列。

但“工程能力很多”和“用户已经能完整使用”仍必须分开：

- M1 桌面盒子与文件夹绑定尚未达到出口。BOX-R1-A/B、FOLDER-R1-A～D、PF-007A/B 等工程链已经存在，但安装后桌面空白处右键、真实 FolderPicker、Explorer 指针拖入、盒子间物理拖动、卸载恢复及完整无障碍旅程仍没有在合规环境闭环；
- M2 任务栏美化尚未形成实际效果。正式个性化页有“系统默认/通透”卡片和真实只读检测，但原生适配器目录明确为空，两个预设无写入 Click 入口，当前 Build 26200 返回 `DeniedNoCertifiedBuild`；
- 因 M1、M2 均未达到出口，目前核心里程碑完成数仍为 `0/2`。这不是代码从零开始，而是最后的产品物理闭环和任务栏真实效果还没有完成；
- 正式 UI 已从教程式工程壳层收敛为四项产品导航，真实概览页可以启动；个性化页跨进程可见证据仍受当前 WinUI 上游 `RPC_E_WRONG_THREAD` 风险组合阻断；
- 启动、恢复、配置安全、文件零误操作、原生 HWND、缩略图隔离和自动化质量底座较成熟，应保留，不应继续用新增探针替代核心旅程。

综合判定：`AlignedWithOriginalRequirements / NoProductScopeDrift / DeliveryGapRemains`。

## 2. 核心需求对账

| 核心需求 | 代码与工程事实 | 用户可用事实 | 判定 |
|---|---|---|---|
| 桌面空白处右键创建盒子 | 原生 `IExplorerCommand` DLL、COM 类工厂、MSIX `Directory\Background` 注册和 App 创建意图链已存在 | 未签名预览包不可安装，尚无可丢弃账户中的菜单点击、Explorer 重启和卸载恢复证据 | `EngineeringComplete / PhysicalJourneyPending` |
| 桌面盒子管理 | DesktopHost 已支持创建、移动/布局、折叠、锁定、重命名、删除、外观、选择、打开、撤销和有限拖放 | 鼠标、键盘、Narrator、高对比与完整两分钟旅程尚未集中闭环 | `EngineeringRich / ProductEvidencePending` |
| 盒子绑定文件夹 | 正式 FolderPicker、schema v3 稳定身份、最多 256 个直属项目、Watcher、刷新、解绑/重连和安全打开已接入 | 真实可见 Picker→绑定→变化刷新→打开→离线/恢复完整旅程未验收 | `EngineeringComplete / VisibleJourneyPending` |
| 任务栏美化 | Worker、真实只读探测、冲突检测、恢复日志/租约、准入策略和两张预设卡片已存在 | `TaskbarNativeAdapterCatalog.Resolve()` 返回 `null`；预设没有写入事件，当前没有任何可应用效果 | `SafetyFoundationComplete / NativeEffectNotImplemented` |
| 现代软件 UI | 正式导航为桌面概览、盒子管理、个性化、设置；产品文案已去除主要工程术语 | 概览可见通过；个性化跨进程截图仍 Pending，高对比/减少动画/Narrator 仍需真人证据 | `ProductShellPresent / EvidenceIncomplete` |
| 一键启动与打包 | 根目录 `启动Long方格.cmd`、Release/RC/MSIX/SBOM 脚本存在 | 内部未签名产物不可安装、不可公开分发；正式签名与安装升级卸载矩阵未闭环 | `EngineeringAvailable / ReleaseNotReady` |
| 小组件与 Long助手兼容 | LPWP 协议文档存在 | 没有 Widget Host、插件宿主或双仓运行时互操作 | `M4Backlog / CorrectlyDeferred` |

## 3. 本轮真实测试：预期、实际、差异与修正

| 检查 | 预期 | 实际 | 差异与修正 |
|---|---|---|---|
| 最新主线 | 审计 GitHub 最先进 `main` | `d069f11`，main run `33058359029` 成功 | `None` |
| Release 构建 | 单进程构建 0 warning / 0 error | 首次构建尚未结束时误发第二个构建，第二进程因 `intermediatexaml/LongGrid.App.dll` 被占用而失败 | 关闭本轮 MSBuild/compiler server 后单独重跑；最终 0 warning / 0 error。确认是审计命令并发冲突，不是代码缺陷 |
| Explorer 背景命令 DLL | 真实 x64 COM DLL 可加载、查询并卸载，且不改包状态、不重启 Explorer | 200/200，0 ms；Title/Icon/State/CanonicalName/Flags/Subcommands 全部通过；handle `140→142`；可卸载 | `Difference=None / Pass` |
| 文件夹绑定与内容 | 真实临时 NTFS 文件夹覆盖绑定、内容读取和打开边界 | 14/14 通过，失败 0、跳过 0 | `Difference=None / Pass` |
| 任务栏只读探测 | 识别真实任务栏且不修改系统 | Build 26200；主/副任务栏均由 Explorer PID 6932 拥有；无冲突；`DeniedNoCertifiedBuild`；身份不变；`modifiedSystemState=false` | `Difference=None / Pass`，只证明安全探测，不证明美化效果 |
| 正式启动窗口 | DesktopHost/控制中心在 10 秒预算内就绪并稳定 10 秒，退出零残留/零临时配置写入 | 控制中心 6,123 ms，DesktopHost 2,099 ms；重定向成功；最终进程 0、临时写入 0 | `Difference=None / Pass`；控制中心比 Stage 218 的 2,331 ms 慢但仍在预算内，后续真实性能矩阵继续观察 |
| UI 结构合同 | 正式产品结构和 AutomationId 可复核 | 195 个唯一 AutomationId，contract-only Pass | `None`；不替代真人可见/无障碍证据 |
| MSIX 生命周期 | 无签名/无可丢弃账户时禁止安装；静态合同仍应可验证 | 默认入口按预期拒绝 live mutation；`-ValidateOnly` 验证身份、Publisher、CLSID、`Directory\Background` 和不可分发状态并通过 | `PendingSignedPackageAndDisposableWindowsProfile`，没有绕过门禁 |

GitHub 主线证据：run `33058359029` 为 `1353/1353`，coverage lines `90.10% (46644/51768)`、branches `75.91% (15286/20138)`；格式、Release 构建、启动、交互 Surface、UI 合同、恢复/资源、文件安全、Worker、依赖漏洞和内部 RC 审计全部成功。

## 4. 文档与代码一致性审计

本轮发现并纠正两处状态滞后：

1. `PRODUCT_EXECUTION_PLAN.md` 的代码基线仍停在 Stage 218 合并前的 `36b583f`，现更新为 `d069f11` 并指向本审计；
2. Stage 218 已经通过 PR #265 合入主线且 main CI 成功，但状态仍为 `IntegrationPending`，现提升为 `Integrated` 并补记合并提交与 main run。

没有发现功能被错误标为完整：计划仍明确 M1/M2 未完成，任务栏没有把只读检测写成真实效果，小组件和插件仍位于 M4。

## 5. 工程风险

- `MainWindow.xaml` 2,171 行、`MainWindow.xaml.cs` 5,226 行、`App.xaml.cs` 4,019 行、`WindowsProductDesktopHostReadOnlySurface.cs` 3,415 行；集中度仍高。后续只在核心旅程修改触及相应区域时就近提取，不另开无用户结果的长期重构阶段；
- 当前正式产物未签名、不可安装、不可公开分发；源码合同和内部 RC 不能代替安装后用户旅程；
- WinUI 跨进程自动化风险会影响截图/UIA 证据，但不能成为无限延期物理测试的理由。合规可丢弃环境可用后应优先补证；
- 控制中心本轮显式启动 6.123 秒，虽低于 10 秒预算，但比上一阶段样本明显变慢。单样本不能判定回归；M1 物理矩阵应采集多轮冷/热启动分布，不再只记录一次值。

## 6. 后续开发方向与严格顺序

### 下一唯一用户结果：M1 物理闭环

在具备签名身份或明确受控测试签名、可丢弃 Windows 账户/VM 后，只执行下面一条完整旅程：

1. 安装预览包，确认桌面空白处出现“新建 Long方格盒子”；
2. 创建盒子并验证移动、缩放、折叠、锁定、重命名、外观和删除/撤销；
3. 通过真实 FolderPicker 绑定测试文件夹，验证新增/删除/重命名刷新、打开、离线和恢复；
4. 用真实 Explorer 指针拖入，再在盒子间改归属并撤销；
5. 复核鼠标、键盘、Narrator、高对比、减少动画和截图；
6. 重启 Explorer、升级/重装、卸载，确认菜单和系统状态恢复；
7. 全程比较测试文件的路径、哈希和数量，任何未授权变化均判 Fail。

### 随后：M2 任务栏真实效果

只在通过 R2B1-A 的可丢弃 Guest 中实现和认证第一个 `Clear → SystemDefault` 原生适配器，完成 15 秒确认、崩溃/Explorer 重启/禁用/卸载恢复后，再扩展深色半透明、浅色半透明和 Acrylic。宿主日常桌面继续禁止试写。

M1/M2 未关闭前，不启动自动整理、小组件、Long助手运行时兼容或新的外围功能阶段。
