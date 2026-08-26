using System.Text.Json;
using System.Text.Json.Serialization;

namespace LongGrid.Core.Configuration;

public sealed class ProductConfigurationContractException(
    ProductConfigurationError error)
    : Exception($"Product configuration rejected: {error}.")
{
    public ProductConfigurationError Error { get; } = error;
}

public static class ProductConfigurationJson
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    public static byte[] SerializeToUtf8Bytes(ProductConfigurationDocument document)
    {
        ProductConfigurationValidationResult validation =
            ProductConfigurationValidator.Validate(document);
        if (!validation.IsValid)
        {
            throw new ProductConfigurationContractException(validation.Error);
        }

        byte[] serialized;
        try
        {
            serialized = JsonSerializer.SerializeToUtf8Bytes(
                document,
                SerializerOptions);
        }
        catch (Exception exception)
            when (exception is JsonException
                or NotSupportedException
                or InvalidOperationException)
        {
            throw new ProductConfigurationContractException(
                ProductConfigurationError.MalformedJson);
        }
        if (serialized.Length > ProductConfigurationLimits.MaximumSerializedBytes)
        {
            throw new ProductConfigurationContractException(
                ProductConfigurationError.DocumentTooLarge);
        }

        return serialized;
    }

    public static ProductConfigurationDocument Deserialize(
        ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length > ProductConfigurationLimits.MaximumSerializedBytes)
        {
            throw new ProductConfigurationContractException(
                ProductConfigurationError.DocumentTooLarge);
        }

        ProductConfigurationDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ProductConfigurationDocument>(
                utf8Json,
                SerializerOptions);
        }
        catch (Exception exception)
            when (exception is JsonException or NotSupportedException)
        {
            throw new ProductConfigurationContractException(
                ProductConfigurationError.MalformedJson);
        }

        if (document is null)
        {
            throw new ProductConfigurationContractException(
                ProductConfigurationError.MalformedJson);
        }

        document = Migrate(document);

        ProductConfigurationValidationResult validation =
            ProductConfigurationValidator.Validate(document);
        if (!validation.IsValid)
        {
            throw new ProductConfigurationContractException(validation.Error);
        }

        return document;
    }

    private static ProductConfigurationDocument Migrate(
        ProductConfigurationDocument document)
    {
        if (document.SchemaVersion == ProductConfigurationLimits.CurrentSchemaVersion)
        {
            return document;
        }

        if (document.SchemaVersion == 1
            && document.SavedDisplayTopology is null
            && document.Containers.All(container => container.FolderBinding is null))
        {
            return document with
            {
                SchemaVersion = ProductConfigurationLimits.CurrentSchemaVersion,
            };
        }

        if (document.SchemaVersion == 2
            && document.Containers.All(container => container.FolderBinding is null))
        {
            return document with
            {
                SchemaVersion = ProductConfigurationLimits.CurrentSchemaVersion,
            };
        }

        throw new ProductConfigurationContractException(
            ProductConfigurationError.UnsupportedSchema);
    }

    private static JsonSerializerOptions CreateOptions() =>
        new()
        {
            AllowTrailingCommas = false,
            MaxDepth = 32,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter(
                    JsonNamingPolicy.CamelCase,
                    allowIntegerValues: false),
            },
        };
}
