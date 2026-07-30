using System.Text.Json;
using LongGrid.Spikes.ConfigurationPersistence;

bool jsonOutput = args.Contains("--json", StringComparer.Ordinal);
int iterations = ParseIterations(args);
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

AtomicJsonConfigurationStore<ProbeConfigurationDocument> CreateStore(
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
    int optionIndex = Array.IndexOf(arguments, "--iterations");
    if (optionIndex < 0)
    {
        return 100;
    }

    if (optionIndex + 1 >= arguments.Length
        || !int.TryParse(arguments[optionIndex + 1], out int iterations)
        || iterations is < 1 or > 10_000)
    {
        throw new ArgumentException("--iterations must be an integer between 1 and 10000.");
    }

    return iterations;
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
