# Stage 140：换机开发交接与当前状态审计

- 审计日期：2026-08-15
- 仓库：`https://github.com/Longyuyeee/Long_Grid.git`
- 默认分支：`main`
- 已合并实现基线：`main@8e7ee34dd71e583f58294f83244983779ab37ebe`（PR #205）
- 本文性质：换机交接快照；GitHub `origin/main` 始终是源码与计划的最终权威

## 1. 交接判定

当前代码、测试、审计文档、协议和构建入口均已进入 Git/GitHub 管理，**继续开发不依赖当前电脑上的未提交文件或本地证据目录**。换机前可以暂停功能开发；新电脑只需从 GitHub 干净克隆并复验基线。

M1、E2/M2、M3、M4a、M4b、M4c1 与 M4c2a 工程链已经完成，当前处于 **M4c2a Session Contract Engineering Pass / M4c2b Product Telemetry Pending / M4c2c 24 小时实机 Pending**。下一步是接入正式受限 worker 与匿名状态修订遥测，再执行同一新 commit 的长稳会话；这不等于 Phase 0 已退出、M4-ready、内部 RC 可分发或产品已经对齐 iTop Easy Desktop 的全部能力。

本机工作区在审计开始时无未提交文件。唯一 Git stash 为 `stage125-local-draft-before-origin-main-sync`，仅包含旧 README 和 Stage 103 的 9 行历史草稿；其内容已被后续 Stage 125～139 主线事实取代，**不得在新电脑恢复或作为交接数据使用**。`TestResults*`、`artifacts/`、`bin/`、`obj/` 均为被忽略的可再生成目录，不需要迁移。

## 2. 当前可运行与可见能力

| 范围 | 当前事实 | 判定 |
| --- | --- | --- |
| 正式 App/UI | `LongGrid.App` 已有可启动 WinUI 界面、工作区/方格、状态与恢复交互、证据管理和 UI Automation 合同 | 工程可运行；仍需真人可用性和动态系统矩阵 |
| 现代视觉 | 已有现代扁平视觉基础、紧凑响应式布局、主题/高对比合同和平滑状态交互 | 已有产品基础；竞品级视觉精修与完整动效仍未收口 |
| 品牌图标 | `assets/brand/rc1` 已提供 L+方格的浅色、深色、单色、高对比及 16～256 px SVG/PNG 资产，打包链会生成 MSIX 精确尺寸资产 | 已实现 RC1 品牌资产 |
| 桌面发现与分组 | 只读枚举用户/公共桌面第一层元数据；正式方格支持创建、重命名、锁定、折叠、有限外观/布局、加入/移除/改归属引用及一次撤销 | M1～M3 工程闭合 |
| DesktopHost | 正式产品自有 Hidden/Passive/Explicit Surface、生命周期、显示拓扑、UIA 和匿名选择观察已接线；危险/未知系统状态 fail-closed | 工程闭合，人工实机证据 Pending |
| 配置与恢复 | 正式配置存储、latest-wins、失败恢复、布局恢复预览/确认/撤销、故障证据库存已实现 | 工程闭合，专用真实卷证据 Pending |
| 匿名交互证据 | 用户二次确认后可保存严格 11 字段快照，并逐条导出或确认清理；不记录路径、名称、内容或输入明细 | M3d Engineering Pass |
| 一键启动 | `eng/Start-LongGrid.ps1` 可锁定恢复、构建并启动，也支持 `-ValidateOnly` | 已实现 |
| 一键构建/打包 | `eng/Pack-LongGrid.ps1` 生成自包含便携 ZIP；`eng/Pack-LongGridMsix.ps1` 生成 unsigned MSIX；`eng/Build-LongGridReleaseCandidate.ps1` 聚合 ZIP、MSIX、SBOM 与证据 | 工程链已实现；未签名、不可公开分发 |

## 3. 与最初需求和竞品目标的真实对齐度

| 最初目标 | 已完成 | 尚未完成/边界 |
| --- | --- | --- |
| 桌面文件整理与方格分组 | 方格模型、引用式整理、批量选择、加入/移除/改归属、恢复与撤销已进入正式 App | 默认仍不读取文件内容、不移动/删除真实桌面文件；“一键物理整理”必须另行批准并提供预览、确认、回滚和冲突策略 |
| 对标 iTop 的低门槛交互 | 已建立发现、工作区、建议/预览原型、显式确认、状态反馈、恢复入口和无障碍合同 | 5 人无提示可用性测试、真实拖放、完整视觉打磨与竞品旅程复测仍 Pending |
| 任务栏美化 | 已完成竞品/Windows 边界审计与独立模块规划 | 尚无正式任务栏美化运行时；不注入 Explorer、不使用驱动，不承诺完整系统任务栏换肤 |
| 自定义窗口效果 | App 自有窗口已有主题、布局与状态视觉；DesktopHost 只操作产品自有窗口 | 尚无面向任意第三方窗口的广泛特效/改造能力；不得把产品自有 Surface 误写为全局窗口美化 |
| 小组件 | 已完成 Widget Host 架构、权限、生命周期、崩溃隔离和资源配额设计 | 尚无 Widget Host 正式运行时与桌面小组件布局实现 |
| Long助手插件兼容 | LPWP 1.0、JSON Schema 与 Long助手实施交接包已就绪 | Long助手插件只有声明 Widget Surface 且通过兼容校验后才可加载；Long方格侧宿主、签名/权限/隔离和兼容测试尚未实现 |
| 现代、扁平、华丽、平滑动效 | 已有品牌和产品 UI 基础 | 需要在核心可靠性与 M4 之后做统一设计令牌、动效预算、降级策略和逐旅程精修，不能用装饰替代可用性证据 |
| L 与格子结合图标 | RC1 全套源文件和多尺寸导出已进入仓库及打包链 | 正式发布前仍需品牌负责人确认最终稿 |
| 稳定、快速、容量可控 | 已有锁定依赖、覆盖率、资源/安全探针、缩略图隔离、有限队列/扫描和可复现便携包 | 500 项正式 App 规模、故障恢复矩阵、24 小时资源长稳尚未完成；此项是下一阶段主线 |

因此，当前最准确的产品描述是：**Long方格已经拥有可运行的现代桌面管理工程骨架和完整安全/恢复基础，但真实文件整理、竞品级 UI 收口、任务栏、通用窗口效果和小组件生态仍是后续阶段。**

## 4. 最近验证基线

- PR #195 CI run `31785742249`：920/920，line 90.23%（25902/28708），branch 79.05%（8392/10616），完整 34 步通过；
- 合并后 main run `31786827386`：920/920，line 90.20%（25896/28708），branch 79.01%（8388/10616），完整流水线通过；
- 本地 Release build：0 warning / 0 error；匿名证据/配置导出专项 35/35；首次完整测试 920/920；
- 本地覆盖率复跑 919/920；唯一失败为已记录的 Windows 前台许可 UIA 安全拒绝，远端独立 runner 未复现；没有放宽前台/NoActivate 合同；
- 144 项 AutomationId 合同、clean-session、批量无障碍、依赖漏洞、启动、100 次配置持久化/故障注入和文件安全探针通过；
- 真实 Narrator、高对比/文本缩放、物理输入、动态 Explorer/系统表面、专用真实卷与五人测试继续 Pending。

## 5. 新电脑恢复步骤

前提：Windows 11 x64、Git、`global.json` 指定的 .NET SDK 8.0.400（允许 latest patch）、可访问 NuGet/GitHub。Windows App Runtime 2.3.1 x64 缺失时启动入口会明确失败。

```powershell
git clone https://github.com/Longyuyeee/Long_Grid.git
Set-Location ./Long_Grid
git switch main
git pull --ff-only

dotnet --info
dotnet restore ./LongGrid.sln --locked-mode

powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-LongGrid.ps1 `
  -Configuration Release `
  -ValidateOnly

dotnet build ./LongGrid.sln --configuration Release --no-restore
dotnet test ./LongGrid.sln --configuration Release --no-build

powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Test-LongGridUi.ps1 `
  -ContractOnly
```

复验通过后，使用以下命令启动界面：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-LongGrid.ps1
```

不要复制旧电脑的 `TestResults*`、`artifacts/`、`bin/`、`obj/`。产品运行数据默认位于当前 Windows 用户的本地应用数据目录，属于用户侧状态而不是源码权威；除非专门做迁移验证，否则不应复制，更不应提交到 Git。任何开发开关都不是用户授权或发布默认值。

## 6. 恢复开发后的固定顺序

1. **M4a：500 项规模预检**——已由 PR #197 合并到 `main@91ff6fd`，PR/main 完整门禁均通过；
2. **M4b1：持久化与目录恢复预检**——PR #199 与 `main@a47a19d` 的 5 场景和完整门禁均通过；
3. **M4b2：原生生命周期故障矩阵**——PR #201 与 `main@8d04e2f` 的 5 场景和完整门禁均通过，M4b Engineering Pass；
4. **M4c1：加速资源所有权/释放预检**——200 轮矩阵与 PR/main 双重门禁已通过，判定 Accelerated Engineering Pass；
5. **M4c2a：正式 App 24 小时资源合同**——入口与预算已冻结并通过远端门禁；
6. **M4c2b1：匿名 revision telemetry**——同用户只读管道、严格连续序列与会话漂移计算已经 PR/main 双重门禁闭合；
7. **M4c2b2：正式 worker 接线**——独立受限产品 runtime、probe 反向复用和正式 App 受控生命周期已通过 PR/main 双重门禁，判定 Engineering Pass；
8. **M4c2c：真实执行**——从 M4c2b2 合并提交在支持设备验证 UIA、目录、worker/Profile、窗口、handle/thread/private bytes 与状态漂移；
9. **外部证据汇合**——按 C1 → C2/C3 → C4 → C5b → C6 完成五人测试、输入/无障碍/系统表面、显示硬件、专用卷和 ADR/Issue 收口；
10. **内部 RC**——许可证、签名、安装/升级/卸载/回滚、正式品牌确认完成后，才允许把内部工程产物提升为可分发候选；
11. **MVP 后扩展**——任务栏外观实验、LongBar、Widget Host、LPWP 插件兼容和广泛窗口视觉能力分别立项、隔离权限与资源预算，不进入核心常驻路径。

每个切片继续遵守：从最新 `main` 建立 `codex/<slice>` 短分支，先写范围/非目标和验收，完成实现与自动化，更新审计文档，审阅差异，推送 PR，等待 PR CI，合并后再次确认 main CI。不得把自动化、probe 或设计文档冒充真人/实机/发布证据。

## 7. 换机前最终检查表

- [x] 功能实现基线已在 GitHub `main`；
- [x] 工作区无未提交源码；
- [x] 本地 stash 已判定为过期历史草稿，不需迁移；
- [x] 本地测试和构建产物均可再生成且被 Git 忽略；
- [x] 仓库范围未发现 `.env`、PFX、SNK、PEM 或私钥文件；
- [x] 一键启动、便携包、unsigned MSIX 和内部 RC 构建入口已文档化；
- [x] 已完成/未完成能力、人工证据与发布边界已明确；
- [x] 本交接文档已由 PR #196 合并，`main@cd4a3ff` 的 34 步 CI 通过。

换机后的第一条事实校验应是 `git status --short --branch` 与 `git log -1 --oneline`；若本文中的 SHA 与新的 `origin/main` 不同，以 GitHub 合并历史和更晚的审计文档为准。
