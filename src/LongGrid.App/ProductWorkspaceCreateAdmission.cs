using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.App;

internal static class ProductWorkspaceCreateAdmission
{
    // Missing configuration is the first-run case, not a recovery read-only session.
    internal static ProductWorkspaceState? Resolve(
        ProductWorkspaceSessionSnapshot session,
        ProductConfigurationLoadStatus? loadStatus)
    {
        if (session.Status == ProductWorkspaceSessionStatus.NoSavedConfiguration
            && loadStatus == ProductConfigurationLoadStatus.Missing)
        {
            return ProductWorkspaceConfigurationResolver.Resolve(
                ProductConfigurationDefaults.CreateEmpty(),
                Array.Empty<DesktopCatalogEntry>()).State;
        }

        return session.IsReadOnly ? null : session.State;
    }
}
