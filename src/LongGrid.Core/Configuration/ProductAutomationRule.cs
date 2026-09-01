using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Configuration;

public enum ProductAutomationRuleMatchMode
{
    All,
    Any,
}

public enum ProductAutomationRuleConditionKind
{
    ItemKind,
    Extension,
    NameContains,
    NameStartsWith,
    NameEndsWith,
    NameEquals,
}

public enum ProductAutomationRuleActionKind
{
    AssignSafeReference,
}

public sealed record ProductAutomationRuleConfiguration
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("priority")]
    public int Priority { get; init; }

    [JsonPropertyName("targetContainerId")]
    public required string TargetContainerId { get; init; }

    [JsonPropertyName("matchMode")]
    public ProductAutomationRuleMatchMode MatchMode { get; init; }

    [JsonPropertyName("action")]
    public ProductAutomationRuleActionKind Action { get; init; } =
        ProductAutomationRuleActionKind.AssignSafeReference;

    [JsonPropertyName("conditions")]
    public required IReadOnlyList<ProductAutomationRuleConditionConfiguration>
        Conditions
    { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ProductAutomationRuleConditionConfiguration
{
    [JsonPropertyName("kind")]
    public ProductAutomationRuleConditionKind Kind { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ProductAutomationRuleState
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public bool Enabled { get; init; } = true;

    public int Priority { get; init; }

    public required string TargetContainerId { get; init; }

    public ProductAutomationRuleMatchMode MatchMode { get; init; }

    public ProductAutomationRuleActionKind Action { get; init; } =
        ProductAutomationRuleActionKind.AssignSafeReference;

    public required IReadOnlyList<ProductAutomationRuleConditionState>
        Conditions
    { get; init; }

    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ProductAutomationRuleConditionState
{
    public ProductAutomationRuleConditionKind Kind { get; init; }

    public required string Value { get; init; }

    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public enum ProductAutomationRulePreviewStatus
{
    Ready,
    ZeroMatches,
    CatalogUnavailable,
    InvalidState,
    InvalidRule,
    TargetMissing,
    TargetLocked,
    Conflict,
    CapacityExceeded,
}

public sealed record ProductAutomationRulePreviewItem(
    int CatalogIndex,
    string DisplayName,
    DesktopItemKind Kind,
    string CanonicalTarget);

public sealed record ProductAutomationRulePreviewSnapshot(
    ProductAutomationRulePreviewStatus Status,
    Guid PreviewId,
    long CatalogGeneration,
    long WorkspaceRevision,
    string WorkspaceFingerprint,
    string CatalogFingerprint,
    string RuleFingerprint,
    ProductAutomationRuleState Rule,
    IReadOnlyList<ProductAutomationRulePreviewItem> Matches,
    IReadOnlyList<ProductAutomationRulePreviewItem> Samples,
    int TotalMatchCount,
    int ConflictCount)
{
    public bool CanApply =>
        Status == ProductAutomationRulePreviewStatus.Ready
        && PreviewId != Guid.Empty
        && WorkspaceFingerprint.Length == 64
        && CatalogFingerprint.Length == 64
        && RuleFingerprint.Length == 64
        && Rule.Enabled
        && Matches.Count > 0
        && ConflictCount == 0;
}

public static class ProductAutomationRulePreviewPlanner
{
    public const int MaximumMatchCount = 256;
    public const int MaximumSampleCount = 20;

    public static ProductAutomationRulePreviewSnapshot Create(
        ProductWorkspaceState state,
        long workspaceRevision,
        long catalogGeneration,
        bool catalogAuthoritative,
        IReadOnlyList<DesktopCatalogEntry> catalog,
        ProductAutomationRuleState rule)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rule);

        if (workspaceRevision <= 0
            || !TryWorkspaceFingerprint(state, out string workspaceFingerprint))
        {
            return Unavailable(ProductAutomationRulePreviewStatus.InvalidState);
        }

        string ruleFingerprint = ComputeRuleFingerprint(rule);
        if (!ProductAutomationRulePolicy.IsValid(rule))
        {
            return Unavailable(
                ProductAutomationRulePreviewStatus.InvalidRule,
                workspaceFingerprint,
                ruleFingerprint);
        }

        ProductContainerState? target = state.Containers.FirstOrDefault(container =>
            string.Equals(container.Id, rule.TargetContainerId, StringComparison.Ordinal));
        if (target is null)
        {
            return Unavailable(
                ProductAutomationRulePreviewStatus.TargetMissing,
                workspaceFingerprint,
                ruleFingerprint);
        }
        if (target.IsLocked)
        {
            return Unavailable(
                ProductAutomationRulePreviewStatus.TargetLocked,
                workspaceFingerprint,
                ruleFingerprint);
        }
        if (!catalogAuthoritative || catalogGeneration <= 0)
        {
            return Unavailable(
                ProductAutomationRulePreviewStatus.CatalogUnavailable,
                workspaceFingerprint,
                ruleFingerprint);
        }

        HashSet<string> referenced = state.Containers
            .SelectMany(container => container.Items)
            .Select(item => item.PersistedTarget)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<ProductAutomationRulePreviewItem>();
        int conflicts = 0;
        for (int index = 0; index < catalog.Count; index++)
        {
            DesktopCatalogEntry? entry = catalog[index];
            if (!IsValid(entry)
                || referenced.Contains(entry.Identity.CanonicalTarget)
                || !unique.Add(entry.Identity.CanonicalTarget)
                || !Matches(entry, rule))
            {
                continue;
            }

            if (state.Rules.Any(existing =>
                existing.Enabled
                && !string.Equals(existing.Id, rule.Id, StringComparison.Ordinal)
                && !string.Equals(
                    existing.TargetContainerId,
                    rule.TargetContainerId,
                    StringComparison.Ordinal)
                && ProductAutomationRulePolicy.IsValid(existing)
                && Matches(entry, existing)))
            {
                conflicts++;
            }

            matches.Add(new(
                index,
                entry.DisplayName,
                entry.Kind,
                entry.Identity.CanonicalTarget));
        }

        int existingItemCount = state.Containers.Sum(container => container.Items.Count);
        ProductAutomationRulePreviewStatus status = matches.Count switch
        {
            0 => ProductAutomationRulePreviewStatus.ZeroMatches,
            > MaximumMatchCount => ProductAutomationRulePreviewStatus.CapacityExceeded,
            _ when existingItemCount + matches.Count
                > ProductConfigurationLimits.MaximumItems =>
                ProductAutomationRulePreviewStatus.CapacityExceeded,
            _ when conflicts > 0 => ProductAutomationRulePreviewStatus.Conflict,
            _ => ProductAutomationRulePreviewStatus.Ready,
        };
        DesktopCatalogEntry[] matchedEntries = matches
            .Take(MaximumMatchCount)
            .Select(match => catalog[match.CatalogIndex])
            .ToArray();
        IReadOnlyList<ProductAutomationRulePreviewItem> boundedMatches =
            matches.Take(MaximumMatchCount).ToArray();
        return new(
            status,
            status == ProductAutomationRulePreviewStatus.Ready
                ? Guid.NewGuid()
                : Guid.Empty,
            catalogGeneration,
            workspaceRevision,
            workspaceFingerprint,
            ProductQuickStartSuggestionPlanner.ComputeCatalogFingerprint(matchedEntries),
            ruleFingerprint,
            rule,
            boundedMatches,
            boundedMatches.Take(MaximumSampleCount).ToArray(),
            matches.Count,
            conflicts);

        ProductAutomationRulePreviewSnapshot Unavailable(
            ProductAutomationRulePreviewStatus unavailableStatus,
            string fingerprint = "",
            string draftFingerprint = "") => new(
                unavailableStatus,
                Guid.Empty,
                catalogGeneration,
                workspaceRevision,
                fingerprint,
                string.Empty,
                draftFingerprint,
                rule,
                [],
                [],
                0,
                0);
    }

    public static bool Matches(
        DesktopCatalogEntry entry,
        ProductAutomationRuleState rule)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(rule);
        IEnumerable<bool> results = rule.Conditions.Select(condition =>
            MatchesCondition(entry, condition));
        return rule.MatchMode == ProductAutomationRuleMatchMode.All
            ? results.All(result => result)
            : results.Any(result => result);
    }

    public static string ComputeRuleFingerprint(ProductAutomationRuleState rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(rule);
        return Convert.ToHexString(SHA256.HashData(json));
    }

    private static bool MatchesCondition(
        DesktopCatalogEntry entry,
        ProductAutomationRuleConditionState condition) => condition.Kind switch
        {
            ProductAutomationRuleConditionKind.ItemKind => string.Equals(
                entry.Kind.ToString(), condition.Value, StringComparison.OrdinalIgnoreCase),
            ProductAutomationRuleConditionKind.Extension => string.Equals(
                Path.GetExtension(entry.DisplayName),
                NormalizeExtension(condition.Value),
                StringComparison.OrdinalIgnoreCase),
            ProductAutomationRuleConditionKind.NameContains => entry.DisplayName.Contains(
                condition.Value, StringComparison.OrdinalIgnoreCase),
            ProductAutomationRuleConditionKind.NameStartsWith => entry.DisplayName.StartsWith(
                condition.Value, StringComparison.OrdinalIgnoreCase),
            ProductAutomationRuleConditionKind.NameEndsWith => entry.DisplayName.EndsWith(
                condition.Value, StringComparison.OrdinalIgnoreCase),
            ProductAutomationRuleConditionKind.NameEquals => string.Equals(
                entry.DisplayName, condition.Value, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    private static string NormalizeExtension(string value) =>
        value.StartsWith('.') ? value : $".{value}";

    private static bool IsValid(DesktopCatalogEntry? entry) =>
        entry is not null
        && entry.Identity is not null
        && !string.IsNullOrWhiteSpace(entry.Identity.CanonicalTarget)
        && !string.IsNullOrWhiteSpace(entry.DisplayName)
        && Enum.IsDefined(entry.Kind);

    private static bool TryWorkspaceFingerprint(
        ProductWorkspaceState state,
        out string fingerprint)
    {
        ProductWorkspaceProjectionResult projection =
            ProductWorkspaceConfigurationProjector.Project(state);
        if (!projection.IsSuccess)
        {
            fingerprint = string.Empty;
            return false;
        }
        fingerprint = ProductWorkspaceConfigurationFingerprint.Compute(
            projection.Document!);
        return true;
    }
}

public static class ProductAutomationRulePolicy
{
    public static bool IsValid(ProductAutomationRuleState? rule) =>
        rule is not null
        && IsText(rule.Id, ProductConfigurationLimits.MaximumIdLength)
        && IsText(rule.Name, ProductConfigurationLimits.MaximumNameLength)
        && IsText(rule.TargetContainerId, ProductConfigurationLimits.MaximumIdLength)
        && rule.Priority is >= 0 and <= ProductConfigurationLimits.MaximumRulePriority
        && Enum.IsDefined(rule.MatchMode)
        && rule.Action == ProductAutomationRuleActionKind.AssignSafeReference
        && rule.Conditions is { Count: > 0 and <= ProductConfigurationLimits.MaximumConditionsPerRule }
        && rule.Conditions.All(IsValidCondition);

    public static bool IsValid(ProductAutomationRuleConfiguration? rule) =>
        rule is not null
        && IsValid(new ProductAutomationRuleState
        {
            Id = rule.Id,
            Name = rule.Name,
            Enabled = rule.Enabled,
            Priority = rule.Priority,
            TargetContainerId = rule.TargetContainerId,
            MatchMode = rule.MatchMode,
            Action = rule.Action,
            Conditions = rule.Conditions?.Select(condition =>
                new ProductAutomationRuleConditionState
                {
                    Kind = condition.Kind,
                    Value = condition.Value,
                    ExtensionData = condition.ExtensionData,
                }).ToArray() ?? [],
            ExtensionData = rule.ExtensionData,
        });

    private static bool IsValidCondition(ProductAutomationRuleConditionState? condition)
    {
        if (condition is null
            || !Enum.IsDefined(condition.Kind)
            || !IsText(condition.Value, ProductConfigurationLimits.MaximumRuleValueLength))
        {
            return false;
        }
        return condition.Kind != ProductAutomationRuleConditionKind.ItemKind
            || Enum.TryParse<DesktopItemKind>(condition.Value, true, out _);
    }

    private static bool IsText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;
}
