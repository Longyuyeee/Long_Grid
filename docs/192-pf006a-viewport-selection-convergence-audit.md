# Stage 192：PF-006A 视口选择收敛与真实 HWND 审计

- 日期：2026-08-24
- 分支：`codex/pf002d-create-preview`
- 起始基线：`5e91cb3`
- 对齐编号：`PF-006A / PF-006`
- 结论：PF-006 `InProgress`；PF-006A 工程切片完成

## 1. 开始审计与开发偏移

PF-006 已有单击、Ctrl、Shift、方向键、Home/End、Space 和 UIA SelectionItem 的共享选择控制器，但重新追踪 Stage 191 的 12 项视口后发现一个正式产品偏差：滚轮翻页改变 `ItemIds` 时，生命周期把它当作结构变化，释放原 Surface/HWND 并撤销显式交互。预期是翻页后仍在同一交互租约和窗口内，选择、焦点和 UIA 可见项确定收敛。

审计还确认两个当前视口缺口：`Ctrl+A` 未映射，方格项目下方的内容空白单击不会清除选择。框选、PageUp/PageDown 跨视口和打开命令仍未实现，本轮不宣称 PF-006 完成。

## 2. 实现与需求对齐

### 2.1 同 HWND 视口选择收敛

- 视口 ordinal、项目 ID/名称变化在 workspace revision、topology generation、容器结构和项目总数不变时，作为更晚 `PresentationGeneration` 原位更新；
- Surface 更新后，唯一 selection transaction 用当前可见 ID 复核同一 lease；无效、重复、超 256 项、lease 不匹配或过期均失败关闭；
- 新旧视口重叠时只保留仍可见选择；完全不重叠时清除选择，把焦点和 anchor 确定落到新页第一项；空页则均为空；
- 每次真实视口变化推进 selection revision，并重建 UIA selection snapshot、刷新 Surface；不可见 ID 不会残留在 UIA 或生命周期摘要；
- 容器消失或收敛失败仍走原有 Surface 重建/租约撤销，不保留陈旧交互。

### 2.2 当前视口基本选择补齐

- `Ctrl+A` 通过正式 keyboard adapter 映射到 `SelectAll`，只选择当前最多 12 项的有界视口，不扩张为 500 项隐式操作；
- 单击活动方格内容区中项目列表之后的空白，在无 Ctrl/Shift 时映射到共享 `Clear`；标题、方格外部、折叠方格及带修饰键的空白仍不产生选择请求；
- pointer、keyboard、UIA 继续共用同一个 selection controller；本轮没有文件打开、移动、删除或配置写入能力。

## 3. 真实 Expected / Actual / Difference

| 场景 | Expected | Actual | Difference / 修正 |
| --- | --- | --- | --- |
| 旧 Stage 191 翻页处于 Explicit | 保持同 HWND 和显式租约 | 旧逻辑因 ID 变化重建 Surface | 有；本轮改为受控 presentation + reconcile |
| 首轮 reconcile→UIA | 生成一致的显式 UIA 快照 | `Reconciled` 被旧校验拒绝 | 有；将新有限成功状态纳入一致性校验后复跑通过 |
| 选中 `item:2` 后翻到 13～24 | 选择 0、焦点/anchor=`item:13`、revision 2 | 完全一致 | None |
| 生命周期翻页 | 同 Handle、Surface 数 1、未 Dispose、Explicit 保持 | 完全一致，`ApplyPresentationCalls=1` | None |
| 真实 HWND/UIA 第二页 | 同 HWND、12 个 UIA Text、首项“项目 13” | 完全一致 | None |
| Ctrl+A | 当前视口全部选择且保留已有焦点 | 5 项控制器样本全部选择、焦点 `c` | None |
| 方格内容空白单击 | 共享 Clear 请求 | `ProductDesktopSelectionAction.Clear` | None |
| Windows PowerShell 5.1 UI 合同入口 | 解析 UTF-8 脚本 | 中文按旧编码解码并 ParserError | 有；改用 CI 对应 PowerShell 7 |
| PowerShell 7 Live UIA | 运行或有限拒绝 | 已知 WinAppRuntime/XAML fail-fast 组合在启动前拒绝 | 已知上游阻断；用 `-ContractOnly`，不伪造 Live Pass |

真实证据测试创建实际 DesktopHost HWND，经 `ApplyPresentation` 更新第二页，并从实际 `AutomationElement.FromHandle` 读取 12 个 UIA 子项及首项名称；同一测试同时断言正式选择控制器的选择、焦点和 revision。生命周期集成测试使用正式 forwarding→prepared intent→consumption→selection→presentation 链验证租约未被撤销。

## 4. 门禁结果

- Release 全量：1142/1142，0 failed，0 skipped；
- Release 全解决方案：0 warning、0 error；
- 真实 HWND PF-006A 专项：Expected/Actual 完全一致，`Difference=None`；
- UI 合同：157 AutomationId，PowerShell 7 `ContractOnly`，`Outcome=Pass`；
- Live UIA：已知 Windows App Runtime 2.4 / Microsoft.UI.Xaml 3.2.3 fail-fast 组合，启动前有限拒绝，继续 Pending；
- `dotnet format --verify-no-changes` 与 `git diff --check`：通过；
- 零桌面文件移动/删除/重命名，零打开进程，零配置写入。

## 5. PF-006 状态与下一步

PF-006A 已关闭“当前 12 项视口内选择”和“翻页后选择/焦点收敛”的正式链路，但 PF-006 仍为 `InProgress`。尚欠：鼠标框选、PageUp/PageDown 跨视口导航、Enter/双击/UIA Invoke 共用的安全打开命令、默认双击/可配置单击、系统关联与未知协议审计、失败原因和高对比/Narrator/物理输入证据。

下一工程切片进入 **PF-006B：统一安全打开命令**。先建立纯判定模型和有限失败状态，再把 Enter、项目双击与 UIA Invoke 接到同一命令；File/Folder 使用系统关联边界，Shortcut/URL 必须先验证解析状态和协议 allowlist。任何打开失败不得猜测成功，也不得改变 Long方格归属或真实文件位置。PageUp/PageDown、框选和产品证据继续显式保留，不混入打开权限扩张。
