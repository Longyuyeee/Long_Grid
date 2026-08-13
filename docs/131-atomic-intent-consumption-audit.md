# Stage 131：原子 Intent 消费边界审计

更新日期：2026-08-14

## 1. 结论与需求对齐

本切片完成 E2/M2 的第一个工程子阶段 E2a：把已有 Prepared Intent 原子、至多一次地交给 M1 的 admission / Explicit surface / selection 事务。它没有接入正式 HWND 输入源，因此 **E2/M2 尚未完成**；下一步固定为 E2b 正式 pointer、keyboard 与 UIA 来源。

范围保持不变：四重精确开发门禁缺一即关闭；只有刚刚复读为 Passive 的产品表面才可准备或消费；系统表面事件、拓扑/工作区替换、表面释放和关闭都取消事务；不读取文件内容，不写入、移动或删除桌面文件。

## 2. 审计发现与修正

- 准备桥新增锁内 `TryConsume`，候选、代次、序号、证据和时限全部匹配后才清除 Prepared 状态并返回 Intent；重放不能再次进入 Explicit。
- 新增唯一消费控制器，复用既有 `ProductDesktopInteractionSurfaceModeTransaction`，而不复制 admission、选择或补偿逻辑。进入失败仍消耗该 Intent 一次，并由事务恢复 Passive/Hidden 安全终态。
- 生命周期每次准备、转发和消费前都重新 `Capture` 正式表面并验证 Passive 与 window registry generation。审计中发现仅依赖 E1 开发控制器旧快照可能在 Explicit 期间准备第二个 Intent，现已封闭：Explicit 期间统一返回 `AwaitingPassiveSurface` 并清除准备态。
- 正式只读投影新增每容器局部的匿名 `item:N` 身份，选择模型不再用可见名称作身份；投影相等性同时比较项目 ID，避免同版本冲突被误判为重复更新。ID 不包含路径、文件名、Catalog 身份或文件内容。
- 焦点丢失等系统事件先失效 Prepared，再取消 Explicit 事务，随后隐藏；投影释放先取消/解绑消费事务，再解绑开发适配器和销毁 HWND。

## 3. 自动化验收目标

- 当前 Prepared Intent 恰好消费一次，重放不产生第二次 Explicit；
- 陈旧证据、关闭门禁、非 Passive 表面、目标消失和事务应用失败均有限拒绝；
- Explicit 期间 pointer/keyboard/UIA 的后续归一化输入不能准备新 Intent；
- 选择只接受当前 lease 目标的匿名可见项目 ID；
- FocusLost、detach、complete 后回到 Passive/Hidden 或完成态，且文件操作能力恒为 false；
- UI 源码合同证明 App 只组装消费边界，尚未调用正式输入、消费或选择入口。

本地收口结果在提交前执行：Release 0 warning/0 error、全量测试与覆盖率、UI 源码合同及仓库既有安全入口。远端 PR CI、合并 SHA 与 main CI 在合并后回填本文和 Stage 125。

## 4. 剩余差距与下一步

E2a 只关闭消费所有权和 TOCTOU/重放边界，不证明物理鼠标、键盘或 Narrator。E2b 必须在新的独立 PR 中：

1. 由产品自有 HWND 的定向消息与 UIA provider 产生归一化、来源可证明的 action；
2. 不安装全局 Hook、不使用 Raw Input 或 `SendInput`，不接触 Explorer/WorkerW；
3. pointer、keyboard、UIA 三条路径进入同一 forwarding → preparation → consumption 状态机；
4. 保持四重门禁、去重、Passive 复读、系统事件取消和零桌面文件操作；
5. 自动化只能形成工程证据，Narrator、物理设备和动态系统表面结果继续保持 PendingManualEvidence。

E2b 关闭后才能判断 M2 工程是否完成；M3、内部 RC、公开分发及四个外部 Issue 的状态均未因本切片改变。
