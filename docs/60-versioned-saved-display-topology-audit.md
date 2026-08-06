# Long方格 v2 保存时显示拓扑合同与迁移审计

> 审计日期：2026-08-06  
> 范围：正式配置 v2、v1→v2 相邻迁移、导入/导出/保存链、产品恢复预览接线  
> 结论：保存时拓扑缺口已关闭；真实恢复确认、DesktopHost 提交和窗口移动仍未获准

## 1. 需求与实现对齐

本阶段解决“恢复规划知道当前显示器，却不知道布局保存时显示器”的证据缺口。正式根合同新增可选 `savedDisplayTopology`，每个节点只保存恢复算法需要的脱敏字段：稳定 ID、屏幕 Bounds、WorkArea、有效 DPI、旋转和主屏标志。配置不保存 CCD device path、adapter LUID、target ID、GDI name 或其他原生身份材料。

App 只在两个条件同时成立时刷新快照：用户提交了会产生配置保存的工作区编辑，且 `ProductDisplayTopologySnapshot.IsAuthoritative=true`。刷新失败、降级、取消、非 Windows 或权威样本为空时保留已有快照；它们不能用“当前猜测”覆盖历史证据。首次配置在拓扑尚未权威时仍可保存，但快照保持缺失，恢复预览明确返回 `SavedTopologyMissing`。

## 2. v2 合同边界

| 项目 | v2 规则 |
|---|---|
| 节点数 | 可选；存在时为 1–32 个 |
| 身份 | StableId 非空、最长 256、Ordinal 唯一 |
| 主屏 | 恰好一个 `isPrimary=true` |
| 几何 | Bounds/WorkArea 为有限正面积整数矩形，WorkArea 完整位于 Bounds 内 |
| 范围 | 坐标绝对值不超过 1,000,000；单边不超过 100,000 |
| DPI | 48–768 |
| 旋转 | 仅允许已定义且非 `unknown` 的枚举值 |
| 扩展 | 根、显示节点和矩形的未知字段经解析—编辑—重投影保留 |
| 预算 | 继续服从 4 MiB UTF-8、JSON 深度 32 和有限错误码 |

任何不合法拓扑统一收敛为 `InvalidDisplayTopology`，公开错误不携带 StableId、几何、路径、JSON 或异常原文。损坏的主配置继续沿用正式 Store 的备份/安全模式路径，不会静默删除拓扑后接受文档。

## 3. 迁移、导入与回滚

- serializer 只写 v2；直接提交 v1 文档写入会被 `UnsupportedSchema` 拒绝；
- deserializer 只允许严格相邻 v1→v2：保留 v1 全部字段与扩展数据，把 schema 提升为 2，并把保存时拓扑保持为 `null`；
- v1 若夹带 v2 的 `savedDisplayTopology` 会被拒绝，避免版本标记与字段语义不一致；
- v0、v3 及未来版本拒绝，不做多跳猜测；
- 导入、主/备加载、保存快照、重试和导出复用同一 serializer/deserializer，因此迁移后预览与后续写入统一显示 v2；
- 迁移只先发生在内存中。没有用户编辑时不因启动而覆写磁盘；下一次真实编辑才通过现有原子保存链发布 v2；
- 回滚继续依靠轮转备份和 SafeMode，不原地降级为 v1。

## 4. 恢复双门禁

恢复预览现在从产品 session 读取持久化的保存时拓扑，同时只在当前拓扑权威时传入当前节点。状态边界如下：

1. session 不可用：`UnavailableSession`；
2. 当前拓扑不权威：`AwaitingAuthoritativeTopology`；
3. 当前拓扑权威但旧 v1/首次保存没有历史快照：`SavedTopologyMissing`；
4. 两侧均有效：才允许 planner 输出 `Automatic`、`ReviewRequired` 或 `Blocked`；
5. 任意无效领域状态：`InvalidState`。

预览结果的 `DesktopWindowsChanged` 仍固定为 `false`。本阶段没有恢复确认按钮、没有把计划写回配置、没有调用 DesktopHost，也没有移动、隐藏或重排任何真实窗口。

## 5. 自动证据与剩余风险

自动测试覆盖 v2 合法往返、v1 无拓扑迁移、v1 夹带 v2 字段拒绝、未来版本拒绝、节点集合/主屏/重复身份/矩形/WorkArea/DPI/旋转/扩展字段边界，以及原生节点与持久化形状的无损映射。现有配置 Store、导入、导出、session、reducer、连续保存、布局恢复和显示拓扑测试继续全量运行。

本地最终证据为 389/389 测试通过；CI 等价 TRX/XPlat Coverage 为行 91.93%（9662/10510）、分支 84.16%（2582/3068），通过 90%/75% 门槛；Debug/Release 全量构建均为 0 warning / 0 error。启动链、116-ID UIA 源码合同、单实例合同、Issue #19/#20/#23/#24 `ValidateOnly`、配置持久化、DesktopHost、文件操作、缩略图隔离和已知漏洞门禁均通过其既有限定结论。真实 UIA 复跑仍因下述环境问题失败，未计入产品通过或失败。

仍未关闭的证据：

- Issue #20 的真实多显示器、热插拔、DPI/方向和会话矩阵；
- Issue #24 的真实卷容量、只读卷、断电和非 NTFS 证据；
- 当前 Windows 会话 PID 39208 无主窗口残留导致的真实 116-ID UIA `0x8000FFFF (E_UNEXPECTED)`，仍记为环境 `Inconclusive`；
- 用户可审查恢复确认、陈旧预览令牌、配置布局提交和撤销；
- DesktopHost 真实窗口创建/移动及任何桌面文件副作用。

## 6. 下一阶段准入

下一切片应只建立“恢复计划审查与确认合同”：令牌必须绑定保存时拓扑指纹、当前拓扑 generation、edit revision 和预览摘要；确认前再次复核两侧证据，陈旧令牌有限拒绝。首个提交仍应只更新 Long方格自身的容器 DIP/显示器放置并进入现有保存控制器，不连接真实 DesktopHost。只有该配置级确认、撤销和 UIA 证据闭环后，才能另行审计真实窗口提交。
