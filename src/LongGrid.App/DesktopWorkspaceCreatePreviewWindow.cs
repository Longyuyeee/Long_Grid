using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace LongGrid.App;

internal sealed class DesktopWorkspaceCreatePreviewWindow : Window
{
    private readonly Func<
        string,
        ProductDesktopWorkspaceCreatePreviewSnapshot> evaluateName;
    private readonly TaskCompletionSource<string?> completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TextBox nameEditor;
    private readonly TextBlock placementSummary;
    private readonly TextBlock validation;
    private readonly Button confirmButton;
    private ProductDesktopWorkspaceCreatePreviewSnapshot current;
    private bool activatedOnce;
    private bool completing;

    internal DesktopWorkspaceCreatePreviewWindow(
        ProductDesktopWorkspaceCreatePreviewSnapshot initial,
        PixelRect windowBounds,
        Func<string, ProductDesktopWorkspaceCreatePreviewSnapshot> evaluateName)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(evaluateName);
        if (!initial.CanSubmit || !windowBounds.HasArea)
        {
            throw new ArgumentException(
                "Desktop preview requires a submittable session and finite bounds.");
        }

        current = initial;
        this.evaluateName = evaluateName;
        Title = "预览新方格 · Long方格";
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new DesktopAcrylicBackdrop();

        nameEditor = new()
        {
            Header = "方格名称",
            Text = initial.Name,
            MaxLength = ProductConfigurationLimits.MaximumNameLength,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(
            nameEditor,
            "DesktopWorkspaceCreateInlinePreviewNameEditor");
        AutomationProperties.SetName(nameEditor, "方格名称");

        placementSummary = new()
        {
            Text = DescribePlacement(initial),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78,
        };
        AutomationProperties.SetAutomationId(
            placementSummary,
            "DesktopWorkspaceCreateInlinePreviewPlacementSummary");

        validation = new()
        {
            Text = DescribeFailure(initial.Failure),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 190, 215, 255)),
        };
        AutomationProperties.SetAutomationId(
            validation,
            "DesktopWorkspaceCreateInlinePreviewValidation");
        AutomationProperties.SetLiveSetting(validation, AutomationLiveSetting.Polite);

        confirmButton = new()
        {
            Content = "创建并保存",
            IsEnabled = initial.CanSubmit,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(
            confirmButton,
            "DesktopWorkspaceCreateInlinePreviewConfirmButton");

        var cancelButton = new Button
        {
            Content = "取消",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(
            cancelButton,
            "DesktopWorkspaceCreateInlinePreviewCancelButton");

        var actions = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new() { Width = new GridLength(1, GridUnitType.Star) },
                new() { Width = new GridLength(1, GridUnitType.Star) },
            },
            Children =
            {
                cancelButton,
                confirmButton,
            },
        };
        Grid.SetColumn(confirmButton, 1);

        var content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "新方格",
                    FontSize = 20,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                },
                new TextBlock
                {
                    Text = initial.Request.SelectedReferences is { } selected
                        ? $"在桌面候选位置确认名称；新方格将包含 {selected.ItemIds.Count} 个 Long方格引用。取消不会修改配置或桌面文件。"
                        : "在桌面候选位置确认名称。取消不会创建方格，也不会修改桌面文件。",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.82,
                },
                nameEditor,
                placementSummary,
                validation,
                actions,
            },
        };
        var root = new Border
        {
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(
                Color.FromArgb(150, 124, 168, 255)),
            Background = new SolidColorBrush(
                Color.FromArgb(220, 25, 32, 48)),
            Child = content,
        };
        AutomationProperties.SetAutomationId(
            root,
            "DesktopWorkspaceCreateInlinePreviewRoot");
        AutomationProperties.SetName(root, "桌面新方格预览");
        Content = root;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }
        AppWindow.IsShownInSwitchers = false;
        AppWindow.MoveAndResize(new RectInt32(
            windowBounds.Left,
            windowBounds.Top,
            windowBounds.Width,
            windowBounds.Height));

        nameEditor.TextChanged += (_, _) => ApplyValidation();
        nameEditor.KeyDown += NameEditor_KeyDown;
        confirmButton.Click += (_, _) => Confirm();
        cancelButton.Click += (_, _) => Complete(null);
        root.KeyDown += Root_KeyDown;
        Activated += PreviewWindow_Activated;
        Closed += (_, _) => Complete(null, closeWindow: false);
        ApplyValidation();
    }

    internal Task<string?> ShowAsync()
    {
        Activate();
        return completion.Task;
    }

    internal void Cancel() => Complete(null);

    private void PreviewWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            if (activatedOnce)
            {
                Complete(null);
            }
            return;
        }

        activatedOnce = true;
        _ = nameEditor.Focus(FocusState.Programmatic);
        nameEditor.SelectAll();
    }

    private void NameEditor_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter)
        {
            args.Handled = true;
            Confirm();
        }
        else if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            Complete(null);
        }
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            Complete(null);
        }
    }

    private void Confirm()
    {
        ApplyValidation();
        if (current.CanSubmit)
        {
            Complete(current.Name);
        }
    }

    private void ApplyValidation()
    {
        if (completing)
        {
            return;
        }

        current = evaluateName(nameEditor.Text);
        confirmButton.IsEnabled = current.CanSubmit;
        validation.Text = DescribeFailure(current.Failure);
        placementSummary.Text = DescribePlacement(current);
        AutomationProperties.SetHelpText(nameEditor, validation.Text);
        AutomationProperties.SetItemStatus(
            validation,
            $"DesktopWorkspaceCreateInlinePreview:{current.Status}:" +
                $"Failure={current.Failure}:CanSubmit={current.CanSubmit}:" +
                "ConfigurationChanged=False:DesktopFilesChanged=False");
    }

    private void Complete(string? name, bool closeWindow = true)
    {
        if (completing)
        {
            return;
        }

        completing = true;
        _ = completion.TrySetResult(name);
        if (closeWindow)
        {
            Close();
        }
    }

    private static string DescribePlacement(
        ProductDesktopWorkspaceCreatePreviewSnapshot snapshot)
    {
        ProductContainerPlacementState? placement = snapshot.CandidatePlacement;
        return placement is null
            ? "候选位置不可用；不会提交。"
            : $"位置 {placement.XDip:0}, {placement.YDip:0} DIP · " +
                $"大小 {placement.WidthDip:0} × {placement.HeightDip:0} DIP";
    }

    private static string DescribeFailure(
        ProductDesktopWorkspaceCreatePreviewFailure failure) => failure switch
        {
            ProductDesktopWorkspaceCreatePreviewFailure.None =>
                "名称可用；确认前配置和桌面文件均未改变。",
            ProductDesktopWorkspaceCreatePreviewFailure.InvalidName =>
                "请输入非空、不过长且不含控制字符的名称。",
            ProductDesktopWorkspaceCreatePreviewFailure.DuplicateName =>
                "已有同名方格，请换一个名称。",
            ProductDesktopWorkspaceCreatePreviewFailure.LimitReached =>
                "方格数量已达到上限。",
            ProductDesktopWorkspaceCreatePreviewFailure.PlacementUnavailable =>
                "当前显示器没有可用候选位置。",
            ProductDesktopWorkspaceCreatePreviewFailure.StaleWorkspace =>
                "工作区已变化，本次预览已失效。",
            ProductDesktopWorkspaceCreatePreviewFailure.StaleTopology =>
                "显示器状态已变化，本次预览已失效。",
            ProductDesktopWorkspaceCreatePreviewFailure.StaleSelection =>
                "所选引用已变化，本次预览已取消。",
            ProductDesktopWorkspaceCreatePreviewFailure.DisplayUnavailable =>
                "目标显示器已不可用。",
            ProductDesktopWorkspaceCreatePreviewFailure.HostUnavailable =>
                "桌面方格当前不可用。",
            _ => "本次预览不可提交；没有产生修改。",
        };
}
