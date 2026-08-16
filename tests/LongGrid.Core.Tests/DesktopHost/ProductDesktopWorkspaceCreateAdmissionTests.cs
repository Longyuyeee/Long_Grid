using LongGrid.Core.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopWorkspaceCreateAdmissionTests
{
    [Theory]
    [InlineData(ProductDesktopWorkspaceCreateInputKind.PrimaryPointer)]
    [InlineData(ProductDesktopWorkspaceCreateInputKind.ContextMenu)]
    [InlineData(ProductDesktopWorkspaceCreateInputKind.KeyboardShortcut)]
    [InlineData(ProductDesktopWorkspaceCreateInputKind.AssistiveInvoke)]
    public void TrustedCurrentInputKindsUseOneAdmissionPath(
        ProductDesktopWorkspaceCreateInputKind kind)
    {
        ProductDesktopWorkspaceCreateAdmissionDecision decision =
            ProductDesktopWorkspaceCreateAdmission.Evaluate(
                Request(kind),
                currentWorkspaceRevision: 7,
                currentTopologyGeneration: 11);

        Assert.True(decision.CanCreate);
        Assert.Equal(
            ProductDesktopWorkspaceCreateAdmissionStatus.Ready,
            decision.Status);
    }

    [Theory]
    [InlineData(false, false, false,
        ProductDesktopWorkspaceCreateAdmissionStatus.UntrustedSource)]
    [InlineData(true, true, false,
        ProductDesktopWorkspaceCreateAdmissionStatus.Injected)]
    [InlineData(true, false, true,
        ProductDesktopWorkspaceCreateAdmissionStatus.AutoRepeat)]
    public void UnsafeSourcesFailClosed(
        bool attested,
        bool injected,
        bool autoRepeat,
        ProductDesktopWorkspaceCreateAdmissionStatus expected)
    {
        ProductDesktopWorkspaceCreateRequest request = Request(
            ProductDesktopWorkspaceCreateInputKind.PrimaryPointer) with
        {
            SourceAttested = attested,
            IsInjected = injected,
            IsAutoRepeat = autoRepeat,
        };

        Assert.Equal(
            expected,
            ProductDesktopWorkspaceCreateAdmission.Evaluate(request, 7, 11)
                .Status);
    }

    [Fact]
    public void StaleWorkspaceAndTopologyAreDistinguished()
    {
        Assert.Equal(
            ProductDesktopWorkspaceCreateAdmissionStatus.StaleWorkspace,
            ProductDesktopWorkspaceCreateAdmission.Evaluate(Request(), 8, 11)
                .Status);
        Assert.Equal(
            ProductDesktopWorkspaceCreateAdmissionStatus.StaleTopology,
            ProductDesktopWorkspaceCreateAdmission.Evaluate(Request(), 7, 12)
                .Status);
    }

    [Fact]
    public void MalformedRequestsFailClosed()
    {
        Assert.Equal(
            ProductDesktopWorkspaceCreateAdmissionStatus.Invalid,
            ProductDesktopWorkspaceCreateAdmission.Evaluate(null, 7, 11).Status);
        Assert.Equal(
            ProductDesktopWorkspaceCreateAdmissionStatus.Invalid,
            ProductDesktopWorkspaceCreateAdmission.Evaluate(
                Request() with { DisplayId = string.Empty },
                7,
                11).Status);
        Assert.Equal(
            ProductDesktopWorkspaceCreateAdmissionStatus.Invalid,
            ProductDesktopWorkspaceCreateAdmission.Evaluate(
                Request() with
                {
                    Kind = (ProductDesktopWorkspaceCreateInputKind)int.MaxValue,
                },
                7,
                11).Status);
    }

    private static ProductDesktopWorkspaceCreateRequest Request(
        ProductDesktopWorkspaceCreateInputKind kind =
            ProductDesktopWorkspaceCreateInputKind.PrimaryPointer) =>
        new(
            kind,
            "display-primary",
            WorkspaceRevision: 7,
            TopologyGeneration: 11,
            SourceAttested: true,
            IsInjected: false,
            IsAutoRepeat: false);
}
