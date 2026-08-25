# Stage 202：main 原生交互 Surface 资源平台期纠偏审计

日期：2026-08-25  
开发项：Gate A / PR #225 主线集成后验收  
结论：**Pass / Gate A Closed**

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

## 4. 纠正 PR 真实远端结果

纠正 PR #226 的完整 Windows CI run [`32805578609`](https://github.com/Longyuyeee/Long_Grid/actions/runs/32805578609) 已通过，不是仅重跑原失败步骤：

| 验收 | Expected | PR Actual | Difference |
| --- | --- | --- | --- |
| 原生 Surface 模式适配器 | 平台期与清理均 true | `RepeatedResourcePlateau=true`、`CleanupPassed=true` | None |
| Release 测试 | 0 fail | 1198/1198，0 fail，12 s | None |
| 覆盖率门 | lines ≥90%、branches ≥75% | 90.01%（40610/45116）、75.51%（13138/17400） | None |
| 依赖漏洞门 | 无已知漏洞 | Pass | None |
| 内部 unsigned RC | portable/MSIX/SBOM/清单审计通过 | Pass；800/800 文件校验成功 | None |

该结果表明纠正后的资源判定在独立 GitHub Windows runner 上复现，且没有以降低覆盖率、资源门槛或跳过下游交付审计换取通过。当前剩余差异仅为：纠正提交尚未合入 `main`，因此仍需主线 push CI 复验。

## 5. main 集成复验与 Gate A 收口

PR #226 已 squash 合入 `main` 为 `3489ca0`。主线 push CI run [`32806544628`](https://github.com/Longyuyeee/Long_Grid/actions/runs/32806544628) 完整通过：

| 验收 | Expected | Main Actual | Difference |
| --- | --- | --- | --- |
| 原生 Surface 模式适配器 | 平台期与清理均 true | `RepeatedResourcePlateau=true`、`CleanupPassed=true` | None |
| Release 测试 | 0 fail | 1198/1198，0 fail，13 s | None |
| 覆盖率门 | lines ≥90%、branches ≥75% | 90.01%（40610/45116）、75.51%（13138/17400） | None |
| 依赖漏洞门 | 无已知漏洞 | Pass | None |
| 内部 unsigned RC | portable/MSIX/SBOM/清单审计通过 | Pass；800/800 文件校验成功 | None |

本次没有通过重跑掩盖 main 差异：旧 main run 明确失败，探针观测与所有权边界被纠正，随后 PR 最终头与新 main 各自完成一次全链路通过。长期分支 PR、完整 CI、主线集成和失败纠偏闭环均已具备证据，**Gate A 关闭**。

## 6. 需求对齐与下一步

本切片只修复 Gate A 的资源稳定证据，不修改桌面文件、配置、窗口交互产品逻辑、任务栏或小组件范围。PF-001～PF-005 仍为 `EngineeringComplete / ProductEvidencePending`，PF-006 仍为 `InProgress`；当前不存在方向偏移。下一工程切片恢复为 PF-006C1：PageUp/PageDown 跨视口键盘导航，继续要求 viewport、选择、焦点和 UIA 快照在真实 HWND 中一致收敛。
