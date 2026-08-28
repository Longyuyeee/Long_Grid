# Stage 224：GitHub Actions v7 人工批准升级审计

> 日期：2026-08-28
> 结论：**Dependabot 首次发现的 checkout/upload-artifact v7.0.1 已经过官方 tag、签名提交、执行入口、Runner 兼容性与消费者范围人工审查；同步 workflow 与 pin 清单后仍须由本机回归和 GitHub 实际加载结果共同批准**

## 1. 目标与初始事实

Stage 223 合入后，真实动态 run `33145315430` 创建 #282/#283，恰好达到配置的 2 个开放 PR 上限。两个 bot PR 只替换 workflow SHA，没有修改 `.github/actions-pins.json`，因此 CI 与 C#/C++ CodeQL 都在最前面的 pin 校验处精确报告 `unapproved-pin`。这是预期的人工审批边界，不是需要删除的障碍。

本切片不直接合并 bot PR，而是在独立分支复核两个上游版本后同步实施。范围只包含 `actions/checkout` 和 `actions/upload-artifact`；setup-dotnet、CodeQL、NuGet、权限、secret、OIDC、签名、安装和分发均不扩大。

## 2. 官方身份与兼容性审查

2026-08-28 通过 GitHub 官方 API 解析两个轻量 tag，均直接指向 Dependabot 提议的 40 位 commit，提交的 GitHub verification 均为 `verified=true / reason=valid`：

| Action | 官方 tag | 执行 commit | 当前消费者 |
| --- | --- | --- | --- |
| `actions/checkout` | `v7.0.1` | `3d3c42e5aac5ba805825da76410c181273ba90b1` | CI、CodeQL |
| `actions/upload-artifact` | `v7.0.1` | `043fb46d1a93c77aae656e7c1c64a875d1fc6a0a` | CI always-upload |

两个固定 commit 的真实 `action.yml` 均可读取且使用 `runs.using: node24`。本项目升级前的 checkout v6 与 upload-artifact v6 已经使用 Node 24，官方最低 Runner 为 `2.327.1`；Stage 223 main runner 日志真实为 `2.336.0`，高于下限。checkout v7 新增对 `pull_request_target/workflow_run` 下不安全 fork PR 检出的默认阻断，而本仓库工作流只由 `push/pull_request` 触发且没有 `allow-unsafe-pr-checkout`。upload-artifact v7 增加 ESM 和 `archive:false` 直接单文件上传；本仓库继续使用默认归档、多路径、唯一 artifact 名称，不启用新输入。

## 3. Expected / Actual / Difference / Correction

Expected：bot 发现的新 SHA 在清单未批准时失败；人工批准后 2 个 workflow、5 个 target、7 个调用仍全部固定到受审 commit，四类负向变体继续失败；GitHub hosted runner 必须实际执行 v7 checkout，并由 CI always-upload 实际产生可下载 artifact。

Initial Actual：#282/#283 的 workflow SHA 与官方 v7.0.1 一致，但自定义清单仍批准 v6，全部检查得到 `unapproved-pin`。Difference 是“上游更新已发现，但仓库批准身份尚未同步”。

Correction：CI/CodeQL 的 checkout、CI 的 upload-artifact、`.github/actions-pins.json` 和 pin 合同负向夹具一起更新到上述 v7.0.1 commit。不得改回 major 标签、删除清单或放宽 `unapproved-pin`。本机与 PR/main 的最终测试数、覆盖率、真实 artifact 和 CodeQL 结果在运行完成后回填审计评论；任何首次差异都保留并按原因修正。

本机 Actual：Windows PowerShell 5.1 与 pwsh 的 pin 正向合同均为 `2 workflows / 5 targets / 7 usages`，四个内存负向变体分别精确得到 `mutable-ref / unapproved-pin / unapproved-action / consumer-drift`；Dependabot 与 CodeQL 合同、3 份 YAML 和清单 JSON 解析均通过。locked restore、格式和 Release 全解决方案构建通过，构建为 `0 warning / 0 error`。关闭 build server 后完整真实套件一次通过 `1,381/1,381`、0 跳过、17 秒，coverage lines `90.41% (47078/52072)`、branches `76.17% (15460/20298)`；漏洞为 0，许可证门禁保持 20 projects / 30 packages、确定性 SHA-256 `7d7d7a...cacfb`、`PendingOwnerReviewAndNotice / distributionApproved=false`；签名和 RC ValidateOnly 继续 `BlockedPendingApprovedPublisherCertificateAndManagedSigningProvider / signed=false / installable=false / distributionApproved=false`。

首次组合测试命令试图递归清理旧 `TestResults`，在创建进程前被本地执行策略拒绝；没有文件被删除，也没有产生测试结果。Correction 是保留旧证据并改用新的 `TestResults-Stage224` 目录，随后上述完整测试和覆盖率一次通过。这个差异属于测试结果目录编排，不计为产品或 Action 回归。

## 4. 开发目标与需求对齐审计

开发目标：把“Dependabot 已发现但未批准”收敛为“官方身份已复核、清单已同步、真实执行结果可证明”的受控升级。

需求对齐：升级不修改产品运行时代码、用户文件或包身份，不新增 workflow 权限、registry、secret、environment、OIDC、Publisher、签名、安装或分发能力。#19/#20/#23/#24/#274 的外部证据和负责人输入状态不因工具升级改变。
