# Stage 105：保存控制器测试工作流准入确定性审计

日期：2026-08-12

基线：PR #154 / `f660756`

触发证据：PR CI run `31555761634` 中 636 项测试有 1 项失败；失败用例为 `OlderSaveCompletionCannotOverwriteNewerWaitingEdit`，期望最终状态 `Saved`，实际为 `Failed`。还原、格式化、构建和包括 Stage 104 在内的全部源码合同已先行通过。

结论：**产品保存准入逻辑没有回退；测试在“状态已进入 Saving”和“工作流 SaveAsync 已实际进入”之间缺少确定性同步。测试已改为等待工作流调用事实，不依赖线程池调度速度。**

## 1. 根因

`ProductWorkspaceSaveController` 在防抖结束后先发布 `Saving`，随后通过可替换调度器让出执行权，再做最新修订准入并调用 `SaveAsync`。这是有意保留的生产异步边界。

原测试按以下顺序运行：

1. 等待状态变为 `Saving`；
2. 立即提交第二个修订；
3. 完成名为 `firstSave` 的任务源。

但步骤 1 不保证第一次 `SaveAsync` 已经调用。若线程池在状态发布后暂未恢复第一次保存，第二个修订可能先取得最新准入，并成为测试替身收到的第一次工作流调用，从而被错误绑定到 `firstSave` 的失败结果。该失败验证的是测试调度偶然性，而不是“旧完成不得覆盖新编辑”的产品合同。

## 2. 修复边界

测试工作流新增只读、线程安全的 `SaveCalls` 计数和有界 `WaitForSaveCallCountAsync` 等待：

- `OlderSaveCompletionCannotOverwriteNewerWaitingEdit` 在提交第二修订前确认第一次工作流调用已进入；
- `CloseTimeoutDoesNotCancelAcceptedSaveAndReopensSubmissions` 在取消关闭令牌前执行相同确认，消除同类窗口；
- 等待仍限定为 200 次、每次 5 ms；超时后硬失败；
- 不修改产品控制器、状态机、工作流、持久化语义或异常映射；
- 不重跑失败 CI 来替代修复，不跳过测试，也不放宽断言和覆盖率门禁。

## 3. 自动化证据

- 保存控制器定向测试：22/22 通过；
- 两个受影响场景重复 10 轮：20/20 通过；
- `git diff --check` 和完整 Release/覆盖率门禁在提交前继续执行；
- 最终接受条件仍为 PR #154 CI 与合并后 `main` CI 均通过。

## 4. 需求与阶段对齐

本修复直接支撑桌面方格创建、重命名、布局、外观和引用编辑的连续保存可靠性，符合“稳定、快速、零惊吓”和每一步可审计的要求。它不新增 UI、文件移动、DesktopHost 窗口执行、任务栏修改、插件权限或发行权限。

Stage 103 的后续开发方向不变。PR/main 门禁关闭后，下一产品切片仍是 A1：App 组合根持有 DesktopHost 生命周期、有限状态桥与默认关闭的开发 Feature Flag。
