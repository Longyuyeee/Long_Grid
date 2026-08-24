# Stage 193：PF-006B1 统一 File/Folder 安全打开审计

- 日期：2026-08-24
- 分支：`codex/pf002d-create-preview`
- 起始基线：`17b0bf1`
- 对齐编号：`PF-006B1 / PF-006`
- 结论：PF-006 `InProgress`；PF-006B1 工程切片完成

## 1. 开始审计与偏移

Stage 192 后重新追踪三个激活入口，发现现状与 PF-006 目标不一致：Enter 只处理选择/布局，项目双击只进入标题双击分支，UIA `Invoke()` 实际等同 `Select()`。DesktopHost 投影刻意只持有 `item:ordinal`、可见名称和有限视觉状态，不含路径；因此不能在 HWND/UIA 内直接启动目标，也不能为打开功能破坏既有隐私边界。

本轮把目标解析固定在 App 当前权威 `ProductWorkspaceState`，由生命周期只转发 container/display/workspace revision/topology generation/item ordinal/source attestation。第一安全子切片只开放现场验证通过的 File/Folder；Shortcut/URL 在完成专用解析和协议白名单前固定返回 `ReviewRequiredKind`，不以系统默认行为掩盖未知协议风险。

## 2. 实现与需求对齐

### 2.1 三入口共用命令

- Keyboard Enter：仅在显式交互、存在 focused item、无 Ctrl/Shift/Alt、非注入且非自动重复时请求打开；
- Pointer DoubleClick：仅在显式 Surface 精确命中项目、无 Ctrl/Shift 且真实消息来源可证明时，先走共享单选，再请求打开；单击继续只选择，因此当前默认仍是双击打开；
- UIA Invoke：真实 ListItem 明确先走共享 Select，再走同一 Open 回调，选择事件顺序不依赖 UIA 客户端偶然行为；
- 生命周期要求当前显式 lease 的 container/item 与当前 display 投影同时匹配，再附加 workspace/topology 权威事实。三入口不会把路径写入投影、UIA 或生命周期摘要。

### 2.2 App 权威解析与有限状态

`ProductDesktopItemOpenController` 在串行化边界内逐项复核：来源、revision、topology、容器唯一性、显示器归属、`item:ordinal` 范围、Resolved 状态、filesystem provider、可选 VolumeId/FileId 一致性、Catalog kind 与 persisted kind、persisted/catalog target 一致、绝对路径、现场存在性、File/Folder 类型和 ReparsePoint。

成功目标由真实 Windows `ShellExecuteExW`、`open` verb、`SEE_MASK_NOCLOSEPROCESS` 提交给系统关联；API 明确接受才返回 `LaunchAccepted`，无进程句柄但 Shell 已接受也不误报失败。失败只落入 `InvalidRequest / StaleAuthority / TargetUnavailable / UnresolvedReference / TypeChanged / ReparsePointRejected / ReviewRequiredKind / LaunchFailed`。控制器不修改配置、引用归属、文件内容或真实位置。

## 3. 真实 Expected / Actual / Difference

| 场景 | Expected | Actual | Difference / 修正 |
| --- | --- | --- | --- |
| 旧 UIA Invoke | 选择后调用统一打开 | 旧实现只 Select | 有；改为 Select→Open |
| 首轮真实 HWND UIA Invoke | 只打开、不改变选择 | Windows UIA 链使 ListItem 成为选中项 | 有；正式冻结为 Select→Open 后复跑一致 |
| 真实 HWND UIA Invoke | source=AssistiveInvoke、container/item 正确、selection=true | 完全一致 | None |
| 真实 `where.exe` Shell 启动 | 目标存在、Shell 接受、有限状态 LaunchAccepted | 真实 PID > 0，进程有限退出，LaunchAccepted | None |
| 真实临时目录预检 | Folder 类型成立、目标传给唯一 launcher、目录时间不变 | 完全一致 | None |
| 真实缺失文件 | TargetUnavailable、零 launch | 完全一致 | None |
| 真实目录伪装 File | TypeChanged、零 launch | 完全一致 | None |
| Shortcut / InternetShortcut | 未审计前 ReviewRequiredKind、零 launch | 两类完全一致 | None |
| revision 陈旧 / 注入 / auto-repeat | 有限拒绝、零 launch | StaleAuthority / InvalidRequest / InvalidRequest | None |
| Shell 失败 | LaunchFailed，不猜测成功 | Win32 error 进入 LaunchFailed | None |
| 生命周期 Pointer/Keyboard 请求 | container/display/revision/topology/item/source 同一权威事实 | 两请求事实一致，仅 source 不同 | None |

真实 Shell 测试没有使用模拟成功：目标是本机现存 `%WINDIR%\System32\where.exe`，`ShellExecuteExW` 返回真实进程句柄/PID并有限退出。目录、缺失和类型变化使用实际临时文件系统对象；测试结束清理沙箱。真实 HWND 测试从 `AutomationElement.FromHandle` 获取实际 ListItem 并调用真实 `InvokePattern`。

## 4. 门禁

- Release 全量：1150/1150，0 failed，0 skipped；
- Release 全解决方案：0 warning、0 error；
- 真实 Shell File：Expected/Actual 一致，`Difference=None`；
- 真实 HWND UIA Invoke：Expected/Actual 一致，`Difference=None`；
- UI 合同：157 AutomationId，PowerShell 7 `ContractOnly`，`Outcome=Pass`；
- Live WinUI 跨进程 UIA 继续受已审计的上游 fail-fast 组合阻断；本轮真实 DesktopHost HWND/UIA 不依赖该 WinUI 路径；
- 格式与 `git diff --check`：通过；
- 零文件移动/删除/重命名、零配置写入、零路径进入 UIA/投影。

## 5. 未完成项与下一步

PF-006B1 只完成 File/Folder 的统一安全打开基础，PF-006 继续 `InProgress`。尚欠：Shortcut `.lnk` 目标解析与目标变化复核、InternetShortcut `.url` 有界读取和 `http/https` 协议白名单、失败原因的可见反馈及“定位/重试”、默认双击/可配置单击设置、PageUp/PageDown 跨视口、框选、高对比/Narrator 和物理双击/Enter 证据。

下一切片进入 **PF-006B2：Shortcut/URL 安全解析与失败反馈**。解析必须有尺寸/编码/协议上限，未知协议继续拒绝；`.lnk` 不允许借参数绕过目标类型审计。完成 B2 后再评估单击打开设置和剩余选择导航，不提前把 PF-006 标记完成。
