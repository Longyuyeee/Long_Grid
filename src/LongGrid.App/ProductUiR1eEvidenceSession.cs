using System.Text.Json;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace LongGrid.App;

internal sealed class ProductUiR1eEvidenceSession
{
    internal const string EnvironmentVariableName =
        "LONGGRID_UI_R1E_EVIDENCE_SESSION";
    private const string EvidenceDirectoryName = "LongGridUiR1eEvidence";
    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true };

    private ProductUiR1eEvidenceSession(Guid sessionId, string directoryPath)
    {
        SessionId = sessionId;
        DirectoryPath = directoryPath;
    }

    internal Guid SessionId { get; }

    internal string DirectoryPath { get; }

    internal string ResultPath => Path.Combine(DirectoryPath, "result.json");

    internal static ProductUiR1eEvidenceSession? TryCreateFromEnvironment()
    {
        string? raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!Guid.TryParseExact(raw, "N", out Guid sessionId))
        {
            throw new InvalidOperationException(
                "UI-R1E evidence session id must be a 32-character GUID.");
        }

        string evidenceRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            EvidenceDirectoryName));
        string directoryPath = Path.GetFullPath(Path.Combine(
            evidenceRoot,
            sessionId.ToString("N")));
        string expectedPrefix = evidenceRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!directoryPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(directoryPath))
        {
            throw new InvalidOperationException(
                "UI-R1E evidence directory must already exist under the system temporary evidence root.");
        }

        System.IO.FileAttributes attributes = File.GetAttributes(directoryPath);
        if ((attributes & System.IO.FileAttributes.ReparsePoint) != 0
            || Directory.EnumerateFileSystemEntries(directoryPath).Any())
        {
            throw new InvalidOperationException(
                "UI-R1E evidence directory must be empty and must not be a reparse point.");
        }

        return new(sessionId, directoryPath);
    }

    internal static string ResolveInstanceKey(string defaultKey)
    {
        string? raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return Guid.TryParseExact(raw, "N", out Guid sessionId)
            ? $"LongGrid.UiR1eEvidence.{sessionId:N}"
            : defaultKey;
    }

    internal async Task WriteResultAsync(
        object result,
        IReadOnlyList<ProductUiR1eRenderCapture>? captures = null)
    {
        foreach (ProductUiR1eRenderCapture capture in captures ?? [])
        {
            if (Path.GetFileName(capture.FileName) != capture.FileName
                || !capture.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "UI-R1E capture name must be one local PNG file name.");
            }

            await SaveCaptureAsync(capture);
        }

        string temporaryPath = ResultPath + ".new";
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(result, JsonOptions));
        File.Move(temporaryPath, ResultPath, overwrite: false);
    }

    private async Task SaveCaptureAsync(ProductUiR1eRenderCapture capture)
    {
        string path = Path.Combine(DirectoryPath, capture.FileName);
        await File.WriteAllBytesAsync(path, []);
        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(
            BitmapEncoder.PngEncoderId,
            stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            capture.PixelWidth,
            capture.PixelHeight,
            capture.Dpi,
            capture.Dpi,
            capture.Pixels);
        await encoder.FlushAsync();
    }
}

internal sealed record ProductUiR1eRenderCapture(
    string FileName,
    uint PixelWidth,
    uint PixelHeight,
    double Dpi,
    byte[] Pixels);

internal sealed record ProductUiR1eRenderResult(
    object Evidence,
    IReadOnlyList<ProductUiR1eRenderCapture> Captures);
