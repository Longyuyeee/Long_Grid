namespace LongGrid.App;

internal static class ProductDesktopPreviewContinuation
{
    internal static async Task<T> RunWithCleanupAsync<T>(Func<Task<T>> operation, Action cleanup)
    {
        try
        {
            return await operation();
        }
        finally
        {
            cleanup();
        }
    }

    // Called on the UI dispatcher after an await. A closed preview must never
    // be repositioned or submitted by delayed presentation/evidence work.
    internal static bool TryRun(Task completion, Action action)
    {
        if (completion.IsCompleted)
        {
            return false;
        }

        action();
        return true;
    }
}
