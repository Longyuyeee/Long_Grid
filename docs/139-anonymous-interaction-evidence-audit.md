# Stage 139：按需匿名交互证据审计

审计日期：2026-08-14

## 1. 判定

M3d 判定为 **Engineering Pass / Manual Evidence Pending**，M3 工程链闭合。正式 App 现在可以在用户明确确认后，把当前 DesktopHost lifecycle 的最小匿名摘要冻结为单条 JSON 证据；该证据进入现有配置证据库的有限清单，继续复用逐条导出、变更复核和单条确认清理。

这不是日志系统：没有后台计时器、事件订阅写盘、自动轮转或退出时隐式保存。取消确认不会创建目录或文件；每次确认最多创建一条独立快照。

## 2. 白名单格式与隐私边界

序列化合同固定为 11 个字段：

1. `schemaVersion`；
2. `hostStatus`；
3. `lifecycleGeneration`；
4. `workspaceRevision`；
5. `topologyGeneration`；
6. `explicitInteractionActive`；
7. `selectedItemCount`；
8. `focusedItemAvailable`；
9. `selectionRevision`；
10. `anonymous=true`；
11. `realFileOperationsAllowed=false`。

合同明确拒绝 container/item ID、方格/项目名称、路径、文件内容、按键、坐标、输入来源、窗口句柄、进程信息和配置正文。选中数量固定在有限上限内；选中或焦点存在时必须处于 Explicit。Schema、匿名标志或文件操作标志被改写时拒绝捕获。

## 3. 存储、导出与清理复用

| 边界 | 行为 | 判定 |
| --- | --- | --- |
| 捕获授权 | 默认按钮为取消；只有 Primary 确认进入一次性捕获 | 通过 |
| 原子发布 | 写租约内创建唯一 `.new`，落盘刷新、SHA-256 复核后原子发布 | 通过 |
| 精确归属 | 只接受 `interaction-evidence.<32 hex>.snapshot.json`，不枚举内容 | 通过 |
| 有限清单 | 复用最多 256 条呈现、4096 目录项扫描、容量饱和和重解析点拒绝 | 通过 |
| 匿名呈现 | UI 仅显示来源、角色、大小和归档时间，不接收 SourcePath | 通过 |
| 导出 | 明确单选与二次确认后复制为唯一 `.json`，SHA-256 验证，原件保持 | 通过 |
| 清理 | 明确单选与永久清理确认后，在写租约内复核大小/时间并只删除一条 | 通过 |
| 取消/竞争 | 未确认、文件夹取消、取消令牌、租约竞争或条目变化均有限失败 | 通过 |

## 4. 自动化与审计结果

- Release build 为 0 warning / 0 error；匿名证据/配置导出专项 35/35；第一次完整本地测试 920/920；
- 独立覆盖率复跑为 919/920；唯一失败仍是 Stage 137/138 已记录的 `NativeActivationSourceExposesFiniteInvokeAndHideRestoreContract`，测试进程未取得 Windows 前台许可后安全返回 `ElementNotEnabledException`，单项复跑仍复现；没有放宽 NoActivate/前台合同，也没有把失败改写为通过；
- 该复跑覆盖率 line 90.55%（25994/28708）、branch 79.05%（8392/10616），门禁通过；
- 144 项 UI automation 源码合同、clean-session 与 batch-accessibility `-ValidateOnly` 入口、依赖漏洞门、启动链、100 次配置持久化/故障注入和临时沙箱文件操作安全探针通过；
- 自动化覆盖精确 11 字段、篡改匿名状态拒绝、未确认零写入、写租约竞争、现有清单识别、JSON 导出、原件保持和单条清理；
- 实现 PR #195 run `31785742249` 为 920/920，line 90.23%（25902/28708）、branch 79.05%（8392/10616），完整 34 步通过；本机前台许可异常未在独立 runner 复现；
- squash 合并为 `main@43b40243cd686298d655eb925ef2f851390f99c1`；合并后 main run `31786827386` 为 920/920，line 90.20%（25896/28708）、branch 79.01%（8388/10616），完整流水线再次通过；
- 真实 Narrator、高对比、文本缩放、物理输入和动态 Explorer/系统表面证据继续 Pending，不得用源码合同冒充。

## 5. M3 收口与下一步

M3a～M3d 的工程链已经覆盖目录—投影修订、匿名选择观察、保存/恢复组合和匿名证据生命周期。该结论不等于 M4-ready，也不解除 Phase 0 的人工/专用环境证据门。

下一步按独立切片进入：

1. 500 项规模下的目录、解析、投影与交互自动压力矩阵；
2. 配置/目录/Explorer/显示器故障恢复矩阵；
3. DesktopHost Surface、UIA provider、目录观察器与缩略图工作器资源长稳预检；
4. 自动预检通过后再审计 M4-ready，不提前宣称 RC 或公开分发。

## 6. 远端轨迹

- 实现 PR / merge SHA：PR #195 / `43b40243cd686298d655eb925ef2f851390f99c1`；
- PR CI / main CI：`31785742249` / `31786827386`，均成功；
- 当前结论：M3 Engineering Pass / Manual Evidence Pending；M4-ready、Phase 0、内部 RC 与公开分发均未完成。
