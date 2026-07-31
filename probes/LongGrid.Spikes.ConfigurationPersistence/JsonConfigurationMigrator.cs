using System.Text.Json.Nodes;

namespace LongGrid.Spikes.ConfigurationPersistence;

public enum ConfigurationMigrationStatus
{
    AlreadyCurrent,
    Migrated,
}

public enum ConfigurationMigrationCheckpoint
{
    AfterClone,
    AfterTransform,
    AfterValidation,
}

public sealed record ConfigurationMigrationResult(
    ConfigurationMigrationStatus Status,
    int SourceVersion,
    int TargetVersion,
    JsonObject Document);

public sealed class InjectedMigrationFailureException(ConfigurationMigrationCheckpoint checkpoint)
    : InvalidOperationException($"Injected configuration migration failure at {checkpoint}.")
{
    public ConfigurationMigrationCheckpoint Checkpoint { get; } = checkpoint;
}

public sealed class JsonConfigurationMigrationStep
{
    public JsonConfigurationMigrationStep(
        int sourceVersion,
        int targetVersion,
        Func<JsonObject, JsonObject> transform)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceVersion, 1);

        if (targetVersion != sourceVersion + 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetVersion),
                targetVersion,
                "Migration steps must advance exactly one schema version.");
        }

        ArgumentNullException.ThrowIfNull(transform);
        SourceVersion = sourceVersion;
        TargetVersion = targetVersion;
        Transform = transform;
    }

    public int SourceVersion { get; }

    public int TargetVersion { get; }

    public Func<JsonObject, JsonObject> Transform { get; }
}

public sealed class JsonConfigurationMigrator
{
    private readonly Dictionary<int, JsonConfigurationMigrationStep> steps;
    private readonly Func<JsonObject, ConfigurationValidationFailure> targetValidator;

    public JsonConfigurationMigrator(
        int targetVersion,
        IEnumerable<JsonConfigurationMigrationStep> steps,
        Func<JsonObject, ConfigurationValidationFailure> targetValidator)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(targetVersion, 1);

        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(targetValidator);

        Dictionary<int, JsonConfigurationMigrationStep> indexedSteps = [];
        foreach (JsonConfigurationMigrationStep step in steps)
        {
            if (!indexedSteps.TryAdd(step.SourceVersion, step))
            {
                throw new ArgumentException(
                    $"More than one migration step starts at schema {step.SourceVersion}.",
                    nameof(steps));
            }
        }

        TargetVersion = targetVersion;
        this.steps = indexedSteps;
        this.targetValidator = targetValidator;
    }

    public int TargetVersion { get; }

    public ConfigurationMigrationResult Migrate(
        JsonObject source,
        ConfigurationMigrationCheckpoint? injectedFailure = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        int sourceVersion = ReadSchemaVersion(source);
        if (sourceVersion > TargetVersion)
        {
            throw new NotSupportedException("A newer configuration schema cannot be downgraded.");
        }

        JsonObject working = (JsonObject)source.DeepClone();
        ThrowIfRequested(injectedFailure, ConfigurationMigrationCheckpoint.AfterClone);

        if (sourceVersion == TargetVersion)
        {
            ValidateTarget(working);
            ThrowIfRequested(injectedFailure, ConfigurationMigrationCheckpoint.AfterValidation);
            return new(
                ConfigurationMigrationStatus.AlreadyCurrent,
                sourceVersion,
                TargetVersion,
                working);
        }

        int currentVersion = sourceVersion;
        while (currentVersion < TargetVersion)
        {
            if (!steps.TryGetValue(currentVersion, out JsonConfigurationMigrationStep? step))
            {
                throw new NotSupportedException(
                    $"No migration step starts at schema {currentVersion}.");
            }

            JsonObject inputClone = (JsonObject)working.DeepClone();
            JsonObject transformed = step.Transform(inputClone)
                ?? throw new InvalidDataException("A migration step returned no document.");
            int transformedVersion = ReadSchemaVersion(transformed);

            if (transformedVersion != step.TargetVersion)
            {
                throw new InvalidDataException(
                    "A migration step did not publish its declared target schema.");
            }

            working = transformed;
            currentVersion = transformedVersion;
            ThrowIfRequested(injectedFailure, ConfigurationMigrationCheckpoint.AfterTransform);
        }

        ValidateTarget(working);
        ThrowIfRequested(injectedFailure, ConfigurationMigrationCheckpoint.AfterValidation);

        return new(
            ConfigurationMigrationStatus.Migrated,
            sourceVersion,
            TargetVersion,
            working);
    }

    private static int ReadSchemaVersion(JsonObject document)
    {
        if (document["schemaVersion"] is not JsonValue schemaValue
            || !schemaValue.TryGetValue(out int version)
            || version < 1)
        {
            throw new InvalidDataException("The configuration schema version is invalid.");
        }

        return version;
    }

    private static void ThrowIfRequested(
        ConfigurationMigrationCheckpoint? requested,
        ConfigurationMigrationCheckpoint current)
    {
        if (requested == current)
        {
            throw new InjectedMigrationFailureException(current);
        }
    }

    private void ValidateTarget(JsonObject document)
    {
        ConfigurationValidationFailure failure = targetValidator(document);
        if (failure != ConfigurationValidationFailure.None)
        {
            throw new InvalidDataException($"Migrated configuration validation failed: {failure}.");
        }
    }
}

internal static class ProbeConfigurationMigration
{
    public const int CurrentSchemaVersion = 2;

    public static JsonConfigurationMigrator CreateMigrator() =>
        new(
            CurrentSchemaVersion,
            [
                new JsonConfigurationMigrationStep(1, 2, MigrateV1ToV2),
            ],
            ValidateVersionedDocument);

    public static ConfigurationValidationFailure ValidateVersionedDocument(JsonObject document)
    {
        if (document["schemaVersion"] is not JsonValue schemaValue
            || !schemaValue.TryGetValue(out int schemaVersion))
        {
            return ConfigurationValidationFailure.InvalidDocument;
        }

        if (schemaVersion is < 1 or > CurrentSchemaVersion)
        {
            return ConfigurationValidationFailure.UnsupportedSchema;
        }

        if (document["profileId"] is not JsonValue profileValue
            || !profileValue.TryGetValue(out string? profileId))
        {
            return ConfigurationValidationFailure.InvalidDocument;
        }

        if (string.IsNullOrWhiteSpace(profileId)
            || document["containers"] is not JsonArray)
        {
            return ConfigurationValidationFailure.InvalidDocument;
        }

        if (schemaVersion == 2)
        {
            if (document["persistenceProbe"] is not JsonObject probe
                || probe["migratedFrom"] is not JsonValue migratedFromValue
                || !migratedFromValue.TryGetValue(out int migratedFrom)
                || migratedFrom != 1)
            {
                return ConfigurationValidationFailure.InvalidDocument;
            }
        }

        return ConfigurationValidationFailure.None;
    }

    private static JsonObject MigrateV1ToV2(JsonObject document)
    {
        if (document.ContainsKey("persistenceProbe"))
        {
            throw new InvalidDataException(
                "The illustrative v2 field conflicts with an existing unknown field.");
        }

        document["schemaVersion"] = 2;
        document["persistenceProbe"] = new JsonObject
        {
            ["migratedFrom"] = 1,
        };
        return document;
    }
}
