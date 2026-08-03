# Long方格应用图标 RC1

状态：**Release Candidate 1 / 仅供内部开发与评审**

批准依据：2026-08-03，产品负责人在图标概念 PR #64 通过后指示继续开发。本批准仅表示 A“结构化 L”造型与候选主色 `#5B5FF5` 可以进入生产校正，不等同于最终商标、法律检索或发布批准。

## 目录

- `source/`：浅色彩色、深色彩色、深/浅单色字形和静态高对比回退母版；
- `sizes/svg/`：16、20、24、32、48、64、128、256 px 原生尺寸校正版；
- `sizes/png/`：从对应原生 SVG 确定性渲染的同尺寸 PNG；
- `longfangge-rc1-preview.svg/.png`：主题和尺寸评审板。

## 使用边界

- 开发分支可以用 RC1 验证 UI Shell、任务栏、开始菜单和快捷方式；
- 不得把 RC1 宣称为最终商标或用于公开商店宣传；
- 高对比 SVG 是系统强制颜色不可用时的静态回退，不替代运行时的 Windows 高对比/Forced Colors 适配；
- 单色版本为保持最小尺寸辨识，保留核心 L，主动省略次级桌面格子；
- ICO、MSIX、StoreLogo、SquareLogo 和 SplashScreen 将在正式应用/打包工程建立后由受审计脚本生成，避免现在固化错误的安全区和平台缩放规则。

审计记录见 [`docs/16-brand-asset-production-audit.md`](../../../docs/16-brand-asset-production-audit.md)。
