# Stage 128：已合并远端分支卫生审计

- 日期：2026-08-13
- 基线：`main@d1f9e0a`（PR #177；main CI 31713801581 通过）
- 范围：仅 `origin/codex/*` 已合并短生命周期分支
- 结论：**32 个历史分支符合删除条件；审计分支在本 PR 合并后按同一条件复核**

## 1. 需求与风险对齐

项目约定每个开发切片使用短生命周期分支，合并后由 `main` 和 PR 保存权威历史。长期保留大量已合并 `codex/*` 会让开发者误判当前工作线、增加拉取/审计噪声，也与最初“把最新内容收束到主分支”的要求偏离。

本阶段只清理已完整进入 `main` 的 Codex 分支，不删除 `main`、标签、PR、提交对象、本地分支或其他命名空间，不改代码、产品行为、Issue 状态和阶段结论。

## 2. 删除准入条件

每个候选必须同时满足：

1. 名称以 `codex/` 开头；
2. 当前没有以该分支为 head 的开放 PR；
3. 存在状态为 MERGED 的 PR，且 PR `headRefOid` 精确等于当前远端分支 tip；
4. 该 PR 的 merge commit 是当前 `origin/main` 的祖先；
5. 远端分支 tip 与 merge commit 的 Git tree 完全相同。

项目使用 squash merge，因此原 head commit 通常不是 `main` 的祖先。第 3～5 条共同证明“当前远端 tip 正是已审查 head，合并提交已在主线，且合并后的文件树没有丢失内容”。任一条件失败都必须保留分支并单独调查。

## 3. 2026-08-13 候选清单

| 远端分支 | PR | main merge |
| --- | ---: | --- |
| `codex/batch-accessibility-manual-matrix` | #136 | `cbab8254950c` |
| `codex/c0-closeout-c1-readiness-audit` | #175 | `1d6e817869be` |
| `codex/c2-manual-matrix-readiness-audit` | #176 | `2ea1f6c76b62` |
| `codex/c5a-product-store-volume-host` | #177 | `d1f9e0ad4515` |
| `codex/ci-dispatcher-test-determinism` | #144 | `54cf6da17805` |
| `codex/ci-vstest-hang-diagnostics` | #143 | `fed75df015ff` |
| `codex/configuration-staging-adapter` | #118 | `81fbc3f7fa87` |
| `codex/config-window-composite-transaction` | #116 | `d36a222e7541` |
| `codex/desktop-host-lifecycle-flag` | #155 | `59589f92146f` |
| `codex/desktop-host-owned-window-registry` | #115 | `90270341bb5b` |
| `codex/formal-container-card-action-layout` | #147 | `e227338ad71b` |
| `codex/formal-container-health-filter` | #140 | `92b2dfcc2d0e` |
| `codex/formal-container-health-state` | #139 | `3ec193967643` |
| `codex/formal-container-name-guidance` | #152 | `5915e7632002` |
| `codex/formal-container-quick-collapse` | #145 | `b211cc29ab48` |
| `codex/formal-container-quick-lock` | #146 | `32d3531f29ea` |
| `codex/formal-workspace-empty-create-shortcut` | #151 | `e37e9269046a` |
| `codex/issue-21-22-closure-readiness` | #78 | `69036dfc8aeb` |
| `codex/issue-23-decision-proposal` | #79 | `fa4001207ae7` |
| `codex/issue-23-scope-approval` | #80 | `ead23e04ba8b` |
| `codex/issue-24-dedicated-session` | #82 | `19e235920e8e` |
| `codex/phase0-internal-rc-closeout-plan` | #174 | `508528b5e51c` |
| `codex/post-issue-23-alignment` | #81 | `3dd39a082d8a` |
| `codex/rc-delivery-audit-entry` | #126 | `f30098f3e14a` |
| `codex/rc-msix-identity` | #124 | `298285783cb4` |
| `codex/rc-portable-publish` | #123 | `2ad71bcf9ecf` |
| `codex/rc-sbom-signing-contract` | #125 | `33262f0c6b2c` |
| `codex/save-stale-revision-admission` | #153 | `043c7fb92dfd` |
| `codex/stage-103-product-plan` | #154 | `00fad72f27f3` |
| `codex/unified-latest-edit-undo` | #138 | `a03f7b859fbb` |
| `codex/verified-window-batch-adapter` | #117 | `a101782115b5` |
| `codex/versioned-saved-topology` | #111 | `7836204aca14` |

审计时开放 PR 数为 0；32/32 分支均满足全部条件。

## 4. 执行与恢复纪律

1. 本文 PR 通过完整 CI 并合并；
2. 确认合并后 `main` CI 成功；
3. 重新 fetch 并按第 2 节逐项复核，防止审计后分支漂移；
4. 使用一次明确的 `git push origin --delete <exact names>` 删除通过复核的分支；
5. 对本审计分支执行同样复核后删除；
6. `git fetch --prune origin`，确认远端只保留 `main` 和任何不符合条件的新分支；
7. 在 PR 留言记录实际删除数量、保留分支和最终远端列表。

恢复方式：任一已删除分支可从对应 PR 的 head SHA 或 GitHub PR 页面重建；例如 `git branch <new-name> <head-sha>` 后重新推送。删除不改变 merge commit、PR diff、Issue 评论、CI run、标签或 `main`。

## 5. 验收目标

- 删除前后 `origin/main` SHA 不变；
- 不删除任何开放 PR head、未合并/树不一致分支或非 `codex/*` 分支；
- 32 个历史候选及本审计分支均有明确 PR/merge 恢复点；
- `git fetch --prune` 后不存在已审计的历史远端分支；
- 最终结果记录在 PR 审计轨迹中。
