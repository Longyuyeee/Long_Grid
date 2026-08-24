# Stage 154：PF-001 桌面方格总开关实现与验收审计

- 审计日期：2026-08-16
- 开发编号：`PF-001`
- 分支：`codex/pf001-boxes-enabled`
- 对标目标：iTop Easy Desktop `Enable Boxes`、Fences 桌面分组的常驻启停体验
- 当前结论：`InProgress`；总开关主闭环工程完成，桌面空工作区创建入口留给 PF-002

## 1. 本步交付的用户结果

用户不再需要设置开发参数才能看到已有桌面方格。控制中心提供一个可被键盘和辅助技术发现的“显示桌面方格”开关：

1. 第一次启动默认开启；
2. 用户关闭后，方格窗口和交互表面被释放，但方格、引用和布局配置不删除；
3. 关闭期间仍可编辑配置，重新开启时按最新修订恢复；
4. 有效切换只写一次设置，重复点击不重复写入；
5. 保存失败时不改变桌面运行状态，并把 UI 回滚到原值；
6. 主设置损坏时读取上一次原子备份；主设置与备份均损坏时安全关闭并显示原因；
7. `LONGGRID_DISABLE_DESKTOP_HOST=1` 始终覆盖用户选择，用户不能从 UI 绕过紧急安全禁用。

本步没有移动、隐藏、重排、删除或重命名 Windows 原生桌面图标和真实文件。

## 2. 与对标产品的功能对齐

### 2.1 对齐的行为

| 对标行为 | Long方格本步行为 | 结论 |
| --- | --- | --- |
| iTop 可统一启用/禁用 Boxes | 控制中心提供单一 `BoxesEnabled` 开关 | 已对齐主语义 |
| 关闭整理表面不应删除用户布局 | 只释放 DesktopHost 资源，工作区配置保持不变 | 已对齐 |
| 再次开启恢复桌面分组 | 使用关闭期间缓存的最新权威投影重建窗口 | 已对齐 |
| 普通用户无需开发参数 | DesktopHost 默认进入 `EnabledForProduct` | 已对齐 |
| 软件必须有紧急安全退出能力 | 独立 emergency disable 优先于用户开关 | Long方格安全增强 |

### 2.2 尚未对齐的行为

| 缺口 | 原因 | 归属与下一验收点 |
| --- | --- | --- |
| 空工作区开启后没有桌面内“创建首个方格”表面 | 当前桌面宿主只为既有容器建窗 | PF-002 首个桌面创建入口 |
| 启动后控制中心仍作为可见主窗口 | PF-033 托盘入口尚未实现，直接隐藏控制中心会失去稳定恢复入口 | PF-001 后续桌面优先启动切片与 PF-033 联合收口 |
| 控制中心仍有“开发期/只读宿主”文案 | 文件操作和显式交互仍受独立安全门控制，不能提前改成全功能文案 | PF-005～PF-007 完成真实项目交互后更新 |
| 托盘与桌面入口尚未消费同一开关 | 托盘功能尚不存在 | PF-033 必须复用本步 controller，禁止另建状态 |

因此，PF-001 维持 `InProgress`，不能仅凭自动化通过标记为 `Complete`。

## 3. 实现审计

### 3.1 产品能力策略

`ProductDesktopHostFeaturePolicy` 已从“只有 `LONGGRID_ENABLE_DESKTOP_HOST=1` 才启用”改为正常产品默认启用：

- 未设置旧变量：`EnabledForProduct`；
- 旧变量显式为 `0`：`DisabledBySafetyPolicy`；
- `LONGGRID_DISABLE_DESKTOP_HOST=1`：`DisabledByEmergencyPolicy`；
- emergency disable 的判定顺序高于旧变量和用户设置。

该变化只默认启用被动 DesktopHost。桌面显式交互、Intent Bridge 和原生输入转发仍保留各自精确 opt-in，没有扩大真实文件操作或全局输入权限。

### 3.2 用户设置边界

新增独立的用户级设置文件：

```text
%LOCALAPPDATA%\LongGrid\settings.json
%LOCALAPPDATA%\LongGrid\settings.backup.json
```

当前合同：

```json
{
  "schemaVersion": 1,
  "boxesEnabled": true
}
```

没有把 `BoxesEnabled` 放入工作区配置，理由如下：

1. 开关属于产品运行偏好，不属于可导入/导出的方格内容；
2. 导入另一个工作区不应在用户不知情时显示或隐藏桌面方格；
3. 用户偏好损坏不应把完整工作区配置判为 SafeMode；
4. 设置文件可以独立回退，且不会增加工作区 schema 迁移风险。

### 3.3 持久化事务

保存流程为同目录临时文件、写穿透、刷新、原子替换，并保留上一次主文件作为备份：

1. 先完整序列化当前 schema；
2. 使用唯一临时文件名，避免覆盖其他在途写入；
3. 写入和刷新成功后才替换主文件；
4. 替换现有主文件时生成 `settings.backup.json`；
5. 失败时清理本次临时文件；
6. controller 只有在保存成功后才更新权威内存状态；
7. 同值请求返回 `Unchanged`，不触发第二次写入。

### 3.4 DesktopHost 生命周期

新增 `DisabledByUser`，与 `DisabledBySafetyPolicy` 明确区分：

- `SetUserEnabled(false)` 先失效 Prepared Input 和活动选择；
- 释放所有 activation source；
- 分离被动交互表面；
- 从窗口注册桥注销每显示器窗口；
- Dispose 所有 DesktopHost 表面；
- 清空窗口、注册和当前 batch；
- 保留最近一次投影 update 的修订和内容；
- 关闭期间的新权威投影只更新缓存，不创建原生窗口；
- `SetUserEnabled(true)` 按最新 `Ready / EmptyWorkspace / TopologyRefreshing` 状态恢复。

安全策略禁用时，`SetUserEnabled(true)` 直接返回既有安全状态，不创建任何表面。

### 3.5 正式 UI

DesktopHost 状态卡新增标准 WinUI `ToggleSwitch`：

- 可访问名称：`显示桌面方格`；
- 自动化 ID：`BoxesEnabledToggle`；
- 可见 On/Off 文案；
- 保存期间禁用，避免快速点击形成竞态；
- 状态文本使用 polite live region；
- 加载、保存、未变化、失败回滚、备份恢复和损坏安全关闭均有独立文案；
- 代码更新开关时抑制 `Toggled` 回调，避免启动恢复被误认为用户提交。

## 4. 失败与恢复矩阵

| 场景 | 权威结果 | DesktopHost 结果 | 用户反馈 |
| --- | --- | --- | --- |
| 首次安装，无设置文件 | 默认 `true`，不立即落盘 | 恢复当前工作区投影 | 默认开启 |
| 用户从开切到关 | 成功写入一次 `false` | 释放全部表面 | 已关闭、布局保留 |
| 用户从关切到开 | 成功写入一次 `true` | 恢复最新投影 | 已开启、恢复布局 |
| 重复请求当前值 | `Unchanged`，零写入 | 零生命周期变化 | 状态未变化 |
| 设置写入失败 | 保持旧值 | 保持旧运行状态 | 明确回滚提示 |
| 主设置损坏、备份有效 | 采用备份 | 按备份值运行 | 明确备份恢复 |
| 主设置与备份均损坏 | `SafeDisabled` | 零 DesktopHost 表面 | 明确安全关闭，可重试保存 |
| emergency disable 开启 | 用户值不能覆盖 | 始终零表面 | 安全策略禁用 |
| 关闭期间工作区更新 | 用户值仍为关 | 只缓存最新投影 | 开启后恢复最新修订 |
| 关闭应用 | 等待既有保存排空 | 释放设置 gate 和所有桌面资源 | 不留下后台宿主 |

## 5. PF-001 验收记账

| Stage 153 验收目标 | 当前证据 | 状态 |
| --- | --- | --- |
| 开启、关闭、重启恢复各只产生一次配置提交 | controller 重复请求测试、真实 store 重启测试 | 工程通过 |
| 关闭后桌面方格、输入区域和 UIA 子树为零 | 生命周期 fake surface/activation 释放测试；worker 当前未由该开关启动 | 工程通过，实机 UIA 待证据 |
| 开启后 1 秒内出现已有方格或明确空状态 | 已有方格可由缓存立即重建；桌面空状态未实现 | 部分通过 |
| 崩溃恢复、配置损坏和 Host 失败进入安全状态 | 双损坏 safe-disabled、保存失败回滚、既有 Host fault | 工程通过 |
| 键盘和 Narrator 读取名称、状态和失败原因 | ToggleSwitch、AutomationId、ItemStatus、LiveRegion 已接线 | 工程通过，Narrator 实机待证据 |
| 不改变 Windows 原生桌面图标 | 代码只管理 Long方格窗口和配置引用 | 通过 |
| 覆盖首次值、迁移、重复点击、失败回滚和重启 | 首次值、schema 拒绝、重复、失败、重启、备份测试 | 工程通过 |
| 不需要开发参数 | 默认 `EnabledForProduct` | 通过 |

## 6. 自动化与构建结果

- PF-001A 专项测试：`45/45`；
- PF-001A + PF-001B 专项测试：`51/51`；
- 全量测试：`942/942`；
- Release App 构建：`0` warning、`0` error；
- `dotnet format --verify-no-changes`：通过；
- UI 源码合同与批量选择无障碍入口：`146` 个 AutomationId，通过；
- `git diff --check`：提交前必须再次通过；
- PR CI 和合并后 main CI：推送后补充远端证据。

## 7. 下一开发步骤

1. 本分支完成 PR、CI、合并和 main CI；
2. 进入 PF-002，先在空 DesktopHost 上提供“创建第一个方格”桌面入口；
3. PF-002 创建成功后复用现有工作区保存链并立即刷新本步生命周期投影；
4. PF-033 增加托盘时必须调用同一个 `ProductBoxesSettingsController`，不得增加第二份开关；
5. PF-005～PF-007 完成真实项目呈现和打开后，清理控制中心遗留的开发期/只读文案；
6. 在真人环境补充 Narrator、UIA 树归零和 1 秒呈现证据后，再评估 PF-001 是否达到 `EngineeringComplete / ProductEvidencePending`。

## 8. 2026-08-24 桌面优先启动收口增量

Stage 182 已移除正常启动无条件激活控制中心的遗留行为，并以有限状态策略区分可用桌面、空工作区、系统表面挂起、方格关闭、配置需注意、Host 故障和第二次用户启动。正式 Release App 真实窗口验证首次只显示 DesktopHost，20 秒持续响应；第二进程退出码 0 并激活唯一控制中心，退出后零活进程、零临时配置写入。

PF-001 因此调整为 `EngineeringComplete / ProductEvidencePending`。Narrator、跨进程 UIA 子树和物理输入仍未在安全环境取得完整产品证据，顶层完成数不增加。详细 Expected/Actual、测试差异和下一 PF-004 方向见 [Stage 182](182-pf001-desktop-first-startup-audit.md)。
