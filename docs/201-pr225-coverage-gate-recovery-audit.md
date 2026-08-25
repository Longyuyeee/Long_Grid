# Stage 201：PR #225 覆盖率门禁恢复审计

日期：2026-08-25
开发项：Gate A / 长期功能分支主线集成
结论：**Local Coverage Gate Pass / PR CI Required**

## 1. 远端真实差异

PR #225 第三次 CI run `32798585694` 已通过 Windows PowerShell 5.1 编码、157-ID 批量无障碍、157-ID clean-session、构建和 1163/1163 测试，但在既有覆盖率门禁停止：

| 项目 | Expected | CI Actual | Difference |
| --- | ---: | ---: | ---: |
| 行覆盖率 | ≥90.00% | 87.89%（39558/45008） | -2.11 pp / Fail |
| 分支覆盖率 | ≥75.00% | 73.22%（12720/17372） | -1.78 pp / Fail |

覆盖率附件按 package、class、method 和源码行重新解析后确认：这不是报告缺失或阈值计算错误。分支相对 `main` 累计 42 个提交、约 1.8 万行 Core/Infrastructure/测试变更，新增正式 DesktopHost 窗口、系统事件、激活、布局、缩略图与安全打开路径没有在每个切片重新执行完整覆盖率门，形成了真实的长期分支质量债务。

## 2. 修正原则

- 不降低 90% 行、75% 分支门槛；
- 不排除正式 Windows 原生适配器，也不删除失败断言；
- 测试执行真实 HWND、真实 Win32 消息分派和真实 Windows 系统采样；
- 对无法稳定伪造的系统回调，把来源观察与有限业务映射拆开，使用受控 sampler/显式 evidence 入口验证同一生产逻辑；
- evidence 入口只为 `InternalsVisibleTo` 测试程序集可见，不进入 App 公共 API，不绕过运行时的来源校验。

## 3. 实现与测试增量

1. `WindowsProductDesktopInteractionSystemSurfaceEventSource` 支持受控采样周期，并把 SessionSwitch/PowerMode 映射抽成同一生产方法；测试覆盖启动幂等、采样、恢复、会话/远程/电源/焦点、sampler 失败、订阅者异常、释放和真实 Windows sampler。
2. `WindowsProductDesktopHostReadOnlySurface` 增加内部消息 evidence 入口；真实 HWND 依次执行 passive/explicit/hidden 的 hit-test、鼠标、滚轮、双击、取消、捕获、热键、绘制和键盘布局焦点矩阵。
3. `WindowsProductDesktopInteractionActivationSource` 将已通过原生来源门的键盘命令路由抽为同一核心方法；真实激活 HWND 覆盖项目导航、Enter 打开、Tab 标题焦点、Alt/Shift 布局、Escape 取消、Ctrl+A 和未知键。
4. 没有改变桌面文件、用户配置、输入注入边界、缩略图 1.5 秒预算、签名或分发权限。

## 4. 逐轮 Expected / Actual / Difference

| 轮次 | Expected | Actual | Difference |
| --- | --- | --- | --- |
| CI 基线 | ≥90% / ≥75% | 87.89% / 73.22% | Fail |
| 系统事件矩阵 | 比基线提高 | 88.67% / 73.89% | +0.78 / +0.67 pp，仍 Fail |
| DesktopHost HWND 消息矩阵 | 比上一轮提高 | 89.66% / 74.79% | +0.99 / +0.90 pp，仍 Fail |
| 激活键盘矩阵 | 分支通过、行继续提高 | 89.93% / 75.22% | 分支 Pass；行 -0.07 pp |
| 最终真实系统采样与布局焦点 | ≥90% / ≥75% | 90.05%（40576/45058）/ 75.34%（13094/17380） | +0.05 / +0.34 pp，Pass |

最终同一次 Release collector 为 1170/1170 通过；不是把多次失败报告相加后制造百分比。

## 5. 缩略图真实测试差异复核

一次全量覆盖率运行中，`RealProductQueueRequestsOwnedBitmapThenDisablesAndCleansProfile` 预期 `ReadyThumbnail`、实际 `FailedFallback`，该运行明确记为 1168/1169 Fail，未计作通过。随后：

- 同用例独立覆盖率复现：1/1 Pass，实际约 884 ms；
- 连续五个全新测试进程、每次启用 XPlat Coverage：5/5 Pass，实际测试耗时 830～908 ms；
- 最终完整 Release collector：1170/1170 Pass。

因此当前证据把单次差异保留为未复现的瞬时 worker fallback，不修改 1.5 秒预算，也不以重跑删除失败事实。若 PR runner 再现，必须增加匿名有限 failure kind / round-trip 诊断并按真实差异修正，不能继续靠重跑。

## 6. 需求对齐与下一步

本切片只恢复 Gate A 的质量门，没有扩张任务栏、小组件、文件移动或窗口特效范围。PF-001～PF-005 仍为 `EngineeringComplete / ProductEvidencePending`，PF-006 仍为 `InProgress`，下一功能切片仍是 PF-006C1 PageUp/PageDown。

推送后必须由 PR #225 的全新 Windows runner 重新执行完整流水线。只有 PR 全绿、合入 `main` 且 main CI 通过，Gate A 才能关闭并进入 PF-006C1。
