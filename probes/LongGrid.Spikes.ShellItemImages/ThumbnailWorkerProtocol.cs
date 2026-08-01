using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

internal enum ThumbnailWorkerRequestKind
{
    Extract,
    Hang,
    MalformedResponse,
    WrongVersionResponse,
    OversizedResponse,
    InvalidPixelFormatResponse,
    InvalidPixelDimensionsResponse,
    InvalidPixelStrideResponse,
    InvalidPixelLengthResponse,
    MalformedPixelPayload,
    UnexpectedPixelPayloadResponse,
    Exit,
}

internal sealed record ThumbnailWorkerRequest(
    int ProtocolVersion,
    string RequestId,
    ThumbnailWorkerRequestKind Kind,
    string? Path,
    int Size,
    ShellItemImageFactoryFlags Flags,
    bool IncludePixels = false);

internal enum ThumbnailPixelFormat
{
    Bgra32 = 1,
}

internal sealed record ThumbnailPixelPayload(
    ThumbnailPixelFormat Format,
    int Width,
    int Height,
    int Stride,
    int ByteLength,
    byte[] Bytes);

internal sealed record ThumbnailWorkerResponse(
    int ProtocolVersion,
    string RequestId,
    bool Success,
    int HResult,
    int Width,
    int Height,
    ThumbnailPixelPayload? Pixels,
    double NativeMilliseconds);

internal static class ThumbnailWorkerServer
{
    internal const int CurrentProtocolVersion = 2;
    internal const int MaximumRequestCharacters = 65_536;
    internal const int MaximumPixelDimension = 256;
    internal const int MaximumPixelBytes =
        MaximumPixelDimension * MaximumPixelDimension * 4;
    internal const int MaximumResponseCharacters = 400_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal static async Task<int> RunAsync(int parentProcessId)
    {
        using Process? parentProcess = TryOpenParentProcess(parentProcessId);
        if (parentProcess is null || parentProcess.HasExited)
        {
            return 72;
        }

        _ = ExitWhenParentExitsAsync(parentProcess);
        var reader = new BoundedLineReader(
            Console.In,
            MaximumRequestCharacters);

        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync();
            }
            catch (InvalidDataException)
            {
                return 66;
            }

            if (line is null)
            {
                return 0;
            }

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

            if (request.Kind == ThumbnailWorkerRequestKind.WrongVersionResponse)
            {
                await WriteResponseAsync(new ThumbnailWorkerResponse(
                    CurrentProtocolVersion + 1,
                    request.RequestId,
                    Success: true,
                    HResult: 0,
                    Width: 0,
                    Height: 0,
                    Pixels: null,
                    NativeMilliseconds: 0));
                continue;
            }

            if (request.Kind == ThumbnailWorkerRequestKind.OversizedResponse)
            {
                await Console.Out.WriteLineAsync(
                    new string('x', MaximumResponseCharacters + 1));
                await Console.Out.FlushAsync();
                continue;
            }

            if (request.Kind == ThumbnailWorkerRequestKind.MalformedPixelPayload)
            {
                await Console.Out.WriteLineAsync(
                    $$"""{"ProtocolVersion":{{CurrentProtocolVersion}},"RequestId":"{{request.RequestId}}","Success":true,"HResult":0,"Width":1,"Height":1,"Pixels":{"Format":1,"Width":1,"Height":1,"Stride":4,"ByteLength":4,"Bytes":"%%%"},"NativeMilliseconds":0}""");
                await Console.Out.FlushAsync();
                continue;
            }

            if (request.Kind is ThumbnailWorkerRequestKind.InvalidPixelFormatResponse
                or ThumbnailWorkerRequestKind.InvalidPixelDimensionsResponse
                or ThumbnailWorkerRequestKind.InvalidPixelStrideResponse
                or ThumbnailWorkerRequestKind.InvalidPixelLengthResponse
                or ThumbnailWorkerRequestKind.UnexpectedPixelPayloadResponse)
            {
                ThumbnailPixelPayload invalidPixels = CreateInvalidPixelPayload(
                    request.Kind);
                await WriteResponseAsync(new ThumbnailWorkerResponse(
                    CurrentProtocolVersion,
                    request.RequestId,
                    Success: true,
                    HResult: 0,
                    Width: 1,
                    Height: 1,
                    invalidPixels,
                    NativeMilliseconds: 0));
                continue;
            }

            if (request.Kind == ThumbnailWorkerRequestKind.Exit)
            {
                return 71;
            }

            if (request.Kind != ThumbnailWorkerRequestKind.Extract
                || string.IsNullOrWhiteSpace(request.Path)
                || request.Path.Length > 32_767
                || request.Size is < 1 or > 1_024
                || (request.IncludePixels
                    && request.Size > MaximumPixelDimension))
            {
                return 65;
            }

            ImageExtractionResult result = ShellImageExtractor.Extract(
                request.Path,
                request.Size,
                request.Flags,
                request.IncludePixels);
            ThumbnailPixelPayload? pixels = result.Pixels is null
                ? null
                : new ThumbnailPixelPayload(
                    ThumbnailPixelFormat.Bgra32,
                    result.Pixels.Width,
                    result.Pixels.Height,
                    result.Pixels.Stride,
                    result.Pixels.Bytes.Length,
                    result.Pixels.Bytes);
            var response = new ThumbnailWorkerResponse(
                CurrentProtocolVersion,
                request.RequestId,
                result.Success,
                result.HResult,
                result.Width,
                result.Height,
                pixels,
                result.Duration.TotalMilliseconds);
            await WriteResponseAsync(response);
        }
    }

    private static ThumbnailPixelPayload CreateInvalidPixelPayload(
        ThumbnailWorkerRequestKind kind)
    {
        return kind switch
        {
            ThumbnailWorkerRequestKind.InvalidPixelFormatResponse =>
                new ThumbnailPixelPayload(
                    (ThumbnailPixelFormat)99,
                    1,
                    1,
                    4,
                    4,
                    new byte[4]),
            ThumbnailWorkerRequestKind.InvalidPixelDimensionsResponse =>
                new ThumbnailPixelPayload(
                    ThumbnailPixelFormat.Bgra32,
                    2,
                    1,
                    8,
                    8,
                    new byte[8]),
            ThumbnailWorkerRequestKind.InvalidPixelStrideResponse =>
                new ThumbnailPixelPayload(
                    ThumbnailPixelFormat.Bgra32,
                    1,
                    1,
                    8,
                    8,
                    new byte[8]),
            ThumbnailWorkerRequestKind.InvalidPixelLengthResponse =>
                new ThumbnailPixelPayload(
                    ThumbnailPixelFormat.Bgra32,
                    1,
                    1,
                    4,
                    8,
                    new byte[4]),
            ThumbnailWorkerRequestKind.UnexpectedPixelPayloadResponse =>
                new ThumbnailPixelPayload(
                    ThumbnailPixelFormat.Bgra32,
                    1,
                    1,
                    4,
                    4,
                    new byte[4]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static Process? TryOpenParentProcess(int parentProcessId)
    {
        try
        {
            return Process.GetProcessById(parentProcessId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static async Task ExitWhenParentExitsAsync(Process parentProcess)
    {
        try
        {
            await parentProcess.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
        }

        Environment.Exit(0);
    }

    private static async Task WriteResponseAsync(
        ThumbnailWorkerResponse response)
    {
        await Console.Out.WriteLineAsync(
            JsonSerializer.Serialize(response, JsonOptions));
        await Console.Out.FlushAsync();
    }
}
