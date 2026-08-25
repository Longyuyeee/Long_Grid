# Stage 190：PF-005B2 缩略图正式呈现与过期结果审计

- 日期：2026-08-24
- 分支：`codex/pf002d-create-preview`
- 起始基线：`e18876f`
- 对齐编号：`PF-005B2`
- 结论：`EngineeringComplete`；PF-005 顶层仍为 `InProgress`

## 1. 开始审计与目标

PF-005B1 已有真实受限 worker、关闭零启动、12 项按需队列、64 项版本缓存和有限回退，但正式 App 没有从权威 workspace 建立图片候选，worker 返回的 BGRA32 也没有进入 DesktopHost。用户因此仍只能看到 PF-005A 类型图标。

本阶段只关闭以下产品链：持久化图片开关、首屏已解析图片候选、Loading/Ready/FailedFallback 投影、真实 HWND 的 BGRA32 绘制，以及 generation、workspace revision、topology generation、开关和状态实例变化后的过期结果拒绝。它不把当前机器的 worker 回退伪装成缩略图成功，也不把 GDI 返回值当成截图视觉验收。

## 2. 实现审计

- `ProductBoxesSettings` 保持 schema v1 向后兼容并增加 `thumbnailsEnabled=true`；损坏设置安全关闭方格和缩略图。控制中心新增可访问的持久化开关、保存中/成功/失败状态，保存失败恢复权威原值。
- `ProductDesktopThumbnailCandidateBuilder` 只从当前 `ProductWorkspaceState` 的已解析 File 引用选择批准图片扩展；每容器只查看首屏 12 项、全轮最多 12 项。路径只进入请求局部候选，投影键为 container/ordinal 的 SHA-256 匿名值。
- App 先发布 `LoadingThumbnail`，再异步调用 PF-005B1 控制器。新刷新会取消旧 CTS；回调发布前统一复核 generation、workspace revision、topology generation、当前有效开关及 workspace 状态实例。任一事实变化即丢弃旧结果。
- 投影只接收匿名结果：成功帧进入 `ReadyThumbnail`，超时、协议/worker 失败和不再合法的文件进入 `FailedFallback`；缺帧或无结果继续使用 PF-005A 类型图标。
- `ProductDesktopThumbnailFrame` 限制为 1～256 像素、packed BGRA32、精确 stride/长度并复制输入。真实 DesktopHost HWND 使用 top-down `StretchDIBits` 绘制；失败立即走系统类型图标，不留下破损空白。
- 图片开关或方格总开关关闭时，当前刷新被取消，控制器收到 disabled 并停止 worker；重新加载设置后会立即重投影，不用重启 App 才生效。

## 3. 真实 Expected / Actual / Difference

| 场景 | Expected | Actual | Difference |
| --- | --- | --- | --- |
| 实际临时目录保存 `thumbnailsEnabled=false` 并重建 Store | 重启后仍为 false | false | None |
| 注入真实写入异常 | 状态恢复 true，不接受未保存值 | true | None |
| 14 个图片引用加 1 个文本引用 | 匿名图片候选 12，文本排除 | 12 / 文本排除 / key 无路径 | None |
| generation、revision、topology 或开关任一变化 | 旧回调不可发布 | 4/4 拒绝；完全一致允许 | None |
| Loading、成功帧、不支持/失败结果 | Loading / Ready / FailedFallback | 三种有限状态一致 | None |
| 确定性 8×8 packed BGRA32 进入真实原生窗口 | HWND 非零且 GDI 接受 8 行 | HWND 非零，`StretchDIBits=8`，返回 true | None |
| 真实受限 worker 读取实际 BMP | Ready 或有限回退 | 本机仍为有限回退 | None；符合失败合同，但不是成功缩略图视觉证据 |
| 关闭后的真实产品队列 | 新请求 0、Profile 删除 | 0 / true | None |

真实 HWND 测试实际创建 `WindowsProductDesktopHostReadOnlySurface` 窗口并取得其 DC，传入确定性 BGRA 像素；不是内存绘图 mock。真实 worker 测试继续生成实际 BMP、启动实际零 Capability AppContainer worker，并验证 Hang、Exit 与 Profile 清理。当前机器 250 ms 内没有取得 worker 像素，所以成功绘制证据使用受产品帧边界校验的确定性 BGRA；后续仍需在成功提取环境完成端到端截图和人工视觉比较。

## 4. 发现的差异与修正

| 轮次 | 预期 | 实际差异 | 修正 |
| --- | --- | --- | --- |
| 首轮构建 | 新 HWND 证据入口可编译 | 证据方法误用了不存在的局部 `handle` | 改为真实 Surface 的 `Handle`，重新完整构建；未使用旧二进制结果 |
| 真实候选测试 | 权威 workspace 测试可建立 | 测试夹具缺少必填 Color/DisplayKey，编译失败 | 补齐真实配置必填字段后重建，不放宽产品模型 |
| 回退投影 | 所有不可提取图片都显示有限失败 | `Unsupported` 初版落回普通 ReadyTypeIcon，原因不可见 | 映射为 `FailedFallback`，仍绘制类型图标并保留有限状态 |
| 设置恢复 | 缩略图开关独立可恢复 | 初版沿用 DesktopHost 安全策略导致缩略图控件也不可改，且加载后未立即重投影 | 缩略图设置保持可保存；加载完成立即按当前开关重投影 |
| 格式门禁 | 全仓格式零差异 | 新增 switch/GDI 分支有 7 处缩进差异 | 仅执行机械格式化，随后 verify 通过 |

## 5. 门禁与需求对齐

- Release 全量测试：1125/1125，通过 0 跳过；
- Release 全解决方案构建：0 warning、0 error；
- 聚焦设置/候选/投影/worker/HWND：36/36；
- 静态 UI 合同：157 个 AutomationId，`Outcome=Pass`；
- `dotnet format --verify-no-changes` 与 `git diff --check`：通过；
- 没有新增桌面文件写入、Explorer Hook、主进程 Shell 内容提取或路径 UIA 泄漏；
- PF-005B2 工程目标已对齐，但真实 worker 成功像素→正式 HWND→截图的端到端视觉证据仍 Pending，因此 PF-005 不升级为 Complete。

下一步固定为 **PF-005C：视觉回归、视口调度与 PF-005 总审**。使用可成功提取的真实 PNG/JPEG/BMP，记录类型图标、Loading、Ready、FailedFallback 在浅/深主题和 100%～400% DPI 的截图/像素差异；验证 13～500 项视口变化不会全量请求、滚出视口立即取消，最终再决定 PF-005 是否可转为 `EngineeringComplete / ProductEvidencePending`。PF-001～PF-004 的物理输入、Narrator/UIA 和截图证据继续并行 Pending。
