# Stage 223：Dependabot GitHub Actions 更新发现与人工批准审计

> 日期：2026-08-28
> 结论：**固定 SHA 的 GitHub Actions 已建立每周更新发现入口；更新只形成 PR，不自动合并，且必须经 pin 清单批准和完整真实 CI 后才能进入 main**

## 1. 目标与初始事实

Stage 222 把 2 个 workflow 中 5 个远程 Action target、7 个调用固定到完整 commit。随后同时复读本地与远端 `.github/dependabot.yml`，实际均不存在（远端 Contents API HTTP 404），仓库文档也没有 Action 更新策略。Expected 为不可变执行身份同时具备受控更新发现；Initial Actual 为 pin 已固定、自动发现入口为 0，只能依赖维护者偶然检查上游 major ref。

本切片依据 GitHub 官方 [Dependabot configuration options](https://docs.github.com/en/code-security/dependabot/dependabot-version-updates/configuration-options-for-the-dependabot.yml-file) 建立 `github-actions` version updates。它不自动批准上游代码，不加入私有 registry 或 secret，不开放 auto-merge，也不扩张到 NuGet；依赖包仍由现有 locked restore、漏洞和许可证门禁管理。

## 2. 有界策略与审批闭环

`.github/dependabot.yml` 精确配置：

- `version: 2`，唯一 ecosystem 为 `github-actions`；
- 扫描仓库根 `/`，目标分支精确为 `main`；
- 每周一 `04:00 Asia/Hong_Kong` 检查；
- 最多同时打开 2 个版本更新 PR；
- commit message 前缀为 `ci`；
- 无 registry、secret、非 main 目标或自动合并指令。

Dependabot 能识别 workflow 中固定 SHA 并提出更新，但不会同步 Long方格自定义 `.github/actions-pins.json`。因此新更新 PR 初始应被 `Test-LongGridWorkflowActionPins.ps1` 以 `unapproved-pin` 拒绝；维护者必须解析官方 ref/commit、审查变更、同步清单，再让完整 CI 与双语言 CodeQL 运行。这个红灯是人工批准边界，禁止通过删除 pin 合同或改回 major 标签消除。

## 3. Expected / Actual / Difference / Correction

Expected：配置只发现 GitHub Actions 更新，频率和 PR 数有界；daily、NuGet 扩张、私有 registry 或非 main 目标必须失败；配置必须是可解析 YAML。

Actual：`eng/Test-LongGridDependabotConfiguration.ps1` 在 Windows PowerShell 与 pwsh 均返回 `github-actions / weekly / main / openPullRequestsLimit=2 / autoMergeEnabled=false`。四个内存负向变体得到：

| 负向变体 | 实际差异 |
| --- | --- |
| weekly → daily | `missing:weekly interval`、`forbidden:daily schedule` |
| github-actions → nuget | `missing:GitHub Actions ecosystem`、`forbidden:non-actions ecosystem` |
| 增加 private registry | `forbidden:private registry` |
| main → develop | `missing:main target branch`、`forbidden:non-main target branch` |

PyYAML 6.0.3 对真实文件解析成功，得到 schema version 2 和唯一更新项，字段、类型与合同一致。Stage 222 pin 合同仍为 2 workflows / 5 targets / 7 usages，CodeQL 双语言、manual build 和权限合同继续通过。

本地 locked restore、格式与 Release 全解决方案构建通过，0 warning / 0 error；关闭 build server 后完整真实套件一次通过 `1,381/1,381`、0 跳过，lines `90.41% (47078/52072)`、branches `76.17% (15460/20298)`。漏洞门禁为 0 known vulnerable package；许可证门禁保持 20 projects / 30 packages、确定性 SHA-256 `7d7d7a...cacfb`、`PendingOwnerReviewAndNotice / distributionApproved=false`；签名 ValidateOnly 继续为 `liveSigningImplemented=false / installOrDistributionApproved=false`。

读取官方文档的临时 PowerShell 命令首次因 `foreach` 结果直接接管道而在发起请求前解析失败；括号化收集后两个官方页面均 HTTP 200，主配置页面同时包含 `github-actions`、`open-pull-requests-limit` 与 `groups` 说明。该编排差异没有写入仓库配置。

## 4. 开发目标与需求对齐审计

开发目标审计：从“固定 SHA 但无自动更新发现”收敛为“每周发现、PR 呈现、清单人工批准、完整回归后合并”，完成。首次真实 Dependabot update job/PR 只能在配置合入 main 后由 GitHub 调度；在它实际发生前不能声称上游更新已验证。

需求对齐审计：不修改产品运行时、NuGet 范围、用户文件、Publisher、许可证、environment、secret、OIDC、签名、安装或分发权限。Dependabot PR 不具有绕过 CI/pin 合同的批准权，#19/#20/#24、#23 和 #274 状态不变。

## 5. 合并后真实调度结果

合入 `main@965dccbe` 后，GitHub 动态 Dependabot run `33145315430` 用时 3 分 17 秒并成功创建恰好 2 个 PR，符合并发上限：#282 将 upload-artifact 从 v6 提升到 v7.0.1，#283 将 checkout 从 v6.1.0 提升到 v7.0.1。两个 PR 的 CI 与 C#/C++ CodeQL 均在 pin 合同处以精确 `unapproved-pin` 失败，没有进入自动批准或合并。Expected 的“发现但不批准”与 Actual 一致；Stage 224 另行承担官方提交审计、清单同步和完整回归。
