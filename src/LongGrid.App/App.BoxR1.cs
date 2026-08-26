using System.Security.Cryptography;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;
using Microsoft.Windows.AppLifecycle;

namespace LongGrid.App;

public partial class App
{
    private readonly object explorerCreateActivationGate = new();
    private readonly Dictionary<Guid, DateTimeOffset>
        acceptedExplorerCreateActivations = new();
    private ProductExplorerCreateActivationIntent?
        pendingExplorerCreateActivation;

    private async Task RunBoxR1ActivationEvidenceSessionAsync(
        ProductBoxR1ActivationEvidenceSession evidence)
    {
        try
        {
            await Task.WhenAll(
                LoadBoxesSettingsAsync(),
                LoadConfigurationStartupStateAsync(),
                RefreshProductDesktopCatalogAsync(),
                RefreshProductDisplayTopologyAsync());

            ProductDesktopHostLifecycleSnapshot host =
                await WaitForRuntimeEnableHostAsync(
                    requireOwnedWindow: true,
                    TimeSpan.FromSeconds(10));
            ProductWorkspaceState beforeState = ResolveDesktopWorkspaceCreateState()
                ?? throw new InvalidOperationException(
                    "BOX-R1 evidence requires a writable workspace.");
            string configurationBefore = CaptureFileFingerprint(
                configurationStore.PrimaryPath);
            await ProductBoxR1ActivationEvidenceSession.WriteJsonAsync(
                evidence.ReadyPath,
                new
                {
                    SchemaVersion = 1,
                    Purpose = "BoxR1ActivationReady",
                    host.Status,
                    host.OwnedWindowCount,
                    TopologyAuthoritative =
                        productDisplayTopology.Snapshot.IsAuthoritative,
                    ContainerCount = beforeState.Containers.Count,
                });

            _ = TryDispatchExplorerCreateActivation();
            await evidence.WaitForPreviewAsync(TimeSpan.FromSeconds(15));

            ProductWorkspaceState afterState = ResolveDesktopWorkspaceCreateState()
                ?? throw new InvalidOperationException(
                    "BOX-R1 evidence lost the writable workspace.");
            string configurationAfter = CaptureFileFingerprint(
                configurationStore.PrimaryPath);
            bool passed = evidence.PreviewDrivenCount == 1
                && evidence.PreviewVisualTreeCount == 1
                && evidence.PreviewActivatedCount == 1
                && beforeState.Containers.Count == afterState.Containers.Count
                && string.Equals(
                    configurationBefore,
                    configurationAfter,
                    StringComparison.Ordinal);
            await ProductBoxR1ActivationEvidenceSession.WriteJsonAsync(
                evidence.ResultPath,
                new
                {
                    SchemaVersion = 1,
                    Purpose = "BoxR1ActivationRealProcessEvidence",
                    Expected = new
                    {
                        PreviewDrivenCount = 1,
                        PreviewVisualTreeCount = 1,
                        PreviewActivatedCount = 1,
                        ContainerCountDifference = 0,
                        ConfigurationFingerprintChanged = false,
                    },
                    Actual = new
                    {
                        evidence.PreviewDrivenCount,
                        evidence.PreviewVisualTreeCount,
                        evidence.PreviewActivatedCount,
                        ContainerCountBefore = beforeState.Containers.Count,
                        ContainerCountAfter = afterState.Containers.Count,
                        ConfigurationFingerprintChanged = !string.Equals(
                            configurationBefore,
                            configurationAfter,
                            StringComparison.Ordinal),
                    },
                    Difference = passed ? "None" : "PreviewOrCancellationMismatch",
                    Outcome = passed ? "Pass" : "Fail",
                });
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or TimeoutException)
        {
            await ProductBoxR1ActivationEvidenceSession.WriteJsonAsync(
                evidence.ResultPath,
                new
                {
                    SchemaVersion = 1,
                    Purpose = "BoxR1ActivationRealProcessEvidence",
                    Difference = "EvidenceSessionFailed",
                    Outcome = "Fail",
                    FailureType = exception.GetType().Name,
                });
        }
        finally
        {
            window?.Close();
        }
    }

    internal void HandleActivation(AppActivationArguments activation)
    {
        ArgumentNullException.ThrowIfNull(activation);

        ProductExplorerCreateActivationDecision explorerCreateActivation =
            ProductExplorerCreateActivation.Parse(
                GetLaunchActivationArguments(activation),
                DateTimeOffset.UtcNow);
        if (QueueExplorerCreateActivation(explorerCreateActivation))
        {
            if (window is not null)
            {
                if (!window.DispatcherQueue.HasThreadAccess)
                {
                    _ = window.DispatcherQueue.TryEnqueue(() =>
                        _ = TryDispatchExplorerCreateActivation());
                }
                else
                {
                    _ = TryDispatchExplorerCreateActivation();
                }
            }
            return;
        }

        if (window is null)
        {
            activationPending = true;
            return;
        }

        if (!window.DispatcherQueue.HasThreadAccess)
        {
            _ = window.DispatcherQueue.TryEnqueue(ActivateMainWindow);
            return;
        }

        ActivateMainWindow();
    }

    private static string[] GetLaunchActivationArguments(
        AppActivationArguments activation)
    {
        if (activation.Data is not
            Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch
            || string.IsNullOrWhiteSpace(launch.Arguments))
        {
            return [];
        }

        return launch.Arguments.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private bool QueueExplorerCreateActivation(
        ProductExplorerCreateActivationDecision decision)
    {
        if (!decision.IsCommand)
        {
            return false;
        }
        if (!decision.CanActivate || decision.Intent is not { } intent)
        {
            return true;
        }

        lock (explorerCreateActivationGate)
        {
            DateTimeOffset expiry = DateTimeOffset.UtcNow
                - ProductExplorerCreateActivation.MaximumAge
                - ProductExplorerCreateActivation.MaximumFutureSkew;
            foreach (Guid nonce in acceptedExplorerCreateActivations
                .Where(pair => pair.Value < expiry)
                .Select(pair => pair.Key)
                .ToArray())
            {
                acceptedExplorerCreateActivations.Remove(nonce);
            }

            if (acceptedExplorerCreateActivations.ContainsKey(intent.Nonce)
                || pendingExplorerCreateActivation is not null)
            {
                return true;
            }

            acceptedExplorerCreateActivations.Add(intent.Nonce, intent.IssuedAt);
            pendingExplorerCreateActivation = intent;
            return true;
        }
    }

    private bool TryDispatchExplorerCreateActivation()
    {
        ProductExplorerCreateActivationIntent? intent;
        lock (explorerCreateActivationGate)
        {
            intent = pendingExplorerCreateActivation;
        }
        if (intent is null || window is null || closingDrainInProgress)
        {
            return false;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeSpan age = now - intent.IssuedAt;
        if (age > ProductExplorerCreateActivation.MaximumAge
            || age < -ProductExplorerCreateActivation.MaximumFutureSkew)
        {
            ClearPendingExplorerCreateActivation(intent.Nonce);
            return false;
        }

        ProductDisplayTopologySnapshot topology = productDisplayTopology.Snapshot;
        if (!topology.IsAuthoritative
            || ResolveDesktopWorkspaceCreateState() is null
            || productDesktopHostLifecycle.Snapshot.Status is not (
                ProductDesktopHostLifecycleStatus.AwaitingWorkspace
                or ProductDesktopHostLifecycleStatus.ReadyReadOnly))
        {
            return false;
        }

        DisplayTopologyNode? display = topology.Displays.SingleOrDefault(candidate =>
            ContainsScreenPoint(
                candidate.WorkArea,
                intent.ScreenX,
                intent.ScreenY));
        if (display is null)
        {
            ClearPendingExplorerCreateActivation(intent.Nonce);
            return false;
        }

        bool accepted = RequestDesktopWorkspaceCreate(new(
            ProductDesktopWorkspaceCreateInputKind.ExplorerContextMenu,
            display.StableId,
            workspaceCommits.CurrentEditRevision,
            topology.Generation,
            SourceAttested: true,
            IsInjected: false,
            IsAutoRepeat: false));
        if (accepted)
        {
            ClearPendingExplorerCreateActivation(intent.Nonce);
        }
        return accepted;
    }

    private void ClearPendingExplorerCreateActivation(Guid nonce)
    {
        lock (explorerCreateActivationGate)
        {
            if (pendingExplorerCreateActivation?.Nonce == nonce)
            {
                pendingExplorerCreateActivation = null;
            }
        }
    }

    private static bool ContainsScreenPoint(PixelRect bounds, int x, int y) =>
        x >= bounds.Left
        && y >= bounds.Top
        && (long)x < (long)bounds.Left + bounds.Width
        && (long)y < (long)bounds.Top + bounds.Height;

    private static string CaptureFileFingerprint(string path)
    {
        if (!File.Exists(path))
        {
            return "MISSING";
        }

        var info = new FileInfo(path);
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}:{hash}";
    }
}
