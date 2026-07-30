using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LongGrid.Core.DesktopHost;

public enum DisplayRotation
{
    Unknown,
    Landscape,
    Portrait,
    LandscapeFlipped,
    PortraitFlipped,
}

public sealed record DisplayTopologyNode(
    string StableId,
    PixelRect Bounds,
    PixelRect WorkArea,
    uint EffectiveDpi,
    DisplayRotation Rotation,
    bool IsPrimary);

public static class DisplayTopologyFingerprint
{
    public static string Compute(IEnumerable<DisplayTopologyNode> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        DisplayTopologyNode[] displayArray = displays.ToArray();
        Validate(displayArray);

        int originX = displayArray.Min(display => display.Bounds.Left);
        int originY = displayArray.Min(display => display.Bounds.Top);
        var canonical = new StringBuilder();

        foreach (DisplayTopologyNode display in displayArray
            .OrderBy(display => display.StableId, StringComparer.Ordinal))
        {
            Append(canonical, display.StableId);
            Append(canonical, display.Bounds.Left - originX);
            Append(canonical, display.Bounds.Top - originY);
            Append(canonical, display.Bounds.Width);
            Append(canonical, display.Bounds.Height);
            Append(canonical, display.WorkArea.Left - originX);
            Append(canonical, display.WorkArea.Top - originY);
            Append(canonical, display.WorkArea.Width);
            Append(canonical, display.WorkArea.Height);
            Append(canonical, display.EffectiveDpi);
            Append(canonical, (int)display.Rotation);
            Append(canonical, display.IsPrimary ? 1 : 0);
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(hash);
    }

    private static void Validate(IReadOnlyList<DisplayTopologyNode> displays)
    {
        if (displays.Count == 0)
        {
            throw new ArgumentException("At least one display is required.", nameof(displays));
        }

        if (displays.Count(display => display.IsPrimary) != 1)
        {
            throw new ArgumentException(
                "Exactly one display must be primary.",
                nameof(displays));
        }

        if (displays.Any(display =>
            string.IsNullOrWhiteSpace(display.StableId)
            || !display.Bounds.HasArea
            || !display.WorkArea.HasArea
            || display.EffectiveDpi is < 48 or > 768))
        {
            throw new ArgumentException(
                "Every display requires an ID, valid bounds, and a plausible DPI.",
                nameof(displays));
        }

        if (displays
            .Select(display => display.StableId)
            .Distinct(StringComparer.Ordinal)
            .Count() != displays.Count)
        {
            throw new ArgumentException("Display IDs must be unique.", nameof(displays));
        }
    }

    private static void Append(StringBuilder destination, string value)
    {
        destination.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        destination.Append(':');
        destination.Append(value);
        destination.Append('|');
    }

    private static void Append(StringBuilder destination, int value)
    {
        destination.Append(value.ToString(CultureInfo.InvariantCulture));
        destination.Append('|');
    }

    private static void Append(StringBuilder destination, uint value)
    {
        destination.Append(value.ToString(CultureInfo.InvariantCulture));
        destination.Append('|');
    }
}
