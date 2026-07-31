using System.Text.Json;

internal enum ThumbnailWorkerRequestKind
{
    Extract,
    Hang,
}

internal sealed record ThumbnailWorkerRequest(
    string RequestId,
    ThumbnailWorkerRequestKind Kind,
    string? Path,
    int Size,
    ShellItemImageFactoryFlags Flags);

internal sealed record ThumbnailWorkerResponse(
    string RequestId,
    bool Success,
    int HResult,
    int Width,
    int Height,
    double NativeMilliseconds);

internal static class ThumbnailWorkerServer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    internal static async Task<int> RunAsync()
    {
        while (await Console.In.ReadLineAsync() is { } line)
        {
            ThumbnailWorkerRequest? request =
                JsonSerializer.Deserialize<ThumbnailWorkerRequest>(line, JsonOptions);
            if (request is null)
            {
                return 65;
            }

            if (request.Kind == ThumbnailWorkerRequestKind.Hang)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan);
                return 70;
            }

            if (string.IsNullOrWhiteSpace(request.Path))
            {
                return 65;
            }

            ImageExtractionResult result = ShellImageExtractor.Extract(
                request.Path,
                request.Size,
                request.Flags);
            var response = new ThumbnailWorkerResponse(
                request.RequestId,
                result.Success,
                result.HResult,
                result.Width,
                result.Height,
                result.Duration.TotalMilliseconds);
            await Console.Out.WriteLineAsync(
                JsonSerializer.Serialize(response, JsonOptions));
            await Console.Out.FlushAsync();
        }

        return 0;
    }
}
