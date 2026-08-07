# Long方格 MSIX 身份与生命周期审计

> 审计日期：2026-08-07
>
> 范围：MSIX 身份、Publisher、版本、品牌资产、能力、结构、确定性、签名隔离与安装生命周期
>
> 结论：未签名 Developer Preview MSIX 的构建与结构验证链已建立；签名和真实安装生命周期仍为 Pending

## 1. 决策边界

正式渠道目标已经批准为 MSIX，但许可证、签名证书、Publisher 正式主体和分发渠道尚未批准。当前不能生成“看起来可安装”的自签名包，更不能要求用户关闭安全策略。本阶段只建立官方工具可复现的未签名结构产物，用来提前验证包身份、版本、主程序、图标、能力和 BlockMap。

包身份固定为开发命名空间：

| 项目 | Developer Preview 合同 |
| --- | --- |
| Identity Name | `Longyuyeee.LongGrid.DeveloperPreview` |
| Publisher | `CN=LongGrid Development` |
| Architecture | `x64` |
| Minimum OS | Windows 11 build 22000 |
| Application Id | `LongGrid.App` |
| EntryPoint | `Windows.FullTrustApplication` |
| Capability | 仅 `runFullTrust` |
| Signature | 无 |
| Installable / distributable | 否 / 否 |

正式证书主体确定后必须使用新的受保护配置替换 Publisher；不得把开发 Publisher 当成商店或企业身份承诺。

## 2. 构建链

`eng/Pack-LongGridMsix.ps1` 执行以下步骤：

1. 要求 Windows、干净 Git 提交、MSIX Manifest 模板、RC1 256 px L+方格母版和 Windows SDK `MakeAppx.exe`；
2. 只复用 `sourceCommit`、版本、RID、.NET self-contained 与 Windows App SDK self-contained 均匹配的便携 ZIP，否则调用正式便携入口重建；
3. 在 `artifacts/.msix-<guid>` 下解压 payload，移除仅属于便携包的说明、前置检查和哈希清单；
4. 从既有 RC1 母版高质量生成 44×44、150×150、50×50 PNG，不修改源品牌资产；
5. 写入固定身份和四段版本的 `AppxManifest.xml`；
6. 用官方 `MakeAppx pack` 对相同 layout 连续生成两份包并比较 SHA-256；
7. 用 `MakeAppx unpack` 解包，要求主程序、三份图标、Manifest、BlockMap、Content Types 和 MSIX 说明存在；
8. 复读身份、Publisher、版本、架构和能力全集，并拒绝意外出现 `AppxSignature.p7x`；
9. 输出 `.msix`、外部 `.sha256` 和不进入包内的构建清单，最后只清理验证过位于 `artifacts/` 下的精确暂存目录。

不把外部构建清单放入 MSIX，避免修改包后产生自引用哈希问题；MSIX 自身由 `AppxBlockMap.xml` 保护包内文件块。

## 3. 签名隔离

仓库和脚本不接受 PFX 密码、不生成开发证书、不搜索用户证书库、不调用 `SignTool`，也不提交 `.pfx/.p12/.cer/.key`。未来签名阶段必须满足：

- 证书和密码只来自受保护 CI/Release 环境；
- 证书 Subject 与最终 Manifest Publisher 精确一致；
- 签名前复读 unsigned MSIX SHA-256，签名后重新验证签名、时间戳和包结构；
- PR CI 只能验证 unsigned 产物，Release job 才能取得签名权限；
- 日志、构建清单和 Actions artifact 名称不得暴露证书秘密。

当前 `signed=false`、`installable=false` 和 `distributionApproved=false` 是强制否定性合同，不是待填默认值。

## 4. 安装、升级、卸载和回滚

`eng/Test-LongGridMsixLifecycle.ps1 -ValidateOnly` 只复核源码合同，固定返回：

- 不启动进程；
- 不调用 `Add-AppxPackage` / `Remove-AppxPackage`；
- 不信任未签名包；
- live 证据为 `PendingSignedPackageAndDisposableWindowsProfile`。

真实矩阵必须在可抛弃 Windows 11 x64 Profile/VM 上使用受保护签名包完成：

| 场景 | 必须验证 |
| --- | --- |
| Clean install | 身份、开始菜单图标、首次启动、普通用户权限、配置目录 |
| Upgrade | 旧版→新版，配置保留、单实例、运行中升级行为 |
| Failed upgrade | 新版损坏/签名错误/版本冲突不破坏旧版 |
| Downgrade | 默认阻断或显式受控路径，不能静默覆盖新 schema |
| Uninstall | 进程/注册/快捷方式移除；用户数据保留策略明确告知 |
| Rollback | 安装失败后包版本与用户配置都回到可判定状态 |
| Multi-user | 每用户身份、数据隔离、另一个用户运行时升级/卸载 |
| Enterprise offline | 证书链、依赖、策略和无商店环境安装 |

未签名包不进入该矩阵；不得通过关闭签名检查来制造 Pass。

## 5. 需求对齐

| 初始需求 | 本切片对齐 | 当前边界 |
| --- | --- | --- |
| 一键打包 | 便携 ZIP 之上新增一键 MSIX | 仍是 unsigned Developer Preview |
| L+方格品牌 | 同一 RC1 母版生成精确 MSIX 尺寸 | 最终 ICO/商店素材未批准 |
| 现代 UI | 打包完整现有 WinUI self-contained payload | 不代表视觉/可用性实机矩阵通过 |
| 安全桌面管理 | 包仅声明必要 `runFullTrust` | App 继续零接线，不开放真实窗口执行 |
| 自动更新 | 已固定四段版本和生命周期矩阵 | AppInstaller/商店更新尚未实现 |

## 6. 后续方向

下一切片不应继续增加未签名包格式，而应建立受保护签名/SBOM 的流水线合同和 Release 权限边界。只有取得合规证书、许可证批准与可抛弃测试环境后，才执行真实 install/upgrade/uninstall/rollback；完成这些证据及 #19/#20/#23/#24 外部矩阵之前，项目仍不能宣称 RC 收口或公开分发。
