# Stage 183：PF-004A 正式桌面方格标题信息与无障碍状态审计

- 日期：2026-08-24
- 分支：`codex/pf002d-create-preview`
- 起始基线：`b35161e`
- 对齐编号：`PF-004A`
- 结论：`EngineeringComplete`；PF-004 顶层仍为 `InProgress`

## 1. 本阶段目标

PF-004 要求用户直接在桌面方格上理解并管理名称、项目数、状态和常用动作。审计发现正式 DesktopHost 原先只画名称：

- 投影只保留最多 12 个可见项目，标题无法知道真实项目总数；
- 折叠只通过高度变化体现，锁定没有标题状态；
- UIA Name 使用英文 `visible items`，ItemStatus 只有旧“只读”说明；
- 右上角箭头实际语义是进入 Explicit 交互，不是更多菜单，不能改名冒充 PF-004 完成。

本阶段因此冻结为 PF-004A：先建立一份视觉和 UIA 共用的有限标题事实。折叠/锁定/更多菜单的直接命令、删除确认和撤销仍属于紧接的 PF-004B～PF-004D。

## 2. 实现结果

### 2.1 同一标题事实

新增 `ProductDesktopContainerHeaderPresentation`，统一生成：

- 展开/折叠方向标记和名称；
- 项目总数，而不是最多 12 个可见名称的数量；
- 当前来源 `安全引用`；
- `已锁定/可整理` 与 `已折叠/已展开`；
- 中文 Narrator 名称；
- 保留“只读方格”边界的有限机器状态。

GDI Surface 和 UIA Provider 都从 `projection.Header` 读取，不维护两份可能漂移的状态文案。

### 2.2 总数与可见上限分离

`ProductDesktopHostReadOnlyProjection` 新增 `TotalItemCount`：

- 可见项目仍最多 12 个，避免扩大渲染和 UIA 表面；
- 总数必须不小于可见数且不超过产品 500 项上限；
- 布局预览跨显示器复制时保留总数；
- Projection Builder 从正式 read model 传入容器真实项目数。

### 2.3 正式标题布局

54 DIP 标题区改为两行：第一行是折叠标记和名称，第二行是总数、来源、锁定和折叠状态；右侧继续为既有有限交互入口保留 44 DIP，不让长标题覆盖按钮。名称与状态继续使用单行省略，不扩大窗口、输入 region 或文件访问范围。

## 3. 真实 Expected / Actual

测试实际创建 `WindowsProductDesktopHostReadOnlySurface`，取得真实非零 HWND、正式原生窗口标题和 Passive 窗口合同，再读取该 HWND 实际使用的标题 presentation。由于 Stage 181 已确认 Windows Capture 会触发已知 WinUI 上游崩溃，本轮不截图、不跨进程查询 UIA、不发送输入。

| 项目 | Expected | Actual | 差异 |
| --- | --- | --- | --- |
| 原生窗口 | 存在 | 非零且 `IsWindow=true` | 无 |
| 原生窗口标题 | `Long方格桌面只读宿主` | 一致 | 无 |
| Passive 合同 | `true` | `true` | 无 |
| 视觉标题 | `▸ 工作资料` | 一致 | 无 |
| 视觉状态 | `7 项 · 安全引用 · 已锁定 · 已折叠` | 一致 | 无 |
| 无障碍名称 | `工作资料；7 个项目；安全引用；已锁定；已折叠` | 一致 | 无 |

真实证据输出为 `Purpose=Pf004aRealNativeHeaderSurfaceEvidence`、`Difference=None`、`Outcome=Pass`。

## 4. 测试差异与修正

| 轮次 | 预期 | 实际差异 | 修正 |
| --- | --- | --- | --- |
| 首次编译 | 真实 HWND 测试可运行 | CA1838 拒绝 P/Invoke `StringBuilder` | 使用固定 `char[]` 缓冲区，没有禁用分析器 |
| 首次全量 | 既有 UIA 边界不回退 | 1091 项中 1 项失败：新 ItemStatus 丢失“只读” | 保留“只读方格”前缀，再追加机器状态；1091/1091 |

第二项是实际产品兼容问题：若只修改旧测试，会让 Narrator 用户失去安全边界提示，因此选择修复实现。

## 5. 门禁结果

- PF-004A + 原生 Surface/UIA 聚焦：`5/5`；
- Release 全量：`1091/1091`；
- Release App 构建：`0 warning / 0 error`；
- 153-ID UI 合同及 PF-004A 视觉/UIA 同源合同：通过；
- 原生 HWND Expected/Actual：`Difference=None`；
- 正式 Release App：`2,235 ms` DesktopHost 就绪并持续响应 20 秒，退出后零活进程/零临时配置写入；
- NuGet 已知漏洞：`0`；
- Windows Capture、跨进程 UIA、输入注入、桌面文件写入：均未执行。

## 6. 需求对齐与下一步

PF-004A 关闭了“标题只显示名称、总数/来源/锁定/折叠状态不可见”的差距，但没有把显示信息冒充可操作闭环。PF-004 顶层继续为 `InProgress`，30 个 PF 仍为 `0 Complete`。

下一切片为 PF-004B：在既有产品有限输入窗口内增加至少 32×32 DIP 的折叠/展开与锁定/解锁按钮，绑定 container id、workspace revision、topology generation 和输入来源事实，通过正式配置事务保存并在失败时回滚；锁定后必须仍能解锁，但不能移动、缩放或删除。之后再做 PF-004C 更多菜单/重命名/外观/排序入口，以及 PF-004D 删除确认和统一撤销。
