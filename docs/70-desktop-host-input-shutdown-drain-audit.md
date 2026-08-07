# Long方格 DesktopHost 输入与关闭排空审计

> 审计日期：2026-08-07
>
> 范围：产品自有 DesktopHost 窗口输入关闭、重开、紧急隐藏、有界 shutdown drain
>
> 结论：RC 硬化切片 5 已完成内部生产适配器与自动化边界；App 继续零接线，真实执行仍受外部准入阻断

## 1. 审计结论

上一切片已经建立配置与窗口的复合事务、真实窗口 Bounds 适配器、目标 UI 线程封送和生命周期失效 guard，但 `IProductWorkspaceCompositeInputGate` 仍由测试替身实现。也就是说，协调器虽然知道何时关闭、重开或隐藏输入，却没有一个只接受产品自有窗口、能够在原生调用前再次验权的生产实现。关闭时同样缺少“先拒绝新操作、等待在途操作、超时可重试、隐藏成功后才能释放”的明确状态机。

本切片补齐两个内部组件：

- `WindowsProductDesktopHostInputController`：仅负责已验证句柄的 `EnableWindow` / `ShowWindow(SW_HIDE)` 原生动作及结果复读；
- `ProductWorkspaceCompositeDesktopHostInputGate`：把精确容器集合、registry generation、DesktopHost 线程、生命周期 guard 和原生控制器组合成事务输入门，并提供有界、可重试的 shutdown drain。

两个组件均未在 `LongGrid.App` 构造或调用，当前产品仍不会移动、禁用或隐藏真实窗口。

## 2. 所有权与线程边界

输入门构造时必须同时满足：容器集合非空、无重复并与注册表完整集合完全一致；registry generation 等于当前 generation；全部窗口通过存在性、进程、线程和实例标识复核；prepared batch 的宿主线程等于 dispatcher 目标线程；生命周期 guard 仍为 `Ready`。

每次关闭、重开或隐藏都执行两阶段检查：

1. 调用线程只准备绑定 bridge ID、generation、claim 和目标线程的 batch，不持有 HWND；
2. 工作封送到目标 DesktopHost UI 线程后，bridge 在注册表串行边界内重新验证完整集合与所有权，然后才把句柄交给原生控制器。

因此，排队期间发生注册表刷新、宿主换代、句柄消失或所有权漂移时，原生调用有限失败，不会使用陈旧 HWND。Core 与 App 均不获得句柄。

## 3. 失败安全语义

| 操作 | 成功状态 | 失败安全动作 |
| --- | --- | --- |
| Close | 全部窗口复读为 disabled | 若出现部分关闭，先全量恢复 enabled；恢复仍不完整则隐藏全部宿主 |
| Reopen | 全部窗口复读为 enabled，且 guard 在执行前后仍为 `Ready` | 任一窗口未重开即隐藏全部宿主，协调器仍会记录 `InputReopenFailed` 并再次请求紧急隐藏 |
| Hide | 全部窗口复读为不可见 | 返回有限失败，保留 gate 以便重试，不宣称已经安全释放 |

`ShowWindow` 只存在于该专用输入控制器，固定使用 `SW_HIDE`。布局批处理仍禁止 `ShowWindow`、激活、Z-order 和 Region 修改；输入控制器也没有 Show、激活或第三方窗口枚举能力。

## 4. 有界 shutdown drain

`ShutdownAndHide` 的顺序固定为：

1. 在 gate 状态锁内调用 `lifecycle.BeginShutdown()`，永久禁止 binding 交换和输入重开；
2. 拒绝新的 Close/Reopen/Hide 请求；
3. 最多等待 1 ms 至 5 s 的调用方指定排空期限；
4. 若在途操作未结束，返回 `DrainTimedOut`，不释放 gate 或 DesktopHost；
5. 排空后在同一 verified-window/目标线程边界隐藏全部宿主；
6. 隐藏失败返回 `HideFailed` 并允许重试；只有 `Hidden` / `AlreadyHidden` 才是关闭完成。

dispatcher 自身保持既有规则：queue timeout 只取消尚未开始的工作；已经 Running 的原生调用必须取得真实结果，禁止“先返回超时、稍后再改变窗口”。`Dispose` 会拒绝仍在运行或 shutdown 后尚未隐藏完成的 gate，防止调用方把未完成关闭误当成收口。

## 5. 自动化矩阵

新增测试覆盖：

- 精确句柄集合的关闭、重开与紧急隐藏；
- 生命周期漂移后禁止重开且不触发原生调用；
- dispatcher 排队期间 registry generation 改变时二次验权失败；
- 打开状态直接 shutdown 隐藏、重复 shutdown 幂等；
- 在途 Close 导致 drain 超时、排空后安全重试；
- 隐藏失败的有限状态与后续重试；
- 陈旧 registry generation 与错误目标线程拒绝构造；
- Win32 控制器成功路径、部分关闭回滚、回滚失败隐藏、重开失败隐藏和无效句柄拒绝。

`eng/Test-LongGridUi.ps1` 同步增加源代码契约：原生输入能力只能出现在专用控制器；gate 必须包含精确 prepared/revalidated batch、目标线程、生命周期 shutdown、排空等待与有限超时；App 必须继续零接线。

本地最终验证结果：Debug 与 Release 均为 512/512 测试通过，构建均为 0 警告、0 错误；Debug 覆盖率为行 90.76%（16556/18242）、分支 82.70%（4026/4868），Release 为行 91.60%（14102/15396）、分支 81.48%（3976/4880）。格式、118-ID UI 契约、单实例契约、启动链、配置持久化 100 次矩阵、文件操作安全、DesktopHost Unicode/UIA smoke 和依赖漏洞门禁均通过。

PR 首次远端运行的 restore、format、build、启动链、四组外部会话预检、DesktopHost smoke、UI 契约和单实例契约均成功，但带覆盖率的 `Test` 步骤超过 10 分钟没有结果或失败断言；相比同一提交本地两次约 20 秒内完成的 collector，可判定为已知 collector 孤立挂起观察。该运行被明确取消且不计为通过，补充记录后从新提交启动干净 CI；只有后续完整运行成功才允许合并。

缩略图 worker 矩阵在本机仍报告既有 `Verdict: Fail`：零能力 AppContainer 对受控样本统一 AccessDenied，500 次 stress 全部失败且超出临时预算；清理、ACL 恢复和隔离断言通过。该结果不属于本输入切片的改动，也未被改写为通过，继续作为既有外部/提供程序兼容 blocker 保留。

## 6. 需求对齐与剩余方向

| 初始需求 | 本切片对齐 | 当前边界 |
| --- | --- | --- |
| 桌面分组与布局恢复 | 为真实产品容器补齐事务期间输入隔离和失败隐藏 | 尚未开放 App 真实执行入口 |
| 桌面文件整理 | 不读取、不移动、不删除桌面文件 | 文件安全矩阵不变 |
| 任务栏美化 | 未进入本切片 | MVP 后续独立模块，不注入 Explorer/taskbar 私有结构 |
| 自定义窗口效果 | 仅控制产品自有宿主的 enabled/hidden 安全状态 | 不修改第三方窗口、Region、激活或 Z-order |
| 现代 UI 与平滑动效 | 无视觉回归，Reduced Motion 契约不变 | 本切片为基础设施硬化 |
| 小组件与 Long助手插件 | LPWP 1.0 文档与隔离边界不变 | 运行时仍未接入 |

布局恢复内部工程链现已具备真实拓扑、产品窗口注册表、配置/窗口复合事务、原生 Bounds 批处理、原生输入控制、目标线程封送、生命周期失效和关闭排空。下一阶段不应继续扩张内部抽象，而应处理干净会话 118-ID UIA、一键打包/安装验证和 RC 审计，并由负责人按 GitHub #19、#20、#23、#24 提供人工、硬件或专用卷证据。全部 blocker 清零并单独批准前，App 仍不得接线真实窗口执行。
