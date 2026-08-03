# Long方格匿名项目与拖放语义原型审计

审计日期：2026-08-03

基线：`main` / `ec8e79f`（PR #71 已合入）+ 当前短生命周期分支

关联：[Issue #23](https://github.com/Longyuyeee/Long_Grid/issues/23)、[Issue #19](https://github.com/Longyuyeee/Long_Grid/issues/19)

结论：**Partial / 首次整理的“加入三个项目、判断拖放、最近动作撤销”已具备五人测试形状；真实拖放仍未实现**

## 1. 需求对齐

PRD 要求首次教学只覆盖创建、拖放语义和撤销，并规定：

- Explorer → 安全引用容器默认添加引用；
- Long方格引用 → 另一个引用容器只改变组织关系；
- 任何移动请求都必须展示源、目标、冲突与明确确认；
- 无效或未批准的目标保持原状并说明原因；
- 新用户创建容器并加入项目的中位数目标小于 2 分钟。

交互审计要求动作徽标不能只靠修饰键表达，并要求五人无提示完成“创建容器并把三个项目加入”。本切片把这些语义编码为匿名、可键盘操作且可由 UIA 复读的低保真流程。

## 2. 已实现流程

创建匿名方格后：

1. `添加 3 个匿名引用`变为可用；
2. 执行后显示匿名项目 A/B/C，计数变为 3，并进入 `PracticeItemsAdded`；
3. 三个语义练习分别输出：
   - `AddReferenceDropPreview`：Explorer → 安全引用方格，动作徽标为“添加引用”；
   - `ReassignDropPreview`：方格引用 → 另一个方格，动作徽标为“改变归属”；
   - `ManagedMoveDropBlocked`：Explorer → 请求移动文件，因缺少计划和确认而阻断；
4. 撤销采用最近动作优先：第一次移除三个匿名引用并保留方格，第二次移除方格关系；
5. 每一步均说明原文件未被读取、移动或删除。

## 3. 为什么没有实现真实拖放

当前 `LongGrid.App` 是设置/体验验证壳层，不是 DesktopHost。仓库尚未批准生产容器、Shell 项目或跨进程拖放合同。在这里注册 `AllowDrop` 或读取 `DataPackage` 会制造“已接 Explorer”的错误产品信号，也无法证明最终桌面层的命中测试、DPI、输入门和 Shell 数据安全。

因此本切片提供符合无障碍要求的非视觉替代流程，只验证用户能否判断后果。真实鼠标/触控拖放、动作光标、取消、Explorer 数据识别和 DesktopHost 路由继续由 Issue #19 及后续生产切片验收。

## 4. 自动化与边界证据

- UI 结构合同覆盖 47 个唯一 AutomationId、4 个访问键和 5 个 `Polite` 状态区域；
- 结构门禁禁止 XAML `AllowDrop`，代码门禁禁止 `DragEventArgs`、`DataPackage` 和 `StorageItem`；
- 真实 UIA 已复读三个匿名项目、三种动作徽标状态及两步撤销；
- 既有 `System.IO`、桌面枚举、DesktopHost、Shell change 和 `FileOrganizationPlanner` 禁令继续生效；
- Release 构建 0 警告、0 错误，结构合同和真实 UIA 本地通过。

GitHub CI 是合并前最终自动证据。

## 5. 未完成与下一步

- 没有真实 Explorer/桌面拖放、文件/文件夹/快捷方式/URL/占位文件识别；
- 没有第二个真实容器，`改变归属`仅验证语义，不执行迁移；
- 没有鼠标附近动作徽标、拖入高亮、无效目标动画或触控反馈；
- 没有生产撤销栈、活动中心、持久化或文件操作补偿；
- 五人测试、Narrator/键鼠/触控人工矩阵和负责人决策仍为 Pending。

下一步先执行更新后的[Issue #23 五人测试](usability/issue-23-first-organization-test-plan.md)。若结果显示语义可理解，再为“布局恢复差异”建立最后一块低保真体验原型；不得直接跳到真实文件拖放。
