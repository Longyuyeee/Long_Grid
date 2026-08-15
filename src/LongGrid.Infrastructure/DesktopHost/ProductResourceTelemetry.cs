using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopItems;

namespace LongGrid.Infrastructure.DesktopHost;

public enum ProductResourceTelemetryFeatureStatus
{
    DisabledByDesktopHostPolicy,
    DisabledBySessionPolicy,
    InvalidPipeName,
    EnabledForControlledSession,
}

public sealed record ProductResourceTelemetryFeatureDecision(
    ProductResourceTelemetryFeatureStatus Status,
    string? PipeName)
{
    public bool IsEnabled =>
        Status == ProductResourceTelemetryFeatureStatus
            .EnabledForControlledSession;
}

public static class ProductResourceTelemetryFeaturePolicy
{
    public const string PipeEnvironmentVariableName =
        "LONGGRID_RESOURCE_TELEMETRY_PIPE";
    public const string SessionEnvironmentVariableName =
        "LONGGRID_ACKNOWLEDGE_RESOURCE_STABILITY_SESSION";
    public const string PipeNamePrefix = "LongGrid.ResourceTelemetry.";
    private const int RandomSuffixLength = 32;

    public static ProductResourceTelemetryFeatureDecision Evaluate(
        ProductDesktopHostFeatureDecision desktopHost,
        string? pipeName,
        string? sessionAcknowledgement)
    {
        ArgumentNullException.ThrowIfNull(desktopHost);
        if (!desktopHost.IsEnabled)
        {
            return new(
                ProductResourceTelemetryFeatureStatus
                    .DisabledByDesktopHostPolicy,
                PipeName: null);
        }

        if (!string.Equals(
                sessionAcknowledgement,
                "1",
                StringComparison.Ordinal))
        {
            return new(
                ProductResourceTelemetryFeatureStatus.DisabledBySessionPolicy,
                PipeName: null);
        }

        return IsValidPipeName(pipeName)
            ? new(
                ProductResourceTelemetryFeatureStatus
                    .EnabledForControlledSession,
                pipeName)
            : new(
                ProductResourceTelemetryFeatureStatus.InvalidPipeName,
                PipeName: null);
    }

    public static bool IsValidPipeName(string? pipeName)
    {
        if (pipeName is null
            || !pipeName.StartsWith(PipeNamePrefix, StringComparison.Ordinal)
            || pipeName.Length != PipeNamePrefix.Length + RandomSuffixLength)
        {
            return false;
        }

        return pipeName[PipeNamePrefix.Length..].All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}

public sealed record ProductResourceTelemetrySnapshot(
    int SchemaVersion,
    long Sequence,
    DateTimeOffset CapturedAtUtc,
    ProductWorkspaceSaveStatus WorkspaceSaveStatus,
    long WorkspaceCurrentRevision,
    long WorkspaceSavedRevision,
    ProductDesktopCatalogStatus CatalogStatus,
    long CatalogGeneration,
    int CatalogEntryCount,
    ProductDisplayTopologyStatus TopologyStatus,
    long TopologyGeneration,
    int TopologyDisplayCount,
    ProductDesktopHostLifecycleStatus DesktopHostStatus,
    long DesktopHostGeneration,
    int DesktopHostOwnedWindowCount,
    long DesktopHostWorkspaceRevision,
    long DesktopHostTopologyGeneration,
    int DesktopHostRenderedContainerCount,
    bool DesktopHostReadOnlyAccessibilityAvailable,
    bool DesktopHostPassiveWindowContractAttested,
    bool ExplicitInteractionActive,
    long SelectionRevision,
    ProductDesktopInteractionDevelopmentStatus InteractionStatus,
    long InteractionRevision,
    bool FormalThumbnailWorkerIntegrated,
    int WorkerProcessCount,
    int ActiveOwnedProfileCount,
    bool OwnedProfileDeletionConfirmed,
    bool ContainsPathsNamesContentHandlesOrProcessIds);

internal sealed class ProductResourceTelemetryServer : IAsyncDisposable
{
    private const string SnapshotRequest = "snapshot";
    private const string CompleteRequest = "complete";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Func<long, ProductResourceTelemetrySnapshot> capture;
    private readonly Action complete;
    private readonly NamedPipeServerStream pipe;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task worker;
    private long sequence;
    private bool disposed;

    private ProductResourceTelemetryServer(
        string pipeName,
        Func<long, ProductResourceTelemetrySnapshot> capture,
        Action complete)
    {
        this.capture = capture;
        this.complete = complete;
        pipe = new(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        worker = RunAsync();
    }

    internal static ProductResourceTelemetryServer? TryStart(
        ProductResourceTelemetryFeatureDecision decision,
        Func<long, ProductResourceTelemetrySnapshot> capture,
        Action complete)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(complete);
        return decision is { IsEnabled: true, PipeName: not null }
            ? new(decision.PipeName, capture, complete)
            : null;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetime.Cancel();
        pipe.Dispose();
        try
        {
            await worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (lifetime.IsCancellationRequested)
        {
        }

        lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RunAsync()
    {
        await pipe.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
        using var reader = new StreamReader(
            pipe,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);
        using var writer = new StreamWriter(
            pipe,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = true,
        };

        while (!lifetime.IsCancellationRequested && pipe.IsConnected)
        {
            string? request = await reader.ReadLineAsync(lifetime.Token)
                .ConfigureAwait(false);
            if (request is null)
            {
                return;
            }

            if (string.Equals(request, CompleteRequest, StringComparison.Ordinal))
            {
                complete();
                long finalSequence = Interlocked.Increment(ref sequence);
                ProductResourceTelemetrySnapshot finalSnapshot =
                    capture(finalSequence);
                Validate(finalSnapshot, finalSequence);
                string finalJson = JsonSerializer.Serialize(
                    finalSnapshot,
                    JsonOptions);
                await writer.WriteLineAsync(
                    finalJson.AsMemory(),
                    lifetime.Token).ConfigureAwait(false);
                return;
            }

            if (!string.Equals(request, SnapshotRequest, StringComparison.Ordinal))
            {
                await writer.WriteLineAsync(
                    "{\"schemaVersion\":1,\"outcome\":\"RejectedUnknownRequest\"}"
                        .AsMemory(),
                    lifetime.Token).ConfigureAwait(false);
                continue;
            }

            long nextSequence = Interlocked.Increment(ref sequence);
            ProductResourceTelemetrySnapshot snapshot = capture(nextSequence);
            Validate(snapshot, nextSequence);
            string json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await writer.WriteLineAsync(json.AsMemory(), lifetime.Token)
                .ConfigureAwait(false);
        }
    }

    private static void Validate(
        ProductResourceTelemetrySnapshot snapshot,
        long expectedSequence)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != 1
            || snapshot.Sequence != expectedSequence
            || snapshot.Sequence <= 0
            || snapshot.CapturedAtUtc == default
            || snapshot.WorkspaceCurrentRevision < 0
            || snapshot.WorkspaceSavedRevision < 0
            || snapshot.CatalogGeneration < 0
            || snapshot.CatalogEntryCount < 0
            || snapshot.TopologyGeneration < 0
            || snapshot.TopologyDisplayCount < 0
            || snapshot.DesktopHostGeneration < 0
            || snapshot.DesktopHostOwnedWindowCount < 0
            || snapshot.DesktopHostWorkspaceRevision < 0
            || snapshot.DesktopHostTopologyGeneration < 0
            || snapshot.DesktopHostRenderedContainerCount < 0
            || snapshot.SelectionRevision < 0
            || snapshot.InteractionRevision < 0
            || snapshot.WorkerProcessCount < 0
            || snapshot.ActiveOwnedProfileCount < 0
            || snapshot.ContainsPathsNamesContentHandlesOrProcessIds
            || snapshot.WorkerProcessCount > 1
            || snapshot.ActiveOwnedProfileCount > 1
            || snapshot.OwnedProfileDeletionConfirmed
                && (snapshot.WorkerProcessCount != 0
                    || snapshot.ActiveOwnedProfileCount != 0)
            || snapshot.FormalThumbnailWorkerIntegrated
                != (snapshot.WorkerProcessCount == 1
                    && snapshot.ActiveOwnedProfileCount == 1))
        {
            throw new InvalidOperationException(
                "Product resource telemetry must remain finite, anonymous, "
                + "monotonic and honest about formal worker integration.");
        }
    }
}
