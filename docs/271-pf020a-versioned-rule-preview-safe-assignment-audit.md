# Stage 271：PF-020A 版本化规则、真实预览与安全引用分配审计

日期：2026-09-01

输入基线：`origin/main@a006e76`（PF-011B / PR #354 已合入）

状态：`PF-020A EngineeringComplete / RealFilesystemPass / ProductEvidencePending`；下一主开发项为 PF-020B 规则生命周期管理

## 1. 本阶段交付结论

PF-020A 已从“正式规则能力为零”推进到可保存、可解释、可原子应用的首个规则闭环。配置 schema 从 v5 升级到 v6，正式持久化规则名称、启用状态、优先级、目标盒子、All/Any 条件组、条件与安全引用动作；v5 及更早版本迁移为空规则集，旧版本不能夹带 v6 规则。首批条件支持项目类型、扩展名，以及名称包含、前缀、后缀和精确匹配。

正式盒子管理页新增规则草稿编辑器。用户明确填写规则、目标、条件、启用状态和优先级后，预览只消费真实 Desktop Catalog 的名称、类型和身份元数据，展示实际匹配数、冲突数、目标和最多 20 个样本；任一草稿字段变化都会使旧预览失效。应用时再次核对工作区 revision、Catalog generation、工作区/规则/匹配目录三组 SHA-256 指纹，然后通过一个 reducer 编辑同时写入规则与全部新引用，只向统一 SaveController 提交一次，并形成一个统一历史项。

本阶段没有移动、删除、重命名或读取真实文件正文。目标盒子锁定、缺失、规则冲突、零匹配、容量超限、目录不可用和过期预览均有限拒绝；删除规则目标盒子时规则自动禁用，保留为后续修复对象，不让配置整体失效。

## 2. Expected、Initial Actual、Difference、Correction、Final Actual

| 检查 | Expected | Initial Actual | Difference | Correction | Final Actual |
|---|---|---|---|---|---|
| 正式规则模型 | 配置和工作区存在版本化规则，旧配置等价为空规则 | 首个红测 `Assert.NotNull()` 在 `ProductConfigurationDocument.Rules` 失败，`0/1` | schema v5、状态、投影、解析均没有规则 | schema v6、规则/条件/动作模型、validator、projector、resolver 与 v1～v5 迁移 | 模型表面基线由 `0/1` 转为 `1/1`；v5 空迁移与 v6 往返通过 |
| 真实解释性预览 | 使用真实 Unicode Catalog 元数据，显示匹配/冲突/目标，不读正文 | 没有正式规划器 | 用户无法知道规则实际效果 | 新增纯规划器、三组指纹、20 项样本、有限状态和 256 项单次上限 | 中文 `.txt/.png` 真实文件仅 `.txt` 匹配，预览前后 SHA-256 完全一致 |
| 原子安全分配 | 一次确认同时保存规则与引用，不发布部分配置 | reducer 和提交协调器没有规则动作 | 规则与引用可能形成两次编辑或无法应用 | `ApplyAutomationRule` 一次构造候选状态、一次验证、一次 SaveController Submit、一个统一历史项 | 两个真实引用与一条规则同时持久化，重启恢复后仍为 `1 rule / 2 refs` |
| 过期与取消 | 取消、旧 revision、旧 generation、目录内容变化都零提交 | 没有预览令牌或过期门 | 无法证明“预览内容就是提交内容” | 提交前核对 revision、generation、工作区/规则/Catalog 指纹和逐项元数据 | 预览取消及三种过期输入的真实 Save 调用均为 `0` |
| 撤销/目标删除 | 整次应用一次撤销/重做；目标删除留下可修复规则 | 正式规则历史项为零 | PF-010 与规则动作未连接，删除目标语义缺失 | 接入 50 步统一历史；删除目标时原子禁用关联规则 | apply→undo→redo 实际恢复规则和全部引用；删除后规则 `Enabled=false` 且配置可投影 |
| 正式 UI | 用户可编辑草稿、预览并明确应用 | 盒子页没有规则入口 | 核心能力不可达 | 规则 Expander、名称/目标/条件/启用/优先级、样本与 live status 接线 | Release XAML 编译通过；静态 UI 合同从 194 增至 205 IDs |

## 3. 真实测试证据

- 初始失败基线：规则模型表面测试 `0/1`，首个实际失败为 `ProductConfigurationDocument.Rules == null`；未先写实现再伪造红测。
- PF-020A 聚焦真实测试最终 `6/6`：真实 Unicode 临时目录、内容 SHA-256 零变化、零匹配、冲突、目标缺失、预览取消、三类过期拒绝、真实 Store 持久化、全新加载/解析重启、统一历史 Undo/Redo、v5→v6 迁移、旧 schema 防夹带、目标删除自动禁用。
- 完整 Release 测试首轮 `1,475/1,475`，失败 0、跳过 0，用时约 44 秒。
- `LongGrid.App` Release 构建：0 warning / 0 error。
- `eng/Test-LongGridUi.ps1 -ContractOnly -NoBuild`：`outcome=Pass`，205 个必需 AutomationId。
- 真实文件 Expected 是内容与位置不变；Actual 为测试前后两个中文文件 SHA-256 数组完全相等。测试只在随机临时目录创建并清理自有文件，没有使用天翼云电脑或外部机器。

自动合同和本机临时目录测试不能替代 Narrator、物理键盘、200% 文本和窄窗口真人证据；这些保持 `ProductEvidencePending`，PF-020 不能标记产品 Complete。

## 4. 开发目标与需求对齐审计

本阶段直接改善核心工程实现和“定义规则→看清实际匹配→明确应用→可一次撤销”的核心用户旅程，没有把开发精力转向权限或安全扩张。规则动作只建立 Long方格引用，复用真实 Catalog、schema、reducer、SaveController 和 PF-010 统一历史，没有建立第二套配置、保存或撤销系统。

需求对齐结论为 `AlignedWithFunctionFirst / NoDesktopFileMutation`。严格完成度不因本切片自动上调：M1/M2 仍为 `0/2 Complete`，30 项 PF 仍为 `0 Complete`；PF-020A 只能标记工程与真实文件系统通过，PF-020 全项仍 InProgress。

## 5. 唯一接续开发点

下一步进入 **PF-020B：规则生命周期管理与条件/性能收口**：

1. 在正式 UI 列出现有规则，支持编辑、复制、禁用/启用、删除和稳定排序，而不是只创建新规则；
2. 所有规则编辑进入 PF-010 同一 50 步历史，并完成保存失败后的配置/UI 自动补偿；
3. 补齐创建/修改时间范围条件以及 All/Any 多条件编辑，不实现任意嵌套表达式；
4. 为 Disabled/NeedsRepair 规则提供重选目标修复入口；
5. 执行 500 项 × 100 条规则真实元数据性能测试，P95 目标小于 500 ms，并覆盖 0/1/256/500 边界；
6. 完成 PF-020B 审计、文档、提交、推送和 CI 后，再进入 PF-021 的逐项预览审查、取消类别/单项与保存失败全事务补偿。

PF-011、BOX 和 TASKBAR 的外部产品证据继续作为并行门禁，不冻结 PF-020B 功能开发。
