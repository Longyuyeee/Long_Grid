# Stage 151：M4c2c 只读环境预检门禁审计

- 审计日期：2026-08-15
- 开发基线：`main@67843fa3129c265710c2d3af6f7daf31a88937a6`
- 对应计划：Stage 150 / G0-01a
- 当前判定：**Environment Preflight Engineering Pass / 当前主机 Rejected / 真实 24 小时 Pending**

## 1. 需求与缺口

Stage 149 已证明当前主机不是合规 24 小时环境，但此前正式会话只要求操作者确认专用账户、匿名工作区和恢复计划，并检查证据目录为空、Long方格进程为零。桌面内容、电源睡眠、待重启、证据目录边界和仓库干净状态仍依赖人工复核，容易在不同系统语言和机器之间产生不一致判断。

本切片只补 G0-01 的只读执行前门禁，不准备或修改环境，不启动真实会话，不产生 M4c Pass。

## 2. 实现范围

新增 `eng/Test-LongGridResourceStabilityEnvironment.ps1`：

- `-ValidateOnly` 只用合成有效/无效快照验证接受与拒绝路径，不读取现场环境；
- live 预检要求专用账户、匿名工作区、恢复计划、持续供电和无自动重启五项明确确认；
- 只输出用户/Public Desktop 条目计数，不输出名称或路径；两者均必须为空；
- 使用 `powercfg` 的十六进制设置索引语言无关地复核 AC/DC 睡眠均为 0；
- 检查 CBS、Windows Update 和待重命名操作的有限待重启信号；
- 证据目录必须已存在、为空、非重解析点、位于仓库外且至少有 1 GiB 可用空间；
- 检查 `LongGrid.App` / worker 为零、Git 工作树干净和交互会话存在；
- 只输出有限失败码和布尔/计数，不输出路径、文件名、内容、PID、账户或机器身份；
- 不创建/删除账户，不改变电源/更新策略，不清理桌面，不启动/关闭 VM 或产品进程。

正式 `Start-LongGridResourceStabilitySession.ps1` 新增持续供电和无自动重启确认，并在任何构建或产品启动前强制调用预检。返回非零即停止，不能只运行旧入口绕过。

## 3. 验收目标

- 有效合成环境得到零失败码，20 个单条件无效环境逐一映射到唯一固定失败码；
- 桌面非空与 AC 睡眠开启的合成环境精确得到两个固定失败码；
- CI 独立执行预检 `-ValidateOnly`；
- 正式会话合同声明 `requiresEnvironmentPreflight=true`；
- 当前主机用仓库外空临时目录实测必须得到 `RejectedEnvironment`，且不写入该目录、不改变系统；
- 采集/复审合同仍为 Pending，`canProduceM4cPass=false`。

本轮现场实测返回退出码 `2` 与 `RejectedEnvironment`。环境失败码为用户/Public Desktop 非空、AC/DC 睡眠未关闭和存在待重启信号；开发中的未提交改动另触发 `RepositoryMustBeClean`。证据目录在预检前后均为零条目，App/worker 进程数为零。该结果只证明拒绝门禁工作，不能作为 G0-01 的 24 小时证据。

## 4. 边界与下一步

环境预检 Engineering Pass 不代表当前主机合格。下一步仍是由项目负责人提供无个人内容的专用账户或可抛弃 VM，在会话外关闭 AC/DC 睡眠和自动重启计划、准备空证据目录，然后重新运行 live 预检。只有 `ReadyForM4c2cSession` 才能从同一 commit 启动完整 24 小时会话。

任何预检失败都只是环境拒绝，不是产品 Fail；不得删除失败码、降低目录/电源条件或手工修改输出进入下一步。
