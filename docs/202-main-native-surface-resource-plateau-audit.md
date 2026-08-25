# Stage 202：main 原生交互 Surface 资源平台期纠偏审计

日期：2026-08-25  
开发项：Gate A / PR #225 主线集成后验收  
结论：**Local Real Probe Pass / Corrective PR And Main CI Required**

## 1. 主线真实失败

PR #225 已以 `9889cdc` squash 合入 `main`。main run [`32804120282`](https://github.com/Longyuyeee/Long_Grid/actions/runs/32804120282) 在正式测试前的 `Validate native interaction surface mode adapter` 停止：

| 项目 | Expected | Main Actual | Difference |
| --- | --- | --- | --- |
| USER 清理 | 回到基线 | 2 → 4 → 2 | None |
| GDI 清理 | 基线 +1 以内 | 1 → 2 → 2 | None |
| Process handle 清理 | 基线 +2 以内 | 308 → 312 → 310 | None |
| 三轮资源平台期 | 每轮与单次清理快照完全相等 | `RepeatedResourcePlateau=false` | Fail |

功能合同、前台稳定、失败恢复和即时清理全部为 true；失败只发生在重复平台期的观测语义。相同代码在两个 PR runner 通过，不能因此重跑或忽略 main 失败。

## 2. 根因审计

原探针用 `Process.GetCurrentProcess()` 读取 GUI/handle 资源却不释放 `Process` 对象。观测动作本身会创建延迟释放的进程句柄，偶尔恰好抵消同进程 UI Automation client/DWM 的连接缓存变化，形成环境相关的“完全相等”。纠正为 `GetCurrentProcess` 伪句柄与 `GetProcessHandleCount` 原生读取后，旧算法在本机 5/5 正确暴露为 Fail，证明旧通过不具备稳定判定力。

进一步分离产品所有权：

- 真实 HWND 的 passive/explicit UIA 模式、SelectionPattern 和一次隐藏清理仍在主流程验证；
- 资源平台期复用一个产品式长生命周期 HWND，只测三轮 warm-up + 三轮 passive/explicit/passive 原生模式循环；
- USER/GDI 每轮必须回到已建立上限，最多等待 1 秒处理有限异步释放；
- HWND 释放后 USER/GDI 必须回到主流程清理上限，process handle 仍不得超过主流程清理快照 +3；
- 释放时显式调用 `UiaDisconnectProvider`，然后销毁 HWND 和窗口类。

这不是放宽产品泄漏门：它把同进程 UIA client proxy 缓存从产品 provider 资源中分离，同时保留真实 UIA 功能、即时清理、长期 HWND 模式循环和最终销毁四个独立门。

## 3. 本机真实修正结果

| 验收 | Expected | Actual | Difference |
| --- | --- | --- | --- |
| Probe build | 0 warning / 0 error | 0 / 0 | None |
| 连续新进程 | 5/5 Pass | 5/5 `Conditional Pass` | None |
| USER | 清理回基线 | 每次 2 → 4 → 2 | None |
| GDI | 清理 ≤ 基线 +1 | 每次 1 → 2 → 2 | None |
| Handles | 清理 ≤ 基线 +2 | 每次 334 → 336 → 336 | None |
| 重复平台期 | true | 5/5 true | None |

随后同一次完整 Release collector 为 **1198/1198、lines 90.35%（20381/22558）、branches 75.66%（6582/8700）**，90%/75% 门通过；格式门与 `git diff --check` 同时通过。

## 4. 需求对齐与下一步

本切片只修复 Gate A 的资源稳定证据，不修改桌面文件、配置、窗口交互产品逻辑、任务栏或小组件范围。PF-001～PF-005 仍为 `EngineeringComplete / ProductEvidencePending`，PF-006 仍为 `InProgress`；PF-006C1 必须继续等待纠正 PR 和新的 main CI 全绿。
