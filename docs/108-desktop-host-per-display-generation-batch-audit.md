# Stage 108：每显示器多方格 Generation 批次审计

日期：2026-08-12

基线：`main` / `0e5b632`（PR #156 已合并，main CI `31560350822` 通过）

结论：**阶段 A 的 A3 已完成。Long方格已从“第一方格/主工作区/一个 HWND”升级为“每个实际使用中的显示器一个 HWND、同显示器聚合多个只读方格”的 generation 批次；默认关闭、非权威拓扑、空工作区、任一所有权失败和关闭路径均收敛为零产品 HWND。输入、文件操作、Explorer 嵌入、任务栏与插件权限没有扩大。**

## 1. 需求与竞品对齐

iTop/Fences 类桌面分组需要同时呈现多个分组，并在多显示器间保持稳定归属。A3 关闭的是渲染拓扑缺口，不是交互缺口：

- 正式工作区最多 100 个方格全部进入有限投影，而非只取第一个；
- 按配置 `DisplayKey` 映射当前权威显示器；旧键/未知键确定性回退唯一主显示器；
- 每个实际承载方格的显示器只创建一个 HWND，避免 100 方格产生 100 个 USER 窗口；
- 同一显示器内以联合 Window Region 承载多个方格，空白区不形成产品窗口区域；
- 批次携带 workspace revision、拓扑 generation、SHA-256 拓扑指纹和逐窗 registry generation。

拓扑状态为 Refreshing、Degraded、Unavailable、Failed 或 Cancelled 时不猜测坐标，当前批次安全关闭。该策略可能在动态刷新期间暂时隐藏方格，A4 再审计无闪烁/保留上一权威代次的安全优化。

## 2. 有限投影合同

`ProductDesktopHostProjectionBatch` 限制：

- workspace revision 非负、拓扑 generation 为正；
- 指纹为 64 位十六进制；
- 最多接受 16 个显示器的权威拓扑、全局最多 100 个方格；超过预算失败关闭；
- DisplayId、ContainerId 全局唯一且长度受正式配置上限约束；
- 每方格最多 12 个显示名，每个显示名最多 512 字符；
- WorkArea 必须有面积，DPI 必须为 48–768；
- 所有数组在入口复制并只读包装，调用方后续修改不能改变批次。

Builder 只消费正式 `ProductWorkspaceReadSnapshot` 已允许展示的名称；未解析引用继续使用匿名“待审查项目 N”，不投影路径、持久化 target、Shell 身份或文件内容。

## 3. 原生宿主与补偿

每个显示器表面覆盖该显示器 WorkArea，但立即用所有方格矩形的 `SetWindowRgn` 联合集合裁剪；方格位置与尺寸按该显示器 Effective DPI 从 DIP 转像素并限制在 WorkArea。窗口继续保持 ToolWindow、Layered、NoActivate、Transparent/HTTRANSPARENT，不置顶、不抢焦点、不接收输入。

所有显示器表面必须属于同一进程和宿主线程。每个 HWND 设置独立实例标记，并经既有 WindowBridge 复读存在性、进程、线程、标记与 Bounds。只有整批注册成功才发布 `ReadyReadOnly`；第二显示器或后续任一窗口失败时，已经成功的窗口也会注销、销毁并断开 bridge，最终报告零 OwnedWindow。

当前 GDI 表面以方格颜色和固定深色桌面基色混合来近似配置透明度。这不是逐像素 Acrylic/Composition 透明度，也不应作为最终视觉验收；它避免整屏统一 alpha 造成空白区域染色。最终材质、阴影、圆角一致性和动效属于后续视觉/Composition 切片。

## 4. 自动化与资源证据

- `dotnet format --verify-no-changes`：通过；
- Release build：0 warning / 0 error；
- A3 定向测试：19/19；
- Release 全量测试：664/664；
- 覆盖率：行 91.70%，分支 80.93%，通过 90%/75% 门禁；
- 真实 Windows 表面：创建、Region、所有权复读、关闭销毁通过；
- 假双显示器：两个显示器生成两个表面、三个方格被同批报告；
- 第二显示器标记故障：两个表面全部回收、OwnedWindow=0；
- 142-ID UI 合同、启动链、单实例、干净会话、CI hang/restore 与依赖漏洞门禁通过；
- DesktopHost 原生探针：不抢前台，USER/GDI/handle 回到基线，仍为 Conditional Pass；
- 配置 100 次原子持久化、文件操作安全和缩略图隔离探针保持既定通过/Conditional Pass。

Issue #19/#20/#23/#24 与 BSA 仍是 PendingManualEvidence。自动双显示器模型测试不替代真实硬件拔插、旋转、不同 DPI、任务栏位置、RDP、Win+D、全屏和 Narrator 人工判断。

## 5. 权限与数据边界

- 文件内容读取：零新增；
- 桌面文件写入、移动、重命名、删除：零新增；
- 配置 schema/持久化：无变更；
- `Progman`、`WorkerW`、Explorer 注入或内部 XAML：无；
- 输入、拖放、UIA Fragment：关闭；
- 任务栏、LongBar、Widget/Long助手插件：未加载；
- 网络、遥测、签名和发布默认值：无变化。

## 6. 下一步 A4

A4 聚焦动态拓扑、关闭/故障补偿与资源审计：连续权威/非权威拓扑切换、显示器增删/旋转/DPI、快速 workspace revision、Explorer 重启、锁屏/RDP/会话切换、异常关闭和 24 小时资源趋势。应定义安全保留上一权威批次与必须立即隐藏之间的状态机，并验证 latest-wins；不得在同一切片提前开放输入或文件权限。

后续结果：Stage 109 已完成 A4 自动化状态机与资源收敛子切片，选择“非权威拓扑立即隐藏”，并加入双代次 latest-wins、同代次晋升/冲突闭合和 100 revision 释放测试；真实硬件、会话与 24 小时矩阵仍明确待人工证据。详见[Stage 109 审计](109-desktop-host-dynamic-topology-lifecycle-audit.md)。
