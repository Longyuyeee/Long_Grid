using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;
using LongGrid.Infrastructure.DesktopItems;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductResourceTelemetryTests
{
    [Theory]
    [InlineData(false, "LongGrid.ResourceTelemetry.0123456789abcdef0123456789abcdef", "1",
        ProductResourceTelemetryFeatureStatus.DisabledByDesktopHostPolicy)]
    [InlineData(true, "LongGrid.ResourceTelemetry.0123456789abcdef0123456789abcdef", null,
        ProductResourceTelemetryFeatureStatus.DisabledBySessionPolicy)]
    [InlineData(true, "LongGrid.ResourceTelemetry.0123456789ABCDEF0123456789ABCDEF", "1",
        ProductResourceTelemetryFeatureStatus.InvalidPipeName)]
    [InlineData(true, "LongGrid.ResourceTelemetry.0123456789abcdef0123456789abcdef", "1",
        ProductResourceTelemetryFeatureStatus.EnabledForControlledSession)]
    public void PolicyRequiresDesktopHostAcknowledgementAndBoundedPipeName(
        bool desktopHostEnabled,
        string pipeName,
        string? acknowledgement,
        ProductResourceTelemetryFeatureStatus expected)
    {
        ProductResourceTelemetryFeatureDecision decision =
            ProductResourceTelemetryFeaturePolicy.Evaluate(
                new(desktopHostEnabled
                    ? ProductDesktopHostFeatureStatus.EnabledForProduct
                    : ProductDesktopHostFeatureStatus.DisabledBySafetyPolicy),
                pipeName,
                acknowledgement);

        Assert.Equal(expected, decision.Status);
        Assert.Equal(
            expected == ProductResourceTelemetryFeatureStatus
                .EnabledForControlledSession,
            decision.IsEnabled);
        Assert.Equal(decision.IsEnabled ? pipeName : null, decision.PipeName);
    }

    [Fact]
    public async Task ServerPublishesOnlyFiniteAnonymousReadOnlyState()
    {
        string pipeName = ProductResourceTelemetryFeaturePolicy.PipeNamePrefix
            + Guid.NewGuid().ToString("N");
        ProductResourceTelemetryFeatureDecision decision =
            ProductResourceTelemetryFeaturePolicy.Evaluate(
                new(ProductDesktopHostFeatureStatus.EnabledForProduct),
                pipeName,
                "1");
        bool completed = false;
        await using ProductResourceTelemetryServer server = Assert.IsType<
            ProductResourceTelemetryServer>(
                ProductResourceTelemetryServer.TryStart(
                    decision,
                    sequence => Snapshot(sequence, completed),
                    () => completed = true));
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(timeout.Token);
        using var reader = new StreamReader(
            client,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);
        using var writer = new StreamWriter(
            client,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = true,
        };

        await writer.WriteLineAsync("unknown");
        string rejected = Assert.IsType<string>(
            await reader.ReadLineAsync(timeout.Token));
        Assert.Contains("RejectedUnknownRequest", rejected, StringComparison.Ordinal);

        await writer.WriteLineAsync("snapshot");
        string json = Assert.IsType<string>(
            await reader.ReadLineAsync(timeout.Token));
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal(1, root.GetProperty("Sequence").GetInt64());
        Assert.Equal("Ready", root.GetProperty("CatalogStatus").GetString());
        Assert.Equal(7, root.GetProperty("CatalogGeneration").GetInt64());
        Assert.Equal(2, root.GetProperty("DesktopHostOwnedWindowCount").GetInt32());
        Assert.True(root.GetProperty("FormalThumbnailWorkerIntegrated").GetBoolean());
        Assert.Equal(1, root.GetProperty("WorkerProcessCount").GetInt32());
        Assert.Equal(1, root.GetProperty("ActiveOwnedProfileCount").GetInt32());
        Assert.False(root.GetProperty(
            "OwnedProfileDeletionConfirmed").GetBoolean());
        Assert.False(root.GetProperty(
            "ContainsPathsNamesContentHandlesOrProcessIds").GetBoolean());
        Assert.DoesNotContain("secret-container", json, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", json, StringComparison.Ordinal);

        await writer.WriteLineAsync("complete");
        string finalJson = Assert.IsType<string>(
            await reader.ReadLineAsync(timeout.Token));
        using JsonDocument finalDocument = JsonDocument.Parse(finalJson);
        JsonElement finalRoot = finalDocument.RootElement;
        Assert.True(completed);
        Assert.Equal(2, finalRoot.GetProperty("Sequence").GetInt64());
        Assert.False(finalRoot.GetProperty(
            "FormalThumbnailWorkerIntegrated").GetBoolean());
        Assert.Equal(0, finalRoot.GetProperty("WorkerProcessCount").GetInt32());
        Assert.Equal(0, finalRoot.GetProperty(
            "ActiveOwnedProfileCount").GetInt32());
        Assert.True(finalRoot.GetProperty(
            "OwnedProfileDeletionConfirmed").GetBoolean());
    }

    private static ProductResourceTelemetrySnapshot Snapshot(
        long sequence,
        bool completed) =>
        new(
            1,
            sequence,
            DateTimeOffset.UtcNow,
            ProductWorkspaceSaveStatus.Saved,
            WorkspaceCurrentRevision: 5,
            WorkspaceSavedRevision: 5,
            ProductDesktopCatalogStatus.Ready,
            CatalogGeneration: 7,
            CatalogEntryCount: 12,
            ProductDisplayTopologyStatus.Ready,
            TopologyGeneration: 3,
            TopologyDisplayCount: 2,
            ProductDesktopHostLifecycleStatus.ReadyReadOnly,
            DesktopHostGeneration: 9,
            DesktopHostOwnedWindowCount: 2,
            DesktopHostWorkspaceRevision: 5,
            DesktopHostTopologyGeneration: 3,
            DesktopHostRenderedContainerCount: 4,
            DesktopHostReadOnlyAccessibilityAvailable: true,
            DesktopHostPassiveWindowContractAttested: true,
            ExplicitInteractionActive: false,
            SelectionRevision: 0,
            ProductDesktopInteractionDevelopmentStatus.Passive,
            InteractionRevision: 6,
            FormalThumbnailWorkerIntegrated: !completed,
            WorkerProcessCount: completed ? 0 : 1,
            ActiveOwnedProfileCount: completed ? 0 : 1,
            OwnedProfileDeletionConfirmed: completed,
            ContainsPathsNamesContentHandlesOrProcessIds: false);
}
