using System.Text.Json;
using LongGrid.Infrastructure.Configuration;

try
{
    if (args.Length != 0)
    {
        WriteError("InvalidArguments");
        return 3;
    }

    ProductWorkspaceRecoveryPreflightResult result =
        await ProductWorkspaceRecoveryPreflight.RunAsync();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        purpose = "M4b1ProductWorkspaceRecoveryPreflight",
        outcome = result.Outcome,
        scenarioCount = result.ScenarioCount,
        backupAcceptedAfterRestart = result.BackupAcceptedAfterRestart,
        safeModeResetAfterRestart = result.SafeModeResetAfterRestart,
        restartSafePointRecovered = result.RestartSafePointRecovered,
        catalogRecovered = result.CatalogRecovered,
        explicitRetrySucceeded = result.ExplicitRetrySucceeded,
        cancellationLeftNoRetry = result.CancellationLeftNoRetry,
        temporarySandboxCleaned = result.TemporarySandboxCleaned,
        readsRealDesktop = result.ReadsRealDesktop,
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
        purpose = "M4b1ProductWorkspaceRecoveryPreflight",
        outcome = "Failed",
        error,
        readsRealDesktop = false,
        realFileOperationsAllowed = false,
    }));
