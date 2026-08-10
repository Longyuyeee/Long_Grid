# Long方格干净会话 UIA 链路审计

> 审计日期：2026-08-07
>
> 范围：130-ID 源码合同、真实 UIA 启动/关闭、单实例重定向、残留进程污染
>
> 结论：干净会话执行链和 CI 合同已建立；当前本机会话因无权限管理的既有无窗口进程而保持 Pending，不记为 live Pass

> 2026-08-10 增量：批量引用加入切片新增一次撤销按钮 1 个 AutomationId；当前权威源码合同、执行说明和 `Test-LongGridCleanSession.ps1` 已更新为 130。下文出现的 121/125/127/129-ID 是历史证据，当前执行值以 130 为准。

## 1. 缺口与根因

`Test-LongGridUi.ps1` 当前维护 121 个唯一 AutomationId，并通过真实 WinUI 窗口验证导航、响应式布局、主题、匿名整理预览和布局恢复语义。旧脚本启动前没有检查已有 `LongGrid.App`。由于产品采用固定 AppInstance key，若会话残留一个无主窗口的主实例，新测试进程会把激活转发给它并退出，随后测试只能报告“新进程没有主窗口”。这不是 UIA 节点缺失，也不能通过终止未知进程来伪造干净会话。

本轮本机只读检查确认存在 PID 39208：SessionId=1、创建于 2026-08-04、MainWindowHandle=0；当前权限无法读取其所有者、路径或命令行。用户此前已明确没有终止许可，因此该进程未被结束或修改。

## 2. 新的干净会话合同

新增统一入口 `eng/Test-LongGridCleanSession.ps1`：

1. live 模式启动前要求当前会话中 `LongGrid.App` 进程数为 0；
2. 执行 UIA 源码合同，要求完整 121-ID 集合；
3. 启动准确构建产物，以准确 PID 附着真实 UIA；
4. 验证 wide → compact 720 → wide、五项导航、主题往返、匿名整理和恢复语义；
5. 正常关闭脚本自己启动的 UI 进程，并要求剩余进程数为 0；
6. 再执行单实例主进程、次进程重定向、退出和窗口恢复矩阵；
7. 最终再次要求剩余进程数为 0，输出有限 JSON 证据。

脚本只会在 `finally` 中关闭或强制终止自己记录的准确 PID。发现外来进程时立即拒绝，不读取桌面、不修改配置、不提升权限，也不调用 Stop-Process。

## 3. 121-ID 判定边界

当前 121-ID 是完整 XAML 源码合同：每个 ID 必须恰好出现一次，并具备既有语义、状态与事件绑定。live UIA 则验证默认安全会话中实际可达的页面和状态路径。部分按钮只会在配置损坏、保存失败或真实可确认恢复时显示；干净默认会话不会人为破坏用户配置来强制它们出现。因此不会把“121 个节点在同一默认 Raw View 同时可见”写成虚假要求，而是组合以下两种证据：

- `requiredAutomationIds=121`：完整、唯一、可由 CI 复读的源码合同；
- `mode=live`：真实进程、真实窗口和真实 UIA Pattern 的安全状态路径。

配置损坏、保存失败、Narrator、高对比和真实恢复确认仍由各自受控矩阵负责。

## 4. CI 与失败语义

CI 新增 `Validate clean-session UIA chain`，使用 `-ValidateOnly`：

- 不启动窗口或进程；
- 复核 121-ID 合同；
- 复核 UIA 脚本具有启动前零进程、关闭后零残留和“不终止外来进程”源码边界；
- 复核单实例脚本具有关闭后零残留断言；
- 输出 `PendingCleanInteractiveSession`，不会冒充 live Pass。

本机会话的负向验收已经通过：执行 live 入口返回非零；Before/After 均只有 PID 39208；`ForeignProcessPreserved=true`。该证据证明污染被有限拒绝，不证明 UIA live 已通过。

本地质量门禁结果：Release 构建 0 警告、0 错误；Debug/Release 均为 512/512 测试通过。Debug 覆盖率为行 90.78%（16560/18242）、分支 82.70%（4026/4868）；Release 为行 91.61%（14108/15400）、分支 81.53%（3982/4884），均超过仓库 90%/75% 门槛。格式、启动链、原 UI 合同、单实例合同和新增干净会话合同均通过。

## 5. 需求对齐

| 初始需求 | 本切片对齐 | 当前边界 |
| --- | --- | --- |
| 现代、平滑、可用 UI | 121-ID、响应式和主题真实 UIA 链统一收口 | Narrator、视觉质量和真实输入仍需人工矩阵 |
| 一键启动 | 继续复用 `Start-LongGrid.ps1` 的锁定恢复/构建链 | 受污染会话不会强行抢占或杀进程 |
| 一键打包 | 明确列为下一切片 | 当前项目仍为 unpackaged，不能宣称安装包就绪 |
| 桌面整理 | 只验证匿名/只读 UI 语义 | 不读取、移动或删除真实桌面文件 |
| 自定义窗口与 DesktopHost | 保持 App 零接线 | 不开放真实窗口执行入口 |
| 任务栏、小组件和 Long助手插件 | 范围不变 | 仍是 MVP 后续模块 |

## 6. 后续方向

获得可管理的干净交互会话后，应运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Test-LongGridCleanSession.ps1 `
  -Configuration Release
```

只有 JSON 返回 `mode=live`、`requiredAutomationIds=121`、首尾均为零进程且 `outcome=Pass`，才可关闭本项 live 证据。一键 publish、可重复压缩包、哈希清单、unsigned MSIX、SPDX 2.2 和内部 RC 聚合入口已由后续切片完成；签名、升级、卸载、多用户与商店分发仍需独立决策，不应在无证书和渠道批准时伪造正式安装能力。
