using System.Security.Cryptography;
using System.Text;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Configuration;

public enum ProductQuickStartSuggestionStatus
{
    Ready,
    CatalogUnavailable,
    WorkspaceNotEmpty,
    NoItems,
    InvalidState,
}

public sealed record ProductQuickStartSuggestionItem(
    int CatalogIndex,
    string DisplayName,
    DesktopItemKind Kind);

public sealed record ProductQuickStartSuggestionSnapshot(
    ProductQuickStartSuggestionStatus Status,
    Guid PreviewId,
    long CatalogGeneration,
    long WorkspaceRevision,
    string WorkspaceFingerprint,
    string CatalogFingerprint,
    string ContainerName,
    IReadOnlyList<ProductQuickStartSuggestionItem> Items,
    int TotalCandidateCount,
    bool IsTruncated)
{
    public bool CanCommit =>
        Status == ProductQuickStartSuggestionStatus.Ready
        && PreviewId != Guid.Empty
        && CatalogGeneration > 0
        && WorkspaceRevision > 0
        && WorkspaceFingerprint.Length == 64
        && CatalogFingerprint.Length == 64
        && Items.Count > 0;
}

public static class ProductQuickStartSuggestionPlanner
{
    public const int MaximumSuggestedItemCount = 256;
    public const string DefaultContainerName = "桌面项目";

    public static ProductQuickStartSuggestionSnapshot Create(
        ProductWorkspaceState state,
        long workspaceRevision,
        long catalogGeneration,
        bool catalogAuthoritative,
        IReadOnlyList<DesktopCatalogEntry> catalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);
        if (workspaceRevision <= 0
            || state.Containers is null
            || state.Containers.Any(container => container is null)
            || !TryWorkspaceFingerprint(state, out string workspaceFingerprint))
        {
            return Unavailable(
                ProductQuickStartSuggestionStatus.InvalidState,
                workspaceRevision,
                catalogGeneration);
        }
        if (state.Containers.Count > 0)
        {
            return Unavailable(
                ProductQuickStartSuggestionStatus.WorkspaceNotEmpty,
                workspaceRevision,
                catalogGeneration,
                workspaceFingerprint);
        }
        if (!catalogAuthoritative || catalogGeneration <= 0)
        {
            return Unavailable(
                ProductQuickStartSuggestionStatus.CatalogUnavailable,
                workspaceRevision,
                catalogGeneration,
                workspaceFingerprint);
        }

        DesktopCatalogEntry[] valid = catalog
            .Where(IsValid)
            .GroupBy(
                entry => entry.Identity.CanonicalTarget,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (valid.Length == 0)
        {
            return Unavailable(
                ProductQuickStartSuggestionStatus.NoItems,
                workspaceRevision,
                catalogGeneration,
                workspaceFingerprint);
        }

        DesktopCatalogEntry[] selected = valid
            .Take(MaximumSuggestedItemCount)
            .ToArray();
        int[] indexes = selected
            .Select(entry => IndexOfReference(catalog, entry))
            .ToArray();
        return new(
            ProductQuickStartSuggestionStatus.Ready,
            Guid.NewGuid(),
            catalogGeneration,
            workspaceRevision,
            workspaceFingerprint,
            ComputeCatalogFingerprint(selected),
            DefaultContainerName,
            selected.Select((entry, index) => new ProductQuickStartSuggestionItem(
                indexes[index],
                entry.DisplayName,
                entry.Kind)).ToArray(),
            valid.Length,
            valid.Length > selected.Length);
    }

    public static string ComputeCatalogFingerprint(
        IEnumerable<DesktopCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (DesktopCatalogEntry entry in entries)
        {
            string value = string.Join(
                '\u001f',
                entry.Identity.Provider,
                entry.Identity.CanonicalTarget,
                entry.SourceId,
                entry.DisplayName,
                ((int)entry.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture));
            hash.AppendData(Encoding.UTF8.GetBytes(value));
            hash.AppendData([0]);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static bool IsValid(DesktopCatalogEntry? entry) =>
        entry is not null
        && entry.Identity is not null
        && !string.IsNullOrWhiteSpace(entry.Identity.Provider)
        && !string.IsNullOrWhiteSpace(entry.Identity.CanonicalTarget)
        && !string.IsNullOrWhiteSpace(entry.SourceId)
        && !string.IsNullOrWhiteSpace(entry.DisplayName)
        && Enum.IsDefined(entry.Kind);

    private static int IndexOfReference(
        IReadOnlyList<DesktopCatalogEntry> catalog,
        DesktopCatalogEntry entry)
    {
        for (int index = 0; index < catalog.Count; index++)
        {
            if (ReferenceEquals(catalog[index], entry))
            {
                return index;
            }
        }
        return -1;
    }

    private static bool TryWorkspaceFingerprint(
        ProductWorkspaceState state,
        out string fingerprint)
    {
        ProductWorkspaceProjectionResult projection =
            ProductWorkspaceConfigurationProjector.Project(state);
        if (!projection.IsSuccess)
        {
            fingerprint = string.Empty;
            return false;
        }
        fingerprint = ProductWorkspaceConfigurationFingerprint.Compute(
            projection.Document!);
        return true;
    }

    private static ProductQuickStartSuggestionSnapshot Unavailable(
        ProductQuickStartSuggestionStatus status,
        long workspaceRevision,
        long catalogGeneration,
        string workspaceFingerprint = "") => new(
            status,
            Guid.Empty,
            catalogGeneration,
            workspaceRevision,
            workspaceFingerprint,
            string.Empty,
            DefaultContainerName,
            [],
            0,
            false);
}
