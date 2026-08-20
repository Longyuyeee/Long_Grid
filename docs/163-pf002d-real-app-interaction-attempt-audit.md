# Stage 163：PF-002D 正式 App 实机交互尝试审计

- 日期：2026-08-20
- 分支：`codex/pf002d-create-preview`
- 目标：在正式 Release App 执行打开预览、名称校验、取消和确认的真实 Windows 交互矩阵
- 结论：**窗口捕获通过；外部控制器激活触发 WinUI 崩溃，输入矩阵未执行，PF-002D 继续 Pending**

## 1. 预期矩阵

1. 唯一正式 `LongGrid.App.exe` 启动并保持响应；
2. 从 DesktopHost 创建入口打开候选位置原生预览；
3. 默认名称获得焦点并全选；
4. 空白/重名名称禁用确认，合法名称恢复；
5. Cancel/Escape/失焦关闭窗口，配置 revision 和容器数不变；
6. 确认只创建一个方格；
7. 全程不读取、移动或修改桌面文件。

## 2. 实际执行

| 步骤 | 实际结果 | 判定 |
| --- | --- | --- |
| 启动 | Release App 被唯一枚举为“Long方格”窗口 | Pass |
| 初次窗口捕获 | 取得真实截图；Long方格位于微信窗口后方 | Pass |
| 外部控制器激活 Long方格 | 控制器返回“foreground window did not report a process id” | Fail |
| 刷新窗口绑定 | Long方格窗口和进程已消失 | Fail |
| Windows 崩溃证据 | Application Error：`Microsoft.UI.Xaml.dll`、`0xc000027b`；WER 参数 `0x8001010e` | Confirmed |
| 无外部激活对照 | 同一 Release App 连续运行 20 秒，`Responding=True`、主窗口有效 | Pass |
| 预览输入矩阵 | 因目标进程已崩溃且旧句柄失效，按安全规则停止 | Pending |

## 3. 差异判断

`0x8001010e` 对应 COM/RPC wrong-thread 类错误，但当前只在实机控制器夺取前台时出现；普通启动 20 秒稳定。现有证据不能区分：

- Windows 实机控制器/云桌面前台桥的异常激活序列；
- WinUI 在特殊外部激活序列下的产品缺陷；
- 前台窗口无进程身份时 Windows App SDK 的边界问题。

因此本轮不修改产品线程模型、不吞掉 WinUI 未处理异常，也不改用陈旧坐标、PowerShell UIA 或模拟结果。把控制器失败写成产品 Pass 会违反真实测试要求；仅凭 WER 参数修改产品同样缺少证据。

## 4. 需求对齐

- Stage 162 的 989/989、Release 构建、原生窗口代码和 20 秒稳定启动仍有效；
- PF-002D 的“真实打开—编辑—取消—确认”没有通过，状态不得升级；
- 零桌面文件操作边界没有变化；
- PF-002E 可以使用可控存储故障注入继续工程开发，但不能替代 PF-002D 的真人/实机证据。

## 5. 后续门禁

在无并发用户输入、非云桌面前台桥或可附加 WinUI 崩溃转储的 Windows 会话中重跑相同矩阵。若普通人工激活也复现 `0x8001010e`，必须取得堆栈并作为 P0 产品崩溃修复；若只在控制器桥复现，则记录工具/环境限制并用合规人工证据完成 PF-002D。
