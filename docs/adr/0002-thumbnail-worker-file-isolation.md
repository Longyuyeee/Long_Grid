# ADR-0002：缩略图工作进程文件隔离

- 状态：Proposed
- 日期：2026-08-01
- 决策者：安全负责人待确认
- 关联 Issue/PR：Issue #22
- 影响版本：Phase 0 / 首个只读 MVP

## 背景

缩略图提供程序属于不受 Long Grid 控制的 Shell 扩展。探针先证明 Low Integrity 只阻止 write-up、不能阻止读取当前用户可读的其他文件；当前实现已把真实 `IShellItemImageFactory` worker 迁入零 Capability AppContainer，并保留硬超时、Job Object 生命周期、最小启动句柄和有界共享内存像素传输。

父进程现在把运行时暂存到随机临时 Profile 的私有存储，并为每个合法提取路径生成单文件最大 32 MiB、client 总计最大 64 MiB、拒绝重解析点的只读受控副本。协议 v6 保留 `ControlledCopy` 默认策略并增加 comparison-only 的 `MinimumPathAcl`：只给探针自有文件和父目录临时增加随机 AppContainer SID 的 Read/Traverse ACE，请求结束后精确删除并复核无显式残留。两种策略都能阻断相邻未授权读取；但 Windows 26100 上二者都在 Shell 提取阶段返回 `E_ACCESSDENIED`，因此 ACL 方案没有解决跨 build/provider 兼容性问题。

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

工作进程在无宽泛 Capability 的 AppContainer 中启动。父进程负责用户授权、策略、缓存和请求生命周期，只向单次请求授予受控输入；像素继续经有界共享内存返回。需要验证 Shell provider 在 AppContainer 下能否使用受控副本或原路径授权，并建立不支持 provider 的安全回退。当前 `IShellItemImageFactory` 入口接收 `IShellItem`，而探针通过 parsing name 创建该对象；原始文件句柄不能直接替换这条路径契约，句柄方案需要另一种 Shell item/provider、stream 或受控 decoder 架构实验。

## 决策

生产方向选择 **C：AppContainer + 父进程 broker**，等待安全负责人确认后转为 Accepted。

- AppContainer 默认不授予网络、用户目录或 broadFileSystemAccess 类能力；
- broker 按单次请求授权，输入与输出句柄不可继承，权限最小化并随 worker 回收；
- `ControlledCopy` 暂时保留为默认实验策略；`MinimumPathAcl` 仅用于兼容性比较，不进入产品默认，因为它临时修改用户文件及父目录 DACL，异常崩溃还缺少持久化清理日志；
- 若 provider 必须依赖原路径或邻接资源，则该 provider 进入显式兼容矩阵，不得自动扩大 Capability；
- 受控副本只能作为有大小、类型、水合和清理上限的显式回退；
- 仅受限 Low Integrity 模式保留为开发诊断基线，不允许处理任意真实用户文件；
- AppContainer/broker 失败时产品回退到类型图标或已验证缓存结果，不回退到主进程现场提取。

## 证据

- 实际 Low Integrity worker：读取未 broker 授权的自有中完整性文件成功，向中完整性目录 write-up 失败；
- 零 Capability AppContainer：三个挂起启动的控制进程均由 `TokenIsAppContainer` 复核，先加入 `KILL_ON_JOB_CLOSE` Job 再恢复；无操作控制成功，精确 AppContainer SID ACL 授权文件可读，相邻未授权文件被拒绝，临时 Profile 删除成功；
- 真实缩略图 worker：全部进程由 `TokenIsAppContainer` 复核，零 Capability、显式标准流句柄白名单、挂起后先入 Job；未代理读写均被拒绝，受控 BMP 副本可提取；
- 协议 v6 明确区分 `ControlledCopy` 与 `MinimumPathAcl`，输入上限 32 MiB、拒绝重解析点；正常回收和父进程无清理退出两种路径均删除临时 Profile；
- 矩阵增加普通父进程 Shell 对照和脱敏扩展级 handler 注册/模块健康布尔值，不输出 handler 身份、CLSID、厂商或路径；父进程结果也只能成功或精确 `0x8007007E`；
- Windows 22621 上 BMP/PNG/GIF/JPEG 的父进程及两种 worker 策略全部成功；TIFF-RGB/TIFF-LZW 在父进程和两种 worker 策略下均精确返回 `0x8007007E`，同时两项都观察到扩展级 handler 已注册但模块缺失，证明该失败不是 AppContainer 特有；默认副本压力 500/500、p95 48.19 ms；
- Windows 26100 GitHub runner 的普通父进程六样本全部成功且未观察到陈旧扩展 handler；同一六样本 × 两种 worker 策略的 12 个组合全部输入可读，但 `IShellItemImageFactory` 都稳定返回 `0x80070005`，把该环境差异明确定位到 AppContainer/Shell 边界；该分支只能安全回退到类型图标或已验证缓存；
- 矩阵只接受成功、精确 `0x80070005` 或精确 `0x8007007E`，并要求同格式两种输入策略完全一致；跨格式一致性只记录，不再把 provider 差异误判为门禁失败；
- 共享内存返回最大 256×256 BGRA32，九类像素/映射故障均被拒绝并恢复；
- Microsoft 文档说明 Mandatory Integrity Control 使用完整性策略限制访问，默认强制 no-write-up；AppContainer 用于隔离进程并按 capability/对象 ACL 控制资源访问。

## 后果

### 正面

- 明确区分“防止低权限进程写坏数据”和“限制它能读取哪些文件”；
- broker、缓存、provider 兼容性和降级行为有统一安全边界；
- 不需要推翻现有超时、生命周期和像素 IPC 实现。

### 负面

- AppContainer 创建、ACL/句柄授权、打包身份和企业策略矩阵增加实现与测试成本；
- 最小路径 ACL 会短时修改文件及父目录 DACL；正常路径已复核恢复，但父进程在 lease 存活时异常退出、并发 ACL 修改和遗留 ACE 修复尚未验证；
- 某些依赖原路径或进程外资源的第三方 provider 可能只能显示类型图标；
- 云文件水合和网络路径必须由父进程显式决策，不能由 worker 自主触发。

### 后续工作

1. 在六个自有格式/编码样本与父进程对照基线上继续验证 HEIF、Office/PDF、OneDrive、网络路径和受控安装的第三方 provider，并在干净环境复测 TIFF 默认 handler；保持失败时的类型图标/缓存回退，不修改用户注册表；
2. 把 handle-backed 输入作为独立的 stream/decoder 或 Shell provider 合同实验，不把原始句柄错误地当成当前 parsing-name API 的直接替代；
3. 为受控副本补充类型/水合策略、缓存预算和多文件隔离测试，不允许静默水合或扩大 Capability；
4. 若重新评估最小路径 ACL，先实现异常退出后的 ACE 日志/修复及并发 DACL 变更测试；
5. 将通过的合同迁入正式渲染接口，并由安全负责人把本 ADR 改为 Accepted 或 Revised。

## 回滚与重新评估

- 若受支持 Windows 版本无法稳定启动 AppContainer，或核心 provider 全部无法在最小授权下工作，则重新评估独立低权限服务、受控解码器或仅缓存图标方案；
- 回滚方式是关闭现场缩略图提取并显示类型图标，不回退到主进程加载第三方 provider；
- 协议版本、provider 支持矩阵和用户授权记录不得静默降级。

## 合规检查

- [x] 不依赖未文档化 Explorer 内部结构
- [x] 不降低文件安全和隐私基线
- [x] 不与 LPWP 或已发布契约冲突
- [x] 已更新相关路线图、审计和测试文档
