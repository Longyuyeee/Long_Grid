# Stage 179：PF-003D4 跨显示器混合 DPI 布局审计

- 审计日期：2026-08-24
- 开发分支：`codex/pf002d-create-preview`
- 起始基线：`f266535`
- 对应需求：跨显示器拖动、Per-Monitor DPI 换算、目标工作区夹取、拓扑变化取消与真实持久化
- 结论：**PF-003D4 工程链和本机真实双显示器混合 DPI 语义证据通过；物理鼠标/触控、截图和跨进程 UIA Bounds 仍未完成，PF-003 保持 `InProgress`。**

## 1. 需求边界与冻结规则

本轮只扩展 Move，不让一次 Resize 手势跨显示器。跨屏移动必须同时满足：

1. Begin 冻结源显示器、源 placement、工作区 revision、拓扑 generation，以及指针在方格内的逻辑 DIP 抓取偏移；
2. Update 只用权威显示器 `Bounds` 判断指针目标，屏幕坐标可以为负；指针不属于任何权威显示器立即取消并恢复源 placement；
3. 方格逻辑 DIP 宽高和逻辑抓取偏移保持不变，指针像素按目标显示器 DPI 换算为目标工作区局部 DIP；
4. 候选只对目标显示器工作区及其方格吸附、夹取，配置中不保存负屏幕坐标；
5. topology/revision、目标显示器、锁定或源 placement 任一事实变化，完成请求均失败关闭并恢复；
6. Complete 仍只进入 Stage 173/174 的唯一提交、正式保存与失败补偿，不增加旁路写盘入口。

## 2. 实现与交互链

Core 预览策略把源显示器验证与目标显示器计算分离，Move 可携带目标显示器及绝对目标局部 DIP；Resize 继续只允许源显示器。手势会话由真实屏幕指针解析目标显示器，并把冻结抓取偏移投影到目标 DPI。

DesktopHost lifecycle 不再假设候选只能画在源 Surface：源 Surface 清理旧候选，目标 Surface 使用源方格投影绘制外部候选；取消会清理全部 Surface。审计确认 Explicit Surface 的窗口区域覆盖完整显示器，跨屏候选已在现有 region 内，无需扩大命中区或引入透明输入遮挡。

正式 App evidence session 使用本机权威显示拓扑和实际工作区/DPI 构造源、目标指针事实，驱动生产 App 请求、唯一布局提交、正式 Store 保存和磁盘重载；输出只保留显示器数量、DPI 和布尔结果，不写稳定显示器身份。

## 3. Expected / Actual / Difference 与修正

| 检查 | Expected | 首次 Actual | 修正后 Actual |
| --- | --- | --- | --- |
| Core 跨屏预览 | 源 placement 可迁移到唯一目标显示器 | 旧策略强制 `source.DisplayKey == request.DisplayId`，跨屏必拒绝 | 源/目标验证分离，目标工作区、DPI、吸附和夹取生效 |
| 指针抓取点 | 混合 DPI 下保持逻辑 DIP 偏移和尺寸 | 旧会话只有累计源 DIP delta，没有绝对屏幕指针 | Begin 冻结抓取偏移，Update 从目标像素计算目标局部 DIP |
| 目标 Surface 候选 | 候选只出现在目标显示器 | 旧 Surface 只接受本地 projection，目标屏无法画源方格 | lifecycle 跨 Surface 路由外部源投影，并清理非目标候选 |
| 取消恢复 | 指针离开权威显示器或拓扑变化恢复源 | 旧协议没有跨屏目标与离屏语义 | 返回有限 Cancelled，placement 精确恢复源显示器与坐标 |
| 保存失败补偿 | 内存、磁盘都回到源显示器 | 原 `.lock` 测试只覆盖同屏坐标 | 真实写租约失败改为跨屏提交，补偿后内存/磁盘均为源显示器 |
| 正式 App 持久化 | 混合 DPI 跨屏后保存目标显示器及目标局部 DIP | 未执行 | Begin/Update/Complete 全 true，重载目标一致，X/Y 差值均 0 DIP |
| 外部副作用 | 不改变真实桌面或用户配置 | 未执行 | 两者元数据不变，临时 evidence 清理，`Difference=None` |

修正均落在生产路径和失败关闭合同中；没有放宽断言，也没有把人工物理输入标记为已执行。

## 4. 真实测试证据

### 4.1 本机权威显示拓扑

只读实际探针发现 2 个显示器、2 条有效路径、2 个强身份映射和 0 个 fallback；源边界匹配 2/2。实际 DPI 为 192 与 240，存在混合 DPI；其中一个显示器使用负虚拟屏幕坐标。100 次枚举指纹稳定、对象和句柄无增长，P95 为 22.0742 ms。

该证据证明当前静态双屏拓扑可被产品权威读取；本轮没有热插拔显示器，因此动态拓扑切换仍由生产状态机测试覆盖，不能声称完成真人热插拔矩阵。

### 4.2 正式 App、真实 Store 与混合 DPI

`Test-LongGridPf002AppEvidence.ps1` 启动真实 Release App，在完成既有创建、撤销、同屏移动及键盘事务后，使用实际两块显示器执行跨屏 Move：

- `CrossDisplayHardwareAvailable=true`；
- `Begin/Update/Complete=true`；
- `ChangedDisplay=true`；
- 正式 Store 重载 `PersistedSameDisplay=true`；
- 重载 X/Y 与目标候选差值均为 `0 DIP`；
- `SourceDpi=192`、`TargetDpi=240`、`MixedDpi=true`；
- 最终 save revision=7；
- 外部 `Difference=None`。

这是真实硬件拓扑、正式 App 组合根、正式事务和真实磁盘重载证据；指针事实由受控 evidence session 注入生产语义入口，不等于操作者用物理鼠标拖过两块屏幕。

### 4.3 原生窗口、失败和恢复

测试实际创建两个非零 HWND Surface，在目标 192 DPI Surface 上把外部候选投影为预期像素边界 `100,120,400,320`。真实临时 Store 的 `.lock` 写租约故障发生在跨屏完成之后：保存失败时磁盘仍为源显示器，补偿后内存恢复源 placement，解除租约后的重载也回到源显示器。

拓扑 generation 改变、目标显示器缺失、指针落在权威显示器外、锁定或 revision 变化都零提交并恢复；负屏幕坐标只参与屏幕命中，不进入局部 placement。

## 5. 完整门禁

- 跨屏/布局聚焦：`63/63`；其中提交/补偿聚焦 `11/11`；
- Release 全量：`1075/1075`，0 skipped；
- Release solution build：`0 warning / 0 error`；
- 正式 App/Store 双屏混合 DPI：Pass，`Difference=None`；
- 100 方格、500 项、2,000 次布局预览 P95：`0.056 ms < 16.7 ms`；
- 153-ID UI/结构合同：Pass；
- 真实窗口：1,853 ms 就绪、稳定 20 秒、退出码 0，`Difference=None`；
- NuGet 全项目漏洞检查：无已知易受攻击包；
- `dotnet format --verify-no-changes` 与 `git diff --check`：Pass；
- live 跨进程 UIA：本机 Windows App Runtime 2.4.0.0 / Microsoft.UI.Xaml 3.2.3.0 已知崩溃组合在 App 启动前安全拒绝；不能升级为 UIA Pass。

## 6. 对齐结论与下一步

| PF-003 要求 | 状态 |
| --- | --- |
| 预览/吸附、会话、唯一提交、失败补偿 | Pass（Stage 172–174） |
| Surface 九向输入、正式 App 候选/提交 | Engineering Pass（Stage 175/177） |
| 标题焦点与键盘移动/缩放 | Engineering Pass（Stage 178） |
| 跨显示器 Move、混合 DPI、负坐标、真实保存/重载 | Engineering Pass；真实双屏语义证据 Pass |
| 物理鼠标跨屏拖动、触控、截图 | Pending PF-003D5 Product Evidence |
| Narrator、跨进程 UIA Bounds、100%–400% 人工 DPI 矩阵 | Pending Product Evidence / upstream-safe runtime |

方向没有偏离 Stage 153：本轮关闭的是 iTop/Fences/Nimi 日常布局所需的跨显示器工程缺口，没有提前进入任务栏、Widget 或插件扩展。PF-003 仍为 `InProgress`，顶层 30 项仍为 `0 Complete`。下一切片固定为 PF-003D5：在安全 Windows 会话执行真实鼠标/键盘/触控与截图证据；若本机 UIA 运行时仍失败关闭，则保留明确 blocker，不通过危险强制开关冒充通过。PF-003 收口后按 Stage 153 进入 PF-004 标题栏与就近操作。
