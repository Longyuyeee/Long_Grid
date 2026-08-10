# 批量引用加入与一次撤销审计

> 日期：2026-08-10
> 范围：未分组桌面项目多选、原子配置提交、整批一次撤销
> 安全边界：零桌面文件内容读取、写入、移动、删除或重命名

## 1. 需求对齐

本切片落实交互审计中的有限批量选择：用户在一个权威 Catalog 代际内，以多选列表选择最多 256 个未分组项目，并加入一个未锁定正式方格。按钮在执行前显示项目数，确认框默认焦点为取消，并明确操作仅修改 Long方格配置。

当前只开放“多个未分组项目 → 一个目标方格”的共同动作。跨方格混合选择、批量改归属、真实文件整理和拖拽批处理继续关闭，避免把配置引用管理误解为文件移动。

## 2. 原子性与并发门禁

`ProductWorkspaceReducer.AddResolvedReferences` 在一个不可变状态转换中追加整批引用。提交协调器同时复核 edit revision、Catalog generation、目标 ordinal、1..256 数量、索引范围、批内索引唯一、批内 canonical target 唯一、工作区全局 target 未占用以及目标方格未锁定。

成功路径只生成一个新状态、调用一次 `saves.Submit`、推进一次 revision。任一输入或 reducer 校验失败时返回单一失败结果，不保存部分条目。

## 3. 一次撤销

接受批次后生成一次性撤销令牌，绑定操作 ID、加入后的 revision、加入后配置指纹和恢复配置指纹。撤销需显式确认并再次通过 revision、令牌和双指纹校验；任何其他成功编辑或外部 revision 推进都会使令牌失效。撤销成功只提交一次完整恢复状态，随后立即消费令牌。

## 4. 隐私、可访问性与 UI 合同

UI 只接收可见名称、类型、ordinal、Catalog generation/index，不接收持久化 target、路径、ProfileId、SourceId、ContainerId、ItemId、ParsingName、VolumeId 或 FileId。列表使用 `SelectionMode=Multiple`，支持 Windows 标准 Ctrl/Shift 选择；状态文本和机器状态持续声明 `DesktopFilesChanged=False`。

新增批量撤销按钮后，权威源码 UIA 合同由 129 增至 130 个 AutomationId；干净会话验证同步要求 130。

## 5. 验证结论

自动化测试覆盖：整批加入、一个 revision、整体撤销一次、第二次撤销拒绝、后续成功编辑使令牌失效、空批次、重复索引、旧 Catalog 代际、输入状态不变，以及两个真实临时文件内容前后相同。全量 Release 测试为 541/541 通过；单份 Cobertura 为行 91.15%（7984/8759）、分支 80.25%（2256/2811），通过 90%/75% 门槛。Release 构建为 0 警告、0 错误；130-ID 源码合同与干净会话 `ValidateOnly` 已通过。内部 RC 与 PR/main 双 CI 仍须在准确提交上复核。

## 6. 后续进展

对称的同方格批量移除切片已在 [82-batch-reference-removal-undo-audit.md](82-batch-reference-removal-undo-audit.md) 落地；跨方格混合操作与真实桌面文件变更继续关闭。
