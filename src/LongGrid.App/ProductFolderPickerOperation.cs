using System.Runtime.InteropServices;

namespace LongGrid.App;

internal enum ProductFolderPickerStatus
{
    Selected,
    Cancelled,
    Unavailable,
    Busy,
}

internal sealed record ProductFolderPickerResult(ProductFolderPickerStatus Status, string? Path = null);

internal sealed class ProductFolderPickerOperation
{
    private int active;

    internal bool IsActive => Volatile.Read(ref active) != 0;

    internal async Task<ProductFolderPickerResult> PickAsync(Func<Task<string?>> pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
        {
            return new(ProductFolderPickerStatus.Busy);
        }

        try
        {
            string? path = await pick();
            return path is null
                ? new(ProductFolderPickerStatus.Cancelled)
                : new(ProductFolderPickerStatus.Selected, path);
        }
        catch (OperationCanceledException)
        {
            return new(ProductFolderPickerStatus.Cancelled);
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException)
        {
            // Do not expose Shell exception messages, which may contain user paths.
            return new(ProductFolderPickerStatus.Unavailable);
        }
        finally
        {
            Volatile.Write(ref active, 0);
        }
    }
}
