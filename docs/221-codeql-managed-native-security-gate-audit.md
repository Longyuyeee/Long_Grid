# Stage 221：C# / C++ CodeQL 安全门禁审计

> 日期：2026-08-28
> 结论：**C# 与原生 C++ 已分别通过真实 CodeQL 2.26.4 manual-build 分析，当前 PR merge ref 结果为 0/0；扫描能力已建立，但不替代人工安全评审、依赖漏洞或发布批准**

## 1. 目标与初始事实

Stage 220 结束后，依赖漏洞、许可证元数据、SBOM、文件/进程/资源探针均已进入 CI，但 CodeQL 仍在质量文档中 Pending。接入前真实调用 Code Scanning API 返回 `no analysis found`（HTTP 404），因此不能把编译器、测试或漏洞依赖扫描冒充源码数据流分析。

仓库同时包含正式 C# 产品链和 `LongGrid.ExplorerCommand` 原生 C++ COM server。目标是让 pull request 与 main 对两种语言分别创建真实数据库、按实际编译追踪并上传 SARIF；不授予 secrets、OIDC、protected environment、证书、包安装或分发能力。

GitHub 官方 `codeql-action` 当前 major 为 `v4`，本轮实际分析工具版本为 `2.26.4`。`build-mode: manual` 只支持每个 job 单一语言，因此使用 `csharp / c-cpp` matrix，而不是在一个初始化步骤中混合两种编译语言。官方实现参考：[CodeQL Action](https://github.com/github/codeql-action)、[CodeQL Bundle 2.26.4](https://github.com/github/codeql-action/releases/tag/codeql-bundle-v2.26.4)。

## 2. 实现与权限

`.github/workflows/codeql.yml` 在 PR 与 main push 上运行两个 Windows job：

- C#：locked solution restore 后执行全解决方案 Release build；
- C++：复用 `eng/Build-LongGridExplorerCommand.ps1`，真实构建正式 DLL 与 Probe；
- 两者均在固定到同一受审 v4 完整 commit 的 `init` 和 `analyze` 之间使用 manual build，并按 `/language:<language>` 分离结果；
- job timeout 为 30 分钟，matrix `fail-fast=false`，一种语言失败不会隐藏另一种语言事实；
- workflow 权限精确为 `contents: read` 与 `security-events: write`。

`eng/Test-LongGridCodeQlWorkflow.ps1` 同时由主 CI 和 CodeQL job 执行，强制检查触发器、双语言 matrix、manual build、完整 commit 固定且属于 v4 系列的 action、locked/Release/原生构建入口、精确权限，并拒绝 `id-token: write`、secret、environment、SignTool/证书和 AppX 状态修改。精确 Action commit 与消费者范围由 Stage 222 清单合同复核；语言负向变体删去 `c-cpp` 后必须得到 `missing:exact language matrix`。

## 3. Expected / Actual / Difference / Correction

Expected：C# 与 C++ 各自真实构建、分析和上传；Code Scanning API 出现两份当前 PR merge ref 分析；任何结果都按实际记录，不能因 job 绿色假定零告警；签名/分发访问保持 false。

Actual：PR #279 run `33141220963` 中 C# `7m41s`、C++ `3m12s` 均成功。API 返回相同 merge commit `8a6f26890f5d02cf9f7b0ba509c79fdff0791b12` 上两份 CodeQL `2.26.4` 分析：

| Category | Analysis ID | Rules | Results |
| --- | ---: | ---: | ---: |
| `/language:csharp` | `1685598495` | 52 | 0 |
| `/language:c-cpp` | `1685587941` | 58 | 0 |

随后读取全仓库 open Code Scanning alerts 为 0。C++ job 注释说明 manual build 不能生成 overlay database，工具已回退为正常 full database；build、analyze 与上传均成功，因此这是执行方式说明，不是扫描跳过。0 results 只描述该提交、该查询集，不承诺未来代码或其他安全工具没有问题。

首次本地合同脚本在断言前因 PowerShell `foreach` 直接枚举未括号化 `[ordered]@{}` 而解析失败。Expected 为检查真实/负向合同，Actual 为脚本未执行；Correction 为括号化哈希表达式后用 Windows PowerShell 复跑，真实 workflow Pass，C#-only 负向变体精确失败。没有用删除 C++、放宽权限或跳过 manual build 修正测试。

本地与工作流等价的 locked restore、格式、C# Release build、原生 DLL/Probe build 均通过且为 0 warning / 0 error；1,381/1,381 测试通过、0 跳过。原有 PR CI run `33141221044` 用时 `7m30s` 全绿，包括新增 CodeQL 合同、覆盖率、全部产品/安全探针、漏洞、许可证元数据和完整 unsigned RC。最终文档提交后的 CodeQL 与主 CI 结果以 PR 最新检查为准。

## 4. 目标与需求审计

开发目标审计：C# 与正式原生 C++ 的源码分析从 `no analysis found` 变为可复读的双语言 CodeQL/SARIF 门禁，完成；当前查询集结果为 0，未发现需要修改的产品代码。

需求对齐审计：扫描是独立最小权限工作流，不接触用户文件、包状态、签名环境或发布秘密；它补充而不替代 90%/75% 覆盖率、依赖漏洞、许可证/NOTICE、SBOM、真实人工矩阵和 signed lifecycle。#19/#20/#24、#23 和 #274 状态不变，产品仍不可安装、不可分发。
