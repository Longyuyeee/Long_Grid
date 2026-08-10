using LongGrid.Core.Configuration;
using LongGrid.Infrastructure.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceLatestUndoSelectorTests
{
    [Fact]
    public void SelectReturnsUnavailableWhenNoUndoTokenExists()
    {
        ProductWorkspaceLatestUndoSelection selection =
            ProductWorkspaceLatestUndoSelector.Select(null, null, null, null, null);

        Assert.Equal(ProductWorkspaceLatestUndoKind.Unavailable, selection.Kind);
        Assert.False(selection.CanUndo);
        Assert.Equal(0, selection.EditRevision);
    }

    [Fact]
    public void SelectMapsEverySupportedUndoToken()
    {
        Guid operationId = Guid.NewGuid();

        AssertSelection(
            ProductWorkspaceLatestUndoKind.LayoutRecovery,
            1,
            ProductWorkspaceLatestUndoSelector.Select(
                new(operationId, 1, Fingerprint('a'), Fingerprint('b'), 1),
                null, null, null, null));
        AssertSelection(
            ProductWorkspaceLatestUndoKind.ContainerRemoval,
            2,
            ProductWorkspaceLatestUndoSelector.Select(
                null,
                new(operationId, 2, Fingerprint('a'), Fingerprint('b')),
                null, null, null));
        AssertSelection(
            ProductWorkspaceLatestUndoKind.ReferenceBatchAddition,
            3,
            ProductWorkspaceLatestUndoSelector.Select(
                null, null,
                new(operationId, 3, Fingerprint('a'), Fingerprint('b')),
                null, null));
        AssertSelection(
            ProductWorkspaceLatestUndoKind.ReferenceRemoval,
            4,
            ProductWorkspaceLatestUndoSelector.Select(
                null, null, null,
                new(operationId, 4, Fingerprint('a'), Fingerprint('b')),
                null));
        AssertSelection(
            ProductWorkspaceLatestUndoKind.ReferenceReassignment,
            5,
            ProductWorkspaceLatestUndoSelector.Select(
                null, null, null, null,
                new(operationId, 5, Fingerprint('a'), Fingerprint('b'))));
    }

    [Fact]
    public void SelectFailsClosedForMultipleOrMalformedTokens()
    {
        Guid operationId = Guid.NewGuid();
        var removal = new ProductWorkspaceReferenceRemovalUndoToken(
            operationId, 3, Fingerprint('a'), Fingerprint('b'));
        var reassignment = new ProductWorkspaceReferenceReassignmentUndoToken(
            operationId, 4, Fingerprint('c'), Fingerprint('d'));

        ProductWorkspaceLatestUndoSelection conflict =
            ProductWorkspaceLatestUndoSelector.Select(
                null, null, null, removal, reassignment);
        ProductWorkspaceLatestUndoSelection invalidRevision =
            ProductWorkspaceLatestUndoSelector.Select(
                null,
                new(operationId, 0, Fingerprint('a'), Fingerprint('b')),
                null, null, null);
        ProductWorkspaceLatestUndoSelection emptyOperation =
            ProductWorkspaceLatestUndoSelector.Select(
                null,
                new(Guid.Empty, 2, Fingerprint('a'), Fingerprint('b')),
                null, null, null);

        Assert.Equal(ProductWorkspaceLatestUndoKind.Conflict, conflict.Kind);
        Assert.False(conflict.CanUndo);
        Assert.Equal(ProductWorkspaceLatestUndoKind.Conflict, invalidRevision.Kind);
        Assert.False(invalidRevision.CanUndo);
        Assert.Equal(ProductWorkspaceLatestUndoKind.Conflict, emptyOperation.Kind);
        Assert.False(emptyOperation.CanUndo);
    }

    private static void AssertSelection(
        ProductWorkspaceLatestUndoKind kind,
        long revision,
        ProductWorkspaceLatestUndoSelection selection)
    {
        Assert.Equal(kind, selection.Kind);
        Assert.Equal(revision, selection.EditRevision);
        Assert.True(selection.CanUndo);
    }

    private static string Fingerprint(char value) => new(value, 64);
}
