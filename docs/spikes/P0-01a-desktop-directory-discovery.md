# P0-01a：物理桌面目录发现

执行日期：2026-07-30
结果：Pass（仅限本探针范围）
关联总探针：P0-01 桌面目录与 Shell Namespace 发现

## 1. 假设

在当前 Windows 用户会话中，可以使用 .NET Known Folder 映射只读发现：

- 当前用户 Desktop Directory；
- Public Desktop；
- 顶层文件、目录、`.lnk` 和 `.url`；
- 不输出完整路径和名称的脱敏统计。

探针不得打开、移动、复制、重命名或删除桌面项目。

## 2. 非目标

- Shell Desktop Namespace 虚拟项；
- PIDL/`IShellItem`；
- NTFS Volume/File ID；
- `.lnk` 目标解析；
- OneDrive 占位状态；
- Shell 图标和缩略图；
- `SHChangeNotifyRegister`；
- 文件操作。

这些能力不能从本报告推断为已经可用。

## 3. 环境

| 项目 | 值 |
|---|---|
| OS | Microsoft Windows NT `10.0.26200.0` |
| 架构 | x64 |
| .NET SDK | `8.0.419` |
| Target Framework | `net8.0` |
| 探针 | `LongGrid.Spikes.DesktopCatalog` |
| 隐私模式 | 默认，不输出名称和完整路径 |

这是单机结果，不能据此确定最低 Windows 版本或完整支持矩阵。

## 4. 实现

```text
Environment.SpecialFolder.DesktopDirectory
Environment.SpecialFolder.CommonDesktopDirectory
    → 顶层只读枚举
    → 规范化绝对路径
    → 大小写不敏感去重
    → File / Directory / Shortcut / InternetShortcut 分类
    → 脱敏 JSON/文本报告
```

运行：

```powershell
dotnet run --project probes/LongGrid.Spikes.DesktopCatalog `
  --configuration Release -- --json
```

只有明确传入 `--include-names` 才会输出显示名；仍不输出完整路径。

## 5. 结果

| 来源 | 存在 | 项目数 | 错误 |
|---|---:|---:|---|
| user-desktop | 是 | 61 | 无 |
| public-desktop | 是 | 35 | 无 |

唯一项目总数：96

| 类型 | 数量 |
|---|---:|
| File | 13 |
| Directory | 4 |
| Shortcut | 74 |
| InternetShortcut | 5 |

连续运行两次得到相同的 96 项，满足本轮短时重复性检查。

## 6. 工程门禁

- Release build：通过，0 warnings / 0 errors；
- xUnit：6/6 通过；
- `dotnet format --verify-no-changes`：通过；
- 直接与传递依赖漏洞扫描：通过；
- 报告默认不含项目名称或完整路径。

模板生成的旧测试依赖曾解析到两个高危传递组件，已升级为：

- `Microsoft.NET.Test.Sdk 18.8.1`
- `xunit 2.9.3`
- `xunit.runner.visualstudio 3.1.5`
- `coverlet.collector 10.0.1`

升级后漏洞扫描为零。

## 7. 判定

`P0-01a` Pass：

- 两个物理桌面 Known Folder 均可解析；
- 顶层只读枚举成功；
- 类型分类、路径去重和隐私默认值可工作；
- 当前数据未触发访问异常。

`P0-01` 总探针仍然 Open，因为本结果不包含 Explorer 展示的完整 Shell Namespace。

## 8. 后续

1. `P0-01b`：用 `IShellFolder`/`IShellItem2` 枚举 Shell Desktop Namespace。
2. 比较 Shell 视图、用户目录和 Public Desktop 的项目差异。
3. 为文件系统项读取稳定 File ID，验证重命名识别。
4. 解析 `.lnk` 自身与目标的双重身份。
5. `P0-02`：实现 `SHChangeNotifyRegister` + 全量对账。
6. 在 Windows 10、Windows 11 Stable、OneDrive 重定向和权限受限账户重复测试。
