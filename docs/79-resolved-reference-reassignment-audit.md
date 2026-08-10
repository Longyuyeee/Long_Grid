# 已解析引用原子改归属与一次撤销审计

> 审计日期：2026-08-10
>
> 范围：正式方格间已解析引用的原子配置改归属、一次撤销、UIA 与保存边界
>
> 结论：**Conditional Pass；配置归属可安全调整，真实桌面文件操作继续为零**

## 1. 需求对齐

加入、移除与移除撤销已经形成基础闭环，但用户若要把项目从一个方格换到另一个方格，不能被迫先删除再重新从桌面目录加入。本切片提供直接改归属：

- 选择未锁定方格中的已解析引用；
- 选择不同的未锁定目标方格；
- 以单个原子配置编辑完成源移除和目标加入；
- 在没有其他成功编辑或外部重载时撤销最近一次改归属。

它不实现 Explorer 拖放，也不授权桌面文件移动。

## 2. Core 原子边界

`ProductWorkspaceReducer.ReassignResolvedReference` 在一份深快照上同时校验源方格、目标方格和项目：

- 源或目标不存在返回有限 `ContainerNotFound`；
- 源或目标锁定返回 `ContainerLocked`；
- 项目不存在返回 `ItemNotFound`；
- 未解析引用不进入该链；
- 同一源/目标返回 `Changed=false`；
- 成功时保留项目领域 ID、Catalog 身份与未知扩展字段，并保持输入状态不变。

没有任何中间状态提交给保存控制器，因此不会发生“源方格已经删除、目标方格尚未加入”的部分保存。

## 3. 提交与撤销合同

共享 `ProductWorkspaceCommitCoordinator` 核对 edit revision、来源/项目/目标序号和不同方格约束，再调用 Core reducer、正式 projector/validator 和唯一 `ProductWorkspaceSaveController`。一次接受只执行一次 `Submit` 并只推进一次 revision。

改归属撤销令牌独立绑定随机操作 ID、改归属后的 revision、改归属结果指纹和恢复状态指纹。令牌、revision 或当前配置任一变化都会拒绝撤销；其他成功引用编辑、容器编辑、布局恢复或外部重载会清除令牌，成功撤销后立即消费。

## 4. 交互、隐私与 UIA

管理已加入引用区域复用既有来源选择器和撤销按钮，新增：

- `ProductWorkspaceResolvedReferenceReassignmentTargetSelector`；
- `ProductWorkspaceResolvedReferenceReassignmentButton`。

UI 合同由 125 增至 127。只有存在已解析来源、至少两个未锁定方格且目标不同于来源时，提交按钮才开放。presentation 仅含可见方格名称与序号，不含持久化 target、路径、ProfileId、SourceId、ContainerId、ItemId、ParsingName、VolumeId 或 FileId。机器状态继续固定声明 `DesktopFilesChanged=False`。

## 5. 自动化证据与剩余风险

定向测试覆盖 reducer 原子移动、领域 ID 保持、输入不可变、同方格 no-change、锁定目标、旧 revision、成功提交、一次撤销、第二次撤销拒绝和真实临时桌面文件内容不变。撤销单元测试覆盖确认、revision、令牌与配置指纹门禁。全量 Release 测试为 531/531 通过；单份 Cobertura 为行 91.31%（7578/8299）、分支 80.71%（2129/2638），通过 90%/75% 门槛。127-ID 源码合同和干净会话 `ValidateOnly` 已通过。

合入前仍必须通过格式、Release 构建、全量测试、单份 Cobertura 90% 行/75% 分支门槛、启动链、单实例、漏洞、内部 RC 和远端 PR/main CI。真实 live UIA、Explorer 数据对象、文件移动、Narrator 和硬件矩阵仍保持 Pending。

## 6. 下一步

只读配置分组链已经具备加入、移除、改归属及一次撤销。下一产品切片应优先评估正式方格删除/批量选择的安全产品语义，或转入已批准的可见 DesktopHost 产品接线；真实拖放与托管文件移动必须继续经过独立准入，不得从本切片推导授权。任务栏美化、小组件和 LPWP 插件运行时仍属于后续模块。
