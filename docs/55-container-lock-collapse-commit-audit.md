# Long方格正式容器锁定与折叠提交审计

> 审计日期：2026-08-06
>
> 范围：正式容器锁定/解锁、折叠/展开、共享 revision 与保存提交
>
> 结论：容器状态编辑进入正式配置链；桌面文件操作、外观数值与布局编辑仍关闭

## 1. 需求对齐

本切片在创建/重命名之后开放两项低风险容器状态：

- 锁定：阻止重命名、外观、布局和内容关系修改；
- 解锁：始终允许用户显式恢复编辑能力；
- 折叠/展开：只改变容器的 `Appearance.Collapsed` 配置，不移除或改变任何引用；
- 锁定容器不能折叠/展开，必须先解锁，避免锁定语义出现例外；
- 备份只读、SafeMode、失败或不可确认 session 继续关闭全部容器编辑。

本轮不包含颜色、透明度、布局拖动、容器删除、撤销栈、DesktopHost 和文件整理动作。

## 2. 提交与并发边界

统一 `ProductWorkspaceCommitCoordinator` 新增 `SetLocked` 和 `SetCollapsed` 两个有限动作。请求仍只携带：

- action；
- 期望 edit revision；
- 容器稳定序号；
- 可空布尔状态值。

UI 不接收 container ID、DisplayKey、坐标、路径或文件身份。协调器在同一锁内检查 revision 和序号，再调用 Core reducer：锁定使用 `SetContainerLocked`，折叠使用 `UpdateAppearance`。之后继续执行 v1 projection、唯一 save-controller Submit、revision 推进和 App 内存基线重建。

NoChange、stale revision、锁定拒绝、无效请求和 save rejection 均不推进 revision，也不产生额外保存。

## 3. 交互与视觉

正式容器编辑区新增“锁定并保存/解锁并保存”和“折叠并保存/展开并保存”两个状态按钮。选择容器后，按钮文字从当前 session presentation 单向派生：

- 已锁定：锁定按钮显示“解锁并保存”，折叠按钮禁用；
- 未锁定：锁定按钮显示“锁定并保存”，折叠按钮按当前状态显示折叠或展开；
- session 刷新后尽量保持原容器序号选择，避免状态提交后跳回首项；
- 状态按钮与现有卡片、主题 brush、圆角和静态 Reduced Motion 基线一致。

新增 2 个稳定 AutomationId，总数由 104 增至 106。UIA 机器状态只包含 action、revision、有限结果和固定 `DesktopFilesChanged=False`，不记录动态容器名称。

## 4. 安全证据

- 显式锁定后重命名被拒绝，显式解锁仍可提交；
- 折叠只改变 Appearance，未解析引用仍保留；重复折叠是 NoChange；
- 锁定容器折叠被 Core reducer 拒绝且保存 revision 不变；
- 真实 Store 集成测试连续保存重命名与折叠，重载后名称/折叠状态正确，被引用测试文件仍存在且内容完全不变；
- UI 合同覆盖 106 个 AutomationId、默认禁用、当前状态映射、共享协调器和隐私边界；
- Debug 与 Release 解决方案构建均为 0 警告、0 错误；Release 全量测试 340/340 通过；
- Release 静态 UI 合同通过，覆盖 106 个 AutomationId；启动链与单实例源码合同通过；
- 行覆盖率 91.58%（4145/4526），分支覆盖率 83.27%（1025/1231），均高于 90%/75% 门槛；
- Issue #19、#20、#23、#24 的 `ValidateOnly` 安全入口均通过，并继续保持 Pending，不冒充人工或专用环境证据；依赖漏洞门禁未发现已知漏洞；
- 本机真实 UIA 在 `AutomationElement.FindFirst` 返回 `0x8000FFFF (E_UNEXPECTED)`。该结果记为 **Inconclusive**：静态合同已通过，但残留无窗口旧进程造成的环境阻塞未获终止许可，因此不把它记作功能 Pass 或 Fail；
- GitHub PR CI 与合并后 `main` CI 在发布流程中复核。

## 5. 后续方向

下一切片建议开放颜色与透明度编辑，但必须使用设计 token/受限颜色格式和有限数值，不允许任意 XAML/brush 输入。之后再进入布局坐标/尺寸编辑；布局能力需要与显示拓扑和 DesktopHost 恢复合同对齐。容器删除与跨动作撤销仍应在布局稳定后单独准入。
