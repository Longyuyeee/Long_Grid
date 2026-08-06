# Long方格正式容器受限布局预设提交审计

> 审计日期：2026-08-06
>
> 范围：正式容器有限位置/尺寸预设、DIP 配置提交、共享 revision 与安全保存
>
> 结论：配置层布局预设进入正式编辑链；显示器切换、自由坐标和真实窗口移动仍关闭

## 1. 需求与准入判断

v1 配置和 `ProductWorkspaceState` 已保存 `DisplayKey`、`XDip/YDip/WidthDip/HeightDip`，Core 已有 placement validator、`UpdatePlacement` reducer 和布局恢复规划器。当前 App 产品会话尚未连接可供用户确认的显示拓扑，因此本切片仅开放可复读的小范围预设：

- 位置：起始位 `(32,48)`、偏移一 `(56,72)`、偏移二 `(80,96)`、偏移三 `(104,120)` DIP；
- 尺寸：紧凑 `280×192`、标准 `360×240`、宽屏 `480×280`、大号 `560×360` DIP；
- 提交始终保留容器原 `DisplayKey`，不伪造显示器选择或重绑定；
- 历史合法自定义坐标/尺寸不自动归一化，只有用户同时明确选择位置和尺寸预设才可提交；
- 锁定、只读备份、SafeMode、旧 revision、无效序号和保存拒绝继续阻止布局写入；
- 本切片只修改配置，不调用 HWND、DesktopHost、布局恢复事务或桌面文件操作。

上述预设满足 v1 最小尺寸、最大尺寸和坐标绝对值边界，但在没有权威显示拓扑时不能证明当前显示器上的最小可见面积。真实呈现仍必须经拓扑解析、DPI 转换、`Automatic/ReviewRequired/Blocked` 计划和事务复读。

## 2. 提交协议

`ProductWorkspaceContainerCommitAction.SetPlacementPreset` 与现有容器动作共享 `ProductWorkspaceCommitCoordinator`。请求只携带 expected revision、稳定容器序号、一个位置枚举和一个尺寸枚举；该动作要求名称以外的其他动作载荷为空、两个枚举同时存在且均为已定义值。

协调器在同一锁内完成 revision/序号检查、有限 DIP 映射、Core `UpdatePlacement`、v1 projection、唯一 save-controller Submit、revision 推进和 App 内存基线重建。NoChange、未定义枚举、锁定拒绝和保存失败不推进 revision，也不产生额外保存。

## 3. 交互与隐私

正式容器编辑卡新增位置与尺寸 ComboBox，以及“应用布局并保存”按钮：

- 当前四个数值精确匹配预设时才回显；历史自定义值保持未选；
- 两项均有明确选择、容器未锁定且数值确有变化时按钮才启用；
- 状态明确说明只更新布局配置，尚未移动真实窗口或桌面文件；
- presentation 只获得 DIP 数值，不获得 `DisplayKey`、路径、内部容器 ID 或显示拓扑身份；
- 新增 3 个稳定 AutomationId，总数从 109 增至 112，保持主题 token、键盘导航和 Reduced Motion 静态基线。

## 4. 安全与测试证据

- 协调器测试覆盖有限 DIP 映射、DisplayKey/外观/引用保持、NoChange revision 稳定、锁定拒绝和未定义枚举拒绝；
- 只读模型测试覆盖位置/尺寸复制，同时继续省略 placement identity；
- 真实 Store 集成链连续执行重命名、折叠、外观和布局提交；重载后配置正确，被引用测试文件存在且内容完全不变；
- UI 源码合同覆盖默认禁用、唯一 audited handler、有限动作、presentation 隐私边界和 112 个 AutomationId；
- Debug/Release 解决方案构建均为 0 警告、0 错误；Debug/Release 全量测试均为 344/344 通过；
- Release 静态 UI 合同通过，覆盖 112 个 AutomationId；启动链与单实例源码合同通过；
- 行覆盖率 91.52%（4233/4625），分支覆盖率 83.66%（1106/1322），均高于 90%/75% 门槛；
- Issue #19、#20、#23、#24 的 `ValidateOnly` 安全入口均通过且继续保持 Pending；依赖漏洞门禁未发现已知漏洞；
- 本机真实 UIA 在 `AutomationElement.FindFirst` 返回 `0x8000FFFF (E_UNEXPECTED)`，继续记为 **Inconclusive**；静态合同已通过，但未获许可终止残留无窗口旧进程，因此不把环境阻塞记作产品 Pass 或 Fail；
- GitHub PR CI 与合并后 `main` CI 在发布流程中复核。

## 5. 后续方向

正式 session placement 与显示拓扑之间的双门禁已进入[产品布局恢复只读预览合同审计](58-product-layout-recovery-preview-contract-audit.md)：当前缺少权威产品拓扑时保持 Awaiting，未来权威拓扑接入后 v1 缺少保存时元数据则保持 SavedTopologyMissing。两侧元数据成立前不开放恢复确认或 DesktopHost 提交。容器删除、跨动作撤销和真实文件整理仍独立准入。
