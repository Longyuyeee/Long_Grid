# Stage 107：单显示器只读 DesktopHost 表面审计

日期：2026-08-12

基线：`main` / `59589f9`（PR #155 已合并，main CI `31558588728` 通过）

结论：**阶段 A 的 A2 已完成。严格开发 opt-in 后，Long方格可以把第一正式方格显示为单显示器只读桌面表面；默认关闭、无配置、所有权失败和关闭路径均收敛为零产品 HWND。本阶段没有开放输入、拖放、文件操作、Explorer 嵌入或任务栏能力。**

## 1. 需求与竞品对齐

本切片关闭 Stage 103 的第二个缺口：正式配置不再只能出现在控制中心。它提供 iTop/Fences 基础体验中“桌面能看见分组”的最小产品证据，但有意限于第一正式方格、主工作区与静态只读内容。

投影内容只有：稳定容器 ID、名称、有限 `#RRGGBB`、0–1 透明度、折叠状态、有限 DIP 放置和最多 12 个 presentation 已允许显示的引用名。未解析引用只使用匿名“待审查项目”占位，不把路径、Shell 身份或文件内容交给宿主。

## 2. 原生实现与安全边界

`WindowsProductDesktopHostReadOnlySurface` 创建 `WS_POPUP` 产品窗口，并组合：

- `WS_EX_TOOLWINDOW`：不建立普通任务栏按钮/Alt+Tab 产品入口；
- `WS_EX_NOACTIVATE` + `SW_SHOWNOACTIVATE`：初始显示不抢焦点；
- `WS_EX_LAYERED`：使用配置透明度；
- `WS_EX_TRANSPARENT`、`HTTRANSPARENT`、`MA_NOACTIVATE`：A2 不接收桌面输入；
- DWM 圆角为 best-effort，静态 GDI 渲染为当前低依赖基线。

窗口创建后写入每实例非零标记，并由 `ProductDesktopHostWindowBridge` 复读 HWND 的存在性、进程 ID、线程 ID、标记与 Bounds。只有所有权完全一致才进入 `ReadyReadOnly`；否则统一注销、销毁、断开并报告 `Faulted`。投影不变时不重建窗口，变化时先释放旧表面再建立新代次；空投影、关闭与重复释放均为有界、幂等路径。

禁止项保持明确：不查找或嵌入 `Progman`/`WorkerW`，不注入 Explorer，不置顶、不激活前台、不注册拖放，不调用 `IFileOperation`，不读取文件内容，不修改配置或桌面文件。

## 3. App 与有限状态

App composition root 继续唯一持有生命周期控制器，并在正式工作区读模型形成后投影第一容器。控制中心只显示：安全策略关闭、等待宿主、只读已连接、所有权异常或已完成，不获得 HWND、进程/线程 ID、实例标记或路径。

严格开关仍为 `LONGGRID_ENABLE_DESKTOP_HOST=1`。未设置或任意其他值不实例化 Windows surface factory/inspector，且 `ApplyProjection` 不创建窗口。该环境变量仅用于开发验证，不代表用户许可，也不是发布默认值。

## 4. 自动化证据

- `dotnet format --verify-no-changes`：通过；
- Release build：0 warning / 0 error；
- 生命周期、投影与 Runtime 定向测试：19/19；
- Release 全量测试：659/659；
- 覆盖率：行 91.63%，分支 81.53%，通过 90%/75% 门禁；
- 真实 Windows factory 测试：创建窗口、实例标记所有权复读、`ReadyReadOnly`、关闭销毁均通过；
- 142-ID UI 源码合同、启动链、单实例、干净会话入口、CI hang/restore 合同：通过；
- DesktopHost 交互探针：Unicode、UIA、无初始激活和 USER/GDI/handle 回收通过，结论仍为 Conditional Pass；
- 配置 100 次原子持久化、文件操作安全、缩略图隔离与依赖漏洞门禁：通过或既定 Conditional Pass，未被 A2 扩权。

人工 Issue #19/#20/#23/#24 与 BSA 结果仍为 Pending；A2 自动证据不能替代 Alt+Tab 实际观察、Win+D、全屏、Narrator、触控、多 DPI/多显示器和 24 小时稳定性。

## 5. 已知限制与下一阶段

A2 不是桌面管理 MVP 完成：

- 只投影第一正式方格，只使用主工作区；
- 一个 HWND 对应一个静态方格，尚未采用最终“每显示器一个 HWND + 多容器视觉/Region”模型；
- 内容为 GDI 文本，没有图标/缩略图、Composition 材质、UIA Fragment 或 Reduced Motion 动效；
- 点击、选择、拖动、缩放、折叠按钮、右键菜单和安全引用拖放均未开放；
- Win+D、全屏、Explorer 重启、RDP、锁屏和动态显示人工矩阵未完成。

下一步进入 **A3：多容器/多显示器 generation 投影**。应以每显示器宿主聚合多个方格，绑定 workspace revision、当前拓扑 generation、registry generation 与渲染 generation，并验证增删/迁移/拓扑变化的 latest-wins 和资源上限。A3 仍保持无输入、零文件内容读取、零桌面文件修改；交互必须等待阶段 B 单独准入。
