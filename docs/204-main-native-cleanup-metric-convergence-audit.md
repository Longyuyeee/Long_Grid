# Stage 204：main 原生 Surface 清理指标收敛审计

- 日期：2026-08-25
- 基线：`main@9b6bed2`
- 触发证据：GitHub Actions run `32810599025`
- 状态：`EngineeringComplete / PRAndMainEvidencePending`

## 1. 目标与基线差异

`main@9b6bed2` 没有产品代码变化，但完整流水线在 `Validate native interaction surface mode adapter` 失败。Expected 为真实 HWND 销毁后 `CleanupPassed=true`；Actual 为 USER `2→2`、GDI `1→2`、重复资源平台期为真，但进程总句柄 `302→308`，旧合同以 `handlesAfter <= handlesBefore + 2` 判定清理失败。

同一基线本机四次独立进程复现为一次 `334→337` 失败、三次 `334→336` 通过。差异证明 .NET/UIA 可在探针运行期间异步初始化进程级基础设施，总进程句柄相对启动瞬间的固定差值不能归属为单个 DesktopHost HWND 泄漏。

## 2. 修正边界

- 不提高 USER/GDI 资源上限；销毁后 USER 必须回到基线，GDI 只允许已审计的 UIA 进程级单对象平台期；
- 同一真实 HWND 先执行三轮 warm-up，再执行三轮 passive→explicit→passive；每轮 USER/GDI 必须回到同一平台期；
- `ProcessHandlesBefore/Created/After` 继续输出为诊断数据，但不再用进程启动瞬间的固定 `+2` 误判单 HWND 清理；
- 不接触 Explorer、桌面文件或产品 App，不使用合成输入，也不降低覆盖率、构建或发布门禁。

静态 UI 合同同步禁止重新引入 `handlesAfter <= handlesBefore`，并要求 USER/GDI 清理、有限平台等待和诊断口径同时存在。

## 3. 真实测试与差异修正

| 验证 | Expected | 首次 Actual | 修正后 Actual | Difference |
| --- | --- | --- | --- | --- |
| 原生探针独立进程 | 6/6 `Conditional Pass` | 原合同 1/4 因总句柄 `+3` 失败 | 6/6 通过 | None |
| HWND USER | 销毁后回到基线 | `2→2` | 六次均 `2→2` | None |
| HWND/UIA GDI | 不超过基线 +1 | `1→2` | 六次均 `1→2` | None，已知进程级 UIA 平台期 |
| 重复模式切换 | 三轮不增长 | `RepeatedResourcePlateau=true` | 六次均为 true | None |
| 清理判定 | `CleanupPassed=true` | 偶发 false | 六次均为 true | None |
| Release 全量测试 | 1200/1200 | 1200/1200 | 1200/1200 | None |
| UI 合同 | 157 IDs，合同通过 | live UIA 被已知运行时崩溃门阻止 | `-ContractOnly` Pass | 产品 live UIA 仍 Pending |

默认 UI 验证在 Windows App Runtime 2.4.0.0 / Microsoft.UI.Xaml 3.2.3.0 上按既有 `RPC_E_WRONG_THREAD` 风险正确失败关闭；本切片没有用确认开关绕过崩溃门。真实 HWND 资源验证由独立原生探针承担。

## 4. 需求与方向对齐

本修正恢复“主干必须绿、测试必须测量产品可归属资源”的真实性，没有把重跑当修复，也没有放宽产品 USER/GDI 清理目标。它不改变 PF 状态：PF-001～PF-005 仍为 `EngineeringComplete / ProductEvidencePending`，PF-006 仍为 `InProgress`；基线集成完成后继续 PF-006C2 鼠标框选。

## 5. 集成门

只有 PR 与合入后的 main 完整流水线都通过，本文才能改为 `EngineeringComplete / ProductEvidencePending`。在此之前不得宣称主干恢复绿色。
