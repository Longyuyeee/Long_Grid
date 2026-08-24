# Stage 195：PF-006B2B1 可配置单击打开策略审计

- 日期：2026-08-24
- 分支：`codex/pf002d-create-preview`
- 起始基线：`8e04acd`
- 对齐编号：`PF-006B2B1 / PF-006B2B / PF-006`
- 结论：PF-006 `InProgress`；PF-006B2B1 工程切片完成

## 1. 开始审计与切片边界

Stage 194 后，Enter、项目双击和 UIA Invoke 已能共用权威安全打开命令，但产品仍没有用户可配置的单击打开策略。PF-006 要求保持 Windows 桌面习惯：默认双击，只有用户明确选择后才改为单击；选择必须先发生，Ctrl/Shift 单击仍只改变选择，不能误启动项目。

原计划 PF-006B2B 同时包含“重试、Explorer 定位、单击策略”。复审发现重试和定位需要新增动作授权、目标再次验证及路径不泄露边界，与输入策略不是同一最小闭环。本轮因此冻结为 B2B1，只交付默认关闭、显式持久化的单击策略；权威重试和安全 Explorer 定位保留到 B2B2，不能据此把 PF-006 标记完成。

## 2. 正式产品实现

### 2.1 配置与控制中心

- `ProductBoxesSettings` 增加 `openItemsWithSingleClick`，缺失字段和安全禁用配置均为 `false`；
- 设置只在用户切换控制中心开关后通过既有原子 Store 保存，保存失败恢复旧显示值，不伪报成功；
- 正式 App 加载设置后把策略下发到生命周期；生命周期同时记住策略并应用到当前及后续新建的每显示器 Surface；
- 控制中心明确显示“默认双击（推荐）/单击打开”，不存在首次启动自动启用或隐式配置写入。

### 2.2 DesktopHost 输入收敛

- 真实 Win32 Surface 在主按钮命中项目时先走既有共享选择控制器；
- 默认策略下单击仅选择，双击仍进入既有权威打开链；
- 显式开启后，普通、可信、非注入的项目单击以 `PointerSingleClick` 来源进入同一权威打开链；
- Ctrl/Shift 单击即使策略开启也只做选择；非可信来源失败关闭；
- 策略开启时抑制同一项目的双击入口，避免两个单击已经提交后再次重复打开；
- Surface 仍只提交 `item:ordinal` 和有限来源事实，真实路径继续只在 App 权威边界解析。

## 3. Expected / Actual / Difference 与修正

| 场景 | Expected | Actual | Difference / 修正 |
| --- | --- | --- | --- |
| 缺失旧配置 | 单击打开为关闭，且只读加载不写盘 | 真实临时 Store 加载为 `false`，文件仍不存在 | None |
| 显式开启并重启 | 设置保存为 `true`，新 Store 实例复读为 `true` | 完全一致 | None |
| 真实 HWND 默认单击 | 选择项目，打开次数 0 | 打开次数 0 | None |
| 真实 HWND 显式单击 | 选择后提交一次 `PointerSingleClick` | 打开次数 1，项目为 `item:1` | None |
| 开启后的 Ctrl 单击 | 只改变选择，累计打开次数仍为 1 | 累计打开次数 1 | None |
| 非可信单击证据 | 拒绝，累计打开次数仍为 1 | 返回拒绝，累计打开次数 1 | None |
| 生命周期新旧 Surface | 当前 Surface 和后建 Surface 使用同一策略 | 正式 lifecycle 接线和合同测试一致 | None |
| 单击模式双击消息 | 不再额外走双击打开入口 | 产品代码在单击策略开启时拒绝双击入口 | 自动合同通过；物理双击证据 Pending |

真实配置证据使用磁盘临时目录、生产 JSON Store 和新的 Store 实例验证重启复读。输入证据创建生产 `WindowsProductDesktopHostReadOnlySurface` 的真实原生 HWND，并通过 Surface 的受控证据入口提交与 Win32 消息处理共用的命中、选择和打开核心；它证明真实 HWND/产品处理链，但不是物理鼠标注入证据。物理单击/双击、真人高对比和 Narrator 仍明确 Pending。

## 4. 最终门禁

- Release 全量：1157/1157，0 failed，0 skipped；
- Release 全解决方案：0 warning、0 error；
- 真实磁盘 Store 默认关闭、显式保存和新实例复读：`Difference=None`；
- 真实 DesktopHost HWND 默认/显式/修饰键/非可信来源：`Difference=None`；
- UI 合同：157 AutomationId，PowerShell 7 `ContractOnly`，`Outcome=Pass`；
- `dotnet format --verify-no-changes` 与 `git diff --check`：通过；
- 零桌面文件移动、删除或重命名；零隐式配置写入；零路径进入 HWND/UIA 文本。

## 5. 需求对齐与下一步

PF-006B2B1 关闭了“默认双击、显式单击”的产品配置差距，没有扩大文件权限，也没有改变既有安全打开判定。PF-006 继续 `InProgress`。

下一工程切片固定为 **PF-006B2B2：权威失败重试与安全 Explorer 定位**。重试必须重新读取当前 workspace/Catalog/现场目标，不能复用旧的已解析路径；定位只有在安全父目录现场存在、非重解析点且重新授权时才出现，并继续禁止把路径写入 UIA、日志摘要或生命周期状态。其后仍欠 PageUp/PageDown 跨视口、框选、物理输入、高对比和 Narrator 证据；PF-001～PF-005 的产品证据、签名安装和公开分发门禁也继续 Pending。
