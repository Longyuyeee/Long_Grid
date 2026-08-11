using LongGrid.Core.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceContainerNameIntentPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public void InvalidNamesFailClosed(string? name)
    {
        ProductWorkspaceContainerNameIntentDecision decision = Evaluate(name);

        Assert.Equal(ProductWorkspaceContainerNameIntentStatus.Invalid, decision.Status);
        Assert.False(decision.CanCreate);
        Assert.False(decision.CanRename);
    }

    [Fact]
    public void ReadOnlyEditorIsUnavailable()
    {
        ProductWorkspaceContainerNameIntentDecision decision = Evaluate(
            "工作",
            canCreate: false,
            canRename: false);

        Assert.Equal(
            ProductWorkspaceContainerNameIntentStatus.Unavailable,
            decision.Status);
        Assert.False(decision.CanCreate);
        Assert.False(decision.CanRename);
    }

    [Fact]
    public void WhitespaceNameDisablesBothActions()
    {
        ProductWorkspaceContainerNameIntentDecision decision = Evaluate("  ");

        Assert.Equal(ProductWorkspaceContainerNameIntentStatus.Empty, decision.Status);
        Assert.False(decision.CanCreate);
        Assert.False(decision.CanRename);
    }

    [Fact]
    public void ValidNameWithoutSelectionAllowsCreateOnly()
    {
        ProductWorkspaceContainerNameIntentDecision decision = Evaluate("工作");

        Assert.Equal(
            ProductWorkspaceContainerNameIntentStatus.CreateReady,
            decision.Status);
        Assert.True(decision.CanCreate);
        Assert.False(decision.CanRename);
    }

    [Fact]
    public void ChangedUnlockedSelectionAllowsBothActions()
    {
        ProductWorkspaceContainerNameIntentDecision decision = Evaluate(
            "项目",
            selectedName: "工作");

        Assert.Equal(
            ProductWorkspaceContainerNameIntentStatus.RenameReady,
            decision.Status);
        Assert.True(decision.CanCreate);
        Assert.True(decision.CanRename);
    }

    [Fact]
    public void TrimmedUnchangedNameDisablesRename()
    {
        ProductWorkspaceContainerNameIntentDecision decision = Evaluate(
            " 工作 ",
            selectedName: "工作");

        Assert.Equal(
            ProductWorkspaceContainerNameIntentStatus.RenameNoChange,
            decision.Status);
        Assert.True(decision.CanCreate);
        Assert.False(decision.CanRename);
    }

    [Fact]
    public void LockedSelectionDisablesRename()
    {
        ProductWorkspaceContainerNameIntentDecision decision = Evaluate(
            "项目",
            selectedName: "工作",
            selectedIsLocked: true);

        Assert.Equal(
            ProductWorkspaceContainerNameIntentStatus.RenameLocked,
            decision.Status);
        Assert.True(decision.CanCreate);
        Assert.False(decision.CanRename);
    }

    [Fact]
    public void EditorWithoutRenameCapabilityDisablesRename()
    {
        ProductWorkspaceContainerNameIntentDecision decision = Evaluate(
            "项目",
            canRename: false,
            selectedName: "工作");

        Assert.Equal(
            ProductWorkspaceContainerNameIntentStatus.RenameUnavailable,
            decision.Status);
        Assert.True(decision.CanCreate);
        Assert.False(decision.CanRename);
    }

    private static ProductWorkspaceContainerNameIntentDecision Evaluate(
        string? name,
        bool canCreate = true,
        bool canRename = true,
        string? selectedName = null,
        bool selectedIsLocked = false) =>
        ProductWorkspaceContainerNameIntentPolicy.Evaluate(
            name,
            canCreate,
            canRename,
            selectedName,
            selectedIsLocked);
}
