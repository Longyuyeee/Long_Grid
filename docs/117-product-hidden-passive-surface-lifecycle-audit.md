# Stage 117：产品 Hidden/Passive Surface 生命周期审计

日期：2026-08-13
阶段：B6b（产品 adapter Hidden/Passive 通过；Explicit 与真实文件操作仍关闭）

## 1. 目标与结论

Stage 116 已把双 opt-in、emergency-disable、暂停/恢复和 shutdown 策略接入 App，但没有产品原生 adapter。B6b 的目标是把正式产品自有 HWND 纳入该安全控制器，同时避免“窗口先显示、registry 后注册”的未证明间隙。

结论为 **Conditional Pass**：自动化、真实本机 HWND Region 往返和故障矩阵通过；真实 Win+D、全屏、锁屏/RDP、Explorer 重启、多显示器 DPI、Narrator、触控与笔仍需人工会话证据，因此不能开放 Explicit。

## 2. 启动顺序

只有以下条件同时成立才走受控产品 Surface 路径：

1. `LONGGRID_ENABLE_DESKTOP_HOST=1`；
2. `LONGGRID_ENABLE_DESKTOP_INTERACTION=1`；
3. `LONGGRID_DISABLE_DESKTOP_INTERACTION` 不精确等于 `1`。

顺序固定为：

1. 创建产品自有 ToolWindow/Layered/NoActivate/Transparent HWND；
2. 应用空 Window Region 并保持隐藏；
3. 写入实例标记并复核进程、线程和 HWND 所有权；
4. 把全部显示器窗口注册到产品 registry；
5. 形成 workspace revision、topology generation 和 registry generation 证明；
6. 控制器先复核 Hidden contract，再让 adapter 恢复正式 Region；
7. `SW_SHOWNOACTIVATE` 后复核 Passive contract，最后发布 ReadyReadOnly。

任一步失败都不发布 InteractionSurfacePassive。

## 3. 产品 adapter 合同

`ProductDesktopHostPassiveSurfaceModeAdapter` 实现 B4 接口的严格子集：

- `Capture` 只接受“全部 Passive”或“全部 Hidden”，混合状态失败；
- `ApplyPassive` 与 `Hide` 必须精确匹配构造时 registry generation；
- `Restore` 只接受 Passive/Hidden；
- `ApplyExplicit` 固定返回 false；
- Passive 批量应用或复核失败时尝试隐藏全部 Surface；
- 不枚举 Explorer，不调用前台 API，不接触路径或文件。

交互开发控制器快照公开 adapter 连接布尔值和有限 Passive/Hidden evidence，但不公开 HWND、原始 ID、路径或线程对象。DesktopHost 生命周期快照继续只描述宿主本身，避免维护第二份可能陈旧的交互状态。

## 4. 原生 Window Region 与消息

产品 Surface 新增动态复核：

- Hidden：`IsWindowVisible=false`、`GetWindowRgn=NULLREGION`、稳定窗口策略成立；
- Passive：窗口可见、Region 非空、ToolWindow/Layered/NoActivate/Transparent、非 Topmost、无 Owner、不拥有前台；
- 两种模式都保持只读 UIA provider；
- `WM_NCHITTEST` 始终 `HTTRANSPARENT`；
- `WM_MOUSEACTIVATE` 始终 `MA_NOACTIVATE`。

因此 B6b 没有鼠标命中窗口，也没有 Selection/Invoke/拖放入口。

## 5. 关闭、替换与故障补偿

生命周期释放顺序为：

1. 让开发控制器隐藏并 detach 当前 adapter；
2. 从 registry 注销每个显示器窗口；
3. 销毁 HWND、清除 UIA provider 和实例属性；
4. 断开 host identity。

同一顺序用于 topology refreshing、冲突更新、创建失败和正常 shutdown。Passive 证明失败会隐藏并释放整个批次，生命周期报告 Faulted，不能冒充 Ready。

## 6. 自动化证据

新增或扩展测试覆盖：

- 双 opt-in 的 fake Surface 确认“创建 Hidden → ApplyPassive → 发布”；
- 真实 Windows 产品 HWND 确认 Hidden Region → Passive Region → suspend Hidden；
- adapter 拒绝 Explicit 和陈旧 generation；
- 不完整 Passive 证明导致隐藏、Faulted 和销毁；
- 系统暂停后捕获 Hidden contract，完整证明才恢复 Passive；
- adapter identity 不匹配不能错误 detach；
- shutdown/生命周期释放前发生隐藏。

当前 Release 全解决方案构建为 0 warning / 0 error，828/828 自动化通过；最终干净 Cobertura 行覆盖率 91.91%（21578/23478），分支覆盖率 81.21%（6732/8290），高于 90%/75% 门槛。格式、locked restore、142-ID UI 合同、启动/会话入口、单实例、CI hang、RC restore、配置持久化 20 场景和依赖漏洞门禁通过；文件安全与缩略图隔离继续保持既有 ConditionalPass 限制。RC 哈希、PR CI 和 main CI 在提交收口时复核。

## 7. 权限边界

本阶段没有：

- 产品 hit-test adapter 或 intent factory 接线；
- Explicit、键盘焦点、Selection pattern 或拖放；
- 合成输入、全局 hook、Explorer/WorkerW/Progman 挂接；
- 文件移动、复制、删除、重命名或内容读取；
- 任务栏美化、小组件、Long助手插件或网络权限。

真实文件能力在控制器快照中继续固定为 false。

## 8. 限制与 B6c

B6b 自动化不能替代 A5-01..A5-06 与 Issue #19/#20 的真实会话矩阵。当前 App 只在 shutdown 路径实际触发完成；失焦、Win+D、全屏、锁屏/RDP 和 Explorer 重启的系统事件来源仍未接到控制器。

下一切片 B6c 应先完成：

1. 有界、可审计的系统表面事件来源；
2. 产品 registry/evidence 到 B1 intent 的最小转接；
3. Explicit 前的二次用户动作与最长 5 秒 lease；
4. 失焦/Esc/Win+D/全屏/会话/RDP/Explorer 的真实取消证据；
5. 专用环境人工矩阵与紧急退出确认。

在 B6c 人工证据通过前，正式 HWND 必须继续 `HTTRANSPARENT`，不得启用 Explicit 或真实文件操作。
