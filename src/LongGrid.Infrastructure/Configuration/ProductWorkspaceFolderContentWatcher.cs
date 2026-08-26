using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.Configuration;

public sealed class ProductWorkspaceFolderContentWatcher : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan HealthPollInterval = TimeSpan.FromSeconds(2);
    private readonly object gate = new();
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly Timer debounceTimer;
    private readonly Timer healthTimer;
    private ProductContainerFolderBindingState[] monitoredBindings = [];
    private string healthFingerprint = string.Empty;
    private long configurationGeneration;
    private int healthPollActive;
    private bool disposed;

    public ProductWorkspaceFolderContentWatcher()
    {
        debounceTimer = new(
            _ => PublishInvalidation(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        healthTimer = new(
            _ => PollBindingHealth(),
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
            configurationGeneration++;
            ClearWatchers();
            if (state is null)
            {
                monitoredBindings = [];
                healthFingerprint = string.Empty;
                _ = healthTimer.Change(
                    Timeout.InfiniteTimeSpan,
                    Timeout.InfiniteTimeSpan);
                return;
            }

            monitoredBindings = state.Containers
                .Select(container => container.FolderBinding)
                .Where(binding => binding is not null)
                .Select(binding => binding!)
                .Distinct()
                .ToArray();
            healthFingerprint = CreateHealthFingerprint(monitoredBindings);
            _ = healthTimer.Change(
                monitoredBindings.Length == 0
                    ? Timeout.InfiniteTimeSpan
                    : HealthPollInterval,
                monitoredBindings.Length == 0
                    ? Timeout.InfiniteTimeSpan
                    : HealthPollInterval);

            string[] roots = state.Containers
                .Select(container => container.FolderBinding)
                .Where(binding => binding is not null
                    && !string.IsNullOrWhiteSpace(binding.PersistedTarget))
                .Select(binding => binding!.PersistedTarget)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var watchedParents = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (string root in roots)
            {
                TryAddWatcher(
                    root,
                    filter: "*",
                    NotifyFilters.FileName
                        | NotifyFilters.DirectoryName
                        | NotifyFilters.Security);
                string? parent = Path.GetDirectoryName(
                    Path.TrimEndingDirectorySeparator(root));
                string name = Path.GetFileName(
                    Path.TrimEndingDirectorySeparator(root));
                if (!string.IsNullOrWhiteSpace(parent)
                    && !string.IsNullOrWhiteSpace(name)
                    && watchedParents.Add(parent + "\0" + name))
                {
                    TryAddWatcher(
                        parent,
                        name,
                        NotifyFilters.FileName | NotifyFilters.DirectoryName);
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
            healthTimer.Dispose();
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

    private void PollBindingHealth()
    {
        if (Interlocked.Exchange(ref healthPollActive, 1) != 0)
        {
            return;
        }

        try
        {
            PollBindingHealthCore();
        }
        finally
        {
            Volatile.Write(ref healthPollActive, 0);
        }
    }

    private void PollBindingHealthCore()
    {
        ProductContainerFolderBindingState[] bindings;
        long generation;
        lock (gate)
        {
            if (disposed || monitoredBindings.Length == 0)
            {
                return;
            }
            bindings = monitoredBindings;
            generation = configurationGeneration;
        }

        string observed = CreateHealthFingerprint(bindings);
        bool changed = false;
        lock (gate)
        {
            if (!disposed
                && generation == configurationGeneration
                && !string.Equals(
                    observed,
                    healthFingerprint,
                    StringComparison.Ordinal))
            {
                healthFingerprint = observed;
                changed = true;
            }
        }
        if (changed)
        {
            ScheduleInvalidation();
        }
    }

    private static string CreateHealthFingerprint(
        IReadOnlyList<ProductContainerFolderBindingState> bindings) =>
        string.Join(
            '|',
            bindings.Select(binding =>
            {
                ProductContainerFolderBindingState resolved =
                    WindowsProductContainerFolderBinding.Resolve(binding);
                return resolved.Resolution.ToString();
            }));

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

    private void TryAddWatcher(
        string path,
        string filter,
        NotifyFilters notifyFilters)
    {
        try
        {
            var watcher = new FileSystemWatcher(path, filter)
            {
                IncludeSubdirectories = false,
                NotifyFilter = notifyFilters,
            };
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or PlatformNotSupportedException)
        {
            // Parent monitoring and explicit refresh remain available when a
            // content watcher cannot be established for an offline binding.
        }
    }
}
