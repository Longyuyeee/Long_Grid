using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using LongGrid.Core.DesktopHost;
using LongGrid.Infrastructure.DesktopHost;
using Xunit.Abstractions;

namespace LongGrid.Core.Tests.DesktopHost;

[Collection(DesktopHostNativeWindowTestGroup.Name)]
public sealed class WindowsProductDesktopHostUiaProviderTests(
    ITestOutputHelper output)
{
    [Fact]
    public void EmptyNativeSurfaceNormalizesContextAndKeyboardCreateInputs()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary",
                new(100, 200, 1920, 1040),
                96,
                Array.Empty<ProductDesktopHostReadOnlyProjection>());
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                display,
                new nint(899));
        var inputs = new List<ProductDesktopWorkspaceCreateInput>();
        surface.BindWorkspaceCreate(input =>
        {
            inputs.Add(input);
            return true;
        });

        Assert.True(surface.SubmitWorkspaceCreateInput(
            ProductDesktopWorkspaceCreateInputKind.ContextMenu,
            sourceAttested: true,
            isInjected: false,
            isAutoRepeat: false));
        Assert.True(surface.SubmitWorkspaceCreateInput(
            ProductDesktopWorkspaceCreateInputKind.KeyboardShortcut,
            sourceAttested: true,
            isInjected: false,
            isAutoRepeat: false));

        Assert.Equal(
            [
                ProductDesktopWorkspaceCreateInputKind.ContextMenu,
                ProductDesktopWorkspaceCreateInputKind.KeyboardShortcut,
            ],
            inputs.Select(input => input.Kind));
        Assert.All(inputs, input =>
        {
            Assert.True(input.SourceAttested);
            Assert.False(input.IsInjected);
            Assert.False(input.IsAutoRepeat);
        });
        Assert.NotEqual(surface.Handle, GetForegroundWindow());
        Assert.True(surface.PassiveWindowContractAttested);

        Assert.True(surface.ApplyHidden());
        Assert.False(surface.WorkspaceKeyboardCreateAvailable);
        Assert.True(surface.ApplyPassive());
        Assert.True(surface.PassiveWindowContractAttested);
    }

    [Fact]
    public void EmptyNativeSurfaceExposesInvokableCreateEntryWithoutForeground()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary",
                new(100, 200, 1920, 1040),
                96,
                Array.Empty<ProductDesktopHostReadOnlyProjection>());
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                display,
                new nint(900));
        int requests = 0;
        surface.BindWorkspaceCreate(input =>
        {
            Assert.Equal(
                ProductDesktopWorkspaceCreateInputKind.AssistiveInvoke,
                input.Kind);
            Assert.True(input.SourceAttested);
            Assert.False(input.IsInjected);
            requests++;
            return true;
        });

        AutomationElement root = AutomationElement.FromHandle(surface.Handle);
        AutomationElement button = root.FindFirst(
            TreeScope.Children,
            new PropertyCondition(
                AutomationElement.AutomationIdProperty,
                "LongGrid.DesktopHost.EmptyCreateButton"));

        Assert.NotNull(button);
        Assert.Equal(ControlType.Button, button.Current.ControlType);
        Assert.Equal("创建第一个方格", button.Current.Name);
        Assert.Contains(
            "不读取或移动",
            button.Current.ItemStatus,
            StringComparison.Ordinal);
        Assert.Equal(
            surface.WorkspaceKeyboardCreateAvailable
                ? "Ctrl+Alt+N"
                : string.Empty,
            button.Current.AccessKey);
        Assert.True(button.TryGetCurrentPattern(
            InvokePattern.Pattern,
            out object pattern));
        ((InvokePattern)pattern).Invoke();
        Assert.Equal(1, requests);
        Assert.NotEqual(surface.Handle, GetForegroundWindow());
        Assert.True(surface.PassiveWindowContractAttested);
    }

    [Fact]
    public void NonEmptyNativeSurfaceKeepsCreateEntryWithoutBlockingSelectionMode()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary",
                new(100, 200, 1920, 1040),
                96,
                [CreateContainer(
                    "container-1",
                    "工作",
                    ["需求文档.docx"],
                    collapsed: false,
                    x: 24,
                    y: 36)],
                isPrimary: true,
                workspaceIsEmpty: false);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                display,
                new nint(9001));
        var kinds = new List<ProductDesktopWorkspaceCreateInputKind>();
        surface.BindWorkspaceCreate(input =>
        {
            kinds.Add(input.Kind);
            return true;
        });

        Assert.True(surface.SubmitWorkspaceCreateInput(
            ProductDesktopWorkspaceCreateInputKind.KeyboardShortcut,
            sourceAttested: true,
            isInjected: false,
            isAutoRepeat: false));

        AutomationElement root = AutomationElement.FromHandle(surface.Handle);
        AutomationElement button = root.FindFirst(
            TreeScope.Children,
            new PropertyCondition(
                AutomationElement.AutomationIdProperty,
                "LongGrid.DesktopHost.WorkspaceCreateButton"));
        Assert.NotNull(button);
        Assert.Equal("新建方格", button.Current.Name);
        Assert.True(button.Current.IsEnabled);
        Assert.True(button.TryGetCurrentPattern(
            InvokePattern.Pattern,
            out object pattern));
        ((InvokePattern)pattern).Invoke();
        Assert.Equal(
            [
                ProductDesktopWorkspaceCreateInputKind.KeyboardShortcut,
                ProductDesktopWorkspaceCreateInputKind.AssistiveInvoke,
            ],
            kinds);

        Assert.True(surface.ApplyExplicit());
        Assert.False(surface.WorkspaceKeyboardCreateAvailable);
        Assert.False(surface.SubmitWorkspaceCreateInput(
            ProductDesktopWorkspaceCreateInputKind.KeyboardShortcut,
            sourceAttested: true,
            isInjected: false,
            isAutoRepeat: false));
        Assert.NotEqual(surface.Handle, GetForegroundWindow());
    }

    [Fact]
    public void RealNativeExplicitSurfaceCarriesDraggedBoundsWithoutForeground()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-secondary",
                new(1920, -100, 1600, 1000),
                192,
                [CreateContainer("container-1", "工作", [], false, 24, 36)]);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(display, new nint(9002));
        ProductDesktopWorkspaceCreateInput? captured = null;
        surface.BindWorkspaceCreate(input =>
        {
            captured = input;
            return true;
        });
        PixelRect requested = new(2120, 100, 640, 400);

        Assert.False(surface.SubmitWorkspaceCreateDragInput(
            requested,
            sourceAttested: true,
            isInjected: false));
        Assert.True(surface.ApplyExplicit());
        Assert.True(surface.SubmitWorkspaceCreateDragInput(
            requested,
            sourceAttested: true,
            isInjected: false));

        Assert.Equal(ProductDesktopWorkspaceCreateInputKind.PointerDrag, captured!.Kind);
        Assert.Equal(requested, captured.RequestedBoundsPixels);
        Assert.True(captured.SourceAttested);
        Assert.False(captured.IsInjected);
        Assert.True(surface.ExplicitWindowContractAttested);
        Assert.NotEqual(surface.Handle, GetForegroundWindow());
    }

    [Fact]
    public void NativeSurfaceExposesReadOnlyNonFocusableProjectionTree()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary",
                new(100, 200, 1920, 1040),
                96,
                [
                    CreateContainer(
                        "container-1",
                        "工作",
                        ["需求文档.docx", "设计参考.fig"],
                        collapsed: false,
                        x: 24,
                        y: 36),
                    CreateContainer(
                        "container-2",
                        "归档",
                        ["历史记录.txt"],
                        collapsed: true,
                        x: 420,
                        y: 48),
                ]);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(display, new nint(901));

        AutomationElement root = AutomationElement.FromHandle(surface.Handle);
        AutomationElementCollection groups = root.FindAll(
            TreeScope.Children,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Group));

        Assert.Equal("LongGrid.DesktopHost.Root", root.Current.AutomationId);
        Assert.Equal("Long方格桌面只读区域", root.Current.Name);
        Assert.True(root.Current.IsKeyboardFocusable);
        Assert.Contains("可新建方格", root.Current.ItemStatus, StringComparison.Ordinal);
        Assert.Equal(2, groups.Count);

        AutomationElement expanded = groups[0];
        AutomationElement collapsed = groups[1];
        Assert.Equal("LongGrid.DesktopHost.Container.1", expanded.Current.AutomationId);
        Assert.Contains("工作", expanded.Current.Name, StringComparison.Ordinal);
        Assert.Contains("只读", expanded.Current.ItemStatus, StringComparison.Ordinal);
        Assert.False(expanded.Current.IsKeyboardFocusable);
        Assert.False(expanded.TryGetCurrentPattern(
            SelectionPattern.Pattern,
            out _));
        Assert.False(expanded.TryGetCurrentPattern(
            InvokePattern.Pattern,
            out _));

        AutomationElementCollection items = expanded.FindAll(
            TreeScope.Children,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Text));
        Assert.Equal(2, items.Count);
        Assert.Equal("需求文档.docx；文件；类型图标已就绪", items[0].Current.Name);
        Assert.Equal("设计参考.fig；文件；类型图标已就绪", items[1].Current.Name);
        Assert.All(items.Cast<AutomationElement>(), item =>
        {
            Assert.False(item.Current.IsKeyboardFocusable);
            Assert.Contains("未公开路径", item.Current.ItemStatus, StringComparison.Ordinal);
            Assert.False(item.TryGetCurrentPattern(InvokePattern.Pattern, out _));
        });
        Assert.Empty(collapsed.FindAll(
            TreeScope.Children,
            Condition.TrueCondition).Cast<AutomationElement>());

        System.Windows.Rect bounds = expanded.Current.BoundingRectangle;
        Assert.Equal(124, bounds.Left);
        Assert.Equal(236, bounds.Top);
        Assert.Equal(360, bounds.Width);
        Assert.Equal(240, bounds.Height);
    }

    [Fact]
    public void NativeSurfaceExplicitModeIsSelectableWithoutTakingForeground()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary",
                new(100, 200, 1920, 1040),
                96,
                [CreateContainer("container-1", "工作", ["需求文档.docx"], false, 24, 36)]);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(display, new nint(902));
        var adapter = new ProductDesktopHostPassiveSurfaceModeAdapter(
            new IProductDesktopHostReadOnlySurface[] { surface },
            registryGeneration: 11);

        Assert.True(adapter.ApplyExplicit(new(
            Guid.NewGuid(),
            "container-1",
            7,
            9,
            11,
            DateTimeOffset.UtcNow.AddSeconds(1))));

        AutomationElement root = AutomationElement.FromHandle(surface.Handle);
        Assert.True(root.Current.IsKeyboardFocusable);
        Assert.Contains("等待输入消费接线", root.Current.ItemStatus, StringComparison.Ordinal);
        Assert.True(root.TryGetCurrentPattern(SelectionPattern.Pattern, out _));
        ProductDesktopInteractionSurfaceEvidence explicitEvidence =
            adapter.Capture().Evidence!;
        Assert.True(explicitEvidence.IsExplicitContract);
        Assert.False(explicitEvidence.OwnsForeground);

        Assert.True(adapter.ApplyPassive(11));
        root = AutomationElement.FromHandle(surface.Handle);
        Assert.True(root.Current.IsKeyboardFocusable);
        Assert.False(root.TryGetCurrentPattern(SelectionPattern.Pattern, out _));
        Assert.True(adapter.Capture().Evidence!.IsPassiveContract);

        Assert.True(adapter.Hide(11));
        Assert.True(adapter.Capture().Evidence!.IsHiddenContract);
    }

    [Fact]
    public void RealHwndUiaInvokeSelectsThenUsesOpenCommand()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        ProductDesktopHostReadOnlyProjection container = CreateContainer(
            "container-1", "工作", ["真实项目"], false, 24, 36);
        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary", new(100, 200, 1920, 1040), 96,
                [container]);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                display,
                new nint(912));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var lease = new ProductDesktopInteractionLease(
            Guid.NewGuid(), "container-1", 7, 9, 11,
            now.AddSeconds(5));
        ProductDesktopInteractionSelectionController selection =
            ProductDesktopInteractionSelectionController.TryCreate(
                lease,
                container.ItemIds,
                now).Controller!;
        ProductDesktopInteractionSurfaceTransactionSnapshot current =
            Transaction(selection.Snapshot);
        var opened = new List<ProductDesktopItemOpenSurfaceInput>();
        surface.BindSelection(
            () => current,
            (_, request) =>
            {
                ProductDesktopSelectionSnapshot updated = selection.Apply(
                    lease,
                    container.ItemIds,
                    request,
                    now);
                current = Transaction(updated);
                return updated.Status == ProductDesktopSelectionStatus.Applied;
            });
        surface.BindItemOpen(input =>
        {
            opened.Add(input);
            return true;
        });
        var adapter = new ProductDesktopHostPassiveSurfaceModeAdapter(
            new IProductDesktopHostReadOnlySurface[] { surface },
            registryGeneration: 11);
        Assert.True(adapter.ApplyExplicit(lease));

        AutomationElement root = AutomationElement.FromHandle(surface.Handle);
        AutomationElement item = root.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.ListItem));
        var invoke = (InvokePattern)item.GetCurrentPattern(
            InvokePattern.Pattern);
        invoke.Invoke();

        ProductDesktopItemOpenSurfaceInput actual = Assert.Single(opened);
        Assert.Equal("container-1", actual.ContainerId);
        Assert.Equal("item:1", actual.ItemId);
        Assert.Equal(ProductDesktopItemOpenSource.AssistiveInvoke,
            actual.Source);
        Assert.Equal(["item:1"], selection.Snapshot.SelectedItemIds);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Purpose = "Pf006b1RealHwndUiaInvokeEvidence",
            Expected = new
            {
                InvokeSource = "AssistiveInvoke",
                ContainerId = "container-1",
                ItemId = "item:1",
                SelectionChanged = true,
            },
            Actual = new
            {
                InvokeSource = actual.Source.ToString(),
                actual.ContainerId,
                actual.ItemId,
                SelectionChanged = selection.Snapshot.HasSelection,
            },
            Difference = "None",
            Outcome = "Pass",
        }));

        ProductDesktopInteractionSurfaceTransactionSnapshot Transaction(
            ProductDesktopSelectionSnapshot selected) => new(
                ProductDesktopInteractionSurfaceTransactionStatus.Explicit,
                new(
                    ProductDesktopInteractionMode.ExplicitInteraction,
                    ProductDesktopInteractionAdmissionStatus.Admitted,
                    ProductDesktopInteractionCancellationReason.None,
                    lease),
                new(
                    ProductDesktopInteractionSurfaceMode.Explicit,
                    11,
                    Visible: true,
                    HitTestTransparent: false,
                    IsKeyboardFocusable: true,
                    SelectionPatternAvailable: true,
                    ToolWindow: true,
                    NoActivate: true,
                    Topmost: false,
                    HasOwner: false,
                    OwnsForeground: false),
                selected,
                ProductDesktopInteractionSelectionAccessibilityAdapter
                    .CreateExplicit(selected),
                selected.SelectionRevision + 1);
    }

    [Fact]
    public void RealHwndPublishesFiniteOpenFailureToVisibleItemAndUia()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        ProductDesktopHostReadOnlyProjection container = CreateContainer(
            "container-1", "工作", ["不安全网址"], false, 24, 36);
        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary", new(100, 200, 1920, 1040), 96,
                [container]);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                display,
                new nint(913));

        const string expected = "网址协议不受支持，仅允许 HTTP/HTTPS";
        Assert.True(surface.ApplyItemOpenFeedback(new(
            "container-1",
            "item:1",
            ProductDesktopItemOpenStatus.ProtocolRejected,
            expected)));

        AutomationElement root = AutomationElement.FromHandle(surface.Handle);
        AutomationElement item = root.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.AutomationIdProperty,
                "LongGrid.DesktopHost.Item.1.1"));
        Assert.Contains(expected, item.Current.ItemStatus,
            StringComparison.Ordinal);
        Assert.DoesNotContain("file:", item.Current.ItemStatus,
            StringComparison.OrdinalIgnoreCase);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Purpose = "Pf006b2RealHwndFiniteOpenFeedback",
            Expected = new
            {
                ContainsMessage = true,
                UiaContainsPath = false,
                Status = "ProtocolRejected",
            },
            Actual = new
            {
                ContainsMessage = item.Current.ItemStatus.Contains(
                    expected,
                    StringComparison.Ordinal),
                UiaContainsPath = item.Current.ItemStatus.Contains(
                    "file:",
                    StringComparison.OrdinalIgnoreCase),
                Status = ProductDesktopItemOpenStatus.ProtocolRejected
                    .ToString(),
            },
            Difference = "None",
            Outcome = "Pass",
        }));
    }

    [Fact]
    public void RealHwndSingleClickOpenRequiresExplicitPolicyOptIn()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        ProductDesktopHostReadOnlyProjection container = CreateContainer(
            "container-1", "工作", ["真实项目"], false, 24, 36);
        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary", new(100, 200, 1920, 1040), 96,
                [container]);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                display,
                new nint(914));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var lease = new ProductDesktopInteractionLease(
            Guid.NewGuid(), "container-1", 7, 9, 11,
            now.AddSeconds(5));
        ProductDesktopInteractionSelectionController selection =
            ProductDesktopInteractionSelectionController.TryCreate(
                lease,
                container.ItemIds,
                now).Controller!;
        ProductDesktopInteractionSurfaceTransactionSnapshot current =
            Transaction(selection.Snapshot);
        var opened = new List<ProductDesktopItemOpenSurfaceInput>();
        surface.BindSelection(
            () => current,
            (_, request) =>
            {
                ProductDesktopSelectionSnapshot updated = selection.Apply(
                    lease,
                    container.ItemIds,
                    request,
                    now);
                current = Transaction(updated);
                return updated.Status == ProductDesktopSelectionStatus.Applied;
            });
        surface.BindItemOpen(input =>
        {
            opened.Add(input);
            return true;
        });
        var adapter = new ProductDesktopHostPassiveSurfaceModeAdapter(
            new IProductDesktopHostReadOnlySurface[] { surface },
            registryGeneration: 11);
        Assert.True(adapter.ApplyExplicit(lease));

        Assert.True(surface.SubmitPrimaryPointerForEvidence(30, 95));
        Assert.Empty(opened);
        int defaultOpenCount = opened.Count;
        Assert.True(surface.ApplyItemOpenPolicy(singleClickEnabled: true));
        Assert.True(surface.SubmitPrimaryPointerForEvidence(30, 95));

        ProductDesktopItemOpenSurfaceInput actual = Assert.Single(opened);
        Assert.Equal(ProductDesktopItemOpenSource.PointerSingleClick,
            actual.Source);
        Assert.Equal("item:1", actual.ItemId);
        Assert.True(surface.SubmitPrimaryPointerForEvidence(
            30, 95, control: true));
        int modifiedOpenCount = opened.Count;
        Assert.Single(opened);
        Assert.False(surface.SubmitPrimaryPointerForEvidence(
            30, 95, sourceAttested: false));
        int unattestedOpenCount = opened.Count;
        Assert.Single(opened);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Purpose = "Pf006b2b1RealHwndSingleClickPolicy",
            Expected = new
            {
                DefaultOpenCount = 0,
                OptInOpenCount = 1,
                ModifiedOpenCount = 1,
                UnattestedOpenCount = 1,
                Source = "PointerSingleClick",
            },
            Actual = new
            {
                DefaultOpenCount = defaultOpenCount,
                OptInOpenCount = opened.Count,
                ModifiedOpenCount = modifiedOpenCount,
                UnattestedOpenCount = unattestedOpenCount,
                Source = actual.Source.ToString(),
            },
            Difference = "None",
            Outcome = "Pass",
        }));

        ProductDesktopInteractionSurfaceTransactionSnapshot Transaction(
            ProductDesktopSelectionSnapshot selected) => new(
                ProductDesktopInteractionSurfaceTransactionStatus.Explicit,
                new(
                    ProductDesktopInteractionMode.ExplicitInteraction,
                    ProductDesktopInteractionAdmissionStatus.Admitted,
                    ProductDesktopInteractionCancellationReason.None,
                    lease),
                new(
                    ProductDesktopInteractionSurfaceMode.Explicit,
                    11,
                    Visible: true,
                    HitTestTransparent: false,
                    IsKeyboardFocusable: true,
                    SelectionPatternAvailable: true,
                    ToolWindow: true,
                    NoActivate: true,
                    Topmost: false,
                    HasOwner: false,
                    OwnsForeground: false),
                selected,
                ProductDesktopInteractionSelectionAccessibilityAdapter
                    .CreateExplicit(selected),
                selected.SelectionRevision + 1);
    }

    [Fact]
    public void RealHwndFeedbackActionsRemainFiniteAndSourceAttested()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        ProductDesktopHostReadOnlyProjection container = CreateContainer(
            "container-1", "工作", ["失败项目"], false, 24, 36);
        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary", new(100, 200, 1920, 1040), 96,
                [container]);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                display,
                new nint(915));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var lease = new ProductDesktopInteractionLease(
            Guid.NewGuid(), "container-1", 7, 9, 11,
            now.AddSeconds(5));
        ProductDesktopInteractionSelectionController selection =
            ProductDesktopInteractionSelectionController.TryCreate(
                lease,
                container.ItemIds,
                now).Controller!;
        ProductDesktopInteractionSurfaceTransactionSnapshot current =
            Transaction(selection.Snapshot);
        var actions = new List<ProductDesktopItemOpenSurfaceInput>();
        surface.BindSelection(
            () => current,
            (_, request) =>
            {
                ProductDesktopSelectionSnapshot updated = selection.Apply(
                    lease,
                    container.ItemIds,
                    request,
                    now);
                current = Transaction(updated);
                return updated.Status == ProductDesktopSelectionStatus.Applied;
            });
        surface.BindItemOpen(input =>
        {
            actions.Add(input);
            return true;
        });
        var adapter = new ProductDesktopHostPassiveSurfaceModeAdapter(
            new IProductDesktopHostReadOnlySurface[] { surface },
            registryGeneration: 11);
        Assert.True(adapter.ApplyExplicit(lease));
        Assert.True(surface.ApplyItemOpenFeedback(new(
            "container-1",
            "item:1",
            ProductDesktopItemOpenStatus.TargetUnavailable,
            "引用不存在；右键可重试或定位",
            CanRetry: true,
            CanLocateInExplorer: true)));

        Assert.True(surface.SubmitItemOpenFeedbackActionForEvidence(
            ProductDesktopItemOpenSource.FeedbackRetry));
        Assert.True(surface.SubmitItemOpenFeedbackActionForEvidence(
            ProductDesktopItemOpenSource.FeedbackLocateInExplorer));
        Assert.False(surface.SubmitItemOpenFeedbackActionForEvidence(
            ProductDesktopItemOpenSource.FeedbackRetry,
            sourceAttested: false));
        Assert.False(surface.SubmitItemOpenFeedbackActionForEvidence(
            ProductDesktopItemOpenSource.FeedbackLocateInExplorer,
            isInjected: true));

        Assert.Equal(2, actions.Count);
        Assert.All(actions, action =>
        {
            Assert.Equal("container-1", action.ContainerId);
            Assert.Equal("item:1", action.ItemId);
            Assert.True(action.SourceAttested);
            Assert.False(action.IsInjected);
        });
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Purpose = "Pf006b2b2RealHwndFeedbackActions",
            Expected = new
            {
                AcceptedSources =
                    "FeedbackRetry,FeedbackLocateInExplorer",
                AcceptedCount = 2,
                UnattestedRejected = true,
                InjectedRejected = true,
            },
            Actual = new
            {
                AcceptedSources = string.Join(",", actions.Select(action =>
                    action.Source.ToString())),
                AcceptedCount = actions.Count,
                UnattestedRejected = true,
                InjectedRejected = true,
            },
            Difference = "None",
            Outcome = "Pass",
        }));

        ProductDesktopInteractionSurfaceTransactionSnapshot Transaction(
            ProductDesktopSelectionSnapshot selected) => new(
                ProductDesktopInteractionSurfaceTransactionStatus.Explicit,
                new(
                    ProductDesktopInteractionMode.ExplicitInteraction,
                    ProductDesktopInteractionAdmissionStatus.Admitted,
                    ProductDesktopInteractionCancellationReason.None,
                    lease),
                new(
                    ProductDesktopInteractionSurfaceMode.Explicit,
                    11,
                    Visible: true,
                    HitTestTransparent: false,
                    IsKeyboardFocusable: true,
                    SelectionPatternAvailable: true,
                    ToolWindow: true,
                    NoActivate: true,
                    Topmost: false,
                    HasOwner: false,
                    OwnsForeground: false),
                selected,
                ProductDesktopInteractionSelectionAccessibilityAdapter
                    .CreateExplicit(selected),
                selected.SelectionRevision + 1);
    }

    [Fact]
    public async Task NativeActivationSourceExposesFiniteInvokeAndHideRestoreContract()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary",
                new(100, 200, 1920, 1040),
                96,
                [CreateContainer(
                    "container-1",
                    "工作",
                    ["需求文档.docx"],
                    false,
                    24,
                    36)]);
        ProductDesktopInteractionForwardedInput? forwarded = null;
        var headerInputs = new List<ProductDesktopContainerHeaderSurfaceInput>();
        var menuInputs = new List<ProductDesktopContainerMenuSurfaceInput>();
        using WindowsProductDesktopInteractionActivationSource source =
            WindowsProductDesktopInteractionActivationSource.Create(
                display,
                new nint(903),
                input =>
                {
                    forwarded = input;
                    return true;
                });
        source.BindContainerHeaderCommand(input =>
        {
            headerInputs.Add(input);
            return true;
        });
        source.BindContainerMenu(
            _ => new(
                CanOpenRename: true,
                CanOpenAppearance: true,
                CanOpenSort: true,
                CanDeleteContainerConfiguration: true),
            input =>
            {
                menuInputs.Add(input);
                return true;
            });

        AutomationElement root = AutomationElement.FromHandle(source.Handle);
        Assert.Equal(
            "LongGrid.DesktopHost.Activation.display-primary",
            root.Current.AutomationId);
        Assert.Equal("桌面方格交互入口", root.Current.Name);
        Assert.Equal(ControlType.Pane, root.Current.ControlType);
        AutomationElementCollection buttons = root.FindAll(
            TreeScope.Children,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button));
        Assert.Equal(4, buttons.Count);
        AutomationElement button = buttons.Cast<AutomationElement>()
            .Single(candidate => candidate.Current.Name == "进入 工作 交互");
        Assert.NotNull(button);
        Assert.Equal(
            "LongGrid.DesktopHost.ActivationButton.3",
            button.Current.AutomationId);
        Assert.False(button.Current.IsKeyboardFocusable);
        Assert.Equal(new System.Windows.Rect(420, 236, 32, 32),
            button.Current.BoundingRectangle);
        AutomationElement collapse = buttons.Cast<AutomationElement>()
            .Single(candidate => candidate.Current.Name == "折叠 工作");
        Assert.Equal(new System.Windows.Rect(388, 236, 32, 32),
            collapse.Current.BoundingRectangle);
        ((InvokePattern)collapse.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        AutomationElement lockButton = buttons.Cast<AutomationElement>()
            .Single(candidate => candidate.Current.Name == "锁定 工作");
        Assert.Equal(new System.Windows.Rect(356, 236, 32, 32),
            lockButton.Current.BoundingRectangle);
        ((InvokePattern)lockButton.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        AutomationElement more = buttons.Cast<AutomationElement>()
            .Single(candidate => candidate.Current.Name == "更多 工作 管理操作");
        Assert.Equal(new System.Windows.Rect(452, 236, 32, 32),
            more.Current.BoundingRectangle);
        ((InvokePattern)more.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Task<(string Name, bool Enabled)[]> observeMenu = Task.Run(() =>
        {
            AutomationElement? nativeMenu = null;
            bool visible = SpinWait.SpinUntil(() =>
            {
                nativeMenu = AutomationElement.RootElement.FindFirst(
                    TreeScope.Children,
                    new AndCondition(
                        new PropertyCondition(
                            AutomationElement.ClassNameProperty,
                            "#32768"),
                        new PropertyCondition(
                            AutomationElement.ProcessIdProperty,
                            Environment.ProcessId)));
                return nativeMenu is not null;
            }, TimeSpan.FromSeconds(5));
            Assert.True(visible);
            return nativeMenu!.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.MenuItem))
                .Cast<AutomationElement>()
                .Select(item => (item.Current.Name, item.Current.IsEnabled))
                .ToArray();
        });
        source.ShowPendingContainerMenuForEvidence();
        (string Name, bool Enabled)[] menuItems = await observeMenu;
        Assert.Equal(
            [
                ("重命名…", true),
                ("外观…", true),
                ("方格列表排序…", true),
                ("创建规则（后续功能）", false),
                ("生成 Portal / Tab（后续功能）", false),
                ("删除方格配置…", true),
            ],
            menuItems);
        Assert.False(source.IsContainerMenuOpenForEvidence);
        Assert.Empty(menuInputs);
        Assert.True(button.TryGetCurrentPattern(
            InvokePattern.Pattern,
            out object? pattern));
        ((InvokePattern)pattern!).Invoke();

        Assert.NotNull(forwarded);
        Assert.Equal(
            ProductDesktopInteractionForwardedInputKind
                .AssistiveTechnologyActivation,
            forwarded.Kind);
        Assert.Equal("display-primary", forwarded.DisplayId);
        Assert.True(forwarded.SourceAttested);
        Assert.False(forwarded.IsInjected);
        Assert.False(forwarded.IsAutoRepeat);
        Assert.Collection(
            headerInputs,
            input =>
            {
                Assert.Equal(
                    ProductDesktopContainerHeaderCommandKind.ToggleCollapsed,
                    input.Kind);
                Assert.Equal("container-1", input.ContainerId);
                Assert.True(input.SourceAttested);
                Assert.False(input.IsInjected);
                Assert.False(input.IsAutoRepeat);
            },
            input => Assert.Equal(
                ProductDesktopContainerHeaderCommandKind.ToggleLocked,
                input.Kind));
        Assert.True(source.ContractAttested);
        Assert.False(source.OwnsForegroundWindow);
        Assert.False(source.RequestKeyboardInteraction());
        source.BindSelection(() => null, _ => false, () => false);
        Assert.Throws<ArgumentNullException>(() =>
            source.BindSelection(null!, _ => false, () => false));
        Assert.Throws<ArgumentNullException>(() =>
            source.BindSelection(() => null, null!, () => false));
        Assert.Throws<ArgumentNullException>(() =>
            source.BindSelection(() => null, _ => false, null!));
        Assert.True(source.ApplyHidden());
        Assert.False(source.IsVisible);
        Assert.True(source.ApplyVisible());
        Assert.True(source.ContractAttested);
        bool keyboardProxyEntered = source.RequestKeyboardInteraction();
        if (keyboardProxyEntered)
        {
            Assert.True(source.OwnsForegroundWindow);
        }
        else
        {
            Assert.False(source.OwnsForegroundWindow);
            Assert.True(source.CanActivate);
            Assert.True(source.ContractAttested);
        }

        source.Dispose();
        source.Dispose();
        Assert.Throws<ObjectDisposedException>(() => source.ApplyVisible());
        Assert.Throws<ObjectDisposedException>(() => source.ApplyHidden());
        Assert.Throws<ObjectDisposedException>(() =>
            source.RequestKeyboardInteraction());

        using WindowsProductDesktopInteractionActivationSource rejected =
            WindowsProductDesktopInteractionActivationSource.Create(
                display,
                new nint(904),
                _ => false);
        Assert.False(rejected.RequestKeyboardInteraction());
        Assert.True(rejected.CanActivate);
    }

    [Fact]
    public void ActivationUiaFragmentsExposeOnlyFiniteButtonsAndDirectInvokePath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary",
                new(100, 200, 1920, 1040),
                96,
                [CreateContainer(
                    "container-1",
                    "工作",
                    ["需求文档.docx"],
                    false,
                    24,
                    36)]);
        using WindowsProductDesktopInteractionActivationSource source =
            WindowsProductDesktopInteractionActivationSource.Create(
                display,
                new nint(904),
                _ => true);
        var firstRegion =
            new WindowsProductDesktopInteractionActivationSource.ActivationRegion(
                24,
                36,
                30,
                30,
                39,
                51);
        var secondRegion =
            new WindowsProductDesktopInteractionActivationSource.ActivationRegion(
                84,
                36,
                30,
                30,
                99,
                51);
        bool available = true;
        bool invokeResult = true;
        WindowsProductDesktopInteractionActivationSource.ActivationRegion?
            invoked = null;
        var provider = new WindowsProductDesktopInteractionActivationSource
            .ActivationUiaProvider(
                source.Handle,
                display,
                [firstRegion, secondRegion],
                new nint(904),
                _ => available,
                region =>
                {
                    invoked = region;
                    return invokeResult;
                });

        Assert.NotNull(provider.HostRawElementProvider);
        Assert.Equal(new System.Windows.Rect(124, 236, 90, 30),
            provider.BoundingRectangle);
        Assert.Same(provider, provider.FragmentRoot);
        Assert.Null(provider.GetPatternProvider(
            InvokePatternIdentifiers.Pattern.Id));
        Assert.Equal("桌面方格交互入口", provider.GetPropertyValue(
            AutomationElementIdentifiers.NameProperty.Id));
        Assert.Equal("LongGrid.DesktopHost.Activation.display-primary",
            provider.GetPropertyValue(
                AutomationElementIdentifiers.AutomationIdProperty.Id));
        Assert.Equal(ControlType.Pane.Id, provider.GetPropertyValue(
            AutomationElementIdentifiers.ControlTypeProperty.Id));
        Assert.True((bool)provider.GetPropertyValue(
            AutomationElementIdentifiers.IsControlElementProperty.Id)!);
        Assert.True((bool)provider.GetPropertyValue(
            AutomationElementIdentifiers.IsContentElementProperty.Id)!);
        Assert.True((bool)provider.GetPropertyValue(
            AutomationElementIdentifiers.IsEnabledProperty.Id)!);
        Assert.False((bool)provider.GetPropertyValue(
            AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id)!);
        Assert.Null(provider.GetPropertyValue(-1));
        Assert.Null(provider.GetRuntimeId());
        Assert.Null(provider.GetEmbeddedFragmentRoots());
        Assert.Null(provider.GetFocus());
        provider.SetFocus();

        var first = Assert.IsType<
            WindowsProductDesktopInteractionActivationSource
                .ActivationUiaButtonProvider>(
                    provider.Navigate(NavigateDirection.FirstChild));
        var second = Assert.IsType<
            WindowsProductDesktopInteractionActivationSource
                .ActivationUiaButtonProvider>(
                    provider.Navigate(NavigateDirection.LastChild));
        Assert.Null(provider.Navigate(NavigateDirection.Parent));
        Assert.Same(first, provider.ElementProviderFromPoint(125, 237));
        Assert.Null(provider.ElementProviderFromPoint(500, 500));
        Assert.True(firstRegion.Contains(24, 36));
        Assert.False(firstRegion.Contains(54, 66));

        Assert.Null(first.HostRawElementProvider);
        Assert.Equal(new System.Windows.Rect(124, 236, 30, 30),
            first.BoundingRectangle);
        Assert.Same(provider, first.FragmentRoot);
        Assert.Same(first, first.GetPatternProvider(
            InvokePatternIdentifiers.Pattern.Id));
        Assert.Null(first.GetPatternProvider(-1));
        Assert.Equal("进入桌面方格交互", first.GetPropertyValue(
            AutomationElementIdentifiers.NameProperty.Id));
        Assert.Equal("LongGrid.DesktopHost.ActivationButton.1",
            first.GetPropertyValue(
                AutomationElementIdentifiers.AutomationIdProperty.Id));
        Assert.Equal(ControlType.Button.Id, first.GetPropertyValue(
            AutomationElementIdentifiers.ControlTypeProperty.Id));
        Assert.True((bool)first.GetPropertyValue(
            AutomationElementIdentifiers.IsControlElementProperty.Id)!);
        Assert.True((bool)first.GetPropertyValue(
            AutomationElementIdentifiers.IsContentElementProperty.Id)!);
        Assert.True((bool)first.GetPropertyValue(
            AutomationElementIdentifiers.IsEnabledProperty.Id)!);
        Assert.False((bool)first.GetPropertyValue(
            AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id)!);
        Assert.Null(first.GetPropertyValue(-1));
        Assert.Same(provider, first.Navigate(NavigateDirection.Parent));
        Assert.Same(second, first.Navigate(NavigateDirection.NextSibling));
        Assert.Null(first.Navigate(NavigateDirection.PreviousSibling));
        Assert.Same(first, second.Navigate(NavigateDirection.PreviousSibling));
        Assert.Null(second.Navigate(NavigateDirection.NextSibling));
        Assert.Equal(3, first.GetRuntimeId().Length);
        Assert.Null(first.GetEmbeddedFragmentRoots());
        first.SetFocus();

        first.Invoke();
        Assert.Equal(firstRegion, invoked);
        available = false;
        Assert.Throws<ElementNotEnabledException>(first.Invoke);
        available = true;
        invokeResult = false;
        Assert.Throws<ElementNotEnabledException>(first.Invoke);
    }

    [Fact]
    public void ExplicitItemProviderUsesSharedSelectionSnapshotAndPatterns()
    {
        if (!OperatingSystem.IsWindows()) return;

        ProductDesktopHostReadOnlyProjection container = CreateContainer(
            "container-1", "Work", ["Plan.docx"], false, 24, 36);
        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary", new(100, 200, 1920, 1040), 96, [container]);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(display, new nint(910));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var lease = new ProductDesktopInteractionLease(
            Guid.NewGuid(), "container-1", 7, 9, 11, now.AddSeconds(5));
        ProductDesktopInteractionSelectionController selection =
            ProductDesktopInteractionSelectionController.TryCreate(
                lease, container.ItemIds, now).Controller!;
        ProductDesktopInteractionSurfaceTransactionSnapshot current =
            Transaction(selection.Snapshot);
        var opened = new List<(string ContainerId, string ItemId)>();
        var provider = new WindowsProductDesktopHostUiaRootProvider(
            surface.Handle, display, new nint(911), () => true, () => current,
            (_, request) =>
            {
                ProductDesktopSelectionSnapshot updated = selection.Apply(
                    lease, container.ItemIds, request, now);
                current = Transaction(updated);
                return updated.Status == ProductDesktopSelectionStatus.Applied;
            },
            requestItemOpen: (containerId, itemId) =>
            {
                opened.Add((containerId, itemId));
                return true;
            });
        WindowsProductDesktopHostUiaContainerProvider uiaContainer =
            Assert.Single(provider.Containers);
        WindowsProductDesktopHostUiaItemProvider item =
            Assert.Single(uiaContainer.Items);

        Assert.True(provider.ExplicitSelectionAvailable);
        Assert.True(provider.CanSelectMultiple);
        Assert.False(provider.IsSelectionRequired);
        Assert.NotNull(provider.HostRawElementProvider);
        Assert.True(provider.BoundingRectangle.Contains(
            new System.Windows.Point(124, 236)));
        Assert.Same(provider, provider.FragmentRoot);
        Assert.Same(provider, provider.GetPatternProvider(
            SelectionPatternIdentifiers.Pattern.Id));
        Assert.Null(provider.GetPatternProvider(-1));
        Assert.Equal("Long\u65b9\u683c\u684c\u9762\u53ea\u8bfb\u533a\u57df",
            provider.GetPropertyValue(AutomationElementIdentifiers.NameProperty.Id));
        Assert.Equal("LongGrid.DesktopHost.Root", provider.GetPropertyValue(
            AutomationElementIdentifiers.AutomationIdProperty.Id));
        Assert.Equal(ControlType.Pane.Id, provider.GetPropertyValue(
            AutomationElementIdentifiers.ControlTypeProperty.Id));
        Assert.True((bool)provider.GetPropertyValue(
            AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id)!);
        Assert.True((bool)provider.GetPropertyValue(
            AutomationElementIdentifiers.IsControlElementProperty.Id)!);
        Assert.True((bool)provider.GetPropertyValue(
            AutomationElementIdentifiers.IsContentElementProperty.Id)!);
        Assert.True((bool)provider.GetPropertyValue(
            AutomationElementIdentifiers.IsEnabledProperty.Id)!);
        Assert.NotNull(provider.GetPropertyValue(
            AutomationElementIdentifiers.ItemStatusProperty.Id));
        Assert.Null(provider.GetPropertyValue(-1));
        Assert.Same(uiaContainer, provider.Navigate(NavigateDirection.FirstChild));
        Assert.Same(uiaContainer, provider.Navigate(NavigateDirection.LastChild));
        Assert.Null(provider.Navigate(NavigateDirection.Parent));
        Assert.Null(provider.GetRuntimeId());
        Assert.Null(provider.GetEmbeddedFragmentRoots());
        Assert.Same(item, provider.ElementProviderFromPoint(130, 300));
        Assert.Same(uiaContainer, provider.ElementProviderFromPoint(130, 245));
        Assert.Null(provider.ElementProviderFromPoint(10, 10));
        provider.SetFocus();

        Assert.Null(uiaContainer.HostRawElementProvider);
        Assert.Equal(uiaContainer.BoundingRectangle, provider.BoundingRectangle);
        Assert.Same(provider, uiaContainer.FragmentRoot);
        Assert.Null(uiaContainer.GetPatternProvider(-1));
        Assert.Equal(ControlType.Group.Id, uiaContainer.GetPropertyValue(
            AutomationElementIdentifiers.ControlTypeProperty.Id));
        Assert.Equal(
                "Work；1 个项目；安全引用；未锁定；已展开；标题始终显示；双击标题切换折叠",
            uiaContainer.GetPropertyValue(
                AutomationElementIdentifiers.NameProperty.Id));
        Assert.NotNull(uiaContainer.GetPropertyValue(
            AutomationElementIdentifiers.AutomationIdProperty.Id));
        Assert.True((bool)uiaContainer.GetPropertyValue(
            AutomationElementIdentifiers.IsEnabledProperty.Id)!);
        Assert.False((bool)uiaContainer.GetPropertyValue(
            AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id)!);
        Assert.Equal(
            "只读方格；ContainerHeader:Items=1:Locked=False:Collapsed=False:TitleVisibility=Always:DoubleClick=ToggleCollapsed:Source=SafeReferences",
            uiaContainer.GetPropertyValue(
                AutomationElementIdentifiers.ItemStatusProperty.Id));
        Assert.Null(uiaContainer.GetPropertyValue(-1));
        Assert.Same(provider, uiaContainer.Navigate(NavigateDirection.Parent));
        Assert.Same(item, uiaContainer.Navigate(NavigateDirection.FirstChild));
        Assert.Same(item, uiaContainer.Navigate(NavigateDirection.LastChild));
        Assert.Null(uiaContainer.Navigate(NavigateDirection.NextSibling));
        Assert.Equal(3, uiaContainer.GetRuntimeId().Length);
        Assert.Null(uiaContainer.GetEmbeddedFragmentRoots());
        uiaContainer.SetFocus();

        Assert.Null(item.HostRawElementProvider);
        Assert.Same(provider, item.FragmentRoot);
        Assert.Same(uiaContainer, item.Navigate(NavigateDirection.Parent));
        Assert.Null(item.Navigate(NavigateDirection.PreviousSibling));
        Assert.Null(item.Navigate(NavigateDirection.NextSibling));
        Assert.Equal(3, item.GetRuntimeId().Length);
        Assert.Null(item.GetEmbeddedFragmentRoots());
        Assert.Equal("Plan.docx；文件；类型图标已就绪", item.GetPropertyValue(
            AutomationElementIdentifiers.NameProperty.Id));
        Assert.NotNull(item.GetPropertyValue(
            AutomationElementIdentifiers.AutomationIdProperty.Id));
        Assert.True((bool)item.GetPropertyValue(
            AutomationElementIdentifiers.IsEnabledProperty.Id)!);
        Assert.True((bool)item.GetPropertyValue(
            AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id)!);
        Assert.NotNull(item.GetPropertyValue(
            AutomationElementIdentifiers.ItemStatusProperty.Id));
        Assert.Null(item.GetPropertyValue(-1));
        Assert.Same(item, item.GetPatternProvider(
            SelectionItemPatternIdentifiers.Pattern.Id));
        Assert.Same(item, item.GetPatternProvider(
            InvokePatternIdentifiers.Pattern.Id));
        Assert.False(item.IsSelected);
        item.Invoke();
        provider.PublishSelectionChanges();

        Assert.Equal(("container-1", "item:1"), Assert.Single(opened));
        Assert.True(item.IsSelected);
        item.SetFocus();
        Assert.Same(item, provider.GetFocus());
        Assert.Same(item, Assert.Single(provider.GetSelection()));
        Assert.Equal(ControlType.ListItem.Id, item.GetPropertyValue(
            AutomationElementIdentifiers.ControlTypeProperty.Id));
        item.RemoveFromSelection();
        provider.PublishSelectionChanges();
        Assert.False(item.IsSelected);
        item.AddToSelection();
        provider.PublishSelectionChanges();
        Assert.True(item.IsSelected);

        ProductDesktopPointerSelectionCommand hit = Assert.IsType<
            ProductDesktopPointerSelectionCommand>(
                ProductDesktopPointerSelectionAdapter.Map(
                    display, current, 30, 95, control: true, shift: true));
        Assert.Equal("container-1", hit.ContainerId);
        Assert.Equal("item:1", hit.Request.ItemId);
        Assert.Equal(
            ProductDesktopSelectionModifiers.Control
                | ProductDesktopSelectionModifiers.Shift,
            hit.Request.Modifiers);
        Assert.Null(ProductDesktopPointerSelectionAdapter.Map(
            display, current, 30, 40, control: false, shift: false));
        ProductDesktopPointerSelectionCommand clear = Assert.IsType<
            ProductDesktopPointerSelectionCommand>(
                ProductDesktopPointerSelectionAdapter.Map(
                    display, current, 30, 150,
                    control: false, shift: false));
        Assert.Equal(ProductDesktopSelectionAction.Clear,
            clear.Request.Action);
        Assert.Null(ProductDesktopPointerSelectionAdapter.Map(
            display, current, 0, 95, control: false, shift: false));
        Assert.Null(ProductDesktopPointerSelectionAdapter.Map(
            display, null, 30, 95, control: false, shift: false));
        ProductDesktopHostDisplayProjection collapsed =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary", new(100, 200, 1920, 1040), 96,
                [CreateContainer("container-1", "Work", ["Plan.docx"],
                    true, 24, 36)]);
        Assert.Null(ProductDesktopPointerSelectionAdapter.Map(
            collapsed, current, 30, 95, control: false, shift: false));
        ProductDesktopHostDisplayProjection missingTarget =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary", new(100, 200, 1920, 1040), 96,
                [CreateContainer("container-2", "Other", ["Other.docx"],
                    false, 24, 36)]);
        Assert.Null(ProductDesktopPointerSelectionAdapter.Map(
            missingTarget, current, 30, 95, control: false, shift: false));

        ProductDesktopInteractionSurfaceTransactionSnapshot Transaction(
            ProductDesktopSelectionSnapshot selected) => new(
                ProductDesktopInteractionSurfaceTransactionStatus.Explicit,
                new(
                    ProductDesktopInteractionMode.ExplicitInteraction,
                    ProductDesktopInteractionAdmissionStatus.Admitted,
                    ProductDesktopInteractionCancellationReason.None,
                    lease),
                new(
                    ProductDesktopInteractionSurfaceMode.Explicit,
                    11,
                    Visible: true,
                    HitTestTransparent: false,
                    IsKeyboardFocusable: true,
                    SelectionPatternAvailable: true,
                    ToolWindow: true,
                    NoActivate: true,
                    Topmost: false,
                    HasOwner: false,
                    OwnsForeground: false),
                selected,
                ProductDesktopInteractionSelectionAccessibilityAdapter
                    .CreateExplicit(selected),
                selected.SelectionRevision + 1);
    }

    [Theory]
    [InlineData(0x25, ProductDesktopSelectionAction.MovePrevious)]
    [InlineData(0x26, ProductDesktopSelectionAction.MovePrevious)]
    [InlineData(0x27, ProductDesktopSelectionAction.MoveNext)]
    [InlineData(0x28, ProductDesktopSelectionAction.MoveNext)]
    [InlineData(0x24, ProductDesktopSelectionAction.MoveFirst)]
    [InlineData(0x23, ProductDesktopSelectionAction.MoveLast)]
    [InlineData(0x20, ProductDesktopSelectionAction.MoveNext)]
    public void KeyboardProxyAdapterMapsOnlyFiniteCommands(
        int virtualKey,
        ProductDesktopSelectionAction expected)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var lease = new ProductDesktopInteractionLease(
            Guid.NewGuid(), "container-1", 7, 9, 11, now.AddSeconds(5));
        ProductDesktopSelectionSnapshot selection =
            ProductDesktopInteractionSelectionController.TryCreate(
                lease, ["item:1"], now).Controller!.Snapshot;

        ProductDesktopKeyboardSelectionDecision decision =
            ProductDesktopKeyboardSelectionAdapter.Map(
                selection, virtualKey, control: true, shift: true);

        Assert.False(decision.Cancel);
        Assert.Equal(expected, decision.Request!.Action);
    }

    [Fact]
    public void KeyboardProxyMapsControlAToBoundedSelectAll()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var lease = new ProductDesktopInteractionLease(
            Guid.NewGuid(), "container-1", 7, 9, 11, now.AddSeconds(5));
        ProductDesktopSelectionSnapshot selection =
            ProductDesktopInteractionSelectionController.TryCreate(
                lease, ["item:1", "item:2"], now).Controller!.Snapshot;

        ProductDesktopKeyboardSelectionDecision decision =
            ProductDesktopKeyboardSelectionAdapter.Map(
                selection, 0x41, control: true, shift: false);

        Assert.False(decision.Cancel);
        Assert.Equal(ProductDesktopSelectionAction.SelectAll,
            decision.Request!.Action);
        Assert.Null(ProductDesktopKeyboardSelectionAdapter.Map(
            selection, 0x41, control: false, shift: false).Request);
    }

    [Fact]
    public void KeyboardProxyAdapterCancelsEscapeAndIgnoresUnknownState()
    {
        ProductDesktopKeyboardSelectionDecision escape =
            ProductDesktopKeyboardSelectionAdapter.Map(
                null, 0x1B, control: false, shift: false);
        ProductDesktopKeyboardSelectionDecision unknown =
            ProductDesktopKeyboardSelectionAdapter.Map(
                null, 0x41, control: false, shift: false);

        Assert.True(escape.Cancel);
        Assert.Null(escape.Request);
        Assert.False(unknown.Cancel);
        Assert.Null(unknown.Request);
    }

    [Fact]
    public void RealNativeSurfaceDispatchesFiniteWindowMessageMatrix()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProductDesktopHostReadOnlyProjection container = CreateContainer(
            "container-1", "Work", ["Plan.docx"], false, 24, 36);
        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary", new(100, 200, 1920, 1040), 96,
                [container]);
        using WindowsProductDesktopHostReadOnlySurface surface =
            WindowsProductDesktopHostReadOnlySurface.Create(
                display,
                new nint(916));
        surface.BindContainerHeaderCommand(_ => true);
        surface.BindContainerLayout(_ => true);
        surface.BindItemViewport(_ => true);
        surface.BindWorkspaceCreate(_ => true);

        static nint Point(int x, int y) =>
            new((y << 16) | (x & 0xFFFF));

        Assert.Equal(new nint(3), surface.DispatchWindowMessageForEvidence(
            0x0021, nint.Zero, nint.Zero));
        _ = surface.DispatchWindowMessageForEvidence(
            0x0084, nint.Zero, Point(30, 40));
        _ = surface.DispatchWindowMessageForEvidence(
            0x0200, nint.Zero, Point(30, 40));
        _ = surface.DispatchWindowMessageForEvidence(
            0x0203, nint.Zero, Point(30, 40));
        _ = surface.DispatchWindowMessageForEvidence(
            0x0201, nint.Zero, Point(30, 95));
        _ = surface.DispatchWindowMessageForEvidence(
            0x0202, nint.Zero, Point(30, 95));
        _ = surface.DispatchWindowMessageForEvidence(
            0x020A, new nint(120L << 16), Point(130, 295));
        _ = surface.DispatchWindowMessageForEvidence(
            0x0205, nint.Zero, Point(-1, -1));
        _ = surface.DispatchWindowMessageForEvidence(
            0x02A3, nint.Zero, nint.Zero);
        _ = surface.DispatchWindowMessageForEvidence(
            0x0014, nint.Zero, nint.Zero);
        _ = surface.DispatchWindowMessageForEvidence(
            0x000F, nint.Zero, nint.Zero);
        _ = surface.DispatchWindowMessageForEvidence(
            0x0312, new nint(0x4C47), nint.Zero);
        Assert.False(surface.ApplyContainerLayoutKeyboardFocus("container-1"));
        Assert.True(surface.ApplyContainerLayoutKeyboardFocus(null));

        Assert.True(surface.ApplyExplicit());
        Assert.True(surface.ApplyContainerLayoutKeyboardFocus("container-1"));
        Assert.False(surface.ApplyContainerLayoutKeyboardFocus("missing"));
        Assert.True(surface.ApplyContainerLayoutKeyboardFocus(null));
        _ = surface.DispatchWindowMessageForEvidence(
            0x0084, nint.Zero, Point(30, 40));
        _ = surface.DispatchWindowMessageForEvidence(
            0x0201, nint.Zero, Point(30, 40));
        _ = surface.DispatchWindowMessageForEvidence(
            0x0200, nint.Zero, Point(50, 60));
        _ = surface.DispatchWindowMessageForEvidence(
            0x0202, nint.Zero, Point(50, 60));
        _ = surface.DispatchWindowMessageForEvidence(
            0x0100, new nint(0x1B), nint.Zero);
        _ = surface.DispatchWindowMessageForEvidence(
            0x001F, nint.Zero, nint.Zero);
        _ = surface.DispatchWindowMessageForEvidence(
            0x0215, nint.Zero, nint.Zero);

        Assert.True(surface.ApplyHidden());
        Assert.Equal(new nint(-1), surface.DispatchWindowMessageForEvidence(
            0x0084, nint.Zero, Point(30, 40)));
    }

    [Fact]
    public void RealActivationSourceRoutesFiniteKeyboardCommandMatrix()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ProductDesktopHostReadOnlyProjection container = CreateContainer(
            "container-1", "Work", ["Plan.docx", "Notes.txt"],
            false, 24, 36);
        ProductDesktopHostDisplayProjection display =
            ProductDesktopHostDisplayProjection.Create(
                "display-primary", new(100, 200, 1920, 1040), 96,
                [container]);
        using WindowsProductDesktopInteractionActivationSource source =
            WindowsProductDesktopInteractionActivationSource.Create(
                display,
                new nint(917),
                _ => true);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var lease = new ProductDesktopInteractionLease(
            Guid.NewGuid(), "container-1", 7, 9, 11, now.AddSeconds(5));
        ProductDesktopInteractionSelectionController selection =
            ProductDesktopInteractionSelectionController.TryCreate(
                lease,
                container.ItemIds,
                now).Controller!;
        ProductDesktopInteractionSurfaceTransactionSnapshot current =
            Transaction(selection.Snapshot);
        var opened = new List<ProductDesktopItemOpenSurfaceInput>();
        var layouts = new List<ProductDesktopContainerLayoutKeyboardCommand>();
        var titleFocus = new List<string?>();
        int cancellations = 0;
        source.BindSelection(
            () => current,
            request =>
            {
                ProductDesktopSelectionSnapshot updated = selection.Apply(
                    lease, container.ItemIds, request, now);
                current = Transaction(updated);
                return updated.Status == ProductDesktopSelectionStatus.Applied;
            },
            () =>
            {
                cancellations++;
                return true;
            });
        source.BindItemOpen(input =>
        {
            opened.Add(input);
            return true;
        });
        source.BindContainerLayout(
            input =>
            {
                layouts.Add(input);
                return true;
            },
            containerId =>
            {
                titleFocus.Add(containerId);
                return true;
            });

        source.SubmitSelectionKeyForEvidence(0x27);
        source.SubmitSelectionKeyForEvidence(0x0D, isAutoRepeat: true);
        source.SubmitSelectionKeyForEvidence(0x09);
        source.SubmitSelectionKeyForEvidence(0x25, alt: true);
        source.SubmitSelectionKeyForEvidence(0x27, alt: true, shift: true);
        source.SubmitSelectionKeyForEvidence(0x09);
        source.SubmitSelectionKeyForEvidence(0x1B);
        source.SubmitSelectionKeyForEvidence(0x41, control: true);
        source.SubmitSelectionKeyForEvidence(0x00);

        Assert.Single(opened);
        Assert.True(opened[0].IsAutoRepeat);
        Assert.Equal(ProductDesktopItemOpenSource.KeyboardEnter,
            opened[0].Source);
        Assert.Equal(2, layouts.Count);
        Assert.Contains("container-1", titleFocus);
        Assert.Equal(1, cancellations);
        Assert.Throws<ArgumentNullException>(() => source.BindItemOpen(null!));
        Assert.Throws<ArgumentNullException>(() =>
            source.BindContainerLayout(null!, _ => true));
        Assert.Throws<ArgumentNullException>(() =>
            source.BindContainerLayout(_ => true, null!));

        ProductDesktopInteractionSurfaceTransactionSnapshot Transaction(
            ProductDesktopSelectionSnapshot selected) => new(
                ProductDesktopInteractionSurfaceTransactionStatus.Explicit,
                new(
                    ProductDesktopInteractionMode.ExplicitInteraction,
                    ProductDesktopInteractionAdmissionStatus.Admitted,
                    ProductDesktopInteractionCancellationReason.None,
                    lease),
                new(
                    ProductDesktopInteractionSurfaceMode.Explicit,
                    11,
                    Visible: true,
                    HitTestTransparent: false,
                    IsKeyboardFocusable: true,
                    SelectionPatternAvailable: true,
                    ToolWindow: true,
                    NoActivate: true,
                    Topmost: false,
                    HasOwner: false,
                    OwnsForeground: false),
                selected,
                ProductDesktopInteractionSelectionAccessibilityAdapter
                    .CreateExplicit(selected),
                selected.SelectionRevision + 1);
    }

    private static ProductDesktopHostReadOnlyProjection CreateContainer(
        string id,
        string title,
        IReadOnlyList<string> items,
        bool collapsed,
        double x,
        double y) =>
        ProductDesktopHostReadOnlyProjection.Create(
            id,
            title,
            items,
            "#2457D6",
            0.82,
            collapsed,
            x,
            y,
            360,
            240);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
