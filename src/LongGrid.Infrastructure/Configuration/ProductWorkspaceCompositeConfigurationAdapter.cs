using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.Configuration;

internal interface IProductWorkspaceCompositeBindingExchange
{
    bool Matches(ProductWorkspaceWindowCompositeBinding expected);

    bool TryExchange(
        ProductWorkspaceWindowCompositeBinding expected,
        ProductWorkspaceWindowCompositeBinding replacement);
}

internal sealed class ProductWorkspaceCompositeBindingState
    : IProductWorkspaceCompositeBindingExchange
{
    private readonly object sync = new();
    private ProductWorkspaceWindowCompositeBinding current;

    internal ProductWorkspaceCompositeBindingState(
        ProductWorkspaceWindowCompositeBinding initial)
    {
        if (!ProductWorkspaceCompositeConfigurationAdapter.IsValidBinding(initial))
        {
            throw new ArgumentException(
                "The initial composite binding is invalid.",
                nameof(initial));
        }

        current = initial;
    }

    internal ProductWorkspaceWindowCompositeBinding Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public bool Matches(ProductWorkspaceWindowCompositeBinding expected)
    {
        lock (sync)
        {
            return current == expected;
        }
    }

    public bool TryExchange(
        ProductWorkspaceWindowCompositeBinding expected,
        ProductWorkspaceWindowCompositeBinding replacement)
    {
        if (!ProductWorkspaceCompositeConfigurationAdapter.IsValidBinding(expected)
            || !ProductWorkspaceCompositeConfigurationAdapter.IsValidBinding(
                replacement))
        {
            return false;
        }

        lock (sync)
        {
            if (current != expected)
            {
                return false;
            }

            current = replacement;
            return true;
        }
    }
}

internal sealed class ProductWorkspaceCompositeConfigurationAdapter
    : IProductWorkspaceCompositeConfigurationLayer
{
    private sealed class ConfigurationSnapshot(
        Guid ownerId,
        ProductConfigurationDocument document,
        string fingerprint)
        : IProductWorkspaceWindowCompositeSnapshot
    {
        private bool disposed;

        internal Guid OwnerId { get; } = ownerId;

        internal ProductConfigurationDocument Document { get; } = document;

        internal string Fingerprint { get; } = fingerprint;

        internal bool IsDisposed => disposed;

        public void Dispose() => disposed = true;
    }

    private readonly object sync = new();
    private readonly Guid ownerId = Guid.NewGuid();
    private readonly ProductConfigurationStore store;
    private readonly IProductWorkspaceCompositeBindingExchange bindingExchange;
    private ConfigurationSnapshot? latestCapture;
    private string? lastPublishedFingerprint;
    private ProductWorkspaceWindowCompositeBinding? lastPublishedBinding;

    internal ProductWorkspaceCompositeConfigurationAdapter(
        ProductConfigurationStore store,
        IProductWorkspaceCompositeBindingExchange bindingExchange)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(bindingExchange);
        this.store = store;
        this.bindingExchange = bindingExchange;
    }

    public ProductWorkspaceWindowCompositeCapture Capture()
    {
        lock (sync)
        {
            ProductConfigurationLoadResult? loaded = TryLoad();
            if (loaded?.Status != ProductConfigurationLoadStatus.LoadedPrimary
                || loaded.Document is null)
            {
                latestCapture = null;
                return ProductWorkspaceWindowCompositeCapture.Failed;
            }

            ProductConfigurationDocument snapshotDocument;
            string fingerprint;
            try
            {
                byte[] serialized =
                    ProductConfigurationJson.SerializeToUtf8Bytes(loaded.Document);
                snapshotDocument = ProductConfigurationJson.Deserialize(serialized);
                fingerprint = ProductWorkspaceConfigurationFingerprint.Compute(
                    snapshotDocument);
            }
            catch (ProductConfigurationContractException)
            {
                latestCapture = null;
                return ProductWorkspaceWindowCompositeCapture.Failed;
            }

            var snapshot = new ConfigurationSnapshot(
                ownerId,
                snapshotDocument,
                fingerprint);
            latestCapture = snapshot;
            return new(true, snapshot);
        }
    }

    public bool Apply(
        ProductWorkspaceState state,
        ProductWorkspaceWindowCompositeBinding expectedBinding)
    {
        lock (sync)
        {
            if (!TryProject(
                    state,
                    expectedBinding,
                    out ProductConfigurationDocument? replacement,
                    out string? replacementFingerprint)
                || latestCapture is null
                || latestCapture.IsDisposed
                || expectedBinding.EditRevision <= 1)
            {
                return false;
            }

            ProductWorkspaceWindowCompositeBinding beforeBinding =
                expectedBinding with
                {
                    EditRevision = expectedBinding.EditRevision - 1,
                    ConfigurationFingerprint = latestCapture.Fingerprint,
                };
            if (!bindingExchange.Matches(beforeBinding))
            {
                return false;
            }

            ProductConfigurationCompareExchangeStatus? status = TryCompareExchange(
                replacement!,
                latestCapture.Fingerprint);
            if (status != ProductConfigurationCompareExchangeStatus.Saved)
            {
                return false;
            }

            lastPublishedFingerprint = replacementFingerprint;
            lastPublishedBinding = expectedBinding;
            return bindingExchange.TryExchange(beforeBinding, expectedBinding);
        }
    }

    public bool Verify(
        ProductWorkspaceState state,
        ProductWorkspaceWindowCompositeBinding expectedBinding)
    {
        lock (sync)
        {
            return TryProject(
                    state,
                    expectedBinding,
                out _,
                    out string? fingerprint)
                && bindingExchange.Matches(expectedBinding)
                && MatchesPrimary(fingerprint!);
        }
    }

    public bool Restore(
        IProductWorkspaceWindowCompositeSnapshot snapshot,
        ProductWorkspaceWindowCompositeBinding expectedBinding)
    {
        lock (sync)
        {
            if (!TryReadSnapshot(
                snapshot,
                expectedBinding,
                out ConfigurationSnapshot? configuration))
            {
                return false;
            }

            ProductConfigurationLoadResult? current = TryLoad();
            if (current?.Status != ProductConfigurationLoadStatus.LoadedPrimary
                || current.Document is null)
            {
                return false;
            }

            string currentFingerprint;
            try
            {
                currentFingerprint =
                    ProductWorkspaceConfigurationFingerprint.Compute(
                        current.Document);
            }
            catch (ProductConfigurationContractException)
            {
                return false;
            }

            if (string.Equals(
                currentFingerprint,
                configuration!.Fingerprint,
                StringComparison.Ordinal))
            {
                lastPublishedFingerprint = configuration.Fingerprint;
                return AdvanceBinding(expectedBinding);
            }

            if (lastPublishedFingerprint is null
                || !string.Equals(
                    currentFingerprint,
                    lastPublishedFingerprint,
                    StringComparison.Ordinal))
            {
                return false;
            }

            ProductConfigurationCompareExchangeStatus? status =
                TryCompareExchange(
                    configuration.Document,
                    currentFingerprint);
            if (status != ProductConfigurationCompareExchangeStatus.Saved)
            {
                return false;
            }

            lastPublishedFingerprint = configuration.Fingerprint;
            return AdvanceBinding(expectedBinding);
        }
    }

    public bool VerifyRestored(
        IProductWorkspaceWindowCompositeSnapshot snapshot,
        ProductWorkspaceWindowCompositeBinding expectedBinding)
    {
        lock (sync)
        {
            return TryReadSnapshot(
                    snapshot,
                    expectedBinding,
                    out ConfigurationSnapshot? configuration)
                && bindingExchange.Matches(expectedBinding)
                && MatchesPrimary(configuration!.Fingerprint);
        }
    }

    private bool AdvanceBinding(
        ProductWorkspaceWindowCompositeBinding expectedBinding)
    {
        if (bindingExchange.Matches(expectedBinding))
        {
            lastPublishedBinding = expectedBinding;
            return true;
        }

        if (lastPublishedBinding is null
            || !bindingExchange.TryExchange(
                lastPublishedBinding,
                expectedBinding))
        {
            return false;
        }

        lastPublishedBinding = expectedBinding;
        return true;
    }

    private bool TryReadSnapshot(
        IProductWorkspaceWindowCompositeSnapshot snapshot,
        ProductWorkspaceWindowCompositeBinding expectedBinding,
        out ConfigurationSnapshot? configuration)
    {
        configuration = snapshot as ConfigurationSnapshot;
        return configuration is not null
            && configuration.OwnerId == ownerId
            && !configuration.IsDisposed
            && IsValidBinding(expectedBinding)
            && string.Equals(
                configuration.Fingerprint,
                expectedBinding.ConfigurationFingerprint,
                StringComparison.Ordinal);
    }

    private bool MatchesPrimary(string fingerprint)
    {
        ProductConfigurationLoadResult? loaded = TryLoad();
        if (loaded?.Status != ProductConfigurationLoadStatus.LoadedPrimary
            || loaded.Document is null)
        {
            return false;
        }

        try
        {
            return string.Equals(
                ProductWorkspaceConfigurationFingerprint.Compute(loaded.Document),
                fingerprint,
                StringComparison.Ordinal);
        }
        catch (ProductConfigurationContractException)
        {
            return false;
        }
    }

    private ProductConfigurationLoadResult? TryLoad()
    {
        try
        {
            return store.LoadAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception) when (
            exception is ProductConfigurationSaveException
                or IOException
                or UnauthorizedAccessException
                or OperationCanceledException)
        {
            return null;
        }
    }

    private ProductConfigurationCompareExchangeStatus? TryCompareExchange(
        ProductConfigurationDocument replacement,
        string expectedFingerprint)
    {
        try
        {
            return store.CompareExchangePrimaryAsync(
                    replacement,
                    expectedFingerprint)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception) when (
            exception is ProductConfigurationSaveException
                or ArgumentException
                or IOException
                or UnauthorizedAccessException
                or OperationCanceledException)
        {
            return null;
        }
    }

    private static bool TryProject(
        ProductWorkspaceState state,
        ProductWorkspaceWindowCompositeBinding expectedBinding,
        out ProductConfigurationDocument? document,
        out string? fingerprint)
    {
        document = null;
        fingerprint = null;
        if (state is null || !IsValidBinding(expectedBinding))
        {
            return false;
        }

        ProductWorkspaceProjectionResult projected =
            ProductWorkspaceConfigurationProjector.Project(state);
        if (!projected.IsSuccess)
        {
            return false;
        }

        try
        {
            fingerprint = ProductWorkspaceConfigurationFingerprint.Compute(
                projected.Document!);
        }
        catch (ProductConfigurationContractException)
        {
            return false;
        }

        if (!string.Equals(
            fingerprint,
            expectedBinding.ConfigurationFingerprint,
            StringComparison.Ordinal))
        {
            return false;
        }

        document = projected.Document;
        return true;
    }

    internal static bool IsValidBinding(
        ProductWorkspaceWindowCompositeBinding? binding) =>
        binding is not null
        && binding.TopologyGeneration > 0
        && binding.EditRevision > 0
        && binding.WindowRegistryGeneration > 0
        && binding.DesktopHostInstanceId != Guid.Empty
        && binding.DesktopHostGeneration > 0
        && binding.ConfigurationFingerprint is { Length: 64 }
        && binding.ConfigurationFingerprint.All(character =>
            character is >= '0' and <= '9'
                or >= 'A' and <= 'F');
}
