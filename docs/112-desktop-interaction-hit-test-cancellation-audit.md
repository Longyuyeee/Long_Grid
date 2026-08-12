# Stage 112：桌面交互命中与取消适配器审计

日期：2026-08-12
阶段：B2（隔离适配器完成，正式 DesktopHost 仍不可交互）

## 1. 本阶段目标

Stage 111 已建立独立默认关闭、最长 5 秒且绑定三类 generation 的交互 lease。Stage 112 在不改变正式 HWND 的前提下补齐 lease 之前与期间的两个边界：

- 使用正式渲染/UIA/Region 共用的几何算法解析方格命中；
- 把明确 pointer、keyboard 或辅助技术激活转换为有限 B1 意图；
- 把 Esc、失焦、Win+D、全屏、会话、RDP、Explorer 和 shutdown 信号统一映射为 lease 取消；
- 对 evidence/timer 信号继续调用 B1 generation/attestation 复核；
- 不订阅 Explorer 内部窗口，不注册全局键盘钩子，不把正式 HWND 变为可点击。

## 2. 需求对齐

| 产品需求 | Stage 112 对齐 | 尚未开放 |
|---|---|---|
| 桌面方格可点击/键盘进入 | 明确三种激活来源生成同语义、5 秒有效意图 | 正式 HWND 输入接线、pointer capture |
| 华丽但稳定的直接交互 | header/content/item 命中语义确定，为 hover/按压/选择动效提供稳定输入 | 视觉 hover、按压和焦点动画 |
| 多显示器与 DPI | 命中直接复用 `ProductDesktopHostSurfaceLayout` 的 DPI/边界钳制 | 真实多屏手工矩阵仍待执行 |
| 不抢占桌面与前台应用 | 正式窗口继续 `HTTRANSPARENT`、NoActivate；失焦/Win+D 只会取消 | 不改变 Z-order、前台和 Explorer 窗口 |
| 显示器/Explorer/RDP 变化安全 | 定义统一取消信号，generation evidence 变化交给 B1 复核 | B3/B4 才连接受控事件源 |
| 锁定方格与文件安全 | B1 仍拒绝锁定目标；B2 只产生意图，不执行配置或文件动作 | 移动、复制、删除及真实拖放继续关闭 |

任务栏美化、自定义普通窗口、Widget/Long 助手插件兼容仍属于独立后续阶段，本切片未扩大它们的权限。

## 3. 命中模型审计

### 3.1 单一几何来源

`ProductDesktopInteractionHitTestAdapter` 对每个方格调用 `ProductDesktopHostSurfaceLayout.GetContainerBounds`，因此与正式 GDI 渲染、Window Region 和只读 UIA Bounds 使用相同的 DPI 换算、最小尺寸与工作区钳制。坐标使用左/上包含、右/下排除的半开区间，避免边界像素同时命中相邻区域。

### 3.2 命中结果

命中结果区分：

- `OutsideSurface`：坐标不在当前 display 工作区客户区域；
- `NoTarget`：在 surface 内但没有方格；
- `AmbiguousTarget`：两个或更多方格重叠命中；
- `Hit`：精确单一目标，并进一步区分 header、content、visible item。

当前没有经过证明的方格 Z-order，因此重叠时 fail closed，不按配置顺序猜测“顶层”。折叠方格只公开 header；空白内容区和被极小工作区裁掉的残缺项目行不会伪造成项目，完整可见行计数与只读 UIA 合同一致。结果构造器不是公开 API，调用方不能直接自报 `Hit`。

### 3.3 有限意图工厂

意图工厂仅接受命中适配器产生的有效结果、明确支持的 primary pointer/keyboard/assistive activation、非空 intent ID 及正数 generation。它从当前 evidence 复制 workspace/topology/window-registry generation，并固定在 B1 最大 5 秒边界到期；极端 UTC 加法溢出按无效 evidence 拒绝。

工厂不授予权限。创建后的意图仍必须进入 B1，重新检查 DesktopHost 状态、UIA/被动窗口证明、generation、目标存在与锁定状态。

## 4. 统一取消模型

`ProductDesktopInteractionCancellationAdapter` 将以下直接信号映射为可审计原因并清除活跃 lease：

- Esc；
- 焦点丢失；
- Win+D/显示桌面请求；
- 全屏转换；
- 锁屏或会话断开；
- RDP/远程会话转换；
- Explorer 重启；
- 应用 shutdown。

`EvidenceChanged` 与 `LeaseTimerElapsed` 必须携带 evidence，并委托 B1 `Revalidate`，从而保留对到期、宿主、attestation、workspace、topology、registry 和目标状态的精确原因。直接信号禁止夹带无关 evidence；没有活跃 lease 时取消是幂等零变化。

## 5. 接线隔离与门禁

工程 UI 合同证明：

- 命中适配器确实调用共享 SurfaceLayout；
- 歧义命中、有限时间和全部主要取消信号存在；
- evidence 变化调用 B1 复核；
- 正式 `WindowsProductDesktopHostReadOnlySurface` 对 `WM_NCHITTEST` 仍返回 `HTTRANSPARENT`；
- `LongGrid.App` 和正式 read-only surface 都没有引用 B2 适配器。

所以 Stage 112 没有用户可见行为变化，也没有把开发开关转化为发布许可。

## 6. 自动化证据与限制

新增确定性测试覆盖：

- 96/192 DPI header、item、content、极小工作区残缺行与半开边界；
- 折叠、surface 外部、无目标和重叠歧义；
- 三种明确激活、完整 generation 绑定和固定 5 秒寿命；
- miss、空 ID、未知激活、非正 generation 与 UTC 溢出；
- 八类直接取消信号；
- evidence generation 变化、timer 未到期/到期、缺失/多余 evidence 与被动态幂等。

这些测试不是实际 pointer、keyboard、Narrator、Win+D、全屏、锁屏、RDP 或 Explorer 重启证据。A5 真实会话矩阵继续保持 `PendingManualEvidence`。

## 7. 下一切片

下一步为 **B3：选择模型、焦点语义与只读 UIA Selection 合同**：

1. 建立单选、Ctrl/Shift 多选、方向导航和选区 anchor 的纯状态机；
2. 选择只改变 Long方格内存/配置引用，不读取文件内容；
3. 明确 pointer capture、焦点环、Esc 与 Reduced Motion 语义；
4. 为交互模式设计可聚焦 UIA Fragment 和 Selection/SelectionItem，但保持 Passive 模式完全只读不可聚焦；
5. 先在隔离 probe 验证，不直接修改正式 read-only surface。

B3 稳定后，B4 才考虑受控正式输入窗口切换；方格拖动/缩放和安全引用拖放继续在更后续切片，真实文件变更仍默认关闭。
