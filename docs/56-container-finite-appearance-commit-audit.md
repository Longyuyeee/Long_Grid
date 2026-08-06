# Long方格正式容器受限外观提交审计

> 审计日期：2026-08-06
>
> 范围：正式容器固定色板、固定透明度档位、共享 revision 与安全保存
>
> 结论：颜色和透明度进入正式配置编辑链；自由颜色/数值、布局和桌面文件操作仍关闭

## 1. 需求与边界

本切片承接容器锁定/折叠之后的外观准入，但不开放任意 XAML、Brush、ARGB、CSS 名称或连续浮点输入：

- 固定颜色：晴空蓝 `#2563EB`、品牌靛蓝 `#5B5FF5`、石板灰 `#334155`、翡翠绿 `#059669`、琥珀橙 `#D97706`；
- 固定透明度：实体 100%、清晰 88%、柔和 72%、轻盈 56%；
- UI 只传递两个有限枚举，Infrastructure 在提交边界映射为 v1 的 `#RRGGBB` 与有限 double；
- 加载到不属于当前预设的历史合法值时，不擅自归一化；选择器保持未选，用户必须明确选择完整预设后才能保存；
- 锁定、只读备份、SafeMode、旧 revision、无效序号和保存拒绝继续阻止外观写入；
- 外观提交不改变名称、折叠状态、引用、布局或任何桌面文件。

## 2. 提交协议

`ProductWorkspaceContainerCommitAction.SetAppearancePreset` 与既有 Create/Rename/SetLocked/SetCollapsed 共享 `ProductWorkspaceCommitCoordinator`。请求包含 expected edit revision、容器稳定序号及两个可空预设枚举；该动作要求其他动作字段为空、两个枚举同时存在且均为已定义值。

协调器在同一锁内完成 revision/序号检查、有限值映射、Core `UpdateAppearance`、v1 projection、唯一 save-controller Submit、revision 推进和 App 内存基线重建。NoChange、未定义枚举、锁定拒绝和保存失败均不推进 revision，也不产生额外保存。

## 3. 交互与可访问性

正式容器编辑卡新增“颜色预设”“透明度”两个 ComboBox 和“应用外观并保存”按钮：

- 选择容器时仅在当前值精确匹配预设时回显；
- 两项均已明确选择、容器未锁定且值确有变化时，提交按钮才启用；
- 锁定容器的两个选择器和提交按钮全部禁用，并提示先解锁；
- 状态文本继续只暴露 action、revision、有限结果、Changed 与 `DesktopFilesChanged=False`；
- 新增 3 个稳定 AutomationId，总数从 106 增至 109；保持主题 token、键盘导航和 Reduced Motion 静态基线。

## 4. 安全与测试证据

- 协调器测试覆盖有限颜色/透明度映射、NoChange revision 稳定、折叠/引用保持、锁定拒绝和未定义枚举拒绝；
- 真实 Store 集成链连续执行重命名、折叠和外观提交；重载后配置正确，被引用测试文件存在且内容完全不变；
- UI 源码合同覆盖选择器默认禁用、唯一 audited handler、有限动作、presentation 隐私边界和 109 个 AutomationId；
- Debug/Release 解决方案构建均为 0 警告、0 错误；Debug/Release 全量测试均为 342/342 通过；
- Release 静态 UI 合同通过，覆盖 109 个 AutomationId；启动链与单实例源码合同通过；
- 行覆盖率 91.51%（4181/4569），分支覆盖率 83.27%（1060/1273），均高于 90%/75% 门槛；
- Issue #19、#20、#23、#24 的 `ValidateOnly` 安全入口均通过且继续保持 Pending；依赖漏洞门禁未发现已知漏洞；
- 本机真实 UIA 在 `AutomationElement.FindFirst` 返回 `0x8000FFFF (E_UNEXPECTED)`，继续记为 **Inconclusive**；静态合同已通过，但未获许可终止残留无窗口旧进程，因此不把环境阻塞记作产品 Pass 或 Fail；
- GitHub PR CI 与合并后 `main` CI 在发布流程中复核。

## 5. 后续方向

下一切片进入受限布局编辑：先定义坐标/尺寸的有限范围、显示器归属、DPI 单位、最小可见面积、锁定语义与拓扑变化恢复合同，再开放 UI。容器删除、跨动作撤销、DesktopHost 接线和真实文件整理继续独立准入，不能与布局编辑捆绑发布。
