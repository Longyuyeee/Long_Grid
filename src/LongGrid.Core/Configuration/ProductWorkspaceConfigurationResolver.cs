using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceResolutionError
{
    None,
    InvalidConfiguration,
    InvalidCatalog,
}

public sealed record ProductWorkspaceResolutionSummary(
    int Resolved,
    int Missing,
    int TypeChanged,
    int Ambiguous,
    int UnsupportedTarget)
{
    public int Total =>
        Resolved + Missing + TypeChanged + Ambiguous + UnsupportedTarget;
}

public sealed record ProductWorkspaceResolutionResult(
    ProductWorkspaceResolutionError Error,
    ProductConfigurationError ConfigurationError,
    ProductWorkspaceState? State,
    ProductWorkspaceResolutionSummary Summary)
{
    public bool IsSuccess =>
        Error == ProductWorkspaceResolutionError.None && State is not null;
}

public static class ProductWorkspaceConfigurationResolver
{
    public static ProductWorkspaceResolutionResult Resolve(
        ProductConfigurationDocument document,
        IReadOnlyList<DesktopCatalogEntry> catalog)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(catalog);

        ProductConfigurationDocument snapshot;
        try
        {
            snapshot = ProductConfigurationJson.Deserialize(
                ProductConfigurationJson.SerializeToUtf8Bytes(document));
        }
        catch (ProductConfigurationContractException exception)
        {
            return Failure(
                ProductWorkspaceResolutionError.InvalidConfiguration,
                exception.Error);
        }

        if (!TryIndexCatalog(
            catalog,
            out Dictionary<string, List<DesktopCatalogEntry>>? index))
        {
            return Failure(ProductWorkspaceResolutionError.InvalidCatalog);
        }

        var summary = new ResolutionSummaryBuilder();
        ProductContainerState[] containers = snapshot.Containers
            .Select(container => ResolveContainer(container, index!, summary))
            .ToArray();
        ProductWorkspaceState state = new()
        {
            ProfileId = snapshot.ProfileId,
            Containers = containers,
            Rules = snapshot.Rules.Select(rule => new ProductAutomationRuleState
            {
                Id = rule.Id,
                Name = rule.Name,
                Enabled = rule.Enabled,
                Priority = rule.Priority,
                TargetContainerId = rule.TargetContainerId,
                MatchMode = rule.MatchMode,
                Action = rule.Action,
                Conditions = rule.Conditions.Select(condition =>
                    new ProductAutomationRuleConditionState
                    {
                        Kind = condition.Kind,
                        Value = condition.Value,
                        ExtensionData = condition.ExtensionData,
                    }).ToArray(),
                ExtensionData = rule.ExtensionData,
            }).ToArray(),
            SavedDisplayTopology = snapshot.SavedDisplayTopology?
                .ToArray(),
            ExtensionData = snapshot.ExtensionData,
        };
        return new(
            ProductWorkspaceResolutionError.None,
            ProductConfigurationError.None,
            state,
            summary.Build());
    }
    private static ProductContainerState ResolveContainer(
        ContainerConfiguration container,
        IReadOnlyDictionary<string, List<DesktopCatalogEntry>> index,
        ResolutionSummaryBuilder summary) =>
        new()
        {
            Id = container.Id,
            Name = container.Name,
            IsLocked = container.IsLocked,
            Appearance = new ProductContainerAppearanceState
            {
                Color = container.Appearance.Color,
                Opacity = container.Appearance.Opacity,
                Collapsed = container.Appearance.Collapsed,
                TitleVisibility = container.Appearance.TitleVisibility,
                TitleDoubleClickAction =
                    container.Appearance.TitleDoubleClickAction,
                ContentDensity = container.Appearance.ContentDensity,
                ExtensionData = container.Appearance.ExtensionData,
            },
            Placement = new ProductContainerPlacementState
            {
                DisplayKey = container.Placement.DisplayKey,
                XDip = container.Placement.XDip,
                YDip = container.Placement.YDip,
                WidthDip = container.Placement.WidthDip,
                HeightDip = container.Placement.HeightDip,
                ExtensionData = container.Placement.ExtensionData,
            },
            Items = container.Items
                .Select(item => ResolveItem(item, index, summary))
                .ToArray(),
            FolderBinding = RestoreFolderBinding(container.FolderBinding),
            ExtensionData = container.ExtensionData,
        };

    private static ProductContainerFolderBindingState? RestoreFolderBinding(
        ContainerFolderBindingConfiguration? binding)
    {
        if (binding is null)
        {
            return null;
        }

        return new()
        {
            PersistedTarget = binding.Target,
            VolumeSerialNumber = Convert.ToUInt64(
                binding.VolumeSerialNumber,
                16),
            FileId = binding.FileId,
            SortMode = binding.SortMode,
            Resolution = ProductContainerFolderBindingResolution.Unavailable,
            ExtensionData = binding.ExtensionData,
        };
    }

    private static ProductItemReferenceState ResolveItem(
        DesktopItemReferenceConfiguration item,
        IReadOnlyDictionary<string, List<DesktopCatalogEntry>> index,
        ResolutionSummaryBuilder summary)
    {
        if (!ProductWorkspaceIdentityPolicy.TryNormalizeCanonicalTarget(
            item.Target,
            out string? target))
        {
            summary.UnsupportedTarget++;
            return Restore(
                item,
                ProductItemReferenceResolution.UnsupportedTarget);
        }

        if (!index.TryGetValue(target!, out List<DesktopCatalogEntry>? matches))
        {
            summary.Missing++;
            return Restore(item, ProductItemReferenceResolution.Missing);
        }

        if (matches.Count != 1)
        {
            summary.Ambiguous++;
            return Restore(item, ProductItemReferenceResolution.Ambiguous);
        }

        DesktopCatalogEntry entry = matches[0];
        if (ProductWorkspaceIdentityPolicy.MapKind(entry.Kind) != item.Kind)
        {
            summary.TypeChanged++;
            return Restore(item, ProductItemReferenceResolution.TypeChanged);
        }

        summary.Resolved++;
        return ProductItemReferenceState.CreateResolved(
            item.Id,
            entry,
            item.ExtensionData);
    }

    private static ProductItemReferenceState Restore(
        DesktopItemReferenceConfiguration item,
        ProductItemReferenceResolution resolution) =>
        ProductItemReferenceState.RestoreUnresolved(
            item.Id,
            item.Kind,
            item.Target,
            resolution,
            item.ExtensionData);

    private static bool TryIndexCatalog(
        IReadOnlyList<DesktopCatalogEntry> catalog,
        out Dictionary<string, List<DesktopCatalogEntry>>? index)
    {
        index = new(StringComparer.OrdinalIgnoreCase);
        foreach (DesktopCatalogEntry? entry in catalog)
        {
            DesktopItemIdentity? identity = entry?.Identity;
            if (entry is null
                || identity is null
                || !ProductWorkspaceIdentityPolicy.IsSupportedProvider(
                    identity.Provider)
                || string.IsNullOrWhiteSpace(entry.SourceId)
                || string.IsNullOrWhiteSpace(entry.DisplayName)
                || !Enum.IsDefined(entry.Kind)
                || !ProductWorkspaceIdentityPolicy.HasConsistentOptionalFileIdentity(
                    identity)
                || !ProductWorkspaceIdentityPolicy.TryNormalizeCanonicalTarget(
                    identity.CanonicalTarget,
                    out string? target))
            {
                index = null;
                return false;
            }

            if (!index.TryGetValue(target!, out List<DesktopCatalogEntry>? matches))
            {
                matches = [];
                index.Add(target!, matches);
            }

            matches.Add(entry);
        }

        return true;
    }

    private static ProductWorkspaceResolutionResult Failure(
        ProductWorkspaceResolutionError error,
        ProductConfigurationError configurationError =
            ProductConfigurationError.None) =>
        new(error, configurationError, null, new(0, 0, 0, 0, 0));

    private sealed class ResolutionSummaryBuilder
    {
        public int Resolved { get; set; }

        public int Missing { get; set; }

        public int TypeChanged { get; set; }

        public int Ambiguous { get; set; }

        public int UnsupportedTarget { get; set; }

        public ProductWorkspaceResolutionSummary Build() =>
            new(Resolved, Missing, TypeChanged, Ambiguous, UnsupportedTarget);
    }
}
