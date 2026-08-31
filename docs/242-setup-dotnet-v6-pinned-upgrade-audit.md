# Stage 242：setup-dotnet v6 固定 SHA 升级审计

日期：2026-08-31

输入基线：`origin/main@fc6d3738e6a2840bdc241ba9e17ca325f60383bd`

Dependabot 输入：PR #314 / `4379d4c8e218e45e606ab79947907daecec5141f`

状态：`CorrectionComplete / LocalAndPrVerificationPass / MainVerificationPending / ProductStatusUnchanged`

## 1. 开发目标与范围

本阶段接续当前唯一可在仓库内关闭的真实缺口：Dependabot 把 CI 与 CodeQL 的 `actions/setup-dotnet` 从 v5.4.0 升到 v6.0.0，但只修改了两个 workflow，没有同步仓库固定 SHA 批准清单，导致 PR #314 的 CI 与 C#/C++ CodeQL 在第一道 Action pin 门禁真实失败。

目标是核验新提交的官方来源和权限边界，把经过审核的精确 SHA 写入批准清单，并用本机完整回归与 GitHub Hosted Runner 验证真实效果。范围不包含产品运行时、Windows App Runtime 安装、M1/M2 功能、签名或分发。

## 2. Expected / Actual / Difference / Correction

| 检查 | Expected | Initial Actual | Difference / Correction |
|---|---|---|---|
| 官方身份 | workflow SHA 必须精确对应官方不可变 tag | GitHub `actions/setup-dotnet` 官方 `v6.0.0` tag 精确指向 `a98b56852c35b8e3190ac28c8c2271da59106c68` | 身份一致；批准清单继续使用 40 位 SHA，不改成可变 `@v6` |
| 上游变化 | 明确 major 变化及其运行环境 | 官方 release 说明为 ESM/依赖升级；该提交的 `action.yml` 使用 Node 24，现有 `global-json-file`、cache 输入仍存在 | GitHub Hosted Runner 必须执行真实 Action；本机合同不能替代远端运行结果 |
| 供应链门禁 | 未批准的新 SHA 必须失败 | PR CI run `33332679682` 和 CodeQL run `33332679693` 均返回两条 `unapproved-pin`；本机也以 exit 1 精确复现 | 在 `.github/actions-pins.json` 将 setup-dotnet 从 v5 SHA 更新为经核验的 v6 SHA |
| 首次结果包装 | 子进程失败必须被外层记录为 Fail | 第一次外层用 `try/catch` 包装 `powershell.exe`；子进程真实失败但没有抛入外层，包装器错误追加 `UnexpectedPass` | 改按 `$LASTEXITCODE` 判断并复测为 exit 1；保留门禁原始两条差异，不把包装错误记为门禁通过 |
| 权限边界 | 升级不得扩大仓库或发布权限 | CI 仍只有 `contents: read`；CodeQL 仍只有 `contents: read / security-events: write`，没有 secret、OIDC、environment、签名或分发入口 | `Difference=None`，不修改权限与任务内容 |
| 修正后 pin 合同 | 两个消费者接受新 SHA，负向控制仍失败关闭 | 本机 `outcome=Pass`，2 workflows / 5 approved targets / 7 pinned usages；mutable、drift、unknown、duplicate-consumer 四类负向差异均被识别 | `Difference=None` |

官方核验来源：[actions/setup-dotnet v6.0.0 release](https://github.com/actions/setup-dotnet/releases/tag/v6.0.0)。

## 3. 实现

- `.github/workflows/ci.yml` 与 `.github/workflows/codeql.yml` 使用 Dependabot 提供并经官方 tag 复核的精确 v6.0.0 SHA；
- `.github/actions-pins.json` 的 `actions/setup-dotnet` 条目同步为 `version=v6` 和同一 SHA；
- workflow 的 SDK 来源仍为仓库 `global.json`，当前锁定 `8.0.400 / latestPatch / allowPrerelease=false`；
- 没有修改产品代码、测试断言、覆盖率阈值、Action 消费范围或工作流权限。

## 4. 本机真实验证

| 门禁 | Expected | Actual | Difference |
|---|---|---|---|
| locked restore / format | 锁文件无漂移、格式无差异 | Pass | None |
| Release build | 0 warning / 0 error | 0 warning / 0 error | None |
| 完整测试 | 1,394 项全部通过、0 skipped | `1,394/1,394`、0 skipped，39 秒 | None |
| coverage | lines >=90%、branches >=75% | lines `90.43% (47090/52072)`；branches `76.16% (15458/20298)` | None |
| 漏洞 | 不存在已知漏洞包 | Pass，0 known vulnerable packages | None |
| 许可证元数据 | 20 projects / 30 packages 确定性通过，同时不得冒充法律批准 | Pass，report SHA-256 `7d7d7aab...0cacfb`；`PendingOwnerReviewAndNotice / distributionApproved=false` | None |
| Action / Dependabot / CodeQL 合同 | 固定 SHA、周更、双语言、最小权限保持 | 全部 Pass；签名或分发访问为 false | None |

本机只能验证仓库合同与产品回归；Node 24 Action 的真实执行必须由本阶段 PR 的 GitHub Hosted Runner CI/CodeQL 完成，远端未通过前状态保持 `RemoteVerificationPending`。

## 5. 开发目标与需求对齐审计

开发目标审计：官方 tag、提交、major 变化和 Action metadata 已复核；首次远端/本机失败及包装器误判均保留；固定 SHA 清单已最小修正，本机完整回归通过。远端 GitHub Hosted Runner 仍是本阶段最后出口。

需求对齐审计：变更只维护既有 CI/CodeQL SDK 安装 Action，不修改 Long方格产品代码，不增加权限，不接触签名、Publisher、用户文件或系统状态，也不把供应链门禁通过写成产品功能完成。

产品状态继续为 M1/M2 `0/2 Complete`、30 项 PF `0 Complete`。下一产品接续点仍是 Stage 241 定义的 BOX-R1-C/D 与 M1 完整物理旅程；当前机器仍缺 Main.2 `>=2.3.1.0` 与 DDLM `2.3.1.0-x6`，并存在既有 LongGrid 进程，不能进入正向证据。远端通过后，PR #314 应由本阶段完整变更取代，避免合入缺少批准清单的半更新。

## 6. PR #315 真实 Hosted Runner 结果

首次提交 `5f9b4fe` 的 [PR #315](https://github.com/Longyuyeee/Long_Grid/pull/315) 已在 GitHub Hosted Windows Runner 真实执行 setup-dotnet v6.0.0。CI run `33348703652` success：`1,394/1,394`、0 skipped，coverage lines `90.12% (46926/52072)`、branches `76.04% (15434/20298)`，漏洞 0，20 projects / 30 packages 许可证元数据通过；内部 RC 仍为 `PendingOwnerReviewAndNotice / distributionApproved=false`，artifact ID `9742949529`、1,000,981 bytes。

CodeQL run `33348703648` 的 C++ 与 C# 均 success，SARIF 成功上传，分支开放告警 0。Initial Actual 的两条 `unapproved-pin` 已消失，v6 Node 24 Action 完成实际 SDK setup 和全部下游消费者，修正后 `Difference=None`。本次证据回填提交仍须通过同一 PR 的 CI/CodeQL；合并后还须核对精确 main 运行，完成前状态保持 `MainVerificationPending`。
