using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Configuration;

public enum ProductWorkspaceContainerCreationDefaultsStatus
{
    Ready,
    Invalid,
    LimitReached,
    DuplicateName,
    PlacementUnavailable,
}

public sealed record ProductWorkspaceContainerCreationDefaultsDecision(
    ProductWorkspaceContainerCreationDefaultsStatus Status,
    string? Name,
    ProductContainerPlacementState? Placement)
{
    public bool CanCreate =>
        Status == ProductWorkspaceContainerCreationDefaultsStatus.Ready
        && Name is not null
        && Placement is not null;
}

public static class ProductWorkspaceContainerCreationDefaults
{
    public const string BaseName = "新方格";
    public const double DefaultWidthDip = 360;
    public const double DefaultHeightDip = 240;
    private const double InitialXDip = 32;
    private const double InitialYDip = 48;
    private const double CascadeStepDip = 24;
    private const int CascadeColumns = 8;
    private const int MaximumIdentityAttempts = 16;

    public static string? CreateUniqueId(
        IReadOnlyList<ProductContainerState>? existingContainers)
    {
        if (existingContainers is null
            || existingContainers.Count >=
                ProductConfigurationLimits.MaximumContainers
            || existingContainers.Any(container => container is null
                || string.IsNullOrWhiteSpace(container.Id)))
        {
            return null;
        }

        for (int attempt = 0; attempt < MaximumIdentityAttempts; attempt++)
        {
            string candidate = $"container-{Guid.NewGuid():N}";
            if (!existingContainers.Any(container => string.Equals(
                container.Id,
                candidate,
                StringComparison.Ordinal)))
            {
                return candidate;
            }
        }

        return null;
    }

    public static ProductWorkspaceContainerCreationDefaultsDecision Evaluate(
        IReadOnlyList<ProductContainerState>? existingContainers,
        string? requestedName,
        string? displayId,
        PixelRect workArea,
        uint effectiveDpi)
    {
        if (existingContainers is null
            || existingContainers.Any(container => container is null
                || container.Name is null
                || container.Placement is null)
            || string.IsNullOrWhiteSpace(displayId)
            || displayId.Length >
                ProductConfigurationLimits.MaximumDisplayKeyLength
            || !workArea.HasArea
            || effectiveDpi is < 48 or > 768)
        {
            return Failure(
                ProductWorkspaceContainerCreationDefaultsStatus.Invalid);
        }

        if (existingContainers.Count >=
            ProductConfigurationLimits.MaximumContainers)
        {
            return Failure(
                ProductWorkspaceContainerCreationDefaultsStatus.LimitReached);
        }

        var existingNames = existingContainers
            .Select(container => container.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? name = requestedName is null
            ? ResolveDefaultName(existingNames)
            : NormalizeRequestedName(requestedName);
        if (name is null)
        {
            return Failure(
                ProductWorkspaceContainerCreationDefaultsStatus.Invalid);
        }
        if (existingNames.Contains(name))
        {
            return Failure(
                ProductWorkspaceContainerCreationDefaultsStatus.DuplicateName);
        }

        double scale = effectiveDpi / 96d;
        double workWidthDip = workArea.Width / scale;
        double workHeightDip = workArea.Height / scale;
        double widthDip = Math.Min(DefaultWidthDip, workWidthDip);
        double heightDip = Math.Min(DefaultHeightDip, workHeightDip);
        if (!double.IsFinite(widthDip)
            || !double.IsFinite(heightDip)
            || widthDip <= 0
            || heightDip <= 0)
        {
            return Failure(
                ProductWorkspaceContainerCreationDefaultsStatus.Invalid);
        }

        double maximumX = Math.Max(0, workWidthDip - widthDip);
        double maximumY = Math.Max(0, workHeightDip - heightDip);
        for (int slot = 0;
            slot < ProductConfigurationLimits.MaximumContainers;
            slot++)
        {
            double xDip = Math.Min(
                InitialXDip + ((slot % CascadeColumns) * CascadeStepDip),
                maximumX);
            double yDip = Math.Min(
                InitialYDip + ((slot / CascadeColumns) * CascadeStepDip),
                maximumY);
            bool overlaps = existingContainers.Any(container =>
                string.Equals(
                    container.Placement.DisplayKey,
                    displayId,
                    StringComparison.Ordinal)
                && NearlyEqual(container.Placement.XDip, xDip)
                && NearlyEqual(container.Placement.YDip, yDip)
                && NearlyEqual(container.Placement.WidthDip, widthDip)
                && NearlyEqual(container.Placement.HeightDip, heightDip));
            if (!overlaps)
            {
                return new(
                    ProductWorkspaceContainerCreationDefaultsStatus.Ready,
                    name,
                    new()
                    {
                        DisplayKey = displayId,
                        XDip = xDip,
                        YDip = yDip,
                        WidthDip = widthDip,
                        HeightDip = heightDip,
                    });
            }
        }

        return Failure(
            ProductWorkspaceContainerCreationDefaultsStatus.PlacementUnavailable);
    }

    private static string? ResolveDefaultName(HashSet<string> existingNames)
    {
        if (!existingNames.Contains(BaseName))
        {
            return BaseName;
        }

        for (int suffix = 2;
            suffix <= ProductConfigurationLimits.MaximumContainers + 1;
            suffix++)
        {
            string candidate = $"{BaseName} {suffix}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private static string? NormalizeRequestedName(string requestedName)
    {
        string normalized = requestedName.Trim();
        return normalized.Length is > 0
            and <= ProductConfigurationLimits.MaximumNameLength
            && !normalized.Any(char.IsControl)
                ? normalized
                : null;
    }

    private static bool NearlyEqual(double left, double right) =>
        double.IsFinite(left)
        && Math.Abs(left - right) < 0.001;

    private static ProductWorkspaceContainerCreationDefaultsDecision Failure(
        ProductWorkspaceContainerCreationDefaultsStatus status) =>
        new(status, Name: null, Placement: null);
}
