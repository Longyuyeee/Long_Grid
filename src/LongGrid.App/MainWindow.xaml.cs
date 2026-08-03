using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace LongGrid.App;

public sealed partial class MainWindow : Window
{
    private const double CompactBreakpoint = 760;
    private const double DefaultWidth = 1180;
    private const double DefaultHeight = 760;
    private const double MaximumWorkAreaFraction = 0.9;
    private bool _initialSizeApplied;

    public MainWindow()
    {
        InitializeComponent();
        RootLayout.Loaded += RootLayout_Loaded;
        RootLayout.SizeChanged += RootLayout_SizeChanged;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();

        ShellNavigation.SelectedItem = ShellNavigation.MenuItems[0];
    }

    private void RootLayout_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialSizeApplied)
        {
            return;
        }

        _initialSizeApplied = true;
        double scale = RootLayout.XamlRoot?.RasterizationScale ?? 1;
        DisplayArea displayArea = DisplayArea.GetFromWindowId(
            AppWindow.Id,
            DisplayAreaFallback.Primary);
        int maximumWidth = (int)Math.Round(
            displayArea.WorkArea.Width * MaximumWorkAreaFraction);
        int maximumHeight = (int)Math.Round(
            displayArea.WorkArea.Height * MaximumWorkAreaFraction);
        int targetWidth = Math.Min(
            (int)Math.Round(DefaultWidth * scale),
            maximumWidth);
        int targetHeight = Math.Min(
            (int)Math.Round(DefaultHeight * scale),
            maximumHeight);

        AppWindow.Resize(new SizeInt32(targetWidth, targetHeight));
    }

    private void RootLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(e.NewSize.Width < CompactBreakpoint);
    }

    private void ApplyResponsiveLayout(bool compact)
    {
        AutomationProperties.SetItemStatus(
            RootLayout,
            compact ? "compact" : "wide");
        ShellNavigation.PaneDisplayMode = compact
            ? NavigationViewPaneDisplayMode.LeftMinimal
            : NavigationViewPaneDisplayMode.LeftCompact;
        ContentFrame.Padding = compact
            ? new Thickness(16, 20, 16, 24)
            : new Thickness(32, 28, 32, 32);
        DevelopmentBadgeText.Text = compact
            ? "只读 · 紧凑布局"
            : "开发期 · 只读 UI Shell";
        ThemeOptions.Orientation = compact
            ? Orientation.Vertical
            : Orientation.Horizontal;

        OverviewMetricsGrid.ColumnSpacing = compact ? 0 : 14;
        OverviewMetricsGrid.RowSpacing = compact ? 12 : 0;
        SetGridPosition(
            CurrentModeCard,
            row: 0,
            column: 0,
            columnSpan: compact ? 3 : 1);
        SetGridPosition(
            FileOperationCard,
            row: compact ? 1 : 0,
            column: compact ? 0 : 1,
            columnSpan: compact ? 3 : 1);
        SetGridPosition(
            DesktopHostCard,
            row: compact ? 2 : 0,
            column: compact ? 0 : 2,
            columnSpan: compact ? 3 : 1);

        WorkspaceItemsGrid.ColumnSpacing = compact ? 0 : 12;
        WorkspaceItemsGrid.RowSpacing = compact ? 8 : 0;
        SetGridPosition(
            WorkspaceItemProject,
            row: 0,
            column: 0,
            columnSpan: compact ? 4 : 1);
        SetGridPosition(
            WorkspaceItemReference,
            row: compact ? 1 : 0,
            column: compact ? 0 : 1,
            columnSpan: compact ? 4 : 1);
        SetGridPosition(
            WorkspaceItemPending,
            row: compact ? 2 : 0,
            column: compact ? 0 : 2,
            columnSpan: compact ? 4 : 1);
        SetGridPosition(
            WorkspaceItemArchive,
            row: compact ? 3 : 0,
            column: compact ? 0 : 3,
            columnSpan: compact ? 4 : 1);

        BrandSurfaceGrid.ColumnSpacing = compact ? 0 : 12;
        BrandSurfaceGrid.RowSpacing = compact ? 12 : 0;
        SetGridPosition(
            BrandPrimarySurface,
            row: 0,
            column: 0,
            columnSpan: compact ? 3 : 1);
        SetGridPosition(
            BrandMutedSurface,
            row: compact ? 1 : 0,
            column: compact ? 0 : 1,
            columnSpan: compact ? 3 : 1);
        SetGridPosition(
            BrandCardSurface,
            row: compact ? 2 : 0,
            column: compact ? 0 : 2,
            columnSpan: compact ? 3 : 1);
    }

    private static void SetGridPosition(
        FrameworkElement element,
        int row,
        int column,
        int columnSpan)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
    }

    private void ShellNavigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItemContainer?.Tag as string) ?? "overview";

        OverviewPanel.Visibility = tag == "overview" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePanel.Visibility = tag == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        SafetyPanel.Visibility = tag == "safety" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ThemeOption_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string theme } || RootLayout is null)
        {
            return;
        }

        (RootLayout.RequestedTheme, ThemeStatusText.Text) = theme switch
        {
            "light" => (ElementTheme.Light, "当前：浅色（仅内存）"),
            "dark" => (ElementTheme.Dark, "当前：深色（仅内存）"),
            _ => (ElementTheme.Default, "当前：跟随系统（仅内存）"),
        };
    }
}
