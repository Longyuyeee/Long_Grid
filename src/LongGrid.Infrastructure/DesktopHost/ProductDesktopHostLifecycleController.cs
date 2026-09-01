using System.ComponentModel;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

internal sealed record ProductDesktopWorkspaceCreateInput(
    ProductDesktopWorkspaceCreateInputKind Kind,
    bool SourceAttested,
    bool IsInjected,
    bool IsAutoRepeat,
    PixelRect? RequestedBoundsPixels = null);

public enum ProductDesktopHostLifecycleStatus
{
    DisabledBySafetyPolicy,
    DisabledByUser,
    AwaitingHost,
    AwaitingWorkspace,
    SuspendedUnsafeTopology,
    SuspendedSystemSurface,
    ReadyReadOnly,
    Faulted,
    Completed,
}

public sealed record ProductDesktopHostLifecycleSnapshot(
    ProductDesktopHostLifecycleStatus Status,
    long Generation,
    bool NativeHostConnected,
    int OwnedWindowCount,
    long WorkspaceRevision = 0,
    long TopologyGeneration = 0,
    int RenderedContainerCount = 0,
    bool ReadOnlyAccessibilityAvailable = false,
    bool PassiveWindowContractAttested = false,
    ProductDesktopInteractionSystemSurfaceEventKind? LastSystemSurfaceEvent = null,
    bool ExplicitInteractionActive = false,
    int SelectedItemCount = 0,
    bool FocusedItemAvailable = false,
    long SelectionRevision = 0)
{
    public bool FeatureEnabled =>
        Status is ProductDesktopHostLifecycleStatus.AwaitingHost
            or ProductDesktopHostLifecycleStatus.AwaitingWorkspace
            or ProductDesktopHostLifecycleStatus.SuspendedUnsafeTopology
            or ProductDesktopHostLifecycleStatus.SuspendedSystemSurface
            or ProductDesktopHostLifecycleStatus.ReadyReadOnly
            or ProductDesktopHostLifecycleStatus.Faulted;
}

public sealed record ProductDesktopHostReadOnlyProjection
{
    public const int MaximumVisibleItems = 12;
    public const int MaximumCompactVisibleItems = 18;
    public const int MaximumVisibleNameLength = 512;

    private ProductDesktopHostReadOnlyProjection(
        string containerId,
        string title,
        IReadOnlyList<string> itemNames,
        string color,
        double opacity,
        bool isCollapsed,
        double xDip,
        double yDip,
        double widthDip,
        double heightDip,
        bool isLocked,
        IReadOnlyList<string> itemIds,
        int totalItemCount,
        IReadOnlyList<ProductDesktopItemVisualPresentation> itemVisuals,
        ProductContainerTitleVisibilityPolicy titleVisibility,
        ProductContainerTitleDoubleClickAction titleDoubleClickAction,
        int visibleItemStartOrdinal,
        ProductContainerContentDensity contentDensity,
        bool searchHighlighted,
        string? searchHighlightedItemId)
    {
        ContainerId = containerId;
        Title = title;
        ItemNames = itemNames;
        Color = color;
        Opacity = opacity;
        IsCollapsed = isCollapsed;
        XDip = xDip;
        YDip = yDip;
        WidthDip = widthDip;
        HeightDip = heightDip;
        IsLocked = isLocked;
        ItemIds = itemIds;
        TotalItemCount = totalItemCount;
        ItemVisuals = itemVisuals;
        TitleVisibility = titleVisibility;
        TitleDoubleClickAction = titleDoubleClickAction;
        VisibleItemStartOrdinal = visibleItemStartOrdinal;
        ContentDensity = contentDensity;
        SearchHighlighted = searchHighlighted;
        SearchHighlightedItemId = searchHighlightedItemId;
    }

    public string ContainerId { get; }

    public string Title { get; }

    public IReadOnlyList<string> ItemNames { get; }

    public string Color { get; }

    public double Opacity { get; }

    public bool IsCollapsed { get; }

    public double XDip { get; }

    public double YDip { get; }

    public double WidthDip { get; }

    public double HeightDip { get; }

    public bool IsLocked { get; }

    public IReadOnlyList<string> ItemIds { get; }

    public int TotalItemCount { get; }

    public IReadOnlyList<ProductDesktopItemVisualPresentation> ItemVisuals { get; }

    public ProductContainerTitleVisibilityPolicy TitleVisibility { get; }

    public ProductContainerTitleDoubleClickAction TitleDoubleClickAction { get; }

    public int VisibleItemStartOrdinal { get; }

    public ProductContainerContentDensity ContentDensity { get; }

    public bool SearchHighlighted { get; }

    public string? SearchHighlightedItemId { get; }

    public static int VisibleItemCapacity(ProductContainerContentDensity density) =>
        density switch
        {
            ProductContainerContentDensity.Comfortable => MaximumVisibleItems,
            ProductContainerContentDensity.Compact => MaximumCompactVisibleItems,
            _ => throw new ArgumentOutOfRangeException(nameof(density)),
        };

    public ProductDesktopContainerHeaderPresentation Header =>
        ProductDesktopContainerHeaderPresentation.Create(
            Title,
            TotalItemCount,
            IsLocked,
            IsCollapsed,
            TitleVisibility,
            TitleDoubleClickAction);

    public static ProductDesktopHostReadOnlyProjection Create(
        string containerId,
        string title,
        IEnumerable<string> itemNames,
        string color,
        double opacity,
        bool isCollapsed,
        double xDip,
        double yDip,
        double widthDip,
        double heightDip,
        bool isLocked = false,
        IEnumerable<string>? itemIds = null,
        int? totalItemCount = null,
        IEnumerable<ProductDesktopItemVisualPresentation>? itemVisuals = null,
        ProductContainerTitleVisibilityPolicy titleVisibility =
            ProductContainerTitleVisibilityPolicy.Always,
        ProductContainerTitleDoubleClickAction titleDoubleClickAction =
            ProductContainerTitleDoubleClickAction.ToggleCollapsed,
        int visibleItemStartOrdinal = 1,
        ProductContainerContentDensity contentDensity =
            ProductContainerContentDensity.Comfortable,
        bool searchHighlighted = false,
        string? searchHighlightedItemId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(itemNames);
        int visibleItemCapacity = VisibleItemCapacity(contentDensity);
        string[] visibleItems = itemNames
            .Take(visibleItemCapacity)
            .ToArray();
        string[] visibleItemIds = (itemIds
                ?? Enumerable.Range(1, visibleItems.Length)
                    .Select(ordinal => $"item:{ordinal}"))
            .Take(visibleItemCapacity)
            .ToArray();
        int boundedTotalItemCount = totalItemCount ?? visibleItems.Length;
        int boundedVisibleItemStartOrdinal = boundedTotalItemCount == 0
            ? 0
            : visibleItemStartOrdinal;
        ProductDesktopItemVisualPresentation[] visibleItemVisuals =
            (itemVisuals ?? visibleItems.Select(_ =>
                ProductDesktopItemVisualPresentation.Create(
                    ConfigurationItemKind.File,
                    ProductItemReferenceResolution.Resolved)))
            .Take(visibleItemCapacity)
            .ToArray();
        if (visibleItems.Any(string.IsNullOrWhiteSpace)
            || containerId.Length > ProductConfigurationLimits.MaximumIdLength
            || title.Length > ProductConfigurationLimits.MaximumNameLength
            || visibleItems.Any(item => item.Length > MaximumVisibleNameLength)
            || visibleItemIds.Length != visibleItems.Length
            || visibleItemVisuals.Length != visibleItems.Length
            || visibleItemIds.Any(string.IsNullOrWhiteSpace)
            || visibleItemIds.Any(id => id.Length
                > ProductConfigurationLimits.MaximumIdLength)
            || visibleItemIds.Distinct(StringComparer.Ordinal).Count()
                != visibleItemIds.Length
            || boundedTotalItemCount < visibleItems.Length
            || boundedTotalItemCount > ProductConfigurationLimits.MaximumItems
            || (boundedTotalItemCount > 0
                && (boundedVisibleItemStartOrdinal < 1
                    || boundedVisibleItemStartOrdinal > boundedTotalItemCount
                    || boundedVisibleItemStartOrdinal - 1 + visibleItems.Length
                        > boundedTotalItemCount))
            || color is null
            || color.Length != 7
            || color[0] != '#'
            || !color[1..].All(Uri.IsHexDigit)
            || !double.IsFinite(opacity)
            || opacity is < 0 or > 1
            || !double.IsFinite(xDip)
            || !double.IsFinite(yDip)
            || !double.IsFinite(widthDip)
            || !double.IsFinite(heightDip)
            || !Enum.IsDefined(titleVisibility)
            || !Enum.IsDefined(titleDoubleClickAction)
            || !Enum.IsDefined(contentDensity)
            || searchHighlightedItemId is not null
                && (!searchHighlighted
                    || !visibleItemIds.Contains(
                        searchHighlightedItemId,
                        StringComparer.Ordinal))
            || widthDip <= 0
            || heightDip <= 0)
        {
            throw new ArgumentException(
                "DesktopHost projection values must remain finite and bounded.");
        }

        return new(
            containerId,
            title,
            Array.AsReadOnly(visibleItems),
            color,
            opacity,
            isCollapsed,
            xDip,
            yDip,
            widthDip,
            heightDip,
            isLocked,
            Array.AsReadOnly(visibleItemIds),
            boundedTotalItemCount,
            Array.AsReadOnly(visibleItemVisuals),
            titleVisibility,
            titleDoubleClickAction,
            boundedVisibleItemStartOrdinal,
            contentDensity,
            searchHighlighted,
            searchHighlightedItemId);
    }
}

internal interface IProductDesktopHostReadOnlySurface : IDisposable
{
    nint Handle { get; }

    nint InstanceMarker { get; }

    uint ProcessId { get; }

    uint ThreadId { get; }

    bool ReadOnlyAccessibilityAttested { get; }

    bool PassiveWindowContractAttested { get; }

    bool ExplicitWindowContractAttested { get; }

    bool HiddenWindowContractAttested { get; }

    bool ApplyExplicit();

    bool ApplyPassive();

    bool ApplyHidden();

    void BindSelection(
        Func<ProductDesktopInteractionSurfaceTransactionSnapshot?> snapshot,
        Func<string, ProductDesktopSelectionRequest, bool> apply)
    {
    }

    void BindWorkspaceCreate(
        Func<ProductDesktopWorkspaceCreateInput, bool> requestCreate)
    {
    }

    void BindContainerLayout(
        Func<ProductDesktopContainerLayoutSurfaceInput, bool> requestLayout)
    {
    }

    void BindContainerHeaderCommand(
        Func<ProductDesktopContainerHeaderSurfaceInput, bool> requestCommand)
    {
    }

    void BindItemViewport(
        Func<ProductDesktopItemViewportSurfaceInput, bool> requestViewport)
    {
    }

    void BindItemOpen(
        Func<ProductDesktopItemOpenSurfaceInput, bool> requestOpen)
    {
    }

    void BindExplorerReferenceDrop(
        Func<object, string, bool> requestDrop)
    {
    }

    void BindReferenceReassignment(
        Func<ProductDesktopReferenceReassignmentSurfaceInput, bool> request)
    {
    }

    bool ApplyItemOpenFeedback(ProductDesktopItemOpenFeedback feedback) => false;

    bool ApplyItemOpenPolicy(bool openItemsWithSingleClick) => true;

    bool ApplyPresentation(ProductDesktopHostDisplayProjection projection) =>
        false;

    bool ApplyContainerLayoutPreview(
        string containerId,
        ProductContainerPlacementState? placement) => false;

    bool ApplyContainerLayoutPreview(
        ProductDesktopHostReadOnlyProjection source,
        ProductContainerPlacementState placement) =>
        ApplyContainerLayoutPreview(source.ContainerId, placement);

    bool ApplyContainerLayoutKeyboardFocus(string? containerId) => false;

    void RefreshSelection()
    {
    }
}

internal interface IProductDesktopHostReadOnlySurfaceFactory
{
    IProductDesktopHostReadOnlySurface Create(
        ProductDesktopHostDisplayProjection projection,
        nint instanceMarker,
        bool startHidden);
}

public sealed class ProductDesktopHostLifecycleController : IAsyncDisposable
{
    private static long nextInstanceMarker;
    private readonly object gate = new();
    private readonly bool enabled;
    private bool userEnabled;
    private readonly IProductDesktopHostReadOnlySurfaceFactory? surfaceFactory;
    private readonly ProductDesktopHostWindowBridge? windowBridge;
    private readonly ProductDesktopInteractionDevelopmentController?
        interactionDevelopment;
    private readonly ProductDesktopInteractionIntentPreparationBridge?
        intentPreparation;
    private readonly ProductDesktopInteractionInputForwardingAdapter?
        inputForwarding;
    private readonly ProductDesktopInteractionIntentConsumptionController?
        intentConsumption;
    private readonly IProductDesktopInteractionActivationSourceFactory?
        activationSourceFactory;
    private readonly Guid hostInstanceId = Guid.NewGuid();
    private ProductDesktopHostLifecycleSnapshot snapshot;
    private readonly List<IProductDesktopHostReadOnlySurface> surfaces = [];
    private readonly List<IProductDesktopInteractionActivationSource>
        activationSources = [];
    private readonly List<(string DisplayId, long WindowGeneration)>
        registrations = [];
    private ProductDesktopHostProjectionBatch? currentBatch;
    private ProductDesktopHostProjectionUpdate? currentUpdate;
    private long lastWorkspaceRevision = -1;
    private long lastTopologyGeneration = -1;
    private long lastPresentationGeneration = -1;
    private long lastSystemSurfaceSequence;
    private long windowGeneration;
    private ProductDesktopHostPassiveSurfaceModeAdapter? interactionSurface;
    private bool disposed;
    private Func<ProductDesktopWorkspaceCreateRequest, bool>
        requestWorkspaceCreate = static _ => false;
    private Func<ProductDesktopContainerLayoutRequest, bool>
        requestContainerLayout = static _ => false;
    private Func<ProductDesktopContainerHeaderCommandRequest, bool>
        requestContainerHeaderCommand = static _ => false;
    private Func<string, string, ProductDesktopContainerMenuAvailability>
        containerMenuAvailability = static (_, _) =>
            ProductDesktopContainerMenuAvailability.Unavailable;
    private Func<ProductDesktopContainerMenuRequest, bool>
        requestContainerMenu = static _ => false;
    private Func<ProductDesktopItemViewportRequest, bool>
        requestItemViewport = static _ => false;
    private Func<ProductDesktopItemOpenRequest, ProductDesktopItemOpenResult>
        requestItemOpen = static request => new(
            ProductDesktopItemOpenStatus.InvalidRequest,
            request.Source);
    private Func<string, bool> requestDesktopSearch = static _ => false;
    private Func<object, string, bool> requestExplorerReferenceDrop =
        static (_, _) => false;
    private Func<ProductDesktopReferenceReassignmentRequest, bool>
        requestReferenceReassignment = static _ => false;
    private bool openItemsWithSingleClick;

    public ProductDesktopHostLifecycleController(
        ProductDesktopHostFeatureDecision featureDecision)
        : this(featureDecision, interactionDevelopment: null)
    {
    }

    public ProductDesktopHostLifecycleController(
        ProductDesktopHostFeatureDecision featureDecision,
        ProductDesktopInteractionDevelopmentController? interactionDevelopment)
        : this(featureDecision, interactionDevelopment, intentPreparation: null)
    {
    }

    public ProductDesktopHostLifecycleController(
        ProductDesktopHostFeatureDecision featureDecision,
        ProductDesktopInteractionDevelopmentController? interactionDevelopment,
        ProductDesktopInteractionIntentPreparationBridge? intentPreparation)
        : this(
            featureDecision,
            interactionDevelopment,
            intentPreparation,
            inputForwarding: null,
            intentConsumption: null)
    {
    }

    public ProductDesktopHostLifecycleController(
        ProductDesktopHostFeatureDecision featureDecision,
        ProductDesktopInteractionDevelopmentController? interactionDevelopment,
        ProductDesktopInteractionIntentPreparationBridge? intentPreparation,
        ProductDesktopInteractionInputForwardingAdapter? inputForwarding)
        : this(
            featureDecision,
            interactionDevelopment,
            intentPreparation,
            inputForwarding,
            intentConsumption: null)
    {
    }

    public ProductDesktopHostLifecycleController(
        ProductDesktopHostFeatureDecision featureDecision,
        ProductDesktopInteractionDevelopmentController? interactionDevelopment,
        ProductDesktopInteractionIntentPreparationBridge? intentPreparation,
        ProductDesktopInteractionInputForwardingAdapter? inputForwarding,
        ProductDesktopInteractionIntentConsumptionController? intentConsumption,
        bool userEnabled = true)
        : this(
            featureDecision,
            featureDecision?.IsEnabled == true
                ? new WindowsProductDesktopHostReadOnlySurfaceFactory()
                : null,
            featureDecision?.IsEnabled == true
                ? new WindowsProductDesktopHostWindowInspector()
                : null,
            interactionDevelopment,
            intentPreparation,
            inputForwarding,
            intentConsumption,
            featureDecision?.IsEnabled == true
                ? new WindowsProductDesktopInteractionActivationSourceFactory()
                : null,
            userEnabled)
    {
    }

    internal ProductDesktopHostLifecycleController(
        ProductDesktopHostFeatureDecision featureDecision,
        IProductDesktopHostReadOnlySurfaceFactory? surfaceFactory,
        IProductDesktopHostWindowInspector? windowInspector,
        ProductDesktopInteractionDevelopmentController?
            interactionDevelopment = null,
        ProductDesktopInteractionIntentPreparationBridge?
            intentPreparation = null,
        ProductDesktopInteractionInputForwardingAdapter?
            inputForwarding = null,
        ProductDesktopInteractionIntentConsumptionController?
            intentConsumption = null,
        IProductDesktopInteractionActivationSourceFactory?
            activationSourceFactory = null,
        bool userEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(featureDecision);
        enabled = featureDecision.IsEnabled;
        this.userEnabled = enabled && userEnabled;
        if (enabled && (surfaceFactory is null || windowInspector is null))
        {
            throw new ArgumentException(
                "Enabled DesktopHost lifecycle requires finite native adapters.");
        }

        this.surfaceFactory = surfaceFactory;
        this.interactionDevelopment = interactionDevelopment;
        this.intentPreparation = intentPreparation;
        this.inputForwarding = inputForwarding;
        this.intentConsumption = intentConsumption;
        this.activationSourceFactory = activationSourceFactory;
        windowBridge = windowInspector is null
            ? null
            : new(windowInspector);
        snapshot = new(
            !enabled
                ? ProductDesktopHostLifecycleStatus.DisabledBySafetyPolicy
                : this.userEnabled
                    ? ProductDesktopHostLifecycleStatus.AwaitingHost
                    : ProductDesktopHostLifecycleStatus.DisabledByUser,
            0,
            NativeHostConnected: false,
            OwnedWindowCount: 0);
    }

    public event EventHandler<ProductDesktopHostLifecycleSnapshot>? SnapshotChanged;

    public void BindWorkspaceCreate(
        Func<ProductDesktopWorkspaceCreateRequest, bool> requestCreate)
    {
        ArgumentNullException.ThrowIfNull(requestCreate);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            requestWorkspaceCreate = requestCreate;
        }
    }

    public void BindContainerLayout(
        Func<ProductDesktopContainerLayoutRequest, bool> requestLayout)
    {
        ArgumentNullException.ThrowIfNull(requestLayout);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            requestContainerLayout = requestLayout;
        }
    }

    public void BindContainerHeaderCommand(
        Func<ProductDesktopContainerHeaderCommandRequest, bool> requestCommand)
    {
        ArgumentNullException.ThrowIfNull(requestCommand);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            requestContainerHeaderCommand = requestCommand;
        }
    }

    public void BindContainerMenu(
        Func<string, string, ProductDesktopContainerMenuAvailability>
            availability,
        Func<ProductDesktopContainerMenuRequest, bool> requestMenu)
    {
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(requestMenu);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            containerMenuAvailability = availability;
            requestContainerMenu = requestMenu;
        }
    }

    public void BindItemViewport(
        Func<ProductDesktopItemViewportRequest, bool> requestViewport)
    {
        ArgumentNullException.ThrowIfNull(requestViewport);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            requestItemViewport = requestViewport;
        }
    }

    public void BindItemOpen(
        Func<ProductDesktopItemOpenRequest, ProductDesktopItemOpenResult>
            requestOpen)
    {
        ArgumentNullException.ThrowIfNull(requestOpen);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            requestItemOpen = requestOpen;
        }
    }

    public void BindDesktopSearch(Func<string, bool> requestSearch)
    {
        ArgumentNullException.ThrowIfNull(requestSearch);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            requestDesktopSearch = requestSearch;
        }
    }

    public void BindExplorerReferenceDrop(
        Func<object, string, bool> requestDrop)
    {
        ArgumentNullException.ThrowIfNull(requestDrop);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            requestExplorerReferenceDrop = requestDrop;
        }
    }

    public void BindReferenceReassignment(
        Func<ProductDesktopReferenceReassignmentRequest, bool> request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            requestReferenceReassignment = request;
        }
    }

    public void ApplyItemOpenPolicy(bool singleClickEnabled)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            openItemsWithSingleClick = singleClickEnabled;
            foreach (IProductDesktopHostReadOnlySurface surface in surfaces)
            {
                _ = surface.ApplyItemOpenPolicy(singleClickEnabled);
            }
        }
    }

    public bool ApplyContainerLayoutPreview(
        string displayId,
        string containerId,
        long expectedWorkspaceRevision,
        long expectedTopologyGeneration,
        ProductContainerPlacementState? placement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayId);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        lock (gate)
        {
            if (disposed
                || currentBatch is null
                || currentBatch.WorkspaceRevision != expectedWorkspaceRevision
                || currentBatch.TopologyGeneration != expectedTopologyGeneration
                || surfaces.Count != currentBatch.Displays.Count)
            {
                return false;
            }

            ProductDesktopHostReadOnlyProjection[] sources = currentBatch.Displays
                .Where(display => string.Equals(
                    display.DisplayId,
                    displayId,
                    StringComparison.Ordinal))
                .SelectMany(display => display.Containers)
                .Where(container => string.Equals(
                    container.ContainerId,
                    containerId,
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (sources.Length != 1)
            {
                return false;
            }
            if (placement is null)
            {
                bool cleared = true;
                foreach (IProductDesktopHostReadOnlySurface surface in surfaces)
                {
                    cleared &= surface.ApplyContainerLayoutPreview(
                        containerId,
                        placement: null);
                }
                return cleared;
            }

            int[] targets = currentBatch.Displays
                .Select((display, index) => new { display.DisplayId, Index = index })
                .Where(candidate => string.Equals(
                    candidate.DisplayId,
                    placement.DisplayKey,
                    StringComparison.Ordinal))
                .Select(candidate => candidate.Index)
                .ToArray();
            if (targets.Length != 1)
            {
                return false;
            }
            for (int index = 0; index < surfaces.Count; index++)
            {
                if (index != targets[0]
                    && !surfaces[index].ApplyContainerLayoutPreview(
                        containerId,
                        placement: null))
                {
                    return false;
                }
            }
            return surfaces[targets[0]].ApplyContainerLayoutPreview(
                sources[0],
                placement);
        }
    }

    public bool CanRequestKeyboardInteraction
    {
        get
        {
            lock (gate)
            {
                return !disposed
                    && snapshot.Status
                        == ProductDesktopHostLifecycleStatus.ReadyReadOnly
                    && IsPassiveInteractionAvailableUnsafe()
                    && activationSources.Any(source => source.CanActivate);
            }
        }
    }

    public bool OwnsForegroundActivationSource
    {
        get
        {
            lock (gate)
            {
                return !disposed
                    && activationSources.Any(source =>
                        source.OwnsForegroundWindow
                        && source.ContractAttested);
            }
        }
    }

    public bool RequestKeyboardInteraction()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (snapshot.Status
                    != ProductDesktopHostLifecycleStatus.ReadyReadOnly
                || activationSources.FirstOrDefault(source =>
                    source.CanActivate) is not { } source)
            {
                return false;
            }

            return source.RequestKeyboardInteraction();
        }
    }

    public ProductDesktopHostLifecycleSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                return snapshot;
            }
        }
    }

    public ProductDesktopHostLifecycleSnapshot SetUserEnabled(bool value)
    {
        ProductDesktopHostLifecycleSnapshot? published = null;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!enabled || userEnabled == value)
            {
                return snapshot;
            }

            userEnabled = value;
            ReleaseSurfaceUnsafe();
            published = !value
                ? UpdateSnapshotUnsafe(
                    ProductDesktopHostLifecycleStatus.DisabledByUser,
                    connected: false,
                    ownedWindowCount: 0,
                    workspaceRevision: currentUpdate?.WorkspaceRevision ?? 0,
                    topologyGeneration: currentUpdate?.TopologyGeneration ?? 0)
                : RestoreCurrentUpdateUnsafe();
        }

        Publish(published);
        return published;
    }

    public ProductDesktopHostLifecycleSnapshot ApplyProjectionBatch(
        ProductDesktopHostProjectionBatch? batch)
    {
        ProductDesktopHostLifecycleSnapshot published;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!enabled)
            {
                return snapshot;
            }

            if (!userEnabled)
            {
                currentBatch = batch;
                return snapshot;
            }

            if (batch is not null
                && currentBatch is not null
                && BatchesEqual(currentBatch, batch))
            {
                return snapshot;
            }

            if (batch is null && surfaces.Count == 0)
            {
                return snapshot;
            }

            ReleaseSurfaceUnsafe();
            published = batch is null
                ? UpdateSnapshotUnsafe(
                    ProductDesktopHostLifecycleStatus.AwaitingHost,
                    connected: false,
                    ownedWindowCount: 0)
                : CreateSurfacesUnsafe(batch);
        }

        Publish(published);
        return published;
    }

    public ProductDesktopHostLifecycleSnapshot ApplyProjectionUpdate(
        ProductDesktopHostProjectionUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        ProductDesktopHostLifecycleSnapshot published;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!enabled)
            {
                return snapshot;
            }

            if (!userEnabled)
            {
                if (update.WorkspaceRevision < lastWorkspaceRevision
                    || update.TopologyGeneration < lastTopologyGeneration
                    || (update.WorkspaceRevision == lastWorkspaceRevision
                        && update.TopologyGeneration == lastTopologyGeneration
                        && update.PresentationGeneration
                            < lastPresentationGeneration))
                {
                    return snapshot;
                }

                lastWorkspaceRevision = update.WorkspaceRevision;
                lastTopologyGeneration = update.TopologyGeneration;
                lastPresentationGeneration = update.PresentationGeneration;
                currentUpdate = update;
                currentBatch = update.Batch;
                return snapshot;
            }

            if (update.WorkspaceRevision < lastWorkspaceRevision
                || update.TopologyGeneration < lastTopologyGeneration)
            {
                return snapshot;
            }

            if (update.WorkspaceRevision == lastWorkspaceRevision
                && update.TopologyGeneration == lastTopologyGeneration)
            {
                if (update.PresentationGeneration < lastPresentationGeneration)
                {
                    return snapshot;
                }
                if (UpdatesEqual(currentUpdate, update))
                {
                    return snapshot;
                }

                if (update.PresentationGeneration == lastPresentationGeneration
                    && currentUpdate?.Disposition !=
                    ProductDesktopHostProjectionDisposition.TopologyRefreshing)
                {
                    ReleaseSurfaceUnsafe();
                    currentUpdate = update;
                    published = UpdateSnapshotUnsafe(
                        ProductDesktopHostLifecycleStatus.Faulted,
                        connected: false,
                        ownedWindowCount: 0,
                        workspaceRevision: update.WorkspaceRevision,
                        topologyGeneration: update.TopologyGeneration);
                }
                else
                {
                    published = currentUpdate?.Batch is not null
                        && update.Batch is not null
                        && PresentationStructuresEqual(
                            currentUpdate.Batch,
                            update.Batch)
                            ? ApplyPresentationUpdateUnsafe(update)
                            : ApplyNewUpdateUnsafe(update);
                }
            }
            else
            {
                published = ApplyNewUpdateUnsafe(update);
            }
        }

        Publish(published);
        return published;
    }

    public ProductDesktopHostLifecycleSnapshot ApplySystemSurfaceEvent(
        ProductDesktopInteractionSystemSurfaceEvent systemEvent)
    {
        ArgumentNullException.ThrowIfNull(systemEvent);
        ProductDesktopHostLifecycleSnapshot? published = null;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!enabled
                || !userEnabled
                || interactionDevelopment is null
                || !systemEvent.IsValid
                || systemEvent.Sequence <= lastSystemSurfaceSequence)
            {
                return snapshot;
            }

            lastSystemSurfaceSequence = systemEvent.Sequence;
            if (systemEvent.RequiresHiddenSurface)
            {
                InvalidatePreparedInputUnsafe();
                _ = intentConsumption?.Cancel(
                    systemEvent.ToCancellationSignal(),
                    systemEvent.ObservedAtUtc);
                if (!TryApplyActivationSourcesUnsafe(visible: false))
                {
                    ReleaseSurfaceUnsafe();
                    published = UpdateSnapshotUnsafe(
                        ProductDesktopHostLifecycleStatus.Faulted,
                        connected: false,
                        ownedWindowCount: 0);
                }
                else if (interactionSurface is null)
                {
                    return snapshot;
                }
                else
                {
                    ProductDesktopInteractionDevelopmentSnapshot suspended =
                        interactionDevelopment.SuspendFailClosed(
                            systemEvent.ToCancellationSignal(),
                            systemEvent.ObservedAtUtc);
                    if (suspended.Surface?.IsHiddenContract != true
                        || !suspended.HiddenRequired)
                    {
                        ReleaseSurfaceUnsafe();
                        published = UpdateSnapshotUnsafe(
                            ProductDesktopHostLifecycleStatus.Faulted,
                            connected: false,
                            ownedWindowCount: 0);
                    }
                    else
                    {
                        published = UpdateSnapshotUnsafe(
                            ProductDesktopHostLifecycleStatus
                                .SuspendedSystemSurface,
                            connected: true,
                            ownedWindowCount: surfaces.Count,
                            workspaceRevision: currentBatch?.WorkspaceRevision ?? 0,
                            topologyGeneration: currentBatch?.TopologyGeneration ?? 0,
                            renderedContainerCount: currentBatch?.ContainerCount ?? 0,
                            readOnlyAccessibilityAvailable: true,
                            passiveWindowContractAttested: false,
                            lastSystemSurfaceEvent: systemEvent.Kind);
                    }
                }
            }
            else if (snapshot.Status
                    == ProductDesktopHostLifecycleStatus.SuspendedSystemSurface
                && currentBatch is not null
                && interactionSurface is not null
                && interactionDevelopment.Snapshot.Surface is
                { WindowRegistryGeneration: > 0 } hidden)
            {
                ProductDesktopInteractionDevelopmentSnapshot resumed =
                    interactionDevelopment.TryResumePassive(
                        CreateInteractionEvidence(
                            currentBatch,
                            hidden.WindowRegistryGeneration));
                if (resumed.IsDevelopmentInteractionAvailable)
                {
                    if (!TryApplyActivationSourcesUnsafe(visible: true))
                    {
                        ReleaseSurfaceUnsafe();
                        published = UpdateSnapshotUnsafe(
                            ProductDesktopHostLifecycleStatus.Faulted,
                            connected: false,
                            ownedWindowCount: 0);
                    }
                    else
                    {
                        published = UpdateSnapshotUnsafe(
                            ProductDesktopHostLifecycleStatus.ReadyReadOnly,
                            connected: true,
                            ownedWindowCount: surfaces.Count,
                            workspaceRevision: currentBatch.WorkspaceRevision,
                            topologyGeneration: currentBatch.TopologyGeneration,
                            renderedContainerCount: currentBatch.ContainerCount,
                            readOnlyAccessibilityAvailable: true,
                            passiveWindowContractAttested: true,
                            lastSystemSurfaceEvent: systemEvent.Kind);
                    }
                }
            }
        }

        if (published is not null)
        {
            Publish(published);
        }

        return published ?? snapshot;
    }

    public ProductDesktopInteractionIntentPreparationResult
        PrepareInteractionIntent(
            ProductDesktopInteractionIntentPreparationRequest request,
            DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (intentPreparation is null)
            {
                return new(
                    new(
                        ProductDesktopInteractionIntentPreparationStatus
                            .DisabledBySafetyPolicy,
                        0,
                        0,
                        PreparedIntentAvailable: false,
                        ExplicitInteractionEntered: false,
                        RealFileOperationsAllowed: false),
                    PreparedIntent: null);
            }

            if (!TryCreatePassiveInteractionEvidenceUnsafe(out var evidence))
            {
                ProductDesktopInteractionIntentPreparationSnapshot awaiting =
                    intentPreparation.AwaitPassiveSurface();
                return new(awaiting, PreparedIntent: null);
            }

            return intentPreparation.Prepare(
                request,
                currentBatch!,
                evidence,
                nowUtc);
        }
    }

    public ProductDesktopInteractionInputForwardingResult
        ForwardInteractionInput(
            ProductDesktopInteractionForwardedInput input,
            DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(input);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (inputForwarding is null)
            {
                return new(
                    new(
                        ProductDesktopInteractionInputForwardingStatus
                            .DisabledBySafetyPolicy,
                        0,
                        0,
                        PreparedIntentAvailable: false,
                        CapturesGlobalInput: false,
                        SendsSyntheticInput: false,
                        ExplicitInteractionEntered: false,
                        RealFileOperationsAllowed: false),
                    PreparedIntent: null);
            }

            if (!TryCreatePassiveInteractionEvidenceUnsafe(out var evidence))
            {
                ProductDesktopInteractionInputForwardingSnapshot awaiting =
                    inputForwarding.AwaitPassiveSurface();
                return new(awaiting, PreparedIntent: null);
            }

            return inputForwarding.Forward(
                input,
                currentBatch!,
                evidence,
                nowUtc);
        }
    }

    public ProductDesktopInteractionIntentConsumptionResult
        ConsumePreparedInteractionIntent(
            ProductDesktopInteractionPreparedIntent preparedIntent,
            DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(preparedIntent);
        ProductDesktopInteractionIntentConsumptionResult result;
        ProductDesktopHostLifecycleSnapshot? published = null;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (intentConsumption is null
                || currentBatch is null
                || interactionSurface is null
                || snapshot.Status
                    != ProductDesktopHostLifecycleStatus.ReadyReadOnly)
            {
                return ProductDesktopInteractionIntentConsumptionResult
                    .Disabled;
            }

            if (!TryCreatePassiveInteractionEvidenceUnsafe(out var evidence))
            {
                result = intentConsumption.AwaitPassiveSurface();
                published = RefreshInteractionObservationUnsafe();
            }
            else
            {
                ProductDesktopHostReadOnlyProjection? target =
                    currentBatch.Displays
                        .SelectMany(display => display.Containers)
                        .SingleOrDefault(container => string.Equals(
                            container.ContainerId,
                            preparedIntent.Intent.TargetContainerId,
                            StringComparison.Ordinal));
                result = target is null
                    ? intentConsumption.RejectUnavailableTarget()
                    : intentConsumption.Consume(
                        preparedIntent,
                        evidence,
                        target.ItemIds,
                        nowUtc);
                published = RefreshInteractionObservationUnsafe();
            }
        }

        if (published is not null)
        {
            Publish(published);
        }
        return result;
    }

    public ProductDesktopInteractionIntentConsumptionResult
        ApplyInteractionSelection(
            ProductDesktopSelectionRequest request,
            DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ProductDesktopInteractionIntentConsumptionResult result;
        ProductDesktopHostLifecycleSnapshot? published = null;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (intentConsumption is null || currentBatch is null)
            {
                return ProductDesktopInteractionIntentConsumptionResult
                    .Disabled;
            }

            string? targetId = intentConsumption.Snapshot.Transaction
                ?.Admission.Lease?.TargetContainerId;
            ProductDesktopHostReadOnlyProjection? target = currentBatch.Displays
                .SelectMany(display => display.Containers)
                .SingleOrDefault(container => string.Equals(
                    container.ContainerId,
                    targetId,
                    StringComparison.Ordinal));
            result = target is null
                ? intentConsumption.RejectUnavailableTarget()
                : intentConsumption.ApplySelection(request, target.ItemIds, nowUtc);
            published = RefreshInteractionObservationUnsafe();
        }

        if (published is not null)
        {
            Publish(published);
        }
        return result;
    }

    private ProductDesktopHostLifecycleSnapshot ApplyNewUpdateUnsafe(
        ProductDesktopHostProjectionUpdate update)
    {
        lastWorkspaceRevision = update.WorkspaceRevision;
        lastTopologyGeneration = update.TopologyGeneration;
        lastPresentationGeneration = update.PresentationGeneration;
        ReleaseSurfaceUnsafe();
        currentUpdate = update;
        if (update.Disposition is ProductDesktopHostProjectionDisposition.Ready
            or ProductDesktopHostProjectionDisposition.EmptyWorkspace)
        {
            return CreateSurfacesUnsafe(
                update.Batch!,
                update.Disposition ==
                    ProductDesktopHostProjectionDisposition.EmptyWorkspace
                    ? ProductDesktopHostLifecycleStatus.AwaitingWorkspace
                    : ProductDesktopHostLifecycleStatus.ReadyReadOnly);
        }

        ProductDesktopHostLifecycleStatus status = update.Disposition ==
            ProductDesktopHostProjectionDisposition.EmptyWorkspace
                ? ProductDesktopHostLifecycleStatus.AwaitingWorkspace
                : ProductDesktopHostLifecycleStatus.SuspendedUnsafeTopology;
        return UpdateSnapshotUnsafe(
            status,
            connected: false,
            ownedWindowCount: 0,
            workspaceRevision: update.WorkspaceRevision,
            topologyGeneration: update.TopologyGeneration);
    }

    private ProductDesktopHostLifecycleSnapshot ApplyPresentationUpdateUnsafe(
        ProductDesktopHostProjectionUpdate update)
    {
        ProductDesktopHostProjectionBatch batch = update.Batch!;
        if (surfaces.Count != batch.Displays.Count)
        {
            return ApplyNewUpdateUnsafe(update);
        }
        for (int index = 0; index < surfaces.Count; index++)
        {
            if (!surfaces[index].ApplyPresentation(batch.Displays[index]))
            {
                ReleaseSurfaceUnsafe();
                currentUpdate = update;
                return UpdateSnapshotUnsafe(
                    ProductDesktopHostLifecycleStatus.Faulted,
                    connected: false,
                    ownedWindowCount: 0,
                    workspaceRevision: update.WorkspaceRevision,
                    topologyGeneration: update.TopologyGeneration);
            }
        }
        ProductDesktopInteractionSurfaceTransactionSnapshot? transaction =
            intentConsumption?.Snapshot.Transaction;
        if (transaction?.IsExplicit == true
            && transaction.Selection is { } activeSelection)
        {
            ProductDesktopHostReadOnlyProjection? previousContainer =
                currentBatch?.Displays
                    .SelectMany(display => display.Containers)
                    .SingleOrDefault(container => string.Equals(
                        container.ContainerId,
                        activeSelection.ContainerId,
                        StringComparison.Ordinal));
            ProductDesktopHostReadOnlyProjection? activeContainer = batch.Displays
                .SelectMany(display => display.Containers)
                .SingleOrDefault(container => string.Equals(
                    container.ContainerId,
                    activeSelection.ContainerId,
                    StringComparison.Ordinal));
            if (activeContainer is null)
            {
                return ApplyNewUpdateUnsafe(update);
            }

            ProductDesktopInteractionIntentConsumptionResult reconciled =
                intentConsumption!.ReconcileVisibleItems(
                    activeContainer.ItemIds,
                    DateTimeOffset.UtcNow);
            if (!reconciled.IsExplicit
                || reconciled.Snapshot.Transaction?.Selection?.Status
                    != ProductDesktopSelectionStatus.Reconciled)
            {
                return ApplyNewUpdateUnsafe(update);
            }
            int previousFocusOffset = activeSelection.FocusedItemId is { } focused
                ? activeSelection.VisibleItemIds
                    .Select((itemId, index) => new { itemId, index })
                    .Where(candidate => string.Equals(
                        candidate.itemId, focused, StringComparison.Ordinal))
                    .Select(candidate => candidate.index)
                    .SingleOrDefault(-1)
                : -1;
            if (previousContainer is not null
                && previousFocusOffset >= 0
                && previousContainer.VisibleItemStartOrdinal
                    != activeContainer.VisibleItemStartOrdinal
                && activeContainer.ItemIds.Count > 0)
            {
                string targetItemId = activeContainer.ItemIds[
                    Math.Min(previousFocusOffset,
                        activeContainer.ItemIds.Count - 1)];
                ProductDesktopInteractionIntentConsumptionResult focusedResult =
                    intentConsumption.ApplySelection(
                        new(
                            ProductDesktopSelectionAction.SelectItem,
                            ProductDesktopSelectionModifiers.None,
                            targetItemId),
                        activeContainer.ItemIds,
                        DateTimeOffset.UtcNow);
                if (!focusedResult.IsExplicit
                    || focusedResult.Snapshot.Transaction?.Selection?.Status
                        != ProductDesktopSelectionStatus.Applied)
                {
                    return ApplyNewUpdateUnsafe(update);
                }
            }
            foreach (IProductDesktopHostReadOnlySurface surface in surfaces)
            {
                surface.RefreshSelection();
            }
        }
        lastPresentationGeneration = update.PresentationGeneration;
        currentUpdate = update;
        currentBatch = batch;
        ProductDesktopSelectionSnapshot? finalSelection =
            intentConsumption?.Snapshot.Transaction?.Selection;
        snapshot = snapshot with
        {
            Generation = checked(snapshot.Generation + 1),
            WorkspaceRevision = update.WorkspaceRevision,
            TopologyGeneration = update.TopologyGeneration,
            RenderedContainerCount = batch.ContainerCount,
            SelectedItemCount = finalSelection?.SelectedItemIds.Count
                ?? snapshot.SelectedItemCount,
            FocusedItemAvailable = finalSelection?.FocusedItemId is not null
                || (finalSelection is null && snapshot.FocusedItemAvailable),
            SelectionRevision = finalSelection?.SelectionRevision
                ?? snapshot.SelectionRevision,
        };
        return snapshot;
    }

    private ProductDesktopHostLifecycleSnapshot RestoreCurrentUpdateUnsafe()
    {
        if (currentUpdate is null)
        {
            return UpdateSnapshotUnsafe(
                ProductDesktopHostLifecycleStatus.AwaitingHost,
                connected: false,
                ownedWindowCount: 0);
        }

        if (currentUpdate.Disposition is
            ProductDesktopHostProjectionDisposition.Ready
            or ProductDesktopHostProjectionDisposition.EmptyWorkspace)
        {
            return CreateSurfacesUnsafe(
                currentUpdate.Batch!,
                currentUpdate.Disposition ==
                    ProductDesktopHostProjectionDisposition.EmptyWorkspace
                    ? ProductDesktopHostLifecycleStatus.AwaitingWorkspace
                    : ProductDesktopHostLifecycleStatus.ReadyReadOnly);
        }

        ProductDesktopHostLifecycleStatus status = currentUpdate.Disposition ==
            ProductDesktopHostProjectionDisposition.EmptyWorkspace
                ? ProductDesktopHostLifecycleStatus.AwaitingWorkspace
                : ProductDesktopHostLifecycleStatus.SuspendedUnsafeTopology;
        return UpdateSnapshotUnsafe(
            status,
            connected: false,
            ownedWindowCount: 0,
            workspaceRevision: currentUpdate.WorkspaceRevision,
            topologyGeneration: currentUpdate.TopologyGeneration);
    }

    public ValueTask DisposeAsync()
    {
        ProductDesktopHostLifecycleSnapshot? published = null;
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }

            disposed = true;
            if (inputForwarding?.IsEnabled == true)
            {
                _ = inputForwarding.Complete();
            }
            else
            {
                _ = intentPreparation?.Complete();
            }
            _ = intentConsumption?.Complete(DateTimeOffset.UtcNow);
            ReleaseSurfaceUnsafe();
            published = UpdateSnapshotUnsafe(
                ProductDesktopHostLifecycleStatus.Completed,
                connected: false,
                ownedWindowCount: 0);
        }

        Publish(published);
        return ValueTask.CompletedTask;
    }

    private ProductDesktopHostLifecycleSnapshot CreateSurfacesUnsafe(
        ProductDesktopHostProjectionBatch batch,
        ProductDesktopHostLifecycleStatus readyStatus =
            ProductDesktopHostLifecycleStatus.ReadyReadOnly)
    {
        try
        {
            bool controlledSurfaceLifecycle =
                interactionDevelopment?.CanAttachNativeSurface == true;
            ProductDesktopHostIdentity? identity = null;
            ProductDesktopHostWindowRegistrationResult? registration = null;
            foreach (ProductDesktopHostDisplayProjection display in batch.Displays)
            {
                IProductDesktopHostReadOnlySurface created =
                    surfaceFactory!.Create(
                        display,
                        NextInstanceMarker(),
                        startHidden: controlledSurfaceLifecycle);
                surfaces.Add(created);
                created.BindSelection(
                    CaptureInteractionTransaction,
                    (containerId, request) => ApplySelectionFromSurface(
                        created,
                        display,
                        containerId,
                        request));
                created.BindWorkspaceCreate(input =>
                    requestWorkspaceCreate(new(
                        input.Kind,
                        display.DisplayId,
                        batch.WorkspaceRevision,
                        batch.TopologyGeneration,
                        input.SourceAttested,
                        input.IsInjected,
                        input.IsAutoRepeat,
                        input.RequestedBoundsPixels)));
                created.BindContainerLayout(input =>
                    requestContainerLayout(new(
                        input.Phase,
                        input.Kind,
                        input.ContainerId,
                        display.DisplayId,
                        batch.WorkspaceRevision,
                        batch.TopologyGeneration,
                        input.CumulativeDeltaXDip,
                        input.CumulativeDeltaYDip,
                        input.SnapEnabled,
                        input.ShiftPressed,
                        input.CancellationReason,
                        input.PointerScreenX,
                        input.PointerScreenY)));
                created.BindContainerHeaderCommand(input =>
                    ApplyContainerHeaderCommandFromSurface(
                        created,
                        display,
                        input));
                created.BindItemViewport(input =>
                    ApplyItemViewportFromSurface(
                        created,
                        display,
                        input));
                created.BindItemOpen(input => ApplyItemOpenFromSurface(
                    created,
                    display,
                    input));
                created.BindExplorerReferenceDrop((dataObject, containerId) =>
                    requestExplorerReferenceDrop(dataObject, containerId));
                created.BindReferenceReassignment(input =>
                    ApplyReferenceReassignmentFromSurface(
                        created,
                        display,
                        input));
                if (!created.ApplyItemOpenPolicy(openItemsWithSingleClick))
                {
                    throw new InvalidOperationException(
                        "Every display surface must accept the item-open policy.");
                }
                if (!created.ReadOnlyAccessibilityAttested
                    || (controlledSurfaceLifecycle
                        ? !created.HiddenWindowContractAttested
                        : !created.PassiveWindowContractAttested))
                {
                    throw new InvalidOperationException(
                        "Every display surface must attest read-only UIA and passive window behavior.");
                }
                identity ??= new(
                    hostInstanceId,
                    batch.TopologyGeneration,
                    created.ProcessId,
                    created.ThreadId);
                if (created.ProcessId != identity.ProcessId
                    || created.ThreadId != identity.ThreadId)
                {
                    throw new InvalidOperationException(
                        "Every display surface must share one host thread.");
                }

                if (surfaces.Count == 1)
                {
                    windowBridge!.Connect(identity);
                }

                long nextWindowGeneration = checked(windowGeneration + 1);
                registration = windowBridge!.Register(
                    new(
                        DisplayRegistrationId(display.DisplayId),
                        identity,
                        nextWindowGeneration,
                        created.Handle,
                        created.InstanceMarker));
                if (!registration.IsRegistered
                    || !registration.Snapshot.OwnershipAttested)
                {
                    ReleaseSurfaceUnsafe();
                    return UpdateSnapshotUnsafe(
                        ProductDesktopHostLifecycleStatus.Faulted,
                        connected: false,
                        ownedWindowCount: 0);
                }

                windowGeneration = nextWindowGeneration;
                registrations.Add((display.DisplayId, nextWindowGeneration));
            }

            currentBatch = batch;
            bool interactionAttached = false;
            if (controlledSurfaceLifecycle)
            {
                interactionSurface = new(
                    surfaces.AsReadOnly(),
                    registration!.Snapshot.Generation);
                ProductDesktopInteractionDevelopmentSnapshot attached =
                    interactionDevelopment!.AttachPassiveSurface(
                        interactionSurface,
                        CreateInteractionEvidence(
                            batch,
                            registration.Snapshot.Generation));
                interactionAttached = attached.IsDevelopmentInteractionAvailable;
                if (interactionAttached && intentConsumption?.IsEnabled == true)
                {
                    interactionAttached = intentConsumption.AttachSurface(
                        interactionSurface);
                }
                if (!interactionAttached)
                {
                    ReleaseSurfaceUnsafe();
                    return UpdateSnapshotUnsafe(
                        ProductDesktopHostLifecycleStatus.Faulted,
                        connected: false,
                        ownedWindowCount: 0);
                }

                if (inputForwarding?.IsEnabled == true
                    && intentConsumption?.IsEnabled == true)
                {
                    if (activationSourceFactory is null)
                    {
                        ReleaseSurfaceUnsafe();
                        return UpdateSnapshotUnsafe(
                            ProductDesktopHostLifecycleStatus.Faulted,
                            connected: false,
                            ownedWindowCount: 0);
                    }

                    foreach (ProductDesktopHostDisplayProjection display
                        in batch.Displays.Where(candidate =>
                            candidate.Containers.Count > 0))
                    {
                        IProductDesktopInteractionActivationSource source =
                            activationSourceFactory.Create(
                                display,
                                NextInstanceMarker(),
                                HandleActivationInput);
                        activationSources.Add(source);
                        source.BindSelection(
                            CaptureInteractionTransaction,
                            request => ApplySelectionFromActivationSource(
                                source,
                                display,
                                request),
                            () => CancelInteractionFromActivationSource(source));
                        source.BindItemOpen(input =>
                            ApplyItemOpenFromActivationSource(
                                source,
                                display,
                                input));
                        source.BindSearch(displayId =>
                            requestDesktopSearch(displayId));
                        source.BindItemViewport(input =>
                            ApplyItemViewportFromActivationSource(
                                source,
                                display,
                                input));
                        source.BindContainerLayout(
                            command => ApplyContainerLayoutFromActivationSource(
                                source,
                                display,
                                command),
                            containerId =>
                                ApplyContainerLayoutKeyboardFocusFromActivationSource(
                                    source,
                                    display,
                                    containerId));
                        source.BindContainerHeaderCommand(input =>
                            ApplyContainerHeaderCommandFromActivationSource(
                                source,
                                display,
                                input));
                        source.BindContainerMenu(
                            containerId =>
                                GetContainerMenuAvailabilityFromActivationSource(
                                    source,
                                    display,
                                    containerId),
                            input => ApplyContainerMenuFromActivationSource(
                                source,
                                display,
                                input));
                        if (!source.IsVisible || !source.ContractAttested)
                        {
                            ReleaseSurfaceUnsafe();
                            return UpdateSnapshotUnsafe(
                                ProductDesktopHostLifecycleStatus.Faulted,
                                connected: false,
                                ownedWindowCount: 0);
                        }
                    }
                }
            }

            return UpdateSnapshotUnsafe(
                readyStatus,
                connected: true,
                ownedWindowCount: registration!.Snapshot.VerifiedWindowCount,
                workspaceRevision: batch.WorkspaceRevision,
                topologyGeneration: batch.TopologyGeneration,
                renderedContainerCount: batch.ContainerCount,
                readOnlyAccessibilityAvailable: true,
                passiveWindowContractAttested: true);
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or ArgumentException
                or InvalidOperationException
                or PlatformNotSupportedException
                or OverflowException)
        {
            ReleaseSurfaceUnsafe();
            return UpdateSnapshotUnsafe(
                ProductDesktopHostLifecycleStatus.Faulted,
                connected: false,
                ownedWindowCount: 0);
        }
    }

    private void ReleaseSurfaceUnsafe()
    {
        InvalidatePreparedInputUnsafe();
        foreach (IProductDesktopInteractionActivationSource source
            in activationSources)
        {
            source.Dispose();
        }
        activationSources.Clear();

        if (interactionSurface is not null)
        {
            _ = intentConsumption?.DetachSurface(
                interactionSurface,
                DateTimeOffset.UtcNow);
            _ = interactionDevelopment?.DetachPassiveSurface(
                interactionSurface);
            interactionSurface = null;
        }

        if (windowBridge is not null)
        {
            foreach ((string displayId, long generation) in registrations)
            {
                _ = windowBridge.Unregister(
                    DisplayRegistrationId(displayId),
                    generation);
            }
        }

        foreach (IProductDesktopHostReadOnlySurface surface in surfaces)
        {
            surface.Dispose();
        }

        surfaces.Clear();
        registrations.Clear();
        currentBatch = null;
        windowBridge?.Disconnect(hostInstanceId);
    }

    private bool HandleActivationInput(
        ProductDesktopInteractionForwardedInput input)
    {
        ProductDesktopInteractionInputForwardingResult forwarded =
            ForwardInteractionInput(input, DateTimeOffset.UtcNow);
        bool entered = forwarded.IsPrepared
            && ConsumePreparedInteractionIntent(
                forwarded.PreparedIntent!,
                DateTimeOffset.UtcNow).IsExplicit;
        if (entered)
        {
            lock (gate)
            {
                foreach (IProductDesktopHostReadOnlySurface surface in surfaces)
                {
                    surface.RefreshSelection();
                }
            }
        }
        return entered;
    }

    private bool ApplyContainerLayoutFromActivationSource(
        IProductDesktopInteractionActivationSource source,
        ProductDesktopHostDisplayProjection display,
        ProductDesktopContainerLayoutKeyboardCommand command)
    {
        lock (gate)
        {
            ProductDesktopInteractionSurfaceTransactionSnapshot? transaction =
                intentConsumption?.Snapshot.Transaction;
            ProductDesktopHostReadOnlyProjection[] targets = display.Containers
                .Where(container => string.Equals(
                    container.ContainerId,
                    command.ContainerId,
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (disposed
                || currentBatch is null
                || !activationSources.Contains(source)
                || !string.Equals(source.DisplayId, display.DisplayId,
                    StringComparison.Ordinal)
                || transaction?.IsExplicit != true
                || !string.Equals(
                    transaction.Selection?.ContainerId,
                    command.ContainerId,
                    StringComparison.Ordinal)
                || targets.Length != 1
                || targets[0].IsLocked
                || !double.IsFinite(command.DeltaXDip)
                || !double.IsFinite(command.DeltaYDip)
                || (command.DeltaXDip == 0 && command.DeltaYDip == 0))
            {
                return false;
            }

            var begin = new ProductDesktopContainerLayoutRequest(
                ProductDesktopContainerLayoutInputPhase.Begin,
                command.Kind,
                command.ContainerId,
                display.DisplayId,
                currentBatch.WorkspaceRevision,
                currentBatch.TopologyGeneration,
                0,
                0,
                SnapEnabled: command.ShiftPressed,
                command.ShiftPressed,
                ProductDesktopContainerLayoutCancellationReason.None);
            if (!TryRequestContainerLayoutUnsafe(begin))
            {
                return false;
            }

            ProductDesktopContainerLayoutRequest update = begin with
            {
                Phase = ProductDesktopContainerLayoutInputPhase.Update,
                CumulativeDeltaXDip = command.DeltaXDip,
                CumulativeDeltaYDip = command.DeltaYDip,
            };
            if (!TryRequestContainerLayoutUnsafe(update))
            {
                _ = TryRequestContainerLayoutUnsafe(begin with
                {
                    Phase = ProductDesktopContainerLayoutInputPhase.Cancel,
                    CancellationReason =
                        ProductDesktopContainerLayoutCancellationReason
                            .HostInvalidated,
                });
                return false;
            }

            if (TryRequestContainerLayoutUnsafe(update with
            {
                Phase = ProductDesktopContainerLayoutInputPhase.Complete,
            }))
            {
                return true;
            }

            _ = TryRequestContainerLayoutUnsafe(begin with
            {
                Phase = ProductDesktopContainerLayoutInputPhase.Cancel,
                CancellationReason =
                    ProductDesktopContainerLayoutCancellationReason
                        .HostInvalidated,
            });
            return false;
        }
    }

    private bool ApplyContainerHeaderCommandFromActivationSource(
        IProductDesktopInteractionActivationSource source,
        ProductDesktopHostDisplayProjection display,
        ProductDesktopContainerHeaderSurfaceInput input)
    {
        lock (gate)
        {
            ProductDesktopHostReadOnlyProjection[] targets = display.Containers
                .Where(container => string.Equals(
                    container.ContainerId,
                    input.ContainerId,
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (disposed
                || currentBatch is null
                || !activationSources.Contains(source)
                || !string.Equals(
                    source.DisplayId,
                    display.DisplayId,
                    StringComparison.Ordinal)
                || targets.Length != 1
                || !input.SourceAttested
                || input.IsInjected
                || input.IsAutoRepeat
                || (input.Kind ==
                        ProductDesktopContainerHeaderCommandKind.ToggleCollapsed
                    && targets[0].IsLocked))
            {
                return false;
            }

            try
            {
                return requestContainerHeaderCommand(new(
                    input.Kind,
                    input.ContainerId,
                    display.DisplayId,
                    currentBatch.WorkspaceRevision,
                    currentBatch.TopologyGeneration,
                    input.SourceAttested,
                    input.IsInjected,
                    input.IsAutoRepeat));
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or InvalidOperationException
                    or OverflowException)
            {
                return false;
            }
        }
    }

    private bool ApplyContainerHeaderCommandFromSurface(
        IProductDesktopHostReadOnlySurface surface,
        ProductDesktopHostDisplayProjection display,
        ProductDesktopContainerHeaderSurfaceInput input)
    {
        lock (gate)
        {
            ProductDesktopHostReadOnlyProjection[] targets = display.Containers
                .Where(container => string.Equals(
                    container.ContainerId,
                    input.ContainerId,
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (disposed
                || currentBatch is null
                || !surfaces.Contains(surface)
                || targets.Length != 1
                || input.Kind !=
                    ProductDesktopContainerHeaderCommandKind.ToggleCollapsed
                || targets[0].TitleDoubleClickAction !=
                    ProductContainerTitleDoubleClickAction.ToggleCollapsed
                || targets[0].IsLocked
                || !input.SourceAttested
                || input.IsInjected
                || input.IsAutoRepeat)
            {
                return false;
            }

            try
            {
                return requestContainerHeaderCommand(new(
                    input.Kind,
                    input.ContainerId,
                    display.DisplayId,
                    currentBatch.WorkspaceRevision,
                    currentBatch.TopologyGeneration,
                    input.SourceAttested,
                    input.IsInjected,
                    input.IsAutoRepeat));
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or InvalidOperationException
                    or OverflowException)
            {
                return false;
            }
        }
    }

    private bool ApplyItemViewportFromSurface(
        IProductDesktopHostReadOnlySurface surface,
        ProductDesktopHostDisplayProjection display,
        ProductDesktopItemViewportSurfaceInput input)
    {
        lock (gate)
        {
            ProductDesktopHostReadOnlyProjection[] targets = display.Containers
                .Where(container => string.Equals(
                    container.ContainerId,
                    input.ContainerId,
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (disposed
                || currentBatch is null
                || !surfaces.Contains(surface)
                || targets.Length != 1
                || targets[0].TotalItemCount <=
                    ProductDesktopHostReadOnlyProjection.VisibleItemCapacity(
                        targets[0].ContentDensity)
                || input.WheelDelta == 0
                || !input.SourceAttested
                || input.IsInjected
                || input.IsAutoRepeat)
            {
                return false;
            }
            try
            {
                return requestItemViewport(new(
                    input.ContainerId,
                    display.DisplayId,
                    currentBatch.WorkspaceRevision,
                    currentBatch.TopologyGeneration,
                    input.WheelDelta,
                    input.SourceAttested,
                    input.IsInjected,
                    input.IsAutoRepeat,
                    input.PageNavigation));
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or InvalidOperationException
                    or OverflowException)
            {
                return false;
            }
        }
    }

    private bool ApplyItemViewportFromActivationSource(
        IProductDesktopInteractionActivationSource source,
        ProductDesktopHostDisplayProjection display,
        ProductDesktopItemViewportSurfaceInput input)
    {
        lock (gate)
        {
            ProductDesktopInteractionSurfaceTransactionSnapshot? transaction =
                intentConsumption?.Snapshot.Transaction;
            ProductDesktopHostReadOnlyProjection[] targets = display.Containers
                .Where(container => string.Equals(
                    container.ContainerId,
                    input.ContainerId,
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (disposed
                || currentBatch is null
                || !activationSources.Contains(source)
                || !string.Equals(source.DisplayId, display.DisplayId,
                    StringComparison.Ordinal)
                || transaction?.IsExplicit != true
                || !string.Equals(transaction.Selection?.ContainerId,
                    input.ContainerId, StringComparison.Ordinal)
                || targets.Length != 1
                || targets[0].TotalItemCount <=
                    ProductDesktopHostReadOnlyProjection.VisibleItemCapacity(
                        targets[0].ContentDensity)
                || input.WheelDelta == 0
                || !input.SourceAttested
                || input.IsInjected
                || input.IsAutoRepeat)
            {
                return false;
            }
            try
            {
                return requestItemViewport(new(
                    input.ContainerId,
                    display.DisplayId,
                    currentBatch.WorkspaceRevision,
                    currentBatch.TopologyGeneration,
                    input.WheelDelta,
                    input.SourceAttested,
                    input.IsInjected,
                    input.IsAutoRepeat,
                    input.PageNavigation));
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or InvalidOperationException
                    or OverflowException)
            {
                return false;
            }
        }
    }

    private bool ApplyItemOpenFromSurface(
        IProductDesktopHostReadOnlySurface surface,
        ProductDesktopHostDisplayProjection display,
        ProductDesktopItemOpenSurfaceInput input)
    {
        lock (gate)
        {
            if (disposed || !surfaces.Contains(surface))
            {
                return false;
            }
            return ApplyItemOpenUnsafe(display, input);
        }
    }

    private bool ApplyItemOpenFromActivationSource(
        IProductDesktopInteractionActivationSource source,
        ProductDesktopHostDisplayProjection display,
        ProductDesktopItemOpenSurfaceInput input)
    {
        lock (gate)
        {
            if (disposed
                || !activationSources.Contains(source)
                || !string.Equals(
                    source.DisplayId,
                    display.DisplayId,
                    StringComparison.Ordinal))
            {
                return false;
            }
            return ApplyItemOpenUnsafe(display, input);
        }
    }

    private bool ApplyItemOpenUnsafe(
        ProductDesktopHostDisplayProjection display,
        ProductDesktopItemOpenSurfaceInput input)
    {
        ProductDesktopInteractionSurfaceTransactionSnapshot? transaction =
            intentConsumption?.Snapshot.Transaction;
        if (currentBatch is null
            || transaction?.IsExplicit != true
            || transaction.Selection is not { } activeSelection
            || !string.Equals(
                activeSelection.ContainerId,
                input.ContainerId,
                StringComparison.Ordinal)
            || !activeSelection.VisibleItemIds.Contains(
                input.ItemId,
                StringComparer.Ordinal)
            || !display.Containers.Any(container =>
                string.Equals(
                    container.ContainerId,
                    input.ContainerId,
                    StringComparison.Ordinal)
                && container.ItemIds.Contains(
                    input.ItemId,
                    StringComparer.Ordinal)))
        {
            return false;
        }
        try
        {
            ProductDesktopItemOpenResult result = requestItemOpen(new(
                input.ContainerId,
                display.DisplayId,
                currentBatch.WorkspaceRevision,
                currentBatch.TopologyGeneration,
                input.ItemId,
                input.Source,
                input.SourceAttested,
                input.IsInjected,
                input.IsAutoRepeat));
            PublishItemOpenFeedbackUnsafe(display, input, result);
            return result.IsAccepted;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            return false;
        }
    }

    private void PublishItemOpenFeedbackUnsafe(
        ProductDesktopHostDisplayProjection display,
        ProductDesktopItemOpenSurfaceInput input,
        ProductDesktopItemOpenResult result)
    {
        if (currentBatch is null)
        {
            return;
        }
        int displayIndex = -1;
        for (int index = 0; index < currentBatch.Displays.Count; index++)
        {
            if (ReferenceEquals(currentBatch.Displays[index], display)
                || string.Equals(
                    currentBatch.Displays[index].DisplayId,
                    display.DisplayId,
                    StringComparison.Ordinal))
            {
                displayIndex = index;
                break;
            }
        }
        if (displayIndex < 0 || displayIndex >= surfaces.Count)
        {
            return;
        }
        _ = surfaces[displayIndex].ApplyItemOpenFeedback(new(
            input.ContainerId,
            input.ItemId,
            result.Status,
            ItemOpenFeedbackMessage(result),
            result.CanRetry,
            result.CanLocateInExplorer));
    }

    private static string ItemOpenFeedbackMessage(
        ProductDesktopItemOpenResult result) =>
        (result.CanRetry, result.CanLocateInExplorer) switch
        {
            (true, true) => $"{result.UserMessage}；右键可重试或定位",
            (true, false) => $"{result.UserMessage}；右键可重试",
            (false, true) => $"{result.UserMessage}；右键可定位",
            _ => result.UserMessage,
        };

    private ProductDesktopContainerMenuAvailability
        GetContainerMenuAvailabilityFromActivationSource(
            IProductDesktopInteractionActivationSource source,
            ProductDesktopHostDisplayProjection display,
            string containerId)
    {
        lock (gate)
        {
            if (disposed
                || currentBatch is null
                || !activationSources.Contains(source)
                || !string.Equals(
                    source.DisplayId,
                    display.DisplayId,
                    StringComparison.Ordinal)
                || display.Containers.Count(container => string.Equals(
                    container.ContainerId,
                    containerId,
                    StringComparison.Ordinal)) != 1)
            {
                return ProductDesktopContainerMenuAvailability.Unavailable;
            }

            try
            {
                return containerMenuAvailability(
                    containerId,
                    display.DisplayId);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or InvalidOperationException
                    or OverflowException)
            {
                return ProductDesktopContainerMenuAvailability.Unavailable;
            }
        }
    }

    private bool ApplyContainerMenuFromActivationSource(
        IProductDesktopInteractionActivationSource source,
        ProductDesktopHostDisplayProjection display,
        ProductDesktopContainerMenuSurfaceInput input)
    {
        lock (gate)
        {
            ProductDesktopContainerMenuAvailability availability =
                GetContainerMenuAvailabilityFromActivationSource(
                    source,
                    display,
                    input.ContainerId);
            bool actionAvailable = input.Action switch
            {
                ProductDesktopContainerMenuAction.OpenRename =>
                    availability.CanOpenRename,
                ProductDesktopContainerMenuAction.OpenAppearance =>
                    availability.CanOpenAppearance,
                ProductDesktopContainerMenuAction.OpenSort =>
                    availability.CanOpenSort,
                ProductDesktopContainerMenuAction.DeleteContainerConfiguration =>
                    availability.CanDeleteContainerConfiguration,
                _ => false,
            };
            if (currentBatch is null
                || !actionAvailable
                || !input.SourceAttested
                || input.IsInjected
                || input.IsAutoRepeat)
            {
                return false;
            }

            try
            {
                return requestContainerMenu(new(
                    input.Action,
                    input.ContainerId,
                    display.DisplayId,
                    currentBatch.WorkspaceRevision,
                    currentBatch.TopologyGeneration,
                    input.SourceAttested,
                    input.IsInjected,
                    input.IsAutoRepeat));
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or InvalidOperationException
                    or OverflowException)
            {
                return false;
            }
        }
    }

    private bool TryRequestContainerLayoutUnsafe(
        ProductDesktopContainerLayoutRequest request)
    {
        try
        {
            return requestContainerLayout(request);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            return false;
        }
    }

    private bool ApplyContainerLayoutKeyboardFocusFromActivationSource(
        IProductDesktopInteractionActivationSource source,
        ProductDesktopHostDisplayProjection display,
        string? containerId)
    {
        lock (gate)
        {
            if (disposed
                || !activationSources.Contains(source)
                || !string.Equals(source.DisplayId, display.DisplayId,
                    StringComparison.Ordinal)
                || surfaces.Count != currentBatch?.Displays.Count)
            {
                return false;
            }

            int displayIndex = currentBatch.Displays
                .Select((candidate, index) => new { candidate.DisplayId, index })
                .Where(candidate => string.Equals(
                    candidate.DisplayId,
                    display.DisplayId,
                    StringComparison.Ordinal))
                .Select(candidate => candidate.index)
                .SingleOrDefault(-1);
            return displayIndex >= 0
                && surfaces[displayIndex]
                    .ApplyContainerLayoutKeyboardFocus(containerId);
        }
    }

    private ProductDesktopInteractionSurfaceTransactionSnapshot?
        CaptureInteractionTransaction()
    {
        lock (gate)
        {
            return disposed ? null : intentConsumption?.Snapshot.Transaction;
        }
    }

    private bool ApplySelectionFromSurface(
        IProductDesktopHostReadOnlySurface source,
        ProductDesktopHostDisplayProjection display,
        string containerId,
        ProductDesktopSelectionRequest request)
    {
        lock (gate)
        {
            string? targetId = intentConsumption?.Snapshot.Transaction
                ?.Selection?.ContainerId;
            if (disposed
                || !surfaces.Contains(source)
                || !string.Equals(targetId, containerId,
                    StringComparison.Ordinal)
                || !display.Containers.Any(container => string.Equals(
                    container.ContainerId,
                    containerId,
                    StringComparison.Ordinal)))
            {
                return false;
            }

            return ApplySelectionAndRefreshUnsafe(request);
        }
    }

    private bool ApplyReferenceReassignmentFromSurface(
        IProductDesktopHostReadOnlySurface source,
        ProductDesktopHostDisplayProjection sourceDisplay,
        ProductDesktopReferenceReassignmentSurfaceInput input)
    {
        lock (gate)
        {
            ProductDesktopHostProjectionBatch? batch = currentBatch;
            ProductDesktopInteractionSurfaceTransactionSnapshot? transaction =
                intentConsumption?.Snapshot.Transaction;
            if (disposed
                || batch is null
                || !surfaces.Contains(source)
                || !sourceDisplay.Containers.Any(container => string.Equals(
                    container.ContainerId,
                    input.SourceContainerId,
                    StringComparison.Ordinal))
                || !input.SourceAttested
                || input.IsInjected
                || input.ItemIds.Count is < 1
                    or > ProductDesktopReferenceReassignmentAdapter.MaximumItemCount
                || input.ItemIds.Distinct(StringComparer.Ordinal).Count()
                    != input.ItemIds.Count
                || input.WorkspaceRevision != batch.WorkspaceRevision
                || input.TopologyGeneration != batch.TopologyGeneration
                || transaction?.IsExplicit != true
                || transaction.Selection is not { } selection
                || !string.Equals(selection.ContainerId, input.SourceContainerId,
                    StringComparison.Ordinal)
                || !selection.SelectedItemIds.SequenceEqual(
                    input.ItemIds,
                    StringComparer.Ordinal))
            {
                return false;
            }

            ProductDesktopHostReadOnlyProjection? sourceContainer =
                sourceDisplay.Containers.SingleOrDefault(container =>
                    string.Equals(
                        container.ContainerId,
                        input.SourceContainerId,
                        StringComparison.Ordinal));
            if (sourceContainer is null || sourceContainer.IsLocked)
            {
                return false;
            }

            ProductDesktopHostDisplayProjection? targetDisplay = batch.Displays
                .SingleOrDefault(display =>
                    input.PointerScreenX >= display.WorkArea.Left
                    && input.PointerScreenX < display.WorkArea.Right
                    && input.PointerScreenY >= display.WorkArea.Top
                    && input.PointerScreenY < display.WorkArea.Bottom);
            if (targetDisplay is null)
            {
                return false;
            }

            int clientX = input.PointerScreenX - targetDisplay.WorkArea.Left;
            int clientY = input.PointerScreenY - targetDisplay.WorkArea.Top;
            ProductDesktopHostReadOnlyProjection[] targets = targetDisplay.Containers
                .Where(container => !container.IsLocked
                    && !string.Equals(
                        container.ContainerId,
                        input.SourceContainerId,
                        StringComparison.Ordinal)
                    && ContainsPoint(
                        ProductDesktopHostSurfaceLayout.GetContainerBounds(
                            targetDisplay,
                            container),
                        clientX,
                        clientY))
                .ToArray();
            if (targets.Length != 1)
            {
                return false;
            }

            return requestReferenceReassignment(new(
                input.SourceContainerId,
                input.ItemIds,
                targets[0].ContainerId,
                targetDisplay.DisplayId,
                input.WorkspaceRevision,
                input.TopologyGeneration,
                input.SourceAttested,
                input.IsInjected));
        }
    }

    private static bool ContainsPoint(PixelRect bounds, int x, int y) =>
        x >= bounds.Left && x < bounds.Right
        && y >= bounds.Top && y < bounds.Bottom;

    private bool ApplySelectionFromActivationSource(
        IProductDesktopInteractionActivationSource source,
        ProductDesktopHostDisplayProjection display,
        ProductDesktopSelectionRequest request)
    {
        lock (gate)
        {
            ProductDesktopInteractionSurfaceTransactionSnapshot? transaction =
                intentConsumption?.Snapshot.Transaction;
            string? targetId = transaction?.Selection?.ContainerId;
            if (disposed
                || !activationSources.Contains(source)
                || !string.Equals(source.DisplayId, display.DisplayId,
                    StringComparison.Ordinal)
                || !display.Containers.Any(container => string.Equals(
                    container.ContainerId,
                    targetId,
                    StringComparison.Ordinal)))
            {
                return false;
            }

            return ApplySelectionAndRefreshUnsafe(request);
        }
    }

    private bool ApplySelectionAndRefreshUnsafe(
        ProductDesktopSelectionRequest request)
    {
        ProductDesktopInteractionIntentConsumptionResult result =
            ApplyInteractionSelection(request, DateTimeOffset.UtcNow);
        foreach (IProductDesktopHostReadOnlySurface surface in surfaces)
        {
            surface.RefreshSelection();
        }

        return result.IsExplicit
            && result.Snapshot.Transaction?.Selection?.Status
                == ProductDesktopSelectionStatus.Applied;
    }

    private bool CancelInteractionFromActivationSource(
        IProductDesktopInteractionActivationSource source)
    {
        ProductDesktopHostLifecycleSnapshot? published;
        bool succeeded;
        lock (gate)
        {
            if (disposed || !activationSources.Contains(source)
                || intentConsumption is null)
            {
                return false;
            }

            ProductDesktopInteractionIntentConsumptionResult cancelled =
                intentConsumption.Cancel(
                    ProductDesktopInteractionCancellationSignal.EscapePressed,
                    DateTimeOffset.UtcNow);
            bool hidden = TryApplyActivationSourcesUnsafe(visible: false);
            bool visible = hidden
                && TryApplyActivationSourcesUnsafe(visible: true);
            if (!visible)
            {
                _ = TryApplyActivationSourcesUnsafe(visible: false);
            }
            foreach (IProductDesktopHostReadOnlySurface surface in surfaces)
            {
                surface.RefreshSelection();
            }
            published = RefreshInteractionObservationUnsafe();
            succeeded = visible && !cancelled.IsExplicit;
        }

        if (published is not null)
        {
            Publish(published);
        }
        return succeeded;
    }

    private void InvalidatePreparedInputUnsafe()
    {
        if (inputForwarding?.IsEnabled == true)
        {
            _ = inputForwarding.Invalidate();
        }
        else
        {
            _ = intentPreparation?.Invalidate();
        }
    }

    private bool IsPassiveInteractionAvailableUnsafe()
    {
        try
        {
            return interactionSurface?.Capture().Evidence
                ?.IsPassiveContract == true;
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or ArgumentException
                or InvalidOperationException
                or PlatformNotSupportedException
                or OverflowException)
        {
            return false;
        }
    }

    private bool TryApplyActivationSourcesUnsafe(bool visible)
    {
        bool succeeded = true;
        foreach (IProductDesktopInteractionActivationSource source
            in activationSources)
        {
            try
            {
                succeeded &= visible
                    ? source.ApplyVisible()
                    : source.ApplyHidden();
            }
            catch (Exception exception) when (
                exception is Win32Exception
                    or ArgumentException
                    or InvalidOperationException
                    or PlatformNotSupportedException
                    or OverflowException)
            {
                succeeded = false;
            }
        }
        return succeeded;
    }

    private bool TryCreatePassiveInteractionEvidenceUnsafe(
        out ProductDesktopInteractionEvidence evidence)
    {
        evidence = default!;
        if (snapshot.Status != ProductDesktopHostLifecycleStatus.ReadyReadOnly
            || currentBatch is null
            || interactionSurface is null
            || interactionDevelopment?.Snapshot is not
            { IsDevelopmentInteractionAvailable: true, Surface: not null }
                interaction)
        {
            return false;
        }

        ProductDesktopInteractionSurfaceCapture capture;
        try
        {
            capture = interactionSurface.Capture();
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or ArgumentException
                or InvalidOperationException
                or PlatformNotSupportedException
                or OverflowException)
        {
            return false;
        }

        if (!capture.Succeeded
            || capture.Evidence?.IsPassiveContract != true
            || capture.Evidence.WindowRegistryGeneration
                != interaction.Surface.WindowRegistryGeneration)
        {
            return false;
        }

        evidence = CreateInteractionEvidence(
            currentBatch,
            capture.Evidence.WindowRegistryGeneration);
        return true;
    }

    private ProductDesktopHostLifecycleSnapshot UpdateSnapshotUnsafe(
        ProductDesktopHostLifecycleStatus status,
        bool connected,
        int ownedWindowCount,
        long workspaceRevision = 0,
        long topologyGeneration = 0,
        int renderedContainerCount = 0,
        bool readOnlyAccessibilityAvailable = false,
        bool passiveWindowContractAttested = false,
        ProductDesktopInteractionSystemSurfaceEventKind?
            lastSystemSurfaceEvent = null)
    {
        ProductDesktopSelectionSnapshot? selection =
            intentConsumption?.Snapshot.IsExplicit == true
                ? intentConsumption.Snapshot.Transaction?.Selection
                : null;
        snapshot = new(
            status,
            checked(snapshot.Generation + 1),
            connected,
            ownedWindowCount,
            workspaceRevision,
            topologyGeneration,
            renderedContainerCount,
            readOnlyAccessibilityAvailable,
            passiveWindowContractAttested,
            lastSystemSurfaceEvent,
            ExplicitInteractionActive: selection is not null,
            SelectedItemCount: selection?.SelectedItemIds.Count ?? 0,
            FocusedItemAvailable: selection?.FocusedItemId is not null,
            SelectionRevision: selection?.SelectionRevision ?? 0);
        return snapshot;
    }

    private ProductDesktopHostLifecycleSnapshot?
        RefreshInteractionObservationUnsafe()
    {
        ProductDesktopSelectionSnapshot? selection =
            intentConsumption?.Snapshot.IsExplicit == true
                ? intentConsumption.Snapshot.Transaction?.Selection
                : null;
        bool explicitInteractionActive = selection is not null;
        int selectedItemCount = selection?.SelectedItemIds.Count ?? 0;
        bool focusedItemAvailable = selection?.FocusedItemId is not null;
        long selectionRevision = selection?.SelectionRevision ?? 0;
        if (snapshot.ExplicitInteractionActive == explicitInteractionActive
            && snapshot.SelectedItemCount == selectedItemCount
            && snapshot.FocusedItemAvailable == focusedItemAvailable
            && snapshot.SelectionRevision == selectionRevision)
        {
            return null;
        }

        snapshot = snapshot with
        {
            Generation = checked(snapshot.Generation + 1),
            ExplicitInteractionActive = explicitInteractionActive,
            SelectedItemCount = selectedItemCount,
            FocusedItemAvailable = focusedItemAvailable,
            SelectionRevision = selectionRevision,
        };
        return snapshot;
    }

    private static ProductDesktopInteractionEvidence CreateInteractionEvidence(
        ProductDesktopHostProjectionBatch batch,
        long registryGeneration) =>
        new(
            NativeHostConnected: true,
            HostReadyReadOnly: true,
            ReadOnlyAccessibilityAttested: true,
            PassiveWindowContractAttested: true,
            batch.WorkspaceRevision,
            batch.TopologyGeneration,
            registryGeneration,
            AvailableContainerIds: new HashSet<string>(
                batch.Displays.SelectMany(display => display.Containers)
                    .Select(container => container.ContainerId),
                StringComparer.Ordinal),
            LockedContainerIds: new HashSet<string>(
                batch.Displays.SelectMany(display => display.Containers)
                    .Where(container => container.IsLocked)
                    .Select(container => container.ContainerId),
                StringComparer.Ordinal));

    private void Publish(ProductDesktopHostLifecycleSnapshot value) =>
        SnapshotChanged?.Invoke(this, value);

    private static nint NextInstanceMarker()
    {
        long value = Interlocked.Increment(ref nextInstanceMarker);
        if (value == 0)
        {
            value = Interlocked.Increment(ref nextInstanceMarker);
        }

        return checked((nint)value);
    }

    private static bool ProjectionsEqual(
        ProductDesktopHostReadOnlyProjection left,
        ProductDesktopHostReadOnlyProjection right) =>
        left.ContainerId == right.ContainerId
        && left.Title == right.Title
        && left.Color == right.Color
        && left.Opacity.Equals(right.Opacity)
        && left.IsCollapsed == right.IsCollapsed
        && left.XDip.Equals(right.XDip)
        && left.YDip.Equals(right.YDip)
        && left.WidthDip.Equals(right.WidthDip)
        && left.HeightDip.Equals(right.HeightDip)
        && left.IsLocked == right.IsLocked
        && left.TotalItemCount == right.TotalItemCount
        && left.VisibleItemStartOrdinal == right.VisibleItemStartOrdinal
        && left.TitleVisibility == right.TitleVisibility
        && left.TitleDoubleClickAction == right.TitleDoubleClickAction
        && left.ItemIds.SequenceEqual(
            right.ItemIds,
            StringComparer.Ordinal)
        && left.ItemNames.SequenceEqual(
            right.ItemNames,
            StringComparer.Ordinal)
        && left.ItemVisuals.SequenceEqual(right.ItemVisuals);

    private static bool BatchesEqual(
        ProductDesktopHostProjectionBatch left,
        ProductDesktopHostProjectionBatch right) =>
        left.WorkspaceRevision == right.WorkspaceRevision
        && left.TopologyGeneration == right.TopologyGeneration
        && left.PresentationGeneration == right.PresentationGeneration
        && left.TopologyFingerprint == right.TopologyFingerprint
        && left.Displays.Count == right.Displays.Count
        && left.Displays.Zip(right.Displays).All(pair =>
            pair.First.DisplayId == pair.Second.DisplayId
            && pair.First.WorkArea == pair.Second.WorkArea
            && pair.First.EffectiveDpi == pair.Second.EffectiveDpi
            && pair.First.IsPrimary == pair.Second.IsPrimary
            && pair.First.WorkspaceIsEmpty == pair.Second.WorkspaceIsEmpty
            && pair.First.Containers.Count == pair.Second.Containers.Count
            && pair.First.Containers.Zip(pair.Second.Containers).All(container =>
                ProjectionsEqual(container.First, container.Second)));

    private static bool PresentationStructuresEqual(
        ProductDesktopHostProjectionBatch left,
        ProductDesktopHostProjectionBatch right) =>
        left.WorkspaceRevision == right.WorkspaceRevision
        && left.TopologyGeneration == right.TopologyGeneration
        && left.TopologyFingerprint == right.TopologyFingerprint
        && left.Displays.Count == right.Displays.Count
        && left.Displays.Zip(right.Displays).All(pair =>
            pair.First.DisplayId == pair.Second.DisplayId
            && pair.First.WorkArea == pair.Second.WorkArea
            && pair.First.EffectiveDpi == pair.Second.EffectiveDpi
            && pair.First.IsPrimary == pair.Second.IsPrimary
            && pair.First.WorkspaceIsEmpty == pair.Second.WorkspaceIsEmpty
            && pair.First.Containers.Count == pair.Second.Containers.Count
            && pair.First.Containers.Zip(pair.Second.Containers).All(container =>
            {
                ProductDesktopHostReadOnlyProjection first = container.First;
                ProductDesktopHostReadOnlyProjection second = container.Second;
                return first.ContainerId == second.ContainerId
                    && first.Title == second.Title
                    && first.Color == second.Color
                    && first.Opacity.Equals(second.Opacity)
                    && first.IsCollapsed == second.IsCollapsed
                    && first.XDip.Equals(second.XDip)
                    && first.YDip.Equals(second.YDip)
                    && first.WidthDip.Equals(second.WidthDip)
                    && first.HeightDip.Equals(second.HeightDip)
                    && first.IsLocked == second.IsLocked
                    && first.TotalItemCount == second.TotalItemCount
                    && first.TitleVisibility == second.TitleVisibility
                    && first.TitleDoubleClickAction ==
                        second.TitleDoubleClickAction
                    && first.ItemIds.Count == second.ItemIds.Count
                    && first.ItemNames.Count == second.ItemNames.Count;
            }));

    private static bool UpdatesEqual(
        ProductDesktopHostProjectionUpdate? left,
        ProductDesktopHostProjectionUpdate right) =>
        left is not null
        && left.WorkspaceRevision == right.WorkspaceRevision
        && left.TopologyGeneration == right.TopologyGeneration
        && left.PresentationGeneration == right.PresentationGeneration
        && left.Disposition == right.Disposition
        && (left.Batch is null
            ? right.Batch is null
            : right.Batch is not null && BatchesEqual(left.Batch, right.Batch));

    private static string DisplayRegistrationId(string displayId) =>
        $"display:{displayId}";
}
