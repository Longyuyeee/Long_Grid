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

internal enum DesktopSearchOverlayAction
{
    Reveal,
    Open,
    LocateInExplorer,
}

internal sealed record DesktopSearchOverlayOutcome(
    ProductWorkspaceSearchResultPresentation Result,
    DesktopSearchOverlayAction Action);

internal sealed class DesktopSearchOverlayWindow : Window
{
    private readonly long workspaceRevision;
    private readonly IReadOnlyList<ProductWorkspaceSearchContainerInput> containers;
    private readonly string? displayKey;
    private readonly PixelRect workArea;
    private readonly TaskCompletionSource<DesktopSearchOverlayOutcome?>
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TextBox queryEditor;
    private readonly ListView resultList;
    private readonly TextBlock status;
    private readonly Button revealButton;
    private readonly Button openButton;
    private readonly Button locateButton;
    private bool activatedOnce;
    private bool completing;

    internal DesktopSearchOverlayWindow(
        long workspaceRevision,
        IReadOnlyList<ProductWorkspaceSearchContainerInput> containers,
        string? displayKey,
        PixelRect workArea)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workspaceRevision);
        ArgumentNullException.ThrowIfNull(containers);
        if (!workArea.HasArea)
        {
            throw new ArgumentException("Search overlay requires a finite work area.");
        }

        this.workspaceRevision = workspaceRevision;
        this.containers = containers;
        this.displayKey = displayKey;
        this.workArea = workArea;
        Title = "搜索桌面 · Long方格";
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new DesktopAcrylicBackdrop();

        queryEditor = new()
        {
            Header = "搜索盒子或项目",
            MaxLength = ProductWorkspaceSearch.MaximumQueryLength,
            PlaceholderText = "输入名称或类型",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(
            queryEditor,
            "DesktopSearchOverlayQuery");
        AutomationProperties.SetName(queryEditor, "桌面搜索查询");

        status = new()
        {
            Text = "输入盒子名、项目名或类型开始搜索。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78,
        };
        AutomationProperties.SetAutomationId(status, "DesktopSearchOverlayStatus");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);

        resultList = new()
        {
            IsItemClickEnabled = true,
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 360,
        };
        AutomationProperties.SetAutomationId(
            resultList,
            "DesktopSearchOverlayResults");
        AutomationProperties.SetName(resultList, "桌面搜索结果");
        resultList.ItemTemplate = CreateResultTemplate();

        revealButton = CreateActionButton(
            "在桌面显示",
            "DesktopSearchOverlayRevealButton");
        openButton = CreateActionButton(
            "打开",
            "DesktopSearchOverlayOpenButton");
        locateButton = CreateActionButton(
            "在资源管理器中定位",
            "DesktopSearchOverlayLocateButton");

        var closeButton = new Button
        {
            Content = "关闭",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        AutomationProperties.SetAutomationId(
            closeButton,
            "DesktopSearchOverlayCloseButton");

        var content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "搜索桌面",
                    FontSize = 22,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                },
                new TextBlock
                {
                    Text = displayKey is null
                        ? "搜索全部显示器上的盒子和项目。"
                        : "搜索当前显示器上的盒子和项目。",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.82,
                },
                queryEditor,
                status,
                resultList,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        revealButton,
                        openButton,
                        locateButton,
                    },
                },
                closeButton,
            },
        };
        var root = new Border
        {
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(160, 124, 168, 255)),
            Background = new SolidColorBrush(Color.FromArgb(232, 25, 32, 48)),
            Child = content,
        };
        AutomationProperties.SetAutomationId(root, "DesktopSearchOverlayRoot");
        AutomationProperties.SetName(root, "Long方格桌面搜索浮层");
        Content = root;

        queryEditor.TextChanged += (_, _) => ApplySearch();
        queryEditor.KeyDown += QueryEditor_KeyDown;
        resultList.KeyDown += ResultList_KeyDown;
        resultList.SelectionChanged += (_, _) => UpdateActionAvailability();
        resultList.ItemClick += (_, args) => Complete(
            args.ClickedItem as ProductWorkspaceSearchResultPresentation,
            DesktopSearchOverlayAction.Reveal);
        revealButton.Click += (_, _) => CompleteSelected(
            DesktopSearchOverlayAction.Reveal);
        openButton.Click += (_, _) => CompleteSelected(
            DesktopSearchOverlayAction.Open);
        locateButton.Click += (_, _) => CompleteSelected(
            DesktopSearchOverlayAction.LocateInExplorer);
        closeButton.Click += (_, _) => Complete(null);
        root.KeyDown += Root_KeyDown;
        Activated += SearchWindow_Activated;
        Closed += (_, _) => Complete(null, closeWindow: false);
        ApplySearch();
    }

    internal async Task<DesktopSearchOverlayOutcome?> ShowAsync()
    {
        Activate();
        await Task.Delay(120);
        ApplyWindowPresentation();
        return await completion.Task;
    }

    private void ApplyWindowPresentation()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }
        AppWindow.IsShownInSwitchers = false;
        int width = Math.Min(620, workArea.Width);
        int height = Math.Min(540, workArea.Height);
        AppWindow.MoveAndResize(new RectInt32(
            workArea.Left + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Top + Math.Max(0, (workArea.Height - height) / 3),
            width,
            height));
    }

    private void SearchWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }
        if (!activatedOnce)
        {
            activatedOnce = true;
            _ = queryEditor.Focus(FocusState.Programmatic);
        }
    }

    private void ApplySearch()
    {
        if (completing)
        {
            return;
        }
        ProductWorkspaceSearchPresentation presentation =
            ProductWorkspaceSearchPresentation.Create(
                workspaceRevision,
                new(
                    queryEditor.Text,
                    workspaceRevision,
                    ProductWorkspaceSearchTargetFilter.All,
                    ProductWorkspaceSearchItemKindFilter.All,
                    ProductWorkspaceContainerHealthFilter.All,
                    displayKey),
                containers);
        resultList.ItemsSource = presentation.Results;
        resultList.IsEnabled = presentation.Results.Count > 0;
        resultList.SelectedIndex = presentation.Results.Count > 0 ? 0 : -1;
        UpdateActionAvailability();
        status.Text = presentation.Detail;
        AutomationProperties.SetItemStatus(status, presentation.MachineStatus);
    }

    private void QueryEditor_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            Complete(null);
        }
        else if (args.Key is VirtualKey.Down or VirtualKey.Up)
        {
            args.Handled = true;
            MoveSelection(args.Key == VirtualKey.Down ? 1 : -1);
        }
        else if (args.Key == VirtualKey.Enter)
        {
            args.Handled = true;
            CompleteSelected(DesktopSearchOverlayAction.Reveal);
        }
    }

    private void ResultList_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            Complete(null);
        }
        else if (args.Key == VirtualKey.Enter)
        {
            args.Handled = true;
            CompleteSelected(DesktopSearchOverlayAction.Reveal);
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

    private void MoveSelection(int delta)
    {
        int count = resultList.Items.Count;
        if (count == 0)
        {
            return;
        }
        resultList.SelectedIndex = Math.Clamp(
            resultList.SelectedIndex + delta,
            0,
            count - 1);
        resultList.ScrollIntoView(resultList.SelectedItem);
    }

    private void UpdateActionAvailability()
    {
        bool hasResult = resultList.SelectedItem
            is ProductWorkspaceSearchResultPresentation;
        bool hasItem = resultList.SelectedItem
            is ProductWorkspaceSearchResultPresentation { ItemOrdinal: not null };
        revealButton.IsEnabled = hasResult;
        openButton.IsEnabled = hasItem;
        locateButton.IsEnabled = hasItem;
    }

    private void CompleteSelected(DesktopSearchOverlayAction action) =>
        Complete(
            resultList.SelectedItem as ProductWorkspaceSearchResultPresentation,
            action);

    private void Complete(
        ProductWorkspaceSearchResultPresentation? result,
        DesktopSearchOverlayAction action = DesktopSearchOverlayAction.Reveal,
        bool closeWindow = true)
    {
        if (completing)
        {
            return;
        }
        completing = true;
        _ = completion.TrySetResult(result is null ? null : new(result, action));
        if (closeWindow)
        {
            AppWindow.Destroy();
        }
    }

    private static DataTemplate CreateResultTemplate()
    {
        const string xaml = """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <StackPanel Padding="8" Spacing="2">
                    <TextBlock FontWeight="SemiBold" Text="{Binding DisplayName}" TextTrimming="CharacterEllipsis" />
                    <TextBlock FontSize="11" Opacity="0.75" Text="{Binding Detail}" TextWrapping="Wrap" />
                </StackPanel>
            </DataTemplate>
            """;
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }

    private static Button CreateActionButton(string text, string automationId)
    {
        var button = new Button
        {
            Content = text,
            IsEnabled = false,
        };
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }
}
