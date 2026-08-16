# Stage 155：PF-002 桌面空状态创建入口实现与验收审计

- 日期：2026-08-16
- 开发基线：`main@f6734a4a9ffcc842fa0b54692a1493b0c636c5cf`
- 分支：`codex/pf002-desktop-empty-create-entry`
- 范围：PF-002 第一步——DesktopHost 空状态入口、默认首个方格、统一配置事务
- 结论：**本切片 Engineering Pass；PF-002 整体保持 `InProgress`**

## 1. 本轮解决的真实产品差距

Stage 100 的“开始创建第一个方格”位于控制中心，只把焦点移动到既有名称编辑器。用户仍需先找到并操作管理窗口，桌面为空时 DesktopHost 不创建任何 Surface，因此与 iTop Easy Desktop、Stardock Fences 以桌面为主要操作面的体验存在直接差距。

本轮不是再次增加控制中心按钮，而是让权威空工作区在主显示器形成正式 DesktopHost 空状态：用户可在桌面点击“创建第一个方格”，直接提交默认空方格，不需要进入控制中心。创建继续复用唯一 `ProductWorkspaceCommitCoordinator` 和既有保存队列，不建立第二套配置写入链。

## 2. 与对标产品的逐项对齐

| 对标能力 | 本轮 Long方格行为 | 对齐状态 | 后续缺口 |
| --- | --- | --- | --- |
| 桌面为空时有明确下一步 | 主显示器中央显示空状态卡片与创建按钮 | 已对齐 | 视觉精修、真人缩放证据 |
| 不进入管理面板即可创建 | 点击或 UIA Invoke 直接创建默认“新方格” | 已对齐首个方格 | 提交前就地命名尚未实现 |
| 桌面其余区域正常使用 | Surface Window Region 只覆盖有限卡片；卡片非按钮区域返回 `HTTRANSPARENT` | 工程对齐 | 物理鼠标/触控实机复核 |
| 创建后立即看到分组 | 成功提交触发同一 session/read model/DesktopHost 投影刷新，空 Surface 被正式方格 Surface 替换 | 已对齐 | 保存失败后的可见补偿待完善 |
| 多种桌面创建入口 | 当前仅空状态按钮与 UIA Invoke | 部分对齐 | 右键、键盘、绘制矩形仍待实现 |
| 创建时可命名和调整范围 | 当前使用默认“新方格”、360×240 DIP | 未完全对齐 | 就地名称编辑与创建预览待实现 |
| 从已选项目创建 | 不读取或接管 Explorer 原生选择 | 未实现 | 仅允许未来使用 Long方格自身选择 |

## 3. 权威准入链

空状态 Surface 只在以下事实同时成立时产生：

1. 正式工作区状态和读模型均有效；
2. 配置方格计数明确为零；
3. 显示拓扑为权威状态，且恰有一个主显示器；
4. `BoxesEnabled` 用户开关有效，且没有紧急安全禁用；
5. 投影 revision/generation 不落后于当前生命周期。

`EmptyWorkspace` 更新现在携带一个零容器 batch。该 batch 只包含权威主显示器的稳定 ID、工作区矩形、DPI 和拓扑指纹。非权威拓扑、状态不一致、空显示列表或多个主显示器仍失败关闭，不产生桌面入口。

## 4. DesktopHost 表面与输入边界

### 4.1 有限窗口区域

- 空状态只建立一个主显示器 Surface；
- 可见 Region 是居中的 360×184 DIP 卡片，不覆盖整个桌面命中区域；
- 创建按钮为卡片内 248×48 DIP；
- 卡片之外没有窗口 Region；
- 卡片内非按钮位置返回 `HTTRANSPARENT`；
- 按钮使用 `WS_EX_NOACTIVATE` 与 `MA_NOACTIVATE`，不得抢占前台窗口；
- 已有方格 Surface 继续使用原有 `WS_EX_TRANSPARENT` 被动合同。

### 4.2 输入来源

- 原生主点击必须由 `GetCurrentInputMessageSource` 证明不是 injected；
- UIA 通过标准 `InvokePattern` 进入同一个有限创建回调；
- 回调始终调度回 WinUI Dispatcher，避免在 WindowProc/UIA 调用栈中销毁当前 Surface；
- 队列执行时再次复读权威显示器和 `AwaitingWorkspace` 状态；
- 连续重复请求中，首个成功请求使生命周期离开空状态，其余陈旧请求被拒绝。

本切片不安装全局 Hook、不使用 Raw Input、不发送模拟输入，也不接管 Explorer 的鼠标、键盘或选择。

## 5. 创建事务

桌面请求调用与控制中心相同的容器提交核心：

1. 复读当前 edit revision；
2. 缺少首份配置时从正式空配置建立 state；
3. 将权威显示拓扑写入待保存 state；
4. 创建唯一 GUID 容器 ID；
5. 使用默认名称“新方格”；
6. 使用请求 Surface 的权威显示器 ID；
7. 使用 32×48 DIP 起点和 360×240 DIP 默认尺寸；
8. 通过 `ProductWorkspaceCommitCoordinator.CommitContainer` 一次提交；
9. 成功后重建正式 session/read model/DesktopHost 投影。

创建不读取文件内容、不移动、删除、隐藏或重排 Windows 原生桌面项目，初始方格不包含任何引用。

## 6. 失败与恢复矩阵

| 场景 | 预期结果 | 本轮状态 |
| --- | --- | --- |
| 拓扑不是权威状态 | 不创建空 Surface | 自动化覆盖既有投影失败关闭 |
| 工作区不是明确空状态 | 不提供入口 | 生命周期投影约束通过 |
| 方格总开关关闭 | 释放空状态 Surface/UIA | 复用 PF-001 生命周期测试 |
| 紧急禁用 | 用户回调不能创建 Surface | 复用 PF-001 安全优先级 |
| 点击卡片非按钮区域 | 穿透到桌面 | 原生 hit-test 合同已实现，实机待复核 |
| 注入鼠标消息 | 拒绝创建 | 原生来源门已实现 |
| UIA Invoke | 请求同一创建回调 | Windows UIA 自动化通过 |
| 连续重复请求 | 首个提交后其余请求复读失败 | 状态复读已实现，压力测试待后续统一创建流 |
| 配置提交被 reducer 拒绝 | 不应用新 session | 复用统一 coordinator |
| 异步保存失败 | 当前保存状态会报告失败 | **仍缺桌面幽灵方格补偿，PF-002 不得完成** |
| Surface 创建/复读失败 | 生命周期进入 `Faulted` 并释放资源 | 复用既有 Host fault 合同 |
| 关闭应用 | 销毁空 Surface/UIA 与回调资源 | 生命周期释放路径覆盖 |

## 7. 无障碍合同

- 空根节点 AutomationId：`LongGrid.DesktopHost.Root`；
- 空根名称：`Long方格桌面空状态`；
- 创建按钮 AutomationId：`LongGrid.DesktopHost.EmptyCreateButton`；
- 按钮名称：`创建第一个方格`；
- 按钮类型：UIA `Button`；
- 按钮模式：标准 `InvokePattern`；
- ItemStatus 明确声明创建默认空方格且不读取或移动桌面文件；
- Invoke 不把 DesktopHost 设为前台窗口。

该 AutomationId 属于原生 DesktopHost UIA 子树，不改变 WinUI XAML 的 146-ID 源码合同计数。

## 8. 本地自动验收

- 空投影/生命周期/原生 UIA 专项：`60/60`；
- 全量测试：`945/945`；
- Release 解决方案构建：`0` warning、`0` error；
- `dotnet format --verify-no-changes`：通过；
- `git diff --check`：文档提交前复核；
- PR/main CI：推送后记录远端结果。

## 9. PF-002 验收记账

| Stage 153 验收目标 | 当前证据 | 状态 |
| --- | --- | --- |
| 三种首版入口进入同一创建流 | 空状态鼠标/UIA 已统一；右键/键盘未实现 | 部分通过 |
| 新方格位于当前显示器可见工作区 | 主显示器 ID 与有限默认位置/尺寸已提交 | 首个方格通过 |
| 名称异常有确定处理 | 当前只生成固定合法默认名称 | 部分通过，就地编辑待实现 |
| 保存失败不留下幽灵方格 | 仍沿用异步保存可见状态，未完成桌面补偿 | 未通过 |
| 连续创建 20 个方格 | 当前入口只适用于首个方格 | 未覆盖 |
| 鼠标、键盘、触控、UIA 一致 | 非注入主点击与 UIA 已接线；其余实机/入口待完成 | 部分通过 |
| 不读取内容、不移动真实文件 | 仅提交零引用配置关系 | 通过 |
| 无需控制中心建立首个方格 | 桌面空状态直接创建默认方格 | 工程通过 |

## 10. 下一开发步骤

1. PF-002B：统一创建默认值、“新方格 2”等稳定去重和 20 方格 Core 压力合同已完成，见 [Stage 156](156-pf002b-deterministic-create-defaults-audit.md)；
2. PF-002C：增加桌面右键和键盘入口，并确保与空状态入口生成同一请求；
3. PF-002D：增加提交前就地名称/位置/尺寸预览，支持 Esc、失焦、拓扑变化和安全门取消；
4. PF-002E：把保存完成纳入可见发布事务，失败时撤回 DesktopHost 新窗口，消除幽灵方格；
5. PF-002F：补充物理鼠标、键盘、触控、Narrator、高对比和 100%～400% DPI 证据；
6. 上述全部通过后才可把 PF-002 标记完成，再进入 PF-003 拖动、缩放与吸附。
