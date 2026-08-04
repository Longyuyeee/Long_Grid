# 配置 latest-wins 与 App 关闭排空审计

审计日期：2026-08-04

基线：`main` / `0790459` + 配置关闭排空增量分支

结论：**Latest-wins and bounded App drain pass / Development shell remains zero-write / Issue #24 保持 OPEN**

## 1. 本轮目标

把 P0-06 已验证的 latest-wins 保存语义迁移到正式 `LongGrid.Infrastructure`，并接入 `LongGrid.App` 关窗生命周期。接线必须保留现有只读 UI 承诺：匿名练习、主题和恢复预览仍只驻留内存，不得因为建立协调器就在启动或关闭时写用户配置。

## 2. 正式协调合同

| 合同 | 实现与证据 | 状态 |
|---|---|---|
| 入队快照 | 每次入队经正式 v1 JSON 往返形成独立深快照 | Pass |
| latest-wins | 进行中保存不中断；等待批次合并为最新快照 | Pass |
| 等待者语义 | 被合并调用方共同观察最新批次完成 | Pass |
| 取消隔离 | 调用方取消只结束自身等待，不撤销已接受保存 | Pass |
| 失败恢复 | 一次物理保存失败只失败该批次，worker 继续处理后续批次 | Pass |
| 完成边界 | `CompleteAsync` 原子停止接收新请求并等待已接受批次 | Pass |
| 排空超时 | 超时只取消等待，后台保存继续；再次完成可观察最终排空 | Pass |

受控租约测试先阻塞首个保存，再入队 100 个状态。释放租约后，主文件为 `profile-100`，备份仍为 `profile-first`，证明只发生首个与最新两个产品提交，而不是 101 次物理写入。

## 3. App 生命周期接线

- App 创建正式 Store/Coordinator，但构造过程不创建目录、锁文件或配置文件；
- `AppWindow.Closing` 首先取消本次系统关窗，再调用 `CompleteAsync`；
- 排空上限固定为 5 秒；成功后解除 Closing 处理器并正常关闭；
- 超时则保留窗口，协调器继续完成已接受保存，用户再次关窗可重新等待；
- 并发关窗由 `closingDrainInProgress` 合并为一次等待；
- 当前 App 源码没有 `configurationSaves.EnqueueAsync`，因此开发期只读 UI 保持零写入。

真实 UI 冒烟完成宽/紧凑布局、导航、主题和匿名原型交互后，通过正常窗口关闭退出。关闭后确认 `LongGrid.App` 无残留进程，且 `%LOCALAPPDATA%\LongGrid`、`configuration.json` 和 `.new` 均未创建。

## 4. 自动验证

- Release solution build：零警告、零错误；
- 131/131 自动测试通过；
- 聚合覆盖率：行 91.92%，分支 79.93%；
- UI 合同新增 `configurationShutdownDrain=bounded-zero-write-retry`；
- 真实 UI 启动、交互与正常关闭通过；
- 全量 UI 冒烟复核发现恢复差异面板的一次性 250ms 可见性复读连续失败；证据工具改为 5 秒内重复 `ScrollIntoView` 并复读 `IsOffscreen`，仍要求最终可见，不改变产品动画或放宽结论；
- 原有配置、文件操作、缩略图隔离和漏洞门禁继续作为全量 CI 门禁。

## 5. 边界与下一步

本轮关闭的是“正式保存协调器与 App 关闭排空结构门槛”，不是完整配置体验。尚未完成：

1. `RecoveredFromBackup`/`SafeMode` 恢复 UI；
2. 真实保存批次关闭期间的第二实例激活竞态矩阵；
3. 经批准的真实产品状态入队与错误提示；
4. I24-01/I24-02 真实专用卷证据；
5. 断电、非 NTFS、企业重定向目录和长期跨进程压力。

在这些证据完成前，Issue #24 继续保持 OPEN；不得把零写入关闭烟测描述为真实配置保存体验已经上线。
