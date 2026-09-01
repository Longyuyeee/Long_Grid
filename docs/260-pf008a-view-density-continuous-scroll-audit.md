# Stage 260：PF-008A 视图密度与连续滚动审计

日期：2026-09-01

输入基线：`origin/main@b66aaf6`（PR #343 / Stage 259 已合入）

状态：`PF008A EngineeringComplete / RealHwndPass / RealFilesystemPass / ProductEvidencePending`

## 1. 本阶段交付结论

PF-008A 已贯通正式产品链：用户可在控制中心为每个方格选择“舒适”或“紧凑”内容密度；舒适模式投影 12 项、单项高度 28 DIP，紧凑模式投影 18 项、单项高度 20 DIP。选择写入每方格 appearance 配置，schema 从 v4 升至 v5，旧 v1～v4 配置均保持原视觉默认 `Comfortable`。

鼠标滚轮由“一次跳 12 项”改为按标准滚轮步数逐项连续移动；键盘 PageUp/PageDown 通过显式 `PageNavigation` 保持按当前密度整页移动。投影、原生 HWND 绘制、指针命中、框选空白区、UIA 几何、缩略图候选与视口归一化使用同一密度容量和行高，不再各自硬编码 12/28。

本阶段只改变 Long方格配置和展示，没有移动、重命名、删除或改写真实桌面文件，也没有增加权限、安全或证据基础设施范围。

## 2. 预期、初始实际、差异、修正、最终实际

| 检查 | Expected | Initial Actual | Difference | Correction | Final Actual |
|---|---|---|---|---|---|
| 鼠标连续滚动 | 标准 `-120` 从起点 0 移到 1 | 回归真实失败：`Expected=1 / Actual=12` | 鼠标滚轮复用了旧整页策略 | 按 `abs(delta)/120` 逐项移动 | 起点实际为 1 |
| 键盘分页 | PageDown 保持整页导航 | 与滚轮共享 `WheelDelta`，无法区分意图 | 修滚轮可能破坏键盘 | 增加显式 `PageNavigation` | 舒适移动 12，紧凑移动 18 |
| 视图密度 | 每方格至少两档且重启保持 | schema v4 无密度字段，桌面固定 12×28 DIP | 用户无入口、无持久化 | schema v5 + appearance 提交/UI/投影/布局 | `Comfortable=12×28`，`Compact=18×20` |
| 旧配置迁移 | v4 打开后保持旧视觉 | 无 v4→新字段迁移 | 升级后选择不明确 | v4 显式迁移为 `Comfortable`，v1～v3 默认相同 | v4→v5 回归通过 |
| 真实宿主与文件 | 同一 HWND 展示 18 项，滚轮到 1，文件不变 | 无 PF-008A 真实结果 | 不能只用 Mock/字符串宣称完成 | 真实 HWND + 24 个 Unicode 临时文件 SHA-256 前后核对 | SameHwnd=true、VisibleItems=18、NextStart=1、FilesUnchanged=true |

## 3. 真实测试结果

- Release/Debug 解决方案构建：`0 warning / 0 error`。
- PF-008A 相关配置、提交、投影、视口、真实 HWND 与 UIA 专项：`115/115`。
- 完整解决方案测试：首轮 `1,406/1,407`，唯一失败是 `eng/Test-LongGridUi.ps1` 仍硬编码旧“12-item viewport”合同；改为密度感知和连续滚动合同后，最终 `1,407/1,407`。
- 真实 HWND 证据使用实际 Win32 窗口句柄并原位更新 presentation；紧凑模式实际包含 18 个可见项，标准滚轮请求实际计算到起点 1。
- 真实文件证据创建 24 个 Unicode `.txt` 文件，对文件名和逐文件 SHA-256 形成汇总指纹；功能执行前后指纹一致。

当前机器的完整跨进程 UIA 仍由已审计 Runtime 组合在产品启动前阻断。测试入口真实返回：`Live cross-process UIA was blocked before application launch`。这只使正式 App 物理点击、截图、键盘与 Narrator 证据保持 `ProductEvidencePending`，不否定本阶段 Core、配置、真实文件系统和真实 HWND 工程结果。

## 4. 开发目标与需求对齐审计

开发目标审计：PF-008A 的两档密度、连续滚动、配置重启保持和真实文件零变化已实现；未把排序、自定义拖序或发布工作混入本切片。

需求对齐审计：本轮直接增加核心用户可见功能，符合“核心工程实现 → 核心用户旅程 → 功能广度对标”。零惊吓边界保持：配置只保存有限枚举，真实文件内容、路径和归属均未变化。

完成度审计：PF-008A 为 `EngineeringComplete / RealHwndPass / RealFilesystemPass / ProductEvidencePending`；PF-008 整体仍为 `InProgress`，不能标为 Complete；30 项 PF 仍为 `0 Complete`，M1/M2 仍为 `0/2 Complete`，产物仍不可公开分发。

## 5. 唯一接续开发点

下一步只进入 **PF-008B：类型与修改时间稳定排序**：

1. 复读文件夹内容模型和现有三种名称排序，冻结当前顺序与文件零写入基线；
2. 为类型、修改时间升序/降序定义稳定 tie-break，明确文件夹优先语义；
3. 贯通每方格配置、控制中心入口、内容读取/投影与重启保持；
4. 使用真实 Unicode 目录和真实文件修改时间记录 Expected、Initial Actual、Difference、Correction、Final Actual，并核对路径、数量和 SHA-256 不变；
5. 结束时更新本文、统一执行计划和 PF backlog，完成目标/需求审计后推送。

PF-008B 完成前不并行展开 PF-008C、PF-009 或新的权限/安全邻接工作。
