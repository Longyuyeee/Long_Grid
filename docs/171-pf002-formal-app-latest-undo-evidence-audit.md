# Stage 171：PF-002 正式 App 最近撤销证据审计

- 审计日期：2026-08-21
- 开发分支：`codex/pf002d-create-preview`
- 对应目标：关闭 PF-002“最近撤销的正式 App 工程证据”缺口
- 结论：**正式 Release App 的创建—删除—统一最近撤销—恢复保存与重载证据连续两次通过；可见交互、物理输入与无障碍证据仍受已知上游缺陷阻断**

## 1. 目标与真实边界

普通方格创建不会签发统一最近撤销令牌；只有删除方格、布局恢复或有限引用事务等受审计操作才有对应令牌。因此本轮没有把普通创建伪装成“可撤销创建”，而是在 Stage 170 已有正式 Preview 链后执行：

1. 取消 Preview，确认内存仍为 0 且磁盘为 `Missing`；
2. 确认创建“PF-002 证据方格”，等待保存控制器权威修订 1 进入 `Saved`，从正式 store 重载为 1；
3. 使用与 `MainWindow` 相同的 App 容器提交委托删除方格，等待修订 2 进入 `Saved`，重载为 0；
4. 生成主窗口正式 `ProductWorkspaceLatestUndoPresentation`，只允许唯一 `ContainerRemoval` 令牌；
5. 调用与按钮 Click 共享的统一分派函数，执行正式 App 恢复委托；
6. 通过 `CompleteAsync` 排空恢复保存，从正式 store 重载为 1 且名称不变；
7. 外部脚本逐字段复核 App JSON，并再次复核桌面、用户配置、退出码和临时目录清理。

证据仍使用 `%TEMP%\LongGridEvidence\<guid>` 专用配置、独立 AppInstance key 和正式 Release App。普通启动、用户配置目录和桌面文件不受影响。

## 2. 实现审计

`MainWindow` 的真实按钮处理器与证据入口现在共享 `ExecuteProductWorkspaceLatestUndo`。证据入口不自行解释令牌，也不直接调用 Core reducer；它只允许现有 presentation 判定后的正式分派。App 仍通过 `CommitProductWorkspaceContainerAction` 删除、通过 `CommitProductWorkspaceContainerRemovalUndo` 恢复，并由同一个 save controller 持久化。

保存等待不再猜测工作区编辑修订。证据在每次提交后读取 `productWorkspaceSaves.Snapshot.CurrentRevision`，仅当状态为 `Saved` 且 `SavedRevision` 达到该权威目标才继续；`Failed` 立即失败，10 秒未达到目标也明确失败。最终 `CompleteAsync` 仍负责停止接收并排空已接受保存。

外部 PowerShell 脚本不再只信任 App 的顶层 `Outcome=Pass`，而是对 29 个实际字段逐项检查，并输出 `FailedAppEvidenceChecks`。中文期望名称通过 Unicode 码点构造，避免 Windows PowerShell 5.1 对无 BOM UTF-8 源码的解码差异。失败 JSON 只保留有限阶段、异常类型和证据自有错误说明，其他异常详情统一脱敏。

## 3. 预期—实际—差异与修正

| 检查项 | 预期 | 最终实际 | 差异 |
| --- | --- | --- | --- |
| 初始/取消 | `0/Missing` | `0/Missing` | 无 |
| 创建落盘 | `1/LoadedPrimary` | `1/LoadedPrimary`，保存修订 1 | 无 |
| 正式删除 | Accepted，`0/LoadedPrimary` | Accepted，`0/LoadedPrimary`，保存修订 2 | 无 |
| 最近撤销选择 | 唯一 `ContainerRemoval` | `ContainerRemoval` | 无 |
| 最近撤销执行 | 共享主窗口正式分派 | `ContainerRemoval` | 无 |
| 恢复落盘 | `1/LoadedPrimary` 且名称不变 | `1/LoadedPrimary / PF-002 证据方格`，保存修订 3 | 无 |
| 外部逐项合同 | 全部 true | `Matched=true / Failed=[]` | 无 |
| 桌面/用户配置 | 不变 | 不变 | 无 |
| 临时目录/退出 | 删除 / 0 | 删除 / 0 | 无 |

开发中的真实失败及修正：

- 首轮等待错误地要求保存修订 2，实际首次保存修订为 1。原因是把工作区编辑修订与保存控制器修订混为一体；改为提交后读取权威保存修订。
- 首次外部逐项复核的应用事实全部正确，但名称检查失败。原因是 Windows PowerShell 5.1 解码脚本内中文常量不一致；改用 Unicode 码点构造同一精确字符串。
- 外部复核最初只有一个合并布尔值，无法定位失败项；改为命名检查集合并保留失败键，随后连续两次通过。
- 正式窗口生命周期脚本也含有源码中文标题常量；Windows PowerShell 5.1 将预期解码为乱码而真实窗口标题为正确的“Long方格”。同样改为 Unicode 码点构造后，两轮 20 秒真实窗口测试通过。

这些修正没有降低产品断言或把 Pending 改写成 Pass。

## 4. 需求对齐

| 需求 | 状态 |
| --- | --- |
| 正式 App、真实 XAML、UI 线程 | Pass |
| 正式创建/删除/撤销委托 | Pass |
| 统一最近撤销选择与按钮共享分派 | Pass |
| 每一步真实保存与正式 store 重载 | Pass |
| 外部独立逐字段复核 | Pass |
| 零桌面文件和用户配置副作用 | Pass |
| 可见按钮点击与确认后动态视图 | `BlockedByKnownUpstream` |
| 物理鼠标/键盘/触控 | `PendingManualEvidence` |
| UIA/Narrator | `BlockedByKnownUpstream` |

最近撤销正式 App 工程证据已关闭，不再列为 PF-002 未执行项。但 PF-002 仍不能标记 `Complete`：当前精确 WinUI 组合下窗口继续隐藏，`PreviewActivatedCount=0`，且可见交互与视图发布继续诚实记录为 `BlockedByKnownUpstream`。

## 5. 验证命令

```powershell
dotnet build src/LongGrid.App/LongGrid.App.csproj `
  --configuration Release --runtime win-x64 --no-restore

.\eng\Test-LongGridPf002AppEvidence.ps1 -NoBuild
.\eng\Test-LongGridPf002AppEvidence.ps1 -NoBuild
.\eng\Test-LongGridUi.ps1 -ContractOnly
```

提交前实际结果：

- Release 全量测试：`1010/1010`；
- Release solution build：`0 warning / 0 error`；
- PF-002 正式 App 证据：连续两轮 Pass，外部逐项合同 `Matched=true / Failed=[]`；
- 153-ID 静态 UI 合同：Pass；
- 正式窗口生命周期：连续两轮 20 秒 Pass，窗口就绪 1,247/1,175 ms，退出码均为 0，未查询跨进程 UIA；
- 完整跨进程 UIA：启动前按已审计不安全运行时组合阻断，状态保持 `BlockedByKnownUpstream`，未强行执行；
- `dotnet format --verify-no-changes` 与 `git diff --check`：Pass。

最终提交 SHA 与远端分支在推送回执中记录。

## 6. 下一方向

安全的下一阶段不是在当前已知不安全运行时强行调用 UIA，而是：

1. 在包含上游修复的 Windows App SDK/Runtime 或独立无有害客户端机器上重跑可见 Preview 与确认后视图发布；
2. 使用真实鼠标、键盘和触控完成打开—编辑—取消—确认及拖画矩阵；
3. 完成 UIA、Narrator、高对比、文本缩放和多 DPI 证据；
4. 上述 PF-002F 产品证据关闭后，再按 Stage 153 进入 PF-003 拖动、缩放与吸附。
