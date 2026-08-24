# Stage 185：PF-004C 桌面方格“更多”菜单与安全导航审计

- 日期：2026-08-24
- 分支：`codex/pf002d-create-preview`
- 起始基线：`15bc619`
- 对齐编号：`PF-004C`
- 结论：`EngineeringComplete`；PF-004 顶层仍为 `InProgress`

## 1. 目标与范围冻结

PF-004C 解决“桌面标题只有直接折叠/锁定，没有其他管理入口”的缺口。本阶段不复制第二套编辑器，也不提前执行 PF-004D 删除：

- 每个方格标题命令面从三个扩为四个 32 DIP 目标，视觉/UIA 顺序为锁定、折叠、进入交互、更多；
- “更多”打开产品 activation HWND 所属线程上的真实 Win32 `#32768` 弹出菜单；
- 重命名、外观、方格列表排序只导航到唯一控制中心的准确方格与准确控件，打开菜单和导航本身零配置写入；
- 创建规则、Portal/Tab 先作为明确的后续功能显示并禁用；
- 删除项显示“下一阶段确认”并禁用，PF-004C 不能绕过 PF-004D 的默认取消确认；
- 锁定、只读或保存失败时，重命名/外观在菜单打开前已禁用；排序是只读视图能力，仍可用；
- 请求继续绑定 container、display、workspace revision、topology generation 和来源证明，拒绝注入、自动重复、陈旧与错误显示器；
- Esc、失焦、系统表面隐藏和 Surface 释放均由原生菜单取消/`EndMenu` 收敛，取消零请求。

## 2. 实现审计

### 2.1 原生有限菜单

`WindowsProductDesktopInteractionActivationSource` 新增 `OpenMoreMenu` 区域和动态可用性回调。UIA Name 为“更多 {方格名} 管理操作”，Invoke 只把打开请求投递回窗口拥有线程，再由该线程调用 `CreatePopupMenu`、`AppendMenuW` 和 `TrackPopupMenuEx`。这避免 UIA RPC 线程直接拥有菜单，也不获取前台窗口、不发送输入、不监听全局消息。

菜单固定为：

1. 重命名…；
2. 外观…；
3. 方格列表排序…；
4. 创建规则（后续功能，禁用）；
5. 生成 Portal / Tab（后续功能，禁用）；
6. 删除方格配置…（下一阶段确认，禁用）。

GDI 标题文字预留从 44 DIP 调整到 140 DIP，避免四个按钮覆盖两行标题事实；长文本继续单行省略，不改变方格或 DesktopHost 窗口尺寸。

### 2.2 双重状态复核与精确导航

Lifecycle 在菜单打开和选择时复核来源仍属于当前 batch/display/container，并补上当前 revision/topology。`ProductDesktopContainerMenuNavigationController` 在 App UI 队列中再次检查：

- 目标必须唯一且仍在同一显示器；
- revision/topology 必须精确相等；
- 来源已证明且非注入、非自动重复；
- 重命名/外观要求可编辑、未锁定且保存不处于 Failed；
- 排序允许只读和锁定状态，但仍要求目标与代次有效。

通过后 App 激活唯一控制中心，按验证所得 ordinal 选择方格，并聚焦名称编辑框、颜色选择器或排序选择器。状态只发布 `Changed=False:DesktopFilesChanged=False`，不制造配置提交。

### 2.3 非目标和权限边界

- 没有删除、移动、重命名任何真实桌面文件；
- 没有直接从菜单提交名称或外观值；最终提交仍由现有控制中心按钮和唯一提交/保存链负责；
- 没有实现规则、Portal、Tab 或 PF-008 的方格内排序；本阶段“方格列表排序”准确指控制中心现有列表视图排序；
- 没有模拟 Explorer 菜单、修改 Shell、任务栏或前台窗口策略。

## 3. 真实 Expected / Actual

### 3.1 真实 HWND 与原生菜单

测试创建真实 activation HWND，经 UIA Invoke 调用“更多”，实际观察 `#32768` Win32 菜单窗口，并从 Windows UI Automation 读取菜单项名称和 Enabled 状态。测试专用关闭由窗口拥有线程的 `WM_TIMER` 调用 `EndMenu`，不发送键鼠输入。

| 项目 | Expected | Actual | 差异 |
| --- | --- | --- | --- |
| 标题按钮数 | 4 | 4 | 无 |
| 四个目标（96 DPI） | 各 `32×32` | 各 `32×32` | 无 |
| 原生菜单窗口 | `#32768` 可见 | 可见 | 无 |
| 重命名/外观/排序 | 显示且启用 | `true/true/true` | 无 |
| 规则/Portal-Tab/删除 | 显示且禁用 | `false/false/false` | 无 |
| 取消结果 | 零菜单请求 | 0 | 无 |
| 前台/输入 | 不抢前台、不发送输入 | 未发送输入 | 无 |

### 3.2 真实 Store 与失败状态

| 场景 | Expected | Actual | 差异 |
| --- | --- | --- | --- |
| 三个导航动作 | 精确命中 ordinal 1 | 三项均 `Accepted:Ordinal=1` | 无 |
| 导航前后配置字节 | 不变化 | 完全一致 | 无 |
| 导航前后写入时间 | 不变化 | 完全一致 | 无 |
| 真实写租约失败 | `WriteLeaseUnavailable` | 一致 | 无 |
| 失败时菜单状态 | 重命名/外观禁用，排序启用 | `false/false/true` | 无 |
| 失败期间磁盘名称 | `Work` | `Work` | 无 |

真实 Store 测试输出结构化 `Expected`、`Actual` 与 `Difference=None`；失败证据复核后使用正式“外部基线替换”清除不可处置的失败重试，没有把测试编辑写入磁盘。

## 4. 测试差异与修正

| 轮次 | 预期 | 实际差异 | 修正 |
| --- | --- | --- | --- |
| 首次 App 编译 | 0 warning/error | CA1822 先后指出 availability 与 Handle 均不访问实例状态 | 将导航控制器明确改为纯静态策略，不压制分析器 |
| 首次失败测试 | 失败状态可直接结束 | SaveController 拒绝携带未处理失败候选 Dispose | 释放写租约并调用正式外部基线替换，证明磁盘原值后安全结束 |
| 首次真实菜单关闭 | 跨线程 `EndMenu` 可关闭 | 菜单未可靠退出，聚焦测试挂起 | 停止准确测试进程；改由菜单拥有线程 `WM_TIMER` 关闭，真实菜单测试通过 |
| 按钮顺序复审 | 键盘进入仍命中 Enter 区域 | 由三按钮改为左到右四按钮后，旧代码仍取 `regions[0]`，实际变成锁定区域 | 按 Kind 查找唯一未锁定 Enter 区域，不依赖数组位置 |

以上均修正实现或测试生命周期，没有降低真实断言，也没有把挂起菜单冒充通过。

## 5. 门禁结果

- PF-004C/Lifecycle/UIA 聚焦：`66/66`；
- Release 全量：`1097/1097`；
- Release App 构建：`0 warning / 0 error`；
- 153-ID UI 合同及 PF-004C 原生菜单/安全导航合同：通过；
- 真实 HWND、真实 `#32768` 菜单和 OS UIA 菜单项：通过；
- 真实 Store 零写入与真实写租约失败：`Difference=None`；
- 正式 Release App：DesktopHost `1,304 ms` 就绪、持续响应 20 秒、重定向后唯一控制中心、退出零进程/零临时配置写入，`Difference=None`；
- 未执行 Windows Capture、跨进程 WinUI UIA、键鼠注入或桌面文件操作。

## 6. 需求对齐与下一步

PF-004C 已把 iTop/Fences 类“方格上就近管理”的更多入口接入正式 DesktopHost，同时复用 Long方格唯一编辑器和安全状态，不产生第二套配置逻辑。当前真实证据证明原生菜单内容/状态/取消、代次路由和零写入；尚未把物理鼠标选择菜单到可见 WinUI 焦点记录为 Product Evidence，因此不能扩大为完整真人可用性结论。

PF-004 顶层仍缺高风险删除的默认取消确认、统一最近撤销，以及菜单选择后的真人/无障碍证据，继续为 `InProgress`；30 个 PF 仍为 `0 Complete`。

下一切片固定为 **PF-004D：桌面删除确认与统一撤销**：启用菜单删除入口但只创建绑定 container/revision/topology 的确认会话；明确“只删 Long方格配置，不删真实文件”，默认按钮必须取消；确认后二次复核并复用现有 Remove/保存失败补偿/最近撤销，取消、Esc、失焦、陈旧状态和锁定方格均零提交。完成 PF-004D 后再总审 PF-004 是否可转为 `EngineeringComplete / ProductEvidencePending`。
