using System.Security.Cryptography;

namespace LongGrid.Core.Configuration;

public static class ProductWorkspaceConfigurationFingerprint
{
    public static string Compute(ProductConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Convert.ToHexString(SHA256.HashData(
            ProductConfigurationJson.SerializeToUtf8Bytes(document)));
    }
}
