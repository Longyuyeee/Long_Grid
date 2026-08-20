# Stage 169：WinUI UIA 已知崩溃运行时失败关闭审计

- 审计日期：2026-08-20
- 开发基线：`codex/pf002d-create-preview@a979189`
- 对应目标：PF-002 正式交互证据安全门禁
- 结论：**真实外部界面尝试再次复现上游 fail-fast；UIA 测试现已在启动 App 前失败关闭并输出结构化差异，避免重复崩溃产品进程。PF-002 仍为 `EngineeringComplete / ProductEvidencePending`**

## 1. 本轮真实尝试

本轮按非 UIA 截图/坐标路径启动正式 Release App，计划验证创建入口、Preview、取消、确认和撤销。Windows 应用控制器成功唯一枚举标题为“Long方格”的窗口；但在取得首张截图前，窗口句柄已经失效。按安全规则刷新一次后，窗口不再存在，因此停止复用旧句柄和坐标。

同一时刻的 Windows 事件不是普通退出：

- Application Error：`Microsoft.UI.Xaml.dll 3.2.3.0`；
- 异常：`0xc000027b`；
- WER P7：`8001010e`；
- 故障进程路径：本分支正式 Release `LongGrid.App.exe`；
- `%LOCALAPPDATA%\LongGrid` 仍不存在，说明本次尝试没有创建或修改产品配置。

这说明当前 Windows 控制器即使请求“截图、不返回 accessibility text”，其窗口发现/捕获链仍会触达 WinUI 跨进程查询路径。不能继续用该控制器完成本机坐标矩阵，也不能把“窗口曾被枚举”记为交互通过。

## 2. 测试系统修正

新增 `eng/Test-LongGridWinUiUiaRuntime.ps1`：

1. 只选择 x64 `Microsoft.WindowsAppRuntime.2`；
2. 按包版本降序选择当前运行时；
3. 读取 `Microsoft.UI.Xaml.dll` 的标准 `FileVersionRaw`；
4. 对当前已实机复现的 `2.4.0.0 + 3.2.3.0` 组合返回 `BlockedByKnownUpstream`；
5. 输出 Expected、Actual、Difference 和 Outcome；
6. 不启动 LongGrid.App，不查询 UIA，不修改配置。

`eng/Test-LongGridUi.ps1` 的 live 模式现在先执行该门禁。命中已知组合时默认在 App 启动前抛出有限错误。只有在一次性诊断环境明确传入 `-AcknowledgeKnownUiaCrashRisk` 才允许继续；`-ContractOnly` 不受影响。

这个门禁不是永久版本黑名单。它只封锁已经由本机 WER 和上游问题共同验证的精确组合；未来稳定运行时发生变化后，live UIA 会重新执行并以真实结果判定。

## 3. 预期、实际、差异与修正

| 检查项 | 预期 | 首次实际 | 修正后实际 |
| --- | --- | --- | --- |
| 非 UIA 控制器截图 | 枚举后取得真实截图 | 枚举成功，截图前 App fail-fast | 判定控制器路径不安全，停止重试 |
| 产品配置边界 | 失败尝试零配置写入 | `%LOCALAPPDATA%\LongGrid` 不存在 | 无差异 |
| DLL 版本解析 | 得到四段版本 | `FileVersion` 含构建标签，无法转为 `System.Version` | 使用 `FileVersionRaw`，得到 `3.2.3.0` |
| 运行时门禁 | 已知组合在 App 启动前阻断 | 首次脚本解析失败 | 返回 `BlockedByKnownUpstream` |
| live UIA 默认行为 | 不再崩溃产品进程 | 旧实现先启动再崩溃 | 新实现退出 1，错误中包含 `2.4.0.0 / 3.2.3.0` |
| App 启动/配置写入 | 门禁测试均为零 | 修正后未启动 App，配置目录仍不存在 | 无差异 |
| 控制器停止后的首次窗口冒烟 | 10 秒内发布窗口 | 未发布窗口且没有新 WER 崩溃，测试清理自启进程 | 保留失败记录；确认零残留后复核 5 秒通过，最终 20 秒通过 |

结构化门禁实际结果：

```json
{
  "expected": {
    "discoverableRuntime": true,
    "knownUnsafePairAbsent": true
  },
  "actual": {
    "discoverableRuntime": true,
    "runtimePackageVersion": "2.4.0.0",
    "xamlFileVersion": "3.2.3.0",
    "knownUnsafePairAbsent": false
  },
  "difference": "KnownUnsafeCrossProcessUiaRuntimePairPresent",
  "outcome": "BlockedByKnownUpstream"
}
```

## 4. 需求对齐

| 需求 | 当前状态 |
| --- | --- |
| 使用真实测试 | 正式 Release 外部控制再次真实复现，WER 与实际配置边界均复读 |
| 记录预期/实际差异 | 控制器、版本解析和失败关闭均逐项记录 |
| 根据差异修正 | 修正版本读取，并把 live UIA 改成启动前失败关闭 |
| 不伪造交互结果 | 创建、Preview、取消、确认、撤销仍为 Pending |
| 不修改桌面文件/用户配置 | 本轮配置目录未创建，零桌面文件操作 |
| 稳定和速度 | 已知有害测试不再浪费一次启动和一次 WER 崩溃 |

## 5. 下一步

当前机器不能再使用跨进程 UIA 或现有 Windows 控制器取得 PF-002 操作证据。下一切片应建立**产品进程内、UI 线程执行、默认关闭、只写专用临时配置目录**的 PF-002 evidence session：由真实 App 加载真实 XAML，调用正式按钮命令/Preview/保存/撤销链，输出有限 Expected/Actual/Difference JSON。它不能替代物理鼠标、Narrator 或 UIA，但可以补齐“正式 App 接线是否真实执行”的证据；物理输入与无障碍仍在上游修复或独立人工机器上收口。

## 6. 提交前验证

| 门禁 | 预期 | 实际 | 结论 |
| --- | --- | --- | --- |
| 运行时预检 | 识别已知组合 | `2.4.0.0 / 3.2.3.0`，`BlockedByKnownUpstream` | Pass |
| live UIA 默认失败关闭 | App 启动前退出 1 | 有限错误包含运行时/DLL 版本，零新产品进程 | Pass |
| 静态 UI 合同 | 147 IDs | 147 IDs | Pass |
| 真实窗口生命周期复核 | 10 秒内发布、响应 20 秒、正常退出 | 3,487 ms、响应 20 秒、退出码 0 | Pass |
| Release 全量测试 | 零失败 | 1,010/1,010，0 跳过 | Pass |
| Release App 构建 | 零 warning/error | 0 warning、0 error | Pass |
| `dotnet format --verify-no-changes` | 无格式差异 | 无格式差异 | Pass |
| `git diff --check` | 无补丁错误 | 无补丁错误 | Pass |
| PF-002 点击/Preview/撤销 | 完成真实操作矩阵 | 控制器触发上游崩溃，未执行 | Pending |
