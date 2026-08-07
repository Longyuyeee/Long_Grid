using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.DesktopItems;

namespace LongGrid.App;

public sealed record ProductWorkspaceResolvedReferenceCandidatePresentation(
    int Ordinal,
    string DisplayName,
    string KindLabel,
    long CatalogGeneration,
    int CatalogIndex)
{
    public string AccessibilityName =>
        $"桌面项目 {Ordinal}，{DisplayName}，{KindLabel}";

    public string MachineStatus =>
        $"ResolvedReferenceCandidate:Ordinal={Ordinal}:Kind={KindLabel}:" +
        $"Generation={CatalogGeneration}:CatalogIndex={CatalogIndex}";
}

internal sealed record ProductWorkspaceResolvedReferenceAddPresentation(
    long EditRevision,
    long CatalogGeneration,
    bool CanAdd,
    IReadOnlyList<ProductWorkspaceResolvedReferenceCandidatePresentation> Candidates)
{
    public static ProductWorkspaceResolvedReferenceAddPresentation Unavailable { get; } =
        new(
            0,
            0,
            CanAdd: false,
            Array.Empty<ProductWorkspaceResolvedReferenceCandidatePresentation>());

    public static ProductWorkspaceResolvedReferenceAddPresentation Create(
        long editRevision,
        bool canEdit,
        ProductWorkspaceState state,
        ProductDesktopCatalogSnapshot catalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);

        if (!catalog.IsAuthoritative)
        {
            return Unavailable;
        }

        var assignedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ProductItemReferenceState item in state.Containers
            .SelectMany(container => container.Items))
        {
            if (item.Resolution == ProductItemReferenceResolution.Resolved
                && item.CatalogEntry is not null)
            {
                assignedTargets.Add(item.CatalogEntry.Identity.CanonicalTarget);
            }
        }

        ProductWorkspaceResolvedReferenceCandidatePresentation[] candidates =
            catalog.Entries
                .Select((entry, index) => (entry, index))
                .Where(pair => !assignedTargets.Contains(
                    pair.entry.Identity.CanonicalTarget))
                .Select((pair, ordinal) =>
                    new ProductWorkspaceResolvedReferenceCandidatePresentation(
                        ordinal + 1,
                        pair.entry.DisplayName,
                        DescribeKind(pair.entry.Kind),
                        catalog.Generation,
                        pair.index))
                .ToArray();
        bool hasUnlockedContainer = state.Containers.Any(container => !container.IsLocked);
        return new(
            editRevision,
            catalog.Generation,
            CanAdd: canEdit && hasUnlockedContainer && candidates.Length > 0,
            candidates);
    }

    private static string DescribeKind(DesktopItemKind kind) => kind switch
    {
        DesktopItemKind.File => "文件",
        DesktopItemKind.Directory => "文件夹",
        DesktopItemKind.Shortcut => "快捷方式",
        DesktopItemKind.InternetShortcut => "网页快捷方式",
        _ => "未知类型",
    };
}
