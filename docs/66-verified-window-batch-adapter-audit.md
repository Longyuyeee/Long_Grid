# Long方格 verified-window 批处理适配器审计

> 审计日期：2026-08-06
>
> 范围：产品自有窗口精确集合、所有权复读、注册表串行化、原生 deferred batch、复合事务快照/恢复边界
>
> 结论：RC 硬化切片 1 已完成代码与定向自动化；App 继续零接线，真实桌面执行与实机证据仍未开放

## 1. 本切片解决的问题

复合事务协调器已经要求窗口层提供 `Capture / Apply / Verify / Restore / VerifyRestored`，但阶段 C 只有 Core 合同，没有能够安全解析产品 HWND 的 Infrastructure 实现。直接按容器 ID 查找窗口或把句柄缓存到 App 都会绕过宿主实例、窗口 generation 和句柄复用防线。

本切片新增 `ProductDesktopHostVerifiedWindowBatchAdapter`。它只从 `ProductDesktopHostWindowBridge` 的私有注册记录取得句柄；Core、App、计划、配置和 UI 均看不到句柄。适配器实现复合事务窗口层，但尚未由 App 构造或调用。

## 2. 精确集合与所有权门禁

每次 capture、apply 或 verify 均要求：

1. registry generation 为正且与当前快照完全一致；
2. 请求容器非空、唯一，并与注册表完整容器集合完全相等；
3. 注册表处于 ownership-attested 状态；
4. 对每个句柄即时复读窗口存在性、进程 ID、线程 ID、实例标识与有效 Bounds；
5. 整个复核及批处理回调位于注册表串行边界内，连接、断开、注册、注销和刷新不能穿插。

任一条件不满足均返回有限失败，不调用原生 mutator。这里故意不支持“尽力移动已找到的子集”，因为部分成功会破坏配置与窗口的复合一致性。

## 3. 原生批处理约束

Windows mutator 使用单个 `BeginDeferWindowPos / DeferWindowPos / EndDeferWindowPos` 批次。每个 placement 固定使用以下 flags：

- `SWP_NOACTIVATE`：不夺取用户焦点；
- `SWP_NOZORDER`：不改变窗口 Z 序；
- `SWP_NOOWNERZORDER`：不改变 owner 链的 Z 序；
- `SWP_NOSENDCHANGING`：不发送可被宿主改写的 `WM_WINDOWPOSCHANGING`。

适配器不调用 `SetForegroundWindow`、`ShowWindow`、`MoveWindow`、`SetWindowRgn`，不操作 Explorer、任务栏或第三方窗口。Begin、任一 Defer 或 End 失败都会作为本次 apply 失败返回，由上层复合事务负责补偿和复读。

## 4. 快照、验证与恢复

capture 保存按容器 ID 排序的真实 Bounds，并绑定 registry generation。快照仅接受创建它的适配器私有类型；释放、跨 generation 或空快照均不可 restore。apply 后的 verify 和补偿后的 verify-restored 都重新通过注册表读取真实窗口 Bounds，不把 mutator 返回值当作最终成功证据。

快照不包含路径、桌面文件或配置内容。句柄只存在于一次同步 Infrastructure 调用的内部记录中，不进入快照、日志、Core 或 UI。

## 5. 自动化审计

定向测试覆盖：精确集合成功捕获、最新 Bounds、陈旧 generation、子集、重复容器、所有权漂移、规范化批次与注册句柄、无效 placement、native 失败、真实 Bounds 不匹配、恢复与复读、已释放/异源快照、注册表并发串行，以及 Begin/Defer/End 的 flags 与三类失败。

`eng/Test-LongGridUi.ps1 -ContractOnly` 额外强制：适配器必须实现复合窗口层并使用 deferred batch；禁止激活、显示、Region、直接 `SetWindowPos`；App 不得引用适配器或 mutator。AutomationId 数量保持 118。

本地全量结果为 461/461 测试通过；两份独立 Cobertura 附件的结果一致，单份行覆盖率 91.56%（6343/6927）、分支覆盖率 81.37%（1730/2126），通过仓库 90%/75% 门槛；聚合计数仅为相同附件的重复汇总，不作为更高覆盖率证据。Debug/Release `-warnaserror` 均为零警告零错误；格式、118-ID UI 合同、单实例合同、启动链、配置持久化、文件操作安全、缩略图 worker 隔离与依赖漏洞门禁通过。DesktopHost 交互探针仍为 `Conditional Pass`；#19/#20/#23/#24 的 ValidateOnly 结果继续分别保持 `PendingManualEvidence`、`ResultsPending` 或 `PendingDedicatedEnvironmentEvidence`。远端 PR/main CI 结果在发布流程中复核。任何本机自动化都不伪造真实桌面窗口、输入、动态显示器或专用卷证据。

## 6. 需求对齐与剩余方向

| 最初需求 | 本切片结果 |
| --- | --- |
| 桌面分组与布局恢复 | 建立只作用于 Long方格自有容器窗口的真实批处理基础 |
| 桌面文件整理 | 不扩权；没有枚举、移动或删除桌面文件 |
| 现代 UI 与平滑动效 | 不改 UI；保持已有设计系统与 reduced-motion 边界 |
| 任务栏美化 | 不进入当前安全关键路径，仍为 MVP 后续 |
| 自定义窗口效果 | 仅改变已批准 Bounds，不改变激活、Z 序、Region 或第三方窗口 |
| 小组件与 Long助手插件 | 协议不变，运行时仍为 MVP 后续隔离阶段 |

RC 硬化阶段尚未结束。下一最小切片是同步配置暂存适配器；其后还需复合故障矩阵、DesktopHost UI 线程封送、输入/动态显示/关闭矩阵、干净会话 118-ID UIA、打包与 Release Candidate 审计。GitHub #19、#20、#23、#24 继续保持外部证据 Pending，全部 blocker 清零前不把真实执行入口接入 App。
