# Stage 191：PF-005C 视口、真实像素与 PF-005 工程收口审计

- 日期：2026-08-24
- 分支：`codex/pf002d-create-preview`
- 起始基线：`641f215`
- 对齐编号：`PF-005C / PF-005`
- 结论：PF-005 `EngineeringComplete / ProductEvidencePending`

## 1. 开始审计与偏移发现

Stage 190 已把 Loading/Ready/FailedFallback 和 BGRA 帧接入正式 App，但本轮从正式生命周期重新追踪后发现两个不能靠增加断言掩盖的产品偏差：

1. Loading→Ready 使用相同 workspace revision/topology generation，原生命周期把任何同事实但不同投影视为冲突并进入 `Faulted`，所以 PF-005B2 的异步结果可能无法真正显示；
2. Surface 固定只取每方格前 12 项，候选构建也固定前 12 项，13～500 项没有视口状态，无法滚入请求或滚出取消。

另外，真实 Release 全量证明 250/500/750 ms 的产品 worker 预算在负载下仍会把可成功图片过早回退。PF-005C 因此同时修正生命周期呈现代次、12 项分页视口和真实超时预算，不把 `ReadyThumbnail 或 FailedFallback` 这种宽松断言当作图片功能成功。

## 2. 实现与需求对齐

### 2.1 呈现代次与原位更新

`ProductDesktopHostProjectionBatch/Update` 增加独立 `PresentationGeneration`。workspace revision 和 topology generation 继续保护配置与显示权威事实；presentation generation 只允许同一权威事实下更晚的 Loading/Ready/Fallback 呈现覆盖更早状态。同代次但内容冲突仍然失败关闭。

生命周期在容器、几何、项目 ID/名称和策略完全相同时调用真实 Surface 的 `ApplyPresentation`，重建 UIA 投影并重绘但保持原 HWND、交互租约和窗口注册；项目集合或视口变化才重建 Surface。这关闭了 Stage 190 的正式接线偏差。

### 2.2 13～500 项视口

- 每方格保存 0-based 临时视口起点，每次最多呈现 12 项；500 项最后一页固定为起点 488，即可见 ordinal 489～500；
- `WM_MOUSEWHEEL` 只在非 Hidden Surface、命中方格且来源可证明/非注入时形成请求；请求携带 container/display/workspace revision/topology generation；
- App 在 DispatcherQueue 中二次复核权威状态、显示器归属、项目总数和事实代次，再按 12 项移动并重投影；
- 新视口立即取消上一轮缩略图 CTS，候选构建只读取新视口 12 项；旧 generation/revision/topology/开关/状态实例结果不能回写；
- 配置删除或项目减少时移除或夹取陈旧视口，不允许无界累积。

### 2.3 真实 worker 预算与熔断

实测 250 ms 首次提取约 303 ms 时回退，放宽到 2 秒后同一真实 BMP 成功。500 ms 和 750 ms 在单项/Debug 可成功，但 750 ms 在 Release 全量并行负载下仍失败。最终产品预算校准为异步 1,500 ms：UI 先显示 Loading，不阻塞窗口线程；成功后缓存，后续同版本/尺寸/主题零请求。

若首个请求发生 timeout、worker exit、protocol error 或隔离运行异常，本轮立即熔断并停止 runtime，剩余最多 11 项直接进入 FailedFallback；因此故障轮上限是一份 1.5 秒预算，不会串行放大为 18 秒。

## 3. 真实 Expected / Actual / Difference

| 场景 | Expected | Actual | Difference |
| --- | --- | --- | --- |
| Stage 190 Loading→Ready 正式生命周期 | Ready 且不 Faulted | 审计发现旧逻辑会 Faulted | 增加 presentation generation 与原位更新后 ReadyReadOnly |
| 同权威事实 Loading→Ready | HWND 不更换 | 同一 Handle，`ApplyPresentationCalls=1` | None |
| 500 项滚至末页 | 只呈现 489～500，共 12 项 | start ordinal=489，ID `item:489`～`item:500` | None |
| 视口从起点向下滚一页 | 起点 0→12 | 12 | None |
| 新视口图片候选 | 只请求当前 ordinal 3～14 | 12 项，匿名 key 对应 3～14 | None |
| 真实 HWND 呈现更新与视口绑定 | 同 HWND、ReadyThumbnail、wheel=-120 | 三项一致 | None |
| 真实 BMP，250 ms | 成功或有限回退 | 约 302.8 ms timeout，有限回退 | 有；预算过紧 |
| 同一真实 BMP，2 秒证据窗口 | ReadyPixels | 16,384 bytes | None |
| 真实 worker→产品帧→真实 HWND | 64×64 BGRA、64 行全部接受、Profile 删除 | 16,384 bytes / 64/64 / true | None |
| 正式产品队列，750 ms Release 全量 | ReadyThumbnail | FailedFallback，首轮全量 1133 pass / 1 fail | 有；不能收口 |
| 正式产品队列，1,500 ms | ReadyThumbnail | ReadyThumbnail | None |
| 12 项首请求 timeout | worker 请求 1，结果 12 个 FailedFallback | 1 / 12 | None |
| 100%/200%/400% DPI 固定 BGRA 像素 | 尺寸 20/40/80，中心 COLORREF `0x001E140A` | 三档完全一致 | None |
| 浅/深主题缓存键 | 主题变化必须重新请求 | light→dark 新请求 1 | None |

真实测试创建实际 BMP、实际随机 AppContainer Profile、实际 worker 进程和实际 DesktopHost HWND；GDI 使用 `StretchDIBits` 后再以 `GetPixel` 读取中心像素。500 项使用正式 workspace/read model/projection/candidate 链，不是只验证一个数学函数。

## 4. 门禁结果

- Release 全量：1135/1135，0 failed，0 skipped；
- Release 全解决方案：0 warning、0 error；
- UI 合同：157 AutomationId，`Outcome=Pass`；
- 格式门禁与 `git diff --check`：通过；
- 真实产品队列：ReadyThumbnail，关闭后 0 请求并删除 Profile；
- 真实 worker→真实 HWND：16,384 bytes、64/64 scan lines；
- 500 项仍只保留 12 项活动视口，不提交 500 个请求；
- 无桌面文件移动/删除、无 Explorer Hook、无路径进入投影/UIA、无主进程 Shell 内容提取。

## 5. PF-005 总审与下一步

PF-005 的工程验收项已形成闭环：系统类型图标、有限状态、受限图片 worker、持久化开关、版本/尺寸/主题缓存、失败回退、真实像素绘制、100%～400% DPI、500 项有界视口、滚动取消和过期结果拒绝均进入正式产品链。因此 PF-005 调整为 `EngineeringComplete / ProductEvidencePending`，不再继续以“缩略图代码未接入”为理由停留在 InProgress。

仍未取得的产品证据必须独立保留：操作者物理滚轮连续滚动、浅/深主题真人截图比较、Narrator/UIA Scroll 体验和成功图片在真实日常桌面的观感。它们不影响下一安全功能编码，但完成前 PF-005 不能标记 `Complete`，产品不能公开分发。

下一工程切片进入 **PF-006：项目选择、键盘导航与安全打开命令**。先审计现有 Explicit selection 与正式可见视口的对齐，建立 pointer/keyboard/UIA 共用选择状态，再单独设计打开命令的系统关联与未知协议安全边界。PF-001～PF-005 的物理/无障碍/截图证据继续并行 Pending。
