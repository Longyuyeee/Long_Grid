namespace LongGrid.Core.Configuration;

public enum ProductAutomationRuleLifecycleAction
{
    Update,
    Duplicate,
    SetEnabled,
    Remove,
    MoveEarlier,
    MoveLater,
}

public sealed record ProductAutomationRuleLifecycleRequest(
    ProductAutomationRuleLifecycleAction Action,
    string RuleId,
    ProductAutomationRuleState? Rule = null,
    bool? Enabled = null);
