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
        ProductDesktopHostProjectionBatch? batch,
        long presentationGeneration)
    {
        WorkspaceRevision = workspaceRevision;
        TopologyGeneration = topologyGeneration;
        Disposition = disposition;
        Batch = batch;
        PresentationGeneration = presentationGeneration;
    }

    public long WorkspaceRevision { get; }

    public long TopologyGeneration { get; }

    public ProductDesktopHostProjectionDisposition Disposition { get; }

    public ProductDesktopHostProjectionBatch? Batch { get; }

    public long PresentationGeneration { get; }

    public static ProductDesktopHostProjectionUpdate Create(
        long workspaceRevision,
        long topologyGeneration,
        ProductDesktopHostProjectionDisposition disposition,
        ProductDesktopHostProjectionBatch? batch = null,
        long presentationGeneration = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workspaceRevision);
        ArgumentOutOfRangeException.ThrowIfNegative(topologyGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(presentationGeneration);

        if (disposition is ProductDesktopHostProjectionDisposition.Ready
            or ProductDesktopHostProjectionDisposition.EmptyWorkspace)
        {
            ArgumentNullException.ThrowIfNull(batch);
            if (batch.WorkspaceRevision != workspaceRevision
                || batch.TopologyGeneration != topologyGeneration
                || batch.PresentationGeneration != presentationGeneration)
            {
                throw new ArgumentException(
                    "Ready projection update metadata must match its batch.",
                    nameof(batch));
            }

            bool batchIsEmpty = batch.ContainerCount == 0;
            if ((disposition == ProductDesktopHostProjectionDisposition.Ready
                    && batchIsEmpty)
                || (disposition ==
                        ProductDesktopHostProjectionDisposition.EmptyWorkspace
                    && !batchIsEmpty))
            {
                throw new ArgumentException(
                    "Projection disposition must match the batch container count.",
                    nameof(batch));
            }
        }
        else if (batch is not null)
        {
            throw new ArgumentException(
                "Only ready or empty-workspace projection updates may carry a batch.",
                nameof(batch));
        }

        return new(
            workspaceRevision,
            topologyGeneration,
            disposition,
            batch,
            presentationGeneration);
    }
}
