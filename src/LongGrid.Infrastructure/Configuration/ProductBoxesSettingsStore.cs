using System.Text.Json;
using LongGrid.Core.Configuration;

namespace LongGrid.Infrastructure.Configuration;

public enum ProductBoxesSettingsLoadStatus
{
    MissingDefaulted,
    LoadedPrimary,
    RecoveredBackup,
    CorruptSafeDisabled,
}

public sealed record ProductBoxesSettingsLoadResult(
    ProductBoxesSettingsLoadStatus Status,
    ProductBoxesSettings Settings)
{
    public bool RequiresAttention =>
        Status == ProductBoxesSettingsLoadStatus.CorruptSafeDisabled;
}

public interface IProductBoxesSettingsStore
{
    Task<ProductBoxesSettingsLoadResult> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        ProductBoxesSettings settings,
        CancellationToken cancellationToken = default);
}

public sealed class ProductBoxesSettingsStore : IProductBoxesSettingsStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
    };
    private readonly string directory;
    private readonly string settingsPath;
    private readonly string backupPath;
    private readonly SemaphoreSlim writeGate = new(1, 1);

    public ProductBoxesSettingsStore(string configurationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        directory = Path.GetFullPath(configurationDirectory);
        settingsPath = Path.Combine(directory, "settings.json");
        backupPath = Path.Combine(directory, "settings.backup.json");
    }

    public async Task<ProductBoxesSettingsLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(settingsPath))
        {
            return new(
                ProductBoxesSettingsLoadStatus.MissingDefaulted,
                ProductBoxesSettings.Default);
        }

        ProductBoxesSettings? primary = await TryReadAsync(
            settingsPath,
            cancellationToken).ConfigureAwait(false);
        if (primary is not null)
        {
            return new(
                ProductBoxesSettingsLoadStatus.LoadedPrimary,
                primary);
        }

        ProductBoxesSettings? backup = await TryReadAsync(
            backupPath,
            cancellationToken).ConfigureAwait(false);
        return backup is not null
            ? new(ProductBoxesSettingsLoadStatus.RecoveredBackup, backup)
            : new(
                ProductBoxesSettingsLoadStatus.CorruptSafeDisabled,
                ProductBoxesSettings.SafeDisabled);
    }

    public async Task SaveAsync(
        ProductBoxesSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid)
        {
            throw new InvalidDataException("Unsupported boxes settings schema.");
        }

        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $"settings.{Guid.NewGuid():N}.tmp");
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
                settings,
                JsonOptions);
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(payload, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(settingsPath))
            {
                File.Replace(
                    temporaryPath,
                    settingsPath,
                    backupPath,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, settingsPath);
            }
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
            writeGate.Release();
        }
    }

    private static async Task<ProductBoxesSettings?> TryReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            ProductBoxesSettings? settings =
                await JsonSerializer.DeserializeAsync<ProductBoxesSettings>(
                    stream,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
            return settings?.IsValid == true ? settings : null;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException)
        {
            return null;
        }
    }

    public void Dispose() => writeGate.Dispose();
}

public enum ProductBoxesSettingsChangeStatus
{
    Saved,
    Unchanged,
    Failed,
}

public sealed record ProductBoxesSettingsChangeResult(
    ProductBoxesSettingsChangeStatus Status,
    ProductBoxesSettings Settings);

public sealed class ProductBoxesSettingsController(
    IProductBoxesSettingsStore store) : IDisposable
{
    private readonly SemaphoreSlim changeGate = new(1, 1);
    private ProductBoxesSettings current = ProductBoxesSettings.Default;

    public ProductBoxesSettings Current => current;

    public void Initialize(ProductBoxesSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid)
        {
            throw new ArgumentException("Boxes settings must use the current schema.", nameof(settings));
        }
        current = settings;
    }

    public async Task<ProductBoxesSettingsChangeResult> ChangeAsync(
        bool boxesEnabled,
        CancellationToken cancellationToken = default)
    {
        await changeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (current.BoxesEnabled == boxesEnabled)
            {
                return new(ProductBoxesSettingsChangeStatus.Unchanged, current);
            }

            ProductBoxesSettings candidate = current with
            {
                BoxesEnabled = boxesEnabled,
            };
            try
            {
                await store.SaveAsync(candidate, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException)
            {
                return new(ProductBoxesSettingsChangeStatus.Failed, current);
            }

            current = candidate;
            return new(ProductBoxesSettingsChangeStatus.Saved, current);
        }
        finally
        {
            changeGate.Release();
        }
    }

    public async Task<ProductBoxesSettingsChangeResult> ChangeThumbnailsAsync(
        bool thumbnailsEnabled,
        CancellationToken cancellationToken = default)
    {
        await changeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (current.ThumbnailsEnabled == thumbnailsEnabled)
            {
                return new(ProductBoxesSettingsChangeStatus.Unchanged, current);
            }
            ProductBoxesSettings candidate = current with
            {
                ThumbnailsEnabled = thumbnailsEnabled,
            };
            try
            {
                await store.SaveAsync(candidate, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException)
            {
                return new(ProductBoxesSettingsChangeStatus.Failed, current);
            }
            current = candidate;
            return new(ProductBoxesSettingsChangeStatus.Saved, current);
        }
        finally
        {
            changeGate.Release();
        }
    }

    public async Task<ProductBoxesSettingsChangeResult> ChangeSingleClickOpenAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await changeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (current.OpenItemsWithSingleClick == enabled)
            {
                return new(ProductBoxesSettingsChangeStatus.Unchanged, current);
            }
            ProductBoxesSettings candidate = current with
            {
                OpenItemsWithSingleClick = enabled,
            };
            try
            {
                await store.SaveAsync(candidate, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException)
            {
                return new(ProductBoxesSettingsChangeStatus.Failed, current);
            }
            current = candidate;
            return new(ProductBoxesSettingsChangeStatus.Saved, current);
        }
        finally
        {
            changeGate.Release();
        }
    }

    public void Dispose() => changeGate.Dispose();
}
