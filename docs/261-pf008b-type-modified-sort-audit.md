# Stage 261：PF-008B 类型与修改时间稳定排序审计

日期：2026-09-01

输入基线：`origin/main@e206a72`（PF-008A / PR #344 已全绿合入）

状态：`PF008B EngineeringComplete / RealFilesystemPass / ProductEvidencePending`

## 1. 本阶段交付结论

PF-008B 已在现有每方格 `FolderBinding.SortMode` 正式链上追加四种有限模式：类型升序、类型降序、修改时间最新优先、修改时间最旧优先。控制中心“内容排序”现在提供七种选择；新模式复用既有 edit revision、锁定、只读、原子保存、失败补偿和最近一次编辑撤销，不新增旁路事务。

类型排序使用直属条目的扩展名作为文件类型键，大小写不敏感比较后用 ordinal 和名称稳定打破平局；修改时间排序读取真实条目的 `LastWriteTimeUtc.Ticks`，相同时间戳以名称升序稳定打破平局。四种新模式都明确保持文件夹优先，排序只改变投影顺序，不修改文件、文件夹、绑定身份或配置 schema。

schema 继续为 v5：本阶段只是向已有字符串枚举追加有限值，旧 v1～v5 配置仍按既有默认/迁移读取；新选择可序列化、重新加载和撤销，无需制造一次没有结构变化的 schema v6。

## 2. 预期、初始实际、差异、修正、最终实际

| 检查 | Expected | Initial Actual | Difference | Correction | Final Actual |
|---|---|---|---|---|---|
| 类型升序 | `甲文件夹, b-图片.png, a-报告.txt, c-笔记.txt` | `a-报告.txt, b-图片.png, c-笔记.txt, 甲文件夹` | 读取器只有名称/粗 Kind，实际仍为名称升序 | 采集扩展名类型键；文件夹优先，类型与名称稳定 tie-break | 与 Expected 完全一致 |
| 类型降序 | 文件夹优先，`.txt` 组后按名称稳定，再到 `.png` | 无对应模式 | 枚举/UI/读取器均缺失 | 新增 `TypeDescending` | `甲文件夹, a-报告.txt, c-笔记.txt, b-图片.png` |
| 最新优先 | `甲文件夹, b-最新.txt, c-同刻.txt, a-最旧.txt` | `a-最旧.txt, b-最新.txt, c-同刻.txt, 甲文件夹` | 未读取修改时间，仍按名称 | 读取真实 UTC ticks；降序后名称稳定 tie-break | 与 Expected 完全一致 |
| 最旧优先 | 文件夹优先，最旧文件在前，同刻名称稳定 | 无对应模式 | 枚举/UI/读取器均缺失 | 新增 `ModifiedOldestFirst` | `甲文件夹, a-最旧.txt, b-最新.txt, c-同刻.txt` |
| 配置和撤销 | 新模式按方格保存、重载并可一次撤销 | 只支持旧三种名称模式 | 用户选择无法持久化 | 复用现有 FolderBinding commit/undo | `TypeDescending` 提交/撤销、`ModifiedNewestFirst` round-trip 通过 |

## 3. 真实测试结果

- 初始真实差异测试：2/2 精确失败，xUnit 同时打印上述 Expected 与 Initial Actual。
- 修正后真实配置/提交/绑定/目录读取专项：`78/78`。
- 完整解决方案测试：`1,411/1,411`，0 failed，0 skipped。
- Debug 构建：`0 warning / 0 error`；格式验证通过。
- UI 工程合同已要求类型、修改时间选项和真实 `LastWriteUtcTicks` 读取；正式 UI 预检越过合同后仍按已知环境边界返回 `Live cross-process UIA was blocked before application launch`。

真实目录包含 Unicode 文件夹、`.png`、多个 `.txt`、不同 UTC 修改时间和相同时间戳项目。测试前后重新枚举路径并计算逐文件 SHA-256；数量、相对路径、内容哈希和显式设置的文件 UTC 时间均不变化。

## 4. 开发目标与需求对齐审计

开发目标审计：PF-008B 要求的类型与修改时间排序、文件夹优先、稳定次序、每方格保存和重启保持均已实现；没有提前实现 PF-008C 自定义拖序，也没有增加无关权限或安全基础设施。

需求对齐审计：本阶段直接扩大核心用户旅程和对标功能广度；真实目录是读取输入，不发生写入。既有锁定、只读、保存补偿和一次撤销边界继续生效，符合“功能优先、零惊吓”。

完成度审计：PF-008B 为 `EngineeringComplete / RealFilesystemPass / ProductEvidencePending`；PF-008 整体仍为 `InProgress`，30 项 PF 仍为 `0 Complete`，M1/M2 仍为 `0/2 Complete`。跨进程物理点击、截图、键盘和 Narrator 证据仍待兼容 Runtime/可丢弃会话，不得用工程合同冒充产品完成。

## 5. 唯一接续开发点

下一步只进入 **PF-008C：自定义顺序、保存补偿和一次撤销**：

1. 复读正式引用 reducer、容器间重排/改归属和最近一次撤销链，冻结“不移动真实文件”基线；
2. 明确自定义顺序只适用于 Long方格持久化引用，绑定文件夹的临时直属内容继续使用有限排序，不把外部目录顺序写回磁盘；
3. 建立预览、原子保存、失败补偿和一次撤销的 Expected / Initial Actual / Difference；
4. 使用真实 Unicode 文件引用验证保存前后路径、数量和 SHA-256 不变，并验证重启重载顺序；
5. 结束时更新执行计划、backlog、路线图和阶段审计，推送同一独立切片。

PF-008C 完成前不并行展开 PF-009 或新的安全邻接工作。
