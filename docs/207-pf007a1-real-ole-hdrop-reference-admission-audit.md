# Stage 207：PF-007A1 真实 OLE HDROP 安全引用准入审计

日期：2026-08-25
开发项：PF-007A1
结论：**Engineering Complete / Integration Pending / Product Surface Pending**

## 1. 目标与边界

PF-007 要让用户从 Explorer 拖入项目并建立安全引用。现有配置层已经支持 1～256 项原子加入、一次保存和一次撤销，但没有安全解析真实 OLE 拖放数据的生产边界。

本切片只完成第一层：把真实 `CF_HDROP` 数据对象转换为既有 `ProductWorkspaceResolvedReferenceBatchCommitRequest`。它不注册 DesktopHost `IDropTarget`，不声明物理 Explorer 拖入已经可用；该窗口接线、悬停高亮、Link 动作光标和取消路由属于 PF-007A2。

## 2. 安全合同

- 只接受 OLE `CF_HDROP` / `TYMED_HGLOBAL`；文本、URL 文本和其他格式拒绝；
- 数量固定为 1～256，257 项在读取路径前整批拒绝；
- 每个路径通过 `DragQueryFileW` 取得并规范化，必须是现存文件或目录；
- 大小写不敏感重复整批拒绝；
- 每一项必须唯一匹配当前权威 Catalog，目录外项目暂时拒绝，避免保存刷新后立即失效的幽灵引用；
- 目标必须是当前正式、未锁定方格；
- 结果只包含既有配置级批量引用请求，没有 Move/Copy/Delete 动作或修饰键切换；
- `STGMEDIUM` 始终通过 `ReleaseStgMedium` 释放。

## 3. 真实测试与差异

| 验收 | Expected | Actual | Difference |
| --- | --- | --- | --- |
| OLE 数据 | 真实 `CF_HDROP` HGLOBAL 可解析 | WPF/OLE DataObject 提供真实 HGLOBAL，解析 2 项 | None |
| 原子配置 | 两项只保存一次 | ItemCount=2，SaveCalls=1 | None |
| 文件安全 | 文件哈希、目录存在性不变 | `DesktopFilesChanged=false` | None |
| 类型 | 文件与目录均映射权威 Catalog | 两者 catalog index 为 0/1 | None |
| 危险输入 | locked/unknown target/not-catalog/missing/duplicate/257 全拒绝 | 6/6 零 CommitRequest | None |
| 修改键语义 | 不存在 Move/Copy 切换入口 | 输出类型只有安全引用批量请求 | None |
| 定向测试 | 0 fail | 8/8 | None |
| Release 全量 | 0 fail | 1233/1233，18 s | None |
| 覆盖率 | ≥90% / ≥75% | 90.43%（41792/46214）/75.77%（13586/17930） | None |

真实证据使用真实沙箱文件和真实 OLE HDROP HGLOBAL，并进入正式批量协调器，保存调用为一次。它不是物理鼠标，也没有经过 DesktopHost 原生窗口，因此不能替代 PF-007A2 或产品证据。

## 4. 需求对齐与下一步

本切片复用既有安全引用和原子保存链，没有扩大文件权限，没有偏向任务栏、小组件或插件。PF-007 仍为 `InProgress`，30 个 PF 项仍为 `0 Complete`。

下一切片 PF-007A2：在 Explicit DesktopHost HWND 上注册/撤销 `IDropTarget`，把屏幕坐标命中未锁定方格，固定返回 Link/“添加引用”语义，接入 workspace/topology/catalog revision 复核，并以真实 HWND、真实 OLE 数据、真实配置重载和文件哈希差异表验证。方格→方格改归属在 Explorer 拖入闭环后进入 PF-007B。

## 5. 集成状态

本节在 PR 和 main 完整 CI 通过后回填；集成前不提升为 Integrated。
