namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductDesktopHostProjectionDisposition
{
    Ready,
    EmptyWorkspace,
    TopologyRefreshing,
    TopologyUnavailable,
    Invalid,
}

public sealed record ProductDesktopHostProjectionUpdate
{
    private ProductDesktopHostProjectionUpdate(
        long workspaceRevision,
        long topologyGeneration,
        ProductDesktopHostProjectionDisposition disposition,
        ProductDesktopHostProjectionBatch? batch)
    {
        WorkspaceRevision = workspaceRevision;
        TopologyGeneration = topologyGeneration;
        Disposition = disposition;
        Batch = batch;
    }

    public long WorkspaceRevision { get; }

    public long TopologyGeneration { get; }

    public ProductDesktopHostProjectionDisposition Disposition { get; }

    public ProductDesktopHostProjectionBatch? Batch { get; }

    public static ProductDesktopHostProjectionUpdate Create(
        long workspaceRevision,
        long topologyGeneration,
        ProductDesktopHostProjectionDisposition disposition,
        ProductDesktopHostProjectionBatch? batch = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workspaceRevision);
        ArgumentOutOfRangeException.ThrowIfNegative(topologyGeneration);

        if (disposition == ProductDesktopHostProjectionDisposition.Ready)
        {
            ArgumentNullException.ThrowIfNull(batch);
            if (batch.WorkspaceRevision != workspaceRevision
                || batch.TopologyGeneration != topologyGeneration)
            {
                throw new ArgumentException(
                    "Ready projection update metadata must match its batch.",
                    nameof(batch));
            }
        }
        else if (batch is not null)
        {
            throw new ArgumentException(
                "Only ready projection updates may carry a batch.",
                nameof(batch));
        }

        return new(
            workspaceRevision,
            topologyGeneration,
            disposition,
            batch);
    }
}
