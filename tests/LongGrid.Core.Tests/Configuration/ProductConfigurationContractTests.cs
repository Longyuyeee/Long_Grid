using System.Text;
using System.Text.Json;
using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductConfigurationContractTests
{
    [Fact]
    public void EmptyProductDefaultIsValidAndContainsNoUserState()
    {
        ProductConfigurationDocument document = ProductConfigurationDefaults.CreateEmpty();

        ProductConfigurationValidationResult result =
            ProductConfigurationValidator.Validate(document);

        Assert.True(result.IsValid);
        Assert.Equal(ProductConfigurationLimits.CurrentSchemaVersion, document.SchemaVersion);
        Assert.Equal("default", document.ProfileId);
        Assert.Empty(document.Containers);
        Assert.Null(document.ExtensionData);
    }

    [Fact]
    public void ValidReferenceConfigurationRoundTripsWithCamelCaseEnums()
    {
        ProductConfigurationDocument source = CreateValidDocument();

        byte[] json = ProductConfigurationJson.SerializeToUtf8Bytes(source);
        string text = Encoding.UTF8.GetString(json);
        ProductConfigurationDocument restored =
            ProductConfigurationJson.Deserialize(json);

        Assert.Contains("\"kind\": \"folder\"", text, StringComparison.Ordinal);
        Assert.Contains("\"behavior\": \"reference\"", text, StringComparison.Ordinal);
        Assert.Equivalent(source, restored, strict: true);
    }

    [Fact]
    public void UnknownFieldsSurviveRoundTripAtEverySupportedLevel()
    {
        byte[] source = Encoding.UTF8.GetBytes(
            """
            {
              "schemaVersion": 1,
              "profileId": "default",
              "rootFuture": { "enabled": true },
              "containers": [{
                "id": "container-1",
                "name": "Current project",
                "isLocked": false,
                "containerFuture": 7,
                "appearance": {
                  "color": "#334155",
                  "opacity": 0.72,
                  "collapsed": false,
                  "appearanceFuture": "kept"
                },
                "placement": {
                  "displayKey": "display-a",
                  "xDip": 32,
                  "yDip": 48,
                  "widthDip": 420,
                  "heightDip": 300,
                  "placementFuture": [1, 2]
                },
                "items": [{
                  "id": "item-1",
                  "kind": "folder",
                  "target": "%USERPROFILE%\\Documents\\Project",
                  "behavior": "reference",
                  "itemFuture": { "state": "kept" }
                }]
              }]
            }
            """);

        ProductConfigurationDocument document =
            ProductConfigurationJson.Deserialize(source);
        byte[] serialized = ProductConfigurationJson.SerializeToUtf8Bytes(document);
        using JsonDocument roundTrip = JsonDocument.Parse(serialized);

        JsonElement root = roundTrip.RootElement;
        Assert.Equal(
            ProductConfigurationLimits.CurrentSchemaVersion,
            root.GetProperty("schemaVersion").GetInt32());
        Assert.Null(document.SavedDisplayTopology);
        JsonElement container = root.GetProperty("containers")[0];
        Assert.True(root.GetProperty("rootFuture").GetProperty("enabled").GetBoolean());
        Assert.Equal(7, container.GetProperty("containerFuture").GetInt32());
        Assert.Equal(
            "kept",
            container.GetProperty("appearance").GetProperty("appearanceFuture").GetString());
        Assert.Equal(
            2,
            container.GetProperty("placement").GetProperty("placementFuture").GetArrayLength());
        Assert.Equal(
            "kept",
            container.GetProperty("items")[0]
                .GetProperty("itemFuture")
                .GetProperty("state")
                .GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void UnsupportedSchemaIsRejectedWithoutDocumentContent(int schemaVersion)
    {
        ProductConfigurationDocument source = CreateValidDocument() with
        {
            SchemaVersion = schemaVersion,
        };

        ProductConfigurationContractException exception = Assert.Throws<
            ProductConfigurationContractException>(
            () => ProductConfigurationJson.SerializeToUtf8Bytes(source));

        Assert.Equal(ProductConfigurationError.UnsupportedSchema, exception.Error);
        Assert.DoesNotContain("container-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SavedDisplayTopologyRoundTripsInCurrentSchema()
    {
        ProductConfigurationDocument source = CreateValidDocument() with
        {
            SavedDisplayTopology =
            [
                new SavedDisplayConfiguration
                {
                    StableId = "display-a",
                    Bounds = new() { Left = -1920, Top = 0, Width = 1920, Height = 1080 },
                    WorkArea = new() { Left = -1920, Top = 0, Width = 1920, Height = 1040 },
                    EffectiveDpi = 144,
                    Rotation = DisplayRotation.Landscape,
                    IsPrimary = true,
                },
            ],
        };

        ProductConfigurationDocument restored = ProductConfigurationJson.Deserialize(
            ProductConfigurationJson.SerializeToUtf8Bytes(source));

        Assert.Equivalent(source, restored, strict: true);
    }

    [Fact]
    public void VersionOneDocumentMigratesWithoutInventingSavedTopology()
    {
        byte[] source = Encoding.UTF8.GetBytes(
            """
            { "schemaVersion": 1, "profileId": "default", "containers": [] }
            """);

        ProductConfigurationDocument migrated = ProductConfigurationJson.Deserialize(source);

        Assert.Equal(ProductConfigurationLimits.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Null(migrated.SavedDisplayTopology);
    }

    [Fact]
    public void VersionOneCannotSmuggleVersionTwoTopology()
    {
        byte[] source = Encoding.UTF8.GetBytes(
            """
            {
              "schemaVersion": 1,
              "profileId": "default",
              "containers": [],
              "savedDisplayTopology": []
            }
            """);

        ProductConfigurationContractException exception = Assert.Throws<
            ProductConfigurationContractException>(
            () => ProductConfigurationJson.Deserialize(source));

        Assert.Equal(ProductConfigurationError.UnsupportedSchema, exception.Error);
    }

    [Fact]
    public void VersionTwoMigratesWithoutInventingFolderBindings()
    {
        byte[] source = Encoding.UTF8.GetBytes(
            """
            { "schemaVersion": 2, "profileId": "default", "containers": [] }
            """);

        ProductConfigurationDocument migrated = ProductConfigurationJson.Deserialize(source);

        Assert.Equal(ProductConfigurationLimits.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Empty(migrated.Containers);
    }

    [Fact]
    public void VersionTwoCannotSmuggleVersionThreeFolderBinding()
    {
        byte[] source = Encoding.UTF8.GetBytes(
            """
            {
              "schemaVersion": 2,
              "profileId": "default",
              "containers": [{
                "id": "container-1",
                "name": "Work",
                "isLocked": false,
                "appearance": {
                  "color": "#2563EB",
                  "opacity": 0.88,
                  "collapsed": false
                },
                "placement": {
                  "displayKey": "display-a",
                  "xDip": 0,
                  "yDip": 0,
                  "widthDip": 360,
                  "heightDip": 240
                },
                "items": [],
                "folderBinding": {
                  "target": "C:\\Work",
                  "volumeSerialNumber": "0000000000000001",
                  "fileId": "00000000000000000000000000000001"
                }
              }]
            }
            """);

        ProductConfigurationContractException exception = Assert.Throws<
            ProductConfigurationContractException>(
            () => ProductConfigurationJson.Deserialize(source));

        Assert.Equal(ProductConfigurationError.UnsupportedSchema, exception.Error);
    }

    [Fact]
    public void VersionThreeFolderBindingMigratesToStableDefaultSort()
    {
        byte[] source = Encoding.UTF8.GetBytes(
            """
            {
              "schemaVersion": 3,
              "profileId": "default",
              "containers": [{
                "id": "container-1",
                "name": "Work",
                "isLocked": false,
                "appearance": {
                  "color": "#2563EB",
                  "opacity": 0.88,
                  "collapsed": false
                },
                "placement": {
                  "displayKey": "display-a",
                  "xDip": 0,
                  "yDip": 0,
                  "widthDip": 360,
                  "heightDip": 240
                },
                "items": [],
                "folderBinding": {
                  "target": "C:\\Work",
                  "volumeSerialNumber": "0000000000000001",
                  "fileId": "00000000000000000000000000000001"
                }
              }]
            }
            """);

        ProductConfigurationDocument migrated =
            ProductConfigurationJson.Deserialize(source);

        Assert.Equal(ProductConfigurationLimits.CurrentSchemaVersion,
            migrated.SchemaVersion);
        Assert.Equal(
            ProductContainerFolderSortMode.FoldersFirstNameAscending,
            migrated.Containers[0].FolderBinding!.SortMode);
    }

    [Fact]
    public void FutureSchemaIsRejectedDuringDeserialize()
    {
        byte[] source = Encoding.UTF8.GetBytes(
            """
            { "schemaVersion": 5, "profileId": "default", "containers": [] }
            """);

        ProductConfigurationContractException exception = Assert.Throws<
            ProductConfigurationContractException>(
            () => ProductConfigurationJson.Deserialize(source));

        Assert.Equal(ProductConfigurationError.UnsupportedSchema, exception.Error);
    }

    [Fact]
    public void InvalidSavedDisplaySetShapesAreRejected()
    {
        SavedDisplayConfiguration valid = new()
        {
            StableId = "display-a",
            Bounds = new() { Width = 1920, Height = 1080 },
            WorkArea = new() { Width = 1920, Height = 1040 },
            EffectiveDpi = 96,
            Rotation = DisplayRotation.Landscape,
            IsPrimary = true,
        };
        IReadOnlyList<IReadOnlyList<SavedDisplayConfiguration>> invalidSets =
        [
            Array.Empty<SavedDisplayConfiguration>(),
            [valid with { IsPrimary = false }],
            [valid, valid with { IsPrimary = false }],
            [valid with { StableId = " " }],
            [valid with { Rotation = DisplayRotation.Unknown }],
            [valid with { Rotation = (DisplayRotation)99 }],
            [valid with { EffectiveDpi = 769 }],
            [valid with { Bounds = valid.Bounds with { Left = 1_000_001 } }],
            [valid with { Bounds = valid.Bounds with { Width = 100_001 } }],
            [valid with
            {
                ExtensionData = new Dictionary<string, JsonElement>
                {
                    ["stableId"] = JsonSerializer.SerializeToElement("shadow"),
                },
            }],
            [valid with
            {
                Bounds = valid.Bounds with
                {
                    ExtensionData = new Dictionary<string, JsonElement>
                    {
                        ["left"] = JsonSerializer.SerializeToElement(1),
                    },
                },
            }],
            Enumerable.Repeat(valid, ProductConfigurationLimits.MaximumSavedDisplays + 1)
                .ToArray(),
        ];

        foreach (IReadOnlyList<SavedDisplayConfiguration> displays in invalidSets)
        {
            ProductConfigurationValidationResult result =
                ProductConfigurationValidator.Validate(
                    CreateValidDocument() with { SavedDisplayTopology = displays });

            Assert.Equal(ProductConfigurationError.InvalidDisplayTopology, result.Error);
        }

        ProductConfigurationValidationResult nullEntry =
            ProductConfigurationValidator.Validate(
                CreateValidDocument() with
                {
                    SavedDisplayTopology = new SavedDisplayConfiguration[] { null! },
                });
        Assert.Equal(
            ProductConfigurationError.InvalidDisplayTopology,
            nullEntry.Error);
    }

    [Theory]
    [InlineData(0, 0, 1920, 1080, 0, 0, 1920, 1040, 96, true)]
    [InlineData(0, 0, 0, 1080, 0, 0, 1920, 1040, 96, false)]
    [InlineData(0, 0, 1920, 1080, -1, 0, 1920, 1040, 96, false)]
    [InlineData(0, 0, 1920, 1080, 0, 0, 1920, 1040, 47, false)]
    public void SavedDisplayTopologyValidationIsFinite(
        int left,
        int top,
        int width,
        int height,
        int workLeft,
        int workTop,
        int workWidth,
        int workHeight,
        uint dpi,
        bool expectedValid)
    {
        ProductConfigurationDocument source = CreateValidDocument() with
        {
            SavedDisplayTopology =
            [
                new SavedDisplayConfiguration
                {
                    StableId = "display-a",
                    Bounds = new() { Left = left, Top = top, Width = width, Height = height },
                    WorkArea = new()
                    {
                        Left = workLeft,
                        Top = workTop,
                        Width = workWidth,
                        Height = workHeight,
                    },
                    EffectiveDpi = dpi,
                    Rotation = DisplayRotation.Landscape,
                    IsPrimary = true,
                },
            ],
        };

        ProductConfigurationValidationResult result =
            ProductConfigurationValidator.Validate(source);

        Assert.Equal(expectedValid, result.IsValid);
        if (!expectedValid)
        {
            Assert.Equal(ProductConfigurationError.InvalidDisplayTopology, result.Error);
        }
    }

    [Fact]
    public void DuplicateIdsAcrossContainersAndItemsAreRejected()
    {
        ProductConfigurationDocument source = CreateValidDocument();
        DesktopItemReferenceConfiguration duplicate = source.Containers[0].Items[0] with
        {
            Id = source.Containers[0].Id,
        };
        source = source with
        {
            Containers =
            [
                source.Containers[0] with
                {
                    Items = [duplicate],
                },
            ],
        };

        ProductConfigurationValidationResult result =
            ProductConfigurationValidator.Validate(source);

        Assert.Equal(ProductConfigurationError.DuplicateObjectId, result.Error);
    }

    [Theory]
    [InlineData("334155", 0.5)]
    [InlineData("#GG4155", 0.5)]
    [InlineData("#334155", -0.1)]
    [InlineData("#334155", 1.1)]
    public void InvalidAppearanceIsRejected(string color, double opacity)
    {
        ProductConfigurationDocument source = CreateValidDocument();
        source = source with
        {
            Containers =
            [
                source.Containers[0] with
                {
                    Appearance = source.Containers[0].Appearance with
                    {
                        Color = color,
                        Opacity = opacity,
                    },
                },
            ],
        };

        ProductConfigurationValidationResult result =
            ProductConfigurationValidator.Validate(source);

        Assert.Equal(ProductConfigurationError.InvalidAppearance, result.Error);
    }

    [Theory]
    [InlineData(double.NaN, 300)]
    [InlineData(32, 47)]
    [InlineData(32, 16385)]
    public void InvalidPlacementIsRejected(double xDip, double heightDip)
    {
        ProductConfigurationDocument source = CreateValidDocument();
        source = source with
        {
            Containers =
            [
                source.Containers[0] with
                {
                    Placement = source.Containers[0].Placement with
                    {
                        XDip = xDip,
                        HeightDip = heightDip,
                    },
                },
            ],
        };

        ProductConfigurationValidationResult result =
            ProductConfigurationValidator.Validate(source);

        Assert.Equal(ProductConfigurationError.InvalidPlacement, result.Error);
    }

    [Fact]
    public void MoreThanFiveHundredItemsIsRejected()
    {
        ProductConfigurationDocument source = CreateValidDocument();
        DesktopItemReferenceConfiguration template = source.Containers[0].Items[0];
        DesktopItemReferenceConfiguration[] items = Enumerable.Range(
                0,
                ProductConfigurationLimits.MaximumItems + 1)
            .Select(index => template with { Id = $"item-{index}" })
            .ToArray();
        source = source with
        {
            Containers = [source.Containers[0] with { Items = items }],
        };

        ProductConfigurationValidationResult result =
            ProductConfigurationValidator.Validate(source);

        Assert.Equal(ProductConfigurationError.TooManyItems, result.Error);
    }

    [Fact]
    public void OversizedJsonIsRejectedBeforeParsing()
    {
        byte[] oversized = new byte[ProductConfigurationLimits.MaximumSerializedBytes + 1];

        ProductConfigurationContractException exception = Assert.Throws<
            ProductConfigurationContractException>(
            () => ProductConfigurationJson.Deserialize(oversized));

        Assert.Equal(ProductConfigurationError.DocumentTooLarge, exception.Error);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":1}")]
    public void MalformedOrIncompleteJsonHasFiniteFailure(string json)
    {
        ProductConfigurationContractException exception = Assert.Throws<
            ProductConfigurationContractException>(
            () => ProductConfigurationJson.Deserialize(Encoding.UTF8.GetBytes(json)));

        Assert.Contains(
            exception.Error,
            new[]
            {
                ProductConfigurationError.MalformedJson,
                ProductConfigurationError.InvalidProfile,
            });
    }

    [Fact]
    public void NumericEnumsAreRejected()
    {
        byte[] json = ProductConfigurationJson.SerializeToUtf8Bytes(CreateValidDocument());
        string text = Encoding.UTF8.GetString(json)
            .Replace("\"folder\"", "1", StringComparison.Ordinal);

        ProductConfigurationContractException exception = Assert.Throws<
            ProductConfigurationContractException>(
            () => ProductConfigurationJson.Deserialize(Encoding.UTF8.GetBytes(text)));

        Assert.Equal(ProductConfigurationError.MalformedJson, exception.Error);
    }

    [Fact]
    public void NonReferenceBehaviorCannotEnterTheCurrentSchema()
    {
        ProductConfigurationDocument source = CreateValidDocument();
        source = source with
        {
            Containers =
            [
                source.Containers[0] with
                {
                    Items =
                    [
                        source.Containers[0].Items[0] with
                        {
                            Behavior = (ConfigurationItemBehavior)42,
                        },
                    ],
                },
            ],
        };

        ProductConfigurationValidationResult result =
            ProductConfigurationValidator.Validate(source);

        Assert.Equal(ProductConfigurationError.InvalidItem, result.Error);
    }

    [Fact]
    public void ExtensionDataCannotShadowKnownProperties()
    {
        ProductConfigurationDocument source = CreateValidDocument() with
        {
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["schemaVersion"] = JsonDocument.Parse("2").RootElement.Clone(),
            },
        };

        ProductConfigurationContractException exception = Assert.Throws<
            ProductConfigurationContractException>(
            () => ProductConfigurationJson.SerializeToUtf8Bytes(source));

        Assert.Equal(ProductConfigurationError.InvalidExtensionData, exception.Error);
    }

    [Fact]
    public void UndefinedExtensionDataHasFiniteFailure()
    {
        ProductConfigurationDocument source = CreateValidDocument();
        source = source with
        {
            Containers =
            [
                source.Containers[0] with
                {
                    ExtensionData = new Dictionary<string, JsonElement>
                    {
                        ["future"] = default,
                    },
                },
            ],
        };

        ProductConfigurationValidationResult result =
            ProductConfigurationValidator.Validate(source);

        Assert.Equal(ProductConfigurationError.InvalidExtensionData, result.Error);
    }

    private static ProductConfigurationDocument CreateValidDocument() =>
        new()
        {
            SchemaVersion = ProductConfigurationLimits.CurrentSchemaVersion,
            ProfileId = "default",
            Containers =
            [
                new ContainerConfiguration
                {
                    Id = "container-1",
                    Name = "Current project",
                    IsLocked = false,
                    Appearance = new ContainerAppearanceConfiguration
                    {
                        Color = "#334155",
                        Opacity = 0.72,
                        Collapsed = false,
                    },
                    Placement = new ContainerPlacementConfiguration
                    {
                        DisplayKey = "display-a",
                        XDip = 32,
                        YDip = 48,
                        WidthDip = 420,
                        HeightDip = 300,
                    },
                    Items =
                    [
                        new DesktopItemReferenceConfiguration
                        {
                            Id = "item-1",
                            Kind = ConfigurationItemKind.Folder,
                            Target = "%USERPROFILE%\\Documents\\Project",
                            Behavior = ConfigurationItemBehavior.Reference,
                        },
                    ],
                },
            ],
        };
}
