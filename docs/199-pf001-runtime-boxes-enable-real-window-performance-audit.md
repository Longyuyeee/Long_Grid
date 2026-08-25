# Stage 199：PF-001 运行期开启方格真实窗口性能审计

日期：2026-08-25  
开发项：Gate A / PF-001 运行期开启性能证据  
结论：**Engineering Pass / Product Runtime Performance Pass / Accessibility Evidence Pending**

## 1. 用户目标

正式 App 已运行时，用户关闭“显示桌面方格”后，Long方格必须释放自己的桌面窗口而不删除布局；再次开启后，应通过同一设置与生命周期产品路径在 1000 ms 内恢复真实 DesktopHost 窗口。

本切片只关闭这一条性能与真实窗口证据，不把它扩张为 Narrator、跨进程 UIA 树、签名安装或 PF-001 顶层 Product Complete。

## 2. 审计中发现并修正的问题

第一版证据直接调用 `ProductDesktopHostLifecycleController.SetUserEnabled`。虽然使用真实 App 和 HWND，但绕过了控制中心的原子设置保存，因此不能代表用户实际开关效果。

最终实现把现有控制中心开关逻辑提取为唯一 `ChangeBoxesEnabledAsync` 产品路径：

- 控制中心 Toggle 与证据会话调用同一方法；
- 计时包含 `ProductBoxesSettingsController.ChangeAsync` 的真实临时目录原子保存；
- 保存成功后复用同一 `SetUserEnabled`、投影刷新和窗口工厂；
- 保存失败仍保持原状态并显示有限回滚文案；
- 没有第二份生产开关或证据专用生命周期实现。

## 3. 真实证据设计

新增 `ProductBoxesRuntimeEnableEvidenceSession` 与 `eng/Test-LongGridBoxesRuntimeEnable.ps1`。证据使用随机临时配置目录和独立 AppInstance key，采用三段双向握手：

1. 正式 Release App 完成配置、桌面目录、显示拓扑和 DesktopHost 初始化；App 写入 ready，外部脚本按 PID 枚举 Win32 顶层窗口并确认 DesktopHost HWND 可见；
2. App 通过真实设置路径关闭方格并写入 disabled，外部脚本确认该 PID 的可见 DesktopHost HWND 数量为 0 后回执；
3. App 收到回执后开始计时，通过同一设置路径重新开启；内部验证 Native Host、被动窗口合同和 OwnedWindowCount，外部再次确认真实 HWND 恢复。

随后脚本真实启动第二实例，验证重定向到唯一控制中心，用 `WM_CLOSE` 正常排空，并确认零残留进程。测试不查询跨进程 UIA、不发送输入、不改变显示设置、不读取文件内容，也不写用户正式配置。

## 4. 正向真实结果

连续执行三次：

```powershell
pwsh ./eng/Test-LongGridBoxesRuntimeEnable.ps1 `
  -Configuration Release `
  -MaximumRuntimeEnableMilliseconds 1000 `
  -NoBuild
```

| 项目 | Expected | Actual | Difference |
| --- | --- | --- | --- |
| 初始可见 DesktopHost HWND | ≥1 | 1 / 1 / 1 | None |
| 关闭后可见 DesktopHost HWND | 0 | 0 / 0 / 0 | None |
| 开启后可见 DesktopHost HWND | ≥1 | 1 / 1 / 1 | None |
| 运行期开启耗时 | ≤1000 ms | 41 / 92 / 40 ms | 最慢 -908 ms |
| 产品内部窗口合同 | Native + Passive attested | 通过 / 通过 / 通过 | None |
| 第二实例 | 退出 0、激活唯一控制中心 | 符合 | None |
| 正常退出残留 | 0 | 0 / 0 / 0 | None |

三次样本的 min/median/max 为 40/41/92 ms。本轮目标是验收 1 秒上限，不以三个样本伪报 P95。

## 5. 负向真实结果

将外部门禁显式收紧到 1 ms，仍运行完整真实产品链：

| 项目 | Expected | Actual | Difference |
| --- | ---: | ---: | ---: |
| 运行期开启预算 | ≤1 ms | 31 ms | +30 ms |
| Outcome | Fail | Fail | None |
| 退出码 | 非零 | 1 | None |
| Difference | 有限差异 | `RuntimeBoxesEnableExceededBy30Milliseconds` | None |

因此门禁不是只记录耗时；真实超限会使测试失败。

## 6. 安全与需求对齐

- 对齐 PF-001：关闭释放窗口，开启恢复缓存的最新权威投影；
- 对齐 iTop/Fences 的常驻启停体验：恢复明显低于 1 秒目标；
- 对齐本地与可逆原则：只写隔离设置，布局和桌面文件不删除、不移动；
- 对齐唯一产品路径：证据没有绕过保存和失败回滚；
- 对齐真实测试要求：正式 App、真实 Win32 HWND、真实原子设置写入、真实第二实例和真实关闭；
- 对齐 Expected / Actual / Difference：正向和负向均机器可读并影响退出码。

## 7. 剩余差距与状态

PF-001 的运行期开启 ≤1000 ms 证据现在关闭，Stage 198 的场景语义问题也已完成纠偏。但以下仍 Pending：

- 真人 Narrator 与跨进程 UIA 子树在关闭/开启前后的完整证据；
- 高对比、文本缩放和物理控制中心 Toggle 旅程；
- 签名安装后的开机启动、升级和多账户证据；
- 冷启动多样本/P95 与驻留唤醒 P95 <300 ms。

因此 PF-001 继续为 `EngineeringComplete / ProductEvidencePending`，不能标记 Product Complete。

## 8. 下一步

Gate A 的冷启动门和运行期开启门均已建立。下一治理动作是把当前长期功能分支通过 PR、完整 GitHub CI 和主分支回归集成；集成完成后进入 PF-006C1 PageUp/PageDown 跨视口键盘导航，不扩大任务栏或 Widgets 范围。
