using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LongGrid.ThumbnailWorker;

internal enum ThumbnailWorkerRequestKind
{
    Extract,
    ReadBoundaryProbe,
    WriteBoundaryProbe,
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
    MissingPixelBufferRequest,
    InvalidPixelBufferCapacityRequest,
    Exit,
}

internal enum ThumbnailInputTransport
{
    DirectPath = 1,
    ControlledCopy = 2,
    MinimumPathAcl = 3,
}

internal sealed record ThumbnailWorkerRequest(
    int ProtocolVersion,
    string RequestId,
    ThumbnailWorkerRequestKind Kind,
    string? Path,
    int Size,
    ShellItemImageFactoryFlags Flags,
    bool IncludePixels = false,
    long? PixelBufferHandle = null,
    int PixelBufferCapacity = 0,
    ThumbnailInputTransport InputTransport = ThumbnailInputTransport.DirectPath);

internal enum ThumbnailPixelFormat
{
    Bgra32 = 1,
}

internal enum ThumbnailPixelTransport
{
    InlineBase64 = 1,
    SharedMemory = 2,
}

internal sealed record ThumbnailPixelPayload(
    ThumbnailPixelTransport Transport,
    ThumbnailPixelFormat Format,
    int Width,
    int Height,
    int Stride,
    int ByteLength,
    byte[]? Bytes);

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
    internal const int CurrentProtocolVersion = 6;
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

    internal static async Task<int> RunAsync(int? parentProcessId)
    {
        using Process? parentProcess = parentProcessId is int processId
            ? TryOpenParentProcess(processId)
            : null;
        if (parentProcessId is not null
            && (parentProcess is null || parentProcess.HasExited))
        {
            return 72;
        }

        if (parentProcess is not null)
        {
            _ = ExitWhenParentExitsAsync(parentProcess);
        }
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

            if (request.Kind is ThumbnailWorkerRequestKind.MissingPixelBufferRequest
                or ThumbnailWorkerRequestKind.InvalidPixelBufferCapacityRequest)
            {
                request = request with
                {
                    Kind = ThumbnailWorkerRequestKind.Extract,
                };
            }

            if (request.Kind is ThumbnailWorkerRequestKind.ReadBoundaryProbe
                or ThumbnailWorkerRequestKind.WriteBoundaryProbe)
            {
                if (string.IsNullOrWhiteSpace(request.Path)
                    || request.Path.Length > 32_767)
                {
                    return 65;
                }

                var stopwatch = Stopwatch.StartNew();
                bool boundaryObserved;
                if (request.Kind == ThumbnailWorkerRequestKind.ReadBoundaryProbe)
                {
                    boundaryObserved = false;
                    try
                    {
                        using FileStream stream = File.OpenRead(request.Path);
                        boundaryObserved = stream.ReadByte() >= 0;
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                    catch (IOException)
                    {
                    }
                }
                else
                {
                    boundaryObserved = false;
                    try
                    {
                        await File.WriteAllTextAsync(
                            request.Path,
                            "must-not-write");
                    }
                    catch (UnauthorizedAccessException)
                    {
                        boundaryObserved = true;
                    }
                }

                stopwatch.Stop();
                await WriteResponseAsync(new ThumbnailWorkerResponse(
                    CurrentProtocolVersion,
                    request.RequestId,
                    Success: boundaryObserved,
                    HResult: 0,
                    Width: boundaryObserved ? 1 : 0,
                    Height: boundaryObserved ? 1 : 0,
                    Pixels: null,
                    stopwatch.Elapsed.TotalMilliseconds));
                continue;
            }

            if (request.Kind != ThumbnailWorkerRequestKind.Extract
                || string.IsNullOrWhiteSpace(request.Path)
                || request.Path.Length > 32_767
                || request.InputTransport is not (
                    ThumbnailInputTransport.ControlledCopy
                    or ThumbnailInputTransport.MinimumPathAcl)
                || request.Size is < 1 or > 1_024
                || (request.IncludePixels
                    && (request.Size > MaximumPixelDimension
                        || request.PixelBufferHandle is null or <= 0
                        || request.PixelBufferCapacity != MaximumPixelBytes))
                || (!request.IncludePixels
                    && (request.PixelBufferHandle is not null
                        || request.PixelBufferCapacity != 0)))
            {
                return 65;
            }

            ImageExtractionResult result = ShellImageExtractor.Extract(
                request.Path,
                request.Size,
                request.Flags,
                request.IncludePixels);
            using var pixelMappingHandle = request.IncludePixels
                ? new Microsoft.Win32.SafeHandles.SafeFileHandle(
                    new nint(request.PixelBufferHandle!.Value),
                    ownsHandle: true)
                : null;
            ThumbnailPixelPayload? pixels = result.Pixels is null
                ? null
                : WritePixelsToSharedMemory(
                    pixelMappingHandle!,
                    request.PixelBufferCapacity,
                    result.Pixels);
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

    private static ThumbnailPixelPayload WritePixelsToSharedMemory(
        Microsoft.Win32.SafeHandles.SafeFileHandle mappingHandle,
        int capacity,
        BitmapPixelData pixels)
    {
        ThumbnailSharedMemoryTransfer.Write(
            mappingHandle,
            pixels.Bytes,
            capacity);
        return new ThumbnailPixelPayload(
            ThumbnailPixelTransport.SharedMemory,
            ThumbnailPixelFormat.Bgra32,
            pixels.Width,
            pixels.Height,
            pixels.Stride,
            pixels.Bytes.Length,
            Bytes: null);
    }

    private static ThumbnailPixelPayload CreateInvalidPixelPayload(
        ThumbnailWorkerRequestKind kind)
    {
        return kind switch
        {
            ThumbnailWorkerRequestKind.InvalidPixelFormatResponse =>
                new ThumbnailPixelPayload(
                    ThumbnailPixelTransport.SharedMemory,
                    (ThumbnailPixelFormat)99,
                    1,
                    1,
                    4,
                    4,
                    Bytes: null),
            ThumbnailWorkerRequestKind.InvalidPixelDimensionsResponse =>
                new ThumbnailPixelPayload(
                    ThumbnailPixelTransport.SharedMemory,
                    ThumbnailPixelFormat.Bgra32,
                    2,
                    1,
                    8,
                    8,
                    Bytes: null),
            ThumbnailWorkerRequestKind.InvalidPixelStrideResponse =>
                new ThumbnailPixelPayload(
                    ThumbnailPixelTransport.SharedMemory,
                    ThumbnailPixelFormat.Bgra32,
                    1,
                    1,
                    8,
                    8,
                    Bytes: null),
            ThumbnailWorkerRequestKind.InvalidPixelLengthResponse =>
                new ThumbnailPixelPayload(
                    ThumbnailPixelTransport.SharedMemory,
                    ThumbnailPixelFormat.Bgra32,
                    1,
                    1,
                    4,
                    8,
                    Bytes: null),
            ThumbnailWorkerRequestKind.UnexpectedPixelPayloadResponse =>
                new ThumbnailPixelPayload(
                    ThumbnailPixelTransport.SharedMemory,
                    ThumbnailPixelFormat.Bgra32,
                    1,
                    1,
                    4,
                    4,
                    Bytes: null),
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
