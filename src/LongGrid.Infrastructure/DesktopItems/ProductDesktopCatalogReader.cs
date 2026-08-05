using System.Security;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Infrastructure.DesktopItems;

public enum ProductDesktopCatalogSourceKind
{
    UserDesktop,
    PublicDesktop,
}

public enum ProductDesktopCatalogSourceStatus
{
    Ready,
    Missing,
    Partial,
    AccessDenied,
    IoFailure,
}

public sealed record ProductDesktopCatalogSourceSnapshot(
    ProductDesktopCatalogSourceKind Source,
    ProductDesktopCatalogSourceStatus Status,
    int ItemCount);

public enum ProductDesktopCatalogReadStatus
{
    Ready,
    Partial,
    Unavailable,
    Failed,
}

public sealed record ProductDesktopCatalogReadResult(
    ProductDesktopCatalogReadStatus Status,
    IReadOnlyList<DesktopCatalogEntry> Entries,
    IReadOnlyList<ProductDesktopCatalogSourceSnapshot> Sources)
{
    public bool IsAuthoritative => Status == ProductDesktopCatalogReadStatus.Ready;
}

public interface IProductDesktopCatalogReader
{
    Task<ProductDesktopCatalogReadResult> ReadAsync(
        CancellationToken cancellationToken = default);
}

public sealed class ProductDesktopCatalogReader : IProductDesktopCatalogReader
{
    private readonly string userDesktopPath;
    private readonly string publicDesktopPath;

    public ProductDesktopCatalogReader(
        string userDesktopPath,
        string publicDesktopPath)
    {
        ArgumentNullException.ThrowIfNull(userDesktopPath);
        ArgumentNullException.ThrowIfNull(publicDesktopPath);
        this.userDesktopPath = userDesktopPath;
        this.publicDesktopPath = publicDesktopPath;
    }

    public static ProductDesktopCatalogReader CreateForCurrentUser() =>
        new(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));

    public Task<ProductDesktopCatalogReadResult> ReadAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Read(cancellationToken), cancellationToken);

    private ProductDesktopCatalogReadResult Read(CancellationToken cancellationToken)
    {
        SourceReadResult[] sources =
        [
            ReadSource(
                ProductDesktopCatalogSourceKind.UserDesktop,
                "user-desktop",
                userDesktopPath,
                cancellationToken),
            ReadSource(
                ProductDesktopCatalogSourceKind.PublicDesktop,
                "public-desktop",
                publicDesktopPath,
                cancellationToken),
        ];
        ProductDesktopCatalogReadStatus status = Classify(sources);
        IReadOnlyList<DesktopCatalogEntry> entries;
        try
        {
            entries = DesktopCatalog.Build(
                sources.SelectMany(source => source.Candidates));
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or ArgumentException
            or NotSupportedException)
        {
            status = ProductDesktopCatalogReadStatus.Failed;
            entries = Array.Empty<DesktopCatalogEntry>();
            sources = sources
                .Select(source => source with
                {
                    Status = ProductDesktopCatalogSourceStatus.IoFailure,
                    Candidates = Array.Empty<DesktopCatalogCandidate>(),
                })
                .ToArray();
        }

        return new(
            status,
            Array.AsReadOnly(entries.ToArray()),
            Array.AsReadOnly(
                sources
                    .Select(source => new ProductDesktopCatalogSourceSnapshot(
                        source.Source,
                        source.Status,
                        source.Candidates.Count))
                    .ToArray()));
    }

    private static SourceReadResult ReadSource(
        ProductDesktopCatalogSourceKind source,
        string sourceId,
        string path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return new(
                source,
                ProductDesktopCatalogSourceStatus.Missing,
                Array.Empty<DesktopCatalogCandidate>());
        }

        try
        {
            var candidates = new List<DesktopCatalogCandidate>();
            bool partial = false;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = false,
                ReturnSpecialDirectories = false,
                AttributesToSkip = 0,
            };
            foreach (string itemPath in Directory.EnumerateFileSystemEntries(
                path,
                "*",
                options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    bool isDirectory = File.GetAttributes(itemPath)
                        .HasFlag(FileAttributes.Directory);
                    candidates.Add(new(sourceId, itemPath, isDirectory));
                }
                catch (Exception exception) when (
                    exception is IOException
                    or UnauthorizedAccessException
                    or SecurityException)
                {
                    partial = true;
                }
            }

            return new(
                source,
                partial
                    ? ProductDesktopCatalogSourceStatus.Partial
                    : ProductDesktopCatalogSourceStatus.Ready,
                Array.AsReadOnly(candidates.ToArray()));
        }
        catch (UnauthorizedAccessException)
        {
            return new(
                source,
                ProductDesktopCatalogSourceStatus.AccessDenied,
                Array.Empty<DesktopCatalogCandidate>());
        }
        catch (SecurityException)
        {
            return new(
                source,
                ProductDesktopCatalogSourceStatus.AccessDenied,
                Array.Empty<DesktopCatalogCandidate>());
        }
        catch (IOException)
        {
            return new(
                source,
                ProductDesktopCatalogSourceStatus.IoFailure,
                Array.Empty<DesktopCatalogCandidate>());
        }
    }

    private static ProductDesktopCatalogReadStatus Classify(
        IReadOnlyList<SourceReadResult> sources)
    {
        if (sources.All(
            source => source.Status == ProductDesktopCatalogSourceStatus.Ready))
        {
            return ProductDesktopCatalogReadStatus.Ready;
        }

        bool hasUsableItems = sources.Any(source => source.Status is
            ProductDesktopCatalogSourceStatus.Ready or
            ProductDesktopCatalogSourceStatus.Partial);
        if (hasUsableItems)
        {
            return ProductDesktopCatalogReadStatus.Partial;
        }

        return sources.All(
            source => source.Status == ProductDesktopCatalogSourceStatus.Missing)
            ? ProductDesktopCatalogReadStatus.Unavailable
            : ProductDesktopCatalogReadStatus.Failed;
    }

    private sealed record SourceReadResult(
        ProductDesktopCatalogSourceKind Source,
        ProductDesktopCatalogSourceStatus Status,
        IReadOnlyList<DesktopCatalogCandidate> Candidates);
}
