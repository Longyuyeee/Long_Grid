# 已解析引用移除与一次撤销审计

> 审计日期：2026-08-07
>
> 范围：正式方格中已解析引用的配置级移除、同会话一次撤销、UIA 与保存边界
>
> 结论：**Conditional Pass；可逆配置闭环成立，真实桌面文件操作继续为零**

## 1. 需求对齐

上一切片只能把未分组的真实桌面项目作为引用加入正式方格，缺少用户可见的反向操作。本切片补齐最小可逆闭环：

- 从未锁定方格中选择可见的已解析引用；
- 显式“移除引用并保存”；
- 在没有其他成功编辑或外部重载的前提下，撤销上一次移除一次；
- 全程只修改 Long方格配置，不移动、重命名、删除或读取桌面文件内容。

跨方格改归属、真实拖放和文件移动不在本切片授权范围内。

## 2. 数据与隐私边界

Core 只读模型向 App 提供方格序号、引用序号和已解析可见名称。移除 presentation 只保留这些有限字段，不携带持久化 target、路径、ProfileId、SourceId、ContainerId、ItemId、ParsingName、VolumeId 或 FileId。MainWindow 只把用户选择的序号送回 App；内部 ID 解析始终留在共享提交协调器内。

候选列表只包含未锁定方格中的 `Resolved` 引用。未解析引用继续走既有匿名审查与显式确认链，避免两套删除语义互相绕过。

## 3. 提交与撤销合同

`ProductWorkspaceCommitCoordinator` 是唯一普通编辑入口：

1. 核对 `ExpectedEditRevision`；
2. 以方格/项目序号重新读取当前状态；
3. 拒绝越界、未解析和锁定目标；
4. 调用正式 `ProductWorkspaceReducer.RemoveReference`；
5. 经正式 projector/validator 复核；
6. 只向 `ProductWorkspaceSaveController` 提交一次；
7. 接受后推进 revision 并发布新的配置文档。

撤销令牌包含随机操作 ID、移除后的 edit revision，以及移除后/恢复前配置文档的 SHA-256 指纹。撤销必须同时满足令牌相等、revision 未变、当前配置指纹仍等于移除结果、恢复状态指纹仍等于原状态和显式确认。任何其他成功引用编辑、容器编辑、布局恢复或外部会话重载都会清除待撤销状态；成功撤销后令牌立即消费。

保存控制器拒绝时，revision、待撤销状态和当前产品会话均不推进。

## 4. 交互与 UIA

正式方格编辑区新增：

- `ProductWorkspaceResolvedReferenceRemovalSelector`；
- `ProductWorkspaceResolvedReferenceRemovalButton`；
- `ProductWorkspaceResolvedReferenceRemovalUndoButton`；
- `ProductWorkspaceResolvedReferenceRemovalStatus`。

UI 合同由 121 增至 125。所有控件默认禁用；只有可编辑会话和有效候选才开放移除，只有当前协调器持有有效令牌才开放撤销。状态使用 Polite live region，机器状态只暴露有限枚举、revision、候选计数、Changed 和固定 `DesktopFilesChanged=False`。

## 5. 自动化证据与剩余风险

定向测试覆盖：成功移除、同一引用一次性撤销、第二次撤销拒绝、桌面文件内容不变、旧 revision、锁定方格，以及撤销的确认、令牌、revision 和配置指纹门禁。全量 Release 测试为 521/521 通过；单份 Cobertura 为行 91.33%（7323/8018）、分支 80.87%（2054/2540），通过 90%/75% 门槛。125-ID 源码合同与干净会话 `ValidateOnly` 已通过。

合入前仍必须通过格式、Release 构建、全量测试、单份 Cobertura 90% 行/75% 分支门槛、启动链、单实例合同、内部 RC 合同和远端 PR CI。本机存在的外来无窗口 `LongGrid.App` 进程不在本任务授权范围内，因此真实 live UIA 继续保持 Pending，不能用源码合同冒充。

## 6. 下一步

后续切片已复用同一 revision/指纹/保存模型实现“跨方格改归属”的单次原子配置提交，并补齐源/目标锁定和独立一次撤销规则。真实 Explorer 拖放、桌面文件移动、任务栏美化、小组件和 LPWP 插件运行时继续受各自准入门禁约束。
