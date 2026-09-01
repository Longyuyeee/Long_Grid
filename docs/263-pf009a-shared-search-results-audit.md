# Stage 263：PF-009A 共用搜索模型与正式结果列表审计

日期：2026-09-01

输入基线：`origin/main@891832f`（PF-008C / PR #346 已全绿合入）

状态：`PF009A EngineeringComplete / RealFilesystemPass / ProductEvidencePending`

## 1. 本阶段交付结论

PF-009A 已建立 Core 共用查询模型，并把正式结果列表接入控制中心。查询对象同时覆盖盒子和项目，可按目标、项目类型、盒子健康状态和显示器范围组合筛选；结果明确给出所属盒子、来源、类型和有限解析状态。空查询、无结果、离线项目、陈旧 revision 和非法输入都有独立有限状态，不展示旧结果或部分不可信结果。

查询只使用正式 read model 提供的盒子名、已批准项目显示名、类型、健康状态、显示器键和有限解析状态。它不会读取文件正文、路径、快捷方式参数或 URL 内容，也不会把用户查询写入 machine status。折叠盒子中的项目仍属于正式工作区，因此进入项目搜索输入；这修正了旧 presentation 搜索只能筛整个盒子且排除折叠项目的问题。

工作区每次重建时把当前 edit revision 一并交给 presentation。查询请求必须与当前 revision 完全一致；工作区变化后，旧请求返回 `StaleAuthority` 和零结果。查询最多扫描正式上限 500 个项目，额外输入被明确标记 `WasTruncated`。

## 2. Expected、Initial Actual、Difference、Correction、Final Actual

| 检查 | Expected | Initial Actual | Difference | Correction | Final Actual |
|---|---|---|---|---|---|
| 真实项目类型查询 | Unicode 文件 `项目-报告.txt` 应被“文件”命中 | 旧 `ProductWorkspaceVisibleSearchPolicy` 返回 0 项；xUnit 精确失败为 `Assert.Single(): collection was empty` | 旧搜索只返回盒子索引，类型文本不是项目级可查询字段 | 新增盒子/项目共用结果模型和有限中英文类型标签 | 返回 1 个 `Item` 结果，名称与所属盒子正确 |
| 正式结果表面 | 用户可查看项目结果及所属盒子，而不只是隐藏/显示整个盒子 | 只有搜索框，结果仍是过滤后的盒子卡片 | 没有项目级结果、离线状态或来源说明 | 新增正式结果区以及目标、类型、显示器筛选 | 206-ID 合同通过；结果含盒子、来源、类型、解析状态 |
| 组合筛选 | 目标、类型、健康状态、显示器应稳定取交集 | 仅有盒子健康筛选与文本过滤 | 不能精确缩小项目范围 | 共享请求模型统一处理四类筛选 | `Items + File + Ready + display-primary` 只返回预期文件 |
| 陈旧结果 | revision 变化时旧请求应丢弃 | 旧搜索没有 authority revision | 配置更新后可能继续展示旧过滤结果 | 请求携带 `ExpectedRevision` 并与当前 edit revision 比较 | 返回 `StaleAuthority`、0 结果、0 扫描 |
| 500 项规模 | 在 100ms 预算内检查 500 项，并明确处理第 501 项 | 没有正式项目结果或规模合同 | 无法证明对标规模下可用 | 有界扫描、预热后真实 Stopwatch 验证、截断标志 | 500 个结果、扫描 500、`WasTruncated=True`，测试低于 100ms |
| 文件内容与零变化 | 正文不得命中，查询前后文件哈希不变 | 旧实现不读正文但也不能返回正式项目结果 | 新模型需同时保持功能与零文件写入 | 真实 Unicode 文件同时执行类型命中与正文探针 | 类型命中；正文为 `NoResults`；SHA-256 前后完全一致 |

## 3. 真实测试结果

- 初始真实差异：在临时目录写入真实 Unicode 文件 `项目-报告.txt` 后，旧盒子级策略按“文件”查询得到 0 项，xUnit 按预期失败。
- 修正后真实文件旅程：同一文件经正式 workspace read model 投影后返回唯一项目结果；以真实正文查询返回 `NoResults`；测试前后 SHA-256 完全一致。
- 搜索专项：`6/6`，覆盖 Unicode Form C 归一化、四类筛选交集、离线项目、空/无结果/陈旧/非法状态、500 项上限和正文隔离。
- 500 项规模：预热后真实 `Stopwatch` 调用低于 100ms；501 项输入只扫描/返回前 500 项并显式截断。
- 完整核心测试：`1,422/1,422`，0 failed，0 skipped。
- Release 全解决方案：`0 warning / 0 error`；`git diff --check` 通过。
- UI 工程合同：`206` 个唯一 AutomationId，正式结果列表及目标/类型/显示器筛选合同通过。
- 正式跨进程 UIA：真实执行在 App 启动前失败关闭；本机缺 `MicrosoftCorporationII.WinAppRuntime.Main.2 >= 2.3.1.0` 与 `Microsoft.WinAppRuntime.DDLM.2.3.1.0-x6`。没有启动 App，也没有把合同测试冒充物理产品证据。

## 4. 开发目标与需求对齐审计

开发目标审计：PF-009A 要求的共享查询模型、盒子/项目正式结果、类型/健康/显示器筛选、500 项规模、Unicode、离线/无结果/陈旧状态均已实现。查询没有建立第二份配置事实源，也没有越过 read model 读取磁盘内容。

需求对齐审计：本阶段直接形成用户可看到、可筛选的搜索结果，优先补足核心用户旅程和功能广度，没有扩张权限或安全邻接工程。旧盒子过滤继续保留作为列表视图能力；正式项目结果使用同一输入查询，不以隐藏盒子卡片冒充项目搜索。

完成度审计：PF-009A 为 `EngineeringComplete / RealFilesystemPass / ProductEvidencePending`。缺完整兼容 Windows App Runtime 的本机不能完成正式 App 物理鼠标、键盘、Narrator、触控与截图证据，因此 PF-009 总项仍为 `InProgress`，30 项 PF 保持 `0 Complete`，M1/M2 保持 `0/2 Complete`，产物不可公开分发。

## 5. 唯一接续开发点

下一步只进入 **PF-009B：桌面搜索浮层与结果导航**：

1. 复用 PF-009A 查询请求和结果，不在 DesktopHost 新建另一套搜索事实源；
2. 提供可显式打开/关闭的桌面搜索浮层，支持键盘输入、结果移动和 Escape 关闭；
3. 选择盒子结果时临时展开、滚动并高亮目标盒子，选择项目结果时定位所属盒子和项目；
4. 文件/文件夹/快捷方式/URL 的执行继续复用既有打开/定位入口，搜索层不直接调用 Shell 或文件 API；
5. 用真实盒子、Unicode 文件、离线项目和 500 项数据记录 Expected / Initial Actual / Difference / Correction / Final Actual；
6. 结束时完成目标审计、需求对齐、文档更新、提交、推送和 CI 收口。

PF-009B 完成前不并行展开 PF-010、PF-011 或新的安全邻接工作；BOX/M1 与 TASKBAR Guest 继续作为并行外部门禁。
