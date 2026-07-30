# P0-01c：稳定文件身份与快捷方式双重身份

执行日期：2026-07-30
结果：**Pass（仅限当前机器和探针范围）**
关联：P0-01a/P0-01b 桌面发现与 Shell 对账

## 1. 目标

验证 Long Grid 是否能：

1. 为真实桌面文件和目录读取不依赖路径的稳定身份；
2. 在同卷重命名后继续识别同一个文件系统对象；
3. 区分复制产生的新对象；
4. 区分 `.lnk` 快捷方式文件自身与其目标对象；
5. 在报告中不泄露名称、路径、卷序列号或 File ID。

## 2. API 与身份规则

- [`GetFileInformationByHandleEx`](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-getfileinformationbyhandleex) 使用 `FileIdInfo` 获取身份；
- [`FILE_ID_INFO`](https://learn.microsoft.com/windows/win32/api/winbase/ns-winbase-file_id_info) 的 Volume Serial 与 128-bit File ID 组合用于比较同一台计算机上的文件对象；
- 目录使用 `CreateFileW` 的 `FILE_FLAG_BACKUP_SEMANTICS` 打开；
- 使用 `FILE_FLAG_OPEN_REPARSE_POINT`，避免身份读取自动跟随重解析点；
- [`IShellLinkW::GetPath`](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishelllinkw-getpath) 只读取得快捷方式的文件系统目标；
- 不调用 `IShellLink::Resolve`，避免搜索、交互或更新快捷方式。

领域身份表示为：

```text
FileSystemObjectIdentity =
    VolumeSerialNumber + 128-bit FileId
```

路径是可变定位信息，不是长期主键。全零 File ID 按“不支持”处理，必须进入降级策略。相同稳定身份可能表示硬链接别名，不能自动判定为数据错误。

## 3. 安全边界

真实桌面与现有 `.lnk` 始终只读。探针只申请属性读取权限，并允许其他进程继续读、写或删除。

重命名、复制和目录操作只在以下模式的临时沙箱内发生：

```text
%TEMP%\LongGrid-P0-01c\<random-guid>
```

清理前再次规范化并验证路径必须位于固定沙箱根目录之下。探针结束后递归删除本次随机目录；清理失败会导致探针失败并输出不含路径的错误。

## 4. 环境

| 项目 | 值 |
|---|---|
| OS | Microsoft Windows NT `10.0.26200.0` |
| 架构 | x64 |
| .NET SDK | `8.0.419` |
| Target Framework | `net8.0-windows` |
| 探针 | `LongGrid.Spikes.FileIdentity` |

```powershell
dotnet run --project probes/LongGrid.Spikes.FileIdentity `
  --configuration Release -- --json
```

## 5. 实测结果

### 真实桌面，只读

| 指标 | 数量 |
|---|---:|
| 物理桌面项目 | 96 |
| 成功读取稳定身份 | 96 |
| 读取失败 | 0 |
| 唯一稳定身份 | 96 |
| 重复稳定身份 | 0 |

### 快捷方式，只读

| 指标 | 数量 |
|---|---:|
| `.lnk` 文件 | 74 |
| 成功只读加载 | 74 |
| 返回文件系统目标 | 73 |
| 当前存在的目标 | 72 |
| 成功读取目标身份 | 72 |
| 快捷方式/目标身份不同 | 72 |
| 快捷方式/目标身份相同 | 0 |

未返回路径或当前不存在的目标是合法状态，不应阻止 Long Grid 显示快捷方式本身。

### 临时沙箱

| 场景 | 结果 |
|---|---|
| 文件重命名后身份不变 | Pass |
| 目录重命名后身份不变 | Pass |
| 文件复制后身份不同 | Pass |
| 沙箱清理 | Pass |

连续运行得到相同统计与判定，且未残留随机沙箱目录。

## 6. 工程判定

`P0-01c` Pass：

- 当前桌面的文件和目录均可读取 Volume/File ID；
- 同卷重命名可以使用稳定身份继续跟踪；
- 复制必须创建新的领域引用，不能沿用源对象身份；
- `.lnk` 文件自身和目标是两个不同对象，必须分别保存；
- Core 只包含不可变身份值对象，不依赖句柄、COM 或路径；
- 默认输出完全脱敏。

P0-01 桌面发现与基础身份子目标在当前机器完成。生产支持仍需兼容矩阵和降级策略，不能由单机结果推断所有文件系统、网络共享或云提供程序均支持 128-bit File ID。

## 7. 生产模型对齐

建议 `DesktopItemRef` 分层：

```text
domainId: Long Grid 生成的 UUID
location: 当前可变路径或 Shell 定位信息
fileSystemIdentity: 可选 Volume + File ID
shellIdentity: 当前会话/适配器持有的 PIDL
shortcutIdentity:
    linkFileIdentity
    optionalTargetIdentity
```

匹配顺序：

1. 有效的同卷 File ID；
2. Shell 变化事件提供的旧/新身份关系；
3. 规范化路径降级；
4. 名称、大小和时间只能作为候选提示，不能静默合并。

## 8. 后续

1. P0-02：`SHChangeNotifyRegister` + 周期全量对账；
2. 验证创建、删除、批量重命名和事件丢失后的最终一致性；
3. 验证同卷移动、跨卷复制/删除和恢复报告；
4. 在 NTFS、ReFS、exFAT、SMB、OneDrive 重定向和受限账户上复测；
5. 验证重解析点、硬链接、云占位和离线目标的 UI/规则语义；
6. 所有身份和路径进入日志前必须脱敏。
