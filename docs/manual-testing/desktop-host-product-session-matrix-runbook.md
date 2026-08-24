# DesktopHost 只读产品会话矩阵运行手册

状态：Ready to execute / Pending manual evidence

本矩阵验证正式 Long方格 DesktopHost，而不是交互探针。启动器只设置开发期精确开关并启动应用；它不会发送输入、改变显示/会话状态、截屏或写入结果。操作者必须使用匿名标签并在专用可恢复环境中一次只执行一个场景。

## 预检

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-DesktopHostProductSessionMatrix.ps1 `
  -ValidateOnly
```

正确输出必须包含 `PendingManualEvidence`。预检不启动 App。

## 场景

| ID | 受控人工动作 | 必须复核 | 通过条件 |
|---|---|---|---|
| A5-01 | Narrator/Inspect 读取正式方格 | Root→方格→可见项目；折叠方格无项目子节点 | 名称、数量、折叠状态、Bounds 正确；无 Selection/Invoke；不读出路径 |
| A5-02 | Win+D 与再次恢复 | 方格、普通窗口、控制中心的可见关系 | 不抢焦点、不置顶、不留下透明输入层，恢复后代次一致 |
| A5-03 | 进入/退出独占或无边框全屏应用 | 全屏覆盖、退出恢复、前台稳定 | 方格不强行覆盖全屏，不激活，不持续闪烁 |
| A5-04 | Explorer 正常重启 | 方格 HWND 所有权和控制中心状态 | 不依赖 WorkerW/Progman；无孤儿窗口，必要时安全隐藏并恢复 |
| A5-05 | 锁定/解锁或本地→RDP→本地 | 会话往返、DPI/WorkArea、资源 | 旧代次不提交，无错误区域或输入遮挡，回到有限状态 |
| A5-06 | 关闭控制中心与重复启动/退出 | HWND、USER/GDI/handle、单实例 | 全部产品表面释放，无孤儿进程；默认关闭启动仍为零 DesktopHost HWND |
| PF003D5-01 | 用物理鼠标抓住未锁定方格标题，从一块屏幕拖到另一块不同 DPI 屏幕后释放 | 抓取点、逻辑宽高、目标工作区、保存后重启 | 拖动连续且不跳变；尺寸/抓取偏移保持；重启仍在目标屏；不移动真实桌面文件 |
| PF003D5-02 | 把物理鼠标跨过含负虚拟坐标的屏幕边界，再移到所有权威显示器之外取消 | 候选所属 Surface、源 Surface 残影、取消恢复 | 候选只在目标屏；源屏无残影；离屏取消精确恢复源 placement，配置零提交 |
| PF003D5-03 | 跨屏拖动期间由操作者执行显示器断开/缩放或 WorkArea 变化 | topology generation、候选、保存 revision、恢复 | 陈旧手势失败关闭；无旧目标提交、无透明遮挡；稳定拓扑后可重新操作 |
| PF003D5-04 | 在 100%/150%/200%/250%/300%/400% 可用组合下分别执行移动、八向缩放、键盘 1/8 DIP 微调 | 可见 Bounds、最小尺寸、焦点框、Narrator 名称/状态 | DIP/像素换算正确，无裁剪/跳变；锁定方格拒绝；UIA Bounds 与可见边界一致 |
| PF003D5-05 | 使用触控或笔完成移动、取消和边缘缩放（设备可用时） | capture 生命周期、滚动/系统手势冲突、唯一提交 | 一次手势最多一次保存；取消零保存；不吞掉 Surface 外系统输入；无设备则记 Inconclusive |

## 启动示例

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ./eng/Start-DesktopHostProductSessionMatrix.ps1 `
  -Scenario A5-01 `
  -OperatorId O1 `
  -AcknowledgeControlledEnvironment `
  -AcknowledgeRecoveryPlan `
  -Configuration Release
```

启动前应准备至少一个正式方格和仅适合展示的测试引用。不得在会话中使用包含隐私名称的真实工作区，也不得把路径、设备 ID、窗口标题或账户信息写入仓库。

## 结果纪律

- 自动化 UIA 测试通过不等于 Narrator 人工体验通过；
- 观察到公开事件或窗口重新出现不等于最终 Pass；
- 任意焦点抢占、错误 Z-order、透明输入遮挡、旧代次提交、孤儿 HWND 或无法恢复均为 Fail；
- 环境不支持、动作未完成或证据不足必须记录为 Inconclusive；
- A5-01..A5-06 均有脱敏、可复读证据并确认恢复后，才能把 A5 真实会话矩阵标记完成。
- PF003D5 场景必须记录 Expected、Actual、Difference 与修正后复测；自动构造指针事实、原生 HWND 单测或进程内 evidence 不能替代物理鼠标/触控/Narrator。
- 当前已知 Windows App Runtime 2.4.0.0 / Microsoft.UI.Xaml 3.2.3.0 组合必须让跨进程 UIA 在启动前安全拒绝；不得使用危险确认参数把可能崩溃的会话冒充通过。可在上游已修复的稳定运行时或独立安全机器执行 UIA/Narrator 子项。
