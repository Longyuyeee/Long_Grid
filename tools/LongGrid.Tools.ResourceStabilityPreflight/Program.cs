using System.Text.Json;
using LongGrid.Infrastructure.DesktopHost;

try
{
    if (args.Length != 0)
    {
        WriteError("InvalidArguments");
        return 3;
    }

    ProductResourceStabilityPreflightResult result =
        await ProductResourceStabilityPreflight.RunAsync();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        purpose = "M4c1AcceleratedResourceStabilityPreflight",
        outcome = result.Outcome,
        lifecycleIterations = result.LifecycleIterations,
        catalogIterations = result.CatalogIterations,
        classifierIterations = result.ClassifierIterations,
        syntheticSurfacesCreated = result.SyntheticSurfacesCreated,
        syntheticSurfacesReleased = result.SyntheticSurfacesReleased,
        catalogRefreshes = result.CatalogRefreshes,
        catalogNotifications = result.CatalogNotifications,
        systemEventStateRecoveredEveryIteration =
            result.SystemEventStateRecoveredEveryIteration,
        allOwnedResourcesReleased = result.AllOwnedResourcesReleased,
        thumbnailWorkerIsolationGateRequired =
            result.ThumbnailWorkerIsolationGateRequired,
        realApp24HourSoakRequired = result.RealApp24HourSoakRequired,
        real24HourEvidenceCollected = result.Real24HourEvidenceCollected,
        readsRealDesktop = result.ReadsRealDesktop,
        createsNativeWindows = result.CreatesNativeWindows,
        realFileOperationsAllowed = result.RealFileOperationsAllowed,
    }));
    return result.Outcome == "AcceleratedPass" ? 0 : 2;
}
catch (OperationCanceledException)
{
    WriteError("Cancelled");
    return 4;
}
catch (Exception exception) when (
    exception is InvalidOperationException
        or IOException
        or UnauthorizedAccessException)
{
    WriteError(exception.GetType().Name);
    return 4;
}

static void WriteError(string error) =>
    Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        purpose = "M4c1AcceleratedResourceStabilityPreflight",
        outcome = "Failed",
        error,
        real24HourEvidenceCollected = false,
        readsRealDesktop = false,
        createsNativeWindows = false,
        realFileOperationsAllowed = false,
    }));
