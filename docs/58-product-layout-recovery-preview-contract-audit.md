# Long方格产品布局恢复只读预览合同审计

> 审计日期：2026-08-06
>
> 范围：正式 workspace placement、保存时/当前显示拓扑双门禁、恢复状态只读呈现
>
> 结论：产品级预览合同与 UI 已建立；v1 元数据和生产拓扑来源不足时必须等待或阻断

## 1. 审计发现

Core 已有经过探针验证的 `DisplayTopologyNode`、拓扑指纹、`LayoutRecoveryPlanner` 与 `Automatic/ReviewRequired/Blocked` 规则，但正式 App 不能直接使用，原因有两项：

1. v1 配置只保存容器 `DisplayKey` 和 DIP placement，没有保存当时显示器 Bounds、WorkArea、有效 DPI、旋转与主屏标记；
2. 本文审计时 App 尚未拥有生产级、权威、只读的当前显示拓扑适配器；该缺口现已由[产品显示拓扑只读适配器审计](59-product-display-topology-adapter-audit.md)关闭，保存时拓扑缺口仍存在。

若把当前拓扑同时冒充保存时拓扑，会掩盖 DPI、工作区和显示器变化并产生假 Automatic；若把空拓扑当权威结果，会把所有显示器误判缺失。因此本切片优先固化“不得推断”的产品合同。

## 2. 有限预览状态

`ProductWorkspaceLayoutRecoveryPreview` 只返回有限计数与以下状态：

- `UnavailableSession`：没有正式 workspace state；
- `AwaitingAuthoritativeTopology`：当前拓扑不存在、不完整或未被标记为权威；
- `SavedTopologyMissing`：已有权威当前拓扑，但配置没有保存时拓扑快照；
- `Automatic`：两侧拓扑精确一致、身份精确映射且无需可见性纠正；
- `ReviewRequired`：DPI/工作区/映射或可见性发生差异；
- `Blocked`：至少一个保存时显示器无法唯一解析；
- `InvalidState`：workspace、拓扑或 DIP 转换未通过有限校验。

只有同时提供保存时拓扑、权威当前拓扑和有效 workspace 时才调用既有 `LayoutRecoveryPlanner`。结果只包含容器数、映射数、未解析数、可见性纠正数和固定 `DesktopWindowsChanged=false`，不向 App 返回 container ID、DisplayKey、StableId、Requested/Proposed Bounds 或执行计划。

## 3. App 接线与交互

概览新增“正式布局恢复只读预览”卡片。当前 App 明确以 `currentTopologyAuthoritative=false` 调用，因此：

- 有 session 时显示“等待权威显示拓扑”；
- 无 session 时显示“布局恢复预览尚不可用”；
- 不读取 probe 输出、不生成示例拓扑、不保存配置、不显示确认按钮；
- 状态通过 Polite LiveRegion 和有限 ItemStatus 复读，所有路径都声明 `DesktopWindowsChanged=False`；
- 新增 4 个稳定 AutomationId，总数由 112 增至 116，保持主题 token 和 Reduced Motion 静态基线。

权威当前拓扑现已接入；完整强样本下 v1 配置自然进入 `SavedTopologyMissing`，降级样本仍保持 Awaiting，不越过版本化元数据门槛。

## 4. 测试证据

- Core 测试覆盖 session 不可用、等待权威拓扑、缺少保存时拓扑、精确 Automatic、DPI ReviewRequired、缺屏 Blocked 与 InvalidState；
- 所有结果固定不改变桌面窗口；
- UI 源码合同覆盖 7 个有限状态、Planner 双门禁调用、count-only presentation、默认不可用状态、116 个 AutomationId 和 Reduced Motion；
- Debug 与 Release 全量构建均为 0 warning / 0 error，Core/Infrastructure/App 测试共 348/348 通过；新增预览定向测试 4/4 通过；
- 覆盖率为行 91.35%（4301/4708）、分支 83.80%（1123/1340）；Debug/Release UI 源码合同均通过，稳定 AutomationId 共 116 个；
- Release 启动链、单实例源码合同、Issue #19/#20/#23/#24 `ValidateOnly` 与已知漏洞扫描均通过；人工矩阵和专用环境证据仍保持 Pending，不以入口验证替代真实验收；
- Release 真实 UIA 在当前桌面会话调用 `FindFirst` 时返回 `0x8000FFFF (E_UNEXPECTED)`。由于系统内存在无法确认归属、无主窗口的旧 LongGrid 进程，本次结论记为环境性 Inconclusive；未擅自终止该进程，也不把结果计为产品通过或失败；
- GitHub PR 与主分支 CI 结果在发布合并后补充到 PR/Issue 审计链，避免在提交前写入不存在的流水线结论。

## 5. 后续方向

Infrastructure 产品适配器已完成完整成功才权威、稳定身份优先、fallback 显式降级、generation/latest-wins、后台采样和关闭排空。下一阶段为保存时拓扑设计版本化配置合同与迁移；在两侧元数据都成立前，不开放恢复确认或 DesktopHost 提交。
