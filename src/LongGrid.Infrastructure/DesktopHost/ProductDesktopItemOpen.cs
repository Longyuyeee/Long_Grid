using System.ComponentModel;
using System.Runtime.InteropServices;
using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopItemOpenSource
{
    KeyboardEnter,
    PointerSingleClick,
    PointerDoubleClick,
    AssistiveInvoke,
    FeedbackRetry,
    FeedbackLocateInExplorer,
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
    ExplorerLocateAccepted,
    ExplorerParentUnavailable,
    ExplorerParentUnsafe,
    ExplorerLaunchFailed,
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
    ProductDesktopItemOpenSource Source,
    bool CanRetry = false,
    bool CanLocateInExplorer = false)
{
    public bool IsAccepted => Status is ProductDesktopItemOpenStatus.LaunchAccepted
        or ProductDesktopItemOpenStatus.ExplorerLocateAccepted;

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
        ProductDesktopItemOpenStatus.ExplorerLocateAccepted =>
            "已提交资源管理器定位",
        ProductDesktopItemOpenStatus.ExplorerParentUnavailable =>
            "父目录不存在，无法安全定位",
        ProductDesktopItemOpenStatus.ExplorerParentUnsafe =>
            "父目录需要重新确认，未执行定位",
        ProductDesktopItemOpenStatus.ExplorerLaunchFailed =>
            "资源管理器未能定位，请重试",
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
    string Message,
    bool CanRetry = false,
    bool CanLocateInExplorer = false);

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
        ProductDisplayTopologySnapshot topology,
        ProductWorkspaceFolderContentSet? folderContents = null)
    {
        lock (gate)
        {
            ProductDesktopItemOpenResult result = request.Source ==
                ProductDesktopItemOpenSource.FeedbackLocateInExplorer
                    ? LocateInExplorerUnsafe(
                        request,
                        state,
                        currentWorkspaceRevision,
                        topology,
                        folderContents)
                    : OpenUnsafe(
                        request,
                        state,
                        currentWorkspaceRevision,
                        topology,
                        folderContents);
            return result with
            {
                CanRetry = CanRetry(result.Status),
                CanLocateInExplorer = !result.IsAccepted
                    && TryResolveExplorerLocation(
                        request,
                        state,
                        currentWorkspaceRevision,
                        topology,
                        folderContents,
                        out _,
                        out _),
            };
        }
    }

    private ProductDesktopItemOpenResult LocateInExplorerUnsafe(
        ProductDesktopItemOpenRequest request,
        ProductWorkspaceState? state,
        long currentWorkspaceRevision,
        ProductDisplayTopologySnapshot topology,
        ProductWorkspaceFolderContentSet? folderContents)
    {
        LastLaunchProcessIdForEvidence = 0;
        if (!TryResolveExplorerLocation(
                request,
            state,
            currentWorkspaceRevision,
            topology,
            folderContents,
            out string? location,
                out bool selectTarget,
                out ProductDesktopItemOpenStatus failure))
        {
            return Result(failure, request);
        }
        string windows = Environment.GetFolderPath(
            Environment.SpecialFolder.Windows);
        string explorer = Path.Combine(windows, "explorer.exe");
        if (!File.Exists(explorer))
        {
            return Result(
                ProductDesktopItemOpenStatus.ExplorerLaunchFailed,
                request);
        }
        string parameters = selectTarget
            ? $"/select,\"{location}\""
            : $"\"{location}\"";
        try
        {
            ProductDesktopShellLaunchResult launched = launcher.Launch(
                explorer,
                parameters);
            LastLaunchProcessIdForEvidence = launched.ProcessId;
            return Result(
                launched.Accepted
                    ? ProductDesktopItemOpenStatus.ExplorerLocateAccepted
                    : ProductDesktopItemOpenStatus.ExplorerLaunchFailed,
                request);
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or InvalidOperationException
                or NotSupportedException)
        {
            return Result(
                ProductDesktopItemOpenStatus.ExplorerLaunchFailed,
                request);
        }
    }

    private static bool TryResolveExplorerLocation(
        ProductDesktopItemOpenRequest request,
        ProductWorkspaceState? state,
        long currentWorkspaceRevision,
        ProductDisplayTopologySnapshot topology,
        ProductWorkspaceFolderContentSet? folderContents,
        out string? location,
        out bool selectTarget) => TryResolveExplorerLocation(
            request,
            state,
            currentWorkspaceRevision,
            topology,
            folderContents,
            out location,
            out selectTarget,
            out _);

    private static bool TryResolveExplorerLocation(
        ProductDesktopItemOpenRequest request,
        ProductWorkspaceState? state,
        long currentWorkspaceRevision,
        ProductDisplayTopologySnapshot topology,
        ProductWorkspaceFolderContentSet? folderContents,
        out string? location,
        out bool selectTarget,
        out ProductDesktopItemOpenStatus failure)
    {
        location = null;
        selectTarget = false;
        failure = ProductDesktopItemOpenStatus.InvalidRequest;
        if (!RequestIsValid(request))
        {
            return false;
        }
        if (state is null
            || !topology.IsAuthoritative
            || request.WorkspaceRevision != currentWorkspaceRevision
            || request.TopologyGeneration != topology.Generation)
        {
            failure = ProductDesktopItemOpenStatus.StaleAuthority;
            return false;
        }
        ProductContainerState[] containers = state.Containers
            .Where(container => string.Equals(
                container.Id,
                request.ContainerId,
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (containers.Length != 1
            || !DisplayMatches(containers[0], request.DisplayId, topology))
        {
            failure = ProductDesktopItemOpenStatus.TargetUnavailable;
            return false;
        }
        if (request.ItemId.StartsWith("folder:", StringComparison.Ordinal))
        {
            if (!TryResolveFolderContentItem(
                    request,
                    containers[0],
                    folderContents,
                    out ProductWorkspaceFolderContentItem? folderItem,
                    out failure))
            {
                return false;
            }
            location = folderItem!.Target;
            selectTarget = true;
            return true;
        }
        if (!TryParseOrdinal(request.ItemId, out int ordinal)
            || ordinal > containers[0].Items.Count)
        {
            failure = ProductDesktopItemOpenStatus.TargetUnavailable;
            return false;
        }
        ProductItemReferenceState item = containers[0].Items[ordinal - 1];
        if (item.Resolution != ProductItemReferenceResolution.Resolved
            || item.CatalogEntry?.Identity is null
            || item.PersistedKind is not (ConfigurationItemKind.File
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
            failure = ProductDesktopItemOpenStatus.UnresolvedReference;
            return false;
        }
        try
        {
            string target = Path.GetFullPath(
                item.CatalogEntry.Identity.CanonicalTarget);
            if (!string.Equals(
                    target,
                    Path.GetFullPath(item.PersistedTarget),
                    StringComparison.OrdinalIgnoreCase)
                || target.Contains('"'))
            {
                return false;
            }
            string? parent = Path.GetDirectoryName(target);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            {
                failure = ProductDesktopItemOpenStatus.ExplorerParentUnavailable;
                return false;
            }
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
            {
                failure = ProductDesktopItemOpenStatus.ExplorerParentUnsafe;
                return false;
            }
            bool exists = File.Exists(target) || Directory.Exists(target);
            if (exists
                && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            {
                failure = ProductDesktopItemOpenStatus.ExplorerParentUnsafe;
                return false;
            }
            location = exists ? target : parent;
            selectTarget = exists;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or NotSupportedException
                or PathTooLongException
                or UnauthorizedAccessException)
        {
            failure = ProductDesktopItemOpenStatus.ExplorerParentUnavailable;
            return false;
        }
    }

    private static bool CanRetry(ProductDesktopItemOpenStatus status) =>
        status is not (
            ProductDesktopItemOpenStatus.LaunchAccepted
                or ProductDesktopItemOpenStatus.ExplorerLocateAccepted
                or ProductDesktopItemOpenStatus.InvalidRequest);

    private ProductDesktopItemOpenResult OpenUnsafe(
        ProductDesktopItemOpenRequest request,
        ProductWorkspaceState? state,
        long currentWorkspaceRevision,
        ProductDisplayTopologySnapshot topology,
        ProductWorkspaceFolderContentSet? folderContents)
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
            || !DisplayMatches(containers[0], request.DisplayId, topology))
        {
            return Result(ProductDesktopItemOpenStatus.TargetUnavailable, request);
        }

        if (request.ItemId.StartsWith("folder:", StringComparison.Ordinal))
        {
            if (!TryResolveFolderContentItem(
                    request,
                    containers[0],
                    folderContents,
                    out ProductWorkspaceFolderContentItem? folderItem,
                    out ProductDesktopItemOpenStatus failure))
            {
                return Result(failure, request);
            }
            string folderTarget = folderItem!.Target;
            string? folderParameters = null;
            if (folderItem.Kind is ConfigurationItemKind.Shortcut
                or ConfigurationItemKind.Url)
            {
                ProductDesktopItemOpenReferenceResolution reference =
                    referenceResolver.Resolve(folderItem.Kind, folderTarget);
                if (!reference.IsResolved)
                {
                    return Result(reference.Status, request);
                }
                folderTarget = reference.Target!;
                folderParameters = reference.Parameters;
            }
            return Launch(folderTarget, folderParameters, request);
        }
        if (!TryParseOrdinal(request.ItemId, out int ordinal)
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

        return Launch(target, parameters, request);
    }

    private ProductDesktopItemOpenResult Launch(
        string target,
        string? parameters,
        ProductDesktopItemOpenRequest request)
    {
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

    private static bool TryResolveFolderContentItem(
        ProductDesktopItemOpenRequest request,
        ProductContainerState container,
        ProductWorkspaceFolderContentSet? folderContents,
        out ProductWorkspaceFolderContentItem? item,
        out ProductDesktopItemOpenStatus failure)
    {
        item = null;
        failure = ProductDesktopItemOpenStatus.TargetUnavailable;
        ProductWorkspaceContainerFolderContent? content =
            folderContents?.Find(container.Id);
        ProductWorkspaceFolderContentItem[] matches = content?.HasUsableProjection == true
            ? content.Items.Where(candidate => string.Equals(
                candidate.ItemId,
                request.ItemId,
                StringComparison.Ordinal)).Take(2).ToArray()
            : Array.Empty<ProductWorkspaceFolderContentItem>();
        if (matches.Length != 1
            || container.FolderBinding is null
            || container.FolderBinding.Resolution !=
                ProductContainerFolderBindingResolution.Resolved)
        {
            return false;
        }

        ProductContainerFolderBindingState binding =
            WindowsProductContainerFolderBinding.Resolve(container.FolderBinding);
        if (binding.Resolution != ProductContainerFolderBindingResolution.Resolved
            || string.IsNullOrWhiteSpace(binding.ResolvedTarget))
        {
            return false;
        }

        try
        {
            string root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(binding.ResolvedTarget));
            string target = Path.GetFullPath(matches[0].Target);
            if (target.Contains('"')
                || !string.Equals(
                    Path.GetDirectoryName(target),
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                failure = ProductDesktopItemOpenStatus.InvalidRequest;
                return false;
            }
            bool file = File.Exists(target);
            bool directory = Directory.Exists(target);
            if (!file && !directory)
            {
                return false;
            }
            if ((matches[0].Kind == ConfigurationItemKind.Folder && !directory)
                || (matches[0].Kind != ConfigurationItemKind.Folder && !file))
            {
                failure = ProductDesktopItemOpenStatus.TypeChanged;
                return false;
            }
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            {
                failure = ProductDesktopItemOpenStatus.ReparsePointRejected;
                return false;
            }
            item = matches[0] with { Target = target };
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or NotSupportedException
                or PathTooLongException or UnauthorizedAccessException)
        {
            return false;
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
