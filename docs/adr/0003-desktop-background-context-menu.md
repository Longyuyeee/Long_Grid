# ADR-0003：桌面背景“新建方格”使用受支持的 Explorer 命令

- 状态：Accepted
- 日期：2026-08-26
- 决策范围：BOX-R1 / Windows 11 桌面与文件夹背景菜单
- 审计基线：`origin/main@026d6f2f900c8fdef6eb8e24c553f7890823297c`

## 背景与真实差异

原始需求要求用户在 Windows 桌面任意空白处右键，并选择“新建 Long方格盒子”。现有生产代码只能在 Long方格自有 Surface 的“新建”按钮区域收到右键：

- Passive Surface 对普通空白区域返回 `HTTRANSPARENT`，让 Explorer 继续拥有桌面点击；
- 把整个透明 Surface 改成 `HTCLIENT` 会吞掉 Explorer 的选择、框选和菜单，不可接受；
- Explicit Surface 可以处理空白拖画，但必须先进入产品交互模式，不等于 Explorer 桌面右键；
- 现有菜单、快捷键和 UIA 已进入统一创建预览/事务，但没有 Explorer 背景入口。

因此，预期“桌面任意空白处可见菜单”，当前实际“仅产品按钮区域可见菜单”，BOX-R1 尚未完成。

## 决策

Windows 11 正式安装形态采用带应用身份的原生 `IExplorerCommand`：

1. 新增最小 x64 原生 COM DLL，只负责菜单标题、图标、有限状态和把明确创建意图激活到 Long方格；
2. MSIX 清单以 `windows.comServer` 注册 COM 类；
3. 以 `windows.fileExplorerContextMenus` 注册命令，`desktop5:ItemType Type="Directory\Background"`；
4. `Invoke` 不读取目录内容、不执行配置事务，只捕获有限屏幕坐标并通过应用激活参数转交；
5. LongGrid.App 重新读取权威显示拓扑、工作区 revision 与创建准入，随后复用既有创建预览和唯一提交事务；
6. 卸载包必须同时移除菜单注册，不留下注册表、COM 或 Explorer 状态。

依据：

- [Microsoft：为打包桌面应用添加文件资源管理器上下文菜单](https://learn.microsoft.com/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)
- [Microsoft：IApplicationActivationManager::ActivateApplication](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-iapplicationactivationmanager-activateapplication)
- [Microsoft：选择静态或动态快捷菜单方法](https://learn.microsoft.com/windows/win32/shell/shortcut-choose-method)

## 明确禁止

- 不把 Passive Surface 的整个桌面空白区改为 `HTCLIENT`；
- 不使用全局鼠标 Hook、Raw Input、输入模拟或 Explorer 注入；
- 不在应用启动时静默写入 `HKCU\Software\Classes\Directory\Background`；
- 不采用旧 `IContextMenu` 作为 Windows 11 主路径；
- 不在 Explorer 菜单构建回调中读取工作区、枚举文件、访问网络或等待 LongGrid.App；
- 不把“MSIX 清单存在”误报成真实桌面菜单已经可用。

## 交付切片

### BOX-R1-A：有限激活合同

状态：EngineeringComplete / RealProcessPass（2026-08-26）

- 定义唯一命令 `--long-grid-create-box` 与有界屏幕坐标；
- 首进程与已运行单实例都能消费同一激活；
- 无效、重复、越界或陈旧输入有限拒绝；
- 只打开创建预览，不直接保存；
- 真实进程测试验证预览窗口、取消零写入和第二实例重定向。

实际合同为 `--long-grid-create-box=v1,x,y,issuedAtUnixMs,nonce`；Initial、Redirect 与 DuplicateRedirect 三个真实 Release 场景均为 `Difference=None`。这只证明 App 激活边界，不能替代 BOX-R1-B/C 的 Explorer DLL、安装与菜单证据。

### BOX-R1-B：原生 Explorer 命令与清单

状态：ImplementationComplete / NativeDllPass / ExactCommitPackagePending（2026-08-26）

- x64 `IExplorerCommand` DLL；
- COM CLSID、清单 CLSID 和测试常量唯一一致；
- `Directory\Background` 为唯一 ItemType；
- 菜单构建方法有界且无产品 I/O；
- 打包链证明 DLL、资源和清单进入同一 MSIX。

实现采用 CLSID `78A940C1-2E65-4A03-9D09-3AC62CEF30BB`，唯一 AUMID 为 `Longyuyeee.LongGrid.DeveloperPreview!LongGrid.App`。真实 x64 DLL 探针已通过类工厂创建、200 轮标题/图标/状态/CanonicalName/Flags/SubCommands 调用、模块卸载和句柄边界；该结果不等于 Explorer 已加载菜单。精确提交 MSIX 的生成、解包与同包哈希复核完成前，本切片不得改为 Complete。

### BOX-R1-C：真实安装/卸载证据

在可丢弃 Windows 测试账户和受批准签名/测试身份下：

- 安装后桌面空白处出现一次“新建 Long方格盒子”；
- 点击后在右键所在显示器打开创建预览；
- 确认后只产生一次配置提交，取消为零提交；
- Explorer 重启、应用未运行/已运行、多显示器和高 DPI 均验证；
- 卸载后菜单和 COM 注册完全消失。

当前 Developer Preview MSIX 未签名，禁止关闭系统安全策略安装，因此 BOX-R1-C 保持 Pending。

### BOX-R1-D：兼容与恢复

- Windows build、x64/未来 ARM64、Explorer 重启、包升级/降级；
- COM 激活失败不阻塞 Explorer；
- App 关闭/重启、配置恢复态、拓扑变化和重复点击有限处理；
- Windows 10 支持范围单独决策；在决定前不得用静默裸注册表动词冒充正式兼容。

## 测试口径

每个切片必须同时记录：

| 字段 | 要求 |
|---|---|
| 预期 | 菜单/激活/预览/提交/卸载的用户结果 |
| 实际 | 真实进程、真实窗口、真实包或真实 Explorer 观察 |
| 差异 | 未出现、重复、错误显示器、延迟、残留或文件变化 |
| 修正 | 对应代码与重跑结果 |
| 安全 | Explorer 响应、注册残留、配置次数和桌面文件哈希 |

静态源码、Mock COM、清单 XPath 和 DLL 编译只属于自动门禁，不能替代 BOX-R1-C 的真实 Explorer 菜单证据。
