using LongGrid.Core.Configuration;
using LongGrid.Core.DesktopHost;
using LongGrid.Core.FileOperations;
using LongGrid.Core.Runtime;
using LongGrid.Infrastructure.Configuration;
using LongGrid.Infrastructure.DesktopItems;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
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
    private bool _suppressBatchSelectionAnnouncements;
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
    private readonly Func<
        ProductConfigurationEvidenceItem,
        Task<ProductConfigurationEvidenceRemovalResult>> _removeConfigurationEvidence;
    private readonly Func<ProductWorkspaceSaveRetryResult> _retryProductWorkspaceSave;
    private readonly Func<Task> _refreshProductDesktopCatalog;
    private readonly Func<
        ProductWorkspaceReferenceReviewToken,
        ProductWorkspaceReferenceAction,
        bool,
        ProductWorkspaceReferenceCandidatePresentation?,
        ProductWorkspaceReferenceCommitResult> _commitProductWorkspaceReferenceAction;
    private readonly Func<
        long,
        int,
        IReadOnlyList<ProductWorkspaceResolvedReferenceCandidatePresentation>,
        ProductWorkspaceResolvedReferenceBatchCommitResult>
        _commitProductWorkspaceResolvedReferenceBatch;
    private readonly Func<
        ProductWorkspaceReferenceBatchAdditionUndoToken,
        bool,
        ProductWorkspaceReferenceBatchAdditionUndoCommitResult>
        _commitProductWorkspaceReferenceBatchAdditionUndo;
    private readonly Func<
        long,
        IReadOnlyList<ProductWorkspaceResolvedReferenceRemovalCandidatePresentation>,
        ProductWorkspaceResolvedReferenceBatchRemovalCommitResult>
        _commitProductWorkspaceResolvedReferenceBatchRemoval;
    private readonly Func<
        ProductWorkspaceReferenceRemovalUndoToken,
        bool,
        ProductWorkspaceReferenceRemovalUndoCommitResult>
        _commitProductWorkspaceReferenceRemovalUndo;
    private readonly Func<
        long,
        IReadOnlyList<ProductWorkspaceResolvedReferenceRemovalCandidatePresentation>,
        ProductWorkspaceReferenceReassignmentTargetPresentation,
        ProductWorkspaceResolvedReferenceReassignmentCommitResult>
        _commitProductWorkspaceResolvedReferenceReassignment;
    private readonly Func<
        ProductWorkspaceReferenceReassignmentUndoToken,
        bool,
        ProductWorkspaceReferenceReassignmentUndoCommitResult>
        _commitProductWorkspaceReferenceReassignmentUndo;
    private readonly Func<
        ProductWorkspaceContainerCommitAction,
        long,
        int,
        string,
        bool?,
        ProductWorkspaceContainerColorPreset?,
        ProductWorkspaceContainerOpacityPreset?,
        ProductWorkspaceContainerPositionPreset?,
        ProductWorkspaceContainerSizePreset?,
        bool,
        ProductWorkspaceContainerCommitResult> _commitProductWorkspaceContainerAction;
    private readonly Func<
        ProductWorkspaceContainerRemovalUndoToken,
        bool,
        ProductWorkspaceContainerRemovalUndoCommitResult>
        _commitProductWorkspaceContainerRemovalUndo;
    private readonly Func<
        ProductWorkspaceLayoutRecoveryReviewToken,
        bool,
        ProductWorkspaceLayoutRecoveryCommitResult>
        _commitProductWorkspaceLayoutRecovery;
    private readonly Func<
        ProductWorkspaceLayoutRecoveryUndoToken,
        bool,
        ProductWorkspaceLayoutRecoveryUndoCommitResult>
        _commitProductWorkspaceLayoutRecoveryUndo;
    private ProductWorkspaceReferenceReviewPresentation _referenceReview =
        ProductWorkspaceReferenceReviewPresentation.Unavailable;
    private ProductWorkspaceContainerEditPresentation _containerEditor =
        ProductWorkspaceContainerEditPresentation.Unavailable;
    private ProductWorkspaceReadPresentation _workspaceRead =
        ProductWorkspaceReadPresentation.Unavailable;
    private ProductWorkspaceResolvedReferenceAddPresentation _resolvedReferenceAdd =
        ProductWorkspaceResolvedReferenceAddPresentation.Unavailable;
    private ProductWorkspaceResolvedReferenceRemovalPresentation
        _resolvedReferenceRemoval =
            ProductWorkspaceResolvedReferenceRemovalPresentation.Unavailable;
    private ProductWorkspaceResolvedReferenceReassignmentPresentation
        _resolvedReferenceReassignment =
            ProductWorkspaceResolvedReferenceReassignmentPresentation.Unavailable;
    private ProductWorkspaceLayoutRecoveryPresentation _layoutRecovery =
        ProductWorkspaceLayoutRecoveryPresentation.Create(
            new(
                new(
                    ProductWorkspaceLayoutRecoveryPreviewStatus.UnavailableSession,
                    0,
                    0,
                    0,
                    0,
                    DesktopWindowsChanged: false),
                null));
    private ProductWorkspaceLatestUndoPresentation _latestUndo =
        ProductWorkspaceLatestUndoPresentation.Unavailable;

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
            Task<ProductConfigurationExportResult?>> exportConfigurationEvidence,
        Func<
            ProductConfigurationEvidenceItem,
            Task<ProductConfigurationEvidenceRemovalResult>> removeConfigurationEvidence,
        Func<ProductWorkspaceSaveRetryResult> retryProductWorkspaceSave,
        Func<Task> refreshProductDesktopCatalog,
        Func<
            ProductWorkspaceReferenceReviewToken,
            ProductWorkspaceReferenceAction,
            bool,
            ProductWorkspaceReferenceCandidatePresentation?,
            ProductWorkspaceReferenceCommitResult> commitProductWorkspaceReferenceAction,
        Func<
            long,
            int,
            IReadOnlyList<ProductWorkspaceResolvedReferenceCandidatePresentation>,
            ProductWorkspaceResolvedReferenceBatchCommitResult>
            commitProductWorkspaceResolvedReferenceBatch,
        Func<
            ProductWorkspaceReferenceBatchAdditionUndoToken,
            bool,
            ProductWorkspaceReferenceBatchAdditionUndoCommitResult>
            commitProductWorkspaceReferenceBatchAdditionUndo,
        Func<
            long,
            IReadOnlyList<ProductWorkspaceResolvedReferenceRemovalCandidatePresentation>,
            ProductWorkspaceResolvedReferenceBatchRemovalCommitResult>
            commitProductWorkspaceResolvedReferenceBatchRemoval,
        Func<
            ProductWorkspaceReferenceRemovalUndoToken,
            bool,
            ProductWorkspaceReferenceRemovalUndoCommitResult>
            commitProductWorkspaceReferenceRemovalUndo,
        Func<
            long,
            IReadOnlyList<ProductWorkspaceResolvedReferenceRemovalCandidatePresentation>,
            ProductWorkspaceReferenceReassignmentTargetPresentation,
            ProductWorkspaceResolvedReferenceReassignmentCommitResult>
            commitProductWorkspaceResolvedReferenceReassignment,
        Func<
            ProductWorkspaceReferenceReassignmentUndoToken,
            bool,
            ProductWorkspaceReferenceReassignmentUndoCommitResult>
            commitProductWorkspaceReferenceReassignmentUndo,
        Func<
            ProductWorkspaceContainerCommitAction,
            long,
            int,
            string,
            bool?,
            ProductWorkspaceContainerColorPreset?,
            ProductWorkspaceContainerOpacityPreset?,
            ProductWorkspaceContainerPositionPreset?,
            ProductWorkspaceContainerSizePreset?,
            bool,
            ProductWorkspaceContainerCommitResult> commitProductWorkspaceContainerAction,
        Func<
            ProductWorkspaceContainerRemovalUndoToken,
            bool,
            ProductWorkspaceContainerRemovalUndoCommitResult>
            commitProductWorkspaceContainerRemovalUndo,
        Func<
            ProductWorkspaceLayoutRecoveryReviewToken,
            bool,
            ProductWorkspaceLayoutRecoveryCommitResult>
            commitProductWorkspaceLayoutRecovery,
        Func<
            ProductWorkspaceLayoutRecoveryUndoToken,
            bool,
            ProductWorkspaceLayoutRecoveryUndoCommitResult>
            commitProductWorkspaceLayoutRecoveryUndo)
    {
        ArgumentNullException.ThrowIfNull(recoverConfiguration);
        ArgumentNullException.ThrowIfNull(prepareConfigurationImport);
        ArgumentNullException.ThrowIfNull(commitConfigurationImport);
        ArgumentNullException.ThrowIfNull(prepareConfigurationExport);
        ArgumentNullException.ThrowIfNull(exportConfiguration);
        ArgumentNullException.ThrowIfNull(loadConfigurationEvidence);
        ArgumentNullException.ThrowIfNull(exportConfigurationEvidence);
        ArgumentNullException.ThrowIfNull(removeConfigurationEvidence);
        ArgumentNullException.ThrowIfNull(retryProductWorkspaceSave);
        ArgumentNullException.ThrowIfNull(refreshProductDesktopCatalog);
        ArgumentNullException.ThrowIfNull(commitProductWorkspaceReferenceAction);
        ArgumentNullException.ThrowIfNull(commitProductWorkspaceResolvedReferenceBatch);
        ArgumentNullException.ThrowIfNull(
            commitProductWorkspaceReferenceBatchAdditionUndo);
        ArgumentNullException.ThrowIfNull(
            commitProductWorkspaceResolvedReferenceBatchRemoval);
        ArgumentNullException.ThrowIfNull(commitProductWorkspaceReferenceRemovalUndo);
        ArgumentNullException.ThrowIfNull(
            commitProductWorkspaceResolvedReferenceReassignment);
        ArgumentNullException.ThrowIfNull(
            commitProductWorkspaceReferenceReassignmentUndo);
        ArgumentNullException.ThrowIfNull(commitProductWorkspaceContainerAction);
        ArgumentNullException.ThrowIfNull(
            commitProductWorkspaceContainerRemovalUndo);
        ArgumentNullException.ThrowIfNull(commitProductWorkspaceLayoutRecovery);
        ArgumentNullException.ThrowIfNull(commitProductWorkspaceLayoutRecoveryUndo);
        _recoverConfiguration = recoverConfiguration;
        _prepareConfigurationImport = prepareConfigurationImport;
        _commitConfigurationImport = commitConfigurationImport;
        _prepareConfigurationExport = prepareConfigurationExport;
        _exportConfiguration = exportConfiguration;
        _loadConfigurationEvidence = loadConfigurationEvidence;
        _exportConfigurationEvidence = exportConfigurationEvidence;
        _removeConfigurationEvidence = removeConfigurationEvidence;
        _retryProductWorkspaceSave = retryProductWorkspaceSave;
        _refreshProductDesktopCatalog = refreshProductDesktopCatalog;
        _commitProductWorkspaceReferenceAction =
            commitProductWorkspaceReferenceAction;
        _commitProductWorkspaceResolvedReferenceBatch =
            commitProductWorkspaceResolvedReferenceBatch;
        _commitProductWorkspaceReferenceBatchAdditionUndo =
            commitProductWorkspaceReferenceBatchAdditionUndo;
        _commitProductWorkspaceResolvedReferenceBatchRemoval =
            commitProductWorkspaceResolvedReferenceBatchRemoval;
        _commitProductWorkspaceReferenceRemovalUndo =
            commitProductWorkspaceReferenceRemovalUndo;
        _commitProductWorkspaceResolvedReferenceReassignment =
            commitProductWorkspaceResolvedReferenceReassignment;
        _commitProductWorkspaceReferenceReassignmentUndo =
            commitProductWorkspaceReferenceReassignmentUndo;
        _commitProductWorkspaceContainerAction =
            commitProductWorkspaceContainerAction;
        _commitProductWorkspaceContainerRemovalUndo =
            commitProductWorkspaceContainerRemovalUndo;
        _commitProductWorkspaceLayoutRecovery =
            commitProductWorkspaceLayoutRecovery;
        _commitProductWorkspaceLayoutRecoveryUndo =
            commitProductWorkspaceLayoutRecoveryUndo;
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

    internal void ApplyProductDesktopCatalogState(
        ProductDesktopCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        (string title,
            string detail,
            string sourceSummary,
            string automationStatus,
            Symbol icon) = DescribeProductDesktopCatalog(snapshot);
        ProductDesktopCatalogTitle.Text = title;
        ProductDesktopCatalogDetail.Text = detail;
        ProductDesktopCatalogGeneration.Text = sourceSummary;
        ProductDesktopCatalogIcon.Symbol = icon;
        AutomationProperties.SetItemStatus(
            ProductDesktopCatalogDetail,
            automationStatus);
        ApplyRuntimeStatus(
            RuntimeStatusSnapshot.CreateDevelopmentReadOnly(
                desktopCatalogConnected: snapshot.IsAuthoritative));
        CurrentModeDetail.Text = snapshot.Status switch
        {
            ProductDesktopCatalogStatus.Ready => "物理桌面目录已只读连接",
            ProductDesktopCatalogStatus.Refreshing => "正在刷新只读桌面目录",
            ProductDesktopCatalogStatus.Partial => "桌面目录结果不完整，未用于解析",
            _ => "桌面目录当前未连接",
        };
        AutomationProperties.SetItemStatus(
            CurrentModeValue,
            $"DevelopmentReadOnly:Catalog={snapshot.Status}:Generation={snapshot.Generation}");
    }

    private static (
        string Title,
        string Detail,
        string SourceSummary,
        string AutomationStatus,
        Symbol Icon) DescribeProductDesktopCatalog(
        ProductDesktopCatalogSnapshot snapshot)
    {
        string authority = snapshot.IsAuthoritative.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        string prefix =
            $"DesktopCatalog{snapshot.Status}:Generation={snapshot.Generation}:" +
            $"Items={snapshot.Entries.Count}:Authoritative={authority}";
        string sources = snapshot.Sources.Count == 0
            ? $"刷新代次 {snapshot.Generation} · 来源状态尚不可用"
            : $"刷新代次 {snapshot.Generation} · " + string.Join(
                " · ",
                snapshot.Sources.Select(source =>
                    $"{DescribeProductDesktopCatalogSource(source.Source)} " +
                    $"{DescribeProductDesktopCatalogSourceStatus(source.Status)} " +
                    $"({source.ItemCount})"));
        return snapshot.Status switch
        {
            ProductDesktopCatalogStatus.Unavailable => (
                "桌面目录当前不可用",
                "用户桌面与公共桌面尚未形成完整快照；不会把空结果用于引用解析。",
                sources,
                prefix,
                Symbol.Folder),
            ProductDesktopCatalogStatus.Refreshing => (
                "正在只读刷新桌面目录",
                "仅枚举用户桌面和公共桌面第一层；不会打开、移动、重命名或删除项目。",
                sources,
                prefix,
                Symbol.Sync),
            ProductDesktopCatalogStatus.Ready => (
                "只读桌面目录已就绪",
                "用户桌面和公共桌面均完整读取；该代次可以用于正式引用解析。",
                sources,
                prefix,
                Symbol.Accept),
            ProductDesktopCatalogStatus.Partial => (
                "桌面目录结果不完整",
                "至少一个来源缺失或读取不完整；已收集项目不会用于缺失引用判断。",
                sources,
                prefix,
                Symbol.Important),
            ProductDesktopCatalogStatus.Failed => (
                "桌面目录读取失败",
                "只读读取没有形成安全快照；不会显示路径，也不会修改桌面项目。",
                sources,
                prefix,
                Symbol.Important),
            ProductDesktopCatalogStatus.Cancelled => (
                "桌面目录刷新已取消",
                "本代次没有发布目录项目；正式产品会话保持 Catalog 未连接状态。",
                sources,
                prefix,
                Symbol.Cancel),
            _ => (
                "桌面目录状态不可用",
                "不会使用未知目录状态解析或保存产品工作区。",
                sources,
                $"DesktopCatalogUnknown:Generation={snapshot.Generation}:Items=0:Authoritative=False",
                Symbol.Important),
        };
    }

    private static string DescribeProductDesktopCatalogSource(
        ProductDesktopCatalogSourceKind source) => source switch
        {
            ProductDesktopCatalogSourceKind.UserDesktop => "用户桌面",
            ProductDesktopCatalogSourceKind.PublicDesktop => "公共桌面",
            _ => "未知来源",
        };

    private static string DescribeProductDesktopCatalogSourceStatus(
        ProductDesktopCatalogSourceStatus status) => status switch
        {
            ProductDesktopCatalogSourceStatus.Ready => "已完成",
            ProductDesktopCatalogSourceStatus.Missing => "不存在",
            ProductDesktopCatalogSourceStatus.Partial => "不完整",
            ProductDesktopCatalogSourceStatus.AccessDenied => "无权读取",
            ProductDesktopCatalogSourceStatus.IoFailure => "读取失败",
            _ => "状态未知",
        };

    private async void ProductDesktopCatalogRefreshButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ProductDesktopCatalogRefreshButton.IsEnabled = false;
        try
        {
            await _refreshProductDesktopCatalog();
        }
        finally
        {
            ProductDesktopCatalogRefreshButton.IsEnabled = true;
        }
    }

    internal void ApplyProductWorkspaceSessionState(
        ProductWorkspaceSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        (string title,
            string detail,
            string summary,
            string automationStatus,
            Symbol icon) = DescribeProductWorkspaceSession(snapshot);
        ProductWorkspaceSessionTitle.Text = title;
        ProductWorkspaceSessionDetail.Text = detail;
        ProductWorkspaceSessionSummary.Text = summary;
        ProductWorkspaceSessionIcon.Symbol = icon;
        AutomationProperties.SetItemStatus(
            ProductWorkspaceSessionDetail,
            automationStatus);
    }

    internal void ApplyProductWorkspaceReadModel(
        ProductWorkspaceReadPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        _workspaceRead = presentation;
        ProductWorkspaceViewDetail.Text = presentation.Detail;
        ProductWorkspaceHealthFilterSelector.IsEnabled = presentation.CanFilter;
        ProductWorkspaceOpenReviewButton.IsEnabled = false;
        ProductWorkspaceOpenReviewButton.Visibility = Visibility.Collapsed;
        AutomationProperties.SetItemStatus(
            ProductWorkspaceOpenReviewButton,
            "WorkspaceReviewShortcutPendingAlignment:Items=0:" +
                "DesktopFilesChanged=False");
        ApplyProductWorkspaceHealthFilter();
    }

    private void ProductWorkspaceHealthFilterSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (ProductWorkspaceContainerList is null || ProductWorkspaceViewStatus is null)
        {
            return;
        }

        ApplyProductWorkspaceHealthFilter();
    }

    private void ApplyProductWorkspaceHealthFilter()
    {
        ProductWorkspaceContainerHealthFilter filter =
            ProductWorkspaceHealthFilterSelector.SelectedIndex switch
            {
                0 => ProductWorkspaceContainerHealthFilter.All,
                1 => ProductWorkspaceContainerHealthFilter.NeedsReview,
                2 => ProductWorkspaceContainerHealthFilter.Empty,
                3 => ProductWorkspaceContainerHealthFilter.Ready,
                _ => ProductWorkspaceContainerHealthFilter.Invalid,
            };
        ProductWorkspaceReadFilterPresentation filtered =
            _workspaceRead.ApplyFilter(filter);
        ProductWorkspaceContainerList.ItemsSource = filtered.Containers;
        ProductWorkspaceViewStatus.Text = filtered.Detail;
        AutomationProperties.SetItemStatus(
            ProductWorkspaceViewStatus,
            filtered.MachineStatus);
    }

    private void ProductWorkspaceOpenReviewButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        int reviewItemCount = _referenceReview.Snapshot?.Items.Count ?? 0;
        bool canOpen = ProductWorkspaceReviewShortcutPolicy.CanOpen(
            _workspaceRead.UnresolvedReferenceCount,
            reviewItemCount,
            _referenceReview.Snapshot is not null
                && _referenceReview.Error == ProductWorkspaceReferenceReviewError.None);
        if (!canOpen)
        {
            ProductWorkspaceViewStatus.Text =
                "待审查入口已变化；没有移动焦点或修改桌面文件。";
            AutomationProperties.SetItemStatus(
                ProductWorkspaceViewStatus,
                "WorkspaceReviewShortcutRejected:Focused=False:" +
                    "DesktopFilesChanged=False");
            UpdateProductWorkspaceOpenReviewButton();
            return;
        }

        ProductWorkspaceHealthFilterSelector.SelectedIndex = 1;
        bool focused = ProductWorkspaceReferenceReviewSelector.Focus(
            FocusState.Programmatic);
        ProductWorkspaceViewStatus.Text = focused
            ? $"已显示待审查方格，并将焦点移到 {reviewItemCount} 个匿名待审查引用。"
            : "已显示待审查方格；审查选择器当前无法获得焦点。";
        AutomationProperties.SetItemStatus(
            ProductWorkspaceViewStatus,
            $"WorkspaceReviewShortcutOpened:Items={reviewItemCount}:" +
                $"Focused={focused}:DesktopFilesChanged=False");
    }

    private void UpdateProductWorkspaceOpenReviewButton()
    {
        int reviewItemCount = _referenceReview.Snapshot?.Items.Count ?? 0;
        bool canOpen = ProductWorkspaceReviewShortcutPolicy.CanOpen(
            _workspaceRead.UnresolvedReferenceCount,
            reviewItemCount,
            _referenceReview.Snapshot is not null
                && _referenceReview.Error == ProductWorkspaceReferenceReviewError.None);
        ProductWorkspaceOpenReviewButton.IsEnabled = canOpen;
        ProductWorkspaceOpenReviewButton.Visibility = canOpen
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProductWorkspaceOpenReviewButton.Content = canOpen
            ? $"查看待审查引用 ({reviewItemCount})"
            : "查看待审查引用";
        AutomationProperties.SetName(
            ProductWorkspaceOpenReviewButton,
            canOpen
                ? $"查看 {reviewItemCount} 个匿名待审查引用"
                : "查看匿名待审查引用");
        AutomationProperties.SetItemStatus(
            ProductWorkspaceOpenReviewButton,
            $"WorkspaceReviewShortcut:{(canOpen ? "Available" : "Unavailable")}:" +
                $"Items={reviewItemCount}:Aligned={canOpen}:" +
                "DesktopFilesChanged=False");
    }

    internal void ApplyProductWorkspaceLatestUndo(
        ProductWorkspaceLatestUndoPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        _latestUndo = presentation;
        ProductWorkspaceLatestUndoButton.Content = presentation.ButtonText;
        ProductWorkspaceLatestUndoButton.IsEnabled = presentation.CanUndo;
        ProductWorkspaceLatestUndoButton.Visibility = presentation.CanUndo
            ? Visibility.Visible
            : Visibility.Collapsed;
        AutomationProperties.SetName(
            ProductWorkspaceLatestUndoButton,
            presentation.AccessibilityName);
        AutomationProperties.SetItemStatus(
            ProductWorkspaceLatestUndoButton,
            presentation.MachineStatus);
    }

    private void ProductWorkspaceLatestUndoButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_latestUndo.CanUndo)
        {
            return;
        }

        switch (_latestUndo.Selection.Kind)
        {
            case ProductWorkspaceLatestUndoKind.LayoutRecovery
                when _latestUndo.LayoutRecoveryToken is { } token:
                UndoLatestLayoutRecovery(token);
                break;
            case ProductWorkspaceLatestUndoKind.ContainerRemoval
                when _latestUndo.ContainerRemovalToken is { } token:
                UndoLatestContainerRemoval(token);
                break;
            case ProductWorkspaceLatestUndoKind.ReferenceBatchAddition
                when _latestUndo.ReferenceBatchAdditionToken is { } token:
                UndoLatestReferenceBatchAddition(token);
                break;
            case ProductWorkspaceLatestUndoKind.ReferenceRemoval
                when _latestUndo.ReferenceRemovalToken is { } token:
                UndoLatestReferenceRemoval(token);
                break;
            case ProductWorkspaceLatestUndoKind.ReferenceReassignment
                when _latestUndo.ReferenceReassignmentToken is { } token:
                UndoLatestReferenceReassignment(token);
                break;
        }
    }

    private void UndoLatestLayoutRecovery(
        ProductWorkspaceLayoutRecoveryUndoToken token)
    {
        ProductWorkspaceLayoutRecoveryUndoCommitResult result =
            _commitProductWorkspaceLayoutRecoveryUndo(token, true);
        ProductWorkspaceLayoutRecoveryDetail.Text = result.IsAccepted
            ? "最近一次布局配置恢复已撤销并保存；桌面窗口与文件均未改变。"
            : DescribeLayoutRecoveryUndoFailure(result.UndoStatus);
        AutomationProperties.SetItemStatus(
            ProductWorkspaceLayoutRecoveryDetail,
            $"LatestWorkspaceEditUndo:{result.Status}:Kind=LayoutRecovery:" +
                $"Revision={result.EditRevision}:Changed={result.IsAccepted}:" +
                "DesktopFilesChanged=False:DesktopWindowsChanged=False");
    }

    private void UndoLatestContainerRemoval(
        ProductWorkspaceContainerRemovalUndoToken token)
    {
        ProductWorkspaceContainerRemovalUndoCommitResult result =
            _commitProductWorkspaceContainerRemovalUndo(token, true);
        ProductWorkspaceContainerEditStatus.Text = result.IsAccepted
            ? "最近删除的方格配置及其引用已即时恢复并保存；桌面文件未改变。"
            : DescribeContainerRemovalUndoFailure(result.UndoStatus);
        AutomationProperties.SetItemStatus(
            ProductWorkspaceContainerEditStatus,
            $"LatestWorkspaceEditUndo:{result.Status}:Kind=ContainerRemoval:" +
                $"Revision={result.EditRevision}:Changed={result.IsAccepted}:" +
                "DesktopFilesChanged=False:DesktopWindowsChanged=False");
        UpdateProductWorkspaceContainerEditButtons();
    }

    private void UndoLatestReferenceBatchAddition(
        ProductWorkspaceReferenceBatchAdditionUndoToken token)
    {
        ProductWorkspaceReferenceBatchAdditionUndoCommitResult result =
            _commitProductWorkspaceReferenceBatchAdditionUndo(token, true);
        ProductWorkspaceResolvedReferenceAddStatus.Text = result.IsAccepted
            ? "最近一次批量加入已即时整体撤销并保存；桌面文件未改变。"
            : "批量加入撤销令牌已失效或保存不可用；配置与桌面文件均未改变。";
        AutomationProperties.SetItemStatus(
            ProductWorkspaceResolvedReferenceAddStatus,
            $"LatestWorkspaceEditUndo:{result.Status}:Kind=ReferenceBatchAddition:" +
                $"Gate={result.UndoStatus}:Revision={result.EditRevision}:" +
                $"Changed={result.IsAccepted}:DesktopFilesChanged=False:" +
                "DesktopWindowsChanged=False");
        UpdateProductWorkspaceResolvedReferenceAddButton();
    }

    private void UndoLatestReferenceRemoval(
        ProductWorkspaceReferenceRemovalUndoToken token)
    {
        ProductWorkspaceReferenceRemovalUndoCommitResult result =
            _commitProductWorkspaceReferenceRemovalUndo(token, true);
        ProductWorkspaceResolvedReferenceRemovalStatus.Text = result.IsAccepted
            ? "最近一次批量引用移除已即时整体撤销并保存；桌面文件未改变。"
            : "引用移除撤销令牌已失效或保存不可用；配置与桌面文件均未改变。";
        AutomationProperties.SetItemStatus(
            ProductWorkspaceResolvedReferenceRemovalStatus,
            $"LatestWorkspaceEditUndo:{result.Status}:Kind=ReferenceRemoval:" +
                $"Gate={result.UndoStatus}:Revision={result.EditRevision}:" +
                $"Changed={result.IsAccepted}:DesktopFilesChanged=False:" +
                "DesktopWindowsChanged=False");
        UpdateProductWorkspaceResolvedReferenceRemovalButtons();
    }

    private void UndoLatestReferenceReassignment(
        ProductWorkspaceReferenceReassignmentUndoToken token)
    {
        ProductWorkspaceReferenceReassignmentUndoCommitResult result =
            _commitProductWorkspaceReferenceReassignmentUndo(token, true);
        ProductWorkspaceResolvedReferenceRemovalStatus.Text = result.IsAccepted
            ? "最近一次批量引用改归属已即时整体撤销并保存；桌面文件未改变。"
            : "引用改归属撤销令牌已失效或保存不可用；配置与桌面文件均未改变。";
        AutomationProperties.SetItemStatus(
            ProductWorkspaceResolvedReferenceRemovalStatus,
            $"LatestWorkspaceEditUndo:{result.Status}:Kind=ReferenceReassignment:" +
                $"Gate={result.UndoStatus}:Revision={result.EditRevision}:" +
                $"Changed={result.IsAccepted}:DesktopFilesChanged=False:" +
                "DesktopWindowsChanged=False");
        UpdateProductWorkspaceResolvedReferenceRemovalButtons();
    }

    internal void ApplyProductWorkspaceLayoutRecoveryPreview(
        ProductWorkspaceLayoutRecoveryPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        _layoutRecovery = presentation;
        ProductWorkspaceLayoutRecoveryTitle.Text = presentation.Title;
        ProductWorkspaceLayoutRecoveryDetail.Text = presentation.Detail;
        ProductWorkspaceLayoutRecoverySummary.Text = presentation.Summary;
        AutomationProperties.SetItemStatus(
            ProductWorkspaceLayoutRecoveryDetail,
            presentation.MachineStatus);
        ProductWorkspaceLayoutRecoveryConfirmButton.IsEnabled =
            presentation.CanConfirm;
        ProductWorkspaceLayoutRecoveryConfirmButton.Visibility =
            presentation.CanConfirm ? Visibility.Visible : Visibility.Collapsed;
        ProductWorkspaceLayoutRecoveryUndoButton.IsEnabled = presentation.CanUndo;
        ProductWorkspaceLayoutRecoveryUndoButton.Visibility =
            presentation.CanUndo ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ProductWorkspaceLayoutRecoveryConfirmButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ProductWorkspaceLayoutRecoveryReviewToken? token = _layoutRecovery.Token;
        if (!_layoutRecovery.CanConfirm || token is null)
        {
            return;
        }

        ProductWorkspaceLayoutRecoveryConfirmButton.IsEnabled = false;
        try
        {
            ContentDialog confirmation = new()
            {
                XamlRoot = RootLayout.XamlRoot,
                Title = "确认更新 Long方格布局配置？",
                Content =
                    $"{_layoutRecovery.Summary}。确认后只会更新 Long方格自身的显示器键、DIP 布局和保存时拓扑，" +
                    "并进入现有安全保存队列；不会移动、隐藏或创建任何真实桌面窗口。",
                PrimaryButtonText = "确认配置恢复",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            ProductWorkspaceLayoutRecoveryCommitResult result =
                _commitProductWorkspaceLayoutRecovery(token, true);
            if (!result.IsAccepted)
            {
                ProductWorkspaceLayoutRecoveryDetail.Text =
                    DescribeLayoutRecoveryCommitFailure(result.ConfirmationStatus);
                AutomationProperties.SetItemStatus(
                    ProductWorkspaceLayoutRecoveryDetail,
                    $"LayoutRecoveryCommitRejected:{result.ConfirmationStatus}:DesktopWindowsChanged=False");
            }
        }
        finally
        {
            ProductWorkspaceLayoutRecoveryConfirmButton.IsEnabled =
                _layoutRecovery.CanConfirm;
        }
    }

    private static string DescribeLayoutRecoveryCommitFailure(
        ProductWorkspaceLayoutRecoveryConfirmationStatus status) => status switch
        {
            ProductWorkspaceLayoutRecoveryConfirmationStatus
                .TopologyGenerationChanged => "显示拓扑已经变化；旧预览已失效，请检查新结果。",
            ProductWorkspaceLayoutRecoveryConfirmationStatus.EditRevisionChanged =>
                "布局配置已经变化；旧预览已失效，请检查新结果。",
            ProductWorkspaceLayoutRecoveryConfirmationStatus.TokenMismatch =>
                "恢复证据与预览不一致；没有修改配置。",
            ProductWorkspaceLayoutRecoveryConfirmationStatus.ContainerLocked =>
                "至少一个需要调整的方格已锁定；没有修改配置。",
            ProductWorkspaceLayoutRecoveryConfirmationStatus.PlanBlocked =>
                "显示器无法唯一映射；没有执行部分恢复。",
            _ => "恢复预览已不可用；没有修改配置，请重新检查当前状态。",
        };

    private async void ProductWorkspaceLayoutRecoveryUndoButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ProductWorkspaceLayoutRecoveryUndoToken? token =
            _layoutRecovery.UndoToken;
        if (!_layoutRecovery.CanUndo || token is null)
        {
            return;
        }

        ProductWorkspaceLayoutRecoveryUndoButton.IsEnabled = false;
        try
        {
            ContentDialog confirmation = new()
            {
                XamlRoot = RootLayout.XamlRoot,
                Title = "撤销本次 Long方格配置恢复？",
                Content =
                    "确认后只会恢复本次布局恢复前的 Long方格配置，并进入现有安全保存队列；" +
                    "不会移动、隐藏或创建任何真实桌面窗口，也不会修改桌面文件。",
                PrimaryButtonText = "确认撤销配置恢复",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            ProductWorkspaceLayoutRecoveryUndoCommitResult result =
                _commitProductWorkspaceLayoutRecoveryUndo(token, true);
            if (!result.IsAccepted)
            {
                ProductWorkspaceLayoutRecoveryDetail.Text =
                    DescribeLayoutRecoveryUndoFailure(result.UndoStatus);
                AutomationProperties.SetItemStatus(
                    ProductWorkspaceLayoutRecoveryDetail,
                    $"LayoutRecoveryUndoRejected:{result.UndoStatus}:DesktopWindowsChanged=False");
            }
        }
        finally
        {
            ProductWorkspaceLayoutRecoveryUndoButton.IsEnabled =
                _layoutRecovery.CanUndo;
        }
    }

    private static string DescribeLayoutRecoveryUndoFailure(
        ProductWorkspaceLayoutRecoveryUndoStatus status) => status switch
        {
            ProductWorkspaceLayoutRecoveryUndoStatus.EditRevisionChanged =>
                "布局配置已经变化；本次恢复的撤销入口已失效。",
            ProductWorkspaceLayoutRecoveryUndoStatus.TokenMismatch =>
                "撤销证据与本次恢复不一致；没有修改配置。",
            ProductWorkspaceLayoutRecoveryUndoStatus
                .CurrentConfigurationChanged =>
                "当前配置已不再是本次恢复后的状态；没有执行覆盖。",
            ProductWorkspaceLayoutRecoveryUndoStatus.Unavailable =>
                "本次恢复已撤销或已被后续编辑取代。",
            _ => "撤销请求已不可用；没有修改配置。",
        };

    internal void ApplyProductWorkspaceContainerEditor(
        ProductWorkspaceContainerEditPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        int previousOrdinal = ProductWorkspaceContainerEditSelector.SelectedItem is
            ProductWorkspaceContainerEditCandidatePresentation previous
                ? previous.Ordinal
                : 0;
        _containerEditor = presentation;
        ProductWorkspaceContainerEditSelector.ItemsSource = presentation.Candidates;
        ProductWorkspaceContainerColorSelector.ItemsSource =
            ProductWorkspaceContainerEditPresentation.ColorChoices;
        ProductWorkspaceContainerOpacitySelector.ItemsSource =
            ProductWorkspaceContainerEditPresentation.OpacityChoices;
        ProductWorkspaceContainerPositionSelector.ItemsSource =
            ProductWorkspaceContainerEditPresentation.PositionChoices;
        ProductWorkspaceContainerSizeSelector.ItemsSource =
            ProductWorkspaceContainerEditPresentation.SizeChoices;
        ProductWorkspaceContainerEditSelector.SelectedIndex =
            previousOrdinal > 0 && previousOrdinal <= presentation.Candidates.Count
                ? previousOrdinal - 1
                : presentation.Candidates.Count > 0 ? 0 : -1;
        ProductWorkspaceContainerEditStatus.Text = presentation.CanCreate
            ? "仅更改 Long方格配置；不会移动、删除或重命名桌面文件。"
            : "当前会话保持只读；容器配置未改变。";
        AutomationProperties.SetItemStatus(
            ProductWorkspaceContainerEditStatus,
            $"WorkspaceContainerEditReady:Revision={presentation.EditRevision}:" +
                $"Candidates={presentation.Candidates.Count}:" +
                $"CanCreate={presentation.CanCreate}:CanRename={presentation.CanRename}:" +
                $"CanUpdateState={presentation.CanUpdateState}:" +
                $"CanUpdateAppearance={presentation.CanUpdateAppearance}:" +
                $"CanUpdatePlacement={presentation.CanUpdatePlacement}:" +
                $"CanRemove={presentation.CanRemove}:" +
                $"CanUndoRemoval={presentation.RemovalUndoToken is not null}:" +
                "Changed=False:DesktopFilesChanged=False");
        UpdateProductWorkspaceContainerEditButtons();
    }

    private void ProductWorkspaceContainerEditSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (ProductWorkspaceContainerEditSelector.SelectedItem is
            ProductWorkspaceContainerEditCandidatePresentation selected)
        {
            ProductWorkspaceContainerNameEditor.Text = selected.DisplayName;
            ApplyProductWorkspaceContainerAppearanceSelection(selected);
            ApplyProductWorkspaceContainerPlacementSelection(selected);
        }
        else
        {
            ProductWorkspaceContainerColorSelector.SelectedIndex = -1;
            ProductWorkspaceContainerOpacitySelector.SelectedIndex = -1;
            ProductWorkspaceContainerPositionSelector.SelectedIndex = -1;
            ProductWorkspaceContainerSizeSelector.SelectedIndex = -1;
        }

        UpdateProductWorkspaceContainerEditButtons();
    }

    private void ProductWorkspaceContainerNameEditor_TextChanged(
        object sender,
        TextChangedEventArgs e) =>
        UpdateProductWorkspaceContainerEditButtons();

    private void ProductWorkspaceContainerAppearanceSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) =>
        UpdateProductWorkspaceContainerEditButtons();

    private void ProductWorkspaceContainerPlacementSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) =>
        UpdateProductWorkspaceContainerEditButtons();

    private static int FindColorChoiceIndex(string color) =>
        ProductWorkspaceContainerEditPresentation.ColorChoices
            .Select((choice, index) => (choice, index))
            .Where(pair => string.Equals(
                pair.choice.Color,
                color,
                StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.index)
            .DefaultIfEmpty(-1)
            .First();

    private static int FindOpacityChoiceIndex(double opacity) =>
        ProductWorkspaceContainerEditPresentation.OpacityChoices
            .Select((choice, index) => (choice, index))
            .Where(pair => Math.Abs(pair.choice.Opacity - opacity) < 0.000001)
            .Select(pair => pair.index)
            .DefaultIfEmpty(-1)
            .First();

    private static int FindPositionChoiceIndex(double xDip, double yDip) =>
        ProductWorkspaceContainerEditPresentation.PositionChoices
            .Select((choice, index) => (choice, index))
            .Where(pair =>
                Math.Abs(pair.choice.XDip - xDip) < 0.000001
                && Math.Abs(pair.choice.YDip - yDip) < 0.000001)
            .Select(pair => pair.index)
            .DefaultIfEmpty(-1)
            .First();

    private static int FindSizeChoiceIndex(double widthDip, double heightDip) =>
        ProductWorkspaceContainerEditPresentation.SizeChoices
            .Select((choice, index) => (choice, index))
            .Where(pair =>
                Math.Abs(pair.choice.WidthDip - widthDip) < 0.000001
                && Math.Abs(pair.choice.HeightDip - heightDip) < 0.000001)
            .Select(pair => pair.index)
            .DefaultIfEmpty(-1)
            .First();

    private void ApplyProductWorkspaceContainerAppearanceSelection(
        ProductWorkspaceContainerEditCandidatePresentation selected)
    {
        ProductWorkspaceContainerColorSelector.SelectedIndex =
            FindColorChoiceIndex(selected.Color);
        ProductWorkspaceContainerOpacitySelector.SelectedIndex =
            FindOpacityChoiceIndex(selected.Opacity);
    }

    private void ApplyProductWorkspaceContainerPlacementSelection(
        ProductWorkspaceContainerEditCandidatePresentation selected)
    {
        ProductWorkspaceContainerPositionSelector.SelectedIndex =
            FindPositionChoiceIndex(selected.XDip, selected.YDip);
        ProductWorkspaceContainerSizeSelector.SelectedIndex =
            FindSizeChoiceIndex(selected.WidthDip, selected.HeightDip);
    }

    private void UpdateProductWorkspaceContainerEditButtons()
    {
        bool hasName = !string.IsNullOrWhiteSpace(
            ProductWorkspaceContainerNameEditor.Text);
        ProductWorkspaceContainerCreateButton.IsEnabled =
            _containerEditor.CanCreate && hasName;
        ProductWorkspaceContainerRenameButton.IsEnabled =
            _containerEditor.CanRename
            && hasName
            && ProductWorkspaceContainerEditSelector.SelectedItem is
                ProductWorkspaceContainerEditCandidatePresentation;
        ProductWorkspaceContainerEditCandidatePresentation? selected =
            ProductWorkspaceContainerEditSelector.SelectedItem as
                ProductWorkspaceContainerEditCandidatePresentation;
        ProductWorkspaceContainerLockButton.IsEnabled =
            _containerEditor.CanUpdateState && selected is not null;
        ProductWorkspaceContainerCollapseButton.IsEnabled =
            _containerEditor.CanUpdateState
            && selected is not null
            && !selected.IsLocked;
        bool canEditAppearance = _containerEditor.CanUpdateAppearance
            && selected is not null
            && !selected.IsLocked;
        ProductWorkspaceContainerColorSelector.IsEnabled = canEditAppearance;
        ProductWorkspaceContainerOpacitySelector.IsEnabled = canEditAppearance;
        ProductWorkspaceContainerColorChoicePresentation? colorChoice =
            ProductWorkspaceContainerColorSelector.SelectedItem as
                ProductWorkspaceContainerColorChoicePresentation;
        ProductWorkspaceContainerOpacityChoicePresentation? opacityChoice =
            ProductWorkspaceContainerOpacitySelector.SelectedItem as
                ProductWorkspaceContainerOpacityChoicePresentation;
        ProductWorkspaceContainerAppearanceButton.IsEnabled =
            canEditAppearance
            && colorChoice is not null
            && opacityChoice is not null
            && (!string.Equals(
                    colorChoice.Color,
                    selected!.Color,
                    StringComparison.OrdinalIgnoreCase)
                || Math.Abs(opacityChoice.Opacity - selected.Opacity) >= 0.000001);
        bool canEditPlacement = _containerEditor.CanUpdatePlacement
            && selected is not null
            && !selected.IsLocked;
        ProductWorkspaceContainerPositionSelector.IsEnabled = canEditPlacement;
        ProductWorkspaceContainerSizeSelector.IsEnabled = canEditPlacement;
        ProductWorkspaceContainerPositionChoicePresentation? positionChoice =
            ProductWorkspaceContainerPositionSelector.SelectedItem as
                ProductWorkspaceContainerPositionChoicePresentation;
        ProductWorkspaceContainerSizeChoicePresentation? sizeChoice =
            ProductWorkspaceContainerSizeSelector.SelectedItem as
                ProductWorkspaceContainerSizeChoicePresentation;
        ProductWorkspaceContainerPlacementButton.IsEnabled =
            canEditPlacement
            && positionChoice is not null
            && sizeChoice is not null
            && (Math.Abs(positionChoice.XDip - selected!.XDip) >= 0.000001
                || Math.Abs(positionChoice.YDip - selected.YDip) >= 0.000001
                || Math.Abs(sizeChoice.WidthDip - selected.WidthDip) >= 0.000001
                || Math.Abs(sizeChoice.HeightDip - selected.HeightDip) >= 0.000001);
        ProductWorkspaceContainerRemoveButton.IsEnabled =
            _containerEditor.CanRemove
            && selected is { IsLocked: false };
        ProductWorkspaceContainerRemovalUndoButton.IsEnabled =
            _containerEditor.RemovalUndoToken is not null;
        ProductWorkspaceContainerLockButton.Content = selected?.IsLocked == true
            ? "解锁并保存"
            : "锁定并保存";
        ProductWorkspaceContainerCollapseButton.Content =
            selected?.IsCollapsed == true
                ? "展开并保存"
                : "折叠并保存";
        UpdateProductWorkspaceResolvedReferenceAddButton();
    }

    private void ProductWorkspaceContainerCreateButton_Click(
        object sender,
        RoutedEventArgs e) =>
        RunProductWorkspaceContainerCommit(
            ProductWorkspaceContainerCommitAction.Create,
            containerOrdinal: 0);

    private void ProductWorkspaceContainerRenameButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        int ordinal = ProductWorkspaceContainerEditSelector.SelectedItem is
            ProductWorkspaceContainerEditCandidatePresentation selected
                ? selected.Ordinal
                : 0;
        RunProductWorkspaceContainerCommit(
            ProductWorkspaceContainerCommitAction.Rename,
            ordinal);
    }

    private void ProductWorkspaceContainerLockButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ProductWorkspaceContainerEditSelector.SelectedItem is not
            ProductWorkspaceContainerEditCandidatePresentation selected)
        {
            return;
        }

        RunProductWorkspaceContainerCommit(
            ProductWorkspaceContainerCommitAction.SetLocked,
            selected.Ordinal,
            !selected.IsLocked);
    }

    private void ProductWorkspaceContainerCollapseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ProductWorkspaceContainerEditSelector.SelectedItem is not
            ProductWorkspaceContainerEditCandidatePresentation selected)
        {
            return;
        }

        RunProductWorkspaceContainerCommit(
            ProductWorkspaceContainerCommitAction.SetCollapsed,
            selected.Ordinal,
            !selected.IsCollapsed);
    }

    private void ProductWorkspaceContainerAppearanceButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ProductWorkspaceContainerEditSelector.SelectedItem is not
                ProductWorkspaceContainerEditCandidatePresentation selected
            || ProductWorkspaceContainerColorSelector.SelectedItem is not
                ProductWorkspaceContainerColorChoicePresentation color
            || ProductWorkspaceContainerOpacitySelector.SelectedItem is not
                ProductWorkspaceContainerOpacityChoicePresentation opacity)
        {
            return;
        }

        RunProductWorkspaceContainerCommit(
            ProductWorkspaceContainerCommitAction.SetAppearancePreset,
            selected.Ordinal,
            colorPreset: color.Preset,
            opacityPreset: opacity.Preset);
    }

    private void ProductWorkspaceContainerPlacementButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ProductWorkspaceContainerEditSelector.SelectedItem is not
                ProductWorkspaceContainerEditCandidatePresentation selected
            || ProductWorkspaceContainerPositionSelector.SelectedItem is not
                ProductWorkspaceContainerPositionChoicePresentation position
            || ProductWorkspaceContainerSizeSelector.SelectedItem is not
                ProductWorkspaceContainerSizeChoicePresentation size)
        {
            return;
        }

        RunProductWorkspaceContainerCommit(
            ProductWorkspaceContainerCommitAction.SetPlacementPreset,
            selected.Ordinal,
            positionPreset: position.Preset,
            sizePreset: size.Preset);
    }

    private async void ProductWorkspaceContainerRemoveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ProductWorkspaceContainerEditSelector.SelectedItem is not
            ProductWorkspaceContainerEditCandidatePresentation selected)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "删除方格配置",
            Content = $"确认删除“{selected.DisplayName}”及其中 {selected.ItemCount} 个配置引用。真实桌面文件不会被删除、移动或重命名。",
            PrimaryButtonText = "确认删除方格",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootLayout.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            RunProductWorkspaceContainerCommit(
                ProductWorkspaceContainerCommitAction.Remove,
                selected.Ordinal,
                confirmed: true);
        }
    }

    private async void ProductWorkspaceContainerRemovalUndoButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ProductWorkspaceContainerRemovalUndoToken? token =
            _containerEditor.RemovalUndoToken;
        if (token is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "撤销删除方格",
            Content = "确认恢复最近删除的方格配置及其引用。该操作仍不会修改任何真实桌面文件。",
            PrimaryButtonText = "确认撤销",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootLayout.XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        ProductWorkspaceContainerRemovalUndoCommitResult result =
            _commitProductWorkspaceContainerRemovalUndo(token, true);
        ProductWorkspaceContainerEditStatus.Text = result.IsAccepted
            ? "最近删除的方格配置及其引用已恢复并进入安全保存队列；桌面文件未改变。"
            : DescribeContainerRemovalUndoFailure(result.UndoStatus);
        AutomationProperties.SetItemStatus(
            ProductWorkspaceContainerEditStatus,
            $"WorkspaceContainerRemovalUndo:{result.Status}:" +
                $"Undo={result.UndoStatus}:Revision={result.EditRevision}:" +
                $"Changed={result.IsAccepted}:DesktopFilesChanged=False");
        UpdateProductWorkspaceContainerEditButtons();
    }

    private static string DescribeContainerRemovalUndoFailure(
        ProductWorkspaceContainerRemovalUndoStatus status) => status switch
        {
            ProductWorkspaceContainerRemovalUndoStatus.EditRevisionChanged =>
                "工作区已发生其他编辑，本次方格删除撤销已失效。",
            ProductWorkspaceContainerRemovalUndoStatus.TokenMismatch =>
                "撤销证据与最近一次方格删除不一致；配置未改变。",
            ProductWorkspaceContainerRemovalUndoStatus.CurrentConfigurationChanged =>
                "当前配置已不再是删除后的状态；未执行覆盖。",
            ProductWorkspaceContainerRemovalUndoStatus.Unavailable =>
                "最近一次方格删除已撤销或已被后续编辑取代。",
            _ => "方格删除撤销当前不可用；配置与桌面文件均未改变。",
        };

    private void RunProductWorkspaceContainerCommit(
        ProductWorkspaceContainerCommitAction action,
        int containerOrdinal,
        bool? stateValue = null,
        ProductWorkspaceContainerColorPreset? colorPreset = null,
        ProductWorkspaceContainerOpacityPreset? opacityPreset = null,
        ProductWorkspaceContainerPositionPreset? positionPreset = null,
        ProductWorkspaceContainerSizePreset? sizePreset = null,
        bool confirmed = false)
    {
        string name = action == ProductWorkspaceContainerCommitAction.Remove
            ? string.Empty
            : ProductWorkspaceContainerNameEditor.Text.Trim();
        ProductWorkspaceContainerCommitResult result =
            _commitProductWorkspaceContainerAction(
                action,
                _containerEditor.EditRevision,
                containerOrdinal,
                name,
                stateValue,
                colorPreset,
                opacityPreset,
                positionPreset,
                sizePreset,
                confirmed);
        bool changed = result.IsAccepted;
        ProductWorkspaceContainerEditStatus.Text = result.Status switch
        {
            ProductWorkspaceContainerCommitStatus.Accepted =>
                action == ProductWorkspaceContainerCommitAction.Create
                    ? "正式方格已创建并进入安全保存队列；桌面文件未改变。"
                    : action == ProductWorkspaceContainerCommitAction.Rename
                        ? "正式方格名称已更新并进入安全保存队列；桌面文件未改变。"
                        : action == ProductWorkspaceContainerCommitAction.SetLocked
                            ? "正式方格锁定状态已更新并进入安全保存队列；桌面文件未改变。"
                            : action == ProductWorkspaceContainerCommitAction.SetCollapsed
                                ? "正式方格折叠状态已更新并进入安全保存队列；桌面文件未改变。"
                                : action == ProductWorkspaceContainerCommitAction.SetAppearancePreset
                                    ? "正式方格外观预设已更新并进入安全保存队列；桌面文件未改变。"
                                    : action == ProductWorkspaceContainerCommitAction.SetPlacementPreset
                                        ? "正式方格布局预设已更新并进入安全保存队列；尚未移动真实窗口或桌面文件。"
                                        : "正式方格配置及其中引用已删除并进入安全保存队列；真实桌面文件未改变，可撤销一次。",
            ProductWorkspaceContainerCommitStatus.NoChange =>
                action == ProductWorkspaceContainerCommitAction.Rename
                    ? "名称没有变化，因此没有提交保存。"
                    : "容器状态没有变化，因此没有提交保存。",
            ProductWorkspaceContainerCommitStatus.StaleEditRevision =>
                "工作区已发生更新，请按当前列表重新操作。",
            ProductWorkspaceContainerCommitStatus.ReducerRejected
                when result.EditError == ProductWorkspaceEditError.ContainerLocked =>
                action == ProductWorkspaceContainerCommitAction.SetCollapsed
                    ? "所选方格已锁定，请先解锁再更改折叠状态。"
                    : action == ProductWorkspaceContainerCommitAction.SetAppearancePreset
                        ? "所选方格已锁定，请先解锁再更改外观。"
                        : action == ProductWorkspaceContainerCommitAction.SetPlacementPreset
                            ? "所选方格已锁定，请先解锁再更改布局。"
                            : action == ProductWorkspaceContainerCommitAction.Remove
                                ? "所选方格已锁定，请先解锁再删除。"
                                : "所选方格已锁定，未执行重命名。",
            ProductWorkspaceContainerCommitStatus.ReducerRejected =>
                "名称或容器状态未通过正式配置校验，未执行保存。",
            ProductWorkspaceContainerCommitStatus.SaveRejected =>
                "保存控制器当前无法接受编辑；配置与桌面文件均未改变。",
            _ => "容器编辑请求无效；配置与桌面文件均未改变。",
        };
        AutomationProperties.SetItemStatus(
            ProductWorkspaceContainerEditStatus,
            $"WorkspaceContainerEdit:{result.Status}:Action={action}:" +
                $"Revision={result.EditRevision}:Changed={changed}:" +
                "DesktopFilesChanged=False");
        if (changed && action is ProductWorkspaceContainerCommitAction.Create
            or ProductWorkspaceContainerCommitAction.Rename)
        {
            ProductWorkspaceContainerNameEditor.Text = string.Empty;
        }

        UpdateProductWorkspaceContainerEditButtons();
    }

    internal void ApplyProductWorkspaceResolvedReferenceAdd(
        ProductWorkspaceResolvedReferenceAddPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        int[] previousCatalogIndexes = ProductWorkspaceResolvedReferenceSelector
            .SelectedItems
            .OfType<ProductWorkspaceResolvedReferenceCandidatePresentation>()
            .Select(candidate => candidate.CatalogIndex)
            .ToArray();
        _resolvedReferenceAdd = presentation;
        _suppressBatchSelectionAnnouncements = true;
        try
        {
            ProductWorkspaceResolvedReferenceSelector.ItemsSource =
                presentation.Candidates;
            foreach (ProductWorkspaceResolvedReferenceCandidatePresentation candidate
                in presentation.Candidates.Where(candidate =>
                    previousCatalogIndexes.Contains(candidate.CatalogIndex)))
            {
                ProductWorkspaceResolvedReferenceSelector.SelectedItems.Add(candidate);
            }
        }
        finally
        {
            _suppressBatchSelectionAnnouncements = false;
        }
        ProductWorkspaceResolvedReferenceAddStatus.Text = presentation.CanAdd
            ? "可按 Ctrl 或 Shift 多选；整批只向 Long方格配置添加引用，不会移动、重命名或删除桌面文件。"
            : presentation.Candidates.Count == 0
                ? "当前没有可加入的未分组桌面项目；桌面文件未改变。"
                : "当前会话或方格不可编辑；桌面文件未改变。";
        AutomationProperties.SetItemStatus(
            ProductWorkspaceResolvedReferenceAddStatus,
            $"ResolvedReferenceAddReady:Generation={presentation.CatalogGeneration}:" +
                $"Revision={presentation.EditRevision}:" +
                $"Candidates={presentation.Candidates.Count}:" +
                $"CanAdd={presentation.CanAdd}:Changed=False:" +
                "DesktopFilesChanged=False");
        ProductWorkspaceReferenceBatchAdditionUndoButton.IsEnabled =
            presentation.BatchUndoToken is not null;
        UpdateProductWorkspaceResolvedReferenceAddButton();
    }

    private void ProductWorkspaceResolvedReferenceSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateProductWorkspaceResolvedReferenceAddButton();
        if (!_suppressBatchSelectionAnnouncements)
        {
            PublishResolvedReferenceAddSelectionStatus();
        }
    }

    private void ProductWorkspaceResolvedReferenceSelectFirstBatchButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!ProductWorkspaceResolvedReferenceSelector.IsEnabled)
        {
            return;
        }

        _suppressBatchSelectionAnnouncements = true;
        try
        {
            ProductWorkspaceResolvedReferenceSelector.SelectedItems.Clear();
            foreach (ProductWorkspaceResolvedReferenceCandidatePresentation candidate
                in _resolvedReferenceAdd.Candidates.Take(
                    ProductWorkspaceCommitCoordinator.MaximumResolvedReferenceBatchSize))
            {
                ProductWorkspaceResolvedReferenceSelector.SelectedItems.Add(candidate);
            }
        }
        finally
        {
            _suppressBatchSelectionAnnouncements = false;
        }

        UpdateProductWorkspaceResolvedReferenceAddButton();
        PublishResolvedReferenceAddSelectionStatus();
    }

    private void ProductWorkspaceResolvedReferenceClearSelectionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _suppressBatchSelectionAnnouncements = true;
        try
        {
            ProductWorkspaceResolvedReferenceSelector.SelectedItems.Clear();
        }
        finally
        {
            _suppressBatchSelectionAnnouncements = false;
        }

        UpdateProductWorkspaceResolvedReferenceAddButton();
        PublishResolvedReferenceAddSelectionStatus();
    }

    private void UpdateProductWorkspaceResolvedReferenceAddButton()
    {
        ProductWorkspaceContainerEditCandidatePresentation? container =
            ProductWorkspaceContainerEditSelector.SelectedItem as
                ProductWorkspaceContainerEditCandidatePresentation;
        bool enabled = _resolvedReferenceAdd.CanAdd
            && container is not null
            && !container.IsLocked
            && ProductWorkspaceResolvedReferenceSelector.SelectedItems.Count is > 0
                and <= ProductWorkspaceCommitCoordinator.MaximumResolvedReferenceBatchSize;
        ProductWorkspaceResolvedReferenceSelector.IsEnabled =
            _resolvedReferenceAdd.CanAdd
            && container is not null
            && !container.IsLocked;
        ProductWorkspaceResolvedReferenceAddButton.IsEnabled = enabled;
        int count = ProductWorkspaceResolvedReferenceSelector.SelectedItems.Count;
        ProductWorkspaceResolvedReferenceSelectFirstBatchButton.IsEnabled =
            ProductWorkspaceResolvedReferenceSelector.IsEnabled
            && _resolvedReferenceAdd.Candidates.Count > 0;
        ProductWorkspaceResolvedReferenceClearSelectionButton.IsEnabled = count > 0;
        ProductWorkspaceResolvedReferenceAddButton.Content = count > 0
            ? $"批量加入 {count} 项并保存"
            : "选择项目后批量加入";
    }

    private void PublishResolvedReferenceAddSelectionStatus()
    {
        int count = ProductWorkspaceResolvedReferenceSelector.SelectedItems.Count;
        if (count == 0)
        {
            ProductWorkspaceResolvedReferenceAddStatus.Text =
                "未选择批量加入项目；配置与桌面文件均未改变。";
            AutomationProperties.SetItemStatus(
                ProductWorkspaceResolvedReferenceAddStatus,
                "ResolvedReferenceBatchAddSelection:Count=0:Changed=False:" +
                    "DesktopFilesChanged=False");
        }
        else
        {
            ProductWorkspaceResolvedReferenceAddStatus.Text =
                $"已选择 {count} 个未分组项目；确认后整批只修改配置引用。";
            AutomationProperties.SetItemStatus(
                ProductWorkspaceResolvedReferenceAddStatus,
                $"ResolvedReferenceBatchAddSelection:Count={count}:" +
                    $"WithinLimit={count <= ProductWorkspaceCommitCoordinator.MaximumResolvedReferenceBatchSize}:" +
                    "Changed=False:DesktopFilesChanged=False");
        }

        RaiseLiveRegionChanged(ProductWorkspaceResolvedReferenceAddStatus);
    }

    private async void ProductWorkspaceResolvedReferenceAddButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ProductWorkspaceResolvedReferenceCandidatePresentation[] candidates =
            ProductWorkspaceResolvedReferenceSelector.SelectedItems
                .OfType<ProductWorkspaceResolvedReferenceCandidatePresentation>()
                .ToArray();
        if (ProductWorkspaceContainerEditSelector.SelectedItem is not
                ProductWorkspaceContainerEditCandidatePresentation container
            || candidates.Length == 0)
        {
            return;
        }

        ContentDialog confirmation = new()
        {
            XamlRoot = RootLayout.XamlRoot,
            Title = $"批量加入 {candidates.Length} 个桌面项目？",
            Content = "操作将作为一次配置编辑提交，可整批撤销。不会移动、删除或重命名桌面文件。",
            PrimaryButtonText = "批量加入",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        ProductWorkspaceResolvedReferenceBatchCommitResult result =
            _commitProductWorkspaceResolvedReferenceBatch(
                _resolvedReferenceAdd.EditRevision,
                container.Ordinal,
                candidates);
        bool changed = result.IsAccepted;
        ProductWorkspaceResolvedReferenceAddStatus.Text = result.Status switch
        {
            ProductWorkspaceResolvedReferenceBatchCommitStatus.Accepted =>
                $"已将 {candidates.Length} 个引用作为一个原子批次加入并保存；可整批撤销，桌面文件未改变。",
            ProductWorkspaceResolvedReferenceBatchCommitStatus.StaleEditRevision =>
                "工作区已经更新，请按当前方格和项目列表重新选择。",
            ProductWorkspaceResolvedReferenceBatchCommitStatus.StaleCatalogGeneration =>
                "桌面目录已经刷新，请按最新项目列表重新选择。",
            ProductWorkspaceResolvedReferenceBatchCommitStatus.AlreadyReferenced =>
                "批次中存在重复或已分组项目，整批未保存。",
            ProductWorkspaceResolvedReferenceBatchCommitStatus.ReducerRejected
                when result.EditError == ProductWorkspaceEditError.ContainerLocked =>
                "所选方格已经锁定，请先解锁；桌面文件未改变。",
            ProductWorkspaceResolvedReferenceBatchCommitStatus.ReducerRejected =>
                "批量引用未通过正式配置校验，整批未保存。",
            ProductWorkspaceResolvedReferenceBatchCommitStatus.SaveRejected =>
                "保存控制器当前无法接受编辑；配置与桌面文件均未改变。",
            _ => "批量加入请求无效或超过 256 项；配置与桌面文件均未改变。",
        };
        AutomationProperties.SetItemStatus(
            ProductWorkspaceResolvedReferenceAddStatus,
            $"ResolvedReferenceBatchAdd:{result.Status}:Count={candidates.Length}:" +
                $"Revision={result.EditRevision}:Atomic=True:Changed={changed}:" +
                "DesktopFilesChanged=False");
        UpdateProductWorkspaceResolvedReferenceAddButton();
    }

    private async void ProductWorkspaceReferenceBatchAdditionUndoButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_resolvedReferenceAdd.BatchUndoToken is not { } token)
        {
            return;
        }

        ContentDialog confirmation = new()
        {
            XamlRoot = RootLayout.XamlRoot,
            Title = "撤销最近一次批量加入？",
            Content = "只恢复 Long方格配置引用，不会移动、删除或重命名桌面文件。",
            PrimaryButtonText = "撤销批量加入",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        ProductWorkspaceReferenceBatchAdditionUndoCommitResult result =
            _commitProductWorkspaceReferenceBatchAdditionUndo(token, true);
        ProductWorkspaceResolvedReferenceAddStatus.Text = result.IsAccepted
            ? "最近一次批量加入已整体撤销并保存；桌面文件未改变。"
            : "批量撤销令牌已失效或保存不可用；配置与桌面文件均未改变。";
        AutomationProperties.SetItemStatus(
            ProductWorkspaceResolvedReferenceAddStatus,
            $"ResolvedReferenceBatchAddUndo:{result.Status}:Undo={result.UndoStatus}:" +
                $"Revision={result.EditRevision}:Changed={result.IsAccepted}:" +
                "DesktopFilesChanged=False");
        UpdateProductWorkspaceResolvedReferenceAddButton();
    }

    internal void ApplyProductWorkspaceResolvedReferenceRemoval(
        ProductWorkspaceResolvedReferenceRemovalPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        (int ContainerOrdinal, int ItemOrdinal)[] previous =
            ProductWorkspaceResolvedReferenceRemovalSelector.SelectedItems
                .OfType<ProductWorkspaceResolvedReferenceRemovalCandidatePresentation>()
                .Select(item => (item.ContainerOrdinal, item.ItemOrdinal))
                .ToArray();
        _resolvedReferenceRemoval = presentation;
        _suppressBatchSelectionAnnouncements = true;
        try
        {
            ProductWorkspaceResolvedReferenceRemovalSelector.ItemsSource =
                presentation.Candidates;
            foreach (ProductWorkspaceResolvedReferenceRemovalCandidatePresentation candidate
                in presentation.Candidates.Where(candidate => previous.Contains(
                    (candidate.ContainerOrdinal, candidate.ItemOrdinal))))
            {
                ProductWorkspaceResolvedReferenceRemovalSelector.SelectedItems.Add(candidate);
            }
        }
        finally
        {
            _suppressBatchSelectionAnnouncements = false;
        }
        ProductWorkspaceResolvedReferenceRemovalStatus.Text =
            presentation.CanRemove
                ? "可按 Ctrl 或 Shift 多选同一方格内 1–256 项并整批移除；只更新 Long方格配置。"
                : presentation.Candidates.Count == 0
                    ? "当前没有可移除的已解析引用；桌面文件未改变。"
                    : "当前会话不可编辑；桌面文件未改变。";
        AutomationProperties.SetItemStatus(
            ProductWorkspaceResolvedReferenceRemovalStatus,
            $"ResolvedReferenceRemovalReady:Revision={presentation.EditRevision}:" +
                $"Candidates={presentation.Candidates.Count}:" +
                $"CanRemove={presentation.CanRemove}:" +
                $"CanUndo={presentation.UndoToken is not null}:Changed=False:" +
                "DesktopFilesChanged=False");
        UpdateProductWorkspaceResolvedReferenceRemovalButtons();
    }

    private void ProductWorkspaceResolvedReferenceRemovalSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateProductWorkspaceResolvedReferenceRemovalButtons();
        if (!_suppressBatchSelectionAnnouncements)
        {
            PublishResolvedReferenceRemovalSelectionStatus();
        }
    }

    private void ProductWorkspaceResolvedReferenceSelectContainerBatchButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ProductWorkspaceResolvedReferenceRemovalCandidatePresentation[] selected =
            ProductWorkspaceResolvedReferenceRemovalSelector.SelectedItems
                .OfType<ProductWorkspaceResolvedReferenceRemovalCandidatePresentation>()
                .ToArray();
        if (selected.Length == 0
            || selected.Select(candidate => candidate.ContainerOrdinal)
                .Distinct()
                .Count() != 1)
        {
            return;
        }

        int containerOrdinal = selected[0].ContainerOrdinal;
        _suppressBatchSelectionAnnouncements = true;
        try
        {
            ProductWorkspaceResolvedReferenceRemovalSelector.SelectedItems.Clear();
            foreach (ProductWorkspaceResolvedReferenceRemovalCandidatePresentation candidate
                in _resolvedReferenceRemoval.Candidates
                    .Where(candidate => candidate.ContainerOrdinal == containerOrdinal)
                    .Take(ProductWorkspaceCommitCoordinator
                        .MaximumResolvedReferenceRemovalBatchSize))
            {
                ProductWorkspaceResolvedReferenceRemovalSelector.SelectedItems.Add(candidate);
            }
        }
        finally
        {
            _suppressBatchSelectionAnnouncements = false;
        }

        UpdateProductWorkspaceResolvedReferenceRemovalButtons();
        PublishResolvedReferenceRemovalSelectionStatus();
    }

    private void ProductWorkspaceResolvedReferenceRemovalClearSelectionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _suppressBatchSelectionAnnouncements = true;
        try
        {
            ProductWorkspaceResolvedReferenceRemovalSelector.SelectedItems.Clear();
        }
        finally
        {
            _suppressBatchSelectionAnnouncements = false;
        }

        UpdateProductWorkspaceResolvedReferenceRemovalButtons();
        PublishResolvedReferenceRemovalSelectionStatus();
    }

    internal void ApplyProductWorkspaceResolvedReferenceReassignment(
        ProductWorkspaceResolvedReferenceReassignmentPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        int previousOrdinal =
            ProductWorkspaceResolvedReferenceReassignmentTargetSelector.SelectedItem is
                ProductWorkspaceReferenceReassignmentTargetPresentation target
                    ? target.ContainerOrdinal
                    : -1;
        _resolvedReferenceReassignment = presentation;
        ProductWorkspaceResolvedReferenceReassignmentTargetSelector.ItemsSource =
            presentation.Targets;
        ProductWorkspaceResolvedReferenceReassignmentTargetSelector.SelectedIndex =
            presentation.Targets
                .Select((target, index) => (target, index))
                .Where(pair => pair.target.ContainerOrdinal == previousOrdinal)
                .Select(pair => pair.index)
                .DefaultIfEmpty(presentation.Targets.Count > 0 ? 0 : -1)
                .First();
        AutomationProperties.SetItemStatus(
            ProductWorkspaceResolvedReferenceRemovalStatus,
            $"ResolvedReferenceManagementReady:" +
                $"Revision={presentation.EditRevision}:" +
                $"Sources={_resolvedReferenceRemoval.Candidates.Count}:" +
                $"Targets={presentation.Targets.Count}:" +
                $"CanReassign={presentation.CanReassign}:" +
                $"CanUndo={presentation.UndoToken is not null
                    || _resolvedReferenceRemoval.UndoToken is not null}:" +
                "Changed=False:DesktopFilesChanged=False");
        UpdateProductWorkspaceResolvedReferenceRemovalButtons();
    }

    private void
        ProductWorkspaceResolvedReferenceReassignmentTargetSelector_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e) =>
        UpdateProductWorkspaceResolvedReferenceRemovalButtons();

    private void UpdateProductWorkspaceResolvedReferenceRemovalButtons()
    {
        ProductWorkspaceResolvedReferenceRemovalCandidatePresentation[] sources =
            ProductWorkspaceResolvedReferenceRemovalSelector.SelectedItems
                .OfType<ProductWorkspaceResolvedReferenceRemovalCandidatePresentation>()
                .ToArray();
        bool sameContainer = sources.Length > 0
            && sources.Select(candidate => candidate.ContainerOrdinal)
                .Distinct()
                .Count() == 1;
        ProductWorkspaceReferenceReassignmentTargetPresentation? target =
            ProductWorkspaceResolvedReferenceReassignmentTargetSelector.SelectedItem as
                ProductWorkspaceReferenceReassignmentTargetPresentation;
        ProductWorkspaceResolvedReferenceRemovalSelector.IsEnabled =
            _resolvedReferenceRemoval.CanRemove;
        ProductWorkspaceResolvedReferenceRemovalButton.IsEnabled =
            _resolvedReferenceRemoval.CanRemove
            && sameContainer
            && sources.Length <=
                ProductWorkspaceCommitCoordinator
                    .MaximumResolvedReferenceRemovalBatchSize;
        ProductWorkspaceResolvedReferenceSelectContainerBatchButton.IsEnabled =
            _resolvedReferenceRemoval.CanRemove && sameContainer;
        ProductWorkspaceResolvedReferenceRemovalClearSelectionButton.IsEnabled =
            sources.Length > 0;
        ProductWorkspaceResolvedReferenceReassignmentTargetSelector.IsEnabled =
            _resolvedReferenceReassignment.CanReassign
            && sameContainer
            && sources.Length <= ProductWorkspaceCommitCoordinator
                .MaximumResolvedReferenceReassignmentBatchSize;
        ProductWorkspaceResolvedReferenceReassignmentButton.IsEnabled =
            _resolvedReferenceReassignment.CanReassign
            && sameContainer
            && sources.Length <= ProductWorkspaceCommitCoordinator
                .MaximumResolvedReferenceReassignmentBatchSize
            && target is not null
            && sources[0].ContainerOrdinal != target.ContainerOrdinal;
        ProductWorkspaceResolvedReferenceRemovalButton.Content = !sameContainer
            && sources.Length > 0
                ? "跨方格不可批量移除"
                : sources.Length > 0
                    ? $"批量移除 {sources.Length} 项并保存"
                    : "选择项目后批量移除";
        ProductWorkspaceResolvedReferenceReassignmentButton.Content = !sameContainer
            && sources.Length > 0
                ? "跨方格不可批量改归属"
                : sources.Length > 0
                    ? $"批量改归属 {sources.Length} 项并保存"
                    : "选择项目后批量改归属";
        ProductWorkspaceResolvedReferenceRemovalUndoButton.IsEnabled =
            _resolvedReferenceRemoval.UndoToken is not null
            || _resolvedReferenceReassignment.UndoToken is not null;
    }

    private void PublishResolvedReferenceRemovalSelectionStatus()
    {
        ProductWorkspaceResolvedReferenceRemovalCandidatePresentation[] sources =
            ProductWorkspaceResolvedReferenceRemovalSelector.SelectedItems
                .OfType<ProductWorkspaceResolvedReferenceRemovalCandidatePresentation>()
                .ToArray();
        bool sameContainer = sources.Length > 0
            && sources.Select(candidate => candidate.ContainerOrdinal)
                .Distinct()
                .Count() == 1;
        if (sources.Length == 0)
        {
            ProductWorkspaceResolvedReferenceRemovalStatus.Text =
                "未选择要管理的引用；配置与桌面文件均未改变。";
            AutomationProperties.SetItemStatus(
                ProductWorkspaceResolvedReferenceRemovalStatus,
                "ResolvedReferenceBatchRemovalSelection:Count=0:Changed=False:" +
                    "DesktopFilesChanged=False");
        }
        else if (!sameContainer)
        {
            ProductWorkspaceResolvedReferenceRemovalStatus.Text =
                "批量管理只接受同一方格内的项目；跨方格选择未提交，桌面文件未改变。";
            AutomationProperties.SetItemStatus(
                ProductWorkspaceResolvedReferenceRemovalStatus,
                $"ResolvedReferenceBatchRemovalSelection:Count={sources.Length}:" +
                    "SameContainer=False:Changed=False:DesktopFilesChanged=False");
        }
        else if (sources.Length >
            ProductWorkspaceCommitCoordinator.MaximumResolvedReferenceRemovalBatchSize)
        {
            ProductWorkspaceResolvedReferenceRemovalStatus.Text =
                "单批最多管理 256 个引用；当前选择未提交，桌面文件未改变。";
            AutomationProperties.SetItemStatus(
                ProductWorkspaceResolvedReferenceRemovalStatus,
                $"ResolvedReferenceBatchRemovalSelection:Count={sources.Length}:" +
                    "WithinLimit=False:Changed=False:DesktopFilesChanged=False");
        }
        else if (sources.Length > 0)
        {
            ProductWorkspaceResolvedReferenceRemovalStatus.Text =
                $"已选择同一方格内 {sources.Length} 个引用；可批量移除或改归属，整批只修改配置。";
            AutomationProperties.SetItemStatus(
                ProductWorkspaceResolvedReferenceRemovalStatus,
                $"ResolvedReferenceBatchRemovalSelection:Count={sources.Length}:" +
                    "SameContainer=True:WithinLimit=True:Changed=False:" +
                    "DesktopFilesChanged=False");
        }
        RaiseLiveRegionChanged(ProductWorkspaceResolvedReferenceRemovalStatus);
    }

    private async void ProductWorkspaceResolvedReferenceRemovalButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ProductWorkspaceResolvedReferenceRemovalCandidatePresentation[] candidates =
            ProductWorkspaceResolvedReferenceRemovalSelector.SelectedItems
                .OfType<ProductWorkspaceResolvedReferenceRemovalCandidatePresentation>()
                .ToArray();
        if (candidates.Length == 0
            || candidates.Select(candidate => candidate.ContainerOrdinal)
                .Distinct()
                .Count() != 1)
        {
            return;
        }

        ContentDialog confirmation = new()
        {
            XamlRoot = RootLayout.XamlRoot,
            Title = $"从方格配置移除 {candidates.Length} 个引用？",
            Content = "整批将作为一次配置编辑提交并可整体撤销。不会移动、删除或重命名桌面文件。",
            PrimaryButtonText = "批量移除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        ProductWorkspaceResolvedReferenceBatchRemovalCommitResult result =
            _commitProductWorkspaceResolvedReferenceBatchRemoval(
                _resolvedReferenceRemoval.EditRevision,
                candidates);
        ProductWorkspaceResolvedReferenceRemovalStatus.Text = result.Status switch
        {
            ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus.Accepted =>
                $"已从同一方格原子移除 {candidates.Length} 个引用；桌面文件未改变，可整批撤销一次。",
            ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus.StaleEditRevision =>
                "工作区已经更新，请按最新引用列表重新选择。",
            ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus.ReducerRejected
                when result.EditError == ProductWorkspaceEditError.ContainerLocked =>
                "所选方格已经锁定，整批未移除；桌面文件未改变。",
            ProductWorkspaceResolvedReferenceBatchRemovalCommitStatus.SaveRejected =>
                "保存控制器当前无法接受编辑；配置与桌面文件均未改变。",
            _ => "请选择同一方格内 1–256 个已解析引用；整批未保存。",
        };
        AutomationProperties.SetItemStatus(
            ProductWorkspaceResolvedReferenceRemovalStatus,
            $"ResolvedReferenceBatchRemoval:{result.Status}:" +
                $"Count={candidates.Length}:Revision={result.EditRevision}:" +
                $"Atomic=True:Changed={result.IsAccepted}:" +
                "DesktopFilesChanged=False");
        UpdateProductWorkspaceResolvedReferenceRemovalButtons();
    }

    private void ProductWorkspaceResolvedReferenceRemovalUndoButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ProductWorkspaceReferenceReassignmentUndoToken? reassignmentToken =
            _resolvedReferenceReassignment.UndoToken;
        if (reassignmentToken is not null)
        {
            ProductWorkspaceReferenceReassignmentUndoCommitResult reassignmentUndo =
                _commitProductWorkspaceReferenceReassignmentUndo(
                    reassignmentToken,
                    true);
            ProductWorkspaceResolvedReferenceRemovalStatus.Text =
                reassignmentUndo.Status switch
                {
                    ProductWorkspaceReferenceReassignmentUndoCommitStatus.Accepted =>
                        "上一次批量引用改归属已整体撤销并进入安全保存队列；桌面文件未改变。",
                    ProductWorkspaceReferenceReassignmentUndoCommitStatus.SaveRejected =>
                        "撤销尚未保存；配置与桌面文件均未改变。",
                    _ => "撤销凭据已经失效，请按当前配置继续操作。",
                };
            AutomationProperties.SetItemStatus(
                ProductWorkspaceResolvedReferenceRemovalStatus,
                $"ResolvedReferenceReassignmentUndo:{reassignmentUndo.Status}:" +
                    $"Gate={reassignmentUndo.UndoStatus}:" +
                    $"Revision={reassignmentUndo.EditRevision}:" +
                    $"Changed={reassignmentUndo.IsAccepted}:" +
                    "DesktopFilesChanged=False");
            UpdateProductWorkspaceResolvedReferenceRemovalButtons();
            return;
        }

        ProductWorkspaceReferenceRemovalUndoToken? removalToken =
            _resolvedReferenceRemoval.UndoToken;
        if (removalToken is null)
        {
            return;
        }

        ProductWorkspaceReferenceRemovalUndoCommitResult result =
            _commitProductWorkspaceReferenceRemovalUndo(removalToken, true);
        ProductWorkspaceResolvedReferenceRemovalStatus.Text = result.Status switch
        {
            ProductWorkspaceReferenceRemovalUndoCommitStatus.Accepted =>
                "上一次引用移除已撤销并进入安全保存队列；桌面文件未改变。",
            ProductWorkspaceReferenceRemovalUndoCommitStatus.SaveRejected =>
                "撤销尚未保存；配置与桌面文件均未改变。",
            _ => "撤销凭据已经失效，请按当前配置继续操作。",
        };
        AutomationProperties.SetItemStatus(
            ProductWorkspaceResolvedReferenceRemovalStatus,
            $"ResolvedReferenceRemovalUndo:{result.Status}:" +
                $"Gate={result.UndoStatus}:Revision={result.EditRevision}:" +
                $"Changed={result.IsAccepted}:DesktopFilesChanged=False");
        UpdateProductWorkspaceResolvedReferenceRemovalButtons();
    }

    private async void ProductWorkspaceResolvedReferenceReassignmentButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ProductWorkspaceResolvedReferenceRemovalCandidatePresentation[] sources =
            ProductWorkspaceResolvedReferenceRemovalSelector.SelectedItems
                .OfType<ProductWorkspaceResolvedReferenceRemovalCandidatePresentation>()
                .ToArray();
        if (sources.Length == 0
            || sources.Length > ProductWorkspaceCommitCoordinator
                .MaximumResolvedReferenceReassignmentBatchSize
            || sources.Select(source => source.ContainerOrdinal)
                .Distinct()
                .Count() != 1
            || ProductWorkspaceResolvedReferenceReassignmentTargetSelector.SelectedItem
                is not ProductWorkspaceReferenceReassignmentTargetPresentation target)
        {
            return;
        }

        ContentDialog confirmation = new()
        {
            XamlRoot = RootLayout.XamlRoot,
            Title = $"把 {sources.Length} 个引用改归属到“{target.DisplayName}”？",
            Content = "整批将作为一次配置编辑提交并可整体撤销。不会移动、删除或重命名桌面文件。",
            PrimaryButtonText = "批量改归属",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        ProductWorkspaceResolvedReferenceReassignmentCommitResult result =
            _commitProductWorkspaceResolvedReferenceReassignment(
                _resolvedReferenceReassignment.EditRevision,
                sources,
                target);
        ProductWorkspaceResolvedReferenceRemovalStatus.Text = result.Status switch
        {
            ProductWorkspaceResolvedReferenceReassignmentCommitStatus.Accepted =>
                $"已将同一源方格内 {sources.Length} 个引用原子改归属；桌面文件未改变，可整批撤销一次。",
            ProductWorkspaceResolvedReferenceReassignmentCommitStatus
                .StaleEditRevision =>
                "工作区已经更新，请按最新引用和目标方格列表重新选择。",
            ProductWorkspaceResolvedReferenceReassignmentCommitStatus.ReducerRejected
                when result.EditError == ProductWorkspaceEditError.ContainerLocked =>
                "源方格或目标方格已经锁定，未改变归属。",
            ProductWorkspaceResolvedReferenceReassignmentCommitStatus.SaveRejected =>
                "保存控制器当前无法接受编辑；配置与桌面文件均未改变。",
            _ => "请选择同一源方格内 1–256 个已解析引用；整批未保存。",
        };
        AutomationProperties.SetItemStatus(
            ProductWorkspaceResolvedReferenceRemovalStatus,
            $"ResolvedReferenceReassignment:{result.Status}:" +
                $"Count={sources.Length}:Revision={result.EditRevision}:" +
                $"Atomic=True:Changed={result.IsAccepted}:" +
                "DesktopFilesChanged=False");
        UpdateProductWorkspaceResolvedReferenceRemovalButtons();
    }

    internal void ApplyProductWorkspaceReferenceReview(
        ProductWorkspaceReferenceReviewPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        _referenceReview = presentation;
        ProductWorkspaceReferenceReviewSelector.ItemsSource =
            presentation.Snapshot?.Items
                .Select(item =>
                    $"引用 {item.Ordinal} · {DescribeReferenceResolution(item.Resolution)}")
                .ToArray()
            ?? Array.Empty<string>();
        ProductWorkspaceReferenceReviewSelector.SelectedIndex =
            presentation.Snapshot?.Items.Count > 0 ? 0 : -1;

        int count = presentation.Snapshot?.Items.Count ?? 0;
        ProductWorkspaceReferenceReviewTitle.Text = count > 0
            ? $"发现 {count} 个待审查引用"
            : "没有待审查引用";
        ProductWorkspaceReferenceReviewDetail.Text = presentation.Error !=
            ProductWorkspaceReferenceReviewError.None
            ? "审查快照未通过校验；所有操作均保持关闭。"
            : presentation.Snapshot is null
                ? "等待正式产品会话与权威桌面目录；配置未改变。"
                : count == 0
                    ? "当前引用均已解析；配置未改变。"
                    : presentation.IsReadOnly
                        ? "备份会话保持只读；接受备份前不可执行引用修改。"
                        : "仅显示匿名序号。重选或移除会先校验，再进入安全保存队列。";
        ProductWorkspaceReferenceReviewStatus.Text = "尚未执行引用操作；配置未改变。";
        AutomationProperties.SetItemStatus(
            ProductWorkspaceReferenceReviewStatus,
            presentation.Snapshot is null
                ? "ReferenceReviewUnavailable:Changed=False"
                : $"ReferenceReviewReady:Generation={presentation.Snapshot.CatalogGeneration}:" +
                    $"Revision={presentation.Snapshot.EditRevision}:Items={count}:" +
                    $"ReadOnly={presentation.IsReadOnly}:Changed=False");
        UpdateProductWorkspaceReferenceButtons();
        UpdateProductWorkspaceOpenReviewButton();
    }

    private void ProductWorkspaceReferenceReviewSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) =>
        UpdateProductWorkspaceReferenceButtons();

    private void UpdateProductWorkspaceReferenceButtons()
    {
        int index = ProductWorkspaceReferenceReviewSelector.SelectedIndex;
        bool enabled = !_referenceReview.IsReadOnly
            && _referenceReview.Snapshot is not null
            && index >= 0
            && index < _referenceReview.Snapshot.Items.Count
            && !_referenceReview.Snapshot.Items[index].ContainerLocked;
        ProductWorkspaceReferenceKeepButton.IsEnabled = enabled;
        ProductWorkspaceReferenceReselectButton.IsEnabled =
            enabled && _referenceReview.Candidates.Count > 0;
        ProductWorkspaceReferenceRemoveButton.IsEnabled = enabled;
    }

    private void ProductWorkspaceReferenceKeepButton_Click(
        object sender,
        RoutedEventArgs e) =>
        RunProductWorkspaceReferenceCommit(
            ProductWorkspaceReferenceAction.Keep,
            confirmed: false,
            replacement: null);

    private async void ProductWorkspaceReferenceReselectButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ProductWorkspaceReferenceReviewToken? token = GetSelectedReferenceToken();
        if (token is null)
        {
            ApplyProductWorkspaceReferenceCommitStatus(
                ProductWorkspaceReferenceGateError.ItemChanged,
                ProductWorkspaceReferenceCommitStatus.GateRejected,
                editRevision: _referenceReview.Snapshot?.EditRevision ?? 0);
            return;
        }

        ProductWorkspaceReferenceCandidatePresentation[] candidates =
            _referenceReview.Candidates.ToArray();
        var selector = new ComboBox
        {
            Header = "请选择当前桌面目录中的匿名候选",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = candidates.Select(candidate =>
                $"候选 {candidate.Ordinal} · {candidate.KindLabel}")
                .ToArray(),
        };
        var dialog = new ContentDialog
        {
            Title = "重选引用并保存",
            Content = selector,
            PrimaryButtonText = "确认重选",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootLayout.XamlRoot,
        };
        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        if (selector.SelectedIndex < 0
            || selector.SelectedIndex >= candidates.Length)
        {
            ApplyProductWorkspaceReferenceCommitStatus(
                ProductWorkspaceReferenceGateError.ReplacementRequired,
                ProductWorkspaceReferenceCommitStatus.GateRejected,
                editRevision: token.EditRevision);
            return;
        }

        RunProductWorkspaceReferenceCommit(
            token,
            ProductWorkspaceReferenceAction.Replace,
            confirmed: true,
            candidates[selector.SelectedIndex]);
    }

    private async void ProductWorkspaceReferenceRemoveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ProductWorkspaceReferenceReviewToken? token = GetSelectedReferenceToken();
        if (token is null)
        {
            ApplyProductWorkspaceReferenceCommitStatus(
                ProductWorkspaceReferenceGateError.ItemChanged,
                ProductWorkspaceReferenceCommitStatus.GateRejected,
                editRevision: _referenceReview.Snapshot?.EditRevision ?? 0);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "移除引用并保存",
            Content = "确认从 Long方格配置中移除此引用并进入安全保存队列。只移除配置引用，不会删除、移动或重命名桌面文件。",
            PrimaryButtonText = "确认移除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootLayout.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            RunProductWorkspaceReferenceCommit(
                token,
                ProductWorkspaceReferenceAction.Remove,
                confirmed: true,
                replacement: null);
        }
    }

    private void RunProductWorkspaceReferenceCommit(
        ProductWorkspaceReferenceAction action,
        bool confirmed,
        ProductWorkspaceReferenceCandidatePresentation? replacement)
    {
        ProductWorkspaceReferenceReviewToken? token = GetSelectedReferenceToken();
        if (token is null)
        {
            ApplyProductWorkspaceReferenceCommitStatus(
                ProductWorkspaceReferenceGateError.ItemChanged,
                ProductWorkspaceReferenceCommitStatus.GateRejected,
                editRevision: _referenceReview.Snapshot?.EditRevision ?? 0);
            return;
        }

        RunProductWorkspaceReferenceCommit(token, action, confirmed, replacement);
    }

    private void RunProductWorkspaceReferenceCommit(
        ProductWorkspaceReferenceReviewToken token,
        ProductWorkspaceReferenceAction action,
        bool confirmed,
        ProductWorkspaceReferenceCandidatePresentation? replacement)
    {
        ProductWorkspaceReferenceCommitResult result =
            _commitProductWorkspaceReferenceAction(
                token,
                action,
                confirmed,
                replacement);
        ApplyProductWorkspaceReferenceCommitStatus(
            result.GateError,
            result.Status,
            result.EditRevision);
    }

    private ProductWorkspaceReferenceReviewToken? GetSelectedReferenceToken()
    {
        int index = ProductWorkspaceReferenceReviewSelector.SelectedIndex;
        return _referenceReview.Snapshot is not null && index >= 0
            && index < _referenceReview.Snapshot.Items.Count
                ? _referenceReview.Snapshot.Items[index].Token
                : null;
    }

    private void ApplyProductWorkspaceReferenceCommitStatus(
        ProductWorkspaceReferenceGateError error,
        ProductWorkspaceReferenceCommitStatus status,
        long editRevision)
    {
        ProductWorkspaceReferenceReviewStatus.Text = status switch
        {
            ProductWorkspaceReferenceCommitStatus.Accepted =>
                "引用更改已进入安全保存队列；Long方格配置已更新，桌面文件未改变。",
            ProductWorkspaceReferenceCommitStatus.Kept =>
                "已选择保留引用；配置未改变。",
            ProductWorkspaceReferenceCommitStatus.SaveRejected =>
                "保存控制器未接受更改；配置和桌面文件均未改变。",
            ProductWorkspaceReferenceCommitStatus.InvalidState =>
                "产品状态未通过提交校验；配置和桌面文件均未改变。",
            _ when error == ProductWorkspaceReferenceGateError.StaleCatalogGeneration =>
                "桌面目录已刷新，请重新选择；配置未改变。",
            _ when error == ProductWorkspaceReferenceGateError.StaleEditRevision =>
                "工作区已有更新，请重新审查；配置未改变。",
            _ when error == ProductWorkspaceReferenceGateError.ContainerLocked =>
                "分组已锁定，操作被拒绝；配置未改变。",
            _ when error == ProductWorkspaceReferenceGateError.ConfirmationRequired =>
                "需要明确确认，操作未执行；配置未改变。",
            _ when error == ProductWorkspaceReferenceGateError.ReplacementRequired =>
                "请选择一个匿名候选；配置未改变。",
            _ when error == ProductWorkspaceReferenceGateError.ReplacementNotFound =>
                "候选已不存在，请刷新后重选；配置未改变。",
            _ when error == ProductWorkspaceReferenceGateError.ReplacementAmbiguous =>
                "候选身份不唯一，操作被拒绝；配置未改变。",
            _ => "引用状态已变化或校验失败；配置未改变。",
        };
        AutomationProperties.SetItemStatus(
            ProductWorkspaceReferenceReviewStatus,
            $"ReferenceCommit:{status}:Gate={error}:EditRevision={editRevision}:" +
            $"ConfigurationChanged={status == ProductWorkspaceReferenceCommitStatus.Accepted}:" +
            "DesktopFilesChanged=False");
    }

    private static string DescribeReferenceResolution(
        ProductItemReferenceResolution resolution) => resolution switch
        {
            ProductItemReferenceResolution.Missing => "缺失",
            ProductItemReferenceResolution.TypeChanged => "类型变化",
            ProductItemReferenceResolution.Ambiguous => "身份歧义",
            ProductItemReferenceResolution.UnsupportedTarget => "不支持的目标",
            _ => "状态变化",
        };


    private static (
        string Title,
        string Detail,
        string Summary,
        string AutomationStatus,
        Symbol Icon) DescribeProductWorkspaceSession(
        ProductWorkspaceSessionSnapshot snapshot)
    {
        string source = snapshot.Source.ToString();
        string catalog = snapshot.CatalogAvailability.ToString();
        string readOnly = snapshot.IsReadOnly.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        string statusPrefix =
            $"WorkspaceSession{snapshot.Status}:Source={source}:Catalog={catalog}:ReadOnly={readOnly}";
        return snapshot.Status switch
        {
            ProductWorkspaceSessionStatus.Loading => (
                "正在建立产品会话",
                "正在安全读取正式配置；不会把空目录清单冒充当前桌面状态。",
                "引用解析计数尚不可用",
                statusPrefix,
                Symbol.Clock),
            ProductWorkspaceSessionStatus.NoSavedConfiguration => (
                "尚无正式产品会话",
                "没有发现已保存配置；本次启动不会自动创建配置或提交匿名示例。",
                "正式容器 0 · 正式引用 0",
                statusPrefix,
                Symbol.Document),
            ProductWorkspaceSessionStatus.AwaitingCatalog => (
                "等待受控桌面目录",
                "配置已通过校验，但当前 Desktop Catalog 尚未连接；不会把所有引用误判为缺失。",
                snapshot.IsReadOnly
                    ? "配置来源：已验证备份 · 保持只读"
                    : "配置来源：主配置 · 尚未解析引用",
                statusPrefix,
                Symbol.Clock),
            ProductWorkspaceSessionStatus.Ready => (
                "正式产品会话已解析",
                "配置已与当前受控 Desktop Catalog 对齐；开发期界面仍未开放普通编辑提交。",
                DescribeProductWorkspaceResolutionSummary(snapshot.Summary),
                AddProductWorkspaceCounts(statusPrefix, snapshot.Summary),
                Symbol.Accept),
            ProductWorkspaceSessionStatus.RecoveredBackupReadOnly => (
                "备份产品会话已只读解析",
                "已验证备份与当前受控 Desktop Catalog 完成对齐；接受备份前不会开放编辑。",
                DescribeProductWorkspaceResolutionSummary(snapshot.Summary),
                AddProductWorkspaceCounts(statusPrefix, snapshot.Summary),
                Symbol.Permissions),
            ProductWorkspaceSessionStatus.SafeMode => (
                "产品会话处于安全模式",
                "主配置与备份均不可安全加载；没有创建产品状态，也没有覆盖损坏证据。",
                "引用解析未执行",
                statusPrefix,
                Symbol.Important),
            ProductWorkspaceSessionStatus.Failed => (
                "产品会话未能建立",
                DescribeProductWorkspaceSessionFailure(snapshot.Failure),
                "引用解析未提交",
                $"{statusPrefix}:Failure={snapshot.Failure}",
                Symbol.Important),
            _ => (
                "产品会话状态不可用",
                "当前无法确认正式产品状态；不会执行保存或文件操作。",
                "引用解析未执行",
                $"WorkspaceSessionUnavailable:Source={source}:Catalog={catalog}:ReadOnly=True",
                Symbol.Important),
        };
    }

    private static string DescribeProductWorkspaceResolutionSummary(
        ProductWorkspaceResolutionSummary summary) =>
        $"已解析 {summary.Resolved} · 缺失 {summary.Missing} · " +
        $"类型变化 {summary.TypeChanged} · 歧义 {summary.Ambiguous} · " +
        $"不支持 {summary.UnsupportedTarget}";

    private static string AddProductWorkspaceCounts(
        string prefix,
        ProductWorkspaceResolutionSummary summary) =>
        $"{prefix}:Resolved={summary.Resolved}:Missing={summary.Missing}:" +
        $"TypeChanged={summary.TypeChanged}:Ambiguous={summary.Ambiguous}:" +
        $"Unsupported={summary.UnsupportedTarget}";

    private static string DescribeProductWorkspaceSessionFailure(
        ProductWorkspaceSessionFailure failure) => failure switch
        {
            ProductWorkspaceSessionFailure.InconsistentLoadResult =>
                "配置存储返回了不一致状态；已停止建立会话且不会写回。",
            ProductWorkspaceSessionFailure.InvalidConfiguration =>
                "配置未通过正式产品状态校验；已停止建立会话且不会写回。",
            ProductWorkspaceSessionFailure.InvalidCatalog =>
                "Desktop Catalog 快照未通过身份校验；不会自动绑定或删除引用。",
            _ => "产品会话加载没有完成；不会执行额外配置或文件操作。",
        };

    internal void ApplyProductWorkspaceSaveState(
        ProductWorkspaceSaveSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        (string title,
            string detail,
            string automationStatus,
            Symbol icon,
            bool retryVisible) = DescribeProductWorkspaceSaveState(snapshot);
        ProductSaveStatusTitle.Text = title;
        ProductSaveStatusDetail.Text = detail;
        ProductSaveStatusIcon.Symbol = icon;
        ProductSaveRetryButton.Visibility = retryVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProductSaveRetryButton.IsEnabled = retryVisible && snapshot.CanRetry;
        bool configurationTransactionsEnabled = snapshot.Status is
            ProductWorkspaceSaveStatus.Clean or ProductWorkspaceSaveStatus.Saved;
        ImportConfigurationButton.IsEnabled = configurationTransactionsEnabled;
        ExportConfigurationButton.IsEnabled = configurationTransactionsEnabled;
        AutomationProperties.SetItemStatus(
            ProductSaveStatusDetail,
            automationStatus);
    }

    private static (
        string Title,
        string Detail,
        string AutomationStatus,
        Symbol Icon,
        bool RetryVisible) DescribeProductWorkspaceSaveState(
        ProductWorkspaceSaveSnapshot snapshot)
    {
        string revision = snapshot.CurrentRevision.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        return snapshot.Status switch
        {
            ProductWorkspaceSaveStatus.Clean => (
                "自动保存待命",
                "尚无需要保存的产品编辑；匿名示例仍只存在于内存中。",
                $"WorkspaceSaveClean:Revision={revision}:Motion=Static",
                Symbol.Save,
                false),
            ProductWorkspaceSaveStatus.WaitingForDebounce => (
                "正在准备保存",
                "正在合并连续编辑；不会读取界面显示名称作为文件身份。",
                $"WorkspaceSaveWaiting:Revision={revision}:Motion=Static",
                Symbol.Clock,
                false),
            ProductWorkspaceSaveStatus.Saving
                when snapshot.Activity == ProductWorkspaceSaveActivity.Retry => (
                    "正在重试保存",
                    "正在重试最近一次明确保留的配置快照。",
                    $"WorkspaceSaveRetrying:Revision={revision}:Motion=Static",
                    Symbol.Sync,
                    false),
            ProductWorkspaceSaveStatus.Saving => (
                "正在安全保存",
                "正在提交已验证的配置快照；原始桌面文件保持不变。",
                $"WorkspaceSaveSaving:Revision={revision}:Motion=Static",
                Symbol.Sync,
                false),
            ProductWorkspaceSaveStatus.Saved => (
                "更改已保存",
                "最近一次产品编辑已安全保存。",
                $"WorkspaceSaveSaved:Revision={revision}:Motion=Static",
                Symbol.Accept,
                false),
            ProductWorkspaceSaveStatus.Failed => DescribeProductWorkspaceSaveFailure(
                snapshot,
                revision),
            _ => (
                "保存状态不可用",
                "当前无法确认保存状态；不会执行额外文件操作。",
                $"WorkspaceSaveUnavailable:Revision={revision}:Motion=Static",
                Symbol.Important,
                false),
        };
    }

    private static (
        string Title,
        string Detail,
        string AutomationStatus,
        Symbol Icon,
        bool RetryVisible) DescribeProductWorkspaceSaveFailure(
        ProductWorkspaceSaveSnapshot snapshot,
        string revision)
    {
        string detail = snapshot.Failure switch
        {
            ProductWorkspaceSaveFailure.InvalidConfiguration =>
                "更改未通过配置校验；请修正产品状态后再保存。",
            ProductWorkspaceSaveFailure.DamagedEvidence =>
                "配置证据需要先安全处理；现有证据没有被覆盖。",
            ProductWorkspaceSaveFailure.WriteLeaseUnavailable =>
                "另一项配置操作正在进行；可以稍后重试。",
            ProductWorkspaceSaveFailure.IoFailure =>
                "暂时无法安全写入配置；没有覆盖已提交内容。",
            ProductWorkspaceSaveFailure.RetryUnavailable =>
                "最近失败的快照已不可重试；请进行新的有效编辑。",
            _ => "保存没有完成；不会执行额外文件操作。",
        };
        string failure = snapshot.Failure.ToString();
        return (
            "更改尚未保存",
            detail,
            $"WorkspaceSaveFailed:{failure}:Retry={snapshot.CanRetry}:Revision={revision}:Motion=Static",
            Symbol.Important,
            snapshot.CanRetry);
    }

    private void ProductSaveRetryButton_Click(object sender, RoutedEventArgs e)
    {
        ProductWorkspaceSaveRetryResult result = _retryProductWorkspaceSave();
        if (result.Status != ProductWorkspaceSaveRetryStatus.Accepted)
        {
            ApplyProductWorkspaceSaveState(result.Snapshot);
        }
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
            RuntimeCapabilityState.ConnectedReadOnly => "物理桌面目录已只读连接",
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

        ApplyTwoActionResponsiveLayout(
            ProductWorkspaceResolvedReferenceSelectionActionGrid,
            ProductWorkspaceResolvedReferenceSelectFirstBatchButton,
            ProductWorkspaceResolvedReferenceClearSelectionButton,
            compact);
        ApplyTwoActionResponsiveLayout(
            ProductWorkspaceResolvedReferenceRemovalSelectionActionGrid,
            ProductWorkspaceResolvedReferenceSelectContainerBatchButton,
            ProductWorkspaceResolvedReferenceRemovalClearSelectionButton,
            compact);

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

    private static void ApplyTwoActionResponsiveLayout(
        Grid grid,
        FrameworkElement primary,
        FrameworkElement secondary,
        bool compact)
    {
        grid.ColumnSpacing = compact ? 0 : 8;
        grid.RowSpacing = compact ? 8 : 0;
        SetGridPosition(primary, row: 0, column: 0, columnSpan: compact ? 2 : 1);
        SetGridPosition(
            secondary,
            row: compact ? 1 : 0,
            column: compact ? 0 : 1,
            columnSpan: compact ? 2 : 1);
    }

    private static void RaiseLiveRegionChanged(FrameworkElement element)
    {
        AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(element)
            ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
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
            RemoveConfigurationEvidenceButton.IsEnabled = false;
            string suffix = inventory.Truncated
                ? "；扫描已达到安全上限，数量与容量为至少值"
                : string.Empty;
            if (inventory.SkippedUnsafeCount > 0)
            {
                suffix += $"；已跳过 {inventory.SkippedUnsafeCount} 个重解析点";
            }

            string observed =
                $"观察到 {inventory.ObservedItemCount} 条、合计 " +
                $"{FormatEvidenceSize(inventory.ObservedSizeBytes)}";
            if (inventory.OldestObservedArchivedUtc is DateTimeOffset oldest)
            {
                observed += $"，最早归档于 {oldest.ToLocalTime():yyyy-MM-dd HH:mm}";
            }
            SetEvidenceStatus(
                inventory.ObservedItemCount == 0
                    ? "没有发现由 Long方格生成的配置归档证据。"
                    : $"{observed}；已列出 {inventory.Items.Count} 条匿名元数据{suffix}。",
                inventory.Truncated ? "EvidenceLoaded:Truncated" : "EvidenceLoaded");
        }
        catch (ProductConfigurationExportException exception)
        {
            _configurationEvidenceInventory = null;
            ConfigurationEvidenceList.ItemsSource = null;
            ExportConfigurationEvidenceButton.IsEnabled = false;
            RemoveConfigurationEvidenceButton.IsEnabled = false;
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
        bool hasSelection =
            _configurationEvidenceInventory is not null
            && selectedIndex >= 0
            && selectedIndex < _configurationEvidenceInventory.Items.Count;
        ExportConfigurationEvidenceButton.IsEnabled = hasSelection;
        RemoveConfigurationEvidenceButton.IsEnabled = hasSelection;
        AutomationProperties.SetItemStatus(
            ConfigurationEvidenceList,
            hasSelection
                ? "EvidenceSelectedForExplicitActions"
                : "NoEvidenceSelected");
    }

    private async void RemoveConfigurationEvidenceButton_Click(
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
        RemoveConfigurationEvidenceButton.IsEnabled = false;
        try
        {
            ContentDialog confirmation = new()
            {
                XamlRoot = RootLayout.XamlRoot,
                Title = "永久清理这条配置证据？",
                Content =
                    $"所选条目为 {DescribeEvidenceOrigin(item.Origin)} / " +
                    $"{DescribeEvidenceRole(item.Role)}，大小 {FormatEvidenceSize(item.SizeBytes)}。" +
                    "此操作只清理当前明确选择的一条证据，无法撤销，也不会自动清理其他条目。" +
                    "如需保留副本，请先取消并使用“导出所选证据”。",
                PrimaryButtonText = "永久清理所选证据",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };
            ContentDialogResult result = await confirmation.ShowAsync();
            if (result is not ContentDialogResult.Primary)
            {
                SetEvidenceStatus(
                    "已取消清理，没有删除任何证据。",
                    "EvidenceRemovalCancelled");
                return;
            }

            SetEvidenceStatus(
                "正在复核并清理唯一选中的证据……",
                "EvidenceRemovalInProgress");
            ProductConfigurationEvidenceRemovalResult removal =
                await _removeConfigurationEvidence(item);
            _configurationEvidenceInventory = null;
            ConfigurationEvidenceList.ItemsSource = null;
            ConfigurationEvidenceList.SelectedIndex = -1;
            SetEvidenceStatus(
                $"已永久清理 1 条 {DescribeEvidenceOrigin(removal.Origin)} / " +
                $"{DescribeEvidenceRole(removal.Role)}证据，释放 " +
                $"{FormatEvidenceSize(removal.SizeBytes)}；请刷新复核剩余容量。",
                "EvidenceRemovalCommitted:SingleItem");
        }
        catch (ProductConfigurationExportException exception)
        {
            SetEvidenceStatus(
                DescribeExportFailure(exception.Error),
                $"EvidenceRemovalFailed:{exception.Error}");
        }
        finally
        {
            bool selectionStillValid =
                _configurationEvidenceInventory is not null
                && selectedIndex == ConfigurationEvidenceList.SelectedIndex;
            ExportConfigurationEvidenceButton.IsEnabled = selectionStillValid;
            RemoveConfigurationEvidenceButton.IsEnabled = selectionStillValid;
        }
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
        RemoveConfigurationEvidenceButton.IsEnabled = false;
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
            bool selectionStillValid =
                _configurationEvidenceInventory is not null
                && selectedIndex == ConfigurationEvidenceList.SelectedIndex;
            ExportConfigurationEvidenceButton.IsEnabled = selectionStillValid;
            RemoveConfigurationEvidenceButton.IsEnabled = selectionStillValid;
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
                "操作尚未获得明确确认，没有写入、导出或清理证据。",
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
                "所选证据已不存在或不再属于可处理的 Long方格归档；请刷新清单。",
            ProductConfigurationExportError.EvidenceChanged =>
                "所选证据在刷新后发生变化；没有执行操作，请重新刷新清单。",
            ProductConfigurationExportError.EvidenceTooLarge =>
                "所选证据超过 64 MiB 单次导出上限，没有写入目标文件。",
            ProductConfigurationExportError.EvidenceVerificationFailed =>
                "证据副本未通过逐字节完整性验证，没有发布目标文件。",
            ProductConfigurationExportError.WriteLeaseUnavailable =>
                "配置存储正被另一项操作使用；没有清理证据，请稍后重试。",
            ProductConfigurationExportError.DestinationUnavailable =>
                "所选文件夹暂时不可用，没有覆盖任何文件。",
            ProductConfigurationExportError.IoFailure =>
                "配置或证据暂时无法安全处理；没有暴露文件内容。",
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
