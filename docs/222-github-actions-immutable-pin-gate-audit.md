# Stage 222：GitHub Actions 不可变提交固定门禁审计

> 日期：2026-08-28
> 结论：**CI 与 CodeQL 的全部 7 个远程 Action 调用已从可移动 major 标签收紧为完整 40 位提交 SHA，并由受审清单、消费者范围和真实负向漂移测试共同失败关闭**

## 1. 目标与初始事实

Stage 221 建立双语言 CodeQL 后，真实复读 `.github/workflows` 发现 7 个远程执行入口仍为 `actions/checkout@v6`、`actions/setup-dotnet@v5`、`actions/upload-artifact@v6` 和 `github/codeql-action/*@v4`。major 标签便于升级，但不是不可变执行身份；工作流审查无法仅凭仓库提交确定实际运行的 Action 代码。

本切片只固定普通 CI/CodeQL 供应链，不增加 workflow 权限，不读取 secret，不接入 OIDC、签名、安装或分发。目标也不包含自动追随上游标签；升级必须单独解析官方 ref、审查提交、更新清单并通过完整 CI。

## 2. 官方 ref 解析与实现

通过 GitHub 官方仓库 API 在 2026-08-28 解析当前 major ref：

| Action | Major ref | 固定执行 commit |
| --- | --- | --- |
| `actions/checkout` | `v6` | `d23441a48e516b6c34aea4fa41551a30e30af803` |
| `actions/setup-dotnet` | `v5` | `26b0ec14cb23fa6904739307f278c14f94c95bf1` |
| `actions/upload-artifact` | `v6` | `b7c566a772e6b6bfb58ed0dc250532a479d7789f` |
| `github/codeql-action` | `v4` | `cdf488f595d80d6e07e03d4674febd5ab45fa938` |

CodeQL 的 `v4` ref 是 annotated tag；API 首先返回 tag object `fddeee1a...`，再解引用到实际 commit `cdf488f...`。workflow 固定的是执行 commit，不是 tag-object SHA。行尾保留 `# v4/v5/v6` 只用于可读的升级系列提示，不参与执行解析。

`.github/actions-pins.json` 保存 5 个批准 target、commit、major 系列和精确 consumer workflow；`eng/Test-LongGridWorkflowActionPins.ps1` 扫描全部 `.yml/.yaml` 并拒绝：

- 远程 Action 使用标签、分支或短 SHA；
- 完整 40 位但不等于受审 commit 的引用；
- 未列入清单的远程 Action；
- target 的 workflow 消费者增加、遗漏或重复；
- 同一 repository 的不同 sub-action 被固定到不同 commit；
- 未按 digest 固定的 `docker://` Action。

CI 与 CodeQL 都在 pinned checkout 后立即执行该合同。CodeQL 原有合同同步要求 `init/analyze` 为 40 位 commit 且保留 `# v4`，双语言 manual build 和最小权限断言不变。

## 3. Expected / Actual / Difference / Correction

Expected：2 个 workflow 中所有远程执行入口均可由当前仓库内容唯一复读；恢复可移动标签或替换成未经批准的 40 位 SHA 必须失败；Windows PowerShell 与 pwsh 结论一致。

Initial Actual：2 个 workflow、5 个 Action target、7 个调用全部使用可移动 major 标签，固定调用数为 0。Difference 是 CI/CodeQL 的工具版本系列受审，但实际执行提交仍可在不改本仓库的情况下变化。

Correction：全部 7 个调用改为上述完整 commit，并建立清单/消费者合同。正向测试在 Windows PowerShell 与 pwsh 均返回 `workflowCount=2 / approvedActionTargets=5 / pinnedRemoteUsages=7`。四个内存负向变体分别恢复 `checkout@v6`、使用全零 40 位 SHA、注入 `example/unapproved` 和重复 checkout consumer，实际依次得到 `mutable-ref`、`unapproved-pin`、`unapproved-action` 与 `consumer-drift`。负向变体都未写回 workflow。

官方 ref 查询包装命令第一次因 PowerShell `foreach` 结果直接接管道而在解析阶段失败，未切分支或修改文件；括号化收集结果后成功解析。该差异只属于审计命令编排，不改变固定值来源。

固定 commit 上的 `checkout/action.yml`、`setup-dotnet/action.yml`、`upload-artifact/action.yml`、`codeql-action/init/action.yml` 与 `analyze/action.yml` 均已由官方 Contents API 真实读取并返回非空 blob，避免只验证 commit 存在却遗漏 sub-action 路径。locked restore、格式、Release 全解决方案构建通过且为 0 warning / 0 error；正式原生 ExplorerCommand DLL 与 Probe 同样真实构建为 0/0。依赖漏洞门禁为 0 known vulnerable package，许可证元数据门禁仍为 20 projects / 30 packages、确定性 SHA-256 `7d7d7a...cacfb`，并保持 `PendingOwnerReviewAndNotice / distributionApproved=false`。

完整测试首次 Expected 为 `1,381/1,381`，Actual 为 `1,380/1,381`：真实强杀子进程测试 `RealKilledChildLeavesDurableRecoveryJournal` 在 10 秒内期望 journal 为 `Applied`，实际一次读到 `Staged`。没有提高超时、放宽断言或修改不相关产品代码；关闭 build server 后该真实测试独立串行三次均通过（每次约 1–2 秒），再关闭 build server 并顺序重跑完整覆盖率套件，Actual 为 `1,381/1,381`、0 跳过，lines `90.41% (47076/52072)`、branches `76.17% (15460/20298)`。Difference 判定为未形成稳定复现的全套件负载时序差异；若 hosted runner 重现，必须升级为测试握手缺陷而不是继续重跑。

完整产品/安全探针、unsigned RC 以及双语言 CodeQL 的最终结果仍以本 PR 最新检查为准；不能用本地合同 Pass 代替 hosted runner 实际加载 pinned Action。

## 4. 开发目标与需求对齐审计

开发目标审计：远程 Action 从“只固定 major 系列”收紧为“仓库提交可唯一确定执行 commit”，并具备可复现的正向与两类负向漂移证据，完成。

需求对齐审计：本切片没有修改产品运行时代码、用户文件、包身份、许可证、Publisher、environment、secret、OIDC 或分发状态；CI 权限仍为 `contents: read`，CodeQL 仍只额外拥有 `security-events: write`。固定 SHA 也不表示上游永远可信，后续升级仍需审查官方 commit 与完整回归。#19/#20/#24、#23 和 #274 状态不变。
