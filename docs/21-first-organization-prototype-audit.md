# Long方格首次整理模式原型审计

审计日期：2026-08-03

基线：`main` / `0d793f6`（PR #69 已合入）

关联：[Issue #23](https://github.com/Longyuyeee/Long_Grid/issues/23)

结论：**Partial / 可进入 5 人测试准备，Issue #23 不得关闭**

后续增量：匿名容器创建与关系级撤销已由[下一阶段审计](22-anonymous-container-undo-prototype-audit.md)补齐；本文件保留 PR #70 切片的历史证据。

## 1. 需求与竞品对齐

现有竞品与交互审计确定了本切片的组合策略：

- 学习 iTop 的低门槛起点：一键建议或从空白开始；
- 学习 Fences 的直接、易懂操作语义，但不提前接 DesktopHost；
- 学习 PowerToys 的预览和逐项状态；
- Long方格增加“默认安全引用、真实移动独立说明、先预览、可撤销”的零惊吓边界。

原型不得复制竞品文案、图标、布局资产或付费墙，只吸收交互原则。

## 2. 已实现流程

`首次整理`导航页现在提供：

1. `一键建议（仅预览）`与`从空白开始`；
2. 默认选中的`安全引用`与明确标记`未开放`的`真实移动`；
3. 选择后的可见后果说明；
4. 安全引用预览或真实移动前置条件预览；
5. 全流程持续显示“尚未修改任何文件”。

安全引用预览只描述 4 个匿名引用；从空白开始不创建容器。真实移动预览固定进入 `ManagedMovePreviewBlocked`，因为当前没有源、目标、冲突检查和明确批准。

## 3. 状态合同与无障碍

| 元素 | UIA 状态 |
|---|---|
| 起点 | `SuggestedStartSelected` / `BlankStartSelected` |
| 整理模式 | `SafeReferenceSelected` / `ManagedMoveSelected` |
| 安全引用预览 | `SafeReferencePreview` |
| 真实移动预览 | `ManagedMovePreviewBlocked` |

起点与预览状态使用 `Polite` live region。四个主导航项具有访问键 1–4；31 个 AutomationId 由结构合同唯一性检查覆盖。

## 4. 响应式与安全边界

在宽屏下，两种起点和两种模式分别双列显示；低于 760 DIP 时均变为单列。真实 UIA 已在 720px 窗口逐项滚入并确认同列布局。

代码边界继续禁止：

- `System.IO`、Known Folder 和 Desktop Catalog 枚举；
- Shell change、DesktopHost 生产类型和任何窗口宿主接线；
- `FileOrganizationPlanner` 调用和真实操作计划；
- 配置写入、文件移动、删除、重命名或复制。

界面只复用 Core 的 `FileOrganizationMode` 语义枚举，不调用计划器或适配器。

## 5. 自动证据

- Release 全解决方案构建：0 警告、0 错误；
- Core 回归：90/90；
- Cobertura：行 91.43%（2412/2638）、分支 77.25%（584/756）；
- UI 结构合同：31 个 AutomationId、4 个访问键、只读边界通过；
- 真实 UIA：一键建议→空白→建议、真实移动阻断、安全引用预览、宽/紧凑/宽、主题、键盘导航和安全页通过。
- 配置持久化 20 场景、文件操作安全、缩略图隔离/清理/预算与依赖漏洞门禁通过；原生探针保持既有 `ConditionalPass` 限制。

上述结果来自本地 Windows 26200 x64 环境。GitHub CI 仍以本切片 PR 为最终证据。

## 6. 尚未完成

- 尚未执行 5 人无提示测试；
- 尚无许可证、支持系统/架构、安装渠道、首版模式和预算的负责人签字；
- 当前原型没有真实扫描、容器创建、拖放、撤销或恢复任务；
- 没有真实文件操作，因此不能证明移动确认或撤销可用。

测试执行使用[Issue #23 五人可用性测试计划](usability/issue-23-first-organization-test-plan.md)。结果和负责人决定未写回 Issue #23 前，本切片只能判定为 Partial。
