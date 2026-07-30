using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using LongGrid.Spikes.ConfigurationPersistence;

if (args.Length > 0 && args[0] == "--child-save")
{
    return await RunChildSaveAsync(args);
}

bool jsonOutput = args.Contains("--json", StringComparer.Ordinal);
int iterations = ParseIterations(args);
int killIterations = ParseBoundedOption(args, "--kill-iterations", defaultValue: 3, maximum: 1000);
List<ScenarioResult> results = [];
string sandbox = Path.Combine(
    Path.GetTempPath(),
    "LongGrid.ConfigurationPersistence",
    Guid.NewGuid().ToString("N"));

try
{
    Directory.CreateDirectory(sandbox);

    await RunScenarioAsync("round-trip-and-backup", VerifyRoundTripAndBackupAsync, results);
    await RunScenarioAsync("safe-mode-preserves-damage", VerifySafeModePreservesDamageAsync, results);
    await RunScenarioAsync("failure-checkpoints", VerifyFailureCheckpointsAsync, results);
    await RunScenarioAsync(
        "process-termination-checkpoints",
        directory => VerifyProcessTerminationCheckpointsAsync(directory, killIterations),
        results);
    await RunScenarioAsync("schema-migration-and-rollback", VerifySchemaMigrationAndRollbackAsync, results);
    await RunScenarioAsync("stale-temp-is-ignored", VerifyStaleTemporaryFileIsIgnoredAsync, results);
    await RunScenarioAsync("unknown-fields-survive", VerifyUnknownFieldsSurviveAsync, results);
    await RunScenarioAsync("bounded-document-size", VerifyBoundedDocumentSizeAsync, results);
    await RunScenarioAsync(
        "repeated-atomic-save",
        directory => VerifyRepeatedSaveAsync(directory, iterations),
        results);
}
finally
{
    try
    {
        Directory.Delete(sandbox, recursive: true);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
}

bool passed = results.All(result => result.Passed);

if (jsonOutput)
{
    Console.WriteLine(JsonSerializer.Serialize(
        new
        {
            passed,
            iterations,
            killIterations,
            scenarios = results,
        },
        ProbeJsonOptions.Output));
}
else
{
    foreach (ScenarioResult result in results)
    {
        Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL")} {result.Name}: {result.Detail}");
    }
}

return passed ? 0 : 1;

async Task VerifyRoundTripAndBackupAsync(string directory)
{
    AtomicJsonConfigurationStore<ProbeConfigurationDocument> store = CreateStore(directory);
    await store.SaveAsync(CreateDocument("first"));
    await store.SaveAsync(CreateDocument("second"));

    ConfigurationLoadResult<ProbeConfigurationDocument> current = await store.LoadAsync();
    Require(current.Status == ConfigurationLoadStatus.LoadedPrimary, "Primary was not loaded.");
    Require(current.Document?.Containers[0].Name == "second", "Latest document was not committed.");

    await File.WriteAllTextAsync(store.PrimaryPath, "{ damaged");
    byte[] damagedPrimary = await File.ReadAllBytesAsync(store.PrimaryPath);

    ConfigurationLoadResult<ProbeConfigurationDocument> recovered = await store.LoadAsync();
    Require(
        recovered.Status == ConfigurationLoadStatus.RecoveredFromBackup,
        "The valid backup was not selected.");
    Require(
        recovered.Document?.Containers[0].Name == "first",
        "The backup does not contain the previous committed document.");
    Require(
        damagedPrimary.SequenceEqual(await File.ReadAllBytesAsync(store.PrimaryPath)),
        "Loading the backup overwrote forensic evidence in the primary file.");

    await RequireNormalSaveRefusedAsync(store, "repaired-without-confirmation");
    Require(
        damagedPrimary.SequenceEqual(await File.ReadAllBytesAsync(store.PrimaryPath)),
        "Normal save overwrote a damaged primary after backup recovery.");
}

async Task VerifySafeModePreservesDamageAsync(string directory)
{
    AtomicJsonConfigurationStore<ProbeConfigurationDocument> store = CreateStore(directory);
    Directory.CreateDirectory(directory);
    await File.WriteAllTextAsync(store.PrimaryPath, "{ primary-damage");
    await File.WriteAllTextAsync(store.BackupPath, "{ backup-damage");
    byte[] primaryBefore = await File.ReadAllBytesAsync(store.PrimaryPath);
    byte[] backupBefore = await File.ReadAllBytesAsync(store.BackupPath);

    ConfigurationLoadResult<ProbeConfigurationDocument> result = await store.LoadAsync();

    Require(result.Status == ConfigurationLoadStatus.SafeMode, "Safe mode was not selected.");
    Require(result.Document is null, "A damaged document was returned.");
    await RequireNormalSaveRefusedAsync(store, "unsafe-reset");
    Require(
        primaryBefore.SequenceEqual(await File.ReadAllBytesAsync(store.PrimaryPath)),
        "The damaged primary was overwritten.");
    Require(
        backupBefore.SequenceEqual(await File.ReadAllBytesAsync(store.BackupPath)),
        "The damaged backup was overwritten.");
}

async Task VerifyFailureCheckpointsAsync(string directory)
{
    foreach (AtomicConfigurationSaveCheckpoint checkpoint in Enum.GetValues<AtomicConfigurationSaveCheckpoint>())
    {
        string checkpointDirectory = Path.Combine(directory, checkpoint.ToString());
        AtomicJsonConfigurationStore<ProbeConfigurationDocument> store = CreateStore(checkpointDirectory);
        await store.SaveAsync(CreateDocument("baseline"));

        try
        {
            await store.SaveAsync(CreateDocument("candidate"), checkpoint);
            throw new InvalidOperationException($"Checkpoint {checkpoint} did not inject a failure.");
        }
        catch (InjectedSaveFailureException exception) when (exception.Checkpoint == checkpoint)
        {
        }

        ConfigurationLoadResult<ProbeConfigurationDocument> loaded = await store.LoadAsync();
        string expectedName = checkpoint == AtomicConfigurationSaveCheckpoint.AfterCommit
            ? "candidate"
            : "baseline";

        Require(
            loaded.Status == ConfigurationLoadStatus.LoadedPrimary,
            $"Checkpoint {checkpoint} left no valid primary.");
        Require(
            loaded.Document?.Containers[0].Name == expectedName,
            $"Checkpoint {checkpoint} produced an unexpected committed version.");
    }
}

async Task VerifyProcessTerminationCheckpointsAsync(string directory, int count)
{
    foreach (AtomicConfigurationSaveCheckpoint checkpoint in Enum.GetValues<AtomicConfigurationSaveCheckpoint>())
    {
        for (int iteration = 0; iteration < count; iteration++)
        {
            string checkpointDirectory = Path.Combine(
                directory,
                checkpoint.ToString(),
                iteration.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AtomicJsonConfigurationStore<ProbeConfigurationDocument> store =
                CreateStore(checkpointDirectory);
            await store.SaveAsync(CreateDocument("baseline"));

            string readyPath = Path.Combine(checkpointDirectory, "child.ready");
            string candidateName = $"candidate-{iteration}";
            ProcessStartInfo startInfo = CreateChildStartInfo(
                checkpointDirectory,
                candidateName,
                checkpoint,
                readyPath);

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The persistence child process did not start.");

            try
            {
                await WaitForChildReadyAsync(process, readyPath);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }

            Require(process.ExitCode != 0, "The child process was not forcefully terminated.");

            ConfigurationLoadResult<ProbeConfigurationDocument> loaded = await store.LoadAsync();
            string expectedName = checkpoint == AtomicConfigurationSaveCheckpoint.AfterCommit
                ? candidateName
                : "baseline";

            Require(
                loaded.Status == ConfigurationLoadStatus.LoadedPrimary,
                $"Process termination at {checkpoint} left no valid primary.");
            Require(
                loaded.Document?.Containers[0].Name == expectedName,
                $"Process termination at {checkpoint} exposed an unexpected version.");
        }
    }
}

async Task VerifySchemaMigrationAndRollbackAsync(string directory)
{
    const string v1Json = """
        {
          "schemaVersion": 1,
          "profileId": "default",
          "containers": [
            {
              "id": "container-1",
              "name": "migration-source",
              "futureContainerProperty": {
                "enabled": true
              }
            }
          ],
          "futureRootProperty": [1, 2, 3]
        }
        """;

    JsonObject source = JsonNode.Parse(v1Json)?.AsObject()
        ?? throw new InvalidDataException("The migration fixture did not parse.");
    string sourceBefore = source.ToJsonString(ProbeJsonOptions.Configuration);
    AtomicJsonConfigurationStore<JsonObject> store = new(
        directory,
        "profile.json",
        ProbeConfigurationMigration.ValidateVersionedDocument);
    JsonConfigurationMigrator migrator = ProbeConfigurationMigration.CreateMigrator();

    await store.SaveAsync(source);
    byte[] committedV1 = await File.ReadAllBytesAsync(store.PrimaryPath);

    foreach (ConfigurationMigrationCheckpoint checkpoint
        in Enum.GetValues<ConfigurationMigrationCheckpoint>())
    {
        try
        {
            _ = migrator.Migrate(source, checkpoint);
            throw new InvalidOperationException(
                $"Migration checkpoint {checkpoint} did not inject a failure.");
        }
        catch (InjectedMigrationFailureException exception)
            when (exception.Checkpoint == checkpoint)
        {
        }

        Require(
            source.ToJsonString(ProbeJsonOptions.Configuration) == sourceBefore,
            $"Migration checkpoint {checkpoint} mutated the source document.");
        Require(
            committedV1.SequenceEqual(await File.ReadAllBytesAsync(store.PrimaryPath)),
            $"Migration checkpoint {checkpoint} changed the committed configuration.");
    }

    ConfigurationMigrationResult first = migrator.Migrate(source);
    ConfigurationMigrationResult second = migrator.Migrate(source);
    Require(first.Status == ConfigurationMigrationStatus.Migrated, "v1 was not migrated.");
    Require(
        first.Document.ToJsonString(ProbeJsonOptions.Configuration)
            == second.Document.ToJsonString(ProbeJsonOptions.Configuration),
        "Repeated migration of the same source was not deterministic.");
    Require(
        source.ToJsonString(ProbeJsonOptions.Configuration) == sourceBefore,
        "Successful migration mutated the source document.");
    Require(
        first.Document["futureRootProperty"] is JsonArray,
        "Migration lost an unknown root property.");
    Require(
        first.Document["containers"]?[0]?["futureContainerProperty"] is JsonObject,
        "Migration lost an unknown nested property.");

    ConfigurationMigrationResult alreadyCurrent = migrator.Migrate(first.Document);
    Require(
        alreadyCurrent.Status == ConfigurationMigrationStatus.AlreadyCurrent,
        "Current schema was not recognized as idempotent.");
    Require(
        alreadyCurrent.Document.ToJsonString(ProbeJsonOptions.Configuration)
            == first.Document.ToJsonString(ProbeJsonOptions.Configuration),
        "Current-schema migration changed the document.");

    await store.SaveAsync(first.Document);
    ConfigurationLoadResult<JsonObject> loadedV2 = await store.LoadAsync();
    Require(
        loadedV2.Status == ConfigurationLoadStatus.LoadedPrimary,
        "Migrated v2 document was not committed.");
    Require(
        loadedV2.Document?["schemaVersion"]?.GetValue<int>() == 2,
        "Committed migration did not contain schema v2.");

    JsonObject backupV1 = JsonNode.Parse(await File.ReadAllTextAsync(store.BackupPath))?.AsObject()
        ?? throw new InvalidDataException("The migration backup did not parse.");
    Require(
        backupV1["schemaVersion"]?.GetValue<int>() == 1,
        "The migration backup did not preserve schema v1.");

    await File.WriteAllTextAsync(store.PrimaryPath, "{ damaged-v2");
    ConfigurationLoadResult<JsonObject> recovered = await store.LoadAsync();
    Require(
        recovered.Status == ConfigurationLoadStatus.RecoveredFromBackup,
        "Damaged v2 did not recover the committed v1 backup.");
    Require(
        recovered.Document?["schemaVersion"]?.GetValue<int>() == 1,
        "Migration rollback did not return schema v1.");

    JsonObject future = (JsonObject)source.DeepClone();
    future["schemaVersion"] = 3;
    string futureBefore = future.ToJsonString(ProbeJsonOptions.Configuration);

    try
    {
        _ = migrator.Migrate(future);
        throw new InvalidOperationException("Future schema was silently downgraded.");
    }
    catch (NotSupportedException)
    {
    }

    Require(
        future.ToJsonString(ProbeJsonOptions.Configuration) == futureBefore,
        "Rejected future schema was mutated.");

    JsonObject conflictingSource = (JsonObject)source.DeepClone();
    conflictingSource["persistenceProbe"] = new JsonObject
    {
        ["ownedByFutureVersion"] = true,
    };
    string conflictingBefore = conflictingSource.ToJsonString(ProbeJsonOptions.Configuration);

    try
    {
        _ = migrator.Migrate(conflictingSource);
        throw new InvalidOperationException("Migration silently overwrote an unknown field.");
    }
    catch (InvalidDataException)
    {
    }

    Require(
        conflictingSource.ToJsonString(ProbeJsonOptions.Configuration) == conflictingBefore,
        "Rejected field conflict mutated the source document.");
}

async Task VerifyStaleTemporaryFileIsIgnoredAsync(string directory)
{
    AtomicJsonConfigurationStore<ProbeConfigurationDocument> store = CreateStore(directory);
    await store.SaveAsync(CreateDocument("primary"));
    await File.WriteAllTextAsync(store.TemporaryPath, "{ interrupted");

    ConfigurationLoadResult<ProbeConfigurationDocument> loaded = await store.LoadAsync();
    Require(loaded.Status == ConfigurationLoadStatus.LoadedPrimary, "Stale .new file affected loading.");
    Require(loaded.Document?.Containers[0].Name == "primary", "Primary content changed.");
}

async Task VerifyUnknownFieldsSurviveAsync(string directory)
{
    const string json = """
        {
          "schemaVersion": 1,
          "profileId": "default",
          "containers": [
            {
              "id": "container-1",
              "name": "unknown-field",
              "futureContainerProperty": { "enabled": true }
            }
          ],
          "futureRootProperty": [1, 2, 3]
        }
        """;

    ProbeConfigurationDocument document = JsonSerializer.Deserialize<ProbeConfigurationDocument>(
        json,
        ProbeJsonOptions.Configuration)
        ?? throw new InvalidDataException("Unknown-field fixture did not deserialize.");

    AtomicJsonConfigurationStore<ProbeConfigurationDocument> store = CreateStore(directory);
    await store.SaveAsync(document);
    ConfigurationLoadResult<ProbeConfigurationDocument> loaded = await store.LoadAsync();

    Require(
        loaded.Document?.ExtensionData?.ContainsKey("futureRootProperty") == true,
        "Unknown root property was lost.");
    Require(
        loaded.Document?.Containers[0].ExtensionData?.ContainsKey("futureContainerProperty") == true,
        "Unknown container property was lost.");
}

async Task VerifyBoundedDocumentSizeAsync(string directory)
{
    AtomicJsonConfigurationStore<ProbeConfigurationDocument> store = CreateStore(
        directory,
        maximumDocumentBytes: 256);
    Directory.CreateDirectory(directory);
    await File.WriteAllTextAsync(store.PrimaryPath, new string('x', 512));

    ConfigurationLoadResult<ProbeConfigurationDocument> result = await store.LoadAsync();
    Require(result.Status == ConfigurationLoadStatus.SafeMode, "Oversized input did not enter safe mode.");
    Require(
        result.PrimaryFailure == ConfigurationValidationFailure.TooLarge,
        "Oversized input was not classified correctly.");
}

async Task VerifyRepeatedSaveAsync(string directory, int count)
{
    AtomicJsonConfigurationStore<ProbeConfigurationDocument> store = CreateStore(directory);

    for (int index = 0; index < count; index++)
    {
        string name = $"version-{index}";
        await store.SaveAsync(CreateDocument(name));
        ConfigurationLoadResult<ProbeConfigurationDocument> loaded = await store.LoadAsync();
        Require(loaded.Status == ConfigurationLoadStatus.LoadedPrimary, "Repeated save lost primary.");
        Require(loaded.Document?.Containers[0].Name == name, "Repeated save returned stale data.");
    }
}

async Task RunScenarioAsync(
    string name,
    Func<string, Task> scenario,
    ICollection<ScenarioResult> destination)
{
    string scenarioDirectory = Path.Combine(sandbox, name);

    try
    {
        await scenario(scenarioDirectory);
        destination.Add(new(name, true, "All assertions passed."));
    }
    catch (Exception exception)
    {
        destination.Add(new(name, false, exception.GetType().Name));
    }
}

static ProcessStartInfo CreateChildStartInfo(
    string directory,
    string candidateName,
    AtomicConfigurationSaveCheckpoint checkpoint,
    string readyPath)
{
    string processPath = Environment.ProcessPath
        ?? throw new InvalidOperationException("The current process path is unavailable.");
    ProcessStartInfo startInfo = new(processPath)
    {
        CreateNoWindow = true,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
        WindowStyle = ProcessWindowStyle.Hidden,
    };

    if (string.Equals(
        Path.GetFileNameWithoutExtension(processPath),
        "dotnet",
        StringComparison.OrdinalIgnoreCase))
    {
        string entryAssemblyPath = Environment.GetCommandLineArgs()[0];
        startInfo.ArgumentList.Add(entryAssemblyPath);
    }

    startInfo.ArgumentList.Add("--child-save");
    startInfo.ArgumentList.Add(directory);
    startInfo.ArgumentList.Add(candidateName);
    startInfo.ArgumentList.Add(checkpoint.ToString());
    startInfo.ArgumentList.Add(readyPath);
    return startInfo;
}

static async Task WaitForChildReadyAsync(Process process, string readyPath)
{
    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));

    while (!File.Exists(readyPath))
    {
        if (process.HasExited)
        {
            throw new InvalidOperationException(
                $"The persistence child exited before the checkpoint with code {process.ExitCode}.");
        }

        await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
    }
}

static async Task<int> RunChildSaveAsync(string[] arguments)
{
    if (arguments.Length != 5
        || !Enum.TryParse(
            arguments[3],
            ignoreCase: false,
            out AtomicConfigurationSaveCheckpoint checkpoint))
    {
        return 2;
    }

    string directory = arguments[1];
    string candidateName = arguments[2];
    string readyPath = arguments[4];
    string fullDirectory = Path.GetFullPath(directory);
    string fullReadyPath = Path.GetFullPath(readyPath);

    if (!string.Equals(
        Path.GetDirectoryName(fullReadyPath),
        fullDirectory,
        StringComparison.OrdinalIgnoreCase))
    {
        return 2;
    }

    AtomicJsonConfigurationStore<ProbeConfigurationDocument> store = CreateStore(directory);

    await store.SaveAsync(
        CreateDocument(candidateName),
        checkpointObserver: async (current, cancellationToken) =>
        {
            if (current != checkpoint)
            {
                return;
            }

            await File.WriteAllTextAsync(fullReadyPath, current.ToString(), cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });

    return 0;
}

static async Task RequireNormalSaveRefusedAsync(
    AtomicJsonConfigurationStore<ProbeConfigurationDocument> store,
    string candidateName)
{
    try
    {
        await store.SaveAsync(CreateDocument(candidateName));
        throw new InvalidOperationException("Normal save overwrote damaged configuration evidence.");
    }
    catch (InvalidDataException)
    {
    }
}

static AtomicJsonConfigurationStore<ProbeConfigurationDocument> CreateStore(
    string directory,
    long maximumDocumentBytes = 4 * 1024 * 1024) =>
    new(
        directory,
        "profile.json",
        ProbeConfigurationValidation.Validate,
        maximumDocumentBytes);

static ProbeConfigurationDocument CreateDocument(string containerName) =>
    new()
    {
        SchemaVersion = 1,
        ProfileId = "default",
        Containers =
        [
            new ProbeContainer
            {
                Id = "container-1",
                Name = containerName,
            },
        ],
    };

static int ParseIterations(string[] arguments)
{
    return ParseBoundedOption(arguments, "--iterations", defaultValue: 100, maximum: 10_000);
}

static int ParseBoundedOption(
    string[] arguments,
    string optionName,
    int defaultValue,
    int maximum)
{
    int optionIndex = Array.IndexOf(arguments, optionName);
    if (optionIndex < 0)
    {
        return defaultValue;
    }

    if (optionIndex + 1 >= arguments.Length
        || !int.TryParse(arguments[optionIndex + 1], out int value)
        || value < 1
        || value > maximum)
    {
        throw new ArgumentException(
            $"{optionName} must be an integer between 1 and {maximum}.");
    }

    return value;
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed record ScenarioResult(string Name, bool Passed, string Detail);

internal static class ProbeJsonOptions
{
    public static JsonSerializerOptions Output { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static JsonSerializerOptions Configuration { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
