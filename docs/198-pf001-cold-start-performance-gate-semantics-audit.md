# Stage 198：PF-001 冷启动性能门禁与验收语义纠偏审计

日期：2026-08-25  
开发项：Gate A / PF-001 性能证据纠偏  
结论：**Engineering Pass / Runtime Enable Evidence Pending**

## 1. 本切片解决的问题

Stage 197 发现 `Test-LongGridDesktopFirstStartup.ps1` 只记录 ready 时间，不按产品预算失败；但当时又把该脚本的“独立进程冷启动”直接与 PF-001 的“运行中关闭后重新开启方格 1 秒”验收项比较。两者不是同一场景：

- 冷启动包含进程创建、Windows App Runtime、WinUI/XAML、配置、桌面目录、显示拓扑与 DesktopHost 初始化；
- 运行期开启发生在 App 已驻留、依赖已初始化的会话内，只测 `BoxesEnabled=false → true` 后产品 Surface 恢复。

本切片不修改需求目标，也不拿更宽松阈值替代 1 秒目标；它把两个预算明确拆开，并让当前真实冷启动场景具备可失败的 Expected / Actual / Difference 门禁。

## 2. 实现变化

`eng/Test-LongGridDesktopFirstStartup.ps1` 现在：

1. 新增 `-MaximumColdProcessReadyMilliseconds`，默认 10,000 ms，对应产品“10 秒恢复到可工作状态”的上限；
2. 将窗口探测周期由 100 ms 缩短为 25 ms，降低测量自身最多约 75 ms 的离散误差；
3. 在 JSON 中分别公开：
   - `ColdProcessDesktopHostReadyBudgetMilliseconds`；
   - `RuntimeBoxesEnableBudgetMilliseconds = 1000`；
   - `RuntimeBoxesEnableMeasuredByThisScenario = false`；
   - 冷启动实际值与带符号差值；
4. 超出冷启动预算时输出有限 Difference、`Outcome=Fail` 并返回非零退出码；
5. `Test-LongGridSingleInstance.ps1` 验证上述语义字段，防止后续再次把未测量场景当作 Pass。

该变化不修改桌面文件、用户正式配置、显示设置或输入状态。证据会话仍使用系统临时目录、独立 AppInstance key、真实 Release 进程、真实 Win32 顶层窗口和正常关闭排空。

## 3. 真实测试：产品冷启动预算

执行：

```powershell
pwsh ./eng/Test-LongGridDesktopFirstStartup.ps1 `
  -Configuration Release `
  -StabilitySeconds 5 `
  -MaximumColdProcessReadyMilliseconds 10000 `
  -NoBuild
```

| 项目 | Expected | Actual | Difference |
| --- | ---: | ---: | ---: |
| 冷进程到真实 DesktopHost 可见 | ≤10000 ms | 7570 ms | -2430 ms，Pass |
| 首次控制中心 | Hidden | Hidden | None |
| DesktopHost | ≥1 | 1 | None |
| 稳定期 | 5 秒响应 | 5 秒响应 | None |
| 第二实例 | 退出 0、唯一控制中心 | 退出 0、唯一控制中心 | None |
| 退出残留 | 0 | 0 | None |
| 临时配置写入 | 0 | 0 | None |

该结果只能证明本次机器/会话满足 10 秒冷启动上限，不能证明 P95，也不能证明运行期开启 1 秒。

## 4. 真实负向测试：门禁确实会失败

执行同一真实进程场景，但显式把冷启动预算收紧为 1000 ms：

```powershell
pwsh ./eng/Test-LongGridDesktopFirstStartup.ps1 `
  -Configuration Release `
  -StabilitySeconds 0 `
  -MaximumColdProcessReadyMilliseconds 1000 `
  -NoBuild
```

| 项目 | Expected | Actual | Difference |
| --- | ---: | ---: | --- |
| 冷进程到真实 DesktopHost 可见 | ≤1000 ms | 1574 ms | +574 ms |
| JSON Outcome | Fail | Fail | None |
| 进程退出码 | 非零 | 1 | None |
| Difference | 有限、可解释 | `ColdProcessDesktopHostReadyExceededBy574Milliseconds` | None |

这证明性能阈值不是只写入报告的观察字段；回归超限会使真实门禁失败。

## 5. 与 Stage 197 的纠偏关系

Stage 197 记录的 5107/1589/1140 ms 均是真实冷启动观察值，数据本身保留；“三次均未达到 PF-001 运行期开启 1 秒目标，所以该产品验收 Fail”的映射不成立。修正后的判定为：

- **冷启动 10 秒恢复上限**：本轮 7570 ms，Pass；仍需多样本/P95 和优化；
- **运行中重新开启方格 ≤1000 ms**：当前脚本明确不测，`PendingDedicatedRealEvidence`；
- **驻留唤醒 P95 <300 ms**：仍无专用真实证据，Pending。

这不是放宽需求，而是避免使用错误场景制造假失败或假通过。

## 6. 需求与竞品对齐

- 对齐产品愿景：冷启动首次恢复现在有 10 秒硬门禁；
- 对齐 PF-001：1 秒运行期开启仍保留为独立硬目标；
- 对齐“真实测试”：使用真实 App、真实 DesktopHost HWND、真实第二实例与真实关闭，不以 Mock 代替；
- 对齐“预期—实际—差异”：正向与负向测试都输出可机器读取的差值；
- 不扩张任务栏、Widgets 或其他后续范围。

## 7. 剩余风险与下一步

1. 新增专用运行期开启真实证据：在已初始化的正式 App 内关闭方格，确认 Surface/输入/UIA 释放，再开启并测量真实 HWND 恢复；目标 ≤1000 ms；
2. 至少采集冷启动多样本并报告 median/P95，不能用一次 7570 ms 代表稳定性能；
3. 若运行期开启或冷启动分布超限，再按配置读取、XAML 构造、目录枚举、拓扑和 Host 创建分段计时定位，不做无证据的“优化”；
4. Gate A 性能证据完成后，将当前长期功能分支通过 PR 和完整主线 CI 集成，再进入 PF-006C1。

因此 PF-001 仍为 `EngineeringComplete / ProductEvidencePending`，项目仍不可公开分发。
