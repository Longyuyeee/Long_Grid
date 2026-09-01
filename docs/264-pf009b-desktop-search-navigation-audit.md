# Stage 264：PF-009B 桌面搜索浮层与结果导航审计

日期：2026-09-01

输入基线：`origin/main@5658874`（PF-009A / PR #347 已全绿合入）

状态：`PF009B EngineeringComplete / RealHwndPass / RealFilesystemPass / ProductEvidencePending`

## 1. 本阶段交付结论

PF-009B 已把 PF-009A 共用查询接入正式桌面搜索浮层。用户可从控制中心或主显示器第一个桌面方格标题上的“搜”按钮显式打开；浮层支持文字输入、上下键移动、Enter 显示、Escape 关闭，以及“在桌面显示 / 打开 / 在资源管理器中定位”三个有限动作。查询历史不保存，浮层关闭即丢弃。

结果导航通过新的 `ProductDesktopSearchNavigation` 把盒子/项目序号解析为当前 workspace revision、topology generation、显示器、容器 ID、项目 ID 和有限 viewport start。revision、拓扑或目标变化时返回有限失败状态，不使用旧结果。项目超出当前 12/18 项可见窗口时，投影只在内存中滚动到包含目标的位置；持久化折叠盒子只在四秒高亮期间临时展开，随后恢复原折叠设置。配置与真实文件均不改变。

DesktopHost 原生投影增加盒子和项目搜索高亮；真实 HWND 已接受并绘制高亮投影。搜索层不直接执行 Shell 或文件 API：“打开”和“在资源管理器中定位”仍提交既有 `ProductDesktopItemOpenController`，沿用其 revision/topology、引用解析和有限反馈合同。

## 2. Expected、Initial Actual、Difference、Correction、Final Actual

| 检查 | Expected | Initial Actual | Difference | Correction | Final Actual |
|---|---|---|---|---|---|
| 第 20 项搜索定位 | 搜索命中后目标进入桌面可见窗口 | 投影仍从第 1 项开始，目标 `item:20` 不在前 12 项 | PF-009A 只有结果，没有桌面 viewport 导航 | 按目标 ordinal 计算有限 viewport start，并交给正式投影 builder | viewport start=`8`，`item:20` 进入 12 项窗口 |
| 折叠盒子定位 | 临时展开并高亮，不能永久修改折叠配置 | xUnit 精确失败：`Assert.False`，Expected `False`、Actual `True` | 原投影完全采用持久化 `Collapsed=True` | 搜索 target 对当前投影做四秒临时展开/高亮覆盖 | 搜索投影 `IsCollapsed=False`；目标清除后恢复持久化状态 |
| 桌面入口 | 桌面和控制中心均可显式打开搜索 | 只有控制中心正式结果列表 | 日常桌面旅程仍需先打开控制中心 | 主显示器首个方格标题增加有限“搜”按钮，控制中心增加明确入口 | 真实原生 UIA 得到 5 个按钮并成功调用当前显示器搜索回调 |
| 键盘旅程 | 输入、上下移动、Enter、Escape 均可完成 | 没有桌面搜索浮层 | 无可聚焦搜索输入或结果键盘导航 | 新增独立 WinUI 浮层、有限结果列表和键盘状态机 | 208-ID 工程合同及源码行为合同通过 |
| 打开/定位 | 搜索动作复用既有执行入口 | 搜索结果不能触发打开或 Explorer 定位 | 结果与项目执行链未连接 | 解析当前目标后提交既有 item-open request | Open 使用 `KeyboardEnter`；Locate 使用 `FeedbackLocateInExplorer`，搜索层零 Shell/File API |
| 真实文件零变化 | 导航只改变临时桌面呈现 | 原功能没有导航，也没有文件变化 | 新滚动/高亮需证明不会触碰文件 | 20 个真实 Unicode 文件覆盖搜索、投影、真实 HWND 和哈希盘点 | 所有路径与 SHA-256 前后完全一致 |

## 3. 真实测试结果

- 初始真实差异：创建 20 个真实文件，目标为第 20 个 `目标-项目.txt`；PF-009A 查询正确命中，但旧 DesktopHost 投影保持 `IsCollapsed=True`，xUnit 以 Expected `False` / Actual `True` 精确失败。
- 修正后真实旅程：同一查询解析为 `container-search / item:20 / display-primary / viewportStart=8`；目标盒子临时展开，项目进入可见列表并标记高亮。
- 真实 HWND：`WindowsProductDesktopHostReadOnlySurface` 创建真实窗口、Passive 合同通过并接受搜索高亮 projection；主显示器原生 activation source 暴露第五个“搜索桌面盒子和项目” UIA Invoke 按钮。
- 真实文件：20 个 Unicode 文件的 SHA-256 前后全部一致。
- 搜索导航与原生入口专项：`5/5`；相关 DesktopHost projection/lifecycle/UIA 回归 `88/88`。
- 完整核心测试：`1,426/1,426`，0 failed，0 skipped。
- Release 全解决方案：`0 warning / 0 error`。
- UI 工程合同：`208` 个唯一 AutomationId；桌面浮层查询、结果、显示、打开、定位、Escape/上下键合同通过。
- 正式跨进程 UIA：真实执行在 App 启动前失败关闭；本机仍缺 `MicrosoftCorporationII.WinAppRuntime.Main.2 >= 2.3.1.0` 与 `Microsoft.WinAppRuntime.DDLM.2.3.1.0-x6`。没有启动 App，也没有把源码合同或真实子 HWND 冒充完整产品浮层证据。

## 4. 开发目标与需求对齐审计

开发目标审计：PF-009 的共用查询、控制中心结果、桌面浮层、当前显示器入口、键盘导航、临时展开/滚动/高亮和打开/定位复用均已形成工程闭环。查询同步执行且每次重建完整结果，不存在旧异步查询覆盖新查询；导航另以 revision/topology 二次复核。

需求对齐审计：本阶段直接补齐“从桌面搜索并立即显示、打开或定位”的核心用户旅程，没有扩张权限或安全邻接工程。临时展开与高亮只影响正式桌面 projection，未把搜索状态写入配置；用户关闭或等待高亮结束后，原折叠设置恢复。

完成度审计：PF-009A/B 均达到 `EngineeringComplete`，并取得真实文件与真实 DesktopHost 子 HWND 证据；PF-009 工程切片收口。完整正式 App 浮层的物理鼠标、键盘、Narrator、触控和截图证据仍因 Runtime 门禁 Pending，所以 PF-009 保持 `EngineeringComplete / ProductEvidencePending`，30 项 PF 仍为 `0 Complete`，M1/M2 仍为 `0/2 Complete`，产物不可公开分发。

## 5. 唯一接续开发点

下一步只进入 **PF-010A：统一会话历史模型与 50 步撤销/重做骨架**：

1. 复读当前“最近一次撤销”、各类 edit undo token、提交补偿和保存失败恢复链；
2. 建立统一历史项、cursor、undo/redo 分支和 50 步有界淘汰模型，不先重写各 reducer；
3. 首批接入创建、重命名、锁定、折叠和外观这些已有完整 undo token 的动作；
4. 新动作发生后截断 redo 分支，revision/外部变化时明确失效原因；
5. 用真实配置 Store 执行 apply→undo→redo→undo、50/51 步、保存失败补偿，并记录 Expected / Initial Actual / Difference / Correction / Final Actual；
6. 在控制中心提供正式历史列表和撤销/重做入口，明确“不会删除或移动真实文件”；
7. 结束时完成目标审计、需求对齐、文档更新、提交、推送和 CI 收口。

PF-010A 完成前不并行展开 PF-011 或新的安全邻接工作；BOX/M1 与 TASKBAR Guest 继续作为并行外部门禁。
