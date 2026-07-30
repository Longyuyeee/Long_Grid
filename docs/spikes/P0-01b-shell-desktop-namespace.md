# P0-01b：Shell Desktop Namespace 枚举与对账

执行日期：2026-07-30
结果：**Pass（仅限当前探针范围）**
关联：P0-01a 物理桌面目录发现

## 1. 目标与依据

目标是验证 Long Grid 能否使用公开的 Windows Shell API，只读枚举 Explorer 的 Desktop Namespace，并与用户桌面和 Public Desktop 的物理目录结果对账。

实现依据：

- [`SHGetDesktopFolder`](https://learn.microsoft.com/windows/win32/api/shlobj_core/nf-shlobj_core-shgetdesktopfolder) 获取 Shell Namespace 根节点的 `IShellFolder`；
- [`IShellFolder::EnumObjects`](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-enumobjects) 枚举相对 PIDL；
- [`IShellFolder::GetAttributesOf`](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-getattributesof) 读取 `SFGAO_FILESYSTEM`、`SFGAO_FOLDER`、`SFGAO_LINK` 和 `SFGAO_HIDDEN`；
- [`SHGetNameFromIDList`](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-shgetnamefromidlist) 获取显示名称或文件系统解析路径；
- [`SHCONTF`](https://learn.microsoft.com/windows/win32/api/shobjidl_core/ne-shobjidl_core-_shcontf) 请求文件夹、非文件夹、隐藏和系统隐藏项目。

Shell COM/PIDL 仅存在于探针适配层；Core 只接收规范化后的字符串身份并执行可单测的集合对账。

## 2. 安全与隐私边界

- 探针只执行枚举、属性读取和名称解析；
- 不调用 Shell 动词、`IFileOperation`、进程注入或未文档化 Explorer 接口；
- 不打开、启动、复制、移动、重命名或删除项目；
- 默认 JSON/文本报告不包含显示名称和完整路径；
- 只有显式传入 `--include-names` 才显示名称，完整路径始终不输出；
- COM 对象、PIDL 和 Shell 分配的字符串均在使用后释放。

## 3. 环境与运行方式

| 项目 | 值 |
|---|---|
| OS | Microsoft Windows NT `10.0.26200.0` |
| 架构 | x64 |
| .NET SDK | `8.0.419` |
| Target Framework | `net8.0-windows` |
| 探针 | `LongGrid.Spikes.ShellDesktopCatalog` |

```powershell
dotnet run --project probes/LongGrid.Spikes.ShellDesktopCatalog `
  --configuration Release -- --json
```

## 4. 实测结果

| 指标 | 数量 |
|---|---:|
| Shell 总项目 | 116 |
| Shell 文件系统项目 | 105 |
| Shell 纯虚拟项目 | 11 |
| Shell 文件夹属性项目 | 23 |
| Shell 链接属性项目 | 79 |
| 用户/Public 物理目录唯一项目 | 96 |
| 物理项目与 Shell 匹配 | 96 |
| 仅物理目录存在 | 0 |
| 仅 Shell 文件系统存在 | 9 |

连续执行三次得到相同计数。物理目录的 96 项全部进入 Shell 结果，说明当前机器上 Desktop Namespace 是两个物理桌面目录的超集；额外 9 个文件系统项目和 11 个纯虚拟项目证明仅扫描目录不足以复现 Explorer 的完整桌面视图。

这些额外项目的存在是预期的命名空间差异，不应自动当作重复文件、异常文件或可移动对象。

## 5. 工程验证

- Release build：0 warnings / 0 errors；
- xUnit：8/8 通过；
- 集合对账覆盖路径规范化、大小写不敏感、去重和非法输入；
- 探针默认输出经过脱敏；
- 未引入新的 NuGet 运行时依赖。

## 6. 判定

`P0-01b` Pass：

- 公开 Shell API 能稳定返回桌面命名空间项目；
- 文件系统项目与物理目录可以对账；
- 虚拟项目可以被明确区分；
- 当前观察中没有物理项目被 Shell 枚举遗漏；
- 连续运行计数一致；
- Core 未依赖 COM、PIDL 或 Windows API。

P0-01 目录/命名空间“发现”子目标完成，但桌面项目身份体系仍是 **Conditional**，尚不能仅凭路径承诺重命名后的稳定跟踪。

## 7. 后续

1. P0-01c：为 NTFS/ReFS 文件系统项目读取并验证 Volume/File ID；
2. 分离“Shell 项目 PIDL 身份”和“文件系统稳定身份”，不把显示名称或路径当主键；
3. 验证 `.lnk` 自身身份与目标身份，禁止整理时误移动链接目标；
4. P0-02：使用 `SHChangeNotifyRegister` 接收变化，并以周期全量对账保证最终一致；
5. 在 Windows 10、Windows 11 Stable、OneDrive 重定向、ARM64 和受限账户上复测；
6. 对 Explorer 重启、Shell 扩展异常和慢网络 Namespace 建立超时/重连策略。
