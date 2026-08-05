# Long方格只读物理桌面目录与刷新代次审计

日期：2026-08-05

基线：`main` / `a50a4cf`（PR #100 已合入）+ Issue #24 只读 Desktop Catalog 增量分支

证据等级：E2-E3 / physical desktop read-only adapter and generation contract

结论：**User + Public Desktop first-level read-only adapter pass / Authoritative-only resolution pass / Latest generation wins / File operations remain disabled / Shell virtual items remain Spike-only / Issue #24 保持 OPEN**

## 1. 晋升审计

已有 P0-01a/P0-01b 探针分别验证了物理桌面目录和 Shell Desktop namespace，但证据成熟度不同：

- 可晋升：Windows 用户桌面与公共桌面的一层物理目录枚举、`DesktopCatalog.Build` 的规范路径去重和有限类型分类；
- 不可晋升：Shell COM 虚拟项、PIDL 持久身份、Shell namespace extension、`.lnk` 目标解析和跨重启身份。它们仍缺正式身份、线程模型、资源释放及兼容矩阵，继续留在 Spike；
- 继续禁止：递归扫描、打开文件内容、执行快捷方式、读取网络/云占位内容、移动、复制、重命名、删除或写入桌面项目。

本轮 App 启动后会自动发起一次只读物理目录刷新，也允许用户显式点击“刷新目录”。读取范围仅为当前用户桌面和公共桌面的第一层。适配器会在内存中取得规范路径、显示文件名和文件属性以建立身份/类型，但 UIA、日志和审计状态只展示匿名计数、来源枚举、有限错误与 generation，不展示名称或路径。

## 2. 来源完整性与权威性

每个来源有限状态为：

- `Ready`：该根目录完整枚举；
- `Missing`：已知目录路径为空或不存在；
- `Partial`：根目录可读，但至少一个条目的属性无法安全读取；
- `AccessDenied`：读取被访问控制阻止；
- `IoFailure`：发生有限 I/O 失败。

聚合读取状态为 `Ready/Partial/Unavailable/Failed`。只有用户桌面和公共桌面都为 Ready 时，结果才是 `IsAuthoritative=true`。关键区别：

- 两个真实存在且成功枚举的空目录 → `Ready + Available([])`，可以权威表达“当前物理桌面为空”；
- 任一来源缺失、不完整或失败 → 非权威，已收集条目仅用于匿名诊断，不传给产品 resolver；
- 两个来源都缺失 → `Unavailable`，不能伪装成空桌面。

因此部分读取不会把未看到的引用错误分类为 Missing，也不会触发自动重绑或删除。

## 3. 刷新控制器与 generation

`ProductDesktopCatalogController` 为每次接受的刷新分配单调递增 generation：

1. 立即发布 `Refreshing(generation)`，旧目录不再作为当前权威快照；
2. 读取在后台执行，不阻塞 WinUI 线程；
3. 只有完成 generation 仍等于当前 generation 时才发布 Ready/Partial/Unavailable/Failed；
4. 旧请求晚于新请求完成时返回 `Stale`，不能覆盖新快照；
5. 调用方取消发布有限 `Cancelled`；关闭时 controller 取消并等待所有已接受刷新，再释放生命周期资源。

该策略避免“较慢的旧桌面枚举覆盖较新的刷新”。generation 从 1 开始，0 只表示从未刷新。

## 4. App 与正式产品会话汇合

App 现在唯一持有目录 controller、最近一次正式配置加载结果和产品会话快照。配置加载与目录刷新可以任意先后完成：

- 配置先完成、目录仍不可用 → `AwaitingCatalog`；
- 权威目录随后完成 → 使用同一正式 resolver 重新生成 ProductWorkspaceState；
- 目录先完成、配置随后完成 → 配置加载直接使用当前权威 generation；
- 刷新中、Partial、Failed、Unavailable 或 Cancelled → 转换为 `CatalogSnapshot.Unavailable`，不解析 Missing；
- 恢复/导入后的配置复读仍使用当前目录快照重新建立会话。

Core `RuntimeCapabilityState` 新增 `ConnectedReadOnly`。只有权威目录 Ready 时才报告只读连接；`FileOperations` 始终保持 `DisabledBySafetyPolicy`，DesktopHost 仍为 Disconnected。

## 5. 隐私安全 UIA 与交互

概览新增 5 个稳定 AutomationId：

- `ProductDesktopCatalogCard`；
- `ProductDesktopCatalogTitle`；
- `ProductDesktopCatalogDetail`；
- `ProductDesktopCatalogGeneration`；
- `ProductDesktopCatalogRefreshButton`。

UIA 总数从 80 增至 85。初始状态为 `DesktopCatalogUnavailable:Generation=0:Items=0:Authoritative=False`；运行期只包含有限状态、generation、匿名 item count 和权威标志。来源摘要只显示“用户桌面/公共桌面 + 有限状态 + 数量”，不包含文件名、路径、profile、container、canonical target、文件身份或异常文本。

状态卡使用 Polite live region，无 Storyboard/Transition，保持静态 Reduced Motion。刷新按钮只调用目录 controller，不调用配置保存、文件操作或 DesktopHost。

## 6. 自动证据

- 物理读取器/刷新控制器定向测试 15/15：完整非递归枚举、单来源缺失非权威、权威空桌面、双来源缺失不可用、有限状态映射、generation 发布、latest-wins、旧代次取消/失败隔离、有限 I/O 失败、取消、关闭排空和重复释放；
- 全量自动测试：300/300 通过；覆盖率 lines 90.92%（7432/8174）、branches 82.41%（1790/2172），继续高于 90%/75% 门禁；
- UI 源码合同：85 个稳定 AutomationId，验证有限初始状态、显式刷新、静态动效、App controller 所有权、权威目录门禁、双 controller 释放和零普通直写；
- Debug/Release 全解决方案构建：均为 0 warning / 0 error；
- 启动、单实例、Issue #19/#20/#23/#24 安全会话链与依赖漏洞门禁：全部通过；预检自身不启动 App、不枚举桌面，真实人工/专用环境证据继续保持 Pending；
- 当前 Windows 会话仍存在上一轮记录的无权限僵死单实例，真实 UIA 继续为 Inconclusive，不伪造 Pass。

## 7. 需求对齐与下一步

本轮首次把真实物理桌面元数据以只读方式接入正式产品会话，关闭了“Catalog 永久 Unavailable”的缺口；但它不是完整 Shell Desktop Catalog，也没有启用产品编辑。

下一条切片应：

1. 基于当前产品会话展示 resolved/missing/type-changed/ambiguous/unsupported 引用的匿名列表；
2. 对未解析引用提供默认保留、显式重新选择、显式删除确认；删除仍只改变配置引用，不删除磁盘文件；
3. 为 Catalog 刷新与配置编辑定义 generation/revision 组合门禁，避免在旧目录上提交重选结果；
4. 在干净 Windows 会话复跑 85-ID UIA、Narrator、文本缩放和关闭中刷新矩阵；
5. Shell 虚拟项继续通过独立 ADR、PIDL 身份与 COM 资源矩阵后再晋升；
6. 上述证据通过后，才允许第一条真实 reducer 编辑提交保存 controller；桌面文件继续零移动。

Issue #24 保持 OPEN；真实卷、自动保留/容量策略、正式 v2、Shell 身份、完整关闭竞态和跨进程公平性仍未关闭。
