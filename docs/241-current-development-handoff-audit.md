# Stage 241：当前开发状态、换机接续与证据传递审计

日期：2026-08-30

输入基线：`origin/main@99ee050e1b9760a5ef57435e8dd98db617fbd57a`

状态：`HandoffPrepared / RemoteAuditPending / ProductEvidenceBlocked`

## 1. 审计范围与结论

本轮为更换开发电脑重新复读最新主线、统一计划、30 项 PF 总表、Stage 240、远端 CI/CodeQL、开放 Issue，以及本机 Runtime、BOX/M1 合同、进程和临时证据目录。没有根据聊天记忆补写完成项，也没有安装 Runtime、启动产品、发送输入、删除历史目录或修改系统状态。

结论：当前不是“全部开发即将收尾”，而是 **M1 桌面盒子与文件夹绑定核心的工程实现基本完成，真实物理产品验收被外部环境阻断**。M2 任务栏只完成安全工程前半段，M3～M5 尚有大量产品功能和正式分发工作。

## 2. 当前完成度

| 口径 | 当前事实 | 换机后解释 |
|---|---|---|
| 严格顶层里程碑 | M1/M2 `0/2 Complete` | 任何工程测试、截图或负向阻断都不能代替两分钟真实旅程和任务栏恢复矩阵 |
| 30 项 PF | `0 Complete` | PF-001～PF-007 为 `EngineeringComplete / ProductEvidencePending`，PF-040 仅单文件夹 Core 完成 |
| M1 工程 | PF-001～PF-007、BOX-R1-A/B、FOLDER-R1-A～D、UI-R1 工程链基本完成 | 下一工作不是重写 Core，而是满足准入后完成 BOX-R1-C/D 与完整物理旅程 |
| M2 工程 | TASKBAR-R1A～R2B1-A2 已完成安全探测、恢复边界、环境准入和有限预设 | R2B1-B 原生效果、R3 恢复、R4 build/显示器/高对比/卸载矩阵仍未完成 |
| M3～M5 | 视图/完整排序、自动整理、Quick-hide/Peek、Portal/Tab/快照、多显示器档案、工作空间、Widget/插件、正式交付仍待开发 | 不得因 M1 工程量大而把全产品写成接近完成 |
| 工程门禁 | `1,394/1,394`，最终 main coverage lines `90.12%`、branches `76.04%`，漏洞 0，C#/C++ CodeQL 成功 | 证明当前代码基线稳定，不证明物理用户旅程或正式分发完成 |

全范围工作量只能作为规划估算而不是状态字段：按统一计划 M1～M5 与 30 项 PF，当前约完成 `25%～35%`，仍有约 `65%～75%`；若只看 M1 工程实现约为 `85%～90%`，但包含签名安装和真实验收的 M1 仍约为 `60%～70%`。

## 3. 当前机器实际验收快照

| 检查 | Actual | 结论 |
|---|---|---|
| Runtime schema 5 | Framework `2.4.0.0`、XAML `3.2.3.0`、Singleton `8002.4.0.0` 可发现；Main.2 `>=2.3.1.0` 与 DDLM `2.3.1.0-x6` 缺失 | `IncompleteRuntimePackageSet / BlockedByIncompleteRuntime` |
| BOX-R1 合同 | 版本化有限命令、freshness、Initial/Redirected dispatch 均为真 | `Difference=None / Pass`；只证明合同，不证明安装后 Explorer 菜单 |
| M1 ValidateOnly | 配置隔离为真，`startsProcess=false / drivesUserInput=false` | `Pass`；只证明入口合同 |
| M1 ExternalAutomation | `startsProcess=false / createsEvidenceSession=false` | 按真实 Runtime 缺项安全阻断 |
| 副作用 | LongGrid 进程 `0→0`；M1 临时根 `0→0` | 未启动产品、未创建 M1 会话 |
| BOX 临时根 | 复测前后均有一个 32 字符 GUID 空目录，非重解析点、子项 0，最后写入早于本轮 | 历史空残留，不是证据；本轮不删除，也不迁移 |

这些是当前电脑的时间点事实。新电脑即使系统版本相同，也必须重新运行预检；不得把包版本、XAML 风险、进程数或临时目录状态照抄为新机器 Actual。

## 4. 可以通过 GitHub 传递的证据

- 最终 `main` 中的产品代码、测试、工程脚本、工作流和全部审计文档；
- Stage 240 的 Runtime 预检宿主/准入修复与真实 PATH 投毒回归；
- CI run `33267750182`：`1,394/1,394`、coverage lines `90.12% (46,926/52,072)`、branches `76.04% (15,434/20,298)`、漏洞 0、SBOM 805 个文件、unsigned RC 否定性门禁；
- CodeQL run `33267750183`：C++/C# 均成功，main 开放告警 0；
- #23 许可证/产品决策和 #274 Publisher/托管签名方案两个开放责任入口；
- 本文记录的 Expected/Actual/Difference、命令顺序和停止规则。

## 5. 不应通过仓库传递或冒充的证据

- `%TEMP%` 下的 M1/BOX 会话目录、空 GUID 目录、截图、窗口句柄、PID、WER 或本机 Appx 清单；
- 当前机器安装的 Runtime、证书存储、Publisher 身份、PFX/P12、私钥、token、client secret 或 OIDC 凭据；
- unsigned MSIX 不能当作可安装/可分发产物，当前 RC 仍为 `signed=false / installable=false / distributionApproved=false`；
- 合同测试、源码断言、自动 UIA 或截图不能替代物理鼠标/键盘/Narrator/高对比和安装/卸载旅程；
- 当前不存在可迁移的 M1/BOX 正向 Pass 证据包。若新电脑产生证据，只提交匿名白名单报告和必要的哈希/运行引用，不提交用户路径、文件名或桌面截图。

## 6. 新电脑接续顺序

1. 克隆仓库并切到 `main`，执行 `git pull --ff-only origin main`；确认 `git status --short --branch` 为干净且本地 HEAD 等于 `origin/main`。
2. 安装 `global.json` 要求的受支持 .NET SDK；不要通过修改 `global.json` 或降低门禁适配机器。
3. 先执行只读合同：
   - `powershell -NoProfile -ExecutionPolicy Bypass -File eng/Test-LongGridBoxR1Activation.ps1 -ContractOnly`
   - `powershell -NoProfile -ExecutionPolicy Bypass -File eng/Start-LongGridM1ManualEvidenceSession.ps1 -ValidateOnly`
4. 执行机器事实预检：`powershell -NoProfile -ExecutionPolicy Bypass -File eng/Test-LongGridWinUiUiaRuntime.ps1`。保留完整 JSON 的 schema、Actual、Difference、Outcome，但不要发布安装路径或本机身份。
5. 运行 Release restore/build、专项与完整测试；要求格式、0 warning/error、全部测试、90%/75% coverage、漏洞、许可证、SBOM、签名否定性合同继续通过。
6. 只有同时满足以下条件才进入正向证据：完整兼容 Runtime 且不存在已知不安全 WinUI/XAML 组合；受保护签名 MSIX；无既有 Long方格进程的独占可丢弃 Windows 账户/VM；#23/#274 所需负责人输入已经到位。
7. 满足条件后依次执行 BOX-R1 `Initial / Redirect / DuplicateRedirect`，再完成 M1 两分钟旅程：桌面空白处创建盒子、绑定文件夹、观察加载/失效/恢复并排序/刷新/打开、Explorer 拖入、盒子间改归属并撤销、键盘/Narrator/高对比、Explorer 重启和卸载恢复。
8. 任一门禁失败时保留首次 Actual 并停止，不安装 unsigned 包、不强杀外来进程、不降低系统安全策略、不在日常账户试写任务栏，也不新增相邻探针伪造进展。

## 7. 唯一接续点与远端状态

当前唯一执行项仍是等待 BOX-R1-C/D 与 M1 完整物理旅程的外部准入。#23、#274 均为 OPEN，更新时间分别保持 `2026-08-28T03:51:37Z` 与 `2026-08-28T02:43:13Z`；审计时开放 PR 为 0。外部事实未变化前，只处理新发现且可由实际代码证明的回归、质量或安全缺陷，不继续增加 M1 邻接探针或提前扩张 M3/M4。

本文实现分支的 PR CI、CodeQL、合并提交和最终 main 结果将在远端实际完成后回填；在此之前状态保持 `RemoteAuditPending`。
