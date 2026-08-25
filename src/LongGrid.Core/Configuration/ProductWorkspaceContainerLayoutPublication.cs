namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceContainerLayoutPublicationDecision
{
    AwaitingSave,
    Published,
    CompensationRequired,
    Superseded,
}

public sealed record ProductWorkspaceContainerLayoutPublicationToken(
    Guid OperationId,
    string ContainerId,
    long WorkspaceRevision,
    long SaveRevision,
    long TopologyGeneration,
    ProductContainerPlacementState OriginalPlacement,
    ProductContainerPlacementState CommittedPlacement);

public static class ProductWorkspaceContainerLayoutPublication
{
    public static ProductWorkspaceContainerLayoutPublicationDecision Evaluate(
        ProductWorkspaceContainerLayoutPublicationToken token,
        ProductWorkspaceSaveSnapshot save,
        long currentWorkspaceRevision,
        ProductContainerPlacementState? currentPlacement)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(save);
        if (token.OperationId == Guid.Empty
            || string.IsNullOrWhiteSpace(token.ContainerId)
            || token.WorkspaceRevision <= 0
            || token.SaveRevision <= 0
            || token.TopologyGeneration <= 0
            || token.OriginalPlacement is null
            || token.CommittedPlacement is null)
        {
            throw new ArgumentOutOfRangeException(nameof(token));
        }

        if (currentWorkspaceRevision != token.WorkspaceRevision
            || save.CurrentRevision != token.SaveRevision
            || !PlacementMatches(currentPlacement, token.CommittedPlacement))
        {
            return ProductWorkspaceContainerLayoutPublicationDecision.Superseded;
        }

        return save.Status switch
        {
            ProductWorkspaceSaveStatus.Saved
                when save.SavedRevision == token.SaveRevision =>
                ProductWorkspaceContainerLayoutPublicationDecision.Published,
            ProductWorkspaceSaveStatus.Failed =>
                ProductWorkspaceContainerLayoutPublicationDecision
                    .CompensationRequired,
            ProductWorkspaceSaveStatus.WaitingForDebounce
                or ProductWorkspaceSaveStatus.Saving =>
                ProductWorkspaceContainerLayoutPublicationDecision.AwaitingSave,
            _ => ProductWorkspaceContainerLayoutPublicationDecision.Superseded,
        };
    }

    internal static bool PlacementMatches(
        ProductContainerPlacementState? left,
        ProductContainerPlacementState? right) =>
        left is not null
        && right is not null
        && string.Equals(
            left.DisplayKey,
            right.DisplayKey,
            StringComparison.Ordinal)
        && Math.Abs(left.XDip - right.XDip) < 0.001
        && Math.Abs(left.YDip - right.YDip) < 0.001
        && Math.Abs(left.WidthDip - right.WidthDip) < 0.001
        && Math.Abs(left.HeightDip - right.HeightDip) < 0.001
        && ExtensionDataMatches(left.ExtensionData, right.ExtensionData);

    private static bool ExtensionDataMatches(
        IDictionary<string, System.Text.Json.JsonElement>? left,
        IDictionary<string, System.Text.Json.JsonElement>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }
        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }
        foreach ((string key, System.Text.Json.JsonElement value) in left)
        {
            if (!right.TryGetValue(key, out System.Text.Json.JsonElement candidate)
                || !string.Equals(
                    value.GetRawText(),
                    candidate.GetRawText(),
                    StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }
}
