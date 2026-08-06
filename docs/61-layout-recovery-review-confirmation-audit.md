# Long方格布局恢复审查令牌与配置级确认审计

> 审计日期：2026-08-06
>
> 范围：布局恢复审查令牌、陈旧门禁、配置级确认、共享保存 revision、WinUI 明示确认
>
> 结论：配置级确认链已建立；真实 DesktopHost/window transaction 继续断开

## 1. 需求对齐

本阶段把只读 `ReviewRequired` 预览推进为用户可审查、可取消、可有限提交的产品流程。提交只更新 Long方格自己的容器 `DisplayKey`、DIP placement 和 v2 保存时拓扑，随后进入既有 `ProductWorkspaceSaveController`；不创建、捕获、移动、隐藏或验证任何真实桌面窗口，也不调用现有 `LayoutRecoveryTransactionCoordinator`/`ILayoutRecoveryWindowBatchAdapter`。

`Automatic` 表示拓扑精确一致且无需纠正，不生成无意义写入；`Blocked`、缺少保存拓扑、当前拓扑不权威、无产品 session、无效状态及只读备份 session 均不提供确认按钮。只有 `ReviewRequired` 且当前 session 可写时才签发令牌。

## 2. 令牌与复核边界

令牌只含有限元数据，不含显示器 ID、容器 ID、坐标或路径：

- 保存时拓扑 SHA-256 指纹；
- 当前权威拓扑 SHA-256 指纹；
- 当前完整配置序列化快照 SHA-256 指纹；
- 当前 topology generation；
- 共享 edit revision；
- 容器、显示映射和可见性纠正计数。

确认时不信任令牌携带的结论，而是重新验证配置、重新计算两侧拓扑、重新运行 planner 并重新生成令牌。topology generation 或 edit revision 首先发生变化时分别返回有限陈旧错误；相同 revision 下配置/拓扑/摘要不一致返回 `TokenMismatch`。取消确认返回 `ConfirmationRequired`，不生成 edit、不递增 revision、不进入保存队列。

## 3. 配置级恢复规则

通过复核后，planner 的目标像素矩形按目标显示器 WorkArea 与有效 DPI 转回 DIP：位置相对 WorkArea，尺寸按 `96 / DPI` 换算，显示器键更新为当前映射 StableId。新状态保存当前权威拓扑作为下一次历史基线，并再次通过正式 v2 projector/validator。

锁定方格若需要改变 placement，整个确认返回 `ContainerLocked`，不做部分恢复。任一容器、显示映射或投影缺失均收敛为 `InvalidState`。提交协调器与引用/容器编辑共享同一把锁、同一 edit revision 和同一个 save controller；只有 save controller 接受后才递增 revision 并替换 App 内存 session。

## 4. 交互与隐私

正式恢复卡新增一个稳定 AutomationId。按钮仅在可写 `ReviewRequired` 状态可见；二次对话框默认焦点为取消，并明确说明确认只修改 Long方格配置、不移动真实窗口。取消不调用提交委托；拒绝结果只呈现有限枚举，不暴露指纹或领域身份。成功后 App 立即用返回的 v2 Document 重建 session，新的保存时拓扑与当前拓扑一致，旧令牌自然失效。

## 5. 自动证据

定向测试覆盖：令牌字段与双指纹、显式取消、topology generation/edit revision 陈旧、配置指纹不一致、非权威/缺失/Automatic/Blocked 状态、锁定方格、配置级 DPI 恢复、共享 revision、唯一 save controller 提交、controller 已完成时拒绝和空参数边界。现有全量配置、拓扑、恢复 planner、App 保存及存储测试继续运行。

本地当前为 398/398 测试通过；CI 等价 TRX/XPlat Coverage 为行 91.79%（10110/11014）、分支 84.00%（2688/3200），通过仓库 90%/75% 门槛。正式恢复卡新增 1 个 AutomationId，总数为 117。Debug/Release 构建、启动链、UIA/单实例源码合同、Issue #19/#20/#23/#24 `ValidateOnly`、配置持久化探针和漏洞门禁均通过；DesktopHost、文件操作安全与缩略图隔离探针按其已记录限制返回 `Conditional Pass`。真实 UIA 在当前会话调用 `FindFirst` 时返回 `0x8000FFFF (E_UNEXPECTED)`，结合 PID 39208 的 `MainWindowHandle=0` 判定为环境 `Inconclusive`，不能计作产品通过或失败。远端 CI 在发布流程中继续复核。

## 6. 未关闭风险与下一阶段

- 真实 UIA 仍受当前会话 PID 39208 无主窗口残留影响；未获授权前不终止该进程；
- Issue #20 的多显示器、热插拔、DPI/旋转与登录会话人工矩阵仍 Pending；
- 当前确认会形成新的原子配置与既有轮转备份，但尚未提供面向用户的一次性“撤销本次布局恢复”入口；
- `LayoutRecoveryTransactionCoordinator` 仍只属于探针/未来真实窗口阶段，App 零引用；
- 桌面文件移动、图标位置写入、Explorer 注入和真实窗口操作仍为零。

下一阶段应建立配置恢复前后快照与一次性撤销令牌：撤销也必须绑定恢复提交 revision、当前配置指纹和用户确认，并继续只走配置保存控制器。完成配置撤销和真实人工矩阵之前，不准连接窗口批处理适配器。

> 后续状态：上述一次性配置撤销已由 [布局恢复一次性配置撤销审计](62-layout-recovery-one-time-undo-audit.md) 落地；真实窗口适配器仍保持断开。
