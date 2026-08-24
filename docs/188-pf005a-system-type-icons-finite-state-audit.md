# Stage 188：PF-005A 系统类型图标与有限项目状态审计

- 日期：2026-08-24
- 分支：`codex/pf002d-create-preview`
- 起始基线：`27a3f66`
- 对齐编号：`PF-005A`
- 结论：`EngineeringComplete`；PF-005 顶层仍为 `InProgress`

## 1. 审计结论与范围冻结

PF-005 开始前复审确认：仓库已有零能力 AppContainer ThumbnailWorker、像素共享内存协议和故障隔离探针，但正式 App 只在资源稳定性受控遥测会话中启动 worker，DesktopHost 投影与绘制完全没有消费其结果。因此“worker 已存在”等于底层能力，不等于用户已经能看到图标或缩略图。

为避免把一次高风险异步接入做成不可验证的大改，本阶段冻结为 PF-005A：

- File、Folder、Shortcut、URL 映射到有限类型；
- DesktopHost 通过真实 Windows Shell stock icon API 获取系统图标；
- Resolved、Missing、TypeChanged、Ambiguous、UnsupportedTarget 映射到有限视觉状态；
- 非就绪状态显示明确短标签并回退系统警告图标，不出现破损空白；
- UIA 只读取可见名称、批准的类型和有限状态，不读取或暴露目标路径；
- 500 项配置仍只投影首屏上限 12 项，不创建 500 个图像请求；
- 100%～400% DPI 的图标尺寸由同一 20 DIP 规格确定。

图片缩略图、正式 worker 按需队列、缓存失效、开关与乱序结果仍属于紧接的 PF-005B；本阶段不冒充 PF-005 已完成。

## 2. 实现审计

新增 `ProductDesktopItemVisualPresentation`，类型枚举为 `File / Folder / Shortcut / Url`，状态枚举覆盖类型图标就绪、缩略图加载/就绪、离线、类型变化、身份歧义、不支持、无权限和失败回退。PF-005A 正式投影实际产生前五种引用解析对应状态；缩略图相关状态先作为 PF-005B 的有限合同，尚未声称已有运行结果。

`ProductDesktopHostProjectionBuilder` 从正式 ReadModel 生成与匿名 `item:n` 一一对应的视觉投影。视觉投影只保存类型与状态，不保存 canonical target、PersistedTarget 或路径。布局预览和批次等价比较也携带这些事实，避免状态变化后继续复用旧表面。

真实桌面 HWND 使用 `SHGetStockIconInfo` 获取 Document、Folder、Link、World 和 Warning 图标，按当前显示器 DPI 以 20 DIP 绘制，并在每次绘制后释放 HICON。离线/类型变化/歧义/不支持使用系统 Warning 回退；文字显示“离线 / 类型变化 / 待确认 / 不支持”，方格仍可选择和管理。

## 3. 真实 Expected / Actual

| 场景 | Expected | Actual | 差异 |
| --- | --- | --- | --- |
| Windows Shell 文件图标 | 可取得 HICON | true | 无 |
| Windows Shell 文件夹图标 | 可取得 HICON | true | 无 |
| Windows Shell 快捷方式图标 | 可取得 HICON | true | 无 |
| Windows Shell URL 图标 | 可取得 HICON | true | 无 |
| 离线网址回退图标 | 可取得 Warning HICON | true | 无 |
| 500 项首屏视觉投影 | 12 | 12 | 无 |
| 总项目计数 | 500 | 500 | 无 |
| 100% / 200% / 400% 图标像素 | 20 / 40 / 80 | 20 / 40 / 80 | 无 |
| UIA 路径暴露 | false | false | 无 |

真实 Windows HWND 测试调用 Shell32，实际创建 DesktopHost 表面并记录结构化 `Expected / Actual / Difference=None`；不是 mock 图标提供器。

## 4. 差异与修正

| 轮次 | 预期 | 实际差异 | 修正 |
| --- | --- | --- | --- |
| 开始审计 | worker 已可被产品消费 | worker 仅在资源遥测受控会话启动，UI 零消费 | PF-005 拆为 A 系统图标/状态、B 正式 worker/缓存，文档禁止提前完成 |
| 首轮编译 | 图标尺寸在 DrawItems 可用 | 变量被补丁放入上一个绘制方法，4 个编译错误 | 把 `iconSize` 收回 DrawItems 局部范围，重建 0 error |
| 首轮全量测试 | UIA 精确名称通过 | 新类型/状态事实使 2 个旧字符串不同 | 更新为“可见名称；类型；状态”，继续断言路径不公开 |
| 空方格复审 | 空态文字保持原位置 | 共用图标缩进使空态多缩进 28 DIP | 仅有实际视觉项时增加图标缩进 |
| 最终静态合同 | PF-005A 新合同通过 | 合同误写 UIA 局部变量为 `index`，实际实现为 `itemIndex` | 合同精确对齐正式实现标识后重跑，不放宽行为约束 |
| 证据覆盖复审 | 四种就绪类型和离线回退均有真实 HICON | 初版 URL 项仅为离线 Warning，未独立调用 World 图标 | 增加正常 URL 项，真实验证 World 与 Warning 两条路径 |

## 5. 门禁与需求对齐

- 全量测试：1112/1112 通过；
- 全解决方案构建：0 warning、0 error；
- 静态 UI 合同：155 个 AutomationId，`Outcome=Pass`；
- 真实 Windows Shell/HWND、500 项有界投影和 100%～400% DPI 均有自动化证据；
- 无真实桌面文件写入、无 Explorer Hook、无路径写入投影/UIA；
- 本阶段不启动 ThumbnailWorker，因此不会伪造缩略图命中、缓存或失败补偿证据。

PF-005A 关闭了“正式方格仍只画项目圆点/文本”的第一处用户差距，但 PF-005 仍为 `InProgress`。下一步固定为 **PF-005B：隔离 worker 正式按需队列、图片缩略图开关、缓存与失败回退**：必须用真实图片、真实 worker、真实修改时间变化、真实超时/退出记录 Expected、Actual 和 Difference；图片开关关闭时必须证明零 worker 请求。
