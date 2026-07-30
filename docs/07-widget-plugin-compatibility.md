# 小组件与 Long助手插件兼容设计

审计日期：2026-07-30
审计对象：[`Longyuyeee/Long_BetterWindows`](https://github.com/Longyuyeee/Long_BetterWindows)
审计提交：`0d1366f`（本次本地读取的 `master` 基线）
状态：Proposal

正式开发契约见：[Long 插件小组件兼容协议（LPWP）1.0](protocol/LONG_WIDGET_PROTOCOL_V1.md)。本文保留产品与架构层面的审计结论，字段、Bridge、IPC、安全和一致性测试以正式协议为准。

## 1. 结论

**Long Grid 可以兼容 Long助手插件，但不能把“加载插件”和“把插件变成桌面小组件”视为同一件事。**

推荐建立三级兼容：

1. **动作卡兼容**：现有命令插件无需修改，可在桌面显示为按钮、菜单或最近结果卡。
2. **原生 Widget 兼容**：Web 插件显式声明 Widget Surface 后，可嵌入 Long Grid 桌面容器。
3. **高信任桥接**：DLL、C# Script、Hybrid 插件通过独立进程/IPC 提供数据或打开原窗口，不直接进入 Long Grid 核心进程。

这样可以保留 Long助手现有生态，又不会让 Long Grid 被 WPF、任意 DLL 或常驻 WebView 的资源成本锁死。

## 2. Long助手当前插件基线

仓库已经具备较成熟的插件基础：

- `.NET DLL`、C# Script、HTML/JS WebView2 三类运行时。
- `.lpak` 确定性打包、逐文件 SHA-256 总账和生产验证。
- Manifest JSON Schema、最低 API/宿主/UI Kit 版本。
- 能力声明、依赖、默认设置、命令、窗口、生命周期和本地化。
- 插件签名、市场安装/升级/回滚设计。
- Web 插件的 `long.*` Bridge、TypeScript SDK 与 Mock Host。
- Web 页面只允许插件目录内顶层导航；新窗口和浏览器下载会被阻止。
- `Loaded → Running → Background → Stopped/Error` 生命周期。

参考：

- [Manifest Schema](https://github.com/Longyuyeee/Long_BetterWindows/blob/master/schemas/plugin-manifest.schema.json)
- [插件开发指南](https://github.com/Longyuyeee/Long_BetterWindows/blob/master/docs/%E6%8F%92%E4%BB%B6%E5%BC%80%E5%8F%91%E6%8C%87%E5%8D%97.md)
- [.NET Plugin SDK](https://github.com/Longyuyeee/Long_BetterWindows/tree/master/src/LongBetterWindows.PluginSdk)
- [Web Plugin SDK](https://github.com/Longyuyeee/Long_BetterWindows/tree/master/sdk/web)

## 3. 当前还不能直接成为小组件的原因

### 3.1 Manifest 没有 Widget Surface

当前 Schema 使用 `additionalProperties: false`，只认识命令、窗口、生命周期等字段，没有：

- 小组件 ID 与多实例策略。
- 网格尺寸、最小/最大尺寸和响应式断点。
- 可见、隐藏、暂停、恢复和刷新策略。
- 透明背景、指针穿透和桌面层级。
- 小组件设置 Schema 与实例状态。

现有 `window.mode: overlay` 和 `preferred_width/height` 描述的是插件窗口，不足以表达桌面小组件。

### 3.2 `ui.floating_box` 是独立窗口，不是 Widget Host

Long助手当前 `ui.floating_box` 最终调用 WPF `Window + WebView2` 创建独立窗口。它没有接入 Long Grid 的容器网格、布局快照、桌面 Z-order、统一拖动、锁定或隐藏生命周期。

因此这个能力可以作为早期原型参考，不能直接当作小组件协议。

### 3.3 UI 框架不同

- Long助手宿主当前是 WPF/.NET 8。
- Long Grid 当前提议使用 WinUI 3/Windows App SDK。
- Long助手原生 UI Kit 返回 WPF 控件/窗口语义。

WPF 控件不能直接放入 WinUI 3 可视树。使用 XAML Island 或 HWND 嵌套会增加焦点、DPI、主题和辅助功能问题，不适合作为通用插件合同。

### 3.4 原生与脚本插件是完全信任

Long助手文档已明确：

- Web 插件受 Bridge 能力声明约束。
- DLL 和 C# Script 可以直接使用 .NET、P/Invoke 和文件系统。
- Manifest 权限对高信任插件是意图声明，不是操作系统沙盒。

若 Long Grid 在桌面核心进程中加载第三方 DLL，一个插件崩溃、死循环或泄漏即可拖垮整个桌面宿主。

### 3.5 小组件是长期驻留负载

普通插件通常按需打开；小组件可能全天显示。还需要解决：

- 定时器和动画在不可见时暂停。
- 多个 WebView2 的进程和内存成本。
- 网络刷新频率、离线和电池模式。
- Explorer 重启、睡眠和显示器切换后的恢复。
- 每实例设置和共享插件设置的边界。

## 4. 运行时兼容等级

| Long助手插件类型 | Long Grid 兼容方式 | 能否直接成为 Widget | 建议等级 |
|---|---|---:|---|
| 纯命令插件 | 自动生成动作卡，调用命令并显示结果 | 部分 | A |
| 本地 Web 插件 | 新增 Widget Surface，嵌入 WebView2 | 是 | A |
| Web + 原生后台 Hybrid | Web Surface + 进程外后台 | 条件可行 | B |
| C# Script | 命令卡或数据 Provider | 不能直接承载 UI | B |
| 原生 DLL 无 UI | IPC 数据 Provider/动作卡 | 不能直接承载 UI | B |
| 原生 WPF UI | 启动卡，打开 Long助手原窗口 | 否 | C |
| 高风险输入/窗口/文件插件 | 显式确认后调用，不常驻嵌入 | 否 | C |

### 自动动作卡

这是最低成本兼容方式。Long Grid 读取现有 `commands`：

- 无参数命令：生成单击按钮。
- 有参数 Schema：生成紧凑表单或固定预设按钮。
- 有输出声明：显示最后结果、复制按钮或状态摘要。
- 高风险能力：执行前跳转到审查对话框。

它不要求旧插件修改 Manifest，也不会伪装成完整 UI 小组件。

## 5. 建议的共享平台边界

不要让 Long Grid 引用 `LongBetterWindows.Host`。建议从现有 SDK 中抽出或扩展一个宿主中立层：

```text
Long.Plugin.Contracts
├─ Manifest DTO + JSON Schema
├─ Capability identifiers
├─ Command / Widget contracts
├─ Lifecycle events
├─ Package identity and compatibility
└─ IPC messages

Long.Plugin.Packaging
├─ .lpak validator
├─ package-files.json verification
├─ signature verification
└─ install/update transaction

LongAssistant.HostAdapter
└─ WPF command/window/workflow surfaces

LongGrid.HostAdapter
└─ WinUI desktop widget/action-card surfaces
```

Long助手保持现有插件兼容；Long Grid 只实现自己支持的 Surface 和能力。插件声明某项能力不代表每个宿主都必须提供，宿主应在安装时展示“不支持/降级运行”。

## 6. Widget Manifest 草案

建议在现有 Manifest 中新增可选 `widgets`。旧插件不声明时继续正常运行。

```json
{
  "id": "com.long.hardware",
  "version": "2.0.0",
  "name": "硬件监控",
  "runtime": "webview",
  "entry_point": "index.html",
  "capabilities": ["system.performance", "storage.local"],
  "commands": [],
  "widgets": [
    {
      "id": "performance-card",
      "title": "系统状态",
      "entry_point": "widgets/performance.html",
      "multiple_instances": false,
      "default_size": {"columns": 2, "rows": 2},
      "min_size": {"columns": 1, "rows": 1},
      "max_size": {"columns": 4, "rows": 4},
      "refresh": {
        "mode": "event",
        "minimum_interval_seconds": 5,
        "when_hidden": "suspend"
      },
      "appearance": {
        "transparent": true,
        "host_chrome": true
      }
    }
  ]
}
```

需要由 Schema 精确定义：

- `id`：插件内稳定唯一。
- `entry_point`：只能位于插件目录。
- `multiple_instances`：是否允许同一 Widget 创建多个实例。
- `default/min/max_size`：使用 Long Grid 网格单位，不使用屏幕像素。
- `refresh.mode`：`event | interval | manual`。
- `when_hidden`：`suspend | throttle | continue`，默认 `suspend`。
- `transparent`：页面是否请求透明背景，最终由宿主决定。
- `host_chrome`：标题、拖动、设置、刷新、错误状态是否由宿主绘制。

不要在第一版开放任意“始终置顶”“穿透点击”或自定义 Z-order；这些应由用户和宿主控制。

## 7. Widget 生命周期

现有插件生命周期保留，在其上增加实例级 Surface 生命周期：

```text
Discover → Install → Load Plugin
                       │
             Create Widget Instance
                       │
        Mount → Visible ↔ Hidden/Throttled
          │         │
        Resize    Suspend/Resume
          │         │
          └────── Unmount → Dispose
```

建议事件：

- `long.widget-mounted`
- `long.widget-visibility-changed`
- `long.widget-resized`
- `long.widget-theme-changed`
- `long.widget-settings-changed`
- `long.widget-suspend`
- `long.widget-resume`
- `long.widget-unmount`

`Unmount` 和 `Dispose` 必须可重复调用；任何计时器、事件、网络请求和原生句柄都必须释放。

## 8. Web Widget 宿主安全要求

Long助手现有 WebView2 Bridge 和能力检查值得复用，但桌面常驻场景还需加强：

1. 使用每插件隔离的虚拟主机映射或资源请求拦截，不只检查顶层 `file://` 导航。
2. 默认 CSP 禁止外部脚本、任意 `eval` 和未声明网络。
3. 外部网络只能通过 `network.http` 能力或明确白名单。
4. 阻止新窗口、下载和任意协议导航；外部链接交给宿主确认后打开。
5. Bridge 只注入受信本地 Widget 页面，并验证消息来源和实例 ID。
6. 每个插件/实例的存储命名空间隔离。
7. 隐藏、锁屏、电池节省和全屏游戏期间暂停动画与高频刷新。
8. 记录 CPU、内存、网络、崩溃和未处理 Promise；超预算可自动降频。

## 9. 原生与脚本插件策略

### 第一阶段

- 不在 Long Grid 进程加载第三方 DLL/Script。
- 通过 Long助手 IPC 调用命令。
- 原生 UI 只显示“在 Long助手中打开”。
- 没有安装 Long助手时，动作卡显示明确依赖，不自动安装。

### 第二阶段

建立 `LongPluginBroker.exe`：

- 独立进程加载高信任插件。
- Long Grid 通过版本化 IPC 获取数据和执行命令。
- 设置超时、取消、崩溃隔离和资源预算。
- 文件写入、输入注入、窗口控制继续要求二次确认。

即使进程外运行也不等于安全沙盒；高权限能力仍需用户审查。

## 10. 插件安装与共享

建议采用“共享包、独立启用状态”：

- `.lpak` 和签名验证逻辑共享。
- 插件包可存放在按用户的共享只读版本目录。
- Long助手和 Long Grid 分别保存启用状态、实例布局和宿主设置。
- 升级采用事务切换，两个宿主确认兼容后再切到新版本。
- 删除包前确认没有任何宿主仍引用。

不要让两个进程同时原地修改插件目录或共享同一个可写 WebView 配置目录。

## 11. 推荐实施顺序

### W0：合同验证

- 抽出宿主中立 Manifest/Packaging 契约。
- Long Grid 能读取现有 `.lpak` 并列出命令、权限和兼容等级。
- 不执行插件。

### W1：动作卡

- 通过 IPC 调用 Long助手命令。
- 支持无参数命令、预设、结果和高风险审查。
- 选 3 个低风险插件验证：UUID、Base64、硬件监控。

### W2：Web Widget

- 新增 `widgets` Schema 和 Web Widget SDK。
- 实现主题、语言、尺寸、可见性、暂停和实例设置。
- 限制活跃 WebView 数量并完成资源基准测试。

### W3：Broker

- Hybrid/Native/Script 进程外运行。
- 超时、崩溃、CPU/内存和权限审计。
- 市场展示 Widget 支持和宿主兼容矩阵。

## 12. 验收标准

- 旧 Long助手插件无需修改仍能安装和运行。
- Long Grid 遇到未知字段/不支持 Surface 时给出明确降级，而非崩溃。
- 一个插件更新不能同时破坏两个宿主。
- 隐藏 Widget 后停止动画、计时器和非必要网络。
- Widget 崩溃不会导致桌面容器或 Explorer 崩溃。
- 多显示器/DPI 切换后实例尺寸与位置可恢复。
- 卸载 Widget 不删除插件创建的用户数据，除非用户明确选择。
- 权限新增、宿主不支持和高信任状态在安装/升级前可见。

## 13. 最终建议

对外不要承诺“所有 Long助手插件都能直接变成小组件”。更准确的产品表述是：

> Long Grid 兼容 Long 插件包和命令生态；支持 Widget Surface 的插件可直接作为桌面小组件运行，其他插件会以动作卡、数据卡或“在 Long助手中打开”的方式安全降级。

这既是真实可实现的兼容，也为未来插件作者提供了清晰迁移路径。
