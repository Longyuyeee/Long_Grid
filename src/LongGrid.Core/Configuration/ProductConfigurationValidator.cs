namespace LongGrid.Core.Configuration;

public static class ProductConfigurationLimits
{
    public const int CurrentSchemaVersion = 4;
    public const int MaximumSerializedBytes = 4 * 1024 * 1024;
    public const int MaximumContainers = 100;
    public const int MaximumItems = 500;
    public const int MaximumIdLength = 128;
    public const int MaximumNameLength = 256;
    public const int MaximumDisplayKeyLength = 256;
    public const int MaximumSavedDisplays = 32;
    public const int MaximumDisplayCoordinate = 1_000_000;
    public const int MaximumDisplayDimension = 100_000;
    public const int MaximumTargetLength = 32_768;
    public const int MaximumExtensionPropertiesPerObject = 64;
    public const int MaximumExtensionPropertyNameLength = 128;
    public const double MinimumContainerWidthDip = 64;
    public const double MinimumContainerHeightDip = 48;
    public const double MaximumContainerDimensionDip = 16_384;
    public const double MaximumAbsoluteCoordinateDip = 1_000_000;
}

public enum ProductConfigurationError
{
    None,
    MalformedJson,
    DocumentTooLarge,
    UnsupportedSchema,
    InvalidProfile,
    TooManyContainers,
    DuplicateObjectId,
    InvalidContainer,
    InvalidAppearance,
    InvalidPlacement,
    InvalidDisplayTopology,
    TooManyItems,
    InvalidItem,
    InvalidExtensionData,
}

public readonly record struct ProductConfigurationValidationResult(
    ProductConfigurationError Error)
{
    public bool IsValid => Error == ProductConfigurationError.None;

    public static ProductConfigurationValidationResult Valid =>
        new(ProductConfigurationError.None);
}

public static class ProductConfigurationValidator
{
    public static ProductConfigurationValidationResult Validate(
        ProductConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.SchemaVersion != ProductConfigurationLimits.CurrentSchemaVersion)
        {
            return new(ProductConfigurationError.UnsupportedSchema);
        }

        if (!IsBoundedText(document.ProfileId, ProductConfigurationLimits.MaximumIdLength)
            || document.Containers is null)
        {
            return new(ProductConfigurationError.InvalidProfile);
        }

        if (!IsValidExtensionData(
            document.ExtensionData,
            "schemaVersion",
            "profileId",
            "containers",
            "savedDisplayTopology"))
        {
            return new(ProductConfigurationError.InvalidExtensionData);
        }

        if (document.Containers.Count > ProductConfigurationLimits.MaximumContainers)
        {
            return new(ProductConfigurationError.TooManyContainers);
        }

        if (!IsValidSavedDisplayTopology(document.SavedDisplayTopology))
        {
            return new(ProductConfigurationError.InvalidDisplayTopology);
        }

        HashSet<string> objectIds = new(StringComparer.Ordinal);
        int totalItems = 0;

        foreach (ContainerConfiguration? container in document.Containers)
        {
            if (container is null
                || !IsBoundedText(container.Id, ProductConfigurationLimits.MaximumIdLength)
                || !IsBoundedText(container.Name, ProductConfigurationLimits.MaximumNameLength)
                || container.Items is null)
            {
                return new(ProductConfigurationError.InvalidContainer);
            }

            if (!objectIds.Add(container.Id))
            {
                return new(ProductConfigurationError.DuplicateObjectId);
            }

            if (!IsValidExtensionData(
                container.ExtensionData,
                "id",
                "name",
                "isLocked",
                "appearance",
                "placement",
                "items",
                "folderBinding"))
            {
                return new(ProductConfigurationError.InvalidExtensionData);
            }

            if (!IsValidAppearance(container.Appearance))
            {
                return new(ProductConfigurationError.InvalidAppearance);
            }

            if (!IsValidExtensionData(
                container.Appearance.ExtensionData,
                "color",
                "opacity",
                "collapsed",
                "titleVisibility",
                "titleDoubleClickAction"))
            {
                return new(ProductConfigurationError.InvalidExtensionData);
            }

            if (!IsValidPlacement(container.Placement))
            {
                return new(ProductConfigurationError.InvalidPlacement);
            }

            if (!IsValidFolderBinding(container.FolderBinding))
            {
                return new(ProductConfigurationError.InvalidContainer);
            }

            if (!IsValidExtensionData(
                container.Placement.ExtensionData,
                "displayKey",
                "xDip",
                "yDip",
                "widthDip",
                "heightDip"))
            {
                return new(ProductConfigurationError.InvalidExtensionData);
            }

            if (container.Items.Count > ProductConfigurationLimits.MaximumItems - totalItems)
            {
                return new(ProductConfigurationError.TooManyItems);
            }

            totalItems += container.Items.Count;
            foreach (DesktopItemReferenceConfiguration? item in container.Items)
            {
                if (!IsValidItem(item))
                {
                    return new(ProductConfigurationError.InvalidItem);
                }

                DesktopItemReferenceConfiguration validItem = item!;
                if (!IsValidExtensionData(
                    validItem.ExtensionData,
                    "id",
                    "kind",
                    "target",
                    "behavior"))
                {
                    return new(ProductConfigurationError.InvalidExtensionData);
                }

                if (!objectIds.Add(validItem.Id))
                {
                    return new(ProductConfigurationError.DuplicateObjectId);
                }
            }
        }

        return ProductConfigurationValidationResult.Valid;
    }

    private static bool IsValidSavedDisplayTopology(
        IReadOnlyList<SavedDisplayConfiguration>? displays)
    {
        if (displays is null)
        {
            return true;
        }

        if (displays.Count is 0 or > ProductConfigurationLimits.MaximumSavedDisplays
            || displays.Count(display => display?.IsPrimary == true) != 1)
        {
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (SavedDisplayConfiguration? display in displays)
        {
            if (display is null
                || !IsBoundedText(
                    display.StableId,
                    ProductConfigurationLimits.MaximumDisplayKeyLength)
                || !ids.Add(display.StableId)
                || display.EffectiveDpi is < 48 or > 768
                || !Enum.IsDefined(display.Rotation)
                || display.Rotation == LongGrid.Core.DesktopHost.DisplayRotation.Unknown
                || !IsValidRect(display.Bounds)
                || !IsValidRect(display.WorkArea)
                || !Contains(display.Bounds, display.WorkArea)
                || !IsValidExtensionData(
                    display.ExtensionData,
                    "stableId",
                    "bounds",
                    "workArea",
                    "effectiveDpi",
                    "rotation",
                    "isPrimary")
                || !IsValidRectExtensionData(display.Bounds)
                || !IsValidRectExtensionData(display.WorkArea))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidRect(PixelRectConfiguration? rect) =>
        rect is not null
        && Math.Abs((long)rect.Left) <= ProductConfigurationLimits.MaximumDisplayCoordinate
        && Math.Abs((long)rect.Top) <= ProductConfigurationLimits.MaximumDisplayCoordinate
        && rect.Width is > 0 and <= ProductConfigurationLimits.MaximumDisplayDimension
        && rect.Height is > 0 and <= ProductConfigurationLimits.MaximumDisplayDimension
        && (long)rect.Left + rect.Width <= ProductConfigurationLimits.MaximumDisplayCoordinate
        && (long)rect.Top + rect.Height <= ProductConfigurationLimits.MaximumDisplayCoordinate;

    private static bool Contains(
        PixelRectConfiguration bounds,
        PixelRectConfiguration workArea) =>
        workArea.Left >= bounds.Left
        && workArea.Top >= bounds.Top
        && (long)workArea.Left + workArea.Width <= (long)bounds.Left + bounds.Width
        && (long)workArea.Top + workArea.Height <= (long)bounds.Top + bounds.Height;

    private static bool IsValidRectExtensionData(PixelRectConfiguration rect) =>
        IsValidExtensionData(
            rect.ExtensionData,
            "left",
            "top",
            "width",
            "height");

    private static bool IsValidAppearance(ContainerAppearanceConfiguration? appearance) =>
        appearance is not null
        && IsRgbColor(appearance.Color)
        && double.IsFinite(appearance.Opacity)
        && appearance.Opacity is >= 0 and <= 1
        && Enum.IsDefined(appearance.TitleVisibility)
        && Enum.IsDefined(appearance.TitleDoubleClickAction);

    private static bool IsValidPlacement(ContainerPlacementConfiguration? placement) =>
        placement is not null
        && IsBoundedText(
            placement.DisplayKey,
            ProductConfigurationLimits.MaximumDisplayKeyLength)
        && IsBoundedCoordinate(placement.XDip)
        && IsBoundedCoordinate(placement.YDip)
        && double.IsFinite(placement.WidthDip)
        && placement.WidthDip is >= ProductConfigurationLimits.MinimumContainerWidthDip
            and <= ProductConfigurationLimits.MaximumContainerDimensionDip
        && double.IsFinite(placement.HeightDip)
        && placement.HeightDip is >= ProductConfigurationLimits.MinimumContainerHeightDip
            and <= ProductConfigurationLimits.MaximumContainerDimensionDip;

    private static bool IsValidItem(DesktopItemReferenceConfiguration? item) =>
        item is not null
        && IsBoundedText(item.Id, ProductConfigurationLimits.MaximumIdLength)
        && IsBoundedText(item.Target, ProductConfigurationLimits.MaximumTargetLength)
        && Enum.IsDefined(item.Kind)
        && item.Behavior == ConfigurationItemBehavior.Reference;

    private static bool IsValidFolderBinding(
        ContainerFolderBindingConfiguration? binding)
    {
        if (binding is null)
        {
            return true;
        }

        return IsBoundedText(
                binding.Target,
                ProductConfigurationLimits.MaximumTargetLength)
            && binding.VolumeSerialNumber.Length == 16
            && binding.VolumeSerialNumber.All(Uri.IsHexDigit)
            && binding.FileId.Length == 32
            && binding.FileId.All(Uri.IsHexDigit)
            && Enum.IsDefined(binding.SortMode)
            && IsValidExtensionData(
                binding.ExtensionData,
                "target",
                "volumeSerialNumber",
                "fileId",
                "sortMode");
    }

    private static bool IsBoundedCoordinate(double value) =>
        double.IsFinite(value)
        && Math.Abs(value) <= ProductConfigurationLimits.MaximumAbsoluteCoordinateDip;

    private static bool IsBoundedText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;

    private static bool IsRgbColor(string? value)
    {
        if (value is null || value.Length != 7 || value[0] != '#')
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            if (!Uri.IsHexDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidExtensionData(
        IDictionary<string, System.Text.Json.JsonElement>? extensionData,
        params string[] knownPropertyNames)
    {
        if (extensionData is null)
        {
            return true;
        }

        if (extensionData.Count > ProductConfigurationLimits.MaximumExtensionPropertiesPerObject)
        {
            return false;
        }

        foreach ((string name, System.Text.Json.JsonElement value) in extensionData)
        {
            if (!IsBoundedText(
                    name,
                    ProductConfigurationLimits.MaximumExtensionPropertyNameLength)
                || value.ValueKind == System.Text.Json.JsonValueKind.Undefined
                || knownPropertyNames.Contains(name, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
