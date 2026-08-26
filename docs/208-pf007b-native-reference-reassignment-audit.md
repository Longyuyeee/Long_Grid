# PF-007B 桌面盒子间原生引用改归属审计

> 日期：2026-08-26  
> 基线：`origin/main@2980857`  
> 分支：`codex/pf-007b-reassignment`  
> 结论：**EngineeringComplete / RealHwndGesturePass / ProductEvidencePending**

## 1. 开发目标与边界

用户在 DesktopHost 显式交互模式中先选择一个或多个正式引用，再按住其中一个项目拖到另一个盒子。接受时只修改 Long方格配置中的归属，一次保存源移除与目标加入，并复用现有一次撤销；不移动、复制、删除、重命名或改写真实文件。

本阶段不把 BoundFolder 运行时临时项目当作可改归属引用，不接受同源、锁定源/目标、失效引用、注入输入、超过 256 项、修订/拓扑/选择漂移。它也不把自动证据入口冒充物理鼠标和屏幕可见证据。

## 2. 代码审计与实现

- 新增 `ProductDesktopReferenceReassignmentAdapter`：只有点中已选项目才能开始；6 DIP 后才成为拖动；冻结 intent lease、workspace revision、topology generation、window registry generation、selection revision 和有序项目集合；任一漂移立即取消。
- `WindowsProductDesktopHostReadOnlySurface` 在真实 HWND 上使用鼠标捕获跟踪手势，安全目标显示有限焦点框；完成、Esc、CaptureLost、CancelMode、投影变化均清理会话和反馈。
- `ProductDesktopHostLifecycleController` 根据最终屏幕坐标在当前权威显示批次命中唯一未锁定目标，支持跨显示器坐标解析，并再次核对当前显式选择与批次代次。
- `ProductDesktopReferenceReassignmentAdmissionAdapter` 将匿名 `item:{ordinal}` 映射回正式状态，拒绝 `folder:*` 临时项目、失效引用、同源/缺失/锁定盒子、目标显示不一致及重复序号。
- `App` 以当前权威拓扑和状态调用既有 `CommitResolvedReferenceReassignment`；成功后使用统一文档重载路径发布，保存拒绝时既有 coordinator 不推进 revision、不发布中间状态；现有一次撤销令牌保持有效。

## 3. 预期—实际—差异

| 检查 | 预期效果 | 实际效果 | 差异与修正 |
|---|---|---|---|
| 手势准入 | 只能从已选正式引用开始，短距离点击不能误提交或吞掉原有打开动作 | 首轮实现会在已选项按下时进入 Pending，阈值内释放虽零提交但吞掉单击；已修正为阈值内释放恢复原选择链，在单击打开模式真实执行一次选择和一次打开；越过 6 DIP 才拖动，注入输入拒绝 | 首轮发现行为差异并已修正；最终 `Difference=None` |
| 权威冻结 | 拖动期间 workspace/topology/selection 任一变化都取消 | 单测实际改变 selection revision 后 Update/Complete 返回空；Lifecycle 和 App 再做当前代次复核 | `Difference=None` |
| 目标安全 | 同源、锁定、缺失或非唯一目标不能提交 | Surface 不高亮同源/锁定目标；Lifecycle 只接受当前批次唯一未锁定命中；Admission 再次拒绝异常盒子 | `Difference=None` |
| 项目安全 | BoundFolder 临时项目、失效引用、重复/越界项目不能提交 | `folder:1:1`、Missing 引用、`item:1 + item:01`、0/越界及 257 项实际有限拒绝 | `Difference=None` |
| 原子提交 | 一次拖动只回调一次、保存一次，源 1→0、目标 0→1，并有一次撤销 | 真实 STA + 真实 HWND 场景实际 callback=1、save=1、source=0、target=1、undo token 非空 | `Difference=None` |
| 文件安全 | 改归属不得改变真实文件 | 真实隔离 Unicode 文件提交前后规范路径和 SHA-256 相同 | `Difference=None` |
| 产品物理证据 | 用户用真实鼠标看到拖动目标反馈并完成改归属 | 本阶段在真实 HWND 上使用正式 Surface 手势代码和证据入口驱动坐标，不是物理鼠标，也没有屏幕录像/Narrator | 保持 `ProductEvidencePending`，并入 M1 集中证据冲刺 |

## 4. 测试与门禁

- PF-007B 专项：4/4；包括真实 STA、真实 HWND、真实隔离文件、一次提交和文件哈希差异表。
- 全量 Release：1,272/1,272，0 skipped；Release 构建 0 warning / 0 error。
- 首轮 PR CI 的所有功能/原生测试通过，但干净 runner 报告 lines 89.75%（21,991/24,502），暴露本地报告对 Lifecycle 新分支的覆盖余量判断不可靠。没有降低门槛或重跑掩盖，而是新增 Surface→Lifecycle 的真实权威目标路由测试，覆盖接受、注入、陈旧修订、同源、空白目标和选择漂移。
- 修正后单份 Cobertura 为 lines 90.40%（22,150/24,502）、branches 75.94%（7,329/9,651），稳定通过 90%/75% 门槛。
- `dotnet format --verify-no-changes`、`git diff --check` 和 188-ID UI 合同通过。
- 完整跨进程 UIA 仍在应用启动前被已审计 WindowsAppRuntime 2.4 / Microsoft.UI.Xaml 3.2 线程 fail-fast 组合阻断；`-ContractOnly` 通过。未绕过风险、未把未执行物理证据写成 Pass。

## 5. 需求对齐结论与下一步

PF-007B 没有扩展为真实文件移动，也没有转向教程、自动整理、小组件或插件。它补齐了竞品桌面盒子核心旅程中“项目在盒子之间调整归属”的正式产品工程路径；PF-007A/B 至此均为工程完成、物理证据待补。

下一唯一执行项转为 **M1 产品证据冲刺**：在可丢弃 Windows 测试账户完成 BOX-R1-C 的真实 Explorer 背景命令安装/菜单/卸载、FOLDER-R1 的物理 Picker/刷新/打开、真实 Explorer 指针 Link 拖入、PF-007B 物理盒子间拖动，以及 UI-R1E 键盘、高对比、减少动画和 Narrator。M1 退出后才进入 TASKBAR-R1～R4。
