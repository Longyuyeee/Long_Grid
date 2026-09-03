# Stage 272：PF-020B1 规则生命周期核心事务审计

日期：2026-09-03

输入基线：`origin/main@3daf110`（PF-020A / PR #355 已合入）

状态：`PF-020B1 CoreTransactionComplete / UIAndFailureCompensationInProgress`；下一接续点为规则列表 UI 与保存失败补偿

## 1. 本步交付结论

PF-020B1 首步已为已保存规则建立正式生命周期事务：编辑、复制、启用/停用、删除、上移和下移均通过 `ProductWorkspaceReducer` 生成完整候选配置，由统一提交协调器核对 edit revision、投影、只提交一次 SaveController，并分别记录为 PF-010 同一 50 步历史中的规则编辑、复制、启停、删除或排序动作。成功事务只增加一个 edit revision，可由统一历史一次 Undo/Redo。

复制规则强制以禁用状态创建，避免复制立即产生未确认的自动分配；启用规则时重新检查目标存在且未锁定。缺失目标的禁用规则仍可保留并在后续 UI 中修复。排序边界、旧 revision、未知规则、不安全启用、重复 ID、无效模型和容量超限均失败关闭且不提交保存。

本步没有读取、移动、重命名或删除真实文件，也没有建立第二套保存或历史系统。

## 2. Expected / Actual / Difference / Correction

| 检查 | Expected | Initial Actual | Correction | Final Actual |
|---|---|---|---|---|
| 生命周期模型 | 已保存规则可被有限动作修改 | 仅有“新建并应用规则”入口 | 新增六类生命周期动作及强类型请求 | reducer 覆盖编辑、复制、启停、删除和双向排序 |
| 安全边界 | 复制不自动生效，启用需有效目标 | 没有既有规则操作 | 复制仅接受 disabled；启用复查目标与锁定 | 不安全启用返回有限错误，Save 调用为 0 |
| 原子保存 | 每个动作一次配置提交、一次 revision | 规则生命周期没有 coordinator | 接入统一投影、SaveController 与 edit revision | 成功动作 `revision + 1`，一次提交 |
| 统一撤销 | 生命周期进入 PF-010 的同一历史 | 历史只有 RuleApplication | 增加五类规则历史语义 | 编辑一次 Undo/Redo 可恢复前后完整规则状态 |

## 3. 自动审计证据

- PF-020 规则专项：`9/9`，失败 0、跳过 0；覆盖真实预览/应用旧用例及新增生命周期 reducer、提交、历史和失败边界。
- 完整 Release：`1,478/1,478`，失败 0、跳过 0，用时约 43 秒。
- `dotnet format --verify-no-changes`：通过。
- 本机使用 `C:\Program Files\dotnet\dotnet.exe` 8.0.423，符合 `global.json` 的 8.0.400 feature band / latestPatch 策略；系统 PATH 中的 x86 host 无 SDK，因此未作为审计执行源。

本步没有新增 UI 元素，205-ID UI 合同不应变化。规则列表可达性、Narrator、物理键盘和保存真实 I/O 失败后的 UI/配置自动补偿仍未在本步声明完成。

## 4. 路线对齐与接续点

实现仍严格对齐原始 PF-020：生命周期操作只修改 Long方格配置和安全引用元数据，复用 schema v6、统一 reducer、SaveController 与 PF-010 历史；没有偏移到文件整理、任意脚本或权限扩张。

下一步继续 PF-020B1：在正式 UI 列出现有规则，编辑名称/目标/优先级时完整保留现有 MatchMode、条件和扩展数据；接通复制、启停、删除和排序；为生命周期保存失败增加可信 token、配置/历史/UI 自动补偿及真实 Store fail-once 证据。完成并合入后再进入 PF-020B2 的 All/Any 多条件编辑、创建/修改时间条件和 NeedsRepair 修复。
