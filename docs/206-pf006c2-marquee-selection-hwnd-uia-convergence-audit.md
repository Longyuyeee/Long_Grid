# Stage 206：PF-006C2 鼠标框选、真实 HWND 与 UIA 收敛审计

日期：2026-08-25
开发项：PF-006C2
当前结论：**Engineering Complete / Integrated / Product Evidence Pending**

## 1. 开发目标与需求边界

本切片补齐 PF-006 最后一项工程缺口：在正式 DesktopHost 显式交互窗口中，从当前方格的空白内容区拖出框选矩形，并让框内可见项目通过现有选择控制器与 UIA SelectionPattern 一次收敛。

本轮边界固定如下：

- 仅处理当前激活方格和当前最多 12 项可见视口，不跨方格、不跨页；
- 只从方格内容区中项目列表之后的空白处起框，不抢标题拖动、边框缩放、项目单击或桌面空白拖画建格；
- 普通框选替换选择，Ctrl 框选追加，Shift 框选暂时失败关闭；普通空框清除，Ctrl 空框保持原选择；
- 只接受正式 Explicit 模式、已证明且非 Injected 的输入源；
- 不移动、重命名、删除或读取真实文件内容，不改变项目归属。

该范围直接对齐 iTop Easy Desktop/Fences 的低摩擦多选价值，没有扩展到任务栏美化、Widgets、插件运行时或文件搬运，因此没有偏离桌面整理主线。

## 2. 实现与安全设计

### 2.1 单一选择事实

`ProductDesktopSelectionRequest` 增加有界 `SelectItems` 动作。控制器在一次 Apply 内按当前可见顺序生成结果：

- 普通框选清空旧集合后写入命中项；
- Ctrl 框选与旧集合求并集；
- focus/anchor 统一落到第一个命中项；
- 非 Ctrl 空集合同时清除 selection/focus/anchor；
- 请求含未知 ID、重复 ID、超过上限、同时携带单项与多项、非法修饰键时零变更失败关闭。

框选没有建立第二套选择状态；pointer、keyboard 与 UIA 继续读取同一个 revisioned selection snapshot。

### 2.2 冻结事实与取消

框选会话冻结 display/container、容器 Bounds、内容起点、项目高度、lease intent、workspace revision、topology generation、window registry generation、selection revision 与可见项目 ID 顺序。更新或完成前任一事实漂移都会取消，不提交猜测结果。

Surface 使用原生鼠标捕获，Escape、`WM_CANCELMODE`、`WM_CAPTURECHANGED`、Presentation 更新、Passive/Hidden 和 Dispose 都会释放捕获并清除预览。完成路径先终止会话和释放捕获，再向生命周期提交一个 `SelectItems` 请求；正式生命周期仍负责唯一 Surface/UIA 刷新。

### 2.3 视觉与输入边界

预览使用原生 `DrawFocusRect`，指针被夹紧到冻结方格的内容范围。真实消息入口继续调用 `GetCurrentInputMessageSource` 并拒绝 Injected 来源；测试辅助入口只用于向真实 HWND 内部注入已证明事实，文档不将其记作物理鼠标证据。

`Test-LongGridUi.ps1` 新增静态门：必须保留有界多项请求、冻结 revision/visible IDs、正式输入源证明、取消路径和原生预览，App 仍不得出现第二选择控制器。

## 3. 真实测试与 Expected / Actual / Difference

| 验收 | Expected | Actual | Difference / Correction |
| --- | --- | --- | --- |
| 几何命中 | 从 `(300,220)` 拖到 `(30,120)`，按可见顺序选中 item-2/item-3 | 单一请求得到 `item-2,item-3` | None |
| 原子提交 | 一次手势仅调用一次 selection Apply | `AtomicApplyCount=1` | None |
| UIA 收敛 | SelectionPattern 返回相同两个项目 | 真实 HWND/UIA 返回两个项目 | 初始测试误把 UIA Name 预期为短标题；实际既有契约为“标题；文件；类型图标已就绪”，已修正测试预期，项目身份与数量始终正确 |
| 输入安全 | Injected 起框拒绝 | `InjectedStartRejected=true` | None |
| 取消 | CancelMode、Presentation、Passive 清除会话且零额外提交 | 三类均清除，Apply 仍为 1 | None |
| 前台所有权 | 框选不抢前台 | 前后 foreground HWND 相同 | None |
| 漂移矩阵 | intent/workspace/topology/registry/selection/visible IDs/Bounds/mode 任一变化均取消 | 8/8 失败关闭 | None |
| 选择合同 | 普通/Ctrl/空框、非法 ID/重复/Shift 均确定 | 选择与框选定向测试 75/75 | None |
| Release 全量 | 0 fail | 1225/1225，约 11 s | None |
| 覆盖率 | lines ≥90%、branches ≥75% | 90.41%（41562/45970）、75.74%（13522/17854） | None |
| UI 静态合同 | 157 AutomationId 与新增框选门通过 | Contract-only Pass | None |
| 构建/格式 | 0 warning / 0 error；格式和 diff 检查通过 | Pass | None |

覆盖率来自独立目录 `artifacts/TestResults-PF006C2-20260825` 的本轮完整 collector，不聚合历史报告。真实 HWND 测试使用真实 Win32 Surface 和真实 UI Automation provider，但其起框由进程内已证明测试入口触发，不等价于真人鼠标、Narrator 或高对比可见验证。

## 4. 需求对齐审计

PF-006 的工程范围现已具备：单击/Ctrl/Shift 选择、框选与空白清除、方向/Home/End/PageUp/PageDown/Ctrl+A、Enter/双击/UIA Invoke、安全 File/Folder/Shortcut/URL 打开、单击策略、有限失败反馈、权威重试与安全 Explorer 定位。

仍不能把 PF-006 标记为产品 `Complete`，因为以下证据尚未取得：

- 真人物理鼠标框选及高 DPI/负坐标可见截图；
- Narrator 的选择事件顺序与播报；
- 高对比模式下 selection/focus/marquee 的可区分性；
- 与正式 App 可见会话相关的既有 Windows App SDK UIA 上游阻断复测。

因此 PF-006 不标记产品 `Complete`，但在 PR 与 main 全链均绿色后可准确提升为 `EngineeringComplete / ProductEvidencePending`。工程主线转入 PF-007 Explorer 拖入与方格间拖放。

## 5. 集成状态

PR #233 run `32816426703` 完整通过，并 squash 合入 `main@5c78ddc`。合入后的 main run `32816956265` 再次完整通过：

| 验收 | PR Actual | Main Actual | Difference |
| --- | --- | --- | --- |
| Release 测试 | 1225/1225，11 s | 1225/1225，12 s | None |
| 覆盖率 | 90.08%（41412/45970）/75.60%（13498/17854） | 90.09%（41414/45970）/75.60%（13498/17854） | 均高于 90%/75%，采样差异不影响门禁 |
| 文件/Worker 清理 | `ConditionalPass`，`CleanupSucceeded=true`，Profile deleted | 同左，Profile deletion attempt=1 | None |
| 依赖漏洞 | 无已知漏洞 | 无已知漏洞 | None |
| RC 清单 | 800/800 | 800/800 | None |

本机、PR 和 main 三层证据均收敛。PF-006C2 状态为 `EngineeringComplete / Integrated / ProductEvidencePending`，PF-006 整体提升为 `EngineeringComplete / ProductEvidencePending`；30 个 PF 项仍为 `0 Complete`，因为物理鼠标、Narrator 和高对比产品证据没有被工程自动化替代。
