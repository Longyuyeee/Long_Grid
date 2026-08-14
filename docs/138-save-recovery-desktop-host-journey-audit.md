# Stage 138：保存/恢复与 DesktopHost 组合旅程审计

审计日期：2026-08-14

## 1. 判定

M3c 判定为 **Engineering Pass / Manual Evidence Pending**。审计发现此前保存失败会在工作流中保留可显式重试的配置快照；配置恢复或导入随后加载新的权威基线时，App 虽推进外部工作区修订，却没有撤销该旧快照。用户若继续点击旧“重试”，存在用恢复前配置覆盖新基线的风险。

本切片增加一个窄范围的外部基线替换事务：它只在保存控制器已经进入有限 `Failed` 状态时生效，同时清除工作流捕获文档、失败原因和 UI 重试能力。`Clean`、等待防抖、正在保存和已保存状态不会被这个入口中断。配置加载仍由既有 Store/SessionLoader 负责，没有建立第二套恢复或持久化系统。

## 2. 组合旅程与需求对齐

| 旅程 | 权威行为 | 判定 |
| --- | --- | --- |
| 保存失败 | 既有保存状态机产生有限错误及显式重试能力，不自动重放 | 通过 |
| 显式重试 | 未发生外部基线替换时，继续重放工作流捕获的不可变快照 | 通过 |
| 配置恢复/导入后加载 | App 在应用新加载结果前撤销旧失败重试；随后推进既有外部工作区修订并重建会话 | 通过 |
| 旧重试失效 | 工作流和控制器同时清空旧意图；后续 `Retry()` 返回 `NotAvailable`，不会写入恢复前文档 | 通过 |
| 非失败保存状态 | 外部基线入口不打断 Clean/Waiting/Saving/Saved；并发保存语义没有被伪装成成功 | 通过 |
| 目录失败/取消 | 含引用配置在目录不可用时由既有 SessionLoader 进入 `AwaitingCatalog`，生成无可显示状态的投影，DesktopHost 释放 Surface | 通过（复审既有链） |
| 目录恢复 | 新的权威目录 generation 先推进外部修订，再重建会话/投影；DesktopHost 仅在有效投影与安全门均成立时恢复 | 通过（复审既有链） |

## 3. 实现边界

- `ProductConfigurationSaveWorkflow.DiscardRetry()` 递增尝试代次并清空捕获文档，使迟到结果不能重新建立旧重试；
- `ProductWorkspaceSaveStateMachine.ExternalBaselineReplaced()` 只接受有限失败态并回到无重试的 Clean 基线；
- `ProductWorkspaceSaveController.DiscardFailedRetryForExternalBaseline()` 在同一控制器锁内完成工作流与呈现状态收敛并发布快照；
- `App.ApplyProductConfigurationLoadResult()` 在应用恢复/导入后的新会话之前调用该入口；
- 目录状态、工作区解析、投影构建与 DesktopHost lifecycle 均复用 M3a 既有单调修订链，没有增加旁路协调器。

本切片不枚举、读取、写入或移动桌面文件，不把配置文档、容器/项目 ID、名称、路径或内容写入观察状态，也不改变任务栏、窗口定制或插件权限。

## 4. 自动化与审计结果

- Release solution build：0 warning / 0 error；
- 保存状态机、真实保存工作流、控制器、SessionLoader 与 DesktopHost lifecycle 专项：86/86；
- 新增回归覆盖真实工作流捕获文档清理、控制器双层清理、旧重试不可用，以及非失败状态不被外部基线入口改变；
- 本地完整测试为 914/915；唯一失败仍是 Stage 137 已记录的 `NativeActivationSourceExposesFiniteInvokeAndHideRestoreContract`，测试进程未获得 Windows 前台许可后按安全合同返回 `ElementNotEnabledException`；独立重跑仍复现，没有放宽前台/NoActivate 合同，也没有把失败改写为通过；
- 失败运行仍产生覆盖率：line 90.64%（148302/163618），branch 79.41%（47591/59928），覆盖率门禁通过；
- 格式与 diff 检查、依赖漏洞门、143 项 UI automation 合同、启动链、100 次配置持久化及故障注入矩阵、临时沙箱文件操作安全探针通过；
- PR Windows runner、合并后 main runner 与最终文档闭环证据将在远端运行完成后回填；
- 真实配置损坏/恢复、目录取消、Explorer 重启与动态系统表面仍属于专用人工证据，不得用单元测试冒充。

## 5. 剩余 M3 顺序

1. M3d：最小匿名交互证据导出与确认清理，复用既有证据库，不记录项目身份；
2. M3 工程闭合后进入 500 项规模、故障恢复和资源长稳自动预检，推进 M4-ready；
3. ReadyForExternalValidation 后停止功能扩展，按 X1～X5 汇合不可伪造的外部证据。

## 6. 远端轨迹

- 实现 PR / merge SHA：等待本切片远端运行；
- PR CI / main CI：等待本切片远端运行；
- 当前结论：M3c Engineering Pass / Manual Evidence Pending。M3、M4-ready、Phase 0、内部 RC 和公开分发均未因本切片完成。
