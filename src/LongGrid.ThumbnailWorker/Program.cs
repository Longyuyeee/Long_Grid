namespace LongGrid.ThumbnailWorker;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        if (args.Length == 2
            && string.Equals(
                args[0],
                "--thumbnail-worker",
                StringComparison.Ordinal)
            && string.Equals(args[1], "--job-only", StringComparison.Ordinal))
        {
            return await ThumbnailWorkerServer.RunAsync(parentProcessId: null);
        }

        if (args.Length == 3
            && string.Equals(
                args[0],
                "--thumbnail-worker",
                StringComparison.Ordinal)
            && string.Equals(args[1], "--parent-pid", StringComparison.Ordinal)
            && int.TryParse(
                args[2],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int parentProcessId)
            && parentProcessId > 0)
        {
            return await ThumbnailWorkerServer.RunAsync(parentProcessId);
        }

        return 64;
    }
}
