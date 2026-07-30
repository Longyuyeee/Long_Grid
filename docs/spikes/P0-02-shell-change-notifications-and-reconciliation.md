# P0-02：Shell 变化通知与最终一致性

执行日期：2026-07-30
结果：**Pass（仅限当前机器和探针范围）**
前置：P0-01a/P0-01b/P0-01c

## 1. 目标

验证 Long Grid 能否：

1. 通过公开 API 注册真实 Desktop Namespace 变化通知；
2. 在不修改真实桌面的前提下，对自有临时目录执行高频变化压力测试；
3. 正确处理 Shell 合并、延迟或缺失具体事件的行为；
4. 通过静默窗口和最大等待时间触发全量对账；
5. 即使不回放任何单项事件细节，仍恢复最终一致状态；
6. 默认不记录名称、路径、PIDL 或文件身份。

## 2. 官方行为与架构结论

[`SHChangeNotifyRegister`](https://learn.microsoft.com/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotifyregister) 会将变化消息发送给指定窗口。探针使用官方推荐的 `SHCNRF_NewDelivery`，并通过 [`SHChangeNotification_Lock`](https://learn.microsoft.com/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotification_lock) 读取共享内存中的事件和 PIDL，再始终调用 Unlock。

微软明确说明高频通知可能被合并，例如大量 `UPDATEITEM` 可以合并为一次 `UPDATEDIR`。因此：

```text
Shell notification = invalidate / dirty hint
full enumeration    = source of truth
```

禁止把“通知数量等于操作数量”作为正确性指标，也禁止只靠逐事件回放维护最终桌面状态。

## 3. 实现

探针创建 STA 消息线程和 message-only window，分别注册：

- Shell Desktop 根节点：递归、真实桌面只读观察；
- `%TEMP%\LongGrid-P0-02\<random-guid>`：递归、受控压力测试。

注册标志：

```text
SHCNRF_InterruptLevel
| SHCNRF_ShellLevel
| SHCNRF_NewDelivery
```

Core 中的 `ChangeReconciliationGate` 采用：

- 静默窗口：750 ms；
- 最大延迟：3 s；
- 任意沙箱相关通知只标记 dirty；
- 静默窗口结束或最大延迟到达后执行全量扫描；
- 对账成功后清除 dirty 状态。

最大延迟防止持续事件让对账永久饥饿。

## 4. 安全与隐私

- 真实 Desktop Namespace 只注册、只观察，不产生测试变化；
- 1,100 次生成操作全部限制在随机临时沙箱；
- 清理前规范化路径，并验证必须位于固定 owned root 之下；
- 不执行 Explorer 注入、Hook、未文档化 WorkerW 操作或文件 Shell 动词；
- PIDL 只在监听线程内临时使用；
- 报告只输出计数和布尔结论；
- 所有注册、通知锁、PIDL、窗口、COM apartment 和线程均有对称释放。

## 5. 压力场景

| 操作 | 数量 |
|---|---:|
| 创建文件 | 400 |
| 重命名文件 | 400 |
| 删除一半文件 | 200 |
| 创建目录 | 40 |
| 重命名目录 | 40 |
| 删除一半目录 | 20 |
| 合计 | 1,100 |

预期最终状态为 200 个文件和 20 个目录，共 220 项。

## 6. 实测结果

环境：

| 项目 | 值 |
|---|---|
| OS | Microsoft Windows NT `10.0.26200.0` |
| 架构 | x64 |
| .NET SDK | `8.0.419` |
| Target Framework | `net8.0-windows` |

连续三轮结果：

| 指标 | 结果 |
|---|---:|
| Desktop 注册 | 3/3 成功 |
| 沙箱注册 | 3/3 成功 |
| 每轮文件操作 | 1,100 |
| 每轮沙箱通知 | 1–4 |
| 通知类型 | 全部被合并为 UpdateDirectory 类 |
| 对账触发 | 3/3 成功 |
| 预期最终项目 | 220 |
| 实际最终项目 | 220 |
| 缺失项目 | 0 |
| 多余项目 | 0 |
| 单轮耗时 | 2.6–3.3 s |
| 沙箱残留 | 0 |

这意味着 1,100 个文件系统变化最终只产生少量目录失效提示；创建、删除和重命名细节没有被逐项交付，但全量对账仍恢复了精确状态。

## 7. 工程验证

- Core 静默窗口、延长窗口、最大延迟和清理状态均有单元测试；
- Release build：0 warnings / 0 errors；
- xUnit：15/15 通过；
- 所有原生资源采用对称注册/释放；
- 默认报告完全脱敏；
- 依赖未新增第三方运行时包。

## 8. 判定

`P0-02` Pass：

- 公开 Shell API 可以注册 Desktop 和指定文件系统目录；
- `SHCNRF_NewDelivery` 的 Lock/Unlock 路径工作正常；
- 实测确认高频事件会被强烈合并；
- 脏标记 + 静默窗口 + 最大延迟 + 全量对账能够达到最终一致；
- 真实桌面无测试写入；
- 临时沙箱完整清理。

生产实现不得承诺实时逐事件准确，只能承诺在明确时间预算内达到最终一致。

## 9. 后续

1. P0-03：`IShellItemImageFactory` 图标/缩略图异步加载、取消和句柄泄漏；
2. Explorer 重启时重建窗口、PIDL 和注册，不重建领域对象；
3. 注入“完全不读取通知详情”的故障模式，确认周期对账仍可恢复；
4. 扩展到 10,000 次变化与 24 小时长稳；
5. 验证 OneDrive 重定向、SMB、ReFS、ARM64 和权限受限账户；
6. 为生产 `ShellChangeCoordinator` 增加代次号，丢弃过期扫描结果。
