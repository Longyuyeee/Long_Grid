using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Core.DesktopItems;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceSelectedReferenceCreateSnapshotTests
{
    [Fact]
    public void CaptureFreezesOrderedSelectionAndDetectsWorkspaceChange()
    {
        ProductWorkspaceState state = State(Item("item-1"), Item("item-2"));

        ProductWorkspaceSelectedReferenceCreateSnapshotResult captured =
            ProductWorkspaceSelectedReferenceCreateSnapshots.Capture(
                state,
                sourceContainerOrdinal: 1,
                itemOrdinals: [2, 1]);

        Assert.True(captured.IsReady);
        Assert.Equal(["item-2", "item-1"], captured.Snapshot!.ItemIds);
        Assert.Equal(
            ProductWorkspaceSelectedReferenceCreateSnapshotStatus.Ready,
            ProductWorkspaceSelectedReferenceCreateSnapshots.Evaluate(
                captured.Snapshot,
                state));
        Assert.Equal(
            ProductWorkspaceSelectedReferenceCreateSnapshotStatus.SelectionChanged,
            ProductWorkspaceSelectedReferenceCreateSnapshots.Evaluate(
                captured.Snapshot,
                state with
                {
                    Containers =
                    [
                        state.Containers[0] with
                        {
                            Name = "Changed",
                        },
                    ],
                }));
    }

    [Fact]
    public void InvalidLockedAndOverLimitSelectionsFailClosed()
    {
        ProductWorkspaceState state = State(Item("item-1"));
        int[] overLimit = Enumerable.Range(
            1,
            ProductWorkspaceSelectedReferenceCreateSnapshot.MaximumItemCount + 1)
            .ToArray();

        Assert.Equal(
            ProductWorkspaceSelectedReferenceCreateSnapshotStatus.InvalidRequest,
            ProductWorkspaceSelectedReferenceCreateSnapshots.Capture(
                state,
                1,
                []).Status);
        Assert.Equal(
            ProductWorkspaceSelectedReferenceCreateSnapshotStatus.InvalidRequest,
            ProductWorkspaceSelectedReferenceCreateSnapshots.Capture(
                state,
                1,
                overLimit).Status);
        Assert.Equal(
            ProductWorkspaceSelectedReferenceCreateSnapshotStatus.SourceLocked,
            ProductWorkspaceSelectedReferenceCreateSnapshots.Capture(
                state with
                {
                    Containers =
                    [
                        state.Containers[0] with { IsLocked = true },
                    ],
                },
                1,
                [1]).Status);
    }

    [Fact]
    public void PreviewCancellationKeepsSelectedReferenceSnapshotWithoutSubmitting()
    {
        ProductWorkspaceState state = State(Item("item-1"));
        ProductWorkspaceSelectedReferenceCreateSnapshot snapshot =
            ProductWorkspaceSelectedReferenceCreateSnapshots.Capture(
                state,
                1,
                [1]).Snapshot!;
        var request = new ProductDesktopWorkspaceCreateRequest(
            ProductDesktopWorkspaceCreateInputKind.SelectedReferences,
            "display-primary",
            WorkspaceRevision: 7,
            TopologyGeneration: 11,
            SourceAttested: true,
            IsInjected: false,
            IsAutoRepeat: false,
            RequestedBoundsPixels: null,
            SelectedReferences: snapshot);
        ProductWorkspaceContainerCreationDefaultsDecision defaults =
            ProductWorkspaceContainerCreationDefaults.Evaluate(
                state.Containers,
                requestedName: null,
                "display-primary",
                new(0, 0, 1920, 1040),
                effectiveDpi: 96);
        ProductDesktopWorkspaceCreatePreviewSession session =
            ProductDesktopWorkspaceCreatePreviewSession.Start(request, defaults);

        ProductDesktopWorkspaceCreatePreviewSnapshot cancelled = session.Cancel(
            ProductDesktopWorkspaceCreatePreviewFailure.UserCancelled);

        Assert.Equal(
            ProductDesktopWorkspaceCreatePreviewStatus.Cancelled,
            cancelled.Status);
        Assert.Equal(
            ProductDesktopWorkspaceCreatePreviewFailure.UserCancelled,
            cancelled.Failure);
        Assert.Equal(snapshot, cancelled.Request.SelectedReferences);
        Assert.Equal(
            ProductWorkspaceSelectedReferenceCreateSnapshotStatus.Ready,
            ProductWorkspaceSelectedReferenceCreateSnapshots.Evaluate(
                snapshot,
                state));
    }

    private static ProductWorkspaceState State(
        params ProductItemReferenceState[] items) => new()
        {
            ProfileId = "default",
            Containers =
            [
                new()
                {
                    Id = "source",
                    Name = "Source",
                    Appearance = new()
                    {
                        Color = "#2563EB",
                        Opacity = 0.88,
                    },
                    Placement = new()
                    {
                        DisplayKey = "display-primary",
                        WidthDip = 360,
                        HeightDip = 240,
                    },
                    Items = items,
                },
            ],
        };

    private static ProductItemReferenceState Item(string id) =>
        ProductItemReferenceState.CreateResolved(
            id,
            new(
                new DesktopItemIdentity("filesystem", $"C:\\Real\\{id}.txt"),
                "user-desktop",
                $"{id}.txt",
                DesktopItemKind.File));
}
