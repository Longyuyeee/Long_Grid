# Issue #24 正式产品配置合同审计

审计日期：2026-08-04（正式 Infrastructure 存储增量复审）

基线：`main` / `c34820f` + 正式产品配置存储分支

结论：**Core schema and first Infrastructure store ready / App lifecycle and real-volume evidence pending / 不得关闭 Issue #24**

## 1. 需求对齐

原始合同阶段把架构文档中的配置示例升级为 `LongGrid.Core.Configuration` 正式 v1 合同，只覆盖首个只读产品切片稳定需要的容器、外观、DIP 布局和项目引用。2026-08-04 增量已创建 `LongGrid.Infrastructure` 并读写测试沙箱中的正式配置，但仍不接 App 生命周期或用户真实配置目录。

当前 `behavior` 只允许 `reference`。这把已批准的“首版仅安全引用”落实为 schema 不变量；托管移动、原生图标位置或自动规则执行已移出首发范围，未来必须通过后续 schema 版本和迁移步骤进入，不能悄悄复用未知枚举值。

## 2. 正式 v1 边界

| 领域 | 合同 |
|---|---|
| 根 | `schemaVersion=1`、有限 profile ID、最多 100 个容器 |
| 容器 | 全局唯一稳定 ID、名称、锁定、外观、DIP placement、项目列表 |
| 外观 | `#RRGGBB`、0–1 opacity、collapsed |
| 布局 | 脱敏 display key、有限坐标、64×48 至 16384 DIP 尺寸 |
| 项目 | file/folder/shortcut/url、有限 target、仅 reference |
| 总预算 | 最多 500 个项目、UTF-8 JSON 最多 4 MiB、深度最多 32 |
| 兼容 | 根、容器、外观、布局和项目级未知字段往返保留 |
| 错误 | 仅有限 `ProductConfigurationError`，不包含路径、target、JSON 或异常原文 |

## 3. 迁移与回滚判定

v1 是 Long方格第一个正式生产 schema，仓库没有需要迁移的历史生产配置，因此本阶段不伪造 v0→v1 数据。合同规则为：

- 只接受当前 v1；无效、v0 和未来版本拒绝；
- 数字枚举、未知枚举、重复 ID、NaN/Infinity 和超预算数据拒绝；
- 输入文档不会在验证或序列化期间被修改；
- 未来 v2 必须新增显式相邻迁移，并继续使用 P0-06 已验证的深拷贝、确定性、失败不发布和旧版本备份回退合同；
- schema 回滚不是就地降级。存储层应保留上一个有效版本，由加载状态机选择备份或安全模式。

## 4. 自动证据

Core 测试覆盖：合法往返、camelCase 枚举、五层未知字段保留、未来/无效 schema、跨层重复 ID、外观/布局边界、500 项预算、4 MiB 预解析门禁、畸形/不完整 JSON、数字枚举和非引用行为拒绝。

这些测试证明纯合同；正式存储增量见第 8 节。它们自身仍不证明关闭排空、单实例或真实卷耐久性。

## 5. 尚未完成

- `LongGrid.Infrastructure` 首个原子存储适配器已经建立，但尚未接入 App 生命周期；
- `.new`/flush/校验/replace/backup、安全模式与写租约已有正式自动证据；强杀、权限和故障注入矩阵仍由独立 probe 承担；
- 真实卷空间耗尽、只读卷、断电与非 NTFS 环境仍 Pending；
- 应用关闭排空、单实例激活、导入/导出和恢复 UI 留在后续生产切片；
- v2 迁移必须等真实新增字段出现后实现，不以 probe 的 `persistenceProbe` 假字段代替；
- D23 已把托管移动、规则执行和未验证支持范围移出首发；这些能力不进入 v1。

## 6. 专用环境会话入口

`eng/Start-Issue24PersistenceBoundarySession.ps1` 为 I24-01 真实容量/配额耗尽和 I24-02 只读独立卷建立统一入口。真实会话必须使用匿名 O1–O9、确认专用环境与恢复计划，并指向带固定标记的非工作区独立卷根目录。

启动器只读取标记并输出脱敏合同；它不写卷、不填盘、不改变卷状态或 ACL、不运行配置探针、不截图、不写结果文件。CI 只调用 `-ValidateOnly`，固定输出 `PendingDedicatedEnvironmentEvidence`。因此入口就绪不提升 P0-06 的 `Conditional Pass`，也不产生真实卷结果。执行纪律见[专用环境运行手册](manual-testing/issue-24-persistence-boundary-runbook.md)，安全判定见[专用环境就绪审计](31-issue-24-dedicated-environment-readiness-audit.md)。

## 7. 下一动作

首个 `LongGrid.Infrastructure` 适配器及 latest-wins/App 有界关闭排空已建立；下一步是单实例激活、恢复 UI 与真实产品状态的显式入队。专用环境真实卷矩阵可独立执行，全部证据链完成后才能关闭 Issue #24。正式程序集继续不得引用或改名发布 spike 程序集。

## 8. 2026-08-04 正式存储增量

`LongGrid.Infrastructure.Configuration.ProductConfigurationStore` 已成为首个正式产品存储适配器。它没有引用 Spike 程序集，而是直接调用 `ProductConfigurationJson`，因此写入前、暂存复读和加载后的文档都服从同一 v1 资源预算与有限错误合同。

当前自动证据覆盖：首次原子发布、第二次替换保留上一版备份、损坏主文件回退备份、主备均不可用时进入安全模式、普通保存拒绝覆盖损坏证据、跨进程文件租约有界超时与调用方取消、I/O 错误不在公开异常中泄露路径，以及 latest-wins/关闭排空合同。该增量允许产品代码依赖正式 Infrastructure 边界，但不改变 I24-01/I24-02 的 `PendingDedicatedEnvironmentEvidence`，也不代表单实例激活、恢复 UI、真实断电或非 NTFS 耐久性已经完成。
