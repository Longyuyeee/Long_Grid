using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
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
    await RunScenarioAsync("concurrent-writer-lease", VerifyConcurrentWriterLeaseAsync, results);
    await RunScenarioAsync("schema-migration-and-rollback", VerifySchemaMigrationAndRollbackAsync, results);
    await RunScenarioAsync("read-only-file-recovery", VerifyReadOnlyFileRecoveryAsync, results);
    await RunScenarioAsync("simulated-disk-full-recovery", VerifyDiskFullRecoveryAsync, results);
    await RunScenarioAsync("directory-acl-denial-recovery", VerifyDirectoryAclRecoveryAsync, results);
    await RunScenarioAsync(
        "directory-read-only-attribute-semantics",
        VerifyDirectoryReadOnlyAttributeSemanticsAsync,
        results);
    await RunScenarioAsync(
        "replace-acl-denial-recovery",
        VerifyReplaceAclDenialRecoveryAsync,
        results);
    await RunScenarioAsync(
        "primary-write-acl-semantics",
        VerifyPrimaryWriteAclSemanticsAsync,
        results);
    await RunScenarioAsync(
        "inherited-replace-acl-recovery",
        VerifyInheritedReplaceAclRecoveryAsync,
        results);
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

async Task VerifyConcurrentWriterLeaseAsync(string directory)
{
    AtomicJsonConfigurationStore<ProbeConfigurationDocument> store = CreateStore(directory);
    await store.SaveAsync(CreateDocument("baseline"));

    string readyPath = Path.Combine(directory, "lease-holder.ready");
    ProcessStartInfo startInfo = CreateChildStartInfo(
        directory,
        "child-candidate",
        AtomicConfigurationSaveCheckpoint.AfterTempFlush,
        readyPath);

    using Process process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("The lease-holder child process did not start.");

    try
    {
        await WaitForChildReadyAsync(process, readyPath);
        Require(File.Exists(store.TemporaryPath), "The lease holder did not stage a .new file.");

        try
        {
            await store.SaveAsync(CreateDocument("competing-writer"));
            throw new InvalidOperationException("A competing writer acquired the active lease.");
        }
        catch (ConfigurationWriteLeaseException)
        {
        }

        Require(
            File.Exists(store.TemporaryPath),
            "The rejected writer removed the lease holder's staged file.");
        ConfigurationLoadResult<ProbeConfigurationDocument> duringContention =
            await store.LoadAsync();
        Require(
            duringContention.Status == ConfigurationLoadStatus.LoadedPrimary,
            "A reader could not load the committed primary during contention.");
        Require(
            duringContention.Document?.Containers[0].Name == "baseline",
            "A reader observed uncommitted concurrent content.");

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

    await WaitForWriteLeaseReleaseAsync(store.WriteLeasePath);

    FileStreamOptions oldReaderOptions = new()
    {
        Access = FileAccess.Read,
        Mode = FileMode.Open,
        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        Share = FileShare.Read | FileShare.Delete,
    };

    await using FileStream oldReader = new(store.PrimaryPath, oldReaderOptions);
    await store.SaveAsync(CreateDocument("after-lease-release"));
    ProbeConfigurationDocument oldSnapshot =
        await JsonSerializer.DeserializeAsync<ProbeConfigurationDocument>(
            oldReader,
            ProbeJsonOptions.Configuration)
        ?? throw new InvalidDataException("The pre-commit reader snapshot did not deserialize.");

    Require(
        oldSnapshot.Containers[0].Name == "baseline",
        "An existing reader did not retain the pre-replacement snapshot.");

    ConfigurationLoadResult<ProbeConfigurationDocument> recovered = await store.LoadAsync();
    Require(
        recovered.Status == ConfigurationLoadStatus.LoadedPrimary,
        "The write lease was not reusable after process termination.");
    Require(
        recovered.Document?.Containers[0].Name == "after-lease-release",
        "The post-termination writer did not commit.");
    Require(!File.Exists(store.TemporaryPath), "The abandoned staged file was not cleaned.");
}

async Task VerifyReadOnlyFileRecoveryAsync(string directory)
{
    await VerifyReadOnlyPathAsync(
        Path.Combine(directory, "primary"),
        store => store.PrimaryPath,
        createTargetAsync: _ => Task.CompletedTask);
    await VerifyReadOnlyPathAsync(
        Path.Combine(directory, "backup"),
        store => store.BackupPath,
        createTargetAsync: store => store.SaveAsync(CreateDocument("second-baseline")));
    await VerifyReadOnlyPathAsync(
        Path.Combine(directory, "temporary"),
        store => store.TemporaryPath,
        createTargetAsync: store => File.WriteAllTextAsync(store.TemporaryPath, "{ stale"));
    await VerifyReadOnlyPathAsync(
        Path.Combine(directory, "lease"),
        store => store.WriteLeasePath,
        createTargetAsync: _ => Task.CompletedTask);
}

async Task VerifyDiskFullRecoveryAsync(string directory)
{
    string firstSaveDirectory = Path.Combine(directory, "first-save");
    AtomicJsonConfigurationStore<ProbeConfigurationDocument> firstSaveStore =
        CreateStore(firstSaveDirectory);

    await RequireDiskFullAsync(
        () => firstSaveStore.SaveAsync(
            CreateDocument("must-not-commit"),
            injectedDiskFull: ConfigurationDiskFullCheckpoint.DuringTempWrite),
        ConfigurationDiskFullCheckpoint.DuringTempWrite);
    Require(!File.Exists(firstSaveStore.PrimaryPath), "Failed first save published a primary.");
    Require(!File.Exists(firstSaveStore.BackupPath), "Failed first save published a backup.");
    Require(!File.Exists(firstSaveStore.TemporaryPath), "Failed first save retained a partial .new.");
    ConfigurationLoadResult<ProbeConfigurationDocument> missing = await firstSaveStore.LoadAsync();
    Require(missing.Status == ConfigurationLoadStatus.Missing, "Failed first save was not missing.");
    await firstSaveStore.SaveAsync(CreateDocument("first-save-recovered"));
    Require(
        (await firstSaveStore.LoadAsync()).Document?.Containers[0].Name == "first-save-recovered",
        "First save did not recover after simulated disk full.");

    foreach (ConfigurationDiskFullCheckpoint checkpoint
        in Enum.GetValues<ConfigurationDiskFullCheckpoint>())
    {
        string checkpointDirectory = Path.Combine(directory, checkpoint.ToString());
        AtomicJsonConfigurationStore<ProbeConfigurationDocument> store =
            CreateStore(checkpointDirectory);
        await store.SaveAsync(CreateDocument("previous"));
        await store.SaveAsync(CreateDocument("baseline"));
        byte[] primaryBefore = await File.ReadAllBytesAsync(store.PrimaryPath);
        byte[] backupBefore = await File.ReadAllBytesAsync(store.BackupPath);

        await RequireDiskFullAsync(
            () => store.SaveAsync(
                CreateDocument("must-not-commit"),
                injectedDiskFull: checkpoint),
            checkpoint);

        Require(
            primaryBefore.SequenceEqual(await File.ReadAllBytesAsync(store.PrimaryPath)),
            $"Disk full at {checkpoint} changed the committed primary.");
        Require(
            backupBefore.SequenceEqual(await File.ReadAllBytesAsync(store.BackupPath)),
            $"Disk full at {checkpoint} changed the backup.");
        Require(
            !File.Exists(store.TemporaryPath),
            $"Disk full at {checkpoint} retained an incomplete .new.");

        ConfigurationLoadResult<ProbeConfigurationDocument> loaded = await store.LoadAsync();
        Require(
            loaded.Status == ConfigurationLoadStatus.LoadedPrimary,
            $"Disk full at {checkpoint} left no valid primary.");
        Require(
            loaded.Document?.Containers[0].Name == "baseline",
            $"Disk full at {checkpoint} exposed uncommitted content.");

        await store.SaveAsync(CreateDocument("recovered"));
        Require(
            (await store.LoadAsync()).Document?.Containers[0].Name == "recovered",
            $"Write did not recover after disk full at {checkpoint}.");
    }
}

async Task VerifyDirectoryAclRecoveryAsync(string directory)
{
    if (!OperatingSystem.IsWindows())
    {
        throw new PlatformNotSupportedException(
            "The directory ACL recovery scenario requires Windows.");
    }

    AtomicJsonConfigurationStore<ProbeConfigurationDocument> store = CreateStore(directory);
    await store.SaveAsync(CreateDocument("previous"));
    await store.SaveAsync(CreateDocument("baseline"));
    byte[] primaryBefore = await File.ReadAllBytesAsync(store.PrimaryPath);
    byte[] backupBefore = await File.ReadAllBytesAsync(store.BackupPath);

    DirectoryInfo directoryInfo = new(directory);
    DirectorySecurity originalSecurity = FileSystemAclExtensions.GetAccessControl(
        directoryInfo,
        AccessControlSections.Access);
    byte[] originalDescriptor = originalSecurity.GetSecurityDescriptorBinaryForm();
    string[] originalDaclRules = GetCanonicalAccessRules(originalSecurity);
    using WindowsIdentity currentIdentity = WindowsIdentity.GetCurrent();
    SecurityIdentifier currentUser = currentIdentity.User
        ?? throw new InvalidOperationException("The current Windows user has no SID.");
    FileSystemAccessRule denyFileCreation = new(
        currentUser,
        FileSystemRights.CreateFiles,
        InheritanceFlags.None,
        PropagationFlags.None,
        AccessControlType.Deny);
    DirectorySecurity deniedSecurity = new();
    deniedSecurity.SetSecurityDescriptorBinaryForm(
        originalDescriptor,
        AccessControlSections.Access);
    deniedSecurity.AddAccessRule(denyFileCreation);
    bool denialApplied = false;

    try
    {
        FileSystemAclExtensions.SetAccessControl(directoryInfo, deniedSecurity);
        denialApplied = true;

        await RequireFileSystemSaveFailureAsync(
            () => store.SaveAsync(CreateDocument("must-not-commit")),
            unexpectedSuccessCode: "acl-denial-not-enforced");
        RequireProbe(
            primaryBefore.SequenceEqual(await File.ReadAllBytesAsync(store.PrimaryPath)),
            "acl-primary-changed");
        RequireProbe(
            backupBefore.SequenceEqual(await File.ReadAllBytesAsync(store.BackupPath)),
            "acl-backup-changed");
        RequireProbe(
            !File.Exists(store.TemporaryPath),
            "acl-temp-retained");
    }
    finally
    {
        if (denialApplied)
        {
            DirectorySecurity restoredSecurity = new();
            restoredSecurity.SetSecurityDescriptorBinaryForm(
                originalDescriptor,
                AccessControlSections.Access);
            FileSystemAclExtensions.SetAccessControl(directoryInfo, restoredSecurity);
        }
    }

    DirectorySecurity restoredSecurityCheck = FileSystemAclExtensions.GetAccessControl(
        directoryInfo,
        AccessControlSections.Access);
    RequireProbe(
        GetCanonicalAccessRules(restoredSecurityCheck).SequenceEqual(originalDaclRules),
        "acl-rules-not-restored");

    await store.SaveAsync(CreateDocument("recovered"));
    ConfigurationLoadResult<ProbeConfigurationDocument> loaded = await store.LoadAsync();
    RequireProbe(
        loaded.Status == ConfigurationLoadStatus.LoadedPrimary,
        "acl-recovery-load-failed");
    RequireProbe(
        loaded.Document?.Containers[0].Name == "recovered",
        "acl-recovery-not-committed");
}

async Task VerifyDirectoryReadOnlyAttributeSemanticsAsync(string directory)
{
    if (!OperatingSystem.IsWindows())
    {
        throw new PlatformNotSupportedException(
            "The directory read-only attribute scenario requires Windows.");
    }

    AtomicJsonConfigurationStore<ProbeConfigurationDocument> store = CreateStore(directory);
    await store.SaveAsync(CreateDocument("previous"));
    await store.SaveAsync(CreateDocument("baseline"));
    FileAttributes originalAttributes = File.GetAttributes(directory);

    try
    {
        File.SetAttributes(directory, originalAttributes | FileAttributes.ReadOnly);
        RequireProbe(
            File.GetAttributes(directory).HasFlag(FileAttributes.ReadOnly),
            "directory-read-only-attribute-not-set");

        await store.SaveAsync(CreateDocument("attribute-does-not-deny-write"));
        ConfigurationLoadResult<ProbeConfigurationDocument> loaded = await store.LoadAsync();
        RequireProbe(
            loaded.Status == ConfigurationLoadStatus.LoadedPrimary,
            "directory-read-only-primary-not-loaded");
        RequireProbe(
            loaded.Document?.Containers[0].Name == "attribute-does-not-deny-write",
            "directory-read-only-blocked-commit");

        ProbeConfigurationDocument backup =
            JsonSerializer.Deserialize<ProbeConfigurationDocument>(
                await File.ReadAllTextAsync(store.BackupPath),
                ProbeJsonOptions.Configuration)
            ?? throw new InvalidDataException("The directory attribute backup did not parse.");
        RequireProbe(
            backup.Containers[0].Name == "baseline",
            "directory-read-only-backup-invalid");
    }
    finally
    {
        File.SetAttributes(directory, originalAttributes);
    }

    RequireProbe(
        File.GetAttributes(directory) == originalAttributes,
        "directory-attributes-not-restored");
    await store.SaveAsync(CreateDocument("restored"));
    RequireProbe(
        (await store.LoadAsync()).Document?.Containers[0].Name == "restored",
        "directory-attribute-recovery-not-committed");
}

async Task VerifyReplaceAclDenialRecoveryAsync(string directory)
{
    if (!OperatingSystem.IsWindows())
    {
        throw new PlatformNotSupportedException(
            "The replacement ACL recovery scenario requires Windows.");
    }

    AtomicJsonConfigurationStore<ProbeConfigurationDocument> store = CreateStore(directory);
    await store.SaveAsync(CreateDocument("previous"));
    await store.SaveAsync(CreateDocument("baseline"));
    byte[] primaryBefore = await File.ReadAllBytesAsync(store.PrimaryPath);
    byte[] backupBefore = await File.ReadAllBytesAsync(store.BackupPath);

    DirectoryInfo directoryInfo = new(directory);
    FileInfo primaryInfo = new(store.PrimaryPath);
    FileInfo backupInfo = new(store.BackupPath);
    DirectorySecurity originalDirectorySecurity = FileSystemAclExtensions.GetAccessControl(
        directoryInfo,
        AccessControlSections.Access);
    FileSecurity originalPrimarySecurity = FileSystemAclExtensions.GetAccessControl(
        primaryInfo,
        AccessControlSections.Access);
    FileSecurity originalBackupSecurity = FileSystemAclExtensions.GetAccessControl(
        backupInfo,
        AccessControlSections.Access);
    byte[] originalDirectoryDescriptor =
        originalDirectorySecurity.GetSecurityDescriptorBinaryForm();
    byte[] originalPrimaryDescriptor = originalPrimarySecurity.GetSecurityDescriptorBinaryForm();
    byte[] originalBackupDescriptor = originalBackupSecurity.GetSecurityDescriptorBinaryForm();
    string[] originalDirectoryRules = GetCanonicalAccessRules(originalDirectorySecurity);
    string[] originalPrimaryRules = GetCanonicalAccessRules(originalPrimarySecurity);
    string[] originalBackupRules = GetCanonicalAccessRules(originalBackupSecurity);

    using WindowsIdentity currentIdentity = WindowsIdentity.GetCurrent();
    SecurityIdentifier currentUser = currentIdentity.User
        ?? throw new InvalidOperationException("The current Windows user has no SID.");
    FileSystemAccessRule denyDeleteChildren = new(
        currentUser,
        FileSystemRights.DeleteSubdirectoriesAndFiles,
        InheritanceFlags.None,
        PropagationFlags.None,
        AccessControlType.Deny);
    FileSystemAccessRule denyDeleteFile = new(
        currentUser,
        FileSystemRights.Delete,
        AccessControlType.Deny);
    DirectorySecurity deniedDirectorySecurity = new();
    deniedDirectorySecurity.SetSecurityDescriptorBinaryForm(
        originalDirectoryDescriptor,
        AccessControlSections.Access);
    deniedDirectorySecurity.AddAccessRule(denyDeleteChildren);
    FileSecurity deniedPrimarySecurity = new();
    deniedPrimarySecurity.SetSecurityDescriptorBinaryForm(
        originalPrimaryDescriptor,
        AccessControlSections.Access);
    deniedPrimarySecurity.AddAccessRule(denyDeleteFile);
    FileSecurity deniedBackupSecurity = new();
    deniedBackupSecurity.SetSecurityDescriptorBinaryForm(
        originalBackupDescriptor,
        AccessControlSections.Access);
    deniedBackupSecurity.AddAccessRule(denyDeleteFile);
    bool directoryDenialApplied = false;
    bool primaryDenialApplied = false;
    bool backupDenialApplied = false;

    try
    {
        FileSystemAclExtensions.SetAccessControl(directoryInfo, deniedDirectorySecurity);
        directoryDenialApplied = true;
        FileSystemAclExtensions.SetAccessControl(primaryInfo, deniedPrimarySecurity);
        primaryDenialApplied = true;
        FileSystemAclExtensions.SetAccessControl(backupInfo, deniedBackupSecurity);
        backupDenialApplied = true;

        await RequireFileSystemSaveFailureAsync(
            () => store.SaveAsync(CreateDocument("must-not-commit")),
            unexpectedSuccessCode: "acl-replace-denial-not-enforced");
        RequireProbe(
            primaryBefore.SequenceEqual(await File.ReadAllBytesAsync(store.PrimaryPath)),
            "acl-replace-primary-changed");
        RequireProbe(
            backupBefore.SequenceEqual(await File.ReadAllBytesAsync(store.BackupPath)),
            "acl-replace-backup-changed");
        RequireProbe(!File.Exists(store.TemporaryPath), "acl-replace-temp-retained");
    }
    finally
    {
        if (backupDenialApplied)
        {
            FileSecurity restoredBackupSecurity = new();
            restoredBackupSecurity.SetSecurityDescriptorBinaryForm(
                originalBackupDescriptor,
                AccessControlSections.Access);
            FileSystemAclExtensions.SetAccessControl(backupInfo, restoredBackupSecurity);
        }

        if (primaryDenialApplied)
        {
            FileSecurity restoredPrimarySecurity = new();
            restoredPrimarySecurity.SetSecurityDescriptorBinaryForm(
                originalPrimaryDescriptor,
                AccessControlSections.Access);
            FileSystemAclExtensions.SetAccessControl(primaryInfo, restoredPrimarySecurity);
        }

        if (directoryDenialApplied)
        {
            DirectorySecurity restoredDirectorySecurity = new();
            restoredDirectorySecurity.SetSecurityDescriptorBinaryForm(
                originalDirectoryDescriptor,
                AccessControlSections.Access);
            FileSystemAclExtensions.SetAccessControl(directoryInfo, restoredDirectorySecurity);
        }
    }

    RequireProbe(
        GetCanonicalAccessRules(FileSystemAclExtensions.GetAccessControl(
            directoryInfo,
            AccessControlSections.Access)).SequenceEqual(originalDirectoryRules),
        "acl-replace-directory-rules-not-restored");
    RequireProbe(
        GetCanonicalAccessRules(FileSystemAclExtensions.GetAccessControl(
            primaryInfo,
            AccessControlSections.Access)).SequenceEqual(originalPrimaryRules),
        "acl-replace-primary-rules-not-restored");
    RequireProbe(
        GetCanonicalAccessRules(FileSystemAclExtensions.GetAccessControl(
            backupInfo,
            AccessControlSections.Access)).SequenceEqual(originalBackupRules),
        "acl-replace-backup-rules-not-restored");

    await store.SaveAsync(CreateDocument("recovered"));
    RequireProbe(
        (await store.LoadAsync()).Document?.Containers[0].Name == "recovered",
        "acl-replace-recovery-not-committed");
}

async Task VerifyPrimaryWriteAclSemanticsAsync(string directory)
{
    if (!OperatingSystem.IsWindows())
    {
        throw new PlatformNotSupportedException(
            "The primary write ACL semantics scenario requires Windows.");
    }

    AtomicJsonConfigurationStore<ProbeConfigurationDocument> store = CreateStore(directory);
    await store.SaveAsync(CreateDocument("previous"));
    await store.SaveAsync(CreateDocument("baseline"));

    FileInfo primaryInfo = new(store.PrimaryPath);
    FileInfo backupInfo = new(store.BackupPath);
    FileSecurity originalPrimarySecurity = FileSystemAclExtensions.GetAccessControl(
        primaryInfo,
        AccessControlSections.Access);
    FileSecurity originalBackupSecurity = FileSystemAclExtensions.GetAccessControl(
        backupInfo,
        AccessControlSections.Access);
    byte[] originalPrimaryDescriptor = originalPrimarySecurity.GetSecurityDescriptorBinaryForm();
    byte[] originalBackupDescriptor = originalBackupSecurity.GetSecurityDescriptorBinaryForm();
    string[] originalPrimaryRules = GetCanonicalAccessRules(originalPrimarySecurity);
    string[] originalBackupRules = GetCanonicalAccessRules(originalBackupSecurity);

    using WindowsIdentity currentIdentity = WindowsIdentity.GetCurrent();
    SecurityIdentifier currentUser = currentIdentity.User
        ?? throw new InvalidOperationException("The current Windows user has no SID.");
    FileSystemAccessRule denyWriteData = new(
        currentUser,
        FileSystemRights.WriteData,
        AccessControlType.Deny);
    FileSecurity deniedPrimarySecurity = new();
    deniedPrimarySecurity.SetSecurityDescriptorBinaryForm(
        originalPrimaryDescriptor,
        AccessControlSections.Access);
    deniedPrimarySecurity.AddAccessRule(denyWriteData);
    bool primaryDenialApplied = false;

    try
    {
        FileSystemAclExtensions.SetAccessControl(primaryInfo, deniedPrimarySecurity);
        primaryDenialApplied = true;

        ConfigurationLoadResult<ProbeConfigurationDocument> loaded = await store.LoadAsync();
        RequireProbe(
            loaded.Status == ConfigurationLoadStatus.LoadedPrimary
                && loaded.Document?.Containers[0].Name == "baseline",
            "acl-write-primary-not-loaded");

        try
        {
            await store.SaveAsync(CreateDocument("candidate"));
        }
        catch (IOException)
        {
            throw new ProbeAssertionException("acl-write-replacement-blocked");
        }
        catch (UnauthorizedAccessException)
        {
            throw new ProbeAssertionException("acl-write-replacement-blocked");
        }

        RequireProbe(
            (await store.LoadAsync()).Document?.Containers[0].Name == "candidate",
            "acl-write-primary-invalid");
        ProbeConfigurationDocument backup =
            JsonSerializer.Deserialize<ProbeConfigurationDocument>(
                await File.ReadAllTextAsync(store.BackupPath),
                ProbeJsonOptions.Configuration)
            ?? throw new ProbeAssertionException("acl-write-backup-invalid");
        RequireProbe(
            backup.Containers[0].Name == "baseline",
            "acl-write-backup-invalid");
        RequireProbe(
            HasDenyRule(
                FileSystemAclExtensions.GetAccessControl(
                    primaryInfo,
                    AccessControlSections.Access),
                currentUser,
                FileSystemRights.WriteData),
            "acl-write-denial-not-preserved");
    }
    finally
    {
        if (primaryDenialApplied)
        {
            FileSecurity restoredPrimarySecurity = new();
            restoredPrimarySecurity.SetSecurityDescriptorBinaryForm(
                originalPrimaryDescriptor,
                AccessControlSections.Access);
            FileSystemAclExtensions.SetAccessControl(primaryInfo, restoredPrimarySecurity);

            FileSecurity restoredBackupSecurity = new();
            restoredBackupSecurity.SetSecurityDescriptorBinaryForm(
                originalBackupDescriptor,
                AccessControlSections.Access);
            FileSystemAclExtensions.SetAccessControl(backupInfo, restoredBackupSecurity);
        }
    }

    RequireProbe(
        GetCanonicalAccessRules(FileSystemAclExtensions.GetAccessControl(
            primaryInfo,
            AccessControlSections.Access)).SequenceEqual(originalPrimaryRules),
        "acl-write-primary-rules-not-restored");
    RequireProbe(
        GetCanonicalAccessRules(FileSystemAclExtensions.GetAccessControl(
            backupInfo,
            AccessControlSections.Access)).SequenceEqual(originalBackupRules),
        "acl-write-backup-rules-not-restored");

    await store.SaveAsync(CreateDocument("recovered"));
    RequireProbe(
        (await store.LoadAsync()).Document?.Containers[0].Name == "recovered",
        "acl-write-recovery-not-committed");
}

async Task VerifyInheritedReplaceAclRecoveryAsync(string directory)
{
    if (!OperatingSystem.IsWindows())
    {
        throw new PlatformNotSupportedException(
            "The inherited replacement ACL recovery scenario requires Windows.");
    }

    AtomicJsonConfigurationStore<ProbeConfigurationDocument> store = CreateStore(directory);
    await store.SaveAsync(CreateDocument("previous"));
    await store.SaveAsync(CreateDocument("baseline"));
    byte[] primaryBefore = await File.ReadAllBytesAsync(store.PrimaryPath);
    byte[] backupBefore = await File.ReadAllBytesAsync(store.BackupPath);

    DirectoryInfo directoryInfo = new(directory);
    FileInfo primaryInfo = new(store.PrimaryPath);
    FileInfo backupInfo = new(store.BackupPath);
    DirectorySecurity originalDirectorySecurity = FileSystemAclExtensions.GetAccessControl(
        directoryInfo,
        AccessControlSections.Access);
    FileSecurity originalPrimarySecurity = FileSystemAclExtensions.GetAccessControl(
        primaryInfo,
        AccessControlSections.Access);
    FileSecurity originalBackupSecurity = FileSystemAclExtensions.GetAccessControl(
        backupInfo,
        AccessControlSections.Access);
    byte[] originalDirectoryDescriptor =
        originalDirectorySecurity.GetSecurityDescriptorBinaryForm();
    byte[] originalPrimaryDescriptor = originalPrimarySecurity.GetSecurityDescriptorBinaryForm();
    byte[] originalBackupDescriptor = originalBackupSecurity.GetSecurityDescriptorBinaryForm();
    string[] originalDirectoryRules = GetCanonicalAccessRules(originalDirectorySecurity);
    string[] originalPrimaryRules = GetCanonicalAccessRules(originalPrimarySecurity);
    string[] originalBackupRules = GetCanonicalAccessRules(originalBackupSecurity);

    using WindowsIdentity currentIdentity = WindowsIdentity.GetCurrent();
    SecurityIdentifier currentUser = currentIdentity.User
        ?? throw new InvalidOperationException("The current Windows user has no SID.");
    FileSystemAccessRule denyDeleteChildren = new(
        currentUser,
        FileSystemRights.DeleteSubdirectoriesAndFiles,
        InheritanceFlags.None,
        PropagationFlags.None,
        AccessControlType.Deny);
    FileSystemAccessRule inheritDeleteFile = new(
        currentUser,
        FileSystemRights.Delete,
        InheritanceFlags.ObjectInherit,
        PropagationFlags.InheritOnly,
        AccessControlType.Deny);
    DirectorySecurity deniedDirectorySecurity = new();
    deniedDirectorySecurity.SetSecurityDescriptorBinaryForm(
        originalDirectoryDescriptor,
        AccessControlSections.Access);
    deniedDirectorySecurity.AddAccessRule(denyDeleteChildren);
    deniedDirectorySecurity.AddAccessRule(inheritDeleteFile);
    bool directoryDenialApplied = false;

    try
    {
        FileSystemAclExtensions.SetAccessControl(directoryInfo, deniedDirectorySecurity);
        directoryDenialApplied = true;

        RequireProbe(
            HasDenyRule(
                FileSystemAclExtensions.GetAccessControl(
                    primaryInfo,
                    AccessControlSections.Access),
                currentUser,
                FileSystemRights.Delete,
                requireInherited: true),
            "acl-inherited-primary-rule-missing");
        RequireProbe(
            HasDenyRule(
                FileSystemAclExtensions.GetAccessControl(
                    backupInfo,
                    AccessControlSections.Access),
                currentUser,
                FileSystemRights.Delete,
                requireInherited: true),
            "acl-inherited-backup-rule-missing");

        await RequireFileSystemSaveFailureAsync(
            () => store.SaveAsync(CreateDocument("must-not-commit")),
            unexpectedSuccessCode: "acl-inherited-denial-not-enforced");
        RequireProbe(
            primaryBefore.SequenceEqual(await File.ReadAllBytesAsync(store.PrimaryPath)),
            "acl-inherited-primary-changed");
        RequireProbe(
            backupBefore.SequenceEqual(await File.ReadAllBytesAsync(store.BackupPath)),
            "acl-inherited-backup-changed");
        RequireProbe(File.Exists(store.TemporaryPath), "acl-inherited-temp-not-retained");
        RequireProbe(
            HasDenyRule(
                FileSystemAclExtensions.GetAccessControl(
                    new FileInfo(store.TemporaryPath),
                    AccessControlSections.Access),
                currentUser,
                FileSystemRights.Delete,
                requireInherited: true),
            "acl-inherited-temp-rule-missing");
        ProbeConfigurationDocument staged =
            JsonSerializer.Deserialize<ProbeConfigurationDocument>(
                await File.ReadAllTextAsync(store.TemporaryPath),
                ProbeJsonOptions.Configuration)
            ?? throw new ProbeAssertionException("acl-inherited-temp-invalid");
        RequireProbe(
            staged.Containers[0].Name == "must-not-commit",
            "acl-inherited-temp-invalid");
        ConfigurationLoadResult<ProbeConfigurationDocument> loaded = await store.LoadAsync();
        RequireProbe(
            loaded.Status == ConfigurationLoadStatus.LoadedPrimary
                && loaded.Document?.Containers[0].Name == "baseline",
            "acl-inherited-temp-was-loaded");
    }
    finally
    {
        if (directoryDenialApplied)
        {
            DirectorySecurity restoredDirectorySecurity = new();
            restoredDirectorySecurity.SetSecurityDescriptorBinaryForm(
                originalDirectoryDescriptor,
                AccessControlSections.Access);
            FileSystemAclExtensions.SetAccessControl(directoryInfo, restoredDirectorySecurity);

            FileSecurity restoredPrimarySecurity = new();
            restoredPrimarySecurity.SetSecurityDescriptorBinaryForm(
                originalPrimaryDescriptor,
                AccessControlSections.Access);
            FileSystemAclExtensions.SetAccessControl(primaryInfo, restoredPrimarySecurity);

            FileSecurity restoredBackupSecurity = new();
            restoredBackupSecurity.SetSecurityDescriptorBinaryForm(
                originalBackupDescriptor,
                AccessControlSections.Access);
            FileSystemAclExtensions.SetAccessControl(backupInfo, restoredBackupSecurity);

            if (File.Exists(store.TemporaryPath))
            {
                FileSecurity restoredTemporarySecurity = new();
                restoredTemporarySecurity.SetSecurityDescriptorBinaryForm(
                    originalPrimaryDescriptor,
                    AccessControlSections.Access);
                FileSystemAclExtensions.SetAccessControl(
                    new FileInfo(store.TemporaryPath),
                    restoredTemporarySecurity);
            }
        }
    }

    RequireProbe(
        GetCanonicalAccessRules(FileSystemAclExtensions.GetAccessControl(
            directoryInfo,
            AccessControlSections.Access)).SequenceEqual(originalDirectoryRules),
        "acl-inherited-directory-rules-not-restored");
    RequireProbe(
        GetCanonicalAccessRules(FileSystemAclExtensions.GetAccessControl(
            primaryInfo,
            AccessControlSections.Access)).SequenceEqual(originalPrimaryRules),
        "acl-inherited-primary-rules-not-restored");
    RequireProbe(
        GetCanonicalAccessRules(FileSystemAclExtensions.GetAccessControl(
            backupInfo,
            AccessControlSections.Access)).SequenceEqual(originalBackupRules),
        "acl-inherited-backup-rules-not-restored");

    await store.SaveAsync(CreateDocument("recovered"));
    RequireProbe(
        (await store.LoadAsync()).Document?.Containers[0].Name == "recovered",
        "acl-inherited-recovery-not-committed");
    RequireProbe(!File.Exists(store.TemporaryPath), "acl-inherited-temp-not-cleaned");
}

async Task VerifyReadOnlyPathAsync(
    string directory,
    Func<AtomicJsonConfigurationStore<ProbeConfigurationDocument>, string> targetSelector,
    Func<AtomicJsonConfigurationStore<ProbeConfigurationDocument>, Task> createTargetAsync)
{
    AtomicJsonConfigurationStore<ProbeConfigurationDocument> store = CreateStore(directory);
    await store.SaveAsync(CreateDocument("baseline"));
    await createTargetAsync(store);

    string targetPath = targetSelector(store);
    Require(File.Exists(targetPath), $"Read-only target was not created: {Path.GetFileName(targetPath)}.");

    byte[] primaryBefore = await File.ReadAllBytesAsync(store.PrimaryPath);
    byte[]? backupBefore = File.Exists(store.BackupPath)
        ? await File.ReadAllBytesAsync(store.BackupPath)
        : null;
    FileAttributes originalAttributes = File.GetAttributes(targetPath);

    try
    {
        File.SetAttributes(targetPath, originalAttributes | FileAttributes.ReadOnly);
        await RequireFileSystemSaveFailureAsync(
            () => store.SaveAsync(CreateDocument("must-not-commit")));

        Require(
            primaryBefore.SequenceEqual(await File.ReadAllBytesAsync(store.PrimaryPath)),
            $"Read-only {Path.GetFileName(targetPath)} changed the committed primary.");
        Require(
            backupBefore is null
                ? !File.Exists(store.BackupPath)
                : backupBefore.SequenceEqual(await File.ReadAllBytesAsync(store.BackupPath)),
            $"Read-only {Path.GetFileName(targetPath)} changed the backup.");
    }
    finally
    {
        File.SetAttributes(targetPath, originalAttributes);
        if (string.Equals(targetPath, store.TemporaryPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(targetPath);
        }
    }

    await store.SaveAsync(CreateDocument("recovered"));
    ConfigurationLoadResult<ProbeConfigurationDocument> loaded = await store.LoadAsync();
    Require(
        loaded.Status == ConfigurationLoadStatus.LoadedPrimary,
        $"Write did not recover after restoring {Path.GetFileName(targetPath)}.");
    Require(
        loaded.Document?.Containers[0].Name == "recovered",
        $"Recovered write was not committed after restoring {Path.GetFileName(targetPath)}.");
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
        string detail = exception is ProbeAssertionException probeAssertion
            ? probeAssertion.Code
            : exception.GetType().Name;
        destination.Add(new(name, false, detail));
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

static async Task WaitForWriteLeaseReleaseAsync(string writeLeasePath)
{
    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

    while (true)
    {
        try
        {
            using FileStream releasedLease = new(
                writeLeasePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            return;
        }
        catch (IOException) when (!timeout.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
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

static async Task RequireFileSystemSaveFailureAsync(
    Func<Task> saveAsync,
    string? unexpectedSuccessCode = null)
{
    bool failed = false;

    try
    {
        await saveAsync();
    }
    catch (IOException)
    {
        failed = true;
    }
    catch (UnauthorizedAccessException)
    {
        failed = true;
    }

    if (unexpectedSuccessCode is not null)
    {
        RequireProbe(failed, unexpectedSuccessCode);
    }
    else
    {
        Require(failed, "A save unexpectedly succeeded against a read-only persistence file.");
    }
}

static string[] GetCanonicalAccessRules(FileSystemSecurity security)
{
    if (!OperatingSystem.IsWindows())
    {
        throw new PlatformNotSupportedException("DACL comparison requires Windows.");
    }

    List<string> rules = [];
    foreach (FileSystemAccessRule rule in security.GetAccessRules(
        includeExplicit: true,
        includeInherited: true,
        typeof(SecurityIdentifier)))
    {
        rules.Add(string.Join(
            '|',
            rule.IdentityReference.Value,
            rule.AccessControlType,
            rule.FileSystemRights,
            rule.InheritanceFlags,
            rule.PropagationFlags,
            rule.IsInherited));
    }

    rules.Sort(StringComparer.Ordinal);
    return [.. rules];
}

static bool HasDenyRule(
    FileSystemSecurity security,
    SecurityIdentifier identity,
    FileSystemRights requiredRights,
    bool requireInherited = false)
{
    if (!OperatingSystem.IsWindows())
    {
        throw new PlatformNotSupportedException("DACL inspection requires Windows.");
    }

    foreach (FileSystemAccessRule rule in security.GetAccessRules(
        includeExplicit: true,
        includeInherited: true,
        typeof(SecurityIdentifier)))
    {
        if (rule.AccessControlType == AccessControlType.Deny
            && rule.IdentityReference.Equals(identity)
            && (rule.FileSystemRights & requiredRights) == requiredRights)
        {
            if (!requireInherited || rule.IsInherited)
            {
                return true;
            }
        }
    }

    return false;
}

static async Task RequireDiskFullAsync(
    Func<Task> saveAsync,
    ConfigurationDiskFullCheckpoint expectedCheckpoint)
{
    try
    {
        await saveAsync();
        throw new InvalidOperationException(
            $"Disk full at {expectedCheckpoint} did not fail the save.");
    }
    catch (InjectedDiskFullException exception) when (exception.Checkpoint == expectedCheckpoint)
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

static void RequireProbe(bool condition, string code)
{
    if (!condition)
    {
        throw new ProbeAssertionException(code);
    }
}

internal sealed class ProbeAssertionException(string code) : Exception(code)
{
    public string Code { get; } = code;
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
