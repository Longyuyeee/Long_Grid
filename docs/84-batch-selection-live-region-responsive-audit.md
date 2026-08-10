# 批量选择状态播报与紧凑布局审计

> 日期：2026-08-10
> 范围：批量选择空状态、辅助功能 live-region 事件、紧凑宽度操作栏重排
> 安全边界：只调整 App presentation 与交互反馈，不新增配置、桌面文件或窗口执行权限

## 1. 缺口与需求对齐

Stage 83 建立了键盘可达的有限批量选择操作栏，但复审发现三个交互缺口：用户用 Ctrl/Shift 取消最后一个项目时，状态区可能保留旧数量；一次“选择前 256 项”会触发多次 `SelectionChanged`，若逐条播报会干扰屏幕阅读器；两个横向按钮组在 720 px 紧凑布局下没有独立重排合同。

本切片只修正 presentation 层反馈与布局。既有同方格、1..256、确认、revision/generation、原子提交和一次撤销门禁保持不变。

## 2. 有限状态与单次播报

加入和移除列表各自建立有限的选择状态发布函数。数量为零时，可见文案和 UIA `ItemStatus` 都显式回到 `Count=0`，并继续声明 `Changed=False`、`DesktopFilesChanged=False`；非空时继续报告实际数量、上限和同方格状态。

用户直接更改选择时，状态更新后显式通过 `FrameworkElementAutomationPeer` 发布一次 `AutomationEvents.LiveRegionChanged`。批量按钮和 presentation 重投影在清空、逐项恢复或加入期间临时抑制中间播报，完成后只发布最终选择状态；这避免最多 256 次重复朗读，也避免 Catalog/session 刷新被误认为 256 次用户动作。

## 3. 紧凑布局

两个选择操作栏由横向 `StackPanel` 改为同构的双列 `Grid`。宽布局保持双列；低于 760 DIP 的既有断点时，统一响应式函数把第二个按钮移到下一行，两按钮各跨两列，并把间距从列间距切换为行间距。按钮本身、Tab 顺序、AutomationId 和辅助功能名称不变，因此权威 UIA 总数保持 134。

## 4. 自动化合同

`Test-LongGridUi.ps1` 新增源码断言，锁定两组响应式 Grid、紧凑行列位置、空选择状态、播报抑制、两个最终发布函数和 `LiveRegionChanged` 事件。干净会话入口继续复核 134-ID 合同并保持 `PendingCleanInteractiveSession`，静态合同不得替代 Narrator、高对比度和文本缩放的真实人工结论。

## 5. 验证结论

本地 Release 构建已通过，0 警告、0 错误；全量测试 544/544 通过。Stage 84 单份 Cobertura 为行 91.12%（8105/8894）、分支 80.11%（2292/2861），通过 90%/75% 门槛。134-ID UI 合同、干净会话 `ValidateOnly`、启动链、Issue #19/#20/#23/#24 会话编排、单实例合同和依赖漏洞门禁均通过。

DesktopHost 交互切片为 `Conditional Pass` 且确认 `DesktopFilesReadOrChanged=False`；100 次持久化及终止/ACL 注入矩阵全部通过；文件操作安全与缩略图隔离探针均为 `ConditionalPass` 且清理成功。内部 RC、PR CI 与 main CI 将在本切片准确提交上继续复核。真实 Narrator、高对比度和文本缩放结果仍为 Pending。

## 6. 后续方向

批量选择的键盘入口、空状态、单次辅助功能播报和紧凑布局合同已经闭环。下一阶段应执行真实 Narrator、高对比度、200% 文本缩放和仅键盘人工矩阵；若环境仍不可用，继续开发不依赖外部授权的正式工作区交互切片，但不得把源码合同提升为人工体验 Pass。
