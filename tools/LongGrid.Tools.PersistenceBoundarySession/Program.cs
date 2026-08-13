using System.Text.Json;
using LongGrid.Infrastructure.Configuration;

const string PhaseOption = "--phase";
const string DirectoryOption = "--directory";

try
{
    if (args.Length != 4
        || !string.Equals(args[0], PhaseOption, StringComparison.Ordinal)
        || !Enum.TryParse(args[1], ignoreCase: false, out ProductConfigurationPersistenceBoundaryPhase phase)
        || !string.Equals(args[2], DirectoryOption, StringComparison.Ordinal)
        || string.IsNullOrWhiteSpace(args[3]))
    {
        WriteError("InvalidArguments");
        return 3;
    }

    ProductConfigurationPersistenceBoundarySession session = new(args[3]);
    ProductConfigurationPersistenceBoundaryResult result = await session.ExecuteAsync(phase);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        purpose = "Issue24ProductConfigurationStoreBoundarySession",
        phase = result.Phase.ToString(),
        outcome = result.Outcome.ToString(),
        loadStatus = result.LoadStatus.ToString(),
        saveError = result.SaveError?.ToString(),
        primarySha256 = result.PrimarySha256,
        backupSha256 = result.BackupSha256,
        temporaryFilePresent = result.TemporaryFilePresent,
        exposesPath = false,
        exposesConfigurationContent = false,
    }));

    return result.Outcome is ProductConfigurationPersistenceBoundaryOutcome.UnexpectedSaveSuccess
        ? 2
        : 0;
}
catch (OperationCanceledException)
{
    WriteError("Cancelled");
    return 4;
}
catch (InvalidOperationException)
{
    WriteError("InvalidSessionState");
    return 4;
}
catch (ProductConfigurationSaveException exception)
{
    WriteError(exception.Error.ToString());
    return 4;
}
catch (IOException)
{
    WriteError("IoFailure");
    return 4;
}
catch (UnauthorizedAccessException)
{
    WriteError("IoFailure");
    return 4;
}

static void WriteError(string error) =>
    Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        purpose = "Issue24ProductConfigurationStoreBoundarySession",
        outcome = "HostRejected",
        error,
        exposesPath = false,
        exposesConfigurationContent = false,
    }));
