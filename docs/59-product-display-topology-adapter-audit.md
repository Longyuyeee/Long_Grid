# Long方格产品显示拓扑只读适配器审计

> 审计日期：2026-08-06
>
> 范围：正式 App 当前显示拓扑、CCD/Monitor 对账、权威门禁、generation/latest-wins 与关闭排空
>
> 结论：产品级当前拓扑适配器已建立；只有完整强身份样本才可进入布局恢复预览

## 1. 产品化边界

此前 `LongGrid.Spikes.DisplayTopology` 已验证 `QueryDisplayConfig`、`DisplayConfigGetDeviceInfo`、`EnumDisplayMonitors`、`GetMonitorInfo` 和每屏 DPI 读取，但 probe 不能作为正式 App 的事实来源。本阶段把只读能力重新实现到 Infrastructure，并保留以下隔离：

- 不引用 probe 项目或 probe 输出；
- 不调用 `SetDisplayConfig`、窗口定位、DesktopHost、桌面文件 API 或配置写入；
- 原始 monitor device path、adapter LUID、target ID、GDI source name 不离开原生采样源；
- 对外 `StableId` 只保留 SHA-256 十六进制摘要；
- Windows 之外返回有限 `UnsupportedPlatform`，不伪造空权威拓扑。

## 2. 完整成功才权威

`ProductDisplayTopologyReader` 只有在以下条件全部成立时返回 `Ready/IsAuthoritative=true`：

1. 至少一个 Monitor，且 Core 拓扑指纹校验通过：唯一 StableId、恰好一个主屏、有效 Bounds/WorkArea/DPI；
2. CCD active path 数与 Monitor 数完全一致；
3. 每个 Monitor 都唯一映射到 active GDI source；
4. 每个 source Bounds 与 `GetMonitorInfo` Bounds 精确一致；
5. 每个 target 可用，且 rotation 不是 Unknown；
6. 每个 StableId 都来自 monitor device path 的强身份；adapter/target 或 GDI name fallback 只能生成 `Degraded`；
7. WorkArea 完整位于对应 Monitor Bounds 内；
8. `QueryDisplayConfig` 在最多 8 次缓冲竞态重试内完成。

任何部分证据缺失都保留有限计数和节点用于诊断分类，但 `IsAuthoritative=false`；非法样本返回清空节点的 `Failed`，避免错误拓扑进入恢复规划器。

## 3. 并发与 App 生命周期

`ProductDisplayTopologyController` 复用产品 Catalog 已验证的并发语义：

- 每次刷新分配单调 generation；
- 较早刷新晚完成时返回 `Stale`，不能覆盖较新快照；
- 刷新期间清空旧节点并发布 `Refreshing`，避免把过期拓扑继续当权威；
- caller/lifetime cancellation 返回有限 `Cancelled`；
- `DisposeAsync` 先取消 lifetime，再等待全部已接受刷新排空。

App 启动后在后台执行一次当前会话采样，快照事件通过 DispatcherQueue 回到 UI。布局恢复预览只在 `topology.IsAuthoritative` 时接收节点；强样本成立但 v1 尚无保存时拓扑时进入 `SavedTopologyMissing`，降级、失败、取消或刷新中继续保持 `AwaitingAuthoritativeTopology`。关闭流程先取消并排空拓扑 Controller，再释放 Catalog 与配置保存控制器。

## 4. 测试证据

- 28 项拓扑定向测试覆盖完整强样本、7 类单点降级、空/非法/原生失败样本、非 Windows、真实 Windows 有限采样与身份脱敏、全部有限 Controller 状态、generation/latest-wins、caller/lifetime 取消和关闭排空，以及不依赖显示硬件的 CCD rotation/source-mode/source-bounds/virtual-rect 纯映射与所有权校验；布局恢复另覆盖无效权威拓扑的有限失败路径；
- 真实 Windows smoke 通过正式 Reader 调用原生源，要求结果有限、非 Unsupported，并验证所有返回 StableId 均为 64 位十六进制摘要；它不把当前机器样本状态冒充多屏/热插拔验收；
- Debug/Release 全量构建均为 0 warning / 0 error；覆盖率修复后的最终测试为 377/377 通过，其中拓扑定向 28/28、布局恢复预览 5/5 通过；
- CI 等价 Release/TRX/XPlat Coverage 命令得到行 91.80%（9444/10288）、分支 83.38%（2418/2900），通过仓库 90%/75% 门槛；Windows P/Invoke 生产实现未使用覆盖率排除属性；
- Debug/Release 116-ID UI 源码合同、Release 启动链、单实例源码合同、Issue #19/#20/#23/#24 `ValidateOnly` 和已知漏洞扫描均通过；人工矩阵与专用环境证据仍保持 Pending；
- Release 真实 UIA 在当前桌面会话调用 `FindFirst` 时再次返回 `0x8000FFFF (E_UNEXPECTED)`。系统仍存在 PID 39208、无主窗口且无法确认归属的旧 LongGrid 进程；本次记为环境性 Inconclusive，未擅自终止该进程，也不计为产品通过或失败；
- GitHub PR 与合并后主干 CI 结果在发布后进入 PR/Issue 审计链，不在提交前伪造远端结论。

首轮 PR CI 的 build、362 项测试、启动/Issue/UI/单实例合同均通过，但 Windows runner 的真实显示环境使原生 smoke 较早返回有限状态，行覆盖率为 89.58%（9216/10288），低于 90% 门槛。修复坚持不降低门槛、不排除原生文件：下载失败 run 覆盖产物对账，新增有限状态、caller cancellation、原生失败、rotation/source-mode/source-bounds/virtual-rect 与无效权威拓扑的确定性测试；相对失败 run 新增 38 个不依赖显示硬件的覆盖行，随后本地 CI 等价门禁达到 91.80%。最终 PR CI 仍以后续远端 run 为准。

## 5. 需求对齐与下一阶段

本阶段关闭了“正式 App 没有生产级当前拓扑来源”的缺口，但没有关闭保存时拓扑缺口，也没有批准真实窗口恢复。下一阶段应为配置设计版本化保存时拓扑快照：只保存恢复所需的脱敏 StableId、Bounds、WorkArea、DPI、Rotation、Primary 与 schema version，定义 v1 → 新版本迁移、导入/导出校验和损坏回退。保存时/当前两侧都权威之前，不显示恢复确认按钮，不调用 DesktopHost 提交。
