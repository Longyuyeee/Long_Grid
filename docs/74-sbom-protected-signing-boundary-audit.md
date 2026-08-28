# Long方格 SBOM 与受保护签名边界审计

> 审计日期：2026-08-07
>
> 范围：unsigned MSIX 内容清单、SPDX 生成与复核、工具锁定、产物绑定、签名权限、证书与 Publisher、时间戳和分发阻断
>
> 结论：**unsigned MSIX 的 SPDX 2.2 SBOM 生成/官方验证链和签名隔离合同已建立；真实签名、安装与分发仍为 Pending/Blocked**

## 1. 决策与依据

本阶段不创建证书、不读取用户证书库、不调用 `SignTool`、不安装包，也不授予 PR/main CI 任何签名权限。原因不是省略发布工作，而是当前缺少正式 Publisher 主体、合规代码签名证书、受保护 Release environment、许可证批准和可抛弃 Windows 生命周期环境；在这些输入缺失时生成自签名包会混淆“结构验证”和“可分发身份”。

采用 Microsoft 官方 SBOM Tool `4.1.5`，由仓库 `.config/dotnet-tools.json` 固定版本。工具扫描最终 MSIX 解包布局和 `src/` 产品源码依赖，生成 SPDX 2.2，再由同一官方工具复核布局哈希；测试与探针工程不作为产品 build-components 输入。官方使用说明见 [microsoft/sbom-tool](https://github.com/microsoft/sbom-tool)。

Microsoft 的 MSIX 签名规则要求证书 Subject 与包 Manifest 的 Publisher 精确一致；代码签名证书还必须具备有效私钥与代码签名 EKU/数字签名用途。`SignTool` 的签名和 RFC 3161 时间戳必须显式指定摘要算法。依据：[Sign an app package using SignTool](https://learn.microsoft.com/windows/msix/package/sign-app-package-using-signtool)、[How to sign an app package](https://learn.microsoft.com/windows/win32/appxpkg/how-to-sign-a-package-using-signtool)、[SignTool reference](https://learn.microsoft.com/windows/win32/seccrypto/signtool)。

GitHub 建议默认令牌只读，并用 environment reviewer 隔离发布秘密；若未来使用云密钥提供方，应只在受保护 Release job 授予 `id-token: write`。该权限只允许取得 OIDC token，不应出现在 PR/main 构建。依据：[Secure use reference](https://docs.github.com/actions/reference/security/secure-use)、[OIDC reference](https://docs.github.com/actions/reference/security/oidc)。

## 2. SBOM 可执行链

`eng/New-LongGridSbom.ps1` 的实际模式要求 Windows、干净 Git 提交、Windows SDK `MakeAppx.exe` 和仓库固定的 SBOM 工具，然后：

1. 复用或重建与当前提交、版本和 SHA-256 完全绑定的 unsigned MSIX；
2. 再次拒绝 `signed=true` 或 `distributionApproved=true` 的输入；
3. 在 `artifacts/.sbom-<guid>` 下用 `MakeAppx unpack` 得到最终交付布局，并拒绝 `AppxSignature.p7x`；
4. 以 MSIX 解包目录为 drop、`src/` 为 build-components，生成 `SPDX:2.2`；supplier 参数传入组织名，由工具按 SPDX 规范写为 `Organization: Longyuyeee`；
5. 使用官方 `validate` 命令重新计算布局并输出验证结果；
6. 复读 `spdxVersion`、`dataLicense`、namespace、产品名称/版本/supplier，并要求清单至少包含 `AppxManifest.xml` 与 `LongGrid.App.exe`；
7. 输出外部 `.spdx.json`、`.sha256` 和 `sbom-evidence.json`，后者把 SBOM SHA-256、MSIX SHA-256、源码提交和工具版本绑定；
8. 只清理已验证位于 `artifacts/` 下的精确暂存目录。

SBOM 描述的是最终 MSIX 内容，不写回 MSIX，避免让包哈希失效或产生自引用。SPDX document namespace 含生成标识，创建时间也会变化，所以当前声明是“内容经官方工具验证并与该次 MSIX SHA-256 绑定”，不是 SBOM 文件逐字节可重复。

`-ValidateOnly` 只复核工具版本、格式和否定性状态，不构建、不安装、不访问证书或网络密钥。CI 会先恢复固定工具，再真实生成和验证 SBOM，但仍不上传为 Release。

## 3. 签名隔离合同

`packaging/release/signing-contract.json` 是机器可读的停止条件，`eng/Test-LongGridReleaseSigning.ps1 -ValidateOnly` 强制检查：

- Developer Preview 身份仍为 `Longyuyeee.LongGrid.DeveloperPreview` / `CN=LongGrid Development`，且禁止公开分发；
- PR 和 main build 都不能访问签名能力；当前 CI 顶层权限只能是 `contents: read`；
- CI 不能引用 secrets、OIDC write、`SignTool`、自签名证书或 AppX 安装/删除命令；
- 正式签名必须位于需要 reviewer 的 `long-grid-release` 受保护环境；
- 私钥文件与自签名证书路径均禁止，未来只允许 OIDC 或托管密钥提供方；
- 正式证书 Subject 必须精确匹配最终 Publisher，并具备代码签名 EKU；
- 文件摘要与 RFC 3161 时间戳摘要固定为 SHA-256；
- 签名前复核 unsigned hash，签后生成新 hash 并验证签名与时间戳；
- SBOM、许可证与 signed lifecycle matrix 都是分发前置条件。

脚本的非 `-ValidateOnly` 模式会直接失败。这个显式失败是安全合同：尚未批准的输入不能靠增加参数绕过。

## 4. CI 与供应链对齐

> 2026-08-28 Stage 221 更新：CodeQL 已作为独立双语言 matrix workflow 接入，C# 和原生 C++ 都使用 manual build，权限仅为 `contents: read / security-events: write`；首轮 CodeQL 2.26.4 查询结果分别为 52 rules / 0 results 与 58 rules / 0 results。它不读取发布 environment 或 secrets，也不改变下面的签名隔离与分发阻断。

> 2026-08-28 Stage 222 更新：CI/CodeQL 的 7 个远程 Action 调用全部固定到官方 major ref 当日解析出的完整执行 commit；`.github/actions-pins.json` 与双负向合同拒绝标签、未知/漂移 SHA 和消费者变化。该门禁没有新增权限，也不自动信任未来上游更新。

> 2026-08-28 Stage 223 更新：Dependabot 仅为 `github-actions` 每周创建最多 2 个更新 PR，目标为 `main`，无 registry/secret/auto-merge。新 SHA 在人工同步 pin 清单前按预期失败，不能自行取得发布或签名权限。

> 2026-08-28 Stage 224 更新：checkout/upload-artifact v7.0.1 经官方签名提交与运行时边界审查后才同步 pin 清单；CI/CodeQL 权限、发布环境、secret、OIDC、签名和分发状态均不变。

PR/main CI 当前顺序为：

1. 用完整 SHA 加载 checkout，并立即验证所有 workflow 的 Action pin 清单与消费者范围；
2. 恢复固定的仓库工具和锁定的产品依赖；
3. 执行既有 format/build/test/coverage/Windows 探针；
4. 验证 MSIX 生命周期仍 Pending；
5. 验证签名权限边界仍 Blocked；
6. 真实构建并验证 unsigned MSIX；
7. 真实生成并验证 SPDX 2.2 SBOM；
8. 只用固定 SHA 的 upload-artifact 上传 TRX/Cobertura，不上传未批准的安装产物。

SBOM Tool 是构建工具依赖，不进入 Long方格运行时 payload；其版本变化必须单独审计生成格式、组件检测差异、许可证和 CI 结果。

## 5. 原始需求对齐

| 需求 | 本切片结果 | 仍未完成 |
| --- | --- | --- |
| 一键打包 | MSIX 之后新增单命令 SBOM 生成与验证 | 尚无正式签名的一键 Release |
| 安全可信 | MSIX/SBOM/源码提交三者哈希绑定；CI 明确拒绝密钥和安装 | 证书、时间戳和实机信任链 |
| 现代 UI 与品牌 | SBOM 清点实际 WinUI/MSIX payload，不改变 L+方格资产 | 商店素材和最终商标批准 |
| 桌面整理可靠性 | 供应链只描述当前零真实文件/零 DesktopHost 接线产物 | 真实桌面文件与窗口执行仍未开放 |
| 可公开交付 | 明确提供可审计的前置机械链 | 许可证、签名、生命周期和外部矩阵仍阻断 |

## 6. 后续准入顺序

1. 项目负责人批准正式 Publisher、许可证与分发渠道；
2. 选择合规代码签名证书和不导出私钥的托管签名方式；
3. 配置 `long-grid-release` environment、reviewer 与最小 OIDC/服务权限；
4. 新增只由已批准 tag/manual release 触发的签名 job，复核 unsigned hash、Publisher/Subject、签名、RFC 3161 时间戳和 signed hash；
5. 在可抛弃 Windows 11 x64 profile/VM 执行 clean install、upgrade、failed upgrade、downgrade、uninstall、rollback、multi-user 和 enterprise offline 矩阵；
6. 所有证据通过后才允许上传 Release 或声明可安装/可分发。

在上述条件和 #19/#20/#23/#24 外部证据关闭前，当前阶段仍是内部 Developer Preview 交付机械链，不是 RC 完成。
