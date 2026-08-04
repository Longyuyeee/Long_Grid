# Issue #21–#22 关闭就绪审计

审计日期：2026-08-04

基线：`main` / `d0cc397`（PR #77 已合入）

结论：**Not closable / Scope decision required / Dedicated evidence pending**

## 1. 审计目的

本审计把 Issue #21 的文件操作安全证据和 Issue #22 的缩略图隔离证据逐项映射到 Phase 0 退出条件。它不执行用户文件移动、不改变真实用户文件 ACL、不安装 codec/provider，也不把自动探针的 `ConditionalPass` 提升为产品 `Pass`。

当前不新增探针深度。按照既定停止规则，只有 CI 回归、安全缺陷、Issue #23 批准的支持范围或已有退出场景失败，才能继续扩展相邻矩阵。

## 2. Issue #21：文件操作安全

| 退出项 | 当前证据 | 判定 | 关闭前动作 |
|---|---|---|---|
| 安全引用零副作用 | Core 计划不生成文件系统动作 | Pass（合同） | 保持首版默认行为 |
| 托管移动必须显式批准 | Core 对未批准、状态缺失和非法计划默认阻断 | Pass（合同） | 若首版禁用托管移动，不得在 UI 中暴露入口 |
| 同卷移动 | 自有临时沙箱中的真实 `IFileOperation` 已完成移动并复读目标 | Conditional Pass | 仅证明受控同卷路径 |
| 冲突预阻断 | 目标冲突时未调用 Shell 操作，源/目标保持不变 | Conditional Pass | 保持默认阻断 |
| 取消与部分成功 | 已观察总体 HRESULT、逐项回调和最终文件状态的差异 | Conditional Pass | 生产实现必须逐项记账和补偿 |
| Explorer 撤销与重启 | 自动探针故意不污染会话级撤销栈 | Pending | 专用账户、自有文件、人工复读 |
| 跨卷复制→校验→删除→补偿 | 未执行 | Pending | 两个可清空测试卷，禁止用户卷 |
| ACL、共享占用、只读卷、磁盘满 | 仅有受控/模拟边界，非完整真实卷矩阵 | Pending | VM 或可还原快照 |
| OneDrive、网络、重解析点、真实取消 | 网络、重解析点、云占位当前以预阻断为主 | Pending | 仅在批准支持范围内用专用账号/共享执行 |

Issue #21 当前不能关闭。Issue #23 若批准首版只支持安全引用，可以把托管移动的专用环境矩阵移出首版退出范围；这属于重新定界，不能把未执行项目改写为 Pass。若首版批准托管移动，则上述 Pending 项必须按支持矩阵完成。

## 3. Issue #22：缩略图隔离与预算

| 退出项 | 当前证据 | 判定 | 关闭前动作 |
|---|---|---|---|
| 非协作调用硬超时 | 250 ms 强制卡死会回收 worker，后续请求使用新进程 | Conditional Pass | 在批准环境复跑 |
| 父进程退出与孤儿回收 | `KILL_ON_JOB_CLOSE`、父退出和 Profile 清理进入 CI | Conditional Pass | 保持回归门禁 |
| 文件保密边界 | Low Integrity 对照证明不可承担保密；真实 worker 使用零 Capability AppContainer | Conditional Pass | 禁止 Low Integrity/主进程现场提取回退 |
| 输入授权 | `ControlledCopy` 为默认，32 MiB/文件、64 MiB/client，拒绝重解析点 | Conditional Pass | 最小路径 ACL 只保留比较用途 |
| 像素 IPC | 匿名共享内存、单请求句柄、BGRA32 与 262,144 bytes 上限 | Conditional Pass | 正式渲染接线在 Phase 1 验收 |
| 协议与恢复 | 超长、畸形、版本错配、意外退出全部阻断并恢复 | Conditional Pass | 保持 CI |
| 500 项预算 | 合成矩阵满足当前临时 CPU、内存、句柄、空闲和时延预算 | Provisional | Issue #23 批准最终预算后，在支持矩阵复跑 |
| Provider/build/architecture | x64 的 22621/26100 有脱敏结果；HEIC/AVIF 成功环境、ARM64 等未覆盖 | Partial | 只补 Issue #23 明确批准的首发组合 |
| 异常最小 ACL lease 修复 | 正常恢复已证实，异常父退出和并发 DACL 修改未证实 | Pending，但非默认路径 | 不得把最小 ACL 提升为产品默认；若重开必须先补修复合同 |
| 正式渲染表面 | 尚未接入生产宿主 | Phase 1 | 不阻断创建只读生产宿主 |

Issue #22 当前不能关闭，因为首发格式、Windows build、架构和最终预算尚未由 Issue #23 批准。批准后只复跑明确组合；未批准的 Office/PDF、第三方 provider、云网络或 ARM64 不得自动成为 Phase 0 必需项。

## 4. 关闭判定与下一动作

### Issue #21

1. Issue #23 明确首版是否仅安全引用；
2. 若禁用托管移动，在 Issue #21 记录重新定界和后续里程碑，不伪造 Pending 结果；
3. 若启用托管移动，按出口手册完成专用环境矩阵并逐项附环境、原始结果、恢复确认和缺陷；
4. 只有批准范围内不存在未解释的 Fail/Inconclusive，才能关闭。

### Issue #22

1. Issue #23 批准首发格式、Windows build、架构和 500 项预算；
2. 在每个批准组合复跑现有 worker 矩阵；
3. 明确接受 `ControlledCopy`、类型图标/已验证缓存回退，以及禁止特权现场提取；
4. 把正式渲染接线保留为 Phase 1 首片验收；
5. 只有批准矩阵全部通过且安全负责人接受边界，才能关闭。

本阶段最有效的下一动作不是继续加代码，而是完成 Issue #23 的范围和预算决策。#21、#22 保持 OPEN，现有 CI 探针继续作为回归门禁。

## 5. 本轮复核

在 `d0cc397` 基线上重新执行：

- Release 构建：0 警告、0 错误；
- Core 测试：111/111；
- 文件操作安全探针：`ConditionalPass`，撤销边界继续为 `Inconclusive`，清理成功；
- 缩略图 worker：`ConditionalPass`，500 个请求在当前机器均被安全分类并在临时预算内完成，p95 31.77 ms；零 Capability AppContainer、Job、Profile/ACL 清理和禁止未代理读取均通过；
- 当前机器 worker 的 500 个请求没有成功提取像素，而是安全返回访问拒绝并要求产品回退；这不能写成 500/500 提取成功，也不能证明支持任意真实 provider。

该复核只刷新自动化回归证据，不改变 `Not closable` 判定。

## 6. PR #78 CI 回归与修复审计

PR #78 首轮 Windows CI 在“Thumbnail worker isolation probe”失败。报告中父进程正常退出、Job 配置有效、孤儿 Worker 已退出，其他正常回收路径的 Profile 也全部删除；唯一失败项是异常父退出场景的 `ParentExit.AppContainerProfileDeleted=false`。失败运行：[30870164681](https://github.com/Longyuyeee/Long_Grid/actions/runs/30870164681)。

根因是主探针确认孤儿进程退出后立即且仅调用一次 `DeleteAppContainerProfile`。Windows 已报告进程退出并不保证相关 Profile 句柄在同一时刻完成释放，因此该单次调用存在很小的清理竞态。这不是隔离边界被绕过，也不能通过删除判定项解决。

修复保持以下边界：

- 仅允许删除名称严格满足 `LongGridThumbnailWorker` + 32 位 GUID 的探针自有 Profile；
- 最多尝试 20 次，每次间隔 50 ms，重试间隔累计最多 950 ms；
- 首次成功立即结束，不改变正常路径时延；
- 所有尝试失败时仍返回失败，不把超时或“可能已删除”伪装为成功；
- 报告新增尝试次数与最终 HRESULT，便于区分瞬时句柄释放和持续清理故障。

修复后本机连续三轮执行与 CI 相同的完整 Worker Matrix：每轮 500 个压力请求、父进程退出、孤儿回收、Profile 删除、沙箱清理和临时预算均通过，三轮 Profile 都在第 1 次调用删除成功并返回 `0x00000000`，总体判定均为 `ConditionalPass`。完整 Release 构建为 0 警告、0 错误，Core 测试为 111/111。

远端 CI 全绿前，本节只能证明本地回归和修复边界；Issue #22 继续保持 OPEN，`Not closable` 判定不变。
