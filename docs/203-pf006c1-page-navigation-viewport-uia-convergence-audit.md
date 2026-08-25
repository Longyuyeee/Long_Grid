# Stage 203：PF-006C1 PageUp/PageDown 跨视口导航与 UIA 收敛审计

日期：2026-08-25  
开发项：PF-006C1  
结论：**Local Engineering Pass / PR And Main CI Required**

## 1. 开发目标与基线差异

PF-006A 已提供当前可见页内的方向键、Home/End、选择和 UIA 快照；PF-005C 已提供每页 12 项的滚轮视口。但基线没有 PageUp/PageDown 键映射，滚轮换页后 `ReconcileVisibleItems` 会把焦点放到新页第一项，无法保持原焦点的页内相对位置。

本切片目标固定为：在现有显式交互租约和正式 DesktopHost 生命周期中，让 PageUp/PageDown 推动 viewport、选择、焦点与 UIA 最终快照一起收敛；不引入全局键盘钩子，不修改文件，不抢前台。

## 2. 实现与安全边界

- 原生激活 HWND 把未修饰的 PageUp/PageDown 映射为 `+120/-120` 的有限视口意图；
- 视口意图继续携带 container/display/workspace revision/topology generation，不直接改 App 字典；
- 非显式租约、来源未证明、Injected、auto-repeat、修饰 Page 键、错误显示器、≤12 项容器和陈旧 revision 全部失败关闭；
- App 仍在 Dispatcher 权威复核当前 workspace、topology、容器和显示器，再更新视口；
- 生命周期收到新投影后，在同一锁内先 reconcile 可见项，再按旧焦点的页内偏移选择新页目标，最后只刷新一次 Surface/UIA；
- lifecycle telemetry snapshot 同步最终 selected/focused/revision，避免 UIA 已到 revision 3 而控制面仍停在 revision 1；
- 最后一页不足 12 项时偏移会夹紧到最后一项，PageUp/PageDown 不越界；
- UIA AutomationId 继续使用匿名容器/可见序号，不公开内部 item id 或路径。

## 3. 真实测试与差异修正

| 验收 | Expected | Actual | Difference / Correction |
| --- | --- | --- | --- |
| Page 键映射 | PageUp `+120`；PageDown `-120` | 两者匹配；Ctrl/Shift 组合不提交 | None |
| auto-repeat | 不产生换页请求 | 真实激活 HWND 只记录 2 个非重复请求 | None |
| 权威请求 | 保留容器、显示器、workspace/topology 代次 | `container-1/display-primary/7/11` | None |
| 相对焦点 | 第 1 页第 2 项 → 第 2 页第 2 项 | `item:2` → `item:14`，selected/focused/anchor 一致 | None |
| 生命周期快照 | 与最终选择 revision 同步 | selected=1、focused=true、revision=3 | 初审发现旧值停在 revision 1，已修正 |
| HWND/UIA | 新页唯一选中“项目 14”，不抢前台 | SelectionPattern 唯一项名称“项目 14”；foreground 未变化 | None |
| UIA AutomationId | 初始误以为包含 `item:14` | 实际为匿名 `LongGrid.DesktopHost.Item.1.2` | 修正测试预期，保留隐私合同 |
| 全量 Release | 0 fail | 1200/1200，13～16 s | None |
| 覆盖率 | lines ≥90%、branches ≥75% | 90.36%（40978/45348）、75.73%（13310/17576） | None |

第一次覆盖率检查错误聚合工作区历史 `TestResults`，显示 86.10%/73.41%；这与同一代码刚通过的主线基线不一致。使用独立临时结果目录重跑完整 1200 项 collector 后得到上表真实值。没有降低门槛、排除正式程序集或用单测子集计算覆盖率。

格式门、Release 全解决方案构建（0 warning / 0 error）和 `git diff --check` 同时通过。

## 4. 需求对齐与剩余范围

本切片直接完成竞品桌面整理软件应具备的跨页键盘导航，不涉及任务栏美化、小组件、插件、文件移动或发布签名，未发生方向偏移。PF-001～PF-005 状态不变，PF-006 仍为 `InProgress`：PF-006C1 本机工程证据完成，但仍需 PR/main CI；下一工程切片为 PF-006C2 鼠标框选，物理键盘、高对比和 Narrator 产品证据继续 Pending。
