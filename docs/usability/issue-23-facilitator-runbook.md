# Issue #23 五人测试主持人运行手册

状态：**Ready to schedule / Results Pending**

本手册只规定安全、可复读的执行方法，不提供任务答案，也不代表测试已经完成。任务文本、记录表和通过门槛以[五人可用性测试计划](issue-23-first-organization-test-plan.md)为准。

## 1. 会话前

1. 准备五位未参与本功能设计的参与者，随机分配匿名标签 `P1`–`P5`；标签不得与姓名、邮箱、账户或工号建立仓库内映射。
2. 在同一已审计 commit 上执行全部会话；若中途更换 commit，停止并重新开始完整五人样本。
3. 记录 Windows build、缩放/分辨率、输入方式、主题、执行日期和主持人代号；不要记录设备序列号、显示器硬件标识或参与者身份。
4. 关闭可能显示通知或隐私内容的其他应用。测试不需要打开 Explorer，也不得使用参与者真实桌面文件。
5. 先执行预检：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-Issue23UsabilitySession.ps1 `
  -ValidateOnly
```

预检只验证入口和隐私合同；输出 `ResultsPending` 才是正确结果。

## 2. 启动独立会话

每位参与者使用全新进程。例如 P1 使用键鼠和系统主题：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-Issue23UsabilitySession.ps1 `
  -ParticipantId P1 `
  -InputMode KeyboardMouse `
  -Theme System `
  -Configuration Release
```

允许的参与者标签只有 `P1`–`P5`，输入方式只有 `KeyboardMouse`、`KeyboardOnly`、`Touch`，主题只有 `System`、`Light`、`Dark`。启动器把匿名环境清单输出到控制台，不自动写结果文件、不截屏、不枚举桌面。

## 3. 主持纪律

- 逐字读出测试计划中的目标，不说控件名称、导航位置或推荐路径；
- 从目标读完开始计时；记录首次错误动作、完成时间和提示次数，不能只记最终成功；
- 参与者询问“下一步点哪里”时，只回答“请按你理解继续”，并记录一次提示请求；
- 任何真实桌面访问、文件确认、外部副作用或参与者认为文件已经被移动/删除的情况，立即停止并记录 `Critical`；
- 不录屏、不录音、不拍摄桌面，不复制 UIA、诊断或终端中的本机路径；
- 不在会话之间复用进程。参与者结束后关闭 Long方格，确认进程退出，再启动下一标签。

## 4. 会话后

1. 将结果手工填入测试计划的 P1–P5 表格；备注只写行为和误解，不写身份或真实文件信息。
2. 五人全部完成后计算中位时间和 5/5 安全判断，不把 4/5 四舍五入成 95%。
3. `Critical` 或 `High` 必须先修复并重测；不同 commit 的结果不得拼成同一个五人样本。
4. 完成隐私复核后，把匿名汇总、缺陷链接和负责人决策写回 Issue #23。
5. 在真实结果写回前，仓库状态必须保持 `Results Pending`，不得因为预检、CI 或 UIA 通过改写为 Pass。

## 5. 主持人停止检查表

- [ ] 只使用 P1–P5 标签；
- [ ] 每人一个全新进程；
- [ ] 没有姓名、账户、路径、文件名、截图、录音或录屏；
- [ ] 每项记录首次错误、耗时和提示次数；
- [ ] 安全误解按严重度记录；
- [ ] 五人结果来自同一 commit；
- [ ] 结果仍未产生时保持 `Results Pending`。
