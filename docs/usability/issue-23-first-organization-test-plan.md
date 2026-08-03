# Issue #23 首次整理五人可用性测试计划

状态：**Ready to run / Results Pending**

目标：验证新用户能否在没有主持人操作提示的情况下，理解“一键建议/从空白开始”和“安全引用/真实移动”的区别，创建匿名方格、立即撤销，并确认全程没有修改文件。

## 1. 安全与隐私

- 使用当前开发期只读 App 和匿名示例，不使用参与者真实桌面文件；
- 不记录姓名、账户、文件名、路径或桌面截图；
- 参与者仅记为 P1–P5；
- 若应用访问真实桌面、出现文件系统确认或产生外部副作用，立即停止并判为 Critical；
- 主持人只读任务目标，不解释按钮含义；需要提示时记录提示次数。

## 2. 环境记录

| 字段 | 值 |
|---|---|
| Commit / PR | Pending |
| Windows build | Pending |
| 缩放与分辨率 | Pending |
| 输入方式 | Pending |
| 主题 | Pending |
| 主持人 | Pending |
| 执行日期 | Pending |

不得在此表写入参与者身份信息。

## 3. 无提示任务

主持人依次读出目标，不描述操作路径：

1. “请找到第一次整理桌面的入口。”
2. “请选择让软件先给出建议，但不要改变任何文件的方式。”
3. “改为从空白开始，并说明此时软件会不会创建容器。”
4. “请选择不会移动原始文件的整理方式，并说明原生桌面图标可能怎样。”
5. “查看真实移动需要什么，并判断现在能否执行。”
6. “回到安全方式生成预览，并判断是否已经修改文件。”
7. “创建你的第一个方格，加入三个项目，并说明这些内容现在保存在哪里。”
8. “分别判断 Explorer 拖入安全方格、方格之间拖动和请求移动文件会发生什么。”
9. “先撤销加入项目，再撤销方格，并判断是否有文件被删除。”
10. “打开恢复预览，判断三种规划结果是否能继续；让预览过期，再取消并说明布局是否改变。”

当前原型只包含单个匿名内存容器、三个固定匿名引用、拖放语义练习、两步关系级撤销和匿名恢复差异，不包含真实拖放、真实显示拓扑、多个容器、持久化、窗口恢复或文件操作撤销；这些任务不得伪记为通过。任务 7 记录从读完目标到三个项目可见的时间，任务 9 分别记录两次撤销的完成时间。

## 4. 单人记录表

每位参与者复制一行；时间从任务读完开始，到参与者明确给出答案结束。

| 参与者 | 找到入口 | 建议起点 | 空白含义 | 引用/移动判断 | 三种拖放判断 | 移动被阻断 | 安全预览无修改 | 创建+3 项耗时 | 两步撤销耗时 | 撤销无删文件 | 恢复三态判断 | 过期/取消无修改 | 总提示数 | 严重问题 | 备注 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| P1 | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| P2 | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| P3 | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| P4 | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending |
| P5 | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending |

每个任务另记完成时间和首次错误动作；不得只记最终成功。

## 5. 严重度

| 等级 | 定义 |
|---|---|
| Critical | 认为安全引用会移动/删除文件，或认为被阻断的真实移动已经执行 |
| High | 无法区分两个模式，或在主持人提示后仍无法完成 |
| Medium | 首次选择错误但能自行恢复，或不能解释原生图标仍可能存在 |
| Low | 文案、焦点、滚动或视觉层级造成迟疑，但不改变安全判断 |

## 6. 通过门槛

- P1–P5 全部正确识别安全引用不会移动文件；
- P1–P5 全部正确识别真实移动当前被阻断；
- 至少 4/5 无提示完成建议预览；
- 无 Critical，且 High 必须修复并重测；
- 每人都能看到或复述“尚未修改任何文件”。
- 至少 4/5 无提示创建匿名方格并加入三个项目，五人完成耗时中位数小于 2 分钟；
- P1–P5 全部正确判断添加引用、改变归属与移动阻断；
- P1–P5 全部按最近动作顺序完成两步撤销并识别没有文件被删除，单步撤销耗时中位数小于 5 秒。
- P1–P5 全部识别 ReviewRequired 需确认、Blocked 禁止部分应用、过期预览不可确认，并确认取消没有改变布局。

五人样本下，原设计目标“引用/移动识别 ≥95%”实际要求 5/5，不得用四舍五入把 4/5 写成通过。

## 7. 负责人决策（测试后填写）

| 决策 | 选择 | 理由 | 日期 | 批准人 |
|---|---|---|---|---|
| 首版整理模式 | Pending | Pending | Pending | Pending |
| 许可证 | Pending | Pending | Pending | Pending |
| Windows / 架构范围 | Pending | Pending | Pending | Pending |
| 安装渠道 | Pending | Pending | Pending | Pending |
| 性能与资源预算 | Pending | Pending | Pending | Pending |
| ADR-0001 Go / Revise / No-Go | Pending | Pending | Pending | Pending |

测试结果、缺陷链接和决策写回 Issue #23 后，才能重新审计该出口门禁。
