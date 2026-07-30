# P0-07b2a：拓扑映射、恢复预览与最小可见性纠正

执行日期：2026-07-30

结果：**Conditional Pass（纯 Core 恢复规划通过；真实显示变化与提交回滚仍需 P0-07b2b）**

前置：P0-07a、P0-07b1

## 1. 目标

建立不依赖 Windows API 或 UI 框架的布局恢复规划器，验证：

1. 等价拓扑可以稳定映射；
2. 虚拟桌面整体坐标平移不会制造恢复差异；
3. DPI、工作区、方向或显示器身份变化不会静默套用；
4. 身份缺失时只接受互为唯一最佳的几何候选；
5. 对称显示器产生歧义时阻断恢复，不按数组下标猜测；
6. 缺屏时保留未解析项，不把容器偷偷搬到主屏；
7. 保存的 DIP 坐标按目标显示器 DPI 转为物理像素；
8. 不可见容器只做满足最小可见面积所需的最小位移；
9. 所有变化都形成可供 UI 展示的 requested/proposed 差异。

本子探针只生成计划，不移动真实窗口，不修改显示配置，也不写入布局快照。

## 2. 官方与竞品边界

[`WM_DISPLAYCHANGE`](https://learn.microsoft.com/windows/win32/gdi/wm-displaychange) 只通知分辨率变化并携带有限的屏幕尺寸信息，不能替代重新读取完整 CCD 拓扑。生产实现收到通知后只能将现有拓扑标记为过期，合并事件后重新执行 P0-07b1 的只读查询。

[`WM_DPICHANGED`](https://learn.microsoft.com/windows/win32/hidpi/wm-dpichanged) 提供新的 DPI 和建议窗口矩形。DesktopHost 必须先采用建议矩形，再基于新的稳定拓扑生成 Region、Composition、UIA Bounds 和恢复计划，不能用旧 DPI 直接重放像素坐标。

[PowerToys Workspaces](https://learn.microsoft.com/windows/powertoys/workspaces) 在恢复过程中显示每个应用的进行中、成功和失败状态，允许取消，并支持重新捕获后的回退。Long Grid 对布局采用同类逐项反馈，但进一步区分 `Automatic`、`ReviewRequired` 和 `Blocked`。

## 3. 数据合同

输入：

- 保存时的 `DisplayTopologyNode[]`；
- 当前 `DisplayTopologyNode[]`；
- 以目标显示器工作区为原点保存的容器 DIP 矩形；
- 最小可见 DIP，默认 48。

输出：

- 显示器映射及 `ExactIdentity`/`SimilarGeometry`；
- 未解析的保存显示器 ID；
- 每个可映射容器的 requested/proposed 物理像素矩形；
- 是否执行最小可见性纠正；
- `Automatic`、`ReviewRequired` 或 `Blocked`。

规划器不输出硬件原始身份；调用方不得把内部稳定 ID 直接显示给用户。

## 4. 恢复状态

### Automatic

同时满足：

- 保存与当前拓扑指纹等价；
- 所有显示器均为精确身份映射；
- 没有容器需要可见性纠正。

指纹对虚拟桌面整体平移不敏感，因此仅系统原点变化不制造确认弹窗。

### ReviewRequired

任一条件成立：

- DPI、工作区、方向或相对拓扑变化；
- 使用相似几何映射；
- 增加了新显示器；
- 任一容器发生最小可见性纠正。

UI 必须展示旧目标、新目标和逐项位置差异，用户确认后才能提交。

### Blocked

任一保存显示器没有唯一映射，例如：

- 显示器缺失；
- 两个或更多候选评分相同；
- 对称同规格屏幕身份同时变化。

Blocked 计划可以展示已解析部分，但事务层禁止部分提交。

## 5. 相似匹配

先按稳定身份做精确匹配。剩余显示器根据以下本地属性评分：

- 工作区 DIP 宽高差；
- rotation 是否相同或属于同一横/竖方向族；
- primary 属性是否一致。

只有“保存显示器的唯一最佳候选”同时也是“当前显示器的唯一最佳来源”时才建立 `SimilarGeometry` 映射。相似映射永远不能自动应用。

相对位置暂不参与消除对称歧义。宁可要求用户确认，也不把左右两台同规格屏幕猜反。

## 6. 坐标与最小纠正

容器位置保存为工作区相对 DIP：

```text
target pixels = target work-area origin + round(DIP × target DPI / 96)
```

规划器保留容器尺寸，只沿 X/Y 轴执行满足默认 48 DIP 可见区域所需的最小平移。若容器本身或工作区小于 48 DIP，则使用可实现的较小值。任何纠正都会把状态升级为 `ReviewRequired`。

## 7. 自动化结果

新增 9 个测试，覆盖：

| 场景 | 期望 |
|---|---|
| 等价拓扑、枚举顺序变化 | Automatic |
| 虚拟桌面整体平移 | Automatic |
| 精确身份但 DPI 变化 | ReviewRequired |
| 单屏唯一相似替换 | ReviewRequired |
| 两块同规格非主屏同时换身份 | Blocked |
| 保存显示器缺失 | Blocked |
| DIP 映射到 200% DPI | 坐标和尺寸 ×2 |
| 容器越界 | 最小位移且 ReviewRequired |
| 容器引用未知显示器 | 拒绝输入 |

最终解决方案测试由 29 增至 38，全部通过。

## 8. 已验证与限制

已验证：

- Core 无 Windows/UI 依赖；
- 自动、确认、阻断三态可判定；
- 不使用数组序号、友好名或单独分辨率做身份；
- 对称歧义不会被贪心猜测；
- DPI 转换采用明确舍入规则；
- 最小纠正不会触发自动应用；
- 输入显示器和容器身份经过唯一性校验。

尚未验证：

- `WM_DISPLAYCHANGE`/`WM_DPICHANGED` 事件合并和稳定窗口；
- 真实负坐标、旋转、缩放、拔插、投影、睡眠和 RDP；
- Region、Composition、UIA Bounds 的同代提交；
- 提交失败后的窗口级回滚；
- 用户交换显示器内容、排除项目和撤销；
- 大规模容器的性能预算与长时间压力测试。

## 9. P0-07b2b

下一子阶段在受控实验室加入事件协调器和事务执行器：

1. 合并连续显示变化，等待拓扑稳定；
2. 先采纳 `WM_DPICHANGED` 建议矩形；
3. 生成恢复预览，不立即覆盖快照；
4. 用户确认后同代提交 Window Region、Composition 与 UIA Bounds；
5. 任一步失败时恢复旧窗口布局；
6. 执行负坐标、旋转、缩放、拔插、投影、睡眠和 RDP 矩阵。
