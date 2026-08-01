# Phase 0 出口执行手册

日期：2026-08-01

状态：**Ready to Execute / 未完成实机与负责人签字**

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

## 4. Issue #20：动态显示与会话

先按[P0-07b2b2b2b4b1 报告](spikes/P0-07b2b2b2b4b1-controlled-dynamic-matrix-harness.md)启动对应 `--matrix-scenario`。每次变化后等待稳定采样，再恢复原布局并复读窗口、Region、Composition、UIA 和资源状态。

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

| Issue | 剩余项 | 安全限制 | 状态 |
|---|---|---|---|
| #21 | Explorer UI 撤销/Explorer 重启 | 仅专用账户和自有文件 | Pending |
| #21 | 跨卷复制→校验→删除→补偿 | 两个可清空测试卷，不使用用户卷 | Pending |
| #21 | ACL、共享占用、只读卷、磁盘满 | VM/可还原快照；不得破坏系统目录 | Pending |
| #21 | OneDrive、网络、重解析点、真实取消 | 专用账号/共享；默认阻断优先 | Pending |
| #22 | 受限 Low Integrity 对照 | Low worker 可读取未授权文件，证明 MIC no-write-up 不能承担文件保密边界 | Decision evidence；不得作为生产回退 |
| #22 | AppContainer 与访问 broker | 真实 worker 全部为零 Capability AppContainer；协议 v6 对照受控副本与最小路径 ACL；BMP/PNG/GIF 逐项验证输入可读、安全分类和 Profile 清理，正常路径复核随机 SID ACE 已恢复 | Conditional Pass（自动探针）；ACL 只作比较，异常退出 ACE 修复 Pending |
| #22 | 有界共享内存句柄 broker | 匿名映射、单请求复制句柄、最大 262,144 bytes；缺失句柄/错误容量/元数据错误全部阻断并恢复 | Conditional Pass（自动探针） |
| #22 | 正式渲染表面集成 | 保持已验证的 transport、长度、格式、尺寸和容量上限 | Pending |
| #22 | 真实 Provider、x64/ARM64、Windows 矩阵 | 自有 BMP/PNG/GIF：22621 两种输入 6/6 提取成功；26100 两种输入 6/6 可读但 Shell 全部 `E_ACCESSDENIED`，必须类型图标/缓存回退；只输出固定格式标签/HRESULT | Partial；JPEG/HEIF、Office/PDF、云/网络、第三方、ARM64 矩阵 Pending |

在这些项目完成前，#21 与 #22 保持打开，产品默认只能使用安全引用和缓存内图像。

## 6. Issue #23：负责人决策记录

以下是建议起点，不是已批准决定。负责人应在 Issue #23 逐项写入选择、理由、日期和批准人。

| 决策 | 建议起点 | 需要确认 | 状态 |
|---|---|---|---|
| 首版整理模式 | 仅安全引用；真实移动保持关闭 | 是否允许任何托管目录入口 | Pending owner decision |
| Folder Portal | 不进入首个 MVP 切片 | 进入 Beta 还是更晚 | Pending owner decision |
| 最低系统 | 以实机矩阵结果确定，不先承诺 | Windows 10 最低 build 或仅 Windows 11 | Pending owner decision |
| 架构 | 先验证 x64，ARM64 在有设备后决定 | 首发是否原生 ARM64 | Pending owner decision |
| 安装渠道 | MSIX 开发包优先，保留离线验证 | Store、离线 MSIX 或企业渠道 | Pending owner decision |
| 许可证 | 在接受贡献或发布二进制前选择 | MIT、Apache-2.0、GPLv3 或闭源/双许可 | Pending owner decision |
| 性能预算 | 以支持矩阵复测后批准 | 500 项 p95、内存、空闲 CPU 最终值 | Pending owner decision |

5 人无提示测试必须覆盖：首次扫描说明、创建容器、添加安全引用、识别原生图标仍存在、区分引用与移动、撤销/恢复。每位参与者记录任务成功率、误解点、严重度和是否需要主持人提示；不能收集真实文件名或桌面截图。

## 7. Issue #24 与 ADR-0001

配置仍需真实卷只读/空间耗尽、应用关闭接线、完整单实例激活和正式 schema 矩阵。它们通过后才可把探针合同迁入 `LongGrid.Infrastructure`。

只有 #19–#24 的必要证据与负责人决策齐全后，才能把[ADR-0001](adr/0001-windows-technology-stack.md)从 `Proposed` 改为 `Accepted`、`Revised` 或 `Rejected`。ADR 决定前不得创建安装承诺，也不得把探针项目改名冒充产品模块。

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
