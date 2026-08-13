# Stage 127：C5a 正式产品存储真实卷会话宿主审计

- 日期：2026-08-13
- 基线：`main@2ea1f6c`（PR #176；main CI 31711092422 通过）
- 阶段：Stage 125 外部门禁等待期的 C5a 安全准备
- 结论：**正式存储宿主已实现 / I24-01、I24-02 仍 PendingDedicatedEnvironmentEvidence**

## 1. 需求与顺序对齐

C1 五人测试和 C2 人工矩阵正在等待外部环境，C3 依赖 C2，不能提前收口。Stage 125 允许等待期推进不依赖外部结论的安全校验、自动化合同和证据准备。本切片只解决 C5a 的已知工程缺口：旧 launcher 不写目标卷、也不运行正式产品存储，无法产生 I24-01/I24-02 所需证据。

本切片不执行真实容量耗尽或只读卷，不改变 C1/C2/C3 状态，不关闭 Issue #24，也不准入断电、非 NTFS、网络/云盘或企业重定向目录。

## 2. 正式产品链路

- 新增 `ProductConfigurationPersistenceBoundarySession`，直接构造正式 `ProductConfigurationStore`；
- 新增薄控制台宿主 `LongGrid.Tools.PersistenceBoundarySession`，只负责解析阶段并输出有限 JSON；
- launcher 继续验证专用卷根、固定 marker、不同于系统卷/工作区卷、非 UNC 且不是子目录；
- 所有产品写入固定落在 `LongGrid-Issue24-ProductStore-Session` 子目录；
- 旧 `LongGrid.Spikes.ConfigurationPersistence` 不被引用或执行。

## 3. 三阶段状态机

### PrepareBaseline

只允许空会话目录。正式 store 连续保存两个确定性 v2 配置，形成主版本 B 和备份 A；随后复读正式合同并核对两个 SHA-256。

### AttemptFailure

先要求主/备份仍精确匹配 B/A，再尝试保存约 3 MiB、500 个匿名引用的合法 v2 候选 C：

- 捕获正式 store 的 `IoFailure` 后，必须复读 B，且主/备份 SHA-256 仍为 B/A，才输出 `ExpectedFailureObserved`；
- 若写入成功，输出 `UnexpectedSaveSuccess` 并以非零退出，人工结果必须为 Fail；
- 其他状态、损坏、租约问题或指纹漂移均拒绝继续。

### RecoverAndRetry

操作者恢复容量/可写状态后，宿主再次要求 B/A 基线，保存候选 C，再确认正式加载 C 且备份为 B，输出 `RecoverySucceeded`。

阶段必须独立调用。任何代码、配置或基线变化都不能把不同 commit/目录的结果拼接为一个会话。

## 4. 安全与隐私

- launcher/宿主不填盘、不改变只读状态、不修改 ACL、不挂载/卸载卷；
- 目标条件只能由可恢复 VM、VHD、配额或专用测试存储设施建立；
- Prepare 拒绝覆盖非空会话目录；
- 输出只包含阶段、有限 outcome/error、加载状态、SHA-256 和暂存物布尔值；
- 不输出盘符、路径、卷标/GUID、机器/用户身份、配置 JSON 或引用内容；
- 候选只含固定匿名值，不枚举桌面或读取任何真实文件；
- `-ValidateOnly` 不创建目录、不写目标卷、不运行宿主。

## 5. 自动验收

新增测试验证：

- 基线产生确定且不同的主/备份指纹；
- Prepare 拒绝覆盖已有数据；
- 在可写卷误跑失败阶段会得到 `UnexpectedSaveSuccess`，不能伪装为故障证据；
- 恢复阶段发布候选，并把失败前主版本变为备份。

CI 继续执行 Issue #24 `-ValidateOnly`，验证 live 合同明确 `writesTargetVolume=true`、`fillsTargetVolume=false`、`changesVolumeState=false`、`runsPersistenceProbe=false`、`usesFormalProductConfigurationStore=true`。

## 6. 剩余门禁

- 在专用 VM/测试卷分别执行 I24-01 和 I24-02 的三个阶段；
- 复核首次失败、主/备份指纹、暂存物、租约释放和恢复结果；
- 将匿名证据和缺陷写回 Issue #24；
- 只有 C1/C2 完成并按顺序进入 C3/C4/C5b 后，才能评审真实卷结果与 Issue #24 关闭资格。

当前只能判定 **C5a 工程实现 Pass / C5b 人工证据 Pending**。
