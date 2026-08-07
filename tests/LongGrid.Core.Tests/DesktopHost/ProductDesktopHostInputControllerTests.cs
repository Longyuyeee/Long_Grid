using LongGrid.Infrastructure.DesktopHost;

namespace LongGrid.Core.Tests.DesktopHost;

public sealed class ProductDesktopHostInputControllerTests
{
    [Fact]
    public void ControllerClosesReopensAndHidesExactWindows()
    {
        var api = new FakeApi();
        var controller = new WindowsProductDesktopHostInputController(api);

        Assert.True(controller.SetEnabled([10, 11], enabled: false));
        Assert.All(api.Enabled.Values, Assert.False);
        Assert.True(controller.SetEnabled([10, 11], enabled: true));
        Assert.All(api.Enabled.Values, Assert.True);
        Assert.True(controller.Hide([10, 11]));
        Assert.All(api.Visible.Values, Assert.False);
    }

    [Fact]
    public void CloseFailureRollsEveryWindowBackToEnabled()
    {
        var api = new FakeApi
        {
            Ignore = (11, false),
        };
        var controller = new WindowsProductDesktopHostInputController(api);

        Assert.False(controller.SetEnabled([10, 11], enabled: false));

        Assert.All(api.Enabled.Values, Assert.True);
        Assert.All(api.Visible.Values, Assert.True);
    }

    [Fact]
    public void FailedCloseRollbackHidesEveryWindow()
    {
        var api = new FakeApi
        {
            Ignore = (11, false),
            IgnoreRollbackFor = 10,
        };
        var controller = new WindowsProductDesktopHostInputController(api);

        Assert.False(controller.SetEnabled([10, 11], enabled: false));

        Assert.All(api.Visible.Values, Assert.False);
    }

    [Fact]
    public void FailedReopenHidesEveryWindow()
    {
        var api = new FakeApi
        {
            Ignore = (11, true),
        };
        api.Enabled[10] = false;
        api.Enabled[11] = false;
        var controller = new WindowsProductDesktopHostInputController(api);

        Assert.False(controller.SetEnabled([10, 11], enabled: true));

        Assert.All(api.Visible.Values, Assert.False);
    }

    [Fact]
    public void ControllerRejectsUnsupportedEmptyZeroAndDuplicateHandles()
    {
        var api = new FakeApi { IsSupported = false };
        var controller = new WindowsProductDesktopHostInputController(api);

        Assert.False(controller.SetEnabled([10], enabled: false));
        api.IsSupported = true;
        Assert.False(controller.SetEnabled([], enabled: false));
        Assert.False(controller.SetEnabled([0], enabled: false));
        Assert.False(controller.Hide([10, 10]));
        Assert.Empty(api.Calls);
    }

    private sealed class FakeApi : IWindowsProductDesktopHostInputApi
    {
        internal FakeApi()
        {
            Enabled[10] = true;
            Enabled[11] = true;
            Visible[10] = true;
            Visible[11] = true;
        }

        public bool IsSupported { get; set; } = true;

        internal (nint Window, bool Enabled)? Ignore { get; init; }

        internal nint IgnoreRollbackFor { get; init; }

        internal Dictionary<nint, bool> Enabled { get; } = [];

        internal Dictionary<nint, bool> Visible { get; } = [];

        internal List<string> Calls { get; } = [];

        public void Enable(nint window, bool enabled)
        {
            Calls.Add($"enable:{window}:{enabled}");
            if (Ignore == (window, enabled)
                || enabled && window == IgnoreRollbackFor)
            {
                return;
            }

            Enabled[window] = enabled;
        }

        public bool IsEnabled(nint window) => Enabled[window];

        public void Hide(nint window)
        {
            Calls.Add($"hide:{window}");
            Visible[window] = false;
        }

        public bool IsVisible(nint window) => Visible[window];
    }
}
