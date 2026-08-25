# Stage 162：PF-002D2 桌面候选位置原生预览审计

- 日期：2026-08-20
- 分支：`codex/pf002d-create-preview`
- 目标：把 PF-002D1 的控制中心预览推进为位于目标显示器候选方格位置的产品自有原生编辑窗口
- 结论：**PF-002D2a Engineering Pass；完整 App 原生预览交互证据 Pending，PF-002D/PF-002 保持 `InProgress`**

## 1. 开发前差异

| 检查项 | 对标预期 | 开发前实际 |
| --- | --- | --- |
| 预览位置 | 在桌面候选方格位置直接出现 | 激活控制中心并显示 `ContentDialog` |
| 任务切换 | 临时编辑面板不成为独立日常窗口 | 只存在控制中心窗口 |
| 名称编辑 | 默认聚焦/全选，非法名称就地修正 | 控制中心对话框已支持 |
| 取消 | Cancel/Escape/失焦/状态失效零提交 | Cancel 和状态失效已有；桌面失焦语义未承载 |
| 多显示器/DPI | DIP 候选映射到目标显示器绝对像素并裁剪 | 只显示文字摘要，没有原生窗口坐标 |

## 2. 本轮实现

### 2.1 权威候选坐标

新增 `ProductDesktopWorkspaceCreatePreviewPlacement.ResolveWindowBounds`：

- 只接受有限候选、有效 work area 和 48～768 DPI；
- 把相对显示器的 DIP 位置/尺寸换算为绝对虚拟屏幕像素；
- 支持负坐标显示器；
- 把过大或越界候选裁剪到目标 work area；
- 非法输入返回 null，禁止创建窗口。

### 2.2 产品自有原生预览窗口

新增 WinUI `DesktopWorkspaceCreatePreviewWindow`：

- 使用候选方格的绝对位置与尺寸，而不是控制中心位置；
- 无标准标题栏、不可缩放/最大化/最小化、不进入任务切换列表；
- 使用亚克力、圆角、有限边框和扁平按钮；
- 默认名称编辑框、候选位置/尺寸摘要、有限校验文本、确认和取消；
- Enter 确认、Escape 取消；获得激活后名称全选；之后失焦即取消；
- 暴露 Root、NameEditor、Validation、PlacementSummary、Confirm、Cancel 六个动态 AutomationId；
- 确认按钮只由 Preview Snapshot 的 `CanSubmit` 控制；
- 关闭和外部取消幂等，旧异步 continuation 不能恢复 session。

正式 App 优先使用该原生窗口；如果 Windows AppWindow/Backdrop/Presenter 创建失败，则激活控制中心并回退到 Stage 160 对话框。两条 UI 路径共用同一个 Preview Session、实时校验、二次 admission 和唯一提交协调器，回退不是第二套创建逻辑。

## 3. 失败和安全边界

| 场景 | 结果 | 副作用 |
| --- | --- | --- |
| 有效候选 | 在目标显示器候选位置打开原生预览 | 确认前零配置提交 |
| 负坐标副屏 | 使用 work-area origin 加相对 DIP | 不错误移回主屏 |
| 越界/过大候选 | 有限裁剪到 work area | 不产生屏外不可恢复窗口 |
| 非法候选/DPI | 拒绝预览并显示有限失败 | 零创建 |
| 原生窗口创建失败 | 回退控制中心预览 | 不绕过校验/确认 |
| 空白/重名名称 | 禁用确认并显示有限提示 | 零创建 |
| Escape、取消、失焦 | 关闭窗口并返回 null | 零创建 |
| revision/topology/display/host 变化 | App 取消 session 和窗口 | 旧回调不能提交 |
| 确认 | 二次复核后调用唯一 commit | 不操作桌面文件 |

本窗口是候选位置上的独立产品 HWND，而不是把 WinUI 子控件嵌入 Explorer 或桌面 WorkerW；窗口外没有产品 hit-test 区域。当前也没有安装 Shell Extension、全局 Hook 或模拟输入。

## 4. 真实测试：预期与实际

| 验证 | 预期 | 实际 | 结果 |
| --- | --- | --- | --- |
| Preview 状态与坐标聚焦测试 | 17/17 | 17/17 | Pass |
| 负坐标 150% DPI | `(-1860,170,540,360)` | 精确相等 | Pass |
| 过大候选裁剪 | 完全落入 800×600 work area | 精确相等 | Pass |
| 无效尺寸/DPI | fail closed | 4/4 返回 null | Pass |
| UI 源码合同 | 原生窗口、六个 UIA ID、无切换项、失焦取消、控制中心回退均存在 | Pass | Pass |
| Release 全量测试 | 989/989 | 989/989 | Pass |
| Release 构建 | 0 warning / 0 error | 0 warning / 0 error | Pass |
| 正式 App 启动 | 8 秒内存活、响应、唯一主窗口 | PID 18756 存活且 `Responding=True`，窗口标题“Long方格” | Pass |
| 原生预览打开—编辑—取消 | 可见、默认全选、非法/合法切换、取消零 revision | 实机控制器检测到窗口最小化/并发用户输入并停止，未执行点击/输入 | **Pending** |

当前不能把静态合同、Core 几何或 App 启动冒充原生预览真实交互。完整 App 矩阵仍必须在无并发输入的可控 Windows 会话中执行。

**后续实机结果**：Stage 163 已捕获正式窗口，但外部控制器激活时触发 `Microsoft.UI.Xaml.dll / 0xc000027b / 0x8001010e` 并使进程退出；同一构建在无外部激活时连续 20 秒稳定。由于未取得足以区分产品与控制器前台桥的堆栈，未修改线程模型，输入矩阵继续 Pending，详见 [Stage 163](163-pf002d-real-app-interaction-attempt-audit.md)。

## 5. 对标与需求对齐

| 对标能力 | 当前状态 |
| --- | --- |
| 创建前在目标位置命名 | 工程实现完成，实机待证 |
| 现代、扁平、轻量预览 | 原生 WinUI 亚克力/圆角/无标题栏实现 |
| 多屏/DPI 候选位置 | 自动化通过 |
| 取消零惊吓 | 状态/代码合同通过，实机待证 |
| 区域外桌面继续可用 | 独立有限窗口不覆盖区域外；真实桌面行为待证 |
| Explorer 原生桌面嵌入 | 明确不实现，不接管 WorkerW |
| 保存失败不留幽灵方格 | 仍待 PF-002E |

本轮继续对齐“桌面优先、明确确认、零惊吓、现代化 UI”，没有扩大真实文件权限。它比 Stage 160 更接近 iTop/Fences 的桌面就地体验，但在真实输入证据完成前不得标记产品完成。

## 6. 严格下一步

1. 在无并发用户输入的 Windows 会话执行空态/非空态入口、名称编辑、Enter/Escape、失焦、取消零 revision 和确认单创建矩阵；
2. 验证预览窗口在主屏、负坐标副屏和 100%/150%/200% DPI 的实际像素位置；
3. 验证 Narrator/UIA Edit/Invoke、文本缩放、高对比和任务切换不可见；
4. 真实证据通过后关闭 PF-002D；
5. 进入 PF-002E 保存与可见发布补偿事务，解决幽灵方格风险。
