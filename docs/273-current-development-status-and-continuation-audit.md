# Stage 273：当前开发状态与唯一接续点审计

日期：2026-09-03

权威代码基线：`origin/main@a9e378cf3751ea0b9656759f6af05b50826e6e4f`

审计结论：`FunctionFirst / RouteAligned / PF-020B InProgress`；当前唯一接续点为 PF-020B1 规则列表 UI 与生命周期保存失败自动补偿

## 1. 仓库与远端事实

- 本地 `main`、`origin/main` 和 `HEAD` 均为 `a9e378c`，工作树在审计开始时干净，没有未推送代码。
- PR [#356](https://github.com/Longyuyeee/Long_Grid/pull/356) 已于 2026-09-03 合并，合并提交为 `a9e378c`；当前没有 Open PR。
- 合并后主线 CI [33762322411](https://github.com/Longyuyeee/Long_Grid/actions/runs/33762322411) 为 `success`：Release 测试 `1,478/1,478`，失败 0、跳过 0、32 秒；覆盖率 lines `90.16% (51,246/56,836)`、branches `75.38% (16,732/22,198)`。
- 同次 CI 的格式、构建、启动链、会话合同、205-ID UI 合同、真实进程测试、500 项预检、恢复/资源/文件安全、缩略图隔离、漏洞和内部 unsigned RC 审计全部通过。测试与覆盖产物 ID 为 `9896299110`，大小 `1,084,573` bytes。
- 合并后 CodeQL [33762322320](https://github.com/Longyuyeee/Long_Grid/actions/runs/33762322320) 的 C# 与 C/C++ 两个任务均为 `success`。
- 依赖漏洞为 0；许可证清单为 20 个项目、30 个包，但仍是 `PendingOwnerReviewAndNotice`，`distributionApproved=false`。Issue [#23](https://github.com/Longyuyeee/Long_Grid/issues/23) 与 [#274](https://github.com/Longyuyeee/Long_Grid/issues/274) 仍为 Open，最后更新时间均停留在 2026-08-28。

## 2. 最近实际完成了什么

PF-020A 已完成 schema v6 规则模型、v5 空迁移、真实 Desktop Catalog 元数据预览、三组指纹过期门、一次原子安全引用分配和统一历史项。PF-020B1 的核心事务随后由 PR #356 合入：已保存规则现在具备编辑、禁用复制、启停、删除和稳定排序 reducer；所有成功动作共用 edit revision、SaveController 和 PF-010 的 50 步历史，旧 revision、排序边界、不安全启用、重复 ID 和容量超限均失败关闭。

这一阶段只修改 Long方格配置和安全引用元数据，不读取文件正文，不移动、重命名或删除真实桌面文件。它直接对应最初 PF-020 的“保存、复制、禁用、删除、排序可撤销”要求，没有偏离到权限扩张、任意脚本或新的保存/撤销体系。

## 3. 当前未完成边界

PF-020B1 还不能标记完成：正式 UI 仍只有新规则草稿/预览/应用入口，没有既有规则列表，也没有编辑、复制、启停、删除和排序按钮；生命周期提交虽已进入异步 SaveController，但真实保存失败后的配置、历史和 UI 自动补偿尚未接通。现有单条件草稿界面也不能安全编辑导入的多条件规则，否则会丢失条件，因此在 B2 前只能编辑名称、目标和优先级并完整保留 MatchMode、Conditions 与扩展数据。

PF-020 后续固定为三个有界切片：

1. PF-020B1 UI 与补偿：规则列表、基本属性编辑、复制/启停/删除/排序、可信补偿 token、真实 Store fail-once 与 UI 回退证据；
2. PF-020B2 条件与修复：All/Any 多条件编辑、创建/修改时间范围、Disabled/NeedsRepair 重选目标；
3. PF-020B3 性能与收口：500 项×100 条规则 P95 小于 500 ms，覆盖 0/1/256/500 边界及 Narrator/键盘合同。

PF-020 完成后才进入 PF-021 的逐项预览审查、类别/单项取消、冲突解释与整批保存失败补偿。

## 4. 全项目完成度

严格产品口径没有变化：M1/M2 仍为 `0/2 Complete`，30 个 PF 仍为 `0 Complete`，不能把工程测试通过等同于完整产品验收。

按工程状态拆分 30 个 PF：

- 11 项（PF-001～PF-011）为 `EngineeringComplete / ProductEvidencePending`；
- 2 项（PF-020、PF-040 高级 Portal 部分）处于 InProgress；
- 7 项（PF-021、PF-030、PF-051、PF-052、PF-070、PF-071、PF-090）只有原型或工程底座；
- 10 项（PF-022、PF-031、PF-032、PF-033、PF-041、PF-042、PF-050、PF-060、PF-080、PF-081）尚未实现正式产品能力。

因此，近期 PF-020 距工程收口还剩上述 3 个切片；整个产品则仍有 19/30 个 PF 未达到 EngineeringComplete，另有 11/30 个即使工程完成也仍缺物理键鼠、Narrator、DPI、真人旅程或外部环境证据。BOX-R1-C/D、TASKBAR-R2B1-B、签名/许可证和 24 小时稳定性继续作为并行门禁，不应抢占 PF-020 的安全功能开发队列。

## 5. 换机或下一轮接续

从 GitHub 最新 `main` 拉取并确认 `HEAD == a9e378c` 后，只进入 PF-020B1 UI 与保存失败补偿。不要合并本机历史分支 `codex/stage259-current-development-handoff`；该分支停留在旧提交 `0b86b21`，已被后续主线取代。开始编码前应再次读取 `ProductAutomationRule.cs`、`ProductWorkspaceReducer.cs`、`ProductWorkspaceCommitCoordinator.AutomationRule.cs`、`App.xaml.cs` 和 `MainWindow.xaml(.cs)`，并保持每个切片“测试—审计文档—PR CI/CodeQL—合并后 main 复验”的顺序。
