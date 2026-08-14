using System.Diagnostics;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Core.DesktopItems;
using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Infrastructure.Configuration;

public enum ProductWorkspaceScalePreflightOutcome
{
    Passed,
    RegressionBudgetExceeded,
}

public sealed record ProductWorkspaceScalePreflightMetric(
    string Name,
    double P95Milliseconds,
    double RegressionLimitMilliseconds)
{
    public bool Passed => P95Milliseconds <= RegressionLimitMilliseconds;
}

public sealed record ProductWorkspaceScalePreflightResult(
    ProductWorkspaceScalePreflightOutcome Outcome,
    int ContainerCount,
    int ItemCount,
    int Iterations,
    int ResolvedItemCount,
    int ProjectedItemCount,
    int SelectionActionCount,
    int SearchMatchCount,
    int SortedContainerCount,
    int ReadyContainerCount,
    IReadOnlyList<ProductWorkspaceScalePreflightMetric> Metrics,
    bool TemporarySandboxCleaned,
    bool ReadsRealDesktop,
    bool RealFileOperationsAllowed);

public static class ProductWorkspaceScalePreflight
{
    public const int ContainerCount = ProductConfigurationLimits.MaximumContainers;
    public const int ItemCount = ProductConfigurationLimits.MaximumItems;
    public const int Iterations = 20;
    public const int PersistenceIterations = 5;

    private const double CorePipelineP95LimitMilliseconds = 1_000;
    private const double SaveP95LimitMilliseconds = 3_000;
    private const double RecoveryP95LimitMilliseconds = 1_000;

    public static async Task<ProductWorkspaceScalePreflightResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "LongGrid.ProductScalePreflight",
            Guid.NewGuid().ToString("N"));
        bool cleaned = false;
        try
        {
            ProductConfigurationDocument candidate = CreateCandidate();
            IReadOnlyList<DesktopCatalogEntry> catalog = CreateCatalog();
            var coreSamples = new List<double>(Iterations);
            int projectedItems = 0;
            int selectionActions = 0;
            int searchMatches = 0;
            int sortedContainers = 0;
            int readyContainers = 0;

            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long started = Stopwatch.GetTimestamp();
                byte[] serialized = ProductConfigurationJson.SerializeToUtf8Bytes(candidate);
                ProductConfigurationDocument parsed =
                    ProductConfigurationJson.Deserialize(serialized);
                ProductWorkspaceResolutionResult resolved =
                    ProductWorkspaceConfigurationResolver.Resolve(parsed, catalog);
                Require(
                    resolved.IsSuccess
                    && resolved.State is not null
                    && resolved.Summary.Resolved == ItemCount
                    && resolved.Summary.Total == ItemCount,
                    "The 500 distinct references did not resolve exactly once.");
                ProductWorkspaceState state = resolved.State
                    ?? throw new InvalidOperationException(
                        "The resolved scale state was unavailable.");

                ProductWorkspaceReadResult read =
                    ProductWorkspaceReadModel.Create(state);
                Require(
                    read.IsSuccess
                    && read.Snapshot is not null
                    && read.Snapshot.ItemCount == ItemCount
                    && read.Snapshot.ResolvedCount == ItemCount,
                    "The product read model did not preserve the 500-item state.");
                ProductWorkspaceReadSnapshot readSnapshot = read.Snapshot
                    ?? throw new InvalidOperationException(
                        "The scale read snapshot was unavailable.");

                ProductDesktopHostProjectionBatch? projection =
                    ProductDesktopHostProjectionBuilder.Build(
                        state,
                        readSnapshot,
                        Topology(),
                        workspaceRevision: iteration + 1);
                projectedItems = projection?.Displays
                    .SelectMany(display => display.Containers)
                    .Sum(container => container.ItemIds.Count) ?? 0;
                Require(
                    projection?.ContainerCount == ContainerCount
                    && projectedItems == ItemCount,
                    "The DesktopHost projection did not preserve the bounded scale fixture.");

                ProductWorkspaceVisibleSearchInput[] searchInputs = readSnapshot
                    .Containers
                    .Select(container => new ProductWorkspaceVisibleSearchInput(
                        container.UserVisibleName,
                        container.Health.ToString(),
                        container.Items
                            .Select(item => item.UserVisibleName ?? string.Empty)
                            .ToArray()))
                    .ToArray();
                ProductWorkspaceVisibleSearchResult search =
                    ProductWorkspaceVisibleSearchPolicy.Resolve(
                        "Item 499",
                        searchInputs);
                searchMatches = search.MatchingIndexes.Count;
                Require(
                    search.IsSupported && searchMatches == 1,
                    "Visible search did not return the unique scale-fixture match.");

                ProductWorkspaceContainerSortInput[] sortInputs = readSnapshot
                    .Containers
                    .Select(container => new ProductWorkspaceContainerSortInput(
                        container.UserVisibleName,
                        container.Health))
                    .ToArray();
                ProductWorkspaceContainerSortResult sort =
                    ProductWorkspaceContainerSortPolicy.Resolve(
                        ProductWorkspaceContainerSort.NameDescending,
                        sortInputs);
                sortedContainers = sort.OrderedIndexes.Count;
                Require(
                    sort.IsSupported
                    && sortedContainers == ContainerCount
                    && sort.OrderedIndexes.Distinct().Count() == ContainerCount,
                    "Container sorting lost or duplicated scale-fixture entries.");

                readyContainers = readSnapshot.Containers.Count(container =>
                    ProductWorkspaceContainerHealthFilterPolicy.Includes(
                        ProductWorkspaceContainerHealthFilter.Ready,
                        container.Health));
                Require(
                    readyContainers == ContainerCount,
                    "Health filtering did not preserve all resolved containers.");

                selectionActions = ExerciseSelection(projection!, iteration + 1);
                Require(
                    selectionActions == ItemCount,
                    "Selection did not traverse every projected scale-fixture item.");
                coreSamples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }

            Directory.CreateDirectory(sandbox);
            var store = new ProductConfigurationStore(sandbox);
            var saveSamples = new List<double>(PersistenceIterations);
            var recoverySamples = new List<double>(PersistenceIterations);
            for (int iteration = 0; iteration < PersistenceIterations; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long saveStarted = Stopwatch.GetTimestamp();
                await store.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
                saveSamples.Add(Stopwatch.GetElapsedTime(saveStarted).TotalMilliseconds);

                long recoveryStarted = Stopwatch.GetTimestamp();
                ProductConfigurationLoadResult loaded =
                    await store.LoadAsync(cancellationToken).ConfigureAwait(false);
                recoverySamples.Add(
                    Stopwatch.GetElapsedTime(recoveryStarted).TotalMilliseconds);
                Require(
                    loaded.Status == ProductConfigurationLoadStatus.LoadedPrimary
                    && loaded.Document?.Containers.Count == ContainerCount
                    && loaded.Document.Containers.Sum(container => container.Items.Count)
                        == ItemCount,
                    "The persisted 500-item candidate did not recover intact.");
            }

            ProductWorkspaceScalePreflightMetric[] metrics =
            [
                Metric(
                    "core-pipeline",
                    coreSamples,
                    CorePipelineP95LimitMilliseconds),
                Metric("save", saveSamples, SaveP95LimitMilliseconds),
                Metric("recovery", recoverySamples, RecoveryP95LimitMilliseconds),
            ];
            ProductWorkspaceScalePreflightOutcome outcome = metrics.All(metric =>
                    metric.Passed)
                ? ProductWorkspaceScalePreflightOutcome.Passed
                : ProductWorkspaceScalePreflightOutcome.RegressionBudgetExceeded;

            DeleteSandbox(sandbox);
            cleaned = !Directory.Exists(sandbox);
            Require(cleaned, "The temporary scale sandbox was not removed.");
            return new(
                outcome,
                ContainerCount,
                ItemCount,
                Iterations,
                ItemCount,
                projectedItems,
                selectionActions,
                searchMatches,
                sortedContainers,
                readyContainers,
                metrics,
                cleaned,
                ReadsRealDesktop: false,
                RealFileOperationsAllowed: false);
        }
        finally
        {
            if (!cleaned)
            {
                DeleteSandbox(sandbox);
            }
        }
    }

    private static ProductConfigurationDocument CreateCandidate()
    {
        ContainerConfiguration[] containers = Enumerable.Range(0, ContainerCount)
            .Select(containerIndex => new ContainerConfiguration
            {
                Id = $"scale-container-{containerIndex:D3}",
                Name = $"Scale Container {containerIndex:D3}",
                IsLocked = false,
                Appearance = new()
                {
                    Color = "#2457D6",
                    Opacity = 0.82,
                    Collapsed = false,
                },
                Placement = new()
                {
                    DisplayKey = "scale-display",
                    XDip = 20 + ((containerIndex % 10) * 350),
                    YDip = 20 + ((containerIndex / 10) * 260),
                    WidthDip = 320,
                    HeightDip = 240,
                },
                Items = Enumerable.Range(containerIndex * 5, 5)
                    .Select(itemIndex => new DesktopItemReferenceConfiguration
                    {
                        Id = $"scale-item-{itemIndex:D3}",
                        Kind = ConfigurationItemKind.File,
                        Target = Target(itemIndex),
                        Behavior = ConfigurationItemBehavior.Reference,
                    })
                    .ToArray(),
            })
            .ToArray();
        return new()
        {
            SchemaVersion = ProductConfigurationLimits.CurrentSchemaVersion,
            ProfileId = "m4a-scale-preflight",
            Containers = containers,
        };
    }

    private static DesktopCatalogEntry[] CreateCatalog() =>
        Enumerable.Range(0, ItemCount)
            .Select(itemIndex => new DesktopCatalogEntry(
                new DesktopItemIdentity("filesystem", Target(itemIndex)),
                "synthetic-desktop",
                $"Item {itemIndex:D3}",
                DesktopItemKind.File))
            .ToArray();

    private static ProductDisplayTopologySnapshot Topology() => new(
        ProductDisplayTopologyStatus.Ready,
        Generation: 1,
        Displays:
        [
            new DisplayTopologyNode(
                "scale-display",
                new PixelRect(0, 0, 3840, 2160),
                new PixelRect(0, 0, 3840, 2120),
                96,
                DisplayRotation.Landscape,
                IsPrimary: true),
        ],
        ActivePathCount: 1,
        StableIdentityCount: 1,
        BufferAttempts: 1);

    private static int ExerciseSelection(
        ProductDesktopHostProjectionBatch projection,
        long revision)
    {
        DateTimeOffset now = new(
            2026,
            8,
            14,
            0,
            0,
            0,
            TimeSpan.Zero);
        int applied = 0;
        foreach (ProductDesktopHostReadOnlyProjection container in projection
            .Displays
            .SelectMany(display => display.Containers))
        {
            var lease = new ProductDesktopInteractionLease(
                Guid.NewGuid(),
                container.ContainerId,
                revision,
                projection.TopologyGeneration,
                WindowRegistryGeneration: 1,
                now.AddMinutes(1));
            ProductDesktopSelectionCreationResult creation =
                ProductDesktopInteractionSelectionController.TryCreate(
                    lease,
                    container.ItemIds,
                    now);
            Require(creation.IsCreated, "A bounded selection controller was rejected.");
            foreach (string itemId in container.ItemIds)
            {
                ProductDesktopSelectionSnapshot selected = creation.Controller!.Apply(
                    lease,
                    container.ItemIds,
                    new(
                        ProductDesktopSelectionAction.SelectItem,
                        ProductDesktopSelectionModifiers.Control,
                        itemId),
                    now);
                Require(
                    selected.Status == ProductDesktopSelectionStatus.Applied,
                    "A bounded selection action was rejected.");
                applied++;
            }
        }

        return applied;
    }

    private static string Target(int itemIndex) => Path.Combine(
        Path.GetTempPath(),
        "LongGrid.ScaleFixture",
        $"item-{itemIndex:D3}.txt");

    private static ProductWorkspaceScalePreflightMetric Metric(
        string name,
        IReadOnlyList<double> samples,
        double limitMilliseconds)
    {
        double[] ordered = samples.Order().ToArray();
        int index = Math.Max(
            0,
            (int)Math.Ceiling(ordered.Length * 0.95) - 1);
        return new(name, Math.Round(ordered[index], 3), limitMilliseconds);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void DeleteSandbox(string sandbox)
    {
        if (Directory.Exists(sandbox))
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }
}
