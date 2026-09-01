using System.Text.Json;
using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.Configuration;

public enum ProductConfigurationRestartRecoveryAvailability
{
    Unavailable,
    Available,
    InvalidRecoveryPoint,
    CurrentConfigurationChanged,
    RecoveryPointChanged,
}

public enum ProductConfigurationRestartRecoveryStatus
{
    Restored,
    ConfirmationRequired,
    Unavailable,
    InvalidRecoveryPoint,
    CurrentConfigurationChanged,
    RecoveryPointChanged,
    WriteLeaseUnavailable,
    IoFailure,
}

public sealed record ProductConfigurationRestartRecoveryPoint(
    Guid RecoveryId,
    string CurrentFingerprint,
    string RecoveryFingerprint,
    DateTimeOffset CreatedAtUtc,
    string ActionSummary,
    int ContainerCount,
    int ItemCount);

public sealed record ProductConfigurationRestartRecoverySnapshot(
    ProductConfigurationRestartRecoveryAvailability Availability,
    ProductConfigurationRestartRecoveryPoint? Point)
{
    public bool IsAvailable =>
        Availability == ProductConfigurationRestartRecoveryAvailability.Available
        && Point is not null;
}

public sealed record ProductConfigurationRestartRecoveryResult(
    ProductConfigurationRestartRecoveryStatus Status,
    ProductConfigurationDocument? Document)
{
    public bool IsRestored =>
        Status == ProductConfigurationRestartRecoveryStatus.Restored
        && Document is not null;
}

public static class ProductConfigurationRestartRecoveryAdmission
{
    public static bool CanRestore(ProductWorkspaceSaveSnapshot save)
    {
        ArgumentNullException.ThrowIfNull(save);
        return save.Status is ProductWorkspaceSaveStatus.Clean
                or ProductWorkspaceSaveStatus.Saved
            && save.CurrentRevision == save.SavedRevision
            && save.ActiveSaveRevision is null
            && save.Failure == ProductWorkspaceSaveFailure.None;
    }
}

public sealed partial class ProductConfigurationStore
{
    private const int RestartRecoverySchemaVersion = 1;
    private const int MaximumRestartRecoveryMetadataBytes = 4096;
    private const string RestartRecoveryActionSummary =
        "恢复到上次保存前的配置";
    private static readonly JsonSerializerOptions RestartRecoveryJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
    };

    internal string RestartRecoveryTemporaryPath =>
        RestartRecoveryPointPath + ".new";

    internal string RestartRecoveryRestoreTemporaryPath =>
        PrimaryPath + ".restart-restore.new";

    public async Task<ProductConfigurationRestartRecoverySnapshot>
        GetRestartRecoveryPointAsync(
            CancellationToken cancellationToken = default)
    {
        RestartRecoveryMarkerRead markerRead =
            await TryReadRestartRecoveryMarkerAsync(cancellationToken)
                .ConfigureAwait(false);
        if (markerRead.Status != RestartRecoveryMarkerReadStatus.Loaded)
        {
            return new(
                markerRead.Status == RestartRecoveryMarkerReadStatus.Missing
                    ? ProductConfigurationRestartRecoveryAvailability.Unavailable
                    : ProductConfigurationRestartRecoveryAvailability
                        .InvalidRecoveryPoint,
                null);
        }

        RestartRecoveryValidation validation =
            await ValidateRestartRecoveryAsync(
                    markerRead.Marker!,
                    cancellationToken)
                .ConfigureAwait(false);
        if (validation.Availability !=
            ProductConfigurationRestartRecoveryAvailability.Available)
        {
            return new(validation.Availability, null);
        }

        ProductConfigurationDocument recovery = validation.RecoveryDocument!;
        return new(
            ProductConfigurationRestartRecoveryAvailability.Available,
            new(
                markerRead.Marker!.RecoveryId,
                markerRead.Marker.CurrentFingerprint,
                markerRead.Marker.RecoveryFingerprint,
                markerRead.Marker.CreatedAtUtc,
                markerRead.Marker.ActionSummary,
                recovery.Containers.Count,
                recovery.Containers.Sum(container => container.Items.Count)));
    }

    public async Task<ProductConfigurationRestartRecoveryResult>
        RestoreRestartRecoveryPointAsync(
            ProductConfigurationRestartRecoveryPoint point,
            bool userConfirmed,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(point);
        if (!userConfirmed)
        {
            return new(
                ProductConfigurationRestartRecoveryStatus.ConfirmationRequired,
                null);
        }

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            await using FileStream writeLease =
                await AcquireWriteLeaseAsync(cancellationToken)
                    .ConfigureAwait(false);
            RestartRecoveryMarkerRead markerRead =
                await TryReadRestartRecoveryMarkerAsync(cancellationToken)
                    .ConfigureAwait(false);
            if (markerRead.Status != RestartRecoveryMarkerReadStatus.Loaded)
            {
                return new(
                    markerRead.Status == RestartRecoveryMarkerReadStatus.Missing
                        ? ProductConfigurationRestartRecoveryStatus.Unavailable
                        : ProductConfigurationRestartRecoveryStatus
                            .InvalidRecoveryPoint,
                    null);
            }

            RestartRecoveryMarker marker = markerRead.Marker!;
            if (marker.RecoveryId != point.RecoveryId
                || !string.Equals(
                    marker.CurrentFingerprint,
                    point.CurrentFingerprint,
                    StringComparison.Ordinal)
                || !string.Equals(
                    marker.RecoveryFingerprint,
                    point.RecoveryFingerprint,
                    StringComparison.Ordinal))
            {
                return new(
                    ProductConfigurationRestartRecoveryStatus.RecoveryPointChanged,
                    null);
            }

            RestartRecoveryValidation validation =
                await ValidateRestartRecoveryAsync(marker, cancellationToken)
                    .ConfigureAwait(false);
            if (validation.Availability !=
                ProductConfigurationRestartRecoveryAvailability.Available)
            {
                return new(MapStatus(validation.Availability), null);
            }

            ProductConfigurationDocument recovery = validation.RecoveryDocument!;
            byte[] serialized =
                ProductConfigurationJson.SerializeToUtf8Bytes(recovery);
            TryDeleteRestartRecoveryRestoreTemporaryFile();
            try
            {
                await using (FileStream stream = new(
                    RestartRecoveryRestoreTemporaryPath,
                    new FileStreamOptions
                    {
                        Access = FileAccess.Write,
                        Mode = FileMode.CreateNew,
                        Options = FileOptions.Asynchronous
                            | FileOptions.WriteThrough,
                        Share = FileShare.None,
                    }))
                {
                    await stream.WriteAsync(serialized, cancellationToken)
                        .ConfigureAwait(false);
                    await FlushToDiskAsync(stream, cancellationToken)
                        .ConfigureAwait(false);
                }

                ReadAttempt staged = await TryReadAsync(
                        RestartRecoveryRestoreTemporaryPath,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (staged.Document is null)
                {
                    return new(
                        ProductConfigurationRestartRecoveryStatus
                            .InvalidRecoveryPoint,
                        null);
                }

                File.Replace(
                    RestartRecoveryRestoreTemporaryPath,
                    PrimaryPath,
                    BackupPath,
                    ignoreMetadataErrors: false);
            }
            finally
            {
                TryDeleteRestartRecoveryRestoreTemporaryFile();
            }

            TryDeleteRestartRecoveryPointFiles();
            return new(
                ProductConfigurationRestartRecoveryStatus.Restored,
                ProductConfigurationJson.Deserialize(
                    ProductConfigurationJson.SerializeToUtf8Bytes(recovery)));
        }
        catch (ProductConfigurationSaveException exception) when (
            exception.Error == ProductConfigurationSaveError.WriteLeaseUnavailable)
        {
            return new(
                ProductConfigurationRestartRecoveryStatus.WriteLeaseUnavailable,
                null);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ProductConfigurationContractException)
        {
            return new(
                ProductConfigurationRestartRecoveryStatus.IoFailure,
                null);
        }
    }

    private async Task TryPublishRestartRecoveryPointAsync(
        ProductConfigurationDocument? previous,
        ProductConfigurationDocument current)
    {
        try
        {
            if (previous is null || !File.Exists(BackupPath))
            {
                TryDeleteRestartRecoveryPointFiles();
                return;
            }

            string currentFingerprint =
                ProductWorkspaceConfigurationFingerprint.Compute(current);
            string recoveryFingerprint =
                ProductWorkspaceConfigurationFingerprint.Compute(previous);
            if (string.Equals(
                currentFingerprint,
                recoveryFingerprint,
                StringComparison.Ordinal))
            {
                TryDeleteRestartRecoveryPointFiles();
                return;
            }

            var marker = new RestartRecoveryMarker(
                RestartRecoverySchemaVersion,
                Guid.NewGuid(),
                currentFingerprint,
                recoveryFingerprint,
                DateTimeOffset.UtcNow,
                RestartRecoveryActionSummary);
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
                marker,
                RestartRecoveryJsonOptions);
            if (payload.Length > MaximumRestartRecoveryMetadataBytes)
            {
                TryDeleteRestartRecoveryPointFiles();
                return;
            }

            TryDeleteRestartRecoveryTemporaryFile();
            await using (FileStream stream = new(
                RestartRecoveryTemporaryPath,
                new FileStreamOptions
                {
                    Access = FileAccess.Write,
                    Mode = FileMode.CreateNew,
                    Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                    Share = FileShare.None,
                }))
            {
                await stream.WriteAsync(payload).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(
                RestartRecoveryTemporaryPath,
                RestartRecoveryPointPath,
                overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException
                or ProductConfigurationContractException)
        {
            TryDeleteRestartRecoveryTemporaryFile();
        }
    }

    private async Task<RestartRecoveryValidation> ValidateRestartRecoveryAsync(
        RestartRecoveryMarker marker,
        CancellationToken cancellationToken)
    {
        ReadAttempt current = await TryReadAsync(PrimaryPath, cancellationToken)
            .ConfigureAwait(false);
        if (current.Document is null)
        {
            return new(
                ProductConfigurationRestartRecoveryAvailability
                    .CurrentConfigurationChanged,
                null);
        }

        string currentFingerprint =
            ProductWorkspaceConfigurationFingerprint.Compute(current.Document);
        if (!string.Equals(
            currentFingerprint,
            marker.CurrentFingerprint,
            StringComparison.Ordinal))
        {
            return new(
                ProductConfigurationRestartRecoveryAvailability
                    .CurrentConfigurationChanged,
                null);
        }

        ReadAttempt recovery = await TryReadAsync(BackupPath, cancellationToken)
            .ConfigureAwait(false);
        if (recovery.Document is null)
        {
            return new(
                ProductConfigurationRestartRecoveryAvailability
                    .RecoveryPointChanged,
                null);
        }

        string recoveryFingerprint =
            ProductWorkspaceConfigurationFingerprint.Compute(recovery.Document);
        if (!string.Equals(
            recoveryFingerprint,
            marker.RecoveryFingerprint,
            StringComparison.Ordinal))
        {
            return new(
                ProductConfigurationRestartRecoveryAvailability
                    .RecoveryPointChanged,
                null);
        }

        return new(
            ProductConfigurationRestartRecoveryAvailability.Available,
            recovery.Document);
    }

    private async Task<RestartRecoveryMarkerRead>
        TryReadRestartRecoveryMarkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(RestartRecoveryPointPath))
            {
                return new(RestartRecoveryMarkerReadStatus.Missing, null);
            }

            await using FileStream stream = new(
                RestartRecoveryPointPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > MaximumRestartRecoveryMetadataBytes)
            {
                return new(RestartRecoveryMarkerReadStatus.Invalid, null);
            }

            RestartRecoveryMarker? marker =
                await JsonSerializer.DeserializeAsync<RestartRecoveryMarker>(
                    stream,
                    RestartRecoveryJsonOptions,
                    cancellationToken).ConfigureAwait(false);
            return IsValid(marker)
                ? new(RestartRecoveryMarkerReadStatus.Loaded, marker)
                : new(RestartRecoveryMarkerReadStatus.Invalid, null);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException)
        {
            return new(RestartRecoveryMarkerReadStatus.Invalid, null);
        }
    }

    private static bool IsValid(RestartRecoveryMarker? marker) =>
        marker is not null
        && marker.SchemaVersion == RestartRecoverySchemaVersion
        && marker.RecoveryId != Guid.Empty
        && IsCanonicalFingerprint(marker.CurrentFingerprint)
        && IsCanonicalFingerprint(marker.RecoveryFingerprint)
        && !string.Equals(
            marker.CurrentFingerprint,
            marker.RecoveryFingerprint,
            StringComparison.Ordinal)
        && marker.CreatedAtUtc != default
        && string.Equals(
            marker.ActionSummary,
            RestartRecoveryActionSummary,
            StringComparison.Ordinal);

    private static ProductConfigurationRestartRecoveryStatus MapStatus(
        ProductConfigurationRestartRecoveryAvailability availability) =>
        availability switch
        {
            ProductConfigurationRestartRecoveryAvailability.Unavailable =>
                ProductConfigurationRestartRecoveryStatus.Unavailable,
            ProductConfigurationRestartRecoveryAvailability.InvalidRecoveryPoint =>
                ProductConfigurationRestartRecoveryStatus.InvalidRecoveryPoint,
            ProductConfigurationRestartRecoveryAvailability
                .CurrentConfigurationChanged =>
                ProductConfigurationRestartRecoveryStatus
                    .CurrentConfigurationChanged,
            ProductConfigurationRestartRecoveryAvailability
                .RecoveryPointChanged =>
                ProductConfigurationRestartRecoveryStatus.RecoveryPointChanged,
            _ => ProductConfigurationRestartRecoveryStatus.InvalidRecoveryPoint,
        };

    private void TryDeleteRestartRecoveryPointFiles()
    {
        TryDeleteFile(RestartRecoveryPointPath);
        TryDeleteRestartRecoveryTemporaryFile();
    }

    private void TryDeleteRestartRecoveryTemporaryFile() =>
        TryDeleteFile(RestartRecoveryTemporaryPath);

    private void TryDeleteRestartRecoveryRestoreTemporaryFile() =>
        TryDeleteFile(RestartRecoveryRestoreTemporaryPath);

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record RestartRecoveryMarker(
        int SchemaVersion,
        Guid RecoveryId,
        string CurrentFingerprint,
        string RecoveryFingerprint,
        DateTimeOffset CreatedAtUtc,
        string ActionSummary);

    private sealed record RestartRecoveryValidation(
        ProductConfigurationRestartRecoveryAvailability Availability,
        ProductConfigurationDocument? RecoveryDocument);

    private sealed record RestartRecoveryMarkerRead(
        RestartRecoveryMarkerReadStatus Status,
        RestartRecoveryMarker? Marker);

    private enum RestartRecoveryMarkerReadStatus
    {
        Missing,
        Loaded,
        Invalid,
    }
}
