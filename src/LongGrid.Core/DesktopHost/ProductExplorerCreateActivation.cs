using System.Globalization;

namespace LongGrid.Core.DesktopHost;

public enum ProductExplorerCreateActivationStatus
{
    NotPresent,
    Ready,
    MultipleCommands,
    ArgumentTooLong,
    InvalidFormat,
    UnsupportedVersion,
    CoordinateOutOfRange,
    InvalidIssuedAt,
    Stale,
    InvalidNonce,
}

public sealed record ProductExplorerCreateActivationIntent(
    int Version,
    int ScreenX,
    int ScreenY,
    DateTimeOffset IssuedAt,
    Guid Nonce);

public sealed record ProductExplorerCreateActivationDecision(
    ProductExplorerCreateActivationStatus Status,
    ProductExplorerCreateActivationIntent? Intent)
{
    public bool IsCommand =>
        Status != ProductExplorerCreateActivationStatus.NotPresent;

    public bool CanActivate =>
        Status == ProductExplorerCreateActivationStatus.Ready
        && Intent is not null;
}

public static class ProductExplorerCreateActivation
{
    public const string CommandPrefix = "--long-grid-create-box=";
    public const int CurrentVersion = 1;
    public const int MaximumAbsoluteCoordinate = 1_000_000;
    public const int MaximumArgumentLength = 256;
    public static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromSeconds(5);

    public static ProductExplorerCreateActivationDecision Parse(
        IEnumerable<string>? arguments,
        DateTimeOffset now)
    {
        if (arguments is null)
        {
            return Decision(ProductExplorerCreateActivationStatus.NotPresent);
        }

        string[] commands = arguments
            .Where(argument => argument is not null
                && argument.StartsWith(
                    CommandPrefix,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray()!;
        if (commands.Length == 0)
        {
            return Decision(ProductExplorerCreateActivationStatus.NotPresent);
        }
        if (commands.Length != 1)
        {
            return Decision(ProductExplorerCreateActivationStatus.MultipleCommands);
        }

        string command = commands[0];
        if (command.Length > MaximumArgumentLength)
        {
            return Decision(ProductExplorerCreateActivationStatus.ArgumentTooLong);
        }

        string[] fields = command[CommandPrefix.Length..].Split(',');
        if (fields.Length != 5)
        {
            return Decision(ProductExplorerCreateActivationStatus.InvalidFormat);
        }
        if (!string.Equals(fields[0], "v1", StringComparison.Ordinal))
        {
            return Decision(ProductExplorerCreateActivationStatus.UnsupportedVersion);
        }
        if (!int.TryParse(
                fields[1],
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out int x)
            || !int.TryParse(
                fields[2],
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out int y))
        {
            return Decision(ProductExplorerCreateActivationStatus.InvalidFormat);
        }
        if (Math.Abs((long)x) > MaximumAbsoluteCoordinate
            || Math.Abs((long)y) > MaximumAbsoluteCoordinate)
        {
            return Decision(
                ProductExplorerCreateActivationStatus.CoordinateOutOfRange);
        }
        if (!long.TryParse(
                fields[3],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long issuedAtUnixMilliseconds))
        {
            return Decision(ProductExplorerCreateActivationStatus.InvalidIssuedAt);
        }

        DateTimeOffset issuedAt;
        try
        {
            issuedAt = DateTimeOffset.FromUnixTimeMilliseconds(
                issuedAtUnixMilliseconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Decision(ProductExplorerCreateActivationStatus.InvalidIssuedAt);
        }

        TimeSpan age = now - issuedAt;
        if (age > MaximumAge || age < -MaximumFutureSkew)
        {
            return Decision(ProductExplorerCreateActivationStatus.Stale);
        }
        if (!Guid.TryParseExact(fields[4], "N", out Guid nonce)
            || nonce == Guid.Empty)
        {
            return Decision(ProductExplorerCreateActivationStatus.InvalidNonce);
        }

        return new(
            ProductExplorerCreateActivationStatus.Ready,
            new(CurrentVersion, x, y, issuedAt, nonce));
    }

    public static string Format(ProductExplorerCreateActivationIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.Version != CurrentVersion
            || Math.Abs((long)intent.ScreenX) > MaximumAbsoluteCoordinate
            || Math.Abs((long)intent.ScreenY) > MaximumAbsoluteCoordinate
            || intent.Nonce == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intent),
                "Explorer create activation intent is outside the v1 contract.");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{CommandPrefix}v1,{intent.ScreenX},{intent.ScreenY}," +
                $"{intent.IssuedAt.ToUnixTimeMilliseconds()},{intent.Nonce:N}");
    }

    private static ProductExplorerCreateActivationDecision Decision(
        ProductExplorerCreateActivationStatus status) => new(status, Intent: null);
}
