using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

public sealed record ProductDesktopHostDisplayProjection
{
    private ProductDesktopHostDisplayProjection(
        string displayId,
        PixelRect workArea,
        uint effectiveDpi,
        IReadOnlyList<ProductDesktopHostReadOnlyProjection> containers)
    {
        DisplayId = displayId;
        WorkArea = workArea;
        EffectiveDpi = effectiveDpi;
        Containers = containers;
    }

    public string DisplayId { get; }

    public PixelRect WorkArea { get; }

    public uint EffectiveDpi { get; }

    public IReadOnlyList<ProductDesktopHostReadOnlyProjection> Containers { get; }

    public static ProductDesktopHostDisplayProjection Create(
        string displayId,
        PixelRect workArea,
        uint effectiveDpi,
        IEnumerable<ProductDesktopHostReadOnlyProjection> containers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayId);
        ArgumentNullException.ThrowIfNull(containers);
        ProductDesktopHostReadOnlyProjection[] copied = containers.ToArray();
        if (!workArea.HasArea
            || displayId.Length > ProductConfigurationLimits.MaximumDisplayKeyLength
            || effectiveDpi is < 48 or > 768
            || copied.Length > ProductConfigurationLimits.MaximumContainers
            || copied.Any(container => container is null)
            || copied.Select(container => container.ContainerId)
                .Distinct(StringComparer.Ordinal).Count() != copied.Length)
        {
            throw new ArgumentException(
                "Display projections require bounded geometry and unique containers.");
        }

        return new(
            displayId,
            workArea,
            effectiveDpi,
            Array.AsReadOnly(copied));
    }
}

public sealed record ProductDesktopHostProjectionBatch
{
    public const int MaximumDisplays = 16;

    private ProductDesktopHostProjectionBatch(
        long workspaceRevision,
        long topologyGeneration,
        string topologyFingerprint,
        IReadOnlyList<ProductDesktopHostDisplayProjection> displays)
    {
        WorkspaceRevision = workspaceRevision;
        TopologyGeneration = topologyGeneration;
        TopologyFingerprint = topologyFingerprint;
        Displays = displays;
    }

    public long WorkspaceRevision { get; }

    public long TopologyGeneration { get; }

    public string TopologyFingerprint { get; }

    public IReadOnlyList<ProductDesktopHostDisplayProjection> Displays { get; }

    public int ContainerCount => Displays.Sum(display => display.Containers.Count);

    public static ProductDesktopHostProjectionBatch Create(
        long workspaceRevision,
        long topologyGeneration,
        string topologyFingerprint,
        IEnumerable<ProductDesktopHostDisplayProjection> displays)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topologyFingerprint);
        ArgumentNullException.ThrowIfNull(displays);
        ProductDesktopHostDisplayProjection[] copied = displays.ToArray();
        if (copied.Any(display => display is null))
        {
            throw new ArgumentException(
                "DesktopHost batches cannot contain null displays.",
                nameof(displays));
        }

        string[] containerIds = copied
            .SelectMany(display => display.Containers)
            .Select(container => container.ContainerId)
            .ToArray();
        if (workspaceRevision < 0
            || topologyGeneration <= 0
            || topologyFingerprint.Length != 64
            || !topologyFingerprint.All(Uri.IsHexDigit)
            || copied.Length is 0 or > MaximumDisplays
            || copied.Select(display => display.DisplayId)
                .Distinct(StringComparer.Ordinal).Count() != copied.Length
            || containerIds.Length > ProductConfigurationLimits.MaximumContainers
            || containerIds.Distinct(StringComparer.Ordinal).Count()
                != containerIds.Length)
        {
            throw new ArgumentException(
                "DesktopHost batches require finite generations and unique identities.");
        }

        return new(
            workspaceRevision,
            topologyGeneration,
            topologyFingerprint.ToUpperInvariant(),
            Array.AsReadOnly(copied));
    }
}
