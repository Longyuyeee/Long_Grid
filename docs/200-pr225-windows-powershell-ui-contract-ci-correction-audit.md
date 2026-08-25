# Stage 200：PR #225 Windows PowerShell UI 合同 CI 纠偏审计

日期：2026-08-25  
开发项：Gate A / 主线 CI 集成纠偏  
结论：**Local Reproduction Fixed / PR CI Rerun Required**

## 1. 远端真实失败

PR #225 首次 CI run `32797843950` 在以下步骤失败：

`Validate batch selection accessibility session chain`

此前 Restore、Format、Build、启动链、显示矩阵、DesktopHost、输入、激活、系统表面和 Issue 19 合同均通过。失败不是 App 编译或 Stage 199 运行期开启性能回归，而是 CI 与本机 PowerShell 运行时差异暴露出的两个合同问题。

## 2. Expected / Actual / Difference

| 项目 | Expected | 首次 CI Actual | Difference |
| --- | --- | --- | --- |
| `Test-LongGridUi.ps1` 在 Windows PowerShell 5.1 解析 | 成功 | 无 BOM UTF-8 被按 ANSI 解码，中文字符串损坏并产生 ParserError | Encoding mismatch |
| 批量无障碍入口消费权威 UI 合同 | 157 AutomationIds | 仍硬编码 146 | -11 / stale contract |
| CI 结果 | Pass | Failure，后续步骤跳过 | Fail |

本机先前使用 `pwsh 7`，能自动识别无 BOM UTF-8，因此没有复现第一项；这证明仅运行新 PowerShell 不足以代表 GitHub Windows runner 的真实入口。

## 3. 修正

1. 将 `eng/Test-LongGridUi.ps1` 机械恢复为 UTF-8 BOM，内容不变；
2. `Start-LongGridBatchAccessibilitySession.ps1` 将权威 AutomationId 总数从过期的 146 更新为 157；
3. 批量无障碍入口新增前三字节 `EF BB BF` 合同，未来 BOM 丢失时在调用解析器前给出明确失败原因；
4. 不改变 BSA-01～BSA-05 的人工证据状态，仍为 `PendingManualEvidence`。

## 4. 与 CI 同运行时复现

执行与 GitHub workflow 相同的 Windows PowerShell 5.1 入口：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Test-LongGridUi.ps1 -ContractOnly

powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-LongGridBatchAccessibilitySession.ps1 -ValidateOnly
```

修正后结果：

| 项目 | Expected | Actual | Difference |
| --- | --- | --- | --- |
| UI 合同解析 | Pass | Pass | None |
| AutomationIds | 157 | 157 | None |
| 批量无障碍合同 | Pass，人工结果保持 Pending | Pass，`PendingManualEvidence` | None |
| Issue 23 后续合同 | Pass | Pass | None |
| Issue 24 后续合同 | Pass | Pass | None |

## 5. 需求和安全对齐

- 没有降低 UI、无障碍或测试阈值；只同步权威总数并修复运行时编码；
- 没有把合同脚本 Pass 伪报为 Narrator/高对比人工 Pass；
- 没有修改产品文件操作、配置或 DesktopHost 权限边界；
- 修正直接来源于远端 Expected / Actual / Difference，而不是假设性改动。

## 6. 下一步

推送修正并等待 PR #225 完整 CI 重跑。只有所有必需步骤通过后才能合并；若后续步骤继续暴露分支长期积累的陈旧合同，继续逐项真实复现与纠偏，不绕过检查。

## 7. 第二次 CI 增量

第二次 run `32798286067` 已真实越过第一次失败点，并通过 Build、启动链、批量无障碍、Issue 23/24、三个原生 DesktopHost 探针和 157-ID 权威 UI 合同。随后 `Validate clean-session UIA chain` 失败：该消费者仍硬编码 153-ID。

本轮将 `Test-LongGridCleanSession.ps1` 的 validate-only、live 校验、输出字段和错误文案统一更新为 157；没有减少 AutomationId，也没有把 clean-session 合同扩张成真人 UIA Pass。修正后必须先用 workflow 同款 Windows PowerShell 5.1 `-ValidateOnly` 本地验证，再触发第三次远端完整 CI。
