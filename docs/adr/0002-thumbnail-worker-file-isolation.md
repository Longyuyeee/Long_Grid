# ADR-0002：缩略图工作进程文件隔离

- 状态：Proposed
- 日期：2026-08-01
- 决策者：安全负责人待确认
- 关联 Issue/PR：Issue #22
- 影响版本：Phase 0 / 首个只读 MVP

## 背景

缩略图提供程序属于不受 Long Grid 控制的 Shell 扩展。现有探针已把调用移入可回收的 Low Integrity 受限工作进程，并实现硬超时、Job Object 生命周期、最小启动句柄和有界共享内存像素传输。然而工作进程仍接收原始路径；Low Integrity 默认阻止 write-up，却不会自动阻止读取当前用户本来可读的其他文件。

最新实际子进程探针读取了父进程创建、但未通过 broker 授予的中完整性标记文件，同时向同一沙箱写入仍被阻断。这证明受限 Low Integrity token 可以降低写破坏和权限滥用风险，但不能独立承担文件保密边界。

## 决策驱动因素

- 受损或恶意 provider 只能访问当前请求明确授予的输入；
- 不把任意用户路径、目录枚举权或网络访问能力隐式交给 worker；
- 保留 Shell provider 对扩展名、文件名、邻接文件或原路径的兼容性差异证据；
- 沿用已验证的硬超时、Job Object、协议上限和共享内存结果通道；
- 不以复制用户文件或静默水合云文件作为默认行为。

## 选项

### A：仅使用受限 Low Integrity token

保留现有启动模型，直接传入原始路径。优点是兼容性高、实现简单；缺点是 worker 仍可读取同一用户可读的非请求文件。实际探针已经证明该暴露，因此不能作为生产文件保密边界。

### B：受限 token 加受控副本

父进程把文件复制到随机 broker 目录后传入副本路径。它可以隐藏原始路径并限制单次输入，但复制会改变原路径、文件名、邻接资源、备用数据流、云占位和 provider 语义，而且 Low Integrity worker 仍能读取沙箱之外的其他用户可读文件。可作为不可信格式的兼容性实验，不能替代进程隔离。

### C：AppContainer + 父进程 broker

工作进程在无宽泛 Capability 的 AppContainer 中启动。父进程负责用户授权、策略、缓存和请求生命周期，只向单次请求授予受控输入；像素继续经有界共享内存返回。需要验证 Shell provider 在 AppContainer 下能否使用 brokered handle、受控副本或原路径授权，并建立不支持 provider 的安全回退。

## 决策

生产方向选择 **C：AppContainer + 父进程 broker**，等待安全负责人确认后转为 Accepted。

- AppContainer 默认不授予网络、用户目录或 broadFileSystemAccess 类能力；
- broker 按单次请求授权，输入与输出句柄不可继承，权限最小化并随 worker 回收；
- 首选不复制且可表达最小只读授权的路径；若 provider 必须依赖原路径或邻接资源，则该 provider 进入显式兼容矩阵，不得自动扩大 Capability；
- 受控副本只能作为有大小、类型、水合和清理上限的显式回退；
- 仅受限 Low Integrity 模式保留为开发诊断基线，不允许处理任意真实用户文件；
- AppContainer/broker 失败时产品回退到类型图标或已验证缓存结果，不回退到主进程现场提取。

## 证据

- 实际 Low Integrity worker：读取未 broker 授权的自有中完整性文件成功，向中完整性目录 write-up 失败；
- 零 Capability AppContainer：三个挂起启动的控制进程均由 `TokenIsAppContainer` 复核，先加入 `KILL_ON_JOB_CLOSE` Job 再恢复；无操作控制成功，精确 AppContainer SID ACL 授权文件可读，相邻未授权文件被拒绝，临时 Profile 删除成功；
- 500/500 合成 BMP 提取、250 ms 硬超时与恢复、父进程退出清理通过；
- 共享内存返回最大 256×256 BGRA32，九类像素/映射故障均被拒绝并恢复；
- Microsoft 文档说明 Mandatory Integrity Control 使用完整性策略限制访问，默认强制 no-write-up；AppContainer 用于隔离进程并按 capability/对象 ACL 控制资源访问。

## 后果

### 正面

- 明确区分“防止低权限进程写坏数据”和“限制它能读取哪些文件”；
- broker、缓存、provider 兼容性和降级行为有统一安全边界；
- 不需要推翻现有超时、生命周期和像素 IPC 实现。

### 负面

- AppContainer 创建、ACL/句柄授权、打包身份和企业策略矩阵增加实现与测试成本；
- 某些依赖原路径或进程外资源的第三方 provider 可能只能显示类型图标；
- 云文件水合和网络路径必须由父进程显式决策，不能由 worker 自主触发。

### 后续工作

1. 把现有缩略图 worker 迁入无宽泛 Capability 的 AppContainer，并保留挂起启动、Job、标准流白名单、硬超时与共享内存合同；
2. 在实际 Shell 提取中比较 brokered handle、受控副本和已通过边界探针的最小路径 ACL 三种单请求输入方式；
3. 验证 BMP、常见图片、Office/PDF、OneDrive、网络路径和第三方 provider；
4. 将通过的合同迁入正式渲染接口，并保留类型图标安全回退；
5. 由安全负责人确认后把本 ADR 改为 Accepted 或 Revised。

## 回滚与重新评估

- 若受支持 Windows 版本无法稳定启动 AppContainer，或核心 provider 全部无法在最小授权下工作，则重新评估独立低权限服务、受控解码器或仅缓存图标方案；
- 回滚方式是关闭现场缩略图提取并显示类型图标，不回退到主进程加载第三方 provider；
- 协议版本、provider 支持矩阵和用户授权记录不得静默降级。

## 合规检查

- [x] 不依赖未文档化 Explorer 内部结构
- [x] 不降低文件安全和隐私基线
- [x] 不与 LPWP 或已发布契约冲突
- [x] 已更新相关路线图、审计和测试文档
