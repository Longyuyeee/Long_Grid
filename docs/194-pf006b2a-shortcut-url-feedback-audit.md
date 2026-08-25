# Stage 194：PF-006B2A Shortcut/URL 与有限反馈审计

- 日期：2026-08-24
- 分支：`codex/pf002d-create-preview`
- 起始基线：`ef1ba49`
- 对齐编号：`PF-006B2A / PF-006B2 / PF-006`
- 结论：PF-006 `InProgress`；PF-006B2A 工程切片完成

## 1. 开始审计与开发边界

Stage 193 后，File/Folder 已能通过 Enter、项目双击和 UIA Invoke 共用的权威命令打开，但 Shortcut/URL 仍固定返回 `ReviewRequiredKind`，打开结果也只从 App 压缩为 `bool`，真实 DesktopHost 项目和 UIA 客户端看不到拒绝原因。这与 PF-006 对 `.lnk/.url` 安全打开和有限错误反馈的要求仍有差距。

本轮不把路径下放到 HWND/UIA，也不直接把 `.lnk/.url` 文件交给系统关联。App 权威控制器先复核配置/Catalog/现场引用，再由专用解析器读取目标；只有解析状态为 `LaunchAccepted` 才把解析后的目标与有界参数交给既有 `ShellExecuteExW`。本切片交付有限文本反馈；“在 Explorer 中定位”按钮和独立重试动作留到 B2B，不能据此把 PF-006 标记完成。

## 2. 实现与安全对齐

### 2.1 `.lnk` Shell Link

- 引用必须是存在的普通 `.lnk` 文件、非 ReparsePoint、大小为 1 byte～1 MiB；
- 使用 Windows `IShellLinkW + IPersistFile` 只读加载真实 Shell Link，不调用可能弹 UI 的 `Resolve`；
- 目标必须为绝对、现场存在、非 ReparsePoint 的 File/Folder；拒绝嵌套 `.lnk/.url`；
- 参数用 32768 字符缓存完整读取，再限制为最多 4096 字符并拒绝控制字符；Folder 不允许携带参数；
- 解析前后复核引用长度、最后写入时间、ReparsePoint 和 SHA-256，变化时有限拒绝，避免把竞态后的内容当作已审计内容。

### 2.2 `.url` InternetShortcut

- 引用必须是普通 `.url`，大小为 1 byte～64 KiB；读取后再次检查真实 byte 长度；
- 只接受严格 UTF-8（有/无 BOM）或 UTF-16LE BOM，最多 128 行、唯一 `[InternetShortcut]`/`URL=`、URL 最多 8192 字符；
- 只允许 `http`/`https`，要求非空 host，并拒绝 user-info；`file:`、`mailto:`、自定义协议和畸形 URI 均零启动；
- 系统接收的是解析后的规范 URL，不是原 `.url` 路径，因此未知字段不能改变启动目标。

### 2.3 有限、无路径反馈

`ProductDesktopItemOpenResult` 把陈旧、缺失、类型变化、重解析点、引用过大、格式/编码错误、协议拒绝、Shortcut 目标缺失/不安全和 Shell 失败映射为有限中文消息。生命周期把结果送回对应 display Surface；真实 HWND 项目标签显示消息，UIA `ItemStatus` 发布相同消息和属性变化事件，且不包含原路径、解析目标或参数。Enter 本身可再次尝试；专用“定位/重试”控件仍 Pending。

## 3. Expected / Actual / Difference 与修正

| 场景 | Expected | Actual | Difference / 修正 |
| --- | --- | --- | --- |
| Stage 193 Shortcut/URL | 专用解析后安全打开 | 固定 `ReviewRequiredKind` | 有；本轮接入解析器 |
| 首轮 `.lnk` 参数边界 | 超过 4096 必须拒绝，不能截断执行 | 4098 字符 COM 缓存存在截断后被接受风险 | 有；改为 32768 完整读取后按 4096 拒绝 |
| 解析期间引用变化 | 变化后不得发布旧审计结果 | 初稿仅解析前检查 | 有；增加长度/时间/ReparsePoint 解析后复核 |
| 真实 Windows `.lnk` | COM 得到真实 `where.exe` 和 `cmd.exe` 参数 | 完全一致 | None |
| 真实 `.lnk` → Shell | 解析目标/参数进入真实 `ShellExecuteExW`，获得 PID | Shell 接受，真实 PID > 0，进程有限退出 | None |
| 真实 UTF-16LE `.url` | HTTPS 规范 URL，零浏览器测试副作用 | 真实文件解析一致；记录启动目标，不启动浏览器 | None |
| 真实 `file:` `.url` | `ProtocolRejected`、零 launch | 完全一致 | None |
| 真实畸形 `.url` | `ReferenceMalformed`、零 launch | 完全一致 | None |
| 真实 64 KiB+ `.url` | `ReferenceTooLarge`、解析前零 launch | 完全一致 | None |
| 真实 HWND/UIA 失败反馈 | 显示有限协议错误，不含路径 | `ItemStatus` 命中消息且不含 `file:` | None |
| 生命周期反馈 | 与打开请求同 container/item/status | 完全一致 | None |

真实 `.lnk` 由本机 Windows Script Host 创建，再由生产解析器通过另一套 Shell Link COM 接口读取；真实 Shell 测试启动 `%WINDIR%\System32\where.exe cmd.exe` 并获得实际 PID。`.url` 使用真实临时文件和严格编码解析。为避免测试擅自弹出用户默认浏览器，URL 的最终 launcher 使用记录适配器；真实 `ShellExecuteExW` 已由 File 和 `.lnk` 两条链分别验证。

## 4. 最终门禁

- Release 全量：1155/1155，0 failed，0 skipped；
- Release 全解决方案：0 warning、0 error；
- 真实 `.lnk` 解析与真实 Shell：`Difference=None`；
- 真实 UTF-16LE `.url`、协议拒绝、畸形和超限：`Difference=None`；
- 真实 DesktopHost HWND/UIA 有限反馈：`Difference=None`；
- UI 合同：157 AutomationId，PowerShell 7 `ContractOnly`，`Outcome=Pass`；
- 格式与 `git diff --check`：通过；
- 零桌面文件移动/删除/重命名，零配置写入，零路径/参数进入 HWND/UIA 文本。

## 5. 需求对齐与下一步

PF-006B2A 已关闭 Shortcut/URL 的核心解析与有限反馈差距，但 PF-006 继续 `InProgress`。下一切片固定为 **PF-006B2B：打开失败操作与可配置单击策略**：提供权威重新验证后的“重试”，仅在安全父目录存在时提供 Explorer 定位，并完成默认双击/显式单击设置；同时补参数超限、引用解析竞态和真实可见像素证据。

其后仍有 PageUp/PageDown 跨视口、框选、高对比/Narrator 和物理双击/Enter 证据。PF-001～PF-005 的产品证据、签名安装和分发门禁仍 Pending，不能提前进入公开发布。
