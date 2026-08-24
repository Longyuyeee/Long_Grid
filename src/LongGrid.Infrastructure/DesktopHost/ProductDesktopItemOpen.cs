using System.ComponentModel;
using System.Runtime.InteropServices;
using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopItemOpenSource
{
    KeyboardEnter,
    PointerDoubleClick,
    AssistiveInvoke,
}

public enum ProductDesktopItemOpenStatus
{
    LaunchAccepted,
    InvalidRequest,
    StaleAuthority,
    TargetUnavailable,
    UnresolvedReference,
    TypeChanged,
    ReparsePointRejected,
    ReviewRequiredKind,
    ReferenceTooLarge,
    ReferenceMalformed,
    ProtocolRejected,
    ShortcutTargetUnavailable,
    ShortcutTargetUnsafe,
    LaunchFailed,
}

public sealed record ProductDesktopItemOpenRequest(
    string ContainerId,
    string DisplayId,
    long WorkspaceRevision,
    long TopologyGeneration,
    string ItemId,
    ProductDesktopItemOpenSource Source,
    bool SourceAttested,
    bool IsInjected,
    bool IsAutoRepeat);

public sealed record ProductDesktopItemOpenResult(
    ProductDesktopItemOpenStatus Status,
    ProductDesktopItemOpenSource Source)
{
    public bool IsAccepted => Status == ProductDesktopItemOpenStatus.LaunchAccepted;

    public string UserMessage => Status switch
    {
        ProductDesktopItemOpenStatus.LaunchAccepted => "已提交系统打开",
        ProductDesktopItemOpenStatus.StaleAuthority => "桌面状态已变化，请重试",
        ProductDesktopItemOpenStatus.TargetUnavailable => "引用不存在，请检查后重试",
        ProductDesktopItemOpenStatus.UnresolvedReference => "引用仍待确认，未执行打开",
        ProductDesktopItemOpenStatus.TypeChanged => "目标类型已变化，未执行打开",
        ProductDesktopItemOpenStatus.ReparsePointRejected => "目标需要重新确认，未执行打开",
        ProductDesktopItemOpenStatus.ReferenceTooLarge => "快捷方式超出安全大小",
        ProductDesktopItemOpenStatus.ReferenceMalformed => "快捷方式格式无效或编码不受支持",
        ProductDesktopItemOpenStatus.ProtocolRejected => "网址协议不受支持，仅允许 HTTP/HTTPS",
        ProductDesktopItemOpenStatus.ShortcutTargetUnavailable => "快捷方式目标不存在",
        ProductDesktopItemOpenStatus.ShortcutTargetUnsafe => "快捷方式目标需要重新确认",
        ProductDesktopItemOpenStatus.LaunchFailed => "系统未能打开，请重试",
        _ => "当前项目无法安全打开",
    };
}

internal sealed record ProductDesktopItemOpenSurfaceInput(
    string ContainerId,
    string ItemId,
    ProductDesktopItemOpenSource Source,
    bool SourceAttested,
    bool IsInjected,
    bool IsAutoRepeat);

internal sealed record ProductDesktopItemOpenFeedback(
    string ContainerId,
    string ItemId,
    ProductDesktopItemOpenStatus Status,
    string Message);

internal interface IProductDesktopItemShellLauncher
{
    ProductDesktopShellLaunchResult Launch(
        string target,
        string? parameters = null);
}

internal sealed record ProductDesktopShellLaunchResult(
    bool Accepted,
    int ProcessId);

internal sealed class WindowsProductDesktopItemShellLauncher
    : IProductDesktopItemShellLauncher
{
    public ProductDesktopShellLaunchResult Launch(
        string target,
        string? parameters = null)
    {
        var info = new ShellExecuteInfo
        {
            Size = Marshal.SizeOf<ShellExecuteInfo>(),
            Mask = SeeMaskNoCloseProcess | SeeMaskNoAsync,
            Verb = "open",
            File = target,
            Parameters = parameters,
            Show = ShowNormal,
        };
        if (!ShellExecuteEx(ref info))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        int processId = 0;
        if (info.Process != nint.Zero)
        {
            try
            {
                processId = checked((int)GetProcessId(info.Process));
            }
            finally
            {
                _ = CloseHandle(info.Process);
            }
        }
        return new(Accepted: true, processId);
    }

    private const uint SeeMaskNoCloseProcess = 0x00000040;
    private const uint SeeMaskNoAsync = 0x00000100;
    private const int ShowNormal = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        internal int Size;
        internal uint Mask;
        internal nint Window;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Verb;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? File;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Parameters;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Directory;
        internal int Show;
        internal nint Instance;
        internal nint IdList;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Class;
        internal nint ClassKey;
        internal uint HotKey;
        internal nint IconOrMonitor;
        internal nint Process;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);

    [DllImport("kernel32.dll")]
    private static extern uint GetProcessId(nint process);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}

public sealed class ProductDesktopItemOpenController
{
    private readonly object gate = new();
    private readonly IProductDesktopItemShellLauncher launcher;
    private readonly IProductDesktopItemOpenReferenceResolver referenceResolver;

    public ProductDesktopItemOpenController()
        : this(
            new WindowsProductDesktopItemShellLauncher(),
            new WindowsProductDesktopItemOpenReferenceResolver())
    {
    }

    internal ProductDesktopItemOpenController(
        IProductDesktopItemShellLauncher launcher,
        IProductDesktopItemOpenReferenceResolver? referenceResolver = null)
    {
        this.launcher = launcher
            ?? throw new ArgumentNullException(nameof(launcher));
        this.referenceResolver = referenceResolver
            ?? new WindowsProductDesktopItemOpenReferenceResolver();
    }

    internal int LastLaunchProcessIdForEvidence { get; private set; }

    public ProductDesktopItemOpenResult Open(
        ProductDesktopItemOpenRequest request,
        ProductWorkspaceState? state,
        long currentWorkspaceRevision,
        ProductDisplayTopologySnapshot topology)
    {
        lock (gate)
        {
            return OpenUnsafe(
                request,
                state,
                currentWorkspaceRevision,
                topology);
        }
    }

    private ProductDesktopItemOpenResult OpenUnsafe(
        ProductDesktopItemOpenRequest request,
        ProductWorkspaceState? state,
        long currentWorkspaceRevision,
        ProductDisplayTopologySnapshot topology)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(topology);
        LastLaunchProcessIdForEvidence = 0;
        if (!RequestIsValid(request))
        {
            return Result(ProductDesktopItemOpenStatus.InvalidRequest, request);
        }
        if (state is null
            || !topology.IsAuthoritative
            || request.WorkspaceRevision != currentWorkspaceRevision
            || request.TopologyGeneration != topology.Generation)
        {
            return Result(ProductDesktopItemOpenStatus.StaleAuthority, request);
        }

        ProductContainerState[] containers = state.Containers
            .Where(container => string.Equals(
                container.Id,
                request.ContainerId,
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (containers.Length != 1
            || !DisplayMatches(containers[0], request.DisplayId, topology)
            || !TryParseOrdinal(request.ItemId, out int ordinal)
            || ordinal > containers[0].Items.Count)
        {
            return Result(ProductDesktopItemOpenStatus.TargetUnavailable, request);
        }

        ProductItemReferenceState item = containers[0].Items[ordinal - 1];
        if (item.Resolution != ProductItemReferenceResolution.Resolved
            || item.CatalogEntry?.Identity is null)
        {
            return Result(
                ProductDesktopItemOpenStatus.UnresolvedReference,
                request);
        }
        if (item.PersistedKind is not (ConfigurationItemKind.File
                or ConfigurationItemKind.Folder
                or ConfigurationItemKind.Shortcut
                or ConfigurationItemKind.Url)
            || !string.Equals(
                item.CatalogEntry.Identity.Provider,
                "filesystem",
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(
                item.CatalogEntry.Identity.VolumeId)
                != string.IsNullOrWhiteSpace(
                    item.CatalogEntry.Identity.FileId)
            || !Enum.IsDefined(item.CatalogEntry.Kind)
            || MapKind(item.CatalogEntry.Kind) != item.PersistedKind
            || !Path.IsPathFullyQualified(
                item.CatalogEntry.Identity.CanonicalTarget))
        {
            return Result(ProductDesktopItemOpenStatus.InvalidRequest, request);
        }

        string target;
        try
        {
            target = Path.GetFullPath(
                item.CatalogEntry.Identity.CanonicalTarget);
            string persistedTarget = Path.GetFullPath(item.PersistedTarget);
            if (!string.Equals(
                target,
                persistedTarget,
                StringComparison.OrdinalIgnoreCase))
            {
                return Result(ProductDesktopItemOpenStatus.InvalidRequest, request);
            }
            if (item.PersistedKind is ConfigurationItemKind.File
                or ConfigurationItemKind.Folder)
            {
                bool file = File.Exists(target);
                bool directory = Directory.Exists(target);
                if (!file && !directory)
                {
                    return Result(
                        ProductDesktopItemOpenStatus.TargetUnavailable,
                        request);
                }
                if ((item.PersistedKind == ConfigurationItemKind.File && !file)
                    || (item.PersistedKind == ConfigurationItemKind.Folder
                        && !directory))
                {
                    return Result(ProductDesktopItemOpenStatus.TypeChanged, request);
                }
                if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                {
                    return Result(
                        ProductDesktopItemOpenStatus.ReparsePointRejected,
                        request);
                }
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or NotSupportedException
                or PathTooLongException
                or UnauthorizedAccessException)
        {
            return Result(ProductDesktopItemOpenStatus.TargetUnavailable, request);
        }

        string? parameters = null;
        if (item.PersistedKind is ConfigurationItemKind.Shortcut
            or ConfigurationItemKind.Url)
        {
            ProductDesktopItemOpenReferenceResolution resolved =
                referenceResolver.Resolve(item.PersistedKind, target);
            if (!resolved.IsResolved)
            {
                return Result(resolved.Status, request);
            }
            target = resolved.Target!;
            parameters = resolved.Parameters;
        }

        try
        {
            ProductDesktopShellLaunchResult launched = launcher.Launch(
                target,
                parameters);
            LastLaunchProcessIdForEvidence = launched.ProcessId;
            return Result(
                launched.Accepted
                    ? ProductDesktopItemOpenStatus.LaunchAccepted
                    : ProductDesktopItemOpenStatus.LaunchFailed,
                request);
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or InvalidOperationException
                or NotSupportedException)
        {
            return Result(ProductDesktopItemOpenStatus.LaunchFailed, request);
        }
    }

    private static bool RequestIsValid(ProductDesktopItemOpenRequest request) =>
        !string.IsNullOrWhiteSpace(request.ContainerId)
        && !string.IsNullOrWhiteSpace(request.DisplayId)
        && request.WorkspaceRevision > 0
        && request.TopologyGeneration > 0
        && !string.IsNullOrWhiteSpace(request.ItemId)
        && Enum.IsDefined(request.Source)
        && request.SourceAttested
        && !request.IsInjected
        && !request.IsAutoRepeat;

    private static bool TryParseOrdinal(string itemId, out int ordinal)
    {
        ordinal = 0;
        return itemId.StartsWith("item:", StringComparison.Ordinal)
            && int.TryParse(
                itemId.AsSpan("item:".Length),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out ordinal)
            && ordinal > 0;
    }

    private static bool DisplayMatches(
        ProductContainerState container,
        string displayId,
        ProductDisplayTopologySnapshot topology)
    {
        string expected = topology.Displays.Any(display => string.Equals(
            display.StableId,
            container.Placement.DisplayKey,
            StringComparison.Ordinal))
                ? container.Placement.DisplayKey
                : topology.Displays.Single(display => display.IsPrimary).StableId;
        return string.Equals(expected, displayId, StringComparison.Ordinal);
    }

    private static ConfigurationItemKind MapKind(
        LongGrid.Core.DesktopItems.DesktopItemKind kind) => kind switch
        {
            LongGrid.Core.DesktopItems.DesktopItemKind.File =>
                ConfigurationItemKind.File,
            LongGrid.Core.DesktopItems.DesktopItemKind.Directory =>
                ConfigurationItemKind.Folder,
            LongGrid.Core.DesktopItems.DesktopItemKind.Shortcut =>
                ConfigurationItemKind.Shortcut,
            LongGrid.Core.DesktopItems.DesktopItemKind.InternetShortcut =>
                ConfigurationItemKind.Url,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static ProductDesktopItemOpenResult Result(
        ProductDesktopItemOpenStatus status,
        ProductDesktopItemOpenRequest request) => new(status, request.Source);
}
