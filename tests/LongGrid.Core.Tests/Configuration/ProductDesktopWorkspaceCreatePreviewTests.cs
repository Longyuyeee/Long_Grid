using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductDesktopWorkspaceCreatePreviewTests
{
    [Fact]
    public void ReadyDefaultsOpenOneEditableSideEffectFreeSession()
    {
        ProductDesktopWorkspaceCreatePreviewSession session =
            ProductDesktopWorkspaceCreatePreviewSession.Start(
                Request(),
                Ready("新方格"));

        Assert.Equal(
            ProductDesktopWorkspaceCreatePreviewStatus.Editing,
            session.Snapshot.Status);
        Assert.Equal(
            ProductDesktopWorkspaceCreatePreviewFailure.None,
            session.Snapshot.Failure);
        Assert.Equal("新方格", session.Snapshot.Name);
        Assert.True(session.Snapshot.CanSubmit);
        Assert.NotEqual(Guid.Empty, session.Snapshot.SessionId);
    }

    [Theory]
    [InlineData(
        ProductWorkspaceContainerCreationDefaultsStatus.Invalid,
        ProductDesktopWorkspaceCreatePreviewFailure.InvalidName)]
    [InlineData(
        ProductWorkspaceContainerCreationDefaultsStatus.DuplicateName,
        ProductDesktopWorkspaceCreatePreviewFailure.DuplicateName)]
    [InlineData(
        ProductWorkspaceContainerCreationDefaultsStatus.LimitReached,
        ProductDesktopWorkspaceCreatePreviewFailure.LimitReached)]
    [InlineData(
        ProductWorkspaceContainerCreationDefaultsStatus.PlacementUnavailable,
        ProductDesktopWorkspaceCreatePreviewFailure.PlacementUnavailable)]
    public void InvalidInitialPlanCannotBecomeAnEditingSession(
        ProductWorkspaceContainerCreationDefaultsStatus status,
        ProductDesktopWorkspaceCreatePreviewFailure expected)
    {
        ProductDesktopWorkspaceCreatePreviewSession session =
            ProductDesktopWorkspaceCreatePreviewSession.Start(
                Request(),
                new(status, null, null));

        Assert.Equal(
            ProductDesktopWorkspaceCreatePreviewStatus.Rejected,
            session.Snapshot.Status);
        Assert.Equal(expected, session.Snapshot.Failure);
        Assert.False(session.Snapshot.CanSubmit);
    }

    [Fact]
    public void NameCanFailThenRecoverWithoutChangingSessionIdentity()
    {
        ProductDesktopWorkspaceCreatePreviewSession session =
            ProductDesktopWorkspaceCreatePreviewSession.Start(
                Request(),
                Ready("新方格"));
        Guid sessionId = session.Snapshot.SessionId;

        ProductDesktopWorkspaceCreatePreviewSnapshot invalid =
            session.UpdateName(
                " work ",
                new(
                    ProductWorkspaceContainerCreationDefaultsStatus.DuplicateName,
                    null,
                    null));
        ProductDesktopWorkspaceCreatePreviewSnapshot fixedName =
            session.UpdateName("Work 2", Ready("Work 2"));

        Assert.Equal(sessionId, fixedName.SessionId);
        Assert.Equal(
            ProductDesktopWorkspaceCreatePreviewFailure.DuplicateName,
            invalid.Failure);
        Assert.False(invalid.CanSubmit);
        Assert.Equal("Work 2", fixedName.Name);
        Assert.True(fixedName.CanSubmit);
    }

    [Fact]
    public void SubmitRequiresMatchingWorkspaceAndTopology()
    {
        ProductDesktopWorkspaceCreatePreviewSession staleWorkspace = Start();
        ProductDesktopWorkspaceCreatePreviewSession staleTopology = Start();
        ProductDesktopWorkspaceCreatePreviewSession ready = Start();

        Assert.Equal(
            ProductDesktopWorkspaceCreatePreviewFailure.StaleWorkspace,
            staleWorkspace.PrepareSubmit(8, 11).Failure);
        Assert.Equal(
            ProductDesktopWorkspaceCreatePreviewFailure.StaleTopology,
            staleTopology.PrepareSubmit(7, 12).Failure);
        Assert.Equal(
            ProductDesktopWorkspaceCreatePreviewStatus.Submitting,
            ready.PrepareSubmit(7, 11).Status);
    }

    [Theory]
    [InlineData(ProductDesktopWorkspaceCreatePreviewFailure.Replaced)]
    [InlineData(ProductDesktopWorkspaceCreatePreviewFailure.UserCancelled)]
    [InlineData(ProductDesktopWorkspaceCreatePreviewFailure.HostUnavailable)]
    [InlineData(ProductDesktopWorkspaceCreatePreviewFailure.WindowClosing)]
    public void CancellationIsTerminalAndIdempotent(
        ProductDesktopWorkspaceCreatePreviewFailure failure)
    {
        ProductDesktopWorkspaceCreatePreviewSession session = Start();

        ProductDesktopWorkspaceCreatePreviewSnapshot first =
            session.Cancel(failure);
        ProductDesktopWorkspaceCreatePreviewSnapshot second =
            session.Cancel(ProductDesktopWorkspaceCreatePreviewFailure.Replaced);
        ProductDesktopWorkspaceCreatePreviewSnapshot afterSubmit =
            session.PrepareSubmit(7, 11);

        Assert.Equal(
            ProductDesktopWorkspaceCreatePreviewStatus.Cancelled,
            first.Status);
        Assert.Equal(failure, first.Failure);
        Assert.Same(first, second);
        Assert.Same(first, afterSubmit);
    }

    private static ProductDesktopWorkspaceCreatePreviewSession Start() =>
        ProductDesktopWorkspaceCreatePreviewSession.Start(
            Request(),
            Ready("新方格"));

    private static ProductDesktopWorkspaceCreateRequest Request() => new(
        ProductDesktopWorkspaceCreateInputKind.PrimaryPointer,
        "display-primary",
        WorkspaceRevision: 7,
        TopologyGeneration: 11,
        SourceAttested: true,
        IsInjected: false,
        IsAutoRepeat: false);

    private static ProductWorkspaceContainerCreationDefaultsDecision Ready(
        string name) => new(
            ProductWorkspaceContainerCreationDefaultsStatus.Ready,
            name,
            new()
            {
                DisplayKey = "display-primary",
                XDip = 32,
                YDip = 48,
                WidthDip = 360,
                HeightDip = 240,
            });
}
