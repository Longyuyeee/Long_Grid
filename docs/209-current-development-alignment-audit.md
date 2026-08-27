# Stage 209：当前开发情况与原始需求对齐审计

日期：2026-08-26

审计基线：`origin/main@183829a78425e54966a4afe3447693cf89ec825b`

审计范围：正式 App、DesktopHost、Explorer 背景命令、文件夹绑定、任务栏、美术资产、启动/打包链、自动化与现有产品计划。本文只汇总当前事实；执行顺序仍以 [PRODUCT_EXECUTION_PLAN](PRODUCT_EXECUTION_PLAN.md) 为唯一权威。

> 历史状态提示：本文的任务栏结论已被 Stage 210～215 取代。R1A/R1B、R2A1、R2A2a/R2A2b 与 R2A2c 已完成工程链；当前仍无原生样式写入/恢复，不得继续引用本文的“M2 尚未开始”作为最新状态。

## 1. 结论

项目没有偏离最初重新确认的三项核心目标，但“工程能力很多、完整用户旅程很少”的问题仍然存在：

1. **桌面盒子**：正式配置事务、DesktopHost 呈现、移动/缩放/锁定/折叠/外观、原生 Explorer 背景命令、OLE Link 拖入和盒子间改归属均已有工程链；真实安装后的桌面空白处右键、物理鼠标完整旅程仍未通过。
2. **文件夹绑定**：单文件夹持久化、稳定身份、Picker 接线、绑定/解绑、直属内容、刷新、变更监听、安全打开、离线/权限/替换恢复均已通过真实 NTFS 测试；可见 Picker、刷新/打开、键盘和 Narrator 旅程仍 Pending。
3. **任务栏美化**：仍只有需求、交互和技术边界文档。正式源码中没有 Taskbar 运行时适配器，个性化页明确显示“任务栏预设将在核心适配器完成后出现”。M2 尚未开始。

因此当前不能称为“核心功能完成”或“可交付竞品替代品”。更准确的状态是：**M1 工程链基本形成、产品证据未收口；M2 运行时未开发；M5 只有未签名内部交付链。**

## 2. 原始需求逐项核对

| 原始需求 | 代码与产品事实 | 当前状态 | 剩余出口 |
|---|---|---|---|
| 桌面空白处右键创建盒子 | `LongGrid.ExplorerCommand`、唯一 CLSID、MSIX `Directory\\Background` 注册和 App 有限激活合同存在；原生 DLL 真实探针通过 | `EngineeringComplete / NativeDllPass / ProductEvidencePending` | 在可丢弃账户安装签名/允许安装的包，验证菜单位置、坐标、多显示器、Explorer 重启和卸载清理 |
| 桌面直接管理盒子 | DesktopHost 已接正式工作区，具备布局、锁定、折叠、标题策略、外观、删除/撤销、选择、打开和 UIA；控制中心也有真实盒子管理页 | `EngineeringComplete / RealHwndPass / ProductEvidencePending` | 两分钟物理鼠标、键盘、Narrator、高对比和减少动画旅程 |
| 盒子绑定真实文件夹 | FOLDER-R1 A～D 代码链完整；最多 256 个直属项、稳定身份、失效状态、事件/健康探测和打开前复核均存在 | `EngineeringComplete / RealFilesystemPass / ProductEvidencePending` | 真实可见 Picker、取消零写入、刷新、打开、离线/权限恢复与辅助功能证据 |
| Explorer 拖入、盒子间整理与撤销 | 正式 OLE `IDropTarget` 和安全 Link 提交存在；选中引用可跨盒子改归属并一次撤销 | `EngineeringComplete / RealHwndPass / ProductEvidencePending` | 用真实 Explorer 指针完成拖入；用真实指针完成改归属和撤销，不以坐标证据入口代替 |
| 正常的软件 UI | 正式一级导航为“桌面概览 / 盒子管理 / 个性化 / 设置”，首屏和盒子管理使用真实工作区；浅/深、宽/紧凑渲染已有证据 | `EngineeringComplete / ManualEvidencePending` | 高对比、减少动画、物理键盘/Narrator；个性化页任务栏占位必须由真实功能替换 |
| 现代、扁平、精致 UI 与平滑动效 | Design Token、Mica、卡片、响应式导航和 RC1 品牌资源存在 | `PartiallyAligned` | 当前主题选择仍只在进程内；动效/触控/高对比矩阵未闭环，DesktopHost 视觉仍偏工程化 |
| “L + 方格”图标 | RC1 有浅/深/高对比 SVG 和 16～256 px PNG；App 品牌头、原生命令和 MSIX 资产链均使用该资源 | `EngineeringComplete / FinalVisualApprovalPending` | ICO、开始菜单/任务栏/桌面快捷方式和系统设置列表实机矩阵，最终品牌批准 |
| 任务栏透明、颜色、材质与恢复 | 正式 `.cs/.cpp/.h` 中没有 Taskbar 实现；现有 `DwmSetWindowAttribute` 只修饰 Long方格自己的 DesktopHost 窗口 | `NotStarted` | TASKBAR-R1～R4：探测、隔离适配器、预设、15 秒确认/回退、Explorer 重启/冲突/卸载恢复和真实矩阵 |
| 一键启动 | 根目录 `启动Long方格.cmd` 调用正式 PowerShell 启动链并传播退出码 | `EngineeringComplete` | 安装后开始菜单入口、可选桌面快捷方式和开机启动仍属 M5 |
| 一键打包 | `eng/Build-LongGridReleaseCandidate.ps1` 聚合 portable ZIP、unsigned MSIX、SPDX、SHA-256 和源码提交 | `InternalUnsignedOnly` | 根目录暂无双击打包入口；正式证书、签名环境、安装/升级/卸载证据和分发批准缺失 |
| 小组件与 Long助手插件 | LPWP/Widget 协议文档存在，正式 Widget Host、权限运行时和 Golden Fixtures 双仓验证不存在 | `DesignOnly / Deferred` | M1、M2 完成后进入 M4；不得抢占三项核心 |

## 3. 代码健康与开发效率审计

### 3.1 正向资产

- 186 个正式 C# 源文件和 109 个测试文件形成较完整的 Core/Infrastructure/App 分层；
- 配置写入使用权威修订、原子保存、失败补偿和有限撤销，文件操作默认安全引用；
- DesktopHost、原生 Explorer 命令、缩略图 Worker、配置恢复和交付脚本均有失败关闭门禁；
- CI 同时覆盖格式、Release 构建、测试/覆盖率、恢复、资源、文件安全、依赖漏洞和内部 RC 审计；
- 最近 M1-A/M1-B 工作聚焦真实产品证据与安全会话，没有转向小组件或其他外围宽度。

### 3.2 主要风险

| 风险 | 事实 | 处理方向 |
|---|---|---|
| 产品证据长期积压 | 多项能力停留在 `EngineeringComplete / ProductEvidencePending` | 不再新增 M1 邻接抽象；下一用户可见工作只做完整物理旅程 |
| WinUI 外部 UIA 崩溃 | 本机 runtime `2.4.0.0` / XAML `3.2.3.0` 会触发已记录的 `RPC_E_WRONG_THREAD` fail-fast | 保持启动前阻断；换安全运行时或专用机器取证，不关闭 UIA/无障碍规避 |
| 大文件维护成本 | `MainWindow.xaml` 2,112 行、`MainWindow.xaml.cs` 5,538 行、`App.xaml.cs` 4,223 行、DesktopHost Surface 3,701 行 | 只在交付 M1/M2 用户结果时按页面/适配器边界渐进拆分，不启动脱离产品结果的大重写 |
| 任务栏完全缺失 | 正式任务栏源文件命中为 0，当前仅有个性化页占位说明 | M1 证据收口后直接进入 TASKBAR-R1，不继续扩展桌面底座 |
| 交付入口不一致 | 根目录有一键启动，内部一键打包只在 `eng/` | M5 增加根目录薄包装；底层仍只保留一个权威 RC 脚本 |
| 文档状态滞后 | 统一计划顶部仍指向 PF-007B 分支，需求总账仍写“文件夹绑定未闭环” | 本轮更新到 `origin/main@183829a` 并以本审计纠正冲突 |

## 4. 本轮真实验证：预期、实际与差异

| 验证 | 预期 | 实际 | 差异/结论 |
|---|---|---|---|
| Git 基线 | 审计最新 `origin/main`，工作区无旧分支误判 | 本地审计基线与 `origin/main@183829a` 一致，主线 push CI `32952167104` 为 success | `Difference=None` |
| 格式与 Release | 格式无差异，0 warning / 0 error | `dotnet format --verify-no-changes` 通过；Release 0/0 | `Difference=None` |
| 完整测试 | 所有测试通过且不跳过 | 1,272/1,272，通过 1,272，失败 0，跳过 0 | `Difference=None` |
| 独立覆盖率 | lines ≥90%，branches ≥75%，不聚合历史报告 | 独立临时结果目录：lines 90.41%（44,304/49,004），branches 75.95%（14,660/19,302） | `Difference=None` |
| 正式 UI 合同 | 产品导航和关键可访问控件合同通过 | 188 个 required AutomationId、4 个 AccessKey、3 个主题模式，结果 Pass | 自动合同通过；不替代物理 Narrator/键盘 |
| Explorer 原生命令 | x64 `/W4 /WX` 构建，200 次 COM 调用、可卸载、句柄有限、零包状态变化 | DLL/Probe 均 0 warning/0 error；200 次 0 ms；handles 141→143；`DllCanUnloadNow=S_OK`；包状态和 Explorer 均未变化 | `Difference=None`；仍不等于已安装菜单可见 |
| 普通隔离产品启动 | 正式 Release 进程存活、配置隔离、夹具不变 | PID 18740 存活并到达四阶段；正常配置 `MISSING→MISSING`；夹具指纹 `A697FC8C...6001` 前后一致；精确清理通过 | `Difference=None`；未执行物理输入 |
| 外部自动化会话 | 安全运行时才允许启动 | 当前真实运行时探针命中 `2.4.0.0 / 3.2.3.0`，返回 `BlockedByKnownUpstream`、`startsProcess=false`、`createsEvidenceSession=false` | 安全门禁符合预期；M1 可见物理证据继续 Pending |
| 内部 RC 合同 | 未取得签名时不得伪报安装/分发就绪 | ValidateOnly 聚合 ZIP/MSIX/SPDX/SHA/source；明确 `signed=false`、`installable=false`、`distributionApproved=false` | 对齐，M5 未完成 |

## 5. 是否发生开发偏移

**产品方向没有偏移，交付方式仍有历史性偏移尚未完全消除。**

- 没有偏移：最近主线工作均围绕右键创建、盒子管理、文件夹绑定、Explorer 拖入、盒子间整理和 M1 证据安全；小组件、Long助手和自动整理没有抢占当前队列。
- 尚存偏移：工程验证成熟度显著高于用户可见完成度；同一 M1 被拆成大量工程切片后，物理闭环仍未完成。
- 必须纠正：后续不能再以新增 reducer、证据入口、静态合同或文档数量代替真实用户旅程；环境阻断应明确 Pending，然后安排可满足条件的测试环境。

## 6. 后续开发方向与收口顺序

达到“核心功能完成”还需三个收口段，不包含后续 M3/M4 扩展：

1. **M1 产品证据收口**：在没有既有 Long方格进程的可丢弃账户、可安装包和安全 UI 运行时下，完成桌面右键创建 → 设置盒子 → Picker 绑定/刷新/打开 → Explorer Link 拖入 → 盒子间改归属/撤销 → 高对比/减少动画/键盘/Narrator → 卸载恢复。任何失败先记录实际差异再修正。
2. **M2 任务栏核心开发**：严格按 TASKBAR-R1 探测与独立故障域 → R2 有限预设 → R3 确认/自动回退/恢复 → R4 Windows build、多显示器、Explorer 重启、冲突、全屏、高对比和卸载真实矩阵交付。
3. **M5 Beta 收口子集**：受保护签名、安装/升级/卸载、开始菜单入口、品牌实机、性能/稳定性/无障碍和最终恢复门禁。根目录打包入口只是薄包装，不能代替签名与生命周期证据。

M3 自动整理/增强和 M4 小组件/插件继续排在核心之后。下一步唯一目标仍是 M1 物理产品旅程；若当前机器继续受 WinUI/UIA 组合或高权限常驻进程阻断，应换专用环境，而不是继续在同一代码附近增加新底座。
