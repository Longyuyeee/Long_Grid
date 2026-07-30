# P0-07b2b2b2b4b1：显示/会话动态矩阵受控采证工具

日期：2026-07-30

结果：**Conditional Pass（只读采证、脱敏判定、baseline 与无事件防假阳性通过；真实硬件/会话场景尚未执行）**

## 1. 可证伪假设

在操作员从 Windows 设置、投影界面、硬件、系统电源或 RDP 客户端手动触发变化时，Long Grid 的隐藏消息窗口能够：

1. 接收与场景匹配的公开 Windows 通知；
2. 在窗口过程外读取 CCD/monitor/DPI 快照；
3. 丢弃旧 generation 的后台结果；
4. 在变化结束后以连续两次一致快照回到 `Ready`；
5. 只输出脱敏的场景、事件类别、计数、状态和资源数据；
6. 未观察到预期事件时返回 `Inconclusive`，而不是制造 Pass。

本报告只验证采证基础设施。它不声称真实缩放、旋转、拔插、投影、锁屏、RDP 或睡眠恢复已经通过。

## 2. 官方合同

- [`WM_DISPLAYCHANGE`](https://learn.microsoft.com/windows/win32/gdi/wm-displaychange) 向顶层窗口通知显示分辨率变化；
- [`WM_DPICHANGED`](https://learn.microsoft.com/windows/win32/hidpi/wm-dpichanged) 携带新 DPI 与建议矩形，Per-Monitor DPI aware 窗口应处理该矩形；
- [`WM_DEVICECHANGE`](https://learn.microsoft.com/windows/win32/devio/wm-devicechange) 通知设备配置变化；
- [`WM_POWERBROADCAST`](https://learn.microsoft.com/windows/win32/power/wm-powerbroadcast) 提供挂起与恢复通知；
- [`WTSRegisterSessionNotification`](https://learn.microsoft.com/windows/win32/api/wtsapi32/nf-wtsapi32-wtsregistersessionnotification) 为已注册窗口提供会话变化，并要求销毁窗口前配对注销；
- Microsoft 的[无障碍测试指南](https://learn.microsoft.com/windows/apps/design/accessibility/accessibility-testing)要求自动检查与人工键盘、Narrator、缩放和辅助技术验证组合使用；
- [AccScope](https://learn.microsoft.com/windows/win32/winauto/accscope)用于检查 Narrator 的元素顺序与实际朗读文本。

## 3. 实现

`--matrix-scenario` 在既有 `DisplayChangeMessageProbe` 之上增加场景化验收，不建立第二套消息管线：

| 场景 | 至少观察到 |
|---|---|
| `baseline` | 无额外事件要求；启动采样必须稳定 |
| `scale` | DPI 或 DisplayConfiguration |
| `rotate` | DisplayConfiguration 或 Device |
| `attach` / `detach` | DisplayConfiguration 或 Device |
| `projection` | DisplayConfiguration 或 Device |
| `lock-unlock` | SessionUnavailable 且 SessionAvailable |
| `remote-session` | SessionUnavailable 且 SessionAvailable |
| `sleep-resume` | PowerSuspend 且 PowerResume |

不同驱动和 Windows build 可能发出多个合法通知，因此显示类场景采用公开事件的允许集合，不要求固定消息数量。会话和电源场景要求按顺序观察暂停后恢复；报告只保存相对毫秒时间，不保存消息参数或系统身份。

结果语义：

- `Observed Pass` / exit 0：采证生命周期正常、最终 `Ready`、本场景全部预期信号出现；
- `Inconclusive` / exit 4：采证基础设施正常，但缺少预期信号；
- `Fail` / exit 2：快照、生命周期、资源闭环或最终稳定状态失败；
- 参数错误 / exit 64。

## 4. 安全与隐私

探针不会调用显示设置、投影、设备、电源、锁屏或 RDP 的修改 API。所有变化都由操作员在进程外执行。

JSON 不包含：

- monitor/GDI 名称；
- PNP、EDID、设备路径；
- adapter LUID、source/target ID；
- 会话 ID、窗口标题；
- 原始或散列拓扑指纹；
- 操作员自由文本。

固定场景枚举防止把机器名、工位、客户或显示器信息误写进报告。若团队需要保存 GPU、dock、显示器型号和录像，必须放在访问受控的实验记录中，不进入默认诊断包。

## 5. 自动验证

### Baseline

```powershell
dotnet run --project probes/LongGrid.Spikes.DisplayTopology `
  --configuration Release --no-build -- `
  --matrix-scenario baseline --watch-seconds 5 --json
```

三次独立进程的正式结果：

| 指标 | Run 1 | Run 2 | Run 3 |
|---|---:|---:|---:|
| 结果 | Observed Pass | Observed Pass | Observed Pass |
| FinalState | Ready | Ready | Ready |
| generation | 1 | 1 | 1 |
| Snapshot 成功/失败 | `2/0` | `2/0` | `2/0` |
| Ready 次数 | 1 | 1 | 1 |
| USER | `1→1` | `1→1` | `1→1` |
| GDI | `0→0` | `0→0` | `0→0` |
| 进程句柄 | `253→253` | `253→253` | `253→253` |
| 系统修改请求 | false | false | false |

原有 `--watch-seconds 3` 静态路径也回归为 `Conditional Pass`，确认场景层没有改变默认只读观察语义。

### 防假阳性

声明 `scale`，但不修改系统缩放：

```powershell
dotnet run --project probes/LongGrid.Spikes.DisplayTopology `
  --configuration Release --no-build -- `
  --matrix-scenario scale --watch-seconds 5 --json
```

结果为 `Inconclusive`、exit 4；预期事件 `Observed=false`。这证明工具不会把启动稳定采样误写成缩放场景通过。

## 6. 人工执行规程

每次只验证一个场景：

1. 保存所有工作，记录原分辨率、缩放、方向、主屏和投影模式；
2. 确认没有文件操作、安装或系统更新正在运行；
3. 启动对应 observer，通常使用 60–180 秒；
4. observer 启动后，由操作员执行一次变化，再恢复原状态；
5. 等待进程自行退出，不强杀；
6. 确认 JSON 为 `Observed Pass`、FinalState `Ready`、SnapshotFailures `0`；
7. 人工检查窗口布局没有越界、输入遮挡、焦点抢占或持续震荡；
8. 保存脱敏 JSON、屏幕录像编号、机器矩阵编号和人工结论；
9. 任何一步失败都先恢复系统原状态，再保留现场和日志。

示例：

```powershell
# 改缩放后恢复
dotnet run --project probes/LongGrid.Spikes.DisplayTopology `
  --configuration Release --no-build -- `
  --matrix-scenario scale --watch-seconds 90 --json

# 旋转后恢复
dotnet run --project probes/LongGrid.Spikes.DisplayTopology `
  --configuration Release --no-build -- `
  --matrix-scenario rotate --watch-seconds 120 --json

# 拔出并重新接回测试显示器/扩展坞
dotnet run --project probes/LongGrid.Spikes.DisplayTopology `
  --configuration Release --no-build -- `
  --matrix-scenario detach --watch-seconds 180 --json
```

锁屏、RDP 和睡眠场景必须给恢复后稳定采样预留时间。若设备或策略阻止对应通知，结论保持 `Inconclusive`，不得手工改写为 Pass。

## 7. Narrator 与真实输入门禁

P0-07b2b2b2b4a 的 alpha=1 探针没有产品视觉内容，也没有真实可操作 Pattern，因此不能作为 Narrator 体验通过的对象。Narrator/真实输入必须等第一个 DesktopHost 垂直切片具备可见容器、项目、焦点和操作模式后执行。

最低人工矩阵：

- Inspect/UIA Verify：Raw、Control、Content View 中只出现预期节点；
- AccScope Narrator 模式：容器和项目顺序与视觉/键盘顺序一致；
- Narrator：朗读 Name、ControlType、状态和位置语义；
- 键盘：Tab、Shift+Tab、方向键、Enter、Space、Esc 全流程；
- 鼠标：打开、双击、框选、拖放、滚轮、容器边界穿透；
- 触控/笔：命中、滚动、长按和拖动；
- 输入门关闭期间：不得调用操作 Pattern、改变选择或把焦点留在即将移除的 Fragment；
- 恢复后：焦点回到仍存在的逻辑项目，否则回到容器根并由 Narrator说明。

听读正确性必须由人工确认；AutomationId 存在不等于 Narrator 体验通过。

## 8. 下一门禁

- 在受控实验机逐项执行 scale、rotate、attach/detach、projection、lock/unlock、RDP、sleep/resume；
- 对每个场景完成“变化 + 恢复原状态”的视觉、输入、资源和稳定性复核；
- 建立可见 DesktopHost 垂直切片后执行 Narrator 与真实输入矩阵；
- 补充 Win+D、Peek、全屏、Alt+Tab、任务视图和 Explorer 重启矩阵；
- 覆盖 Windows 10/11、x64/ARM64 与 Intel/AMD/NVIDIA。

在真实场景未执行前，P0-07b2b2b2b4b 总项保持未完成。
