using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.App;

public partial class App
{
    private readonly ProductWorkspaceFolderContentWatcher folderContentWatcher =
        new();
    private CancellationTokenSource? folderContentRefreshCancellation;
    private ProductWorkspaceFolderContentSet folderContents =
        ProductWorkspaceFolderContentSet.Empty;
    private Task folderContentRefreshTask = Task.CompletedTask;
    private string? folderContentFingerprint;
    private long folderContentGeneration;

    private Task RefreshProductWorkspaceFolderContentsAsync()
    {
        StartProductWorkspaceFolderContentRefresh(force: true);
        return folderContentRefreshTask;
    }

    private void EnsureProductWorkspaceFolderContents() =>
        StartProductWorkspaceFolderContentRefresh(force: false);

    private string? ResolveProductWorkspaceFolderBindingDisplayPath(
        long editRevision,
        int containerOrdinal)
    {
        ProductWorkspaceSelectedFolderBindingPathResult result =
            ProductWorkspaceSelectedFolderBindingPathPolicy.Resolve(
                productWorkspaceSession.State,
                workspaceCommits.CurrentEditRevision,
                editRevision,
                containerOrdinal);
        return result.IsAvailable ? result.DisplayPath : null;
    }

    private void StartProductWorkspaceFolderContentRefresh(bool force)
    {
        ProductWorkspaceState? state = productWorkspaceSession.State;
        string fingerprint = CreateFolderContentFingerprint(
            state,
            workspaceCommits.CurrentEditRevision);
        if (!force
            && string.Equals(
                folderContentFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        if (!string.Equals(
            folderContentFingerprint,
            fingerprint,
            StringComparison.Ordinal))
        {
            folderContents = ProductWorkspaceFolderContentSet.Empty;
        }
        folderContentFingerprint = fingerprint;
        folderContentWatcher.Configure(state);
        folderContentRefreshCancellation?.Cancel();
        folderContentRefreshCancellation?.Dispose();
        folderContentRefreshCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken =
            folderContentRefreshCancellation.Token;
        if (state is null
            || state.Containers.All(container => container.FolderBinding is null))
        {
            folderContents = ProductWorkspaceFolderContentSet.Empty;
            folderContentRefreshTask = Task.CompletedTask;
            return;
        }

        long generation = checked(++folderContentGeneration);
        long workspaceRevision = workspaceCommits.CurrentEditRevision;
        folderContentRefreshTask = ReadAndPublishProductWorkspaceFolderContentsAsync(
            state,
            workspaceRevision,
            generation,
            cancellationToken);
    }

    private async Task ReadAndPublishProductWorkspaceFolderContentsAsync(
        ProductWorkspaceState state,
        long workspaceRevision,
        long generation,
        CancellationToken cancellationToken)
    {
        ProductWorkspaceFolderContentSet result;
        try
        {
            result = await Task.Run(
                () => WindowsProductContainerFolderContentReader.ReadWorkspace(
                    state,
                    generation,
                    cancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        MainWindow? currentWindow = window;
        if (currentWindow is null || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var published = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!currentWindow.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (!cancellationToken.IsCancellationRequested
                    && ReferenceEquals(state, productWorkspaceSession.State)
                    && workspaceRevision == workspaceCommits.CurrentEditRevision
                    && generation == folderContentGeneration)
                {
                    folderContents = result;
                    ApplyProductWorkspaceSessionViews();
                }
            }
            finally
            {
                published.TrySetResult();
            }
        }))
        {
            published.TrySetResult();
        }
        await published.Task;
    }

    private static string CreateFolderContentFingerprint(
        ProductWorkspaceState? state,
        long workspaceRevision)
    {
        if (state is null)
        {
            return $"{workspaceRevision}:none";
        }

        return string.Join(
            '|',
            state.Containers.Select(container =>
                container.FolderBinding is null
                    ? $"{container.Id}:none"
                    : $"{container.Id}:{container.FolderBinding.VolumeSerialNumber}:" +
                        $"{container.FolderBinding.FileId}:" +
                        $"{container.FolderBinding.Resolution}"))
            + $":revision={workspaceRevision}";
    }

    private void ProductWorkspaceFolderContentWatcher_Invalidated(
        object? sender,
        EventArgs args)
    {
        MainWindow? currentWindow = window;
        if (currentWindow is null || closingDrainInProgress)
        {
            return;
        }
        _ = currentWindow.DispatcherQueue.TryEnqueue(
            () => StartProductWorkspaceFolderContentRefresh(force: true));
    }
}
