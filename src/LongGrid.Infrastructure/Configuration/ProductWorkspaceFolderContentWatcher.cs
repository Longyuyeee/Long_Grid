using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.Configuration;

public sealed class ProductWorkspaceFolderContentWatcher : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250);
    private readonly object gate = new();
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly Timer debounceTimer;
    private bool disposed;

    public ProductWorkspaceFolderContentWatcher()
    {
        debounceTimer = new(
            _ => PublishInvalidation(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    public event EventHandler? Invalidated;

    public void Configure(ProductWorkspaceState? state)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ClearWatchers();
            if (state is null)
            {
                return;
            }

            IEnumerable<string> roots = state.Containers
                .Select(container => container.FolderBinding)
                .Where(binding => binding?.Resolution ==
                    ProductContainerFolderBindingResolution.Resolved
                    && !string.IsNullOrWhiteSpace(binding.ResolvedTarget))
                .Select(binding => binding!.ResolvedTarget!)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (string root in roots)
            {
                try
                {
                    var watcher = new FileSystemWatcher(root)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName
                            | NotifyFilters.DirectoryName,
                    };
                    watcher.Created += OnChanged;
                    watcher.Deleted += OnChanged;
                    watcher.Renamed += OnRenamed;
                    watcher.Error += OnError;
                    watcher.EnableRaisingEvents = true;
                    watchers.Add(watcher);
                }
                catch (Exception exception) when (exception is
                    ArgumentException or IOException
                        or PlatformNotSupportedException)
                {
                    // Explicit refresh remains available when a watcher cannot
                    // be established for an otherwise valid binding.
                }
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            ClearWatchers();
            debounceTimer.Dispose();
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs args) =>
        ScheduleInvalidation();

    private void OnRenamed(object sender, RenamedEventArgs args) =>
        ScheduleInvalidation();

    private void OnError(object sender, ErrorEventArgs args) =>
        ScheduleInvalidation();

    private void ScheduleInvalidation()
    {
        if (!Monitor.TryEnter(gate))
        {
            return;
        }
        try
        {
            if (!disposed)
            {
                _ = debounceTimer.Change(
                    DebounceDelay,
                    Timeout.InfiniteTimeSpan);
            }
        }
        finally
        {
            Monitor.Exit(gate);
        }
    }

    private void PublishInvalidation()
    {
        EventHandler? handler;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            handler = Invalidated;
        }
        handler?.Invoke(this, EventArgs.Empty);
    }

    private void ClearWatchers()
    {
        foreach (FileSystemWatcher watcher in watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnChanged;
            watcher.Deleted -= OnChanged;
            watcher.Renamed -= OnRenamed;
            watcher.Error -= OnError;
            watcher.Dispose();
        }
        watchers.Clear();
    }
}
