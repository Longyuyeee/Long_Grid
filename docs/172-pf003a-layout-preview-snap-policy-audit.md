# Stage 172：PF-003A 布局预览与吸附策略审计

- 审计日期：2026-08-21
- 开发分支：`codex/pf002d-create-preview`
- 对应目标：PF-003 的移动/缩放纯内存预览与有限吸附基础
- 结论：**正式 Core 预览策略与 100 方格生产规模延迟门通过；正式 DesktopHost 输入、提交补偿、跨显示器和物理/UIA 产品证据仍未完成，PF-003 保持 `InProgress`**

## 1. 阶段准入

本机项目引用 `Microsoft.WindowsAppSDK 2.3.1`，框架依赖启动实际加载已安装的 `Microsoft.WindowsAppRuntime.2 2.4.0.0 / Microsoft.UI.Xaml.dll 3.2.3.0`。Microsoft WinUI 官方问题 [#11139](https://github.com/microsoft/microsoft-ui-xaml/issues/11139) 截至本轮仍是 Open/Backlog，并说明跨进程 UIA 查询可触发应用无法捕获的 fail-fast。故本轮不强行执行 UIA、不删除可访问语义、不把回退运行时猜测成修复。

Stage 171 已关闭 PF-002 正式 App 创建、保存和最近撤销工程证据，PF-002 调整为 `EngineeringComplete / ProductEvidencePending`。依据 Stage 153“外部证据不阻断安全边界内功能编码”，进入 PF-003A；PF-002F 物理/无障碍证据继续并行阻止 `Complete` 和分发。

## 2. 本轮实现

新增 `ProductWorkspaceContainerLayoutPreview`：

- 支持移动、四边缩放和四角缩放九种手势；
- 请求绑定容器 ID、当前 edit revision、topology generation 和显示器 ID；
- 在 DIP 中计算候选，不写配置、不调用保存、不读取桌面；
- 使用 8 DIP 网格和 6 DIP 阈值，吸附工作区边缘及同显示器其他方格的边缘；
- Shift 对配置的吸附开关取反，支持“默认开时临时关闭、默认关时临时开启”；
- 移动保持原尺寸，缩放保持最小 160×120 DIP；
- 结果约束在按目标显示器有效 DPI 换算的工作区；
- 锁定容器、陈旧 revision、陈旧 topology、缺失/重复身份、未准入跨显示器、NaN/Infinity 和极端 delta 均失败关闭；
- 输入状态不被修改，输出为独立 placement 候选和有限 `Changed/SnappedX/SnappedY` 事实。

现有 `ProductWorkspaceScalePreflight` 增加 `layout-preview-100-containers` 指标：在正式 100 方格/500 引用状态上预热 100 次，然后逐次测量 2,000 次生产策略调用的 P95，预算为 16.7 ms。该指标与既有保存/恢复真实临时沙箱测试共同输出 Expected/Actual。

## 3. 预期—实际—差异

| 检查项 | 预期 | 首轮实际 | 差异/处理 |
| --- | --- | --- | --- |
| 九种移动/缩放 | 产生有限候选 | 19 项聚焦合同通过 | 无 |
| 相邻方格吸附 | 右边缘 308 吸附到 310 | 最终 X=110、宽=200 | 无 |
| Shift 反转 | 默认开变关、默认关变开 | 108（不吸附）/110（吸附） | 无 |
| 最小尺寸 | 不小于 160×120 DIP | 左缩放钳制为宽 160 | 无 |
| 锁定/陈旧/错误显示 | 零预览拒绝 | 分别返回有限失败状态 | 无 |
| DPI/负坐标 | 100/150/200/300/400% DIP 边界一致 | 聚焦合同通过 | 无 |
| 100 方格预览延迟 | P95 < 16.7 ms | 首轮 `0.067 ms` | 无 |
| 桌面/文件副作用 | 零 | `readsRealDesktop=false / realFileOperationsAllowed=false` | 无 |

失败关闭复审发现初版只验证 delta 为有限数，仍可能接受数量级极端输入；显示/容器身份也依赖 `SingleOrDefault` 抛异常，越界旧 placement 在部分缩放方向可能形成无效 `Math.Clamp` 区间。修正为 1,000,000 DIP 有界输入、显式唯一身份计数和源 placement 工作区校验；约束若改写了吸附候选则清除吸附标志。重复、极端和越界输入均返回有限失败结果，再重跑聚焦测试。

## 4. 需求对齐与未完成项

| PF-003 要求 | 当前状态 |
| --- | --- |
| 移动、四边和四角缩放计算 | Engineering Pass |
| 指针移动期间只更新内存 | Pass；策略无保存依赖 |
| 网格/屏幕/其他方格边缘吸附 | Engineering Pass |
| Shift 切换吸附 | Engineering Pass |
| 锁定与陈旧状态拒绝 | Engineering Pass |
| 100 方格计算 P95 <16.7 ms | Pass；不等同于视觉 P95 |
| 手势 begin/update/cancel/complete 生命周期 | Pending PF-003B |
| 正式 DesktopHost 标题栏和八向命中 | Pending |
| 方向键 1 DIP / Shift 大步微调 | Pending |
| 结束时唯一一次保存提交 | Pending |
| 保存失败完整恢复 | Pending |
| 跨显示器 DPI 转换 | Pending；本轮明确拒绝 |
| 重启误差、物理输入、UIA Bounds | PendingProductEvidence |

本轮不是用户可见 PF-003 完成项，也没有用 Core 类型或性能数字折算功能完成率。PF-003 保持 `InProgress`。

## 5. 真实测试

```powershell
dotnet test tests/LongGrid.Core.Tests/LongGrid.Core.Tests.csproj `
  --configuration Release `
  --filter FullyQualifiedName~ProductWorkspaceContainerLayoutPreviewTests

dotnet run --project tools/LongGrid.Tools.ProductScalePreflight `
  --configuration Release
```

提交前实际结果：

- PF-003A 聚焦合同：`19/19`；
- 100 方格生产规模预检：2,000 次，最终 P95 `0.056 ms < 16.7 ms`，临时沙箱已清理；
- Release 全量测试：`1029/1029`；
- Release solution build：`0 warning / 0 error`；
- 153-ID 静态 UI 合同：Pass；
- PF-002 正式 App 回归：Pass，外部 29 字段合同无差异；
- 正式窗口生命周期：两轮 20 秒 Pass，就绪 1,165/1,045 ms，退出码 0，未查询跨进程 UIA；
- 完整跨进程 UIA：官方问题仍 Open/Backlog，本机已知组合在启动前安全阻断，未伪报 Pass；
- 漏洞、格式与差异检查：Pass。

## 6. 下一切片

PF-003B 建立一次手势会话：

1. begin 冻结容器、原 placement、revision、topology 和 display；
2. update 只调用本轮策略并发布内存候选，不提交保存；
3. Esc、捕获丢失、锁定/配置/拓扑变化执行 cancel 并恢复原候选；
4. complete 对最终候选执行一次且仅一次正式配置提交；
5. 真实临时配置验证中间 1,000 次 update 为零磁盘变化，complete 后只推进一个保存修订并以 ≤1 DIP 误差重载；
6. 随后单独实现保存失败恢复，再准入 DesktopHost 原生输入和跨显示器。
