# Stage 106：DesktopHost 生命周期与默认关闭 Feature Flag 审计

日期：2026-08-12

基线：`main` / `00fad72`（PR #154 已合并，main CI `31556676525` 通过）

结论：**阶段 A 的 A1 已完成。App 现在唯一持有 DesktopHost 生命周期边界，开关严格默认关闭，控制中心只接收有限匿名状态，关闭链可确定释放；本切片没有创建真实宿主或桌面方格窗口。**

## 1. 需求对齐

本切片对应 Stage 103 的 A1，解决四个产品化前置问题：

1. DesktopHost 生命周期必须由 App composition root 唯一拥有；
2. 未经显式开发授权不得创建宿主；
3. 控制中心必须区分“安全策略关闭”与“已授权但尚未连接”；
4. Catalog 刷新、应用关闭和后续 A2 接线必须有稳定状态合并点。

它没有承诺 iTop/Fences 级真实桌面方格已经出现。可见 HWND、半透明渲染、每显示器宿主、Alt+Tab/Win+D 行为和真实 UIA Fragment 属于 A2–A5。

## 2. 实现合同

### 2.1 Core Feature Policy

`ProductDesktopHostFeaturePolicy` 只接受大小写和空白均精确的 `LONGGRID_ENABLE_DESKTOP_HOST=1`：

- 未设置、空字符串、`0`、`true`、前后空格等全部关闭；
- opt-in 只代表 `EnabledForDevelopment`，不代表宿主已连接；
- 默认关闭映射为 `RuntimeCapabilityState.DisabledBySafetyPolicy`；
- opt-in 但尚无宿主映射为 `Disconnected`；
- `HasExternalConnection` 只对真正的 `ConnectedReadOnly` 返回 true，不能把开关打开误报为外部连接。

### 2.2 Infrastructure 生命周期边界

`ProductDesktopHostLifecycleController` 只报告：

- `DisabledBySafetyPolicy`；
- `AwaitingHost`；
- `Completed`；
- 单调 generation；
- `NativeHostConnected`；
- `OwnedWindowCount`。

A1 中连接布尔值恒为 false、窗口数量恒为 0。控制器不依赖或创建 `ProductDesktopHostWindowBridge`、`WindowsProductDesktopHostWindowInspector`、HWND、进程 ID、线程 ID或路径。显式字段为 A2 留出状态投影位置，但没有提前获得原生权限。

### 2.3 App 与 UI

App 构造期读取环境变量并创建唯一控制器字段；启动时订阅匿名快照并投影到现有运行状态卡；关闭时先退订、释放控制器，再释放显示拓扑、Desktop Catalog 和保存控制器。

MainWindow 缓存 Catalog 连接布尔值与 DesktopHost Feature 布尔值，每次任一来源更新都重建同一 Core Runtime snapshot，防止 Catalog 后续刷新把 DesktopHost 状态重置。UI 文案为：

- 默认：`安全策略关闭` / 不创建宿主、不影响 Explorer；
- opt-in：`等待宿主` / 本阶段尚未创建桌面窗口；
- UIA ItemStatus 继续使用有限枚举字符串，不暴露环境变量值或原生身份。

## 3. 自动化与运行证据

- Release build：0 warning / 0 error；
- 新增及相关定向测试：14/14；
- 全量 Release：647/647；
- 覆盖率：行 91.22%，分支 81.43%，通过 90%/75% 门禁；
- `Test-LongGridUi.ps1 -ContractOnly -NoBuild`：Pass，142-ID 不变；
- 源码合同验证唯一 composition-root 字段、严格 opt-in、有限快照、释放链，以及生命周期层不得出现原生窗口权限；
- Release App 实际启动：窗口标题 `Long方格`、进程响应正常、正常关闭后零残留。

当前桌面会话的完整 live UIA 在读取 `ResponsiveStatusText` 前多次遇到 WinUI Automation Tree 的 `E_UNEXPECTED`/节点暂不可见。脚本现仅在原有 5 秒截止时间内重试 COMException，找不到控件和状态不符仍硬失败；本轮 live 状态读数因此标记 **Conditional / 未采信**，不作为 A1 通过依据。PR 和 main 的干净 runner 仍是最终自动化门禁。

## 4. 安全与权限边界

- 桌面文件读取/写入/移动/删除：无新增；
- DesktopHost HWND 创建与注册：无新增；
- Explorer、Progman、WorkerW、任务栏：无访问；
- 配置 schema 与持久化：无变更；
- Widget/Long助手插件：无加载、无权限；
- 网络、遥测、签名和分发：无新增。

环境变量只用于本机开发进程，不写配置、不继承为“用户已同意”的产品设置。正式可用开关仍需 A2–A5 的可见宿主、故障补偿、资源、DPI、多显示器和人工矩阵证据。

## 5. 下一步

进入 A2：单显示器只读方格渲染。

A2 应复用现有窗口所有权桥和 Windows inspector，在 opt-in 后创建一个产品自有、不可交互修改文件的只读方格；默认关闭路径必须继续保持零 HWND。首个 PR 只覆盖单显示器、单容器、静态内容和关闭零残留，不同时引入多显示器、拖放、文件移动、任务栏或插件能力。
