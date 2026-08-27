# Stage 218：UI-R1F 产品语言与状态收敛审计

日期：2026-08-27

开发基线：`origin/main@36b583fdac098bddffb05b3848222576f709eae8`

状态：`EngineeringComplete / ContractPass / RealOverviewPass / PersonalizationVisibleEvidencePending / IntegrationPending`

## 1. 目标与结论

结合代码和文档复审发现，产品方向已收敛到桌面盒子、文件夹绑定和任务栏美化三项 Core，但正式界面仍残留“只读适配器、权威快照、认证 Build、恢复事务、独立检测进程”等内部工程语言；统一执行计划也同时保留“BOX-R1-B 已完成”和“尚无原生命令 DLL”两种互相矛盾的状态。

本阶段只纠正这两类偏移：

- 桌面内容、工作区和任务栏状态改用用户能够直接理解的语言；
- 内部诊断代码继续保留在 UIA `ItemStatus` 和高级诊断，不删除安全事实；
- 任务栏未认证、适配器缺失和探测失败仍保持按钮禁用、`Mutation=Disabled`；
- 统一计划基线更新到 `main@36b583f`，BOX-R1-B 明确为工程完成、BOX-R1-C/D 物理证据 Pending；
- Stage 217 根据 PR #264 与 main CI 事实提升为 Integrated；
- 下一用户结果重新固定为 BOX-R1-C/D 与 M1 完整物理旅程，任务栏 R2B1-B 保持隔离环境阻断。

本阶段不增加新导航、教程、诊断卡、插件、小组件、自动整理或系统权限，也不修改桌面文件和任务栏。

## 2. 预期、实际、差异与修正

| 检查 | 预期 | 审计时实际 | 修正 |
|---|---|---|---|
| 核心页面语言 | 用户看到当前状态、影响和下一动作 | 部分可见/候选状态使用适配器、权威快照、Build、恢复事务等内部词 | 改为“正在加载、现有文件保持原位、当前 Windows 版本尚未验证”等产品语言 |
| 安全可审计性 | 文案简化不能隐藏真实准入 | Taskbar UIA 已有有限 admission、按钮状态和 mutation 标记 | 保留 UIA `ItemStatus` 与 Core 策略，不开放任何 Click 写入事件 |
| BOX-R1 状态 | DLL/清单工程完成与菜单物理 Pending 分开 | 计划前部说 B 完成，历史表仍说没有 DLL | 更新历史结果栏，保留 C/D Pending，不改写历史测试数据 |
| 主线状态 | 权威基线和 Stage 集成状态反映 GitHub | 计划停在 `cb032b0`，Stage 217 仍 `IntegrationPending` | 更新为 `36b583f / Integrated` 并记录 main run `33054899086` |
| UI 合同 | 新产品文案和禁用准入状态可由结构化合同复核 | 首轮合同因新增断言误用空变量 `$xaml`，在读取文案前失败 | 改为使用已安全加载的 `$document.OuterXml`；最终 195 个 AutomationId 全部通过 |
| 真实概览窗口 | 唯一 Long方格窗口呈现现代产品概览，而非教程/工程面板 | 首次显式启动窗口在捕获前消失；按受控恢复只重试一次后成功看到“桌面概览”、0 个方格、运行正常和新建盒子入口 | 判定 `RealOverviewPass`，保留首次消失事实，不重复启动掩盖差异 |
| 个性化可见验收 | 从概览进入个性化并看到任务栏预设和新文案 | 一次导航操作后 Long方格窗口消失，未取得个性化页截图；Application Error/WER 对应记录为 Microsoft.UI.Xaml `3.2.3.0`、`0xc000027b`、P7 `8001010e` | 与既有 `RPC_E_WRONG_THREAD` 上游风险签名一致；判定 `PersonalizationVisibleEvidencePending`，停止继续点击，不把 XAML 合同冒充真实可见 Pass |
| 真实进程启动 | 桌面优先、显式控制中心、单实例和清理均满足预算 | 显式控制中心 `2,331 ms`，桌面宿主 `2,093 ms`，稳定 `10 s`；重定向、退出、零配置写入均符合预期 | `Difference=None / Outcome=Pass`，说明基础启动链路正常，不能消除导航可见差异 |
| 全量回归命令 | 运行仓库 CI 等价覆盖率测试 | 首轮错误引用不存在的 `eng/coverage.runsettings`，测试在启动前退出 | 删除无效参数后重新完整执行，最终 `1353/1353` 通过 |

## 3. 测试与证据边界

本阶段最终证据如下：

- `dotnet format LongGrid.sln --verify-no-changes --no-restore`：通过；
- Release App 与完整 Solution 构建：`0 warning / 0 error`；
- `eng/Test-LongGridUi.ps1 -ContractOnly`：最终通过，`195` 个唯一 AutomationId；不带参数的 live UIA 入口在应用启动前按预期识别 WindowsAppRuntime `2.4.0.0` / Microsoft.UI.Xaml `3.2.3.0` 已知风险并失败关闭；
- `eng/Test-LongGridWindowSmoke.ps1 -Configuration Release -NoBuild -StabilitySeconds 10`：`Difference=None / Outcome=Pass`；
- 完整 Release 测试：`1353/1353`，失败 `0`、跳过 `0`；
- 独立覆盖率目录 `TestResults-UIR1F`：lines `90.40% (23398/25884)`、branches `76.04% (7656/10069)`，通过 `90% / 75%` 门槛；
- 真实概览截图观察：Pass；真实个性化/任务栏卡片截图：Pending，原因是一次导航后窗口消失，系统 Application Error/WER 留下 Microsoft.UI.Xaml `3.2.3.0`、异常 `0xc000027b`、P7 `8001010e` 的既有上游风险签名。

因此，本阶段可以确认产品语言、结构合同、构建、测试与基础启动收敛，但不能确认个性化页在当前跨进程可见自动化路径下稳定。该差异不是任务栏写入失败：本轮没有发起任何任务栏写入，也没有改变系统状态。

真实窗口检查只允许启动、观察、切换产品导航和关闭 Long方格；不得创建盒子、选择真实文件夹、移动文件、安装包、修改任务栏或系统设置。

## 4. 需求对齐与下一步

本阶段直接修正“软件看起来像教程/工程控制台”的既有偏移，同时没有削弱失败关闭。完成后下一核心阶段是 BOX-R1-C/D 与 M1 完整物理旅程；开始条件仍是可丢弃 Windows 账户或 VM、安全 WinUI 运行时和安装/卸载证据能力。TASKBAR-R2B1-B 继续等待 R2B1-A Guest 准入，不在宿主试写。

需求对齐结论：`AlignedWithCore / NoScopeExpansion`。当前没有发生向教程、诊断工具或外围功能继续扩张的开发偏移；未完成项仍集中在用户最初定义的核心结果——桌面右键创建盒子的物理闭环、盒子绑定真实文件夹的完整可见旅程，以及任务栏美化的可丢弃环境认证与默认恢复。
