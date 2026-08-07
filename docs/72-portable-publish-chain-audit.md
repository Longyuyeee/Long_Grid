# Long方格便携发布链审计

> 审计日期：2026-08-07
>
> 范围：一键质量门禁、self-contained publish、确定性 ZIP、哈希清单、安装前置检查与发布边界
>
> 结论：内部 Developer Preview 便携交付链已建立；MSIX、签名、许可证、SBOM 和正式分发仍未就绪

## 1. 原始缺口与决策

仓库已有一键开发启动入口，但路线图承诺的 `eng/Pack-LongGrid.ps1` 不存在。`LongGrid.App` 是 `WindowsPackageType=None` 的 unpackaged WinUI 3 应用，并依赖框架安装；直接复制普通 build 目录不能证明目标机器可启动，也没有来源提交、逐文件哈希、压缩包哈希或安装前置检查。

本阶段不建立伪 MSIX。许可证选择已经由负责人延期，代码签名证书、包身份和发布渠道也未批准，因此唯一允许的产物是内部、未签名、不可公开分发的 Windows 11 x64 Developer Preview 便携 ZIP。它用于验证交付机械链，不代表产品达到 Beta 或 Stable。

## 2. 一键入口与质量门禁

`eng/Pack-LongGrid.ps1` 默认执行：

1. 拒绝非 Windows、缺失 SDK、缺失输入和非干净 Git 工作树；
2. 锁定依赖恢复、格式检查、Release build/test、90%/75% 覆盖率门槛和依赖漏洞检查；
3. 使用 `dotnet publish` 生成 `win-x64`、.NET self-contained、Windows App SDK self-contained 的 Release 目录；
4. 生成不可变 `artifact-manifest.json` 和逐文件 `SHA256SUMS.txt`；
5. 以规范路径顺序和固定 UTC 时间戳写入 ZIP；
6. 对相同目录连续生成两份 ZIP 并比较 SHA-256，拒绝不确定输出；
7. 复核 ZIP 根目录、禁止绝对/反斜杠/`..` 路径，并要求关键文件存在；
8. 输出 ZIP 的外部 `.sha256` sidecar 和有限 JSON 结果。

`-SkipQualityGates` 仅供已经执行完整质量门禁的 CI 阶段复用；默认开发入口不会跳过门禁。`-ValidateOnly` 只复核源码合同，不产生文件、不启动 GUI。

普通 solution restore 继续严格复读仓库 lock 文件。self-contained publish 额外需要 SDK 的 `win-x64` runtime graph；该次 restore 使用 `artifacts/` 下的隔离、非持久 lock 路径，禁止改写平台无关 Core/Infrastructure 的仓库 lock 文件。CI 由前置 locked restore 固定 NuGet 依赖，再由隔离 restore 补齐 SDK runtime pack。

脚本只在仓库 `artifacts/` 下创建 GUID 暂存目录，并在 `finally` 中验证它仍是该目录的子路径后清理；不会删除用户指定路径。最终 ZIP 和 sidecar 位于固定的 `artifacts/LongGrid-<version>-win-x64.*`，该目录已被 Git 忽略。

## 3. 构建清单的否定性合同

包内清单固定记录：

| 字段 | 当前值 | 含义 |
| --- | --- | --- |
| `packageType` | `portable-unpacked-zip` | 不是 MSIX/安装器 |
| `channel` | `DeveloperPreview` | 仅内部开发验证 |
| `signed` / `installer` | `false` | 无签名、无安装生命周期 |
| `distributionApproved` | `false` | 不得公开发布 |
| `licenseStatus` | `Deferred` | 许可证仍是正式分发阻断项 |
| `desktopHostExecutionEnabled` | `false` | 不开放真实窗口执行路径 |

这些字段既方便后续自动门禁复读，也避免把“能够打包”错误描述成“能够发布”。

## 4. 安装前置检查

压缩包内的 `Install-Preflight.ps1` 在启动前执行以下只读检查：

- 当前系统为 Windows 11 build 22000+ 且操作系统为 64 位；
- 主程序、构建清单和哈希清单存在；
- 清单明确描述 win-x64 与两层 self-contained；
- `SHA256SUMS.txt` 每一项格式合法、路径不越过解压根目录、文件存在且 SHA-256 匹配；
- 最终只返回版本、系统 build、已验证文件数、签名/安装器状态和启动命令，不收集或上传机器信息。

它不提权、不写注册表、不安装服务/驱动、不修改 Explorer，也不会为缺失许可证或签名背书。

## 5. 需求对齐

| 初始需求 | 本切片对齐 | 保持的边界 |
| --- | --- | --- |
| 一键打包 | 一个命令执行质量门禁并生成可核验 ZIP | 不是正式安装器 |
| 一键启动 | 包内给出前置检查后的精确 EXE | 不建立开机启动或 Shell 集成 |
| 现代 UI 与 L+格子品牌 | 强制包含现有 `Assets/LongFangGe.png` | 最终 ICO/MSIX/商店资产仍待审计 |
| 桌面整理/自定义窗口 | 交付现有只读安全 UI | App 继续零接线，不移动真实文件或窗口 |
| 任务栏、小组件与插件 | 没有扩大包能力 | 继续属于 MVP 后续阶段 |

## 6. CI 与剩余风险

PR/main CI 在既有完整测试、覆盖率和漏洞门禁之后，以 `-SkipQualityGates -NoRestore` 真实执行 publish、两次确定性压缩和结构复核；产物不上传到 Actions，也不创建 GitHub Release。

下一工程切片应审计 MSIX 工程与包身份，明确开发证书/正式证书隔离、版本升级/降级、卸载残留、开始菜单/快捷方式、多用户和回滚矩阵。在许可证、签名、SBOM、受支持机器矩阵、#19/#20/#23/#24 外部证据和正式渠道批准完成前，项目仍不能公开发布或宣称 RC 完成。
