# P0-07a：显示器静态拓扑、有效 DPI 与隐私安全指纹

执行日期：2026-07-30

结果：**Conditional Pass（静态双屏混合 DPI 稳定；热切换仍需 P0-07b）**

前置：P0-04/P0-05a

## 1. 目标

在不修改任何显示设置的前提下验证：

1. 以 Per-Monitor V2 上下文枚举当前可见显示器；
2. 获得物理像素 Bounds、Work Area、Primary 和每窗口有效 DPI；
3. 在双屏混合 DPI 下连续生成稳定拓扑指纹；
4. 指纹与枚举顺序和虚拟桌面整体平移无关；
5. 相对位置、尺寸、DPI 或方向变化会产生不同指纹；
6. 不把显示器名称、PNP 标识、设备路径或序列号式信息写入报告；
7. 反复创建 DPI 探测窗口不会泄漏 USER、GDI 或进程句柄。

本子探针只验证当前静态拓扑，不自动拔插、旋转、切换主屏、修改缩放或进入 RDP。

## 2. 官方 API 结论

[`EnumDisplayMonitors`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumdisplaymonitors) 在设备上下文和裁剪区域均为 NULL 时使用虚拟屏幕坐标枚举显示器，适合取得显示器位置。`GetMonitorInfo` 提供 Monitor Bounds、Work Area、Primary 标志和会话设备名。

[`SetProcessDpiAwarenessContext`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setprocessdpiawarenesscontext) 可以请求 Per-Monitor V2，但微软建议生产应用通过 Manifest 声明；若使用 API，必须在任何依赖 DPI 的 API 或 UI 创建前调用。探针在入口第一时间调用，产品实现改为 Manifest。

微软对 [`GetDpiForMonitor`](https://learn.microsoft.com/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor) 的说明指出，它不应由 Per-Monitor DPI-aware 线程使用，应改用 `GetDpiForWindow`。因此探针在每个显示器 Bounds 内创建一个不可见、非激活的 1 × 1 临时窗口，再调用 `GetDpiForWindow`，而不是把系统 DPI 误当成每屏 DPI。

[`WM_DPICHANGED`](https://learn.microsoft.com/windows/win32/hidpi/wm-dpichanged) 会提供新 DPI 和建议矩形；生产窗口必须采用建议矩形并重算 Region/Composition。P0-07a 没有制造 DPI 变化，因此不把这条路径写成已通过。

## 3. 身份和指纹设计

当前适配器通过 `EnumDisplayDevices` 尝试读取设备 ID/Device Key：

```text
Device ID
  → 不可用则 Device Key
  → 再不可用才退回会话 Device Name
```

原始值只在进程内短暂存在，立即 SHA-256；报告不输出单屏散列或最终拓扑散列，只输出“100 次是否一致”。哈希不能把可预测硬件标识变成匿名数据，因此生产诊断仍禁止导出这些值。

Core 指纹输入：

- 本地稳定 ID；
- 相对虚拟桌面原点的 Bounds；
- 相对 Work Area；
- 有效 DPI；
- 方向；
- Primary 标志。

指纹先按稳定 ID 排序，再把整个虚拟桌面平移到零原点。由此避免：

- 枚举顺序变化导致误判；
- 所有屏幕同时平移但相对拓扑未变时创建无意义快照；
- 只按 `DISPLAY1`、数组下标或分辨率错误匹配。

当前方向仅根据 Bounds 区分 Landscape/Portrait，不能识别 Flipped。生产实现仍需 `QueryDisplayConfig`/`DisplayConfigGetDeviceInfo` 获得 adapter/target、旋转和更可靠的连接路径；友好名只用于 UI，不作为身份。

## 4. 实现

新增：

- Core `DisplayTopologyNode`；
- Core `DisplayTopologyFingerprint`；
- 六个顺序、平移、DPI、方向、相对位置和重复 ID 测试；
- `LongGrid.Spikes.DisplayTopology` Windows 只读探针。

每个真实显示器快照包括：

- 物理像素 Monitor Bounds；
- Work Area；
- Primary；
- `GetDpiForWindow` 有效 DPI；
- 进程内散列身份；
- 横/竖方向近似值。

探针先预热一次，再执行 100 次枚举和指纹计算，记录 p50/p95 以及稳定资源基线。

## 5. 安全与隐私

- 未修改分辨率、缩放、方向、主屏、亮度、HDR、色彩或投影模式；
- 未发送 Win 键、显示切换或其他合成输入；
- 未输出显示器名称、Device String、PNP ID、Device Key、设备路径或单屏散列；
- 未输出最终拓扑指纹；
- 临时 DPI 窗口从不显示、不激活、不置顶；
- 未读取其他应用窗口或屏幕内容；
- 所有 HWND 都在同次枚举中销毁。

## 6. 实测环境

| 项目 | 值 |
|---|---|
| OS | Microsoft Windows NT `10.0.26200.0` |
| 架构 | x64 |
| .NET SDK | `8.0.419` |
| 显示器 | 2 |
| Primary | 1 |
| 有效 DPI | 192、240 |
| 混合 DPI | 是 |
| 强身份来源 | 2/2 |
| 会话名 fallback | 0 |
| 负虚拟坐标 | 当前拓扑没有 |

## 7. 三轮结果

每轮包含 100 次完整枚举与指纹计算：

| 指标 | 第 1 轮 | 第 2 轮 | 第 3 轮 |
|---|---:|---:|---:|
| 显示器 | 2 | 2 | 2 |
| 不同拓扑指纹 | 1 | 1 | 1 |
| Work Area 位于 Bounds 内 | 2/2 | 2/2 | 2/2 |
| 虚拟屏幕边界等于显示器外接矩形 | 是 | 是 | 是 |
| p50 | 30.55 ms | 28.14 ms | 32.74 ms |
| p95 | 44.78 ms | 36.74 ms | 57.06 ms |
| USER 基线 → 结束 | 1 → 1 | 1 → 1 | 1 → 1 |
| GDI 基线 → 结束 | 0 → 0 | 0 → 0 | 0 → 0 |
| 进程句柄净增长 | 0 | 0 | 0 |

三轮合计 300 次枚举，未观察到身份、拓扑、DPI、Bounds 或资源趋势漂移。

最终质量门禁另跑一轮，p50/p95 为 51.84/80.39 ms，正确性和资源结果不变。这说明枚举延迟受当前系统负载影响明显；P0-07a 只记录基线，不据此制定启动或热切换性能预算。生产应合并显示变化通知并在后台采集，不能在 UI 线程同步循环枚举。

## 8. 结论

### 已通过

- 当前双屏混合 DPI 能被只读、完整枚举；
- Per-Monitor V2 请求成功；
- 每屏 DPI 没有被错误折叠为系统 DPI；
- 虚拟屏幕边界与显示器 Bounds 外接矩形一致；
- 100 次/轮的指纹完全稳定；
- 指纹算法对枚举顺序和整体平移不敏感；
- DPI、方向或相对拓扑变化会使 Core 指纹变化；
- 当前硬件两屏都有较强身份来源；
- 临时窗口和原生资源没有净增长；
- 输出遵守硬件标识最小化。

### 为什么仍是 Conditional Pass

- 没有真实拔插、旋转、主屏切换和缩放热切换；
- 没有收到并处理 `WM_DPICHANGED` 建议矩形；
- 当前没有负坐标，只有 Core 单测覆盖负坐标规划；
- 没有验证睡眠、GPU 重置、投影、HDR、RDP 或虚拟显示器；
- 尚未使用 `QueryDisplayConfig` 获取旋转、adapter/target 和连接路径；
- Device ID/Key 在驱动重装、虚拟化或 RDP 后可能变化；
- 尚未执行“最近拓扑”相似度匹配和布局恢复。

所以 P0-07 的发布门禁尚未关闭。

## 9. 产品实现约束

1. 生产进程通过 Manifest 声明 Per-Monitor V2；
2. HWND/Win32 坐标统一使用物理像素，领域布局统一使用 DIP；
3. 只在边界层做 DIP/像素转换和舍入；
4. 拓扑身份不能使用数组下标、`DISPLAY1`、友好名或单独分辨率；
5. 生产适配器使用 `QueryDisplayConfig` 并处理缓冲不足重试；
6. 原始硬件标识只保存在本地受控存储，不进入默认日志或诊断包；
7. `WM_DPICHANGED` 先应用建议矩形，再更新 Region、Composition 和 UIA Bounds；
8. 显示器变化时创建恢复预览，不立即覆盖旧快照；
9. 找不到完全匹配时只做最小可见性纠正，并报告映射差异；
10. 热切换失败时隐藏 DesktopHost，不能留下跨屏输入遮挡。

## 10. 后续 P0-07b

1. `QueryDisplayConfig`/`DisplayConfigGetDeviceInfo` adapter-target 枚举与缓冲重试；
2. 双屏负坐标和主屏切换；
3. 100%–300% 缩放热切换与 `WM_DPICHANGED` 建议矩形；
4. 横竖屏旋转、拔插、投影和休眠恢复；
5. RDP、虚拟显示器和显卡驱动重置；
6. 拓扑相似度、最小位移恢复、撤销和恢复报告；
7. Window Region、Composition、UIA Bounds 同代提交与失败回滚。
