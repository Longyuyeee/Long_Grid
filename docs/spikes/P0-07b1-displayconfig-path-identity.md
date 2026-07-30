# P0-07b1：DisplayConfig 活动路径、旋转与显示器身份关联

执行日期：2026-07-30

结果：**Conditional Pass（当前静态会话的 CCD 路径关联通过；动态显示变化仍需 P0-07b2）**

前置：P0-07a

## 1. 目标与范围

本子探针在不修改显示设置的前提下验证：

1. 使用 `GetDisplayConfigBufferSizes`/`QueryDisplayConfig` 读取当前活动显示路径；
2. 正确解析 Windows 10 virtual-mode-aware 的 source mode 索引；
3. 用 `DisplayConfigGetDeviceInfo` 取得 source/target 关联；
4. 通过 source GDI name 与 `EnumDisplayMonitors` 结果一一映射；
5. 从 CCD 读取 0/90/180/270° rotation，不再仅靠宽高猜测；
6. 使用 monitor device path 的进程内散列作为当前本地连接身份；
7. 在重复查询中验证路径、拓扑、资源和隐私稳定性；
8. 对容量查询与正式查询之间的 `ERROR_INSUFFICIENT_BUFFER` 竞态执行有界重试。

探针不调用 `SetDisplayConfig`，不旋转、缩放、拔插或切换主显示器，也不进入 RDP。

## 2. 官方 API 边界

[`QueryDisplayConfig`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-querydisplayconfig) 明确指出，`GetDisplayConfigBufferSizes` 只代表某一时刻；两次调用之间若显示配置变化，正式查询可能返回 `ERROR_INSUFFICIENT_BUFFER`，调用者必须重新取容量并重试。本实现最多重试 8 次，不能无限阻塞。

查询使用：

```text
QDC_ONLY_ACTIVE_PATHS | QDC_VIRTUAL_MODE_AWARE
```

活动路径包含 source、target 和 mode table 的关联。virtual-mode 路径使用打包的 `sourceModeInfoIdx`，不能把整个 32 位 union 直接当作普通 `modeInfoIdx`。

[`DisplayConfigGetDeviceInfo`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-displayconfiggetdeviceinfo) 用 adapter LUID 与 source/target ID 查询附加信息。source GDI name 只用于本次会话内连接两个公开枚举；adapter LUID 和 target ID 是关联键，不是跨重启硬件身份。

[`DISPLAYCONFIG_TARGET_DEVICE_NAME`](https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-displayconfig_target_device_name) 可能包含友好名、EDID 字段和 monitor device path。友好名只用于 UI；所有原始字段均禁止进入默认日志和探针报告。

## 3. 实现

在现有 `LongGrid.Spikes.DisplayTopology` 中新增 CCD 适配器：

1. 查询活动 path/mode 数量；
2. 分配精确数组并执行 `QueryDisplayConfig`；
3. 缓冲不足时重新从第一步开始，最多 8 次；
4. 验证活动 source name 和脱敏 target identity 唯一；
5. 验证 source mode 索引、类型、adapter 和 source ID 一致；
6. 将 source 物理像素矩形与 `GetMonitorInfo` Bounds 对账；
7. 将 CCD rotation 映射为 Core `DisplayRotation`；
8. 以脱敏 target identity 生成拓扑和路径指纹。

失败策略是拒绝生成可自动恢复的拓扑快照，而不是按数组下标、分辨率或友好名猜测。

## 4. 隐私和安全

报告不输出：

- adapter LUID；
- source/target ID；
- source GDI name；
- monitor friendly name；
- monitor device path；
- EDID manufacturer/product；
- 单屏 identity hash；
- 路径或拓扑指纹。

monitor device path 只在进程内散列。散列仍可能构成可关联标识，因此也不进入默认日志、诊断包或遥测。

## 5. 当前实测环境

| 项目 | 值 |
|---|---|
| OS | Microsoft Windows NT `10.0.26200.0` |
| 架构 | x64 |
| 显示器 | 2 |
| 活动 CCD 路径 | 2 |
| 有效 DPI | 192、240 |
| virtual-mode 路径 | 2 |
| 当前 rotation | 2 × Landscape |
| 负虚拟坐标 | 当前拓扑没有 |

## 6. 三轮结果

每轮包含 100 次完整 CCD + monitor + DPI 联合快照：

| 指标 | 第 1 轮 | 第 2 轮 | 第 3 轮 |
|---|---:|---:|---:|
| 活动路径/显示器 | 2/2 | 2/2 | 2/2 |
| source-name 映射 | 2/2 | 2/2 | 2/2 |
| source bounds 匹配 | 2/2 | 2/2 | 2/2 |
| available target | 2/2 | 2/2 | 2/2 |
| monitor device path | 2/2 | 2/2 | 2/2 |
| 不同拓扑指纹 | 1 | 1 | 1 |
| 不同路径指纹 | 1 | 1 | 1 |
| 最大缓冲尝试次数 | 1 | 1 | 1 |
| p50 | 34.51 ms | 37.22 ms | 26.70 ms |
| p95 | 45.96 ms | 48.78 ms | 37.53 ms |
| USER | 1 → 1 | 1 → 1 | 1 → 1 |
| GDI | 0 → 0 | 0 → 0 | 0 → 0 |
| 进程句柄 | 252 → 252 | 252 → 252 | 252 → 252 |

共 300 次查询没有观察到路径漂移、映射歧义、Bounds 差异或资源净增长。没有发生真实热切换，因此只证明重试逻辑存在且正常路径为一次成功，不能证明已经命中缓冲竞态。

## 7. 结论

### 已通过

- 当前 2 条活动 CCD 路径可完整读取；
- 两个 virtual-mode source mode 索引解析正确；
- CCD source 与 monitor 枚举达到 2/2 一一映射；
- source mode 物理像素矩形与 monitor Bounds 达到 2/2 一致；
- 当前两条 target 均 available；
- 当前两条 target 均提供 monitor device path；缺失时的 adapter/target 会话 fallback 不计为强身份；
- 当前 rotation 能从 CCD 读取；
- 三轮路径和拓扑指纹稳定；
- 无 USER、GDI 或进程句柄净增长；
- 原始硬件/连接身份没有出现在输出中。

### 尚未通过

- 真实 `ERROR_INSUFFICIENT_BUFFER` 热切换竞态；
- 90/180/270° 旋转后的映射和 Region/UIA 重算；
- 负坐标、主屏切换、缩放、拔插、投影、睡眠和 GPU 重置；
- RDP/虚拟显示器进入与退出；
- 克隆/投影模式下多个 target 共享 source 的映射；
- `WM_DPICHANGED` 建议矩形；
- 最近拓扑匹配、最小位移恢复、预览、撤销和失败回滚。

因此 P0-07 整体仍是 Conditional，不能启用无人确认的自动布局恢复。

## 8. 后续 P0-07b2

在受控实验室执行动态矩阵：

1. 负坐标和主屏切换；
2. 100%–400% 缩放热切换与 `WM_DPICHANGED`；
3. 横竖屏、180°/270°、拔插和投影；
4. 睡眠、RDP、虚拟显示器和 GPU 重置；
5. 显示变化通知合并、后台重采集和有界重试；
6. 恢复预览、最小可见性纠正、撤销与失败回滚；
7. Window Region、Composition 和 UIA Bounds 同代提交。
