# Long方格布局恢复差异原型审计

审计日期：2026-08-03

基线：`main` / `ddb0f4e`（PR #72 已合入）+ 当前短生命周期分支

关联：[Issue #23](https://github.com/Longyuyeee/Long_Grid/issues/23)、[Issue #20](https://github.com/Longyuyeee/Long_Grid/issues/20)

结论：**Partial / 恢复差异心智模型可进入五人测试；真实布局恢复、硬件矩阵和生产事务仍未实现**

## 1. 需求与 Core 对齐

PRD 要求恢复前展示差异、失败时回退最近有效快照。交互规范进一步要求 UI 直接区分 Core 规划状态：

- `Automatic`：拓扑等价、精确映射且零位置纠正；
- `ReviewRequired`：显示 requested/proposed 差异和可见性纠正，必须确认；
- `Blocked`：缺失或歧义映射，禁止部分应用；
- 新显示变化出现时，旧预览必须过期且不可确认；
- 内部指纹、代次、硬件路径和评分不得直接展示。

本切片只复用纯 Core 的 `LayoutRecoveryStatus` 枚举，没有调用 `LayoutRecoveryPlanner`、稳定器、事务协调器或 DesktopHost 适配器。

## 2. 已实现的匿名流程

新增第五个导航入口“恢复预览”，提供三个固定匿名场景：

1. `AutomaticRecoveryPreview`：两个方格保持匿名显示区域，零位置纠正；
2. `ReviewRequiredRecoveryPreview`：两个方格映射到主显示区域，一个方格需要最小可见性纠正；
3. `BlockedRecoveryPreview`：一个显示区域缺失或歧义，明确禁止只应用已匹配部分；
4. `RecoveryPreviewExpired`：模拟新的显示变化后，旧确认入口立即禁用；
5. `RecoveryPreviewAcknowledged`：只记录用户理解 ReviewRequired 差异，不执行恢复；
6. `RecoveryPreviewCancelled`：关闭差异并保持当前布局。

页面持续说明没有读取显示器、创建快照或移动方格。`Automatic` 和 `Blocked` 均不开放确认理解按钮；只有 `ReviewRequired` 可以确认，并且按钮文案明确“不执行”。

## 3. 隐私与安全边界

原型只使用“主显示区域”“匿名显示区域”和数量摘要，不包含：

- PNP ID、设备路径、EDID、adapter LUID、source/target ID；
- 拓扑指纹、内部 generation、映射分数；
- 真实显示器 Bounds、DPI、方向或坐标；
- 用户容器名、文件名、路径或桌面截图。

结构门禁继续禁止显示拓扑稳定器、恢复规划器、恢复事务协调器、DesktopHost 窗口规划器及所有文件/桌面适配器调用。

## 4. 自动化证据

- UI 结构合同覆盖 61 个唯一 AutomationId、5 个访问键和 6 个 `Polite` 状态区域；
- 结构检查固定三种 Core 状态、过期和取消合同；
- 真实 UIA 已复读 ReviewRequired、差异可见、过期禁用、重新确认理解、Blocked 禁止确认、Automatic 和取消；
- Release 构建 0 警告、0 错误，结构合同和真实 UIA 本地通过。

GitHub CI 是合并前最终自动证据。

## 5. 未完成与停止规则

- 没有读取真实显示器或创建 `LayoutRecoveryPlan`；
- 没有 requested/proposed 坐标级逐项比较、屏幕内容交换或手动映射；
- 没有保存/读取快照、执行窗口批量移动、事务验证或补偿回滚；
- 没有自动恢复、活动中心记录、恢复 Toast 或诊断导出；
- 动态 DPI、旋转、拔插、投影、睡眠和 RDP 矩阵仍由 Issue #20 保持 Pending；
- 五人测试和负责人决策仍未完成。

本原型完成后，Issue #23 的核心低保真链路已具备测试形状。下一动作应是执行更新后的[五人无提示测试](usability/issue-23-first-organization-test-plan.md)并记录缺陷，而不是继续增加相邻演示场景或直接启用真实自动恢复。
