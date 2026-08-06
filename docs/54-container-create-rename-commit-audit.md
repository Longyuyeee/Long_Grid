# Long方格正式容器创建与重命名提交审计

> 审计日期：2026-08-06
>
> 范围：正式容器首次创建、已有容器重命名、共享 revision 与保存提交
>
> 结论：容器名称编辑进入正式配置链；桌面文件操作、容器删除与布局编辑仍关闭

## 1. 需求对齐

本切片承接正式工作区只读视图，开放第一组真实容器编辑：

- 没有保存配置时，用户可以明确输入名称并创建第一个正式方格；
- 已有可编辑 session 中，用户可以选择方格并重命名；
- 创建/重命名只改变 Long方格配置，不创建目录，也不移动、删除、打开或重命名桌面文件；
- 备份只读、SafeMode、失败或不可确认的 session 不开放编辑；
- 锁定容器继续由 Core reducer 拒绝重命名。

本轮不包含删除容器、锁定/折叠开关、颜色/透明度、布局拖动、跨动作撤销、DesktopHost 或文件整理动作。

## 2. 统一并发与保存边界

原引用提交协调器扩展为统一 `ProductWorkspaceCommitCoordinator`。引用编辑和容器编辑共享：

```text
single lock
  -> current edit revision check
  -> reference gate or container reducer
  -> v1 projection
  -> exactly one save-controller Submit
  -> monotonic edit revision advance
  -> App in-memory Document/session/view/review rebuild
```

因此，一个已接受的容器改名会立即使旧引用审查 token 失效；一个外部导入/恢复也会使旧容器编辑 revision 失效。NoChange、stale revision、锁定、无效名称、非法序号和 save rejection 均不推进 revision、也不提交保存。

UI 只持有稳定序号和 edit revision，不接收 container ID、DisplayKey、坐标、路径或文件身份。App 在当前受锁状态内把序号解析到容器，并生成仅供持久化使用的内部 ID。

## 3. 首次配置与默认布局

当正式 Store 状态为 Missing 时，创建动作以 v1 空配置作为受验证基线。新容器使用：

- 随机且不进入 UI 的内部 container ID；
- `display-unassigned` 占位显示键；
- 360 × 240 DIP 保守默认尺寸和有界错位坐标；
- 蓝色、0.88 不透明度、展开、未锁定、零引用。

这些值只保证 v1 配置有效并为后续布局编辑提供可迁移起点，不表示 DesktopHost 已把容器放到真实显示器。无引用配置不依赖 Desktop Catalog，因此即使 Catalog 尚未就绪，也可以安全建立可编辑 session；只要存在引用，原有权威 Catalog 门禁不变。

## 4. 交互与辅助功能

正式工作区卡新增已有方格选择、256 字符上限名称输入、“创建并保存”“重命名并保存”和有限状态区。按钮默认禁用，只有当前 presentation 明确允许且名称非空时启用。

新增 6 个稳定 AutomationId，UI 合同由 98 增至 104。状态明确区分 Accepted、NoChange、StaleEditRevision、ReducerRejected、SaveRejected 与 InvalidRequest，并固定输出 `DesktopFilesChanged=False`。机器状态不含动态名称、内部 ID 或路径；可见名称仍作为产品内容供视觉和辅助功能用户读取。

## 5. 自动证据

- 统一协调器覆盖首次创建、序号重命名、旧 revision、NoChange、锁定/非法序号、空白名称、引用 token 跨类型失效和真实 Store 重载；
- 真实 Store 集成测试验证容器名称从 Before 变为 After，同时被引用测试文件仍存在且内容完全不变；
- session 测试验证无引用主配置可在 Catalog 未连接时安全加载，含引用配置仍保持 AwaitingCatalog；
- UI 源码合同覆盖 104 个 AutomationId、256 字符输入、默认禁用、共享协调器、首次空配置、有限状态与身份隔离；
- Debug/Release 全解决方案构建均为 0 warning / 0 error，全量测试 337/337；
- Release 覆盖率 lines 91.49%（8236/9002）、branches 82.92%（2010/2424），高于 90%/75% 门禁；
- 格式、启动、单实例、Issue #19/#20/#23/#24 安全会话链和依赖漏洞门禁均通过；人工与专用环境结果继续保持 Pending；
- 当前 Windows 会话仍有 `MainWindowHandle=0` 的残留 LongGrid.App PID 39208，真实 UIA `FindFirst` 返回 `E_UNEXPECTED`，因此保持 Inconclusive；本轮没有终止无权限进程。

## 6. 下一步

下一切片建议开放容器锁定与折叠状态。锁定是后续外观、布局和删除操作的共同门禁；折叠是展示状态，不应改变引用或文件。完成后再依次推进外观编辑、布局编辑和删除/撤销，桌面文件整理动作继续单独审批。
