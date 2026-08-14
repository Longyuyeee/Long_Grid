using System.Text.Json;
using LongGrid.Infrastructure.DesktopHost;

try
{
    if (args.Length != 0)
    {
        WriteError("InvalidArguments");
        return 3;
    }

    ProductDesktopHostRecoveryPreflightResult result =
        await ProductDesktopHostRecoveryPreflight.RunAsync();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        purpose = "M4b2DesktopHostRecoveryPreflight",
        outcome = result.Outcome,
        scenarioCount = result.ScenarioCount,
        explorerRestartRecovered = result.ExplorerRestartRecovered,
        sessionUnavailableRecovered = result.SessionUnavailableRecovered,
        topologyUnavailableRecovered = result.TopologyUnavailableRecovered,
        displayReplacementReleasedOldSurfaces =
            result.DisplayReplacementReleasedOldSurfaces,
        hostRestartRejectedStaleIdentity = result.HostRestartRejectedStaleIdentity,
        allSyntheticSurfacesReleased = result.AllSyntheticSurfacesReleased,
        readsRealDesktop = result.ReadsRealDesktop,
        createsNativeWindows = result.CreatesNativeWindows,
        realFileOperationsAllowed = result.RealFileOperationsAllowed,
    }));
    return result.Outcome == "Passed" ? 0 : 2;
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
        purpose = "M4b2DesktopHostRecoveryPreflight",
        outcome = "Failed",
        error,
        readsRealDesktop = false,
        createsNativeWindows = false,
        realFileOperationsAllowed = false,
    }));
