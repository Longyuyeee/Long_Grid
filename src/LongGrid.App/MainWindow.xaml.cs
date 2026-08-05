using LongGrid.Core.DesktopHost;
using LongGrid.Core.FileOperations;
using LongGrid.Core.Runtime;
using LongGrid.Infrastructure.Configuration;
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
    private ProductConfigurationStartupMode _configurationStartupMode;
    private ProductConfigurationEvidenceInventory? _configurationEvidenceInventory;
    private readonly Func<
        ProductConfigurationRecoveryAction,
        Task<ProductConfigurationStartupState>> _recoverConfiguration;
    private readonly Func<Task<ProductConfigurationImportPlan?>> _prepareConfigurationImport;
    private readonly Func<
        ProductConfigurationImportPlan,
        Task<ProductConfigurationStartupState>> _commitConfigurationImport;
    private readonly Func<Task<ProductConfigurationExportPlan>> _prepareConfigurationExport;
    private readonly Func<
        ProductConfigurationExportPlan,
        Task<ProductConfigurationExportResult?>> _exportConfiguration;
    private readonly Func<Task<ProductConfigurationEvidenceInventory>>
        _loadConfigurationEvidence;
    private readonly Func<
        ProductConfigurationEvidenceItem,
        Task<ProductConfigurationExportResult?>> _exportConfigurationEvidence;

    public MainWindow(
        Func<
            ProductConfigurationRecoveryAction,
            Task<ProductConfigurationStartupState>> recoverConfiguration,
        Func<Task<ProductConfigurationImportPlan?>> prepareConfigurationImport,
        Func<
            ProductConfigurationImportPlan,
            Task<ProductConfigurationStartupState>> commitConfigurationImport,
        Func<Task<ProductConfigurationExportPlan>> prepareConfigurationExport,
        Func<
            ProductConfigurationExportPlan,
            Task<ProductConfigurationExportResult?>> exportConfiguration,
        Func<Task<ProductConfigurationEvidenceInventory>> loadConfigurationEvidence,
        Func<
            ProductConfigurationEvidenceItem,
            Task<ProductConfigurationExportResult?>> exportConfigurationEvidence)
    {
        ArgumentNullException.ThrowIfNull(recoverConfiguration);
        ArgumentNullException.ThrowIfNull(prepareConfigurationImport);
        ArgumentNullException.ThrowIfNull(commitConfigurationImport);
        ArgumentNullException.ThrowIfNull(prepareConfigurationExport);
        ArgumentNullException.ThrowIfNull(exportConfiguration);
        ArgumentNullException.ThrowIfNull(loadConfigurationEvidence);
        ArgumentNullException.ThrowIfNull(exportConfigurationEvidence);
        _recoverConfiguration = recoverConfiguration;
        _prepareConfigurationImport = prepareConfigurationImport;
        _commitConfigurationImport = commitConfigurationImport;
        _prepareConfigurationExport = prepareConfigurationExport;
        _exportConfiguration = exportConfiguration;
        _loadConfigurationEvidence = loadConfigurationEvidence;
        _exportConfigurationEvidence = exportConfigurationEvidence;
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

    internal void ApplyConfigurationStartupState(
        ProductConfigurationStartupState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _configurationStartupMode = state.Mode;
        ConfigurationRecoveryActionButton.Visibility = Visibility.Collapsed;
        ConfigurationRecoveryActionButton.IsEnabled = true;

        AutomationProperties.SetItemStatus(
            ConfigurationRecoveryBanner,
            $"{state.Mode}:{state.PrimaryFailure}:{state.BackupFailure}");

        switch (state.Mode)
        {
            case ProductConfigurationStartupMode.NoSavedConfiguration:
                ConfigurationRecoveryBanner.Severity = InfoBarSeverity.Informational;
                ConfigurationRecoveryBanner.Title = "尚无保存配置";
                ConfigurationRecoveryBanner.Message =
                    "本次启动没有创建配置目录或文件；当前继续使用安全只读界面。";
                break;
            case ProductConfigurationStartupMode.LoadedPrimary:
                ConfigurationRecoveryBanner.Severity = InfoBarSeverity.Success;
                ConfigurationRecoveryBanner.Title = "配置已安全读取";
                ConfigurationRecoveryBanner.Message =
                    "配置内容已通过校验；当前开发期界面仍不会自动写回或移动文件。";
                break;
            case ProductConfigurationStartupMode.RecoveredBackupReadOnly:
                ConfigurationRecoveryBanner.Severity = InfoBarSeverity.Warning;
                ConfigurationRecoveryBanner.Title = "已从备份只读恢复设置";
                ConfigurationRecoveryBanner.Message =
                    $"主配置{DescribeConfigurationFailure(state.PrimaryFailure)}。" +
                    "Long方格已采用上次有效备份，但不会自动覆盖损坏证据。";
                ConfigurationRecoveryActionButton.Content = "检查并接受备份";
                AutomationProperties.SetName(
                    ConfigurationRecoveryActionButton,
                    "检查并接受已验证备份");
                ConfigurationRecoveryActionButton.Visibility = Visibility.Visible;
                break;
            case ProductConfigurationStartupMode.SafeMode:
                ConfigurationRecoveryBanner.Severity = InfoBarSeverity.Error;
                ConfigurationRecoveryBanner.Title = "已进入配置安全模式";
                ConfigurationRecoveryBanner.Message =
                    $"主配置{DescribeConfigurationFailure(state.PrimaryFailure)}，" +
                    $"备份{DescribeConfigurationFailure(state.BackupFailure)}。" +
                    "当前没有加载或覆盖任何配置；请查看“安全边界”页。";
                ConfigurationRecoveryActionButton.Content = "检查安全重置";
                AutomationProperties.SetName(
                    ConfigurationRecoveryActionButton,
                    "检查配置安全重置");
                ConfigurationRecoveryActionButton.Visibility = Visibility.Visible;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }
    }

    private async void ConfigurationRecoveryActionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ConfigurationRecoveryActionButton.IsEnabled = false;
        try
        {
            ProductConfigurationRecoveryAction action = _configurationStartupMode switch
            {
                ProductConfigurationStartupMode.RecoveredBackupReadOnly =>
                    ProductConfigurationRecoveryAction.AcceptValidatedBackup,
                ProductConfigurationStartupMode.SafeMode =>
                    ProductConfigurationRecoveryAction.ResetSafeMode,
                _ => throw new ProductConfigurationRecoveryException(
                    ProductConfigurationRecoveryError.RecoveryNotAvailable),
            };
            bool resettingSafeMode =
                action is ProductConfigurationRecoveryAction.ResetSafeMode;
            ContentDialog confirmation = new()
            {
                XamlRoot = RootLayout.XamlRoot,
                Title = resettingSafeMode
                    ? "重置为空白安全配置？"
                    : "接受已验证备份？",
                Content = resettingSafeMode
                    ? "Long方格将先把现有损坏主配置和备份分别归档为独立证据，再创建不含容器或桌面项目的空白配置。" +
                        "此操作会写入配置目录，且不能在 Long方格内自动撤销。"
                    : "Long方格将把当前损坏主配置归档为独立证据，并把已经通过校验的备份设为主配置。" +
                        "原备份不会删除；此操作会写入配置目录，且不能在 Long方格内自动撤销。",
                PrimaryButtonText = resettingSafeMode
                    ? "确认安全重置"
                    : "确认接受备份",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };

            ContentDialogResult result = await confirmation.ShowAsync();
            if (result is not ContentDialogResult.Primary)
            {
                return;
            }

            AutomationProperties.SetItemStatus(
                ConfigurationRecoveryBanner,
                resettingSafeMode
                    ? "SafeModeResetInProgress"
                    : "BackupAcceptanceInProgress");
            ProductConfigurationStartupState state = await _recoverConfiguration(action);
            ApplyConfigurationStartupState(state);
            ConfigurationRecoveryBanner.Severity = InfoBarSeverity.Success;
            ConfigurationRecoveryBanner.Title = resettingSafeMode
                ? "已创建空白安全配置"
                : "已接受备份";
            ConfigurationRecoveryBanner.Message = resettingSafeMode
                ? "空白配置已通过校验；原有损坏主配置和备份均已按实际存在情况分别保留为证据。"
                : "已验证备份现在是主配置；原损坏主配置已作为独立证据保留，原备份未删除。";
            AutomationProperties.SetItemStatus(
                ConfigurationRecoveryBanner,
                resettingSafeMode
                    ? "SafeModeReset:DamagedEvidenceArchived"
                    : "BackupAccepted:DamagedPrimaryArchived");
        }
        catch (ProductConfigurationRecoveryException exception)
        {
            if (exception.Error is ProductConfigurationRecoveryError.RecoveryNotAvailable)
            {
                ConfigurationRecoveryActionButton.Visibility = Visibility.Collapsed;
            }

            ConfigurationRecoveryBanner.Severity = InfoBarSeverity.Error;
            ConfigurationRecoveryBanner.Title = "未能接受备份";
            ConfigurationRecoveryBanner.Message = DescribeRecoveryFailure(exception.Error);
            AutomationProperties.SetItemStatus(
                ConfigurationRecoveryBanner,
                $"BackupAcceptanceFailed:{exception.Error}");
        }
        finally
        {
            ConfigurationRecoveryActionButton.IsEnabled = true;
        }
    }

    private static string DescribeRecoveryFailure(
        ProductConfigurationRecoveryError error) => error switch
        {
            ProductConfigurationRecoveryError.ConfirmationRequired =>
                "操作未获得明确确认，没有修改任何配置。",
            ProductConfigurationRecoveryError.RecoveryNotAvailable =>
                "恢复状态已经变化，没有执行写入；请重新启动后再检查。",
            ProductConfigurationRecoveryError.WriteLeaseUnavailable =>
                "其他 Long方格进程正在使用配置，没有执行写入；请稍后重试。",
            ProductConfigurationRecoveryError.IoFailure =>
                "暂时无法安全完成配置操作；损坏证据没有被静默丢弃，请稍后重试。",
            _ => "配置操作未完成；损坏证据没有被静默丢弃。",
        };

    private static string DescribeConfigurationFailure(
        ProductConfigurationStorageFailure failure) => failure switch
        {
            ProductConfigurationStorageFailure.None => "可用",
            ProductConfigurationStorageFailure.Missing => "不存在",
            ProductConfigurationStorageFailure.Empty => "为空",
            ProductConfigurationStorageFailure.TooLarge => "超出安全大小上限",
            ProductConfigurationStorageFailure.InvalidConfiguration => "未通过内容校验",
            ProductConfigurationStorageFailure.IoFailure => "暂时无法读取",
            _ => "处于未知状态",
        };

    private async void ImportConfigurationButton_Click(object sender, RoutedEventArgs e)
    {
        ImportConfigurationButton.IsEnabled = false;
        try
        {
            SetImportStatus("正在等待选择本地 JSON 配置……", "ImportPickerOpen");
            ProductConfigurationImportPlan? plan = await _prepareConfigurationImport();
            if (plan is null)
            {
                SetImportStatus("已取消选择，没有读取或修改配置。", "ImportCancelled");
                return;
            }

            ProductConfigurationImportPreview preview = plan.Preview;
            SetImportStatus(
                $"已验证 v{preview.SchemaVersion}：{preview.ContainerCount} 个容器、" +
                $"{preview.ItemCount} 个引用项目。",
                "ImportPreviewValidated");
            ContentDialog confirmation = new()
            {
                XamlRoot = RootLayout.XamlRoot,
                Title = "导入这份已验证配置？",
                Content =
                    $"配置使用 v{preview.SchemaVersion}，包含 {preview.ContainerCount} 个容器和 " +
                    $"{preview.ItemCount} 个引用项目。{DescribeImportReplacement(preview.ExistingState)}" +
                    "现存主配置和损坏备份会按实际状态分别归档；此操作会写入配置目录，" +
                    "且不能在 Long方格内自动撤销。",
                PrimaryButtonText = "确认导入",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };

            ContentDialogResult confirmationResult = await confirmation.ShowAsync();
            if (confirmationResult is not ContentDialogResult.Primary)
            {
                SetImportStatus("已取消导入，没有修改配置。", "ImportCancelledAfterPreview");
                return;
            }

            SetImportStatus("正在复核配置状态并安全导入……", "ImportCommitInProgress");
            ProductConfigurationStartupState state =
                await _commitConfigurationImport(plan);
            ApplyConfigurationStartupState(state);
            SetImportStatus(
                "配置已导入并通过复读校验；原配置证据已按实际状态保留。",
                "ImportCommitted:EvidencePreserved");
        }
        catch (ProductConfigurationImportException exception)
        {
            SetImportStatus(
                DescribeImportFailure(exception.Error),
                $"ImportFailed:{exception.Error}");
        }
        finally
        {
            ImportConfigurationButton.IsEnabled = true;
        }
    }

    private void SetImportStatus(string text, string automationStatus)
    {
        ConfigurationImportStatus.Text = text;
        AutomationProperties.SetItemStatus(ConfigurationImportStatus, automationStatus);
    }

    private static string DescribeImportReplacement(
        ProductConfigurationImportExistingState state) => state switch
        {
            ProductConfigurationImportExistingState.NoSavedConfiguration =>
                "当前没有已保存配置；确认后会创建首份主配置。",
            ProductConfigurationImportExistingState.LoadedPrimary =>
                "当前主配置会被替换并归档。",
            ProductConfigurationImportExistingState.RecoveredBackupReadOnly =>
                "当前损坏主配置会归档，已验证备份保持不变。",
            ProductConfigurationImportExistingState.SafeMode =>
                "当前处于安全模式，现存损坏主配置和备份会分别归档。",
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

    private static string DescribeImportFailure(
        ProductConfigurationImportError error) => error switch
        {
            ProductConfigurationImportError.SourceNotUserSelected =>
                "导入来源没有获得用户选择授权，没有读取或写入配置。",
            ProductConfigurationImportError.UnsupportedFileType =>
                "仅支持用户选择的 .json 配置文件。",
            ProductConfigurationImportError.NonLocalSource =>
                "当前切片仅允许本地文件系统来源，不读取云端或虚拟提供程序来源。",
            ProductConfigurationImportError.ReparsePointNotAllowed =>
                "为避免链接跳转到未授权位置，不能导入重解析点来源。",
            ProductConfigurationImportError.EmptyDocument =>
                "所选配置为空，没有执行写入。",
            ProductConfigurationImportError.DocumentTooLarge =>
                "所选配置超过 4 MiB 安全上限，没有执行写入。",
            ProductConfigurationImportError.InvalidConfiguration =>
                "所选文件不是当前版本支持的有效 Long方格配置。",
            ProductConfigurationImportError.StoreChanged =>
                "预览后本机配置已经变化，没有覆盖新状态；请重新选择并检查。",
            ProductConfigurationImportError.WriteLeaseUnavailable =>
                "其他 Long方格进程正在使用配置，没有执行导入；请稍后重试。",
            ProductConfigurationImportError.SourceUnavailable =>
                "暂时无法安全读取所选来源，没有执行写入。",
            ProductConfigurationImportError.ConfirmationRequired =>
                "导入未获得明确确认，没有修改配置。",
            ProductConfigurationImportError.IoFailure =>
                "暂时无法安全发布配置；原配置证据没有被静默丢弃。",
            _ => "配置导入未完成，没有静默覆盖现有配置。",
        };

    private async void ExportConfigurationButton_Click(object sender, RoutedEventArgs e)
    {
        ExportConfigurationButton.IsEnabled = false;
        try
        {
            SetExportStatus("正在生成只读导出预览……", "ExportPreparing");
            ProductConfigurationExportPlan plan = await _prepareConfigurationExport();
            ProductConfigurationExportPreview preview = plan.Preview;
            string source = preview.SourceState is
                ProductConfigurationExportSourceState.RecoveredBackupReadOnly
                ? "当前主配置损坏，将导出已验证备份；不会修改损坏证据。"
                : "将导出当前已验证主配置。";
            SetExportStatus(
                $"已验证 v{preview.SchemaVersion}：{preview.ContainerCount} 个容器、" +
                    $"{preview.ItemCount} 个引用项目。",
                "ExportPreviewValidated");
            ContentDialog confirmation = new()
            {
                XamlRoot = RootLayout.XamlRoot,
                Title = "导出这份已验证配置？",
                Content =
                    $"配置使用 v{preview.SchemaVersion}，包含 {preview.ContainerCount} 个容器和 " +
                    $"{preview.ItemCount} 个引用项目。{source}" +
                    "导出文件会包含配置中保存的引用目标。确认后才会请求选择本地文件夹，" +
                    "并创建一个不覆盖既有文件的 JSON 副本。",
                PrimaryButtonText = "确认并选择文件夹",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };
            ContentDialogResult confirmationResult = await confirmation.ShowAsync();
            if (confirmationResult is not ContentDialogResult.Primary)
            {
                SetExportStatus("已取消导出，没有请求文件夹权限或写入文件。", "ExportCancelled");
                return;
            }

            SetExportStatus("正在等待选择本地文件夹……", "ExportFolderPickerOpen");
            ProductConfigurationExportResult? result = await _exportConfiguration(plan);
            if (result is null)
            {
                SetExportStatus("已取消文件夹选择，没有写入文件。", "ExportFolderPickerCancelled");
                return;
            }

            SetExportStatus(
                $"已安全导出为 {result.FileName}；Long方格配置存储未被修改。",
                "ExportCommitted");
        }
        catch (ProductConfigurationExportException exception)
        {
            SetExportStatus(
                DescribeExportFailure(exception.Error),
                $"ExportFailed:{exception.Error}");
        }
        finally
        {
            ExportConfigurationButton.IsEnabled = true;
        }
    }

    private async void RefreshConfigurationEvidenceButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        RefreshConfigurationEvidenceButton.IsEnabled = false;
        try
        {
            SetEvidenceStatus("正在读取归档证据的有限元数据……", "EvidenceLoading");
            ProductConfigurationEvidenceInventory inventory =
                await _loadConfigurationEvidence();
            _configurationEvidenceInventory = inventory;
            ConfigurationEvidenceList.ItemsSource = inventory.Items.Select(item =>
                $"{DescribeEvidenceOrigin(item.Origin)} · {DescribeEvidenceRole(item.Role)} · " +
                $"{FormatEvidenceSize(item.SizeBytes)} · {item.ArchivedUtc.ToLocalTime():yyyy-MM-dd HH:mm}")
                .ToArray();
            ConfigurationEvidenceList.SelectedIndex = -1;
            ExportConfigurationEvidenceButton.IsEnabled = false;
            string suffix = inventory.Truncated
                ? "；清单已达到 256 条安全上限"
                : string.Empty;
            if (inventory.SkippedUnsafeCount > 0)
            {
                suffix += $"；已跳过 {inventory.SkippedUnsafeCount} 个重解析点";
            }

            SetEvidenceStatus(
                inventory.Items.Count == 0
                    ? "没有发现由 Long方格生成的配置归档证据。"
                    : $"已列出 {inventory.Items.Count} 条匿名证据元数据{suffix}。",
                inventory.Truncated ? "EvidenceLoaded:Truncated" : "EvidenceLoaded");
        }
        catch (ProductConfigurationExportException exception)
        {
            _configurationEvidenceInventory = null;
            ConfigurationEvidenceList.ItemsSource = null;
            ExportConfigurationEvidenceButton.IsEnabled = false;
            SetEvidenceStatus(
                DescribeExportFailure(exception.Error),
                $"EvidenceFailed:{exception.Error}");
        }
        finally
        {
            RefreshConfigurationEvidenceButton.IsEnabled = true;
        }
    }

    private void ConfigurationEvidenceList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        int selectedIndex = ConfigurationEvidenceList.SelectedIndex;
        ExportConfigurationEvidenceButton.IsEnabled =
            _configurationEvidenceInventory is not null
            && selectedIndex >= 0
            && selectedIndex < _configurationEvidenceInventory.Items.Count;
        AutomationProperties.SetItemStatus(
            ConfigurationEvidenceList,
            ExportConfigurationEvidenceButton.IsEnabled
                ? "EvidenceSelectedForExplicitExport"
                : "NoEvidenceSelected");
    }

    private async void ExportConfigurationEvidenceButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        int selectedIndex = ConfigurationEvidenceList.SelectedIndex;
        if (_configurationEvidenceInventory is null
            || selectedIndex < 0
            || selectedIndex >= _configurationEvidenceInventory.Items.Count)
        {
            SetEvidenceStatus("请先刷新并选择一条证据。", "EvidenceSelectionRequired");
            return;
        }

        ProductConfigurationEvidenceItem item =
            _configurationEvidenceInventory.Items[selectedIndex];
        ExportConfigurationEvidenceButton.IsEnabled = false;
        try
        {
            ContentDialog confirmation = new()
            {
                XamlRoot = RootLayout.XamlRoot,
                Title = "导出这条原始配置证据？",
                Content =
                    $"所选条目为 {DescribeEvidenceOrigin(item.Origin)} / " +
                    $"{DescribeEvidenceRole(item.Role)}，大小 {FormatEvidenceSize(item.SizeBytes)}。" +
                    "原始证据可能损坏，也可能包含保存过的文件路径、名称或其他私人配置。" +
                    "确认后才会请求选择本地文件夹；Long方格会创建独立 .bin 副本，原证据不会删除或修改。",
                PrimaryButtonText = "确认并选择文件夹",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };
            ContentDialogResult result = await confirmation.ShowAsync();
            if (result is not ContentDialogResult.Primary)
            {
                SetEvidenceStatus(
                    "已取消证据导出，没有请求文件夹权限或写入文件。",
                    "EvidenceExportCancelled");
                return;
            }

            SetEvidenceStatus(
                "正在等待选择本地文件夹……",
                "EvidenceExportFolderPickerOpen");
            ProductConfigurationExportResult? exportResult =
                await _exportConfigurationEvidence(item);
            if (exportResult is null)
            {
                SetEvidenceStatus(
                    "已取消文件夹选择，没有写入文件。",
                    "EvidenceExportFolderPickerCancelled");
                return;
            }

            SetEvidenceStatus(
                $"已安全导出为 {exportResult.FileName}；原证据保持不变。",
                "EvidenceExportCommitted:SourcePreserved");
        }
        catch (ProductConfigurationExportException exception)
        {
            SetEvidenceStatus(
                DescribeExportFailure(exception.Error),
                $"EvidenceExportFailed:{exception.Error}");
        }
        finally
        {
            ExportConfigurationEvidenceButton.IsEnabled =
                _configurationEvidenceInventory is not null
                && selectedIndex == ConfigurationEvidenceList.SelectedIndex;
        }
    }

    private void SetExportStatus(string text, string automationStatus)
    {
        ConfigurationExportStatus.Text = text;
        AutomationProperties.SetItemStatus(ConfigurationExportStatus, automationStatus);
    }

    private void SetEvidenceStatus(string text, string automationStatus)
    {
        ConfigurationEvidenceStatus.Text = text;
        AutomationProperties.SetItemStatus(ConfigurationEvidenceStatus, automationStatus);
    }

    private static string DescribeExportFailure(
        ProductConfigurationExportError error) => error switch
        {
            ProductConfigurationExportError.ConfirmationRequired =>
                "导出尚未获得明确确认，没有写入文件。",
            ProductConfigurationExportError.ExportNotAvailable =>
                "当前没有可导出的已验证配置；安全模式不会导出损坏内容。",
            ProductConfigurationExportError.DestinationNotUserSelected =>
                "导出位置没有获得用户选择授权，没有写入文件。",
            ProductConfigurationExportError.NonLocalDestination =>
                "当前阶段仅支持本地文件系统文件夹。",
            ProductConfigurationExportError.ReparsePointNotAllowed =>
                "为避免链接跳转到未授权位置，不能使用重解析点文件夹。",
            ProductConfigurationExportError.StoreChanged =>
                "预览后配置状态发生变化；请重新开始导出。",
            ProductConfigurationExportError.EvidenceNotAvailable =>
                "所选证据已不存在或不再属于可导出的 Long方格归档；请刷新清单。",
            ProductConfigurationExportError.EvidenceChanged =>
                "所选证据在刷新后发生变化；没有导出，请重新刷新清单。",
            ProductConfigurationExportError.EvidenceTooLarge =>
                "所选证据超过 64 MiB 单次导出上限，没有写入目标文件。",
            ProductConfigurationExportError.EvidenceVerificationFailed =>
                "证据副本未通过逐字节完整性验证，没有发布目标文件。",
            ProductConfigurationExportError.DestinationUnavailable =>
                "所选文件夹暂时不可用，没有覆盖任何文件。",
            ProductConfigurationExportError.IoFailure =>
                "配置或证据元数据暂时无法读取；没有暴露文件内容。",
            _ => "导出未完成，没有覆盖既有文件。",
        };

    private static string DescribeEvidenceOrigin(
        ProductConfigurationEvidenceOrigin origin) => origin switch
        {
            ProductConfigurationEvidenceOrigin.DamagedRecovery => "恢复归档",
            ProductConfigurationEvidenceOrigin.ImportPrevious => "导入前归档",
            _ => "配置归档",
        };

    private static string DescribeEvidenceRole(
        ProductConfigurationEvidenceRole role) => role switch
        {
            ProductConfigurationEvidenceRole.Primary => "主配置",
            ProductConfigurationEvidenceRole.Backup => "备份",
            _ => "未知角色",
        };

    private static string FormatEvidenceSize(long sizeBytes) => sizeBytes < 1024
        ? $"{sizeBytes} B"
        : $"{sizeBytes / 1024d:F1} KB";

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
