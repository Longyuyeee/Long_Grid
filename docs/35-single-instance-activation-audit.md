# Long方格单实例激活与参数转发审计

审计日期：2026-08-04

基线：`main` / `dc3dbc8` + 单实例激活增量分支

结论：**Single-instance activation pass / 关闭竞态与恢复 UI 仍需后续矩阵 / Issue #24 保持 OPEN**

## 1. 目标与需求对齐

本切片只关闭 Issue #24 中“完整单实例激活与第二实例参数转发”的首个产品接线门槛：

- 同一用户会话只保留一个 Long方格主 UI 进程；
- 第二进程不创建 XAML Application、窗口、配置 Store 或 DesktopHost；
- 第二进程把 Windows App SDK 提供的完整 `AppActivationArguments` 转发给已注册主实例后退出；
- 主实例在自己的 UI 调度队列中恢复最小化窗口并激活；
- 转发不新增文件写入、不读取桌面、不提升权限，也不解释未知参数为文件操作。

这与首发“本地、无账号、安全引用、默认不移动文件”的范围一致，但不代表命令行、文件关联、URI、插件或小组件激活协议已经定义。

## 2. API 与启动边界审计

Windows App SDK 的 WinUI 应用默认允许多实例。官方当前文档要求在任何窗口初始化之前判断实例，并通过 `DISABLE_XAML_GENERATED_MAIN` 替换 XAML 自动生成入口；若当前实例未取得固定 key，则必须等待 `RedirectActivationToAsync` 完成后退出。

实现采用固定 key `LongGrid.Main`：

1. 自定义 `[STAThread]` 异步 `Main` 初始化 WinRT；
2. 读取当前 `AppActivationArguments` 并调用 `FindOrRegisterForKey`；
3. 第二实例只执行异步转发和尽力前台激活，然后以 0 退出；转发失败返回非零且仍不创建竞争窗口；
4. 主实例先订阅 `Activated`，再启动 XAML；
5. 构造 `App` 前到达的激活只保存在进程内队列，App 接管后按到达顺序交付；
6. 正常关闭排空成功后，先退订并释放实例 key，再关闭窗口，降低新激活被转发给正在退出进程的概率。

## 3. UI 线程与窗口行为

`App.HandleActivation` 不访问转发线程上的 XAML 对象。窗口尚未创建时只设置内存态 pending 标记；窗口存在但当前线程不是 UI 线程时，通过该窗口的 `DispatcherQueue.TryEnqueue` 调度。最终处理只做两件事：若 `OverlappedPresenter` 处于 `Minimized` 则调用 `Restore`，随后调用 `Window.Activate`。

参数原样由 AppLifecycle 交付，但当前只读 Shell 不消费内容。这是刻意的安全边界：未来文件、URI、插件或小组件激活必须分别定义验证、长度预算、来源和权限合同，不能因为“已经转发”就默认执行。

## 4. 验证证据

- Release App 构建：0 警告、0 错误；
- CI 源码合同验证：生成入口已禁用、固定 key、完整参数转发、早到激活排队、UI 调度和最小化恢复均存在；
- 本机真实双进程验证：主窗口启动后被测试工具最小化，第二进程携带匿名探针参数启动，在 10 秒内以 0 退出；随后主窗口恢复，最终仅保留一个 `LongGrid.App` 进程；
- 测试只终止自身启动的进程；若检测到已有 Long方格进程则拒绝运行；
- 当前 App 仍无 `configurationSaves.EnqueueAsync`，本轮没有新增配置、桌面或诊断文件写入。

## 5. 未关闭风险与下一步

1. 在有真实待保存批次的 5 秒关闭排空窗口中密集启动第二实例，仍需专门竞态矩阵；
2. `RecoveredFromBackup` / `SafeMode` 恢复 UI 尚未接入；
3. 真实产品状态入队、保存失败提示与可恢复重试尚未批准；
4. I24-01/I24-02 真实专用卷证据仍为 Pending；
5. 文件关联、URI、通知、插件/小组件激活的 payload 合同尚未定义；
6. MSIX 身份、升级、注销和多用户/远程会话矩阵仍待安装阶段验证。

因此本切片是 **E3 / Production lifecycle slice pass**，Issue #24 继续保持 OPEN。下一项优先实现配置备份恢复/安全模式 UI；不得把本次匿名启动参数测试描述为文件、插件或小组件协议已兼容。
