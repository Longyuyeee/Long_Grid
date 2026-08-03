using LongGrid.Core.DesktopHost;
using LongGrid.Core.FileOperations;
using LongGrid.Core.Runtime;
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
    private string _organizationStartChoice = "suggested";
    private bool _practiceItemsAdded;

    public MainWindow()
    {
        InitializeComponent();
        RootLayout.Loaded += RootLayout_Loaded;
        RootLayout.SizeChanged += RootLayout_SizeChanged;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();

        ApplyRuntimeStatus(RuntimeStatusSnapshot.CreateDevelopmentReadOnly());
        ApplyStartChoice(_organizationStartChoice);
        SafeReferenceMode.IsChecked = true;
        ApplyOrganizationMode(FileOrganizationMode.SafeReference);
        ShellNavigation.SelectedItem = ShellNavigation.MenuItems[0];
    }

    private void ApplyRuntimeStatus(RuntimeStatusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        CurrentModeValue.Text = snapshot.IsDevelopmentReadOnly
            ? "Core 只读已接线"
            : "状态不可用";
        CurrentModeDetail.Text = snapshot.DesktopCatalog switch
        {
            RuntimeCapabilityState.Disconnected => "桌面目录保持未连接",
            _ => "桌面目录状态不可用",
        };
        AutomationProperties.SetItemStatus(
            CurrentModeValue,
            snapshot.IsDevelopmentReadOnly
                ? "DevelopmentReadOnly"
                : "Unavailable");

        FileOperationValue.Text = snapshot.FileOperations switch
        {
            RuntimeCapabilityState.DisabledBySafetyPolicy => "安全策略关闭",
            _ => "状态不可用",
        };
        FileOperationDetail.Text = "没有移动、删除或写入入口";
        AutomationProperties.SetItemStatus(
            FileOperationValue,
            snapshot.FileOperations.ToString());

        DesktopHostValue.Text = snapshot.DesktopHost switch
        {
            RuntimeCapabilityState.Disconnected => "未连接",
            _ => "状态不可用",
        };
        DesktopHostDetail.Text = "不会创建宿主或影响 Explorer";
        AutomationProperties.SetItemStatus(
            DesktopHostValue,
            snapshot.DesktopHost.ToString());
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

        StartChoiceGrid.ColumnSpacing = compact ? 0 : 12;
        StartChoiceGrid.RowSpacing = compact ? 8 : 0;
        SetGridPosition(
            SuggestedStartChoice,
            row: 0,
            column: 0,
            columnSpan: compact ? 2 : 1);
        SetGridPosition(
            BlankStartChoice,
            row: compact ? 1 : 0,
            column: compact ? 0 : 1,
            columnSpan: compact ? 2 : 1);

        OrganizationModeGrid.ColumnSpacing = compact ? 0 : 14;
        OrganizationModeGrid.RowSpacing = compact ? 12 : 0;
        SetGridPosition(
            SafeReferenceModeCard,
            row: 0,
            column: 0,
            columnSpan: compact ? 2 : 1);
        SetGridPosition(
            ManagedMoveModeCard,
            row: compact ? 1 : 0,
            column: compact ? 0 : 1,
            columnSpan: compact ? 2 : 1);

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
        FirstRunPanel.Visibility = tag == "first-run" ? Visibility.Visible : Visibility.Collapsed;
        RecoveryPanel.Visibility = tag == "recovery" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePanel.Visibility = tag == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        SafetyPanel.Visibility = tag == "safety" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StartChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string choice })
        {
            ApplyStartChoice(choice);
        }
    }

    private void ApplyStartChoice(string choice)
    {
        _organizationStartChoice = choice == "blank" ? "blank" : "suggested";
        StartChoiceStatus.Text = _organizationStartChoice == "blank"
            ? "当前：从空白开始，不创建任何容器。"
            : "当前：一键建议，只生成匿名预览。";
        AutomationProperties.SetItemStatus(
            StartChoiceStatus,
            _organizationStartChoice == "blank"
                ? "BlankStartSelected"
                : "SuggestedStartSelected");
        OrganizationPreviewStatus.Text = "尚未生成预览；尚未修改任何文件。";
        AutomationProperties.SetItemStatus(
            OrganizationPreviewStatus,
            "NotGenerated");
    }

    private void OrganizationMode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string mode } ||
            OrganizationPreviewStatus is null)
        {
            return;
        }

        ApplyOrganizationMode(mode == "managed-move"
            ? FileOrganizationMode.ManagedMove
            : FileOrganizationMode.SafeReference);
    }

    private void ApplyOrganizationMode(FileOrganizationMode mode)
    {
        (OrganizationOutcomeTitle.Text,
            OrganizationOutcomeDetail.Text,
            OrganizationPreviewButton.Content) = mode switch
            {
                FileOrganizationMode.SafeReference => (
                    "添加引用，不移动文件",
                    "Long方格只保存匿名组织关系；原文件位置不变，原生桌面图标可能继续显示。",
                    "生成安全引用预览"),
                FileOrganizationMode.ManagedMove => (
                    "移动文件，必须再次确认",
                    "正式版本必须先列出源、目标、冲突和撤销边界；当前开发原型不具备执行能力。",
                    "查看移动前置条件"),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };

        OrganizationPreviewButton.Tag = mode;
        OrganizationPreviewStatus.Text = "尚未生成预览；尚未修改任何文件。";
        AutomationProperties.SetItemStatus(
            OrganizationPreviewStatus,
            mode == FileOrganizationMode.SafeReference
                ? "SafeReferenceSelected"
                : "ManagedMoveSelected");
    }

    private void OrganizationPreviewButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        FileOrganizationMode mode = OrganizationPreviewButton.Tag is FileOrganizationMode selected
            ? selected
            : FileOrganizationMode.SafeReference;

        if (mode == FileOrganizationMode.SafeReference)
        {
            OrganizationPreviewStatus.Text = _organizationStartChoice == "blank"
                ? "预览：从空白布局开始；不会创建容器或移动文件。尚未修改任何文件。"
                : "预览：将建议 4 个匿名引用；原始文件保持原位。尚未修改任何文件。";
            AutomationProperties.SetItemStatus(
                OrganizationPreviewStatus,
                "SafeReferencePreview");
            return;
        }

        OrganizationPreviewStatus.Text =
            "预览已阻断：缺少真实源、目标、冲突检查和明确批准。尚未修改任何文件。";
        AutomationProperties.SetItemStatus(
            OrganizationPreviewStatus,
            "ManagedMovePreviewBlocked");
    }

    private void CreatePracticeContainerButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        string name = PracticeContainerName.Text.Trim();
        if (name.Length == 0)
        {
            PracticeActivityStatus.Text = "请输入方格名称；尚未创建关系，尚未修改任何文件。";
            AutomationProperties.SetItemStatus(
                PracticeActivityStatus,
                "PracticeContainerNameRequired");
            return;
        }

        PracticeContainerName.Text = name;
        PracticeContainerNameValue.Text = name;
        PracticeContainerCountValue.Text = "0 个匿名引用 · 仅内存";
        PracticeItemsList.Visibility = Visibility.Collapsed;
        PracticeContainerPreview.Visibility = Visibility.Visible;
        _practiceItemsAdded = false;
        AddPracticeItemsButton.IsEnabled = true;
        UndoPracticeContainerButton.IsEnabled = true;
        UndoPracticeContainerButton.Content = "撤销创建（Ctrl+Z）";
        DropSafeReferenceButton.IsEnabled = true;
        DropReassignButton.IsEnabled = true;
        DropManagedMoveButton.IsEnabled = true;
        DropActionStatus.Text = "选择一个来源与目标，查看动作徽标；尚未修改任何文件。";
        AutomationProperties.SetItemStatus(
            DropActionStatus,
            "DropPracticeReady");
        PracticeActivityStatus.Text =
            "已创建匿名方格，仅改变当前原型中的内存关系；可以立即撤销，尚未修改任何文件。";
        AutomationProperties.SetItemStatus(
            PracticeActivityStatus,
            "PracticeContainerCreated");
    }

    private void AddPracticeItemsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        PracticeItemsList.Visibility = Visibility.Visible;
        PracticeContainerCountValue.Text = "3 个匿名引用 · 仅内存";
        _practiceItemsAdded = true;
        AddPracticeItemsButton.IsEnabled = false;
        UndoPracticeContainerButton.Content = "撤销添加（Ctrl+Z）";
        PracticeActivityStatus.Text =
            "已添加 3 个匿名引用；只改变当前原型中的组织关系，没有读取或移动文件。";
        AutomationProperties.SetItemStatus(
            PracticeActivityStatus,
            "PracticeItemsAdded");
    }

    private void UndoPracticeContainerButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_practiceItemsAdded)
        {
            PracticeItemsList.Visibility = Visibility.Collapsed;
            PracticeContainerCountValue.Text = "0 个匿名引用 · 仅内存";
            _practiceItemsAdded = false;
            AddPracticeItemsButton.IsEnabled = true;
            UndoPracticeContainerButton.Content = "撤销创建（Ctrl+Z）";
            PracticeActivityStatus.Text =
                "已撤销添加 3 个匿名引用；方格仍然存在，没有文件被移动或删除。";
            AutomationProperties.SetItemStatus(
                PracticeActivityStatus,
                "PracticeItemsUndone");
            return;
        }

        PracticeContainerPreview.Visibility = Visibility.Collapsed;
        PracticeContainerNameValue.Text = string.Empty;
        AddPracticeItemsButton.IsEnabled = false;
        UndoPracticeContainerButton.IsEnabled = false;
        DropSafeReferenceButton.IsEnabled = false;
        DropReassignButton.IsEnabled = false;
        DropManagedMoveButton.IsEnabled = false;
        DropActionStatus.Text = "先创建匿名方格，再练习判断拖放语义。";
        AutomationProperties.SetItemStatus(
            DropActionStatus,
            "DropPracticeUnavailable");
        PracticeActivityStatus.Text =
            "已撤销匿名方格关系；没有文件被移动或删除。";
        AutomationProperties.SetItemStatus(
            PracticeActivityStatus,
            "PracticeContainerUndone");
    }

    private void DropPracticeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        string semantics = sender is Button { Tag: string value }
            ? value
            : "unsupported";

        (string statusText, string itemStatus) = semantics switch
        {
            "safe-reference" => (
                "动作徽标：添加引用。原文件保持原位；当前练习不会读取 Explorer 数据。",
                "AddReferenceDropPreview"),
            "reassign" => (
                "动作徽标：改变归属。只改变 Long方格关系，不移动或删除磁盘文件。",
                "ReassignDropPreview"),
            "managed-move" => (
                "动作徽标：移动文件（已阻断）。缺少源、目标、冲突检查和明确确认。",
                "ManagedMoveDropBlocked"),
            _ => (
                "动作徽标：不支持。当前状态保持不变。",
                "UnsupportedDropPreview"),
        };
        DropActionStatus.Text = statusText;
        AutomationProperties.SetItemStatus(DropActionStatus, itemStatus);
    }

    private void RecoveryScenarioButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        LayoutRecoveryStatus status = sender is Button { Tag: "automatic" }
            ? LayoutRecoveryStatus.Automatic
            : sender is Button { Tag: "blocked" }
                ? LayoutRecoveryStatus.Blocked
                : LayoutRecoveryStatus.ReviewRequired;

        ApplyRecoveryScenario(status);
    }

    private void ApplyRecoveryScenario(LayoutRecoveryStatus status)
    {
        (string summaryTitle,
            string diffDetail,
            string safetyDetail,
            string itemStatus) = status switch
            {
                LayoutRecoveryStatus.Automatic => (
                    "拓扑等价 · 可自动恢复",
                    "匿名差异：2 个方格保持原显示区域，0 个方格需要位置纠正。",
                    "正式产品仍需在事务提交后逐项复读；当前原型没有执行能力。",
                    "AutomaticRecoveryPreview"),
                LayoutRecoveryStatus.ReviewRequired => (
                    "需要确认 · 先检查可见性纠正",
                    "匿名差异：2 个方格将映射到主显示区域，1 个方格需要最小可见性纠正。",
                    "确认只记录你理解了差异；当前原型不会移动任何方格。",
                    "ReviewRequiredRecoveryPreview"),
                LayoutRecoveryStatus.Blocked => (
                    "恢复已阻断 · 映射缺失或歧义",
                    "匿名差异：1 个显示区域无法可靠映射；禁止只应用已匹配的部分。",
                    "请保持当前布局、手动映射或等待显示器稳定；当前没有执行恢复。",
                    "BlockedRecoveryPreview"),
                _ => throw new ArgumentOutOfRangeException(nameof(status)),
            };

        RecoverySummaryTitle.Text = summaryTitle;
        RecoveryDiffDetail.Text = diffDetail;
        RecoverySafetyDetail.Text = safetyDetail;
        RecoveryDiffPanel.Visibility = Visibility.Visible;
        ReviewRecoveryButton.IsEnabled = status == LayoutRecoveryStatus.ReviewRequired;
        ExpireRecoveryPreviewButton.IsEnabled = true;
        CancelRecoveryPreviewButton.IsEnabled = true;
        RecoveryPlanStatus.Text = status switch
        {
            LayoutRecoveryStatus.Automatic =>
                "状态：Automatic。仅展示匿名零纠正差异；尚未移动任何方格。",
            LayoutRecoveryStatus.ReviewRequired =>
                "状态：ReviewRequired。必须先查看差异；尚未移动任何方格。",
            LayoutRecoveryStatus.Blocked =>
                "状态：Blocked。禁止部分应用；尚未移动任何方格。",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        AutomationProperties.SetItemStatus(RecoveryPlanStatus, itemStatus);
    }

    private void ReviewRecoveryButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ReviewRecoveryButton.IsEnabled = false;
        RecoveryPlanStatus.Text =
            "已记录你理解了 ReviewRequired 差异；当前原型没有执行恢复，布局保持不变。";
        AutomationProperties.SetItemStatus(
            RecoveryPlanStatus,
            "RecoveryPreviewAcknowledged");
    }

    private void ExpireRecoveryPreviewButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ReviewRecoveryButton.IsEnabled = false;
        ExpireRecoveryPreviewButton.IsEnabled = false;
        RecoveryPlanStatus.Text =
            "预览已过期：检测到新的匿名显示变化；旧差异不能确认，尚未移动任何方格。";
        AutomationProperties.SetItemStatus(
            RecoveryPlanStatus,
            "RecoveryPreviewExpired");
    }

    private void CancelRecoveryPreviewButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        RecoveryDiffPanel.Visibility = Visibility.Collapsed;
        ReviewRecoveryButton.IsEnabled = false;
        ExpireRecoveryPreviewButton.IsEnabled = false;
        CancelRecoveryPreviewButton.IsEnabled = false;
        RecoveryPlanStatus.Text = "已取消恢复预览；当前布局保持不变。";
        AutomationProperties.SetItemStatus(
            RecoveryPlanStatus,
            "RecoveryPreviewCancelled");
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
