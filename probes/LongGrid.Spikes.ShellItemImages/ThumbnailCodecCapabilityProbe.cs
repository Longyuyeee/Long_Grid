using System.Runtime.InteropServices;

internal static class ThumbnailCodecCapabilityProbe
{
    private const int MediaFoundationVersion = 0x00020070;
    private const int MediaFoundationStartupLite = 1;
    private const uint MftEnumFlagAll = 0x3f;
    private static readonly Guid VideoDecoderCategory =
        new("D6C02D4B-6833-45B4-971A-05A4B04BAB91");
    private static readonly Guid VideoMediaType =
        new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid HevcVideoFormat =
        new("43564548-0000-0010-8000-00AA00389B71");
    private static readonly Guid Av1VideoFormat =
        new("31305641-0000-0010-8000-00AA00389B71");

    internal static ThumbnailCodecCapabilityResult Run()
    {
        int startupHResult = MFStartup(
            MediaFoundationVersion,
            MediaFoundationStartupLite);
        if (startupHResult < 0)
        {
            return new ThumbnailCodecCapabilityResult(
                StartupSucceeded: false,
                StartupHResult: startupHResult,
                Hevc: ThumbnailDecoderCapability.NotQueried,
                Av1: ThumbnailDecoderCapability.NotQueried);
        }

        try
        {
            return new ThumbnailCodecCapabilityResult(
                StartupSucceeded: true,
                StartupHResult: startupHResult,
                Hevc: QueryDecoder(HevcVideoFormat),
                Av1: QueryDecoder(Av1VideoFormat));
        }
        finally
        {
            _ = MFShutdown();
        }
    }

    private static ThumbnailDecoderCapability QueryDecoder(Guid subtype)
    {
        var inputType = new MftRegisterTypeInfo(VideoMediaType, subtype);
        nint activateArray = 0;
        uint count = 0;
        int hResult = MFTEnumEx(
            VideoDecoderCategory,
            MftEnumFlagAll,
            inputType,
            0,
            out activateArray,
            out count);
        try
        {
            return new ThumbnailDecoderCapability(
                QuerySucceeded: hResult >= 0,
                HResult: hResult,
                DecoderAvailable: hResult >= 0 && count > 0);
        }
        finally
        {
            if (activateArray != 0)
            {
                for (uint index = 0; index < count; index++)
                {
                    nint activate = Marshal.ReadIntPtr(
                        activateArray,
                        checked((int)(index * (uint)nint.Size)));
                    if (activate != 0)
                    {
                        _ = Marshal.Release(activate);
                    }
                }

                Marshal.FreeCoTaskMem(activateArray);
            }
        }
    }

    [DllImport("mfplat.dll")]
    private static extern int MFStartup(int version, int flags);

    [DllImport("mfplat.dll")]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll")]
    private static extern int MFTEnumEx(
        in Guid category,
        uint flags,
        in MftRegisterTypeInfo inputType,
        nint outputType,
        out nint activateArray,
        out uint activateCount);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct MftRegisterTypeInfo(Guid MajorType, Guid Subtype);
}

internal sealed record ThumbnailCodecCapabilityResult(
    bool StartupSucceeded,
    int StartupHResult,
    ThumbnailDecoderCapability Hevc,
    ThumbnailDecoderCapability Av1)
{
    internal bool AllQueriesSucceeded =>
        StartupSucceeded && Hevc.QuerySucceeded && Av1.QuerySucceeded;
}

internal sealed record ThumbnailDecoderCapability(
    bool QuerySucceeded,
    int HResult,
    bool DecoderAvailable)
{
    internal static ThumbnailDecoderCapability NotQueried { get; } =
        new(QuerySucceeded: false, HResult: 0, DecoderAvailable: false);
}
