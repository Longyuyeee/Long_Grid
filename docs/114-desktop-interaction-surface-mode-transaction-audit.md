# Stage 114：隔离交互 Surface 与输入模式事务审计

日期：2026-08-12
阶段：B4（产品形状的隔离事务完成；正式 DesktopHost 仍保持被动只读）

## 1. 阶段目标与结论

Stage 111–113 已提供准入 lease、共享命中语义、统一取消、选择/焦点及隔离 UIA Selection。Stage 114 补齐真正触碰原生 surface 之前的事务边界：

- 从经过证明的 Passive surface 开始；
- 准入成功后切到 Explicit，并立即复读全部窗口、命中和 UIA 证据；
- B3 selection 和 UIA snapshot 只能在已验证 Explicit 状态发布；
- Esc、失焦、Win+D、全屏、会话/RDP、Explorer、到期及 generation 漂移统一回到 Passive；
- Apply 或验证失败恢复精确基线；恢复失败立即隐藏；隐藏失败明确报告，绝不谎报安全；
- 不持有 HWND、路径、PIDL、Shell 对象或文件内容，不接入正式 App。

## 2. 需求对齐

| 原始需求 | Stage 114 对齐 | 仍未开放 |
|---|---|---|
| 桌面方格可直接点击和键盘访问 | 明确 Passive→Explicit 的原子切换及可命中/可聚焦合同 | 正式 HWND 消息和真实 pointer/keyboard 输入 |
| 鼠标、键盘和 Narrator 等价 | Explicit surface 同时要求可命中、可聚焦与 Selection pattern，复用 B3 语义 | 真实 Narrator、触控、笔与焦点环会话证据 |
| 不抢前台、不污染任务切换 | 每次切换复核 ToolWindow、NoActivate、非 Topmost、无 Owner、不拥有前台 | 真机 Win+D/Alt+Tab/全屏矩阵仍 Pending |
| Explorer/显示器变化安全恢复 | lease 到期及 workspace/topology/registry 漂移统一取消；恢复失败隐藏 | 正式事件源与真实 Explorer 重启接线 |
| 桌面文件安全 | 输入只含匿名 item ID；不读内容、不移动/复制/删除 | 安全引用拖放与真实文件动作属于后续独立阶段 |

任务栏美化、自定义普通窗口特效、Widget Host 和 Long 助手插件兼容不属于本切片权限范围。

## 3. Surface 合同

`ProductDesktopInteractionSurfaceEvidence` 只接受三个有限模式：

- `Passive`：Visible、HTTRANSPARENT、不可键盘聚焦、无 Selection pattern；
- `Explicit`：Visible、可命中、可键盘聚焦、Selection pattern 可用；
- `Hidden`：不可见、HTTRANSPARENT、不可聚焦、无 Selection pattern。

三个模式共同强制正数 window registry generation、ToolWindow、NoActivate、非 Topmost、无 Owner且不拥有前台。任何未定义或混合状态都不是有效合同。

## 4. 事务顺序与失败关闭

进入 Explicit 的固定顺序为：捕获 Passive 基线 → 复核 intent/evidence/registry generation → B1 admission → ApplyExplicit → 复读 Explicit 合同 → 创建最多 256 个匿名 ID 的 B3 selection → 发布 UIA Selection snapshot。

Apply、复读或 selection 创建失败时取消 lease，恢复捕获的完整 Passive evidence，并复读逐字段相等。恢复失败则调用 Hide 并复核 Hidden 合同；Hide 调用或复核失败返回 `EmergencyHideFailed`，不会把未知窗口状态标记为安全。重复进入、被拒绝准入和无状态变化不会增加事务 revision。

## 5. 取消与可访问性

取消继续复用 Stage 112 adapter。仍有效的 timer/evidence 复核保持 Explicit；lease 到期或任一 generation 漂移先取消 B1 lease，再切回并复核 Passive。回到 Passive 后 selection 被释放，UIA 回到 pattern-free/nonfocusable。选择操作使用 Stage 113 同一控制器，陈旧 lease 或可见项目变化不推进事务 revision，也不伪造新的 UIA 选择。

## 6. 接线隔离审计

`eng/Test-LongGridUi.ps1` 新增 B4 静态门禁：

- 必须存在 Passive/Explicit/Hidden、窗口策略、generation、restore/hide 与 B3 接线；
- `LongGrid.App` 不得引用 B4 协调器/adapter；
- 正式 `WindowsProductDesktopHostReadOnlySurface` 不得引用 B4，且 `WM_NCHITTEST` 继续返回 `HTTRANSPARENT`；
- 正式 UIA provider 继续不实现 Selection/SelectionItem。

因此本阶段没有改变用户当前可见行为，也没有把开发 opt-in 转化为发布许可。

## 7. 自动化证据与限制

定向测试覆盖成功进入、选择/UIA 同步、Passive 合同逐字段漂移、准入拒绝、Apply/显式复核/项目模型失败补偿、恢复失败隐藏、隐藏失败、Esc/Explorer/expiry/generation 取消、live timer、陈旧选择、重复进入、被动取消和异常捕获。

本地最终证据：Release 全解决方案构建为 0 warning / 0 error；803/803 自动化测试通过；单份 Cobertura 行覆盖率 91.84%（10427/11353）、分支覆盖率 81.17%（3209/3953），高于 90%/75% 门槛；142-ID UI 源码合同、锁定恢复、格式、启动链、干净会话、单实例、VSTest hang diagnostics、RC restore 与依赖漏洞门禁通过。精确提交 RC、PR CI 和合并后 main CI 在提交/推送后独立复核。

这些自动化测试不是实际鼠标、键盘、触控、笔、Narrator、Win+D、全屏、锁屏、RDP、Explorer 重启或多显示器硬件证据。A5、Issue #19/#20 与 BSA 人工矩阵继续保持 PendingManualEvidence。

## 8. 下一切片

下一步建议为 **B5：受控真实 HWND 交互适配器探针**：

1. 只创建探针自有、无用户数据、默认隐藏的顶层 HWND；
2. 实现 B4 adapter，验证 `WS_EX_TOOLWINDOW/NOACTIVATE`、非 Topmost、无 Owner、前台不变；
3. 验证 Passive/Explicit 的 Region、`WM_NCHITTEST`、`WM_MOUSEACTIVATE`、焦点和 UIA provider 切换；
4. 注入每一步失败并复读 Passive/Hidden 补偿与 USER/GDI/句柄闭环；
5. 仍不连接正式 App、Explorer 内部窗口、真实桌面文件或全局 hook。

B5 探针证据稳定后，才能评估正式 DesktopHost 的受控开发 opt-in 接线。框选、拖动/缩放和安全引用拖放继续拆成后续独立切片。
