# P0-08 文件操作安全与 IFileOperation 边界

状态：**Conditional Pass**

日期：2026-08-01

关联：Issue #21

## 1. 目标

验证“安全引用”和“托管目录移动”在代码层不可混淆，并在 Long Grid 自建临时沙箱内确认 Shell `IFileOperation` 的成功、冲突、取消和部分成功边界。探针不得读取或修改真实桌面，不得输出文件名、完整路径、内容或稳定文件身份。

## 2. 已建立的合同

Core 新增纯 `FileOrganizationPlanner`：

- `SafeReference` 只产生 `AddReference`，`HasFileSystemMutations` 永远为 false；
- `ManagedMove` 只在源可用、属于文件系统、目标已配置且没有冲突时形成候选移动；
- 网络路径、重解析点、云占位、同源同目标、目标已存在和缺失状态默认阻断；
- 任一项目不安全时整批不能进入批准态，不把部分成功伪装成完整成功；
- 无冲突的真实移动仍返回 `RequiresExplicitApproval`，不能作为无需批准的自动动作；
- Core 只携带匿名项目 ID 和预验证事实，Shell 路径与 COM 对象留在边界适配器。

## 3. 受控环境

本轮自动证据：

| 项目 | 值 |
|---|---|
| 系统 | Windows 10.0.22621 x64 |
| 运行时 | .NET 8 / STA |
| 文件范围 | `%TEMP%` 下新建的随机专用目录 |
| Shell API | `IFileOperation.MoveItem` + `PerformOperations` |
| UI | `FOF_SILENT`、`FOF_NOCONFIRMATION`、`FOF_NOERRORUI` |
| 错误策略 | `FOFX_EARLYFAILURE` |
| 撤销注册 | 明确关闭，避免污染用户 Explorer 会话级撤销栈 |
| 输出 | 仅场景状态、计数、HRESULT、OS 和架构 |

运行命令：

```powershell
dotnet run --project probes/LongGrid.Spikes.FileOperationSafety --configuration Release -- --json
```

## 4. 结果

| 场景 | 结果 | 证据 |
|---|---|---|
| 安全引用 | Pass | 规划器不产生文件系统动作，源文件内容不变 |
| 托管移动 | Pass | 明确要求批准；源消失、目标内容复读一致、未中止 |
| 同名冲突 | Pass | 计划阶段报告冲突，未调用 Shell，源和目标均不变 |
| 回调取消 | Pass | `PreMoveItem` 返回取消；源保留、目标不存在 |
| 部分成功 | Pass | 第一项完成，第二项在回调取消后保留；报告 1 完成、1 保留 |
| 沙箱清理 | Pass | 全部场景结束后专用临时目录删除成功 |
| Explorer 撤销 | Inconclusive | 自动化不写入用户会话级撤销栈；需专用交互会话验证 |

本机 JSON 结论为 `ConditionalPass`。Core 测试从 82 项增加到 88 项，覆盖引用零副作用、不可用源、合法移动必须批准、全部阻断原因、批量阻断和非法计划形状。

## 5. 关键发现

### 5.1 取消不能只看 GetAnyOperationsAborted

进度回调返回 `ERROR_CANCELLED` 时，本机 `PerformOperations` 返回 `0x800704C7`，但 `GetAnyOperationsAborted` 返回 false；`PostMoveItem` 仍收到失败回调。微软文档同时说明用户取消也可能让 `PerformOperations` 返回成功，因此生产适配器必须把以下信号合并，而不能只检查其中一个：

- `PerformOperations` HRESULT；
- `GetAnyOperationsAborted`；
- 每项 `Post*` 回调 HRESULT；
- 操作后的源/目标身份复读。

### 5.2 批量调用不是原子事务

两项移动中，第一项可以已经完成，第二项才取消。生产实现必须逐项 journal 完成状态，把结果明确分为完整成功、完整取消和部分成功；重试只能针对复读后仍未完成的项目。

### 5.3 撤销是产品合同，不是一个布尔标志

`FOFX_ADDUNDORECORD`/`FOF_ALLOWUNDO` 保存的是 Explorer 用户会话级撤销信息，`IFileOperation` 没有确定性的“执行刚才这条撤销”API。自动探针若注册撤销会污染用户现有撤销历史，因此本轮主动关闭。专用测试账户后续必须验证 Explorer 重启、会话切换、跨卷移动、部分成功和用户再次修改文件后的撤销可见性。

## 6. 当前决策

- 安全引用可以继续作为首个 MVP 的默认且唯一自动语义。
- 托管目录移动仍不得进入首版默认路径；本轮只证明受控同卷移动和取消边界可观测。
- 生产 `IFileOperationService` 必须执行 Plan → Revalidate → Confirm → Execute → Observe → Journal，不得直接暴露探针程序集。
- 日志只记录领域操作 ID、匿名项目 ID、动作、HRESULT 分类和完成状态；路径只在本地交互计划中短时显示，不进入默认诊断导出。

## 7. 尚未关闭

- 专用账户中的 Explorer UI 撤销与会话/Explorer 重启边界；
- 跨卷移动的复制、校验、删除与补偿；
- ACL、只读卷、共享占用、磁盘满和执行期间源被替换；
- OneDrive/其他云占位、网络路径、重解析点和符号链接的受控矩阵；
- 用户从 Shell 进度 UI 取消，而不是探针回调取消；
- 真实拖放数据对象、长路径、大批量和性能；
- 正式 Infrastructure 适配器、操作 journal、重验证令牌和产品确认 UI。

在这些矩阵关闭前，P0-08 保持 Conditional Pass，Issue #21 不应关闭。

## 8. 官方依据

- [IFileOperation](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifileoperation)
- [IFileOperation::PerformOperations](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-performoperations)
- [IFileOperation::SetOperationFlags](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags)
- [IFileOperationProgressSink](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifileoperationprogresssink)
