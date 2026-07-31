using System.Text.Json;
using System.Text.Json.Serialization;

internal enum ThumbnailWorkerRequestKind
{
    Extract,
    Hang,
    MalformedResponse,
    Exit,
}

internal sealed record ThumbnailWorkerRequest(
    int ProtocolVersion,
    string RequestId,
    ThumbnailWorkerRequestKind Kind,
    string? Path,
    int Size,
    ShellItemImageFactoryFlags Flags);

internal sealed record ThumbnailWorkerResponse(
    int ProtocolVersion,
    string RequestId,
    bool Success,
    int HResult,
    int Width,
    int Height,
    double NativeMilliseconds);

internal static class ThumbnailWorkerServer
{
    internal const int CurrentProtocolVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
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

            if (request.ProtocolVersion != CurrentProtocolVersion
                || string.IsNullOrWhiteSpace(request.RequestId)
                || request.RequestId.Length > 64)
            {
                return 65;
            }

            if (request.Kind == ThumbnailWorkerRequestKind.Hang)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan);
                return 70;
            }

            if (request.Kind == ThumbnailWorkerRequestKind.MalformedResponse)
            {
                await Console.Out.WriteLineAsync("{malformed");
                await Console.Out.FlushAsync();
                continue;
            }

            if (request.Kind == ThumbnailWorkerRequestKind.Exit)
            {
                return 71;
            }

            if (request.Kind != ThumbnailWorkerRequestKind.Extract
                || string.IsNullOrWhiteSpace(request.Path)
                || request.Path.Length > 32_767
                || request.Size is < 1 or > 1_024)
            {
                return 65;
            }

            ImageExtractionResult result = ShellImageExtractor.Extract(
                request.Path,
                request.Size,
                request.Flags);
            var response = new ThumbnailWorkerResponse(
                CurrentProtocolVersion,
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
