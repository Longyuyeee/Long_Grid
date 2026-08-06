# Long方格引用编辑正式保存提交审计

日期：2026-08-06

基线：`main` / `914fc20`（PR #102 已合入）+ 引用编辑提交增量分支

证据等级：E2-E3 / explicit product configuration edit submission

结论：**首条正式产品编辑提交已接通 / 单调 edit revision / 内存基线与 Catalog 刷新一致 / controller-owned save / 桌面文件零修改 / Issue #24 保持 OPEN**

## 1. 本轮准入范围

上一切片只允许未解析引用的匿名 Dry-run。本轮只晋升两种已经过 Core gate 的配置编辑：

- `Replace`：用户明确选择当前权威 Catalog 中的匿名候选后，将未解析引用重新绑定为 resolved；
- `Remove`：用户在二次确认后，只从 Long方格配置中移除引用。

`Keep` 仍是默认安全行为，不生成 edit、不推进修订、不进入保存队列。容器 CRUD、布局修改、真实拖放、自动整理和桌面文件操作没有随本轮开放。

## 2. 提交协调器

Infrastructure 新增 `ProductWorkspaceReferenceCommitCoordinator`，固定执行顺序为：

1. 使用当前 Catalog generation、当前 edit revision、对象状态、锁定状态及候选唯一性执行 `ProductWorkspaceReferenceGate`；
2. 对 gate 产生的 reducer 深快照再次执行正式 v1 projector；
3. 只调用一次 App-owned `ProductWorkspaceSaveController.Submit`；
4. 只有 controller 返回 `Accepted` 后才推进单调 edit revision，并向 App 返回状态与独立 Document。

Gate、projection 或 controller 任一拒绝均不推进 revision，也不更新 App 会话。revision 的溢出检查发生在 Submit 之前，避免“已接受保存但无法发布新 revision”的半提交状态。

外部配置加载、备份接受、SafeMode 重置或导入复读都会调用 `AdvanceExternalRevision`。因此在系统文件对话框或确认对话框打开前捕获的旧 token，不能在配置被外部替换后因 revision 重置而重新变成有效。

## 3. App 内存一致性

保存被 controller 接受后，App 立即执行三项内存更新：

- 用返回的正式 v1 Document 替换 `currentConfigurationLoadResult` 的产品基线；
- 用同一 Document 与当前权威 Catalog 重建 `ProductWorkspaceSessionSnapshot`；
- 用新 edit revision 重建匿名引用审查快照。

这样即使 400 ms 防抖期间发生 Catalog 刷新，会话也从新配置基线重新解析，不会回退到提交前的旧磁盘快照。若后台保存失败，保存卡进入有限 Failed/Retry 状态，窗口关闭仍由现有 controller 排空/失败阻断合同保护。

## 4. 配置事务与 UI

引用卡将“预演重选/移除”改为“重选并保存/移除并保存”。确认文本明确说明：

- Long方格配置引用会改变；
- 更改进入安全保存队列；
- 原桌面文件不会被删除、移动、重命名或打开。

UIA 结果使用 `ReferenceCommit` 有限状态，同时分别暴露 `ConfigurationChanged` 与固定 `DesktopFilesChanged=False`，不含路径、名称、内部 ID 或异常文本。

当产品保存状态为 Waiting/Saving/Failed 时，配置导入和导出按钮保持禁用；只有 Clean/Saved 才重新开放，避免待保存引用编辑与显式配置事务交叉覆盖。证据清单等只读能力不受影响。

## 5. 自动证据

- 提交协调器定向测试 8/8：外部 revision 单调推进、Keep 零提交、确认移除单次提交、明确重选、门禁拒绝、旧 token 失效、controller 完成后拒绝，以及真实临时 Store 保存/重载；
- 真实 Store 测试在临时目录保存含真实测试文件引用的配置，执行引用移除并排空 controller，重载后配置引用消失，但原测试文件内容与存在性保持不变；
- 引用 gate/review + commit 定向测试合计 22/22；
- UI 源码合同保持 93 个 AutomationId，并新增唯一 Submit、内存基线重建、外部 revision、导入/导出互斥及桌面文件零修改断言；
- Debug/Release 全解决方案构建均为 0 warning / 0 error；全量测试 322/322，覆盖率 lines 91.35%（7916/8666）、branches 82.13%（1912/2328），高于 90%/75% 门禁；
- 启动、单实例、Issue #19/#20/#23/#24 安全会话链和依赖漏洞门禁全部通过；人工和专用环境结果继续保持 Pending，不由自动预检替代；PR/main CI 结果在推送后复核。

## 6. 仍未完成与下一步

本轮是第一条真实产品配置编辑，不是桌面管理完整闭环。仍缺：

- 干净 Windows 会话中的 93-ID UIA、Narrator、文本缩放与确认对话框证据；
- 正式产品容器可视化与 CRUD、布局编辑、跨动作撤销和活动记录；
- Shell 虚拟项、真实拖放、DesktopHost 产品接线；
- Issue #24 真实卷矩阵、完整进程终止/恢复与跨进程配置事务公平性；
- 安装包、升级/卸载和许可证。

下一切片优先建立只读的正式容器/引用视图模型与 UI 映射，复用当前 session 作为唯一来源；待匿名、锁定、无障碍和性能合同稳定后，再逐项开放容器编辑。桌面文件操作继续保持关闭。

当前 Windows 会话仍可见既有的无窗口残留单实例，真实 UIA 继续记为 Inconclusive；本轮不终止无权限进程，也不把源码合同 Pass 冒充真实窗口 Pass。
