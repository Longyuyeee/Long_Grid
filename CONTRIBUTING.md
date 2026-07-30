# Contributing to Long Grid

感谢参与 Long Grid。项目当前处于 Phase 0，优先验证 Windows 桌面集成、文件安全和交互风险。

开始前请阅读：

- [开发流程与交付规范](docs/10-development-workflow.md)
- [产品需求文档](docs/02-product-requirements.md)
- [交互设计规范](docs/09-interaction-design-audit.md)
- [技术架构](docs/03-architecture.md)
- [质量、安全与隐私基线](docs/05-quality-security.md)

## 提交工作前

1. 确认工作项类型和风险等级。
2. 检查 Definition of Ready。
3. 高风险 Windows 行为先建立 Technical Spike。
4. 长期技术决策使用 ADR。
5. 跨 Long助手的变更先修改 LPWP 协议。

## 分支

- 禁止直接向 `main` 推送。
- 使用短生命周期分支。
- 建议前缀：`feature/`、`fix/`、`spike/`、`docs/`。
- 自动代理分支使用 `codex/`。

## 提交

建议使用 Conventional Commits：

```text
feat(containers): add safe-reference drop flow
fix(display): preserve topology after reconnect
spike(shell): test public desktop enumeration
docs(workflow): define release gates
```

不要提交秘密、证书、生产令牌、个人 IDE 配置或生成目录。

## Pull Request

- 使用仓库 PR 模板。
- 一个 PR 保持一个主要意图。
- 描述用户影响、风险、测试和回滚。
- 列出文档同步情况。
- UI 变更提供状态截图或录像。
- Windows 互操作变更提供系统 build、DPI、显示器和恢复证据。
- 文件操作变更必须覆盖预览、冲突、取消、部分成功和撤销。

## 完成标准

PR 合并不等于功能完成。功能只有在[Definition of Done](docs/10-development-workflow.md#17-definition-of-done)全部满足后才能标记完成。

## 安全问题

不要在公开 Issue 中披露可利用漏洞、敏感路径、令牌或用户数据。正式安全报告渠道将在发布前补充；在此之前请联系仓库所有者。
