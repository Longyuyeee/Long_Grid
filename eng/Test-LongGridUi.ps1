[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64')]
    [string]$Architecture = 'x64',

    [switch]$NoBuild,
    [switch]$ContractOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$xamlPath = Join-Path $projectRoot 'src\LongGrid.App\MainWindow.xaml'
$codeBehindPath = Join-Path $projectRoot 'src\LongGrid.App\MainWindow.xaml.cs'
$appCodePath = Join-Path $projectRoot 'src\LongGrid.App\App.xaml.cs'
$referenceReviewCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceReferenceReview.cs'
$referenceCommitCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\Configuration\ProductWorkspaceReferenceCommitCoordinator.cs'
$referencePresentationCodePath = Join-Path $projectRoot `
    'src\LongGrid.App\ProductWorkspaceReferenceReviewPresentation.cs'
$resolvedReferenceAddPresentationCodePath = Join-Path $projectRoot `
    'src\LongGrid.App\ProductWorkspaceResolvedReferenceAddPresentation.cs'
$resolvedReferenceRemovalPresentationCodePath = Join-Path $projectRoot `
    'src\LongGrid.App\ProductWorkspaceResolvedReferenceRemovalPresentation.cs'
$resolvedReferenceReassignmentPresentationCodePath = Join-Path $projectRoot `
    'src\LongGrid.App\ProductWorkspaceResolvedReferenceReassignmentPresentation.cs'
$workspaceReadModelCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceReadModel.cs'
$workspaceReadPresentationCodePath = Join-Path $projectRoot `
    'src\LongGrid.App\ProductWorkspaceReadPresentation.cs'
$containerEditPresentationCodePath = Join-Path $projectRoot `
    'src\LongGrid.App\ProductWorkspaceContainerEditPresentation.cs'
$layoutRecoveryPreviewCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceLayoutRecoveryPreview.cs'
$layoutRecoveryReviewCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceLayoutRecoveryReview.cs'
$layoutRecoveryUndoCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceLayoutRecoveryUndo.cs'
$realWindowRecoveryAdmissionCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceRealWindowRecoveryAdmission.cs'
$windowCompositeTransactionCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceWindowCompositeTransaction.cs'
$layoutRecoveryPresentationCodePath = Join-Path $projectRoot `
    'src\LongGrid.App\ProductWorkspaceLayoutRecoveryPresentation.cs'
$displayTopologyReaderCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDisplayTopologyReader.cs'
$displayTopologyControllerCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDisplayTopologyController.cs'
$windowsDisplayTopologySourceCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\WindowsDisplayTopologySource.cs'
$desktopHostWindowBridgeCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopHostWindowBridge.cs'
$windowsDesktopHostWindowInspectorCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\WindowsProductDesktopHostWindowInspector.cs'
$verifiedWindowBatchAdapterCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopHostVerifiedWindowBatchAdapter.cs'
$desktopHostThreadDispatcherCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopHostThreadDispatcher.cs'
$desktopHostInputControllerCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopHostInputController.cs'
$configurationCompareExchangeCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\Configuration\ProductConfigurationStore.CompareExchange.cs'
$compositeConfigurationAdapterCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\Configuration\ProductWorkspaceCompositeConfigurationAdapter.cs'
$compositeLifecycleGuardCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\Configuration\ProductWorkspaceCompositeLifecycleGuard.cs'
$compositeInputGateCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\Configuration\ProductWorkspaceCompositeDesktopHostInputGate.cs'
$projectPath = Join-Path $projectRoot 'src\LongGrid.App\LongGrid.App.csproj'
$runtimeIdentifier = "win-$Architecture"

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-XamlNodeByAutomationId {
    param(
        [System.Xml.XmlDocument]$Document,
        [string]$AutomationId
    )

    $matches = @(
        $Document.SelectNodes('//*') |
            Where-Object {
                $_.GetAttribute('AutomationProperties.AutomationId') -eq $AutomationId
            }
    )

    Assert-Condition ($matches.Count -eq 1) `
        "Expected exactly one XAML node with AutomationId '$AutomationId'; found $($matches.Count)."
    return $matches[0]
}

function Test-SourceContract {
    [xml]$document = Get-Content -LiteralPath $xamlPath -Raw -Encoding UTF8
    $codeBehind = Get-Content -LiteralPath $codeBehindPath -Raw -Encoding UTF8
    $appCode = Get-Content -LiteralPath $appCodePath -Raw -Encoding UTF8
    $referenceReviewCode = Get-Content `
        -LiteralPath $referenceReviewCodePath `
        -Raw `
        -Encoding UTF8
    $referenceCommitCode = Get-Content `
        -LiteralPath $referenceCommitCodePath `
        -Raw `
        -Encoding UTF8
    $referencePresentationCode = Get-Content `
        -LiteralPath $referencePresentationCodePath `
        -Raw `
        -Encoding UTF8
    $resolvedReferenceAddPresentationCode = Get-Content `
        -LiteralPath $resolvedReferenceAddPresentationCodePath `
        -Raw `
        -Encoding UTF8
    $resolvedReferenceRemovalPresentationCode = Get-Content `
        -LiteralPath $resolvedReferenceRemovalPresentationCodePath `
        -Raw `
        -Encoding UTF8
    $resolvedReferenceReassignmentPresentationCode = Get-Content `
        -LiteralPath $resolvedReferenceReassignmentPresentationCodePath `
        -Raw `
        -Encoding UTF8
    $workspaceReadModelCode = Get-Content `
        -LiteralPath $workspaceReadModelCodePath `
        -Raw `
        -Encoding UTF8
    $workspaceReadPresentationCode = Get-Content `
        -LiteralPath $workspaceReadPresentationCodePath `
        -Raw `
        -Encoding UTF8
    $containerEditPresentationCode = Get-Content `
        -LiteralPath $containerEditPresentationCodePath `
        -Raw `
        -Encoding UTF8
    $layoutRecoveryPreviewCode = Get-Content `
        -LiteralPath $layoutRecoveryPreviewCodePath `
        -Raw `
        -Encoding UTF8
    $layoutRecoveryReviewCode = Get-Content `
        -LiteralPath $layoutRecoveryReviewCodePath `
        -Raw `
        -Encoding UTF8
    $layoutRecoveryUndoCode = Get-Content `
        -LiteralPath $layoutRecoveryUndoCodePath `
        -Raw `
        -Encoding UTF8
    $realWindowRecoveryAdmissionCode = Get-Content `
        -LiteralPath $realWindowRecoveryAdmissionCodePath `
        -Raw `
        -Encoding UTF8
    $windowCompositeTransactionCode = Get-Content `
        -LiteralPath $windowCompositeTransactionCodePath `
        -Raw `
        -Encoding UTF8
    $layoutRecoveryPresentationCode = Get-Content `
        -LiteralPath $layoutRecoveryPresentationCodePath `
        -Raw `
        -Encoding UTF8
    $displayTopologyReaderCode = Get-Content `
        -LiteralPath $displayTopologyReaderCodePath `
        -Raw `
        -Encoding UTF8
    $displayTopologyControllerCode = Get-Content `
        -LiteralPath $displayTopologyControllerCodePath `
        -Raw `
        -Encoding UTF8
    $windowsDisplayTopologySourceCode = Get-Content `
        -LiteralPath $windowsDisplayTopologySourceCodePath `
        -Raw `
        -Encoding UTF8
    $desktopHostWindowBridgeCode = Get-Content `
        -LiteralPath $desktopHostWindowBridgeCodePath `
        -Raw `
        -Encoding UTF8
    $windowsDesktopHostWindowInspectorCode = Get-Content `
        -LiteralPath $windowsDesktopHostWindowInspectorCodePath `
        -Raw `
        -Encoding UTF8
    $verifiedWindowBatchAdapterCode = Get-Content `
        -LiteralPath $verifiedWindowBatchAdapterCodePath `
        -Raw `
        -Encoding UTF8
    $desktopHostThreadDispatcherCode = Get-Content `
        -LiteralPath $desktopHostThreadDispatcherCodePath `
        -Raw `
        -Encoding UTF8
    $desktopHostInputControllerCode = Get-Content `
        -LiteralPath $desktopHostInputControllerCodePath `
        -Raw `
        -Encoding UTF8
    $configurationCompareExchangeCode = Get-Content `
        -LiteralPath $configurationCompareExchangeCodePath `
        -Raw `
        -Encoding UTF8
    $compositeConfigurationAdapterCode = Get-Content `
        -LiteralPath $compositeConfigurationAdapterCodePath `
        -Raw `
        -Encoding UTF8
    $compositeLifecycleGuardCode = Get-Content `
        -LiteralPath $compositeLifecycleGuardCodePath `
        -Raw `
        -Encoding UTF8
    $compositeInputGateCode = Get-Content `
        -LiteralPath $compositeInputGateCodePath `
        -Raw `
        -Encoding UTF8
    $requiredIds = @(
        'LongGridRoot',
        'ShellNavigation',
        'NavOverview',
        'NavFirstRun',
        'NavAppearance',
        'NavSafety',
        'NavRecovery',
        'OverviewPanel',
        'ConfigurationRecoveryActionButton',
        'FirstRunPanel',
        'AppearancePanel',
        'SafetyPanel',
        'ImportConfigurationButton',
        'ConfigurationImportStatus',
        'ExportConfigurationButton',
        'ConfigurationExportStatus',
        'RefreshConfigurationEvidenceButton',
        'ConfigurationEvidenceStatus',
        'ConfigurationEvidenceList',
        'ExportConfigurationEvidenceButton',
        'RemoveConfigurationEvidenceButton',
        'RecoveryPanel',
        'RecoverySafetyBanner',
        'RecoveryAutomaticScenarioButton',
        'RecoveryReviewScenarioButton',
        'RecoveryBlockedScenarioButton',
        'RecoveryPlanStatus',
        'RecoveryDiffPanel',
        'RecoverySummaryTitle',
        'RecoveryDiffDetail',
        'RecoverySafetyDetail',
        'ReviewRecoveryButton',
        'ExpireRecoveryPreviewButton',
        'CancelRecoveryPreviewButton',
        'FirstRunSafetyBanner',
        'SuggestedStartChoice',
        'BlankStartChoice',
        'StartChoiceStatus',
        'SafeReferenceMode',
        'ManagedMoveMode',
        'OrganizationOutcomeTitle',
        'OrganizationPreviewButton',
        'OrganizationPreviewStatus',
        'PracticeContainerName',
        'CreatePracticeContainerButton',
        'PracticeContainerPreview',
        'PracticeContainerNameValue',
        'PracticeContainerCountValue',
        'PracticeItemsList',
        'PracticeItemOne',
        'PracticeItemTwo',
        'PracticeItemThree',
        'AddPracticeItemsButton',
        'UndoPracticeContainerButton',
        'PracticeActivityStatus',
        'DropSafeReferenceButton',
        'DropReassignButton',
        'DropManagedMoveButton',
        'DropActionStatus',
        'CurrentModeCard',
        'FileOperationCard',
        'DesktopHostCard',
        'CurrentModeValue',
        'FileOperationValue',
        'DesktopHostValue',
        'ProductDesktopCatalogCard',
        'ProductDesktopCatalogTitle',
        'ProductDesktopCatalogDetail',
        'ProductDesktopCatalogGeneration',
        'ProductDesktopCatalogRefreshButton',
        'ProductWorkspaceSessionCard',
        'ProductWorkspaceSessionTitle',
        'ProductWorkspaceSessionDetail',
        'ProductWorkspaceSessionSummary',
        'ProductWorkspaceLayoutRecoveryCard',
        'ProductWorkspaceLayoutRecoveryTitle',
        'ProductWorkspaceLayoutRecoveryDetail',
        'ProductWorkspaceLayoutRecoverySummary',
        'ProductWorkspaceLayoutRecoveryConfirmButton',
        'ProductWorkspaceLayoutRecoveryUndoButton',
        'ProductWorkspaceViewCard',
        'ProductWorkspaceViewTitle',
        'ProductWorkspaceViewDetail',
        'ProductWorkspaceContainerEditorPanel',
        'ProductWorkspaceContainerEditSelector',
        'ProductWorkspaceContainerNameEditor',
        'ProductWorkspaceContainerCreateButton',
        'ProductWorkspaceContainerRenameButton',
        'ProductWorkspaceContainerLockButton',
        'ProductWorkspaceContainerCollapseButton',
        'ProductWorkspaceContainerColorSelector',
        'ProductWorkspaceContainerOpacitySelector',
        'ProductWorkspaceContainerAppearanceButton',
        'ProductWorkspaceContainerPositionSelector',
        'ProductWorkspaceContainerSizeSelector',
        'ProductWorkspaceContainerPlacementButton',
        'ProductWorkspaceResolvedReferenceSelector',
        'ProductWorkspaceResolvedReferenceAddButton',
        'ProductWorkspaceResolvedReferenceAddStatus',
        'ProductWorkspaceResolvedReferenceRemovalSelector',
        'ProductWorkspaceResolvedReferenceReassignmentTargetSelector',
        'ProductWorkspaceResolvedReferenceRemovalButton',
        'ProductWorkspaceResolvedReferenceReassignmentButton',
        'ProductWorkspaceResolvedReferenceRemovalUndoButton',
        'ProductWorkspaceResolvedReferenceRemovalStatus',
        'ProductWorkspaceContainerEditStatus',
        'ProductWorkspaceContainerList',
        'ProductWorkspaceViewStatus',
        'ProductWorkspaceReferenceReviewCard',
        'ProductWorkspaceReferenceReviewTitle',
        'ProductWorkspaceReferenceReviewDetail',
        'ProductWorkspaceReferenceReviewSelector',
        'ProductWorkspaceReferenceKeepButton',
        'ProductWorkspaceReferenceReselectButton',
        'ProductWorkspaceReferenceRemoveButton',
        'ProductWorkspaceReferenceReviewStatus',
        'ProductSaveStatusCard',
        'ProductSaveStatusTitle',
        'ProductSaveStatusDetail',
        'ProductSaveMotionPolicy',
        'ProductSaveRetryButton',
        'ResponsiveStatusText',
        'ContentScrollViewer',
        'ThemeSystem',
        'ThemeLight',
        'ThemeDark',
        'ThemeStatusText'
    )

    foreach ($automationId in $requiredIds) {
        $null = Get-XamlNodeByAutomationId $document $automationId
    }

    $rootNode = Get-XamlNodeByAutomationId $document 'LongGridRoot'
    Assert-Condition ($rootNode.GetAttribute('AutomationProperties.Name').Length -gt 0) `
        'LongGridRoot must keep a semantic accessibility name.'
    $themeStatusNode = Get-XamlNodeByAutomationId $document 'ThemeStatusText'
    Assert-Condition (
        $themeStatusNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite'
    ) 'ThemeStatusText must politely announce in-process theme changes.'
    $previewStatusNode = Get-XamlNodeByAutomationId $document 'OrganizationPreviewStatus'
    Assert-Condition (
        $previewStatusNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite'
    ) 'OrganizationPreviewStatus must politely announce preview changes.'
    $startChoiceStatusNode = Get-XamlNodeByAutomationId $document 'StartChoiceStatus'
    Assert-Condition (
        $startChoiceStatusNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite'
    ) 'StartChoiceStatus must politely announce first-run path changes.'
    $practiceActivityNode = Get-XamlNodeByAutomationId $document 'PracticeActivityStatus'
    Assert-Condition (
        $practiceActivityNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite'
    ) 'PracticeActivityStatus must politely announce create and undo changes.'
    $dropActionNode = Get-XamlNodeByAutomationId $document 'DropActionStatus'
    Assert-Condition (
        $dropActionNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite'
    ) 'DropActionStatus must politely announce the audited drop semantics.'
    $recoveryStatusNode = Get-XamlNodeByAutomationId $document 'RecoveryPlanStatus'
    Assert-Condition (
        $recoveryStatusNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite'
    ) 'RecoveryPlanStatus must politely announce preview and expiry changes.'
    $configurationRecoveryNode = $document.SelectSingleNode(
        "//*[@*[local-name()='Name' and .='ConfigurationRecoveryBanner']]"
    )
    Assert-Condition ($null -ne $configurationRecoveryNode) `
        'The startup configuration state must reuse the overview InfoBar.'
    Assert-Condition (
        $configurationRecoveryNode.GetAttribute('IsClosable') -eq 'False'
    ) 'Configuration recovery warnings must not be dismissible without resolution.'
    $runtimeScopeNode = $document.SelectSingleNode(
        "//*[@*[local-name()='Name' and .='RuntimeScopeDisclosureText']]"
    )
    Assert-Condition ($null -ne $runtimeScopeNode) `
        'The overview must keep a persistent runtime data-scope disclosure.'
    $runtimeScopeStatus = $runtimeScopeNode.GetAttribute(
        'AutomationProperties.ItemStatus'
    )
    Assert-Condition (
        $runtimeScopeStatus -eq `
            'Catalog=RealDesktopFirstLevelMetadata;Practice=AnonymousMemory;' +
            'FileContent=NotRead;DesktopFileWrites=Disabled;DesktopHost=Disconnected'
    ) 'The runtime disclosure must expose the complete audited data and execution boundary.'
    Assert-Condition (
        $configurationRecoveryNode.GetAttribute('AutomationProperties.ItemStatus') -eq `
            'StartupReadOnly:Catalog=FirstLevelMetadata;' +
            'DesktopFileWrites=Disabled;DesktopHost=Disconnected'
    ) 'The startup banner must expose the audited real read-only catalog boundary.'
    $configurationActionNode = Get-XamlNodeByAutomationId `
        $document `
        'ConfigurationRecoveryActionButton'
    Assert-Condition ($configurationActionNode.GetAttribute('Visibility') -eq 'Collapsed') `
        'Configuration repair must be unavailable until a finite recovery state is loaded.'
    Assert-Condition (
        $configurationActionNode.GetAttribute('Click') -eq `
            'ConfigurationRecoveryActionButton_Click'
    ) 'Configuration repair must use the audited confirmation handler.'
    $importButtonNode = Get-XamlNodeByAutomationId $document 'ImportConfigurationButton'
    Assert-Condition (
        $importButtonNode.GetAttribute('Click') -eq 'ImportConfigurationButton_Click'
    ) 'Configuration import must use the audited preview-and-confirm handler.'
    $importStatusNode = Get-XamlNodeByAutomationId $document 'ConfigurationImportStatus'
    Assert-Condition (
        $importStatusNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite'
    ) 'Configuration import status must politely announce finite state changes.'
    $exportButtonNode = Get-XamlNodeByAutomationId $document 'ExportConfigurationButton'
    Assert-Condition (
        $exportButtonNode.GetAttribute('Click') -eq 'ExportConfigurationButton_Click'
    ) 'Configuration export must use the audited preview-and-confirm handler.'
    $exportStatusNode = Get-XamlNodeByAutomationId $document 'ConfigurationExportStatus'
    Assert-Condition (
        $exportStatusNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite'
    ) 'Configuration export status must politely announce finite state changes.'
    $evidenceButtonNode = Get-XamlNodeByAutomationId `
        $document `
        'RefreshConfigurationEvidenceButton'
    Assert-Condition (
        $evidenceButtonNode.GetAttribute('Click') -eq `
            'RefreshConfigurationEvidenceButton_Click'
    ) 'Configuration evidence must use the audited read-only refresh handler.'
    $evidenceStatusNode = Get-XamlNodeByAutomationId $document 'ConfigurationEvidenceStatus'
    Assert-Condition (
        $evidenceStatusNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite'
    ) 'Configuration evidence status must politely announce finite state changes.'
    $evidenceListNode = Get-XamlNodeByAutomationId $document 'ConfigurationEvidenceList'
    Assert-Condition (
        $evidenceListNode.GetAttribute('SelectionMode') -eq 'Single' -and
        $evidenceListNode.GetAttribute('SelectionChanged') -eq `
            'ConfigurationEvidenceList_SelectionChanged'
    ) 'Configuration evidence must require one explicit anonymous selection.'
    $evidenceExportNode = Get-XamlNodeByAutomationId `
        $document `
        'ExportConfigurationEvidenceButton'
    Assert-Condition (
        $evidenceExportNode.GetAttribute('IsEnabled') -eq 'False' -and
        $evidenceExportNode.GetAttribute('Click') -eq `
            'ExportConfigurationEvidenceButton_Click'
    ) 'Raw evidence export must remain disabled until an explicit selection.'
    $evidenceRemovalNode = Get-XamlNodeByAutomationId `
        $document `
        'RemoveConfigurationEvidenceButton'
    Assert-Condition (
        $evidenceRemovalNode.GetAttribute('IsEnabled') -eq 'False' -and
        $evidenceRemovalNode.GetAttribute('Click') -eq `
            'RemoveConfigurationEvidenceButton_Click'
    ) 'Evidence removal must remain disabled until one explicit anonymous selection.'
    $practiceNameNode = Get-XamlNodeByAutomationId $document 'PracticeContainerName'
    Assert-Condition ($practiceNameNode.GetAttribute('MaxLength') -eq '40') `
        'The anonymous practice-container name must remain bounded to 40 characters.'
    $undoNode = Get-XamlNodeByAutomationId $document 'UndoPracticeContainerButton'
    $undoAccelerator = $undoNode.SelectSingleNode(".//*[local-name()='KeyboardAccelerator']")
    Assert-Condition (
        $null -ne $undoAccelerator -and
        $undoAccelerator.GetAttribute('Key') -eq 'Z' -and
        $undoAccelerator.GetAttribute('Modifiers') -eq 'Control'
    ) 'The anonymous container undo action must keep its Ctrl+Z accelerator.'
    Assert-Condition (-not ($document.OuterXml -match 'AllowDrop')) `
        'The semantic practice must not masquerade as an Explorer drop target.'

    $productCatalogDetailNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductDesktopCatalogDetail'
    Assert-Condition (
        $productCatalogDetailNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite' -and
        $productCatalogDetailNode.GetAttribute('AutomationProperties.ItemStatus') -eq `
            'DesktopCatalogUnavailable:Generation=0:Items=0:Authoritative=False'
    ) 'Desktop catalog must start finite, unavailable, and non-authoritative.'
    $productCatalogRefreshNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductDesktopCatalogRefreshButton'
    Assert-Condition (
        $productCatalogRefreshNode.GetAttribute('Click') -eq `
            'ProductDesktopCatalogRefreshButton_Click'
    ) 'Desktop catalog refresh must remain an explicit read-only action.'
    $productCatalogCardNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductDesktopCatalogCard'
    Assert-Condition (-not ($productCatalogCardNode.OuterXml -match 'Storyboard|Transition')) `
        'Desktop catalog status must keep a Reduced Motion-safe static baseline.'

    $productSessionDetailNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceSessionDetail'
    Assert-Condition (
        $productSessionDetailNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite' -and
        $productSessionDetailNode.GetAttribute('AutomationProperties.ItemStatus') -eq `
            'WorkspaceSessionLoading:Source=None:Catalog=Unavailable:ReadOnly=True'
    ) 'Product session loading must start finite, read-only, and catalog-unavailable.'
    $productSessionCardNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceSessionCard'
    Assert-Condition (-not ($productSessionCardNode.OuterXml -match 'Storyboard|Transition')) `
        'Product session status must keep a Reduced Motion-safe static baseline.'

    $layoutRecoveryDetailNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceLayoutRecoveryDetail'
    Assert-Condition (
        $layoutRecoveryDetailNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite' -and
        $layoutRecoveryDetailNode.GetAttribute('AutomationProperties.ItemStatus') -eq `
            'LayoutRecoveryPreviewUnavailableSession:Containers=0:Mappings=0:Unresolved=0:Corrected=0:DesktopWindowsChanged=False'
    ) 'Layout recovery preview must start finite, unavailable, and non-mutating.'
    $layoutRecoveryCardNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceLayoutRecoveryCard'
    Assert-Condition (-not ($layoutRecoveryCardNode.OuterXml -match 'Storyboard|Transition')) `
        'Layout recovery preview must keep a Reduced Motion-safe static baseline.'

    $workspaceViewStatusNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceViewStatus'
    Assert-Condition (
        $workspaceViewStatusNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite' -and
        $workspaceViewStatusNode.GetAttribute('AutomationProperties.ItemStatus') -eq `
            'WorkspaceViewUnavailable:Containers=0:Items=0'
    ) 'Formal workspace view must start finite, unavailable, and empty.'
    $workspaceViewListNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceContainerList'
    Assert-Condition (
        $workspaceViewListNode.GetAttribute('SelectionMode') -eq 'None' -and
        $workspaceViewListNode.GetAttribute('IsItemClickEnabled') -eq 'False'
    ) 'Formal workspace view must remain non-interactive in this slice.'
    $workspaceViewCardNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceViewCard'
    Assert-Condition (-not ($workspaceViewCardNode.OuterXml -match 'Storyboard|Transition')) `
        'Formal workspace view must keep a Reduced Motion-safe static baseline.'
    $containerNameNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceContainerNameEditor'
    Assert-Condition (
        $containerNameNode.GetAttribute('MaxLength') -eq '256' -and
        $containerNameNode.GetAttribute('TextChanged') -eq `
            'ProductWorkspaceContainerNameEditor_TextChanged'
    ) 'Formal container names must use the v1 bound and update explicit actions.'
    foreach ($buttonId in @(
            'ProductWorkspaceContainerCreateButton',
            'ProductWorkspaceContainerRenameButton',
            'ProductWorkspaceContainerLockButton',
            'ProductWorkspaceContainerCollapseButton',
            'ProductWorkspaceContainerAppearanceButton',
            'ProductWorkspaceContainerPlacementButton'
        )) {
        $buttonNode = Get-XamlNodeByAutomationId $document $buttonId
        Assert-Condition ($buttonNode.GetAttribute('IsEnabled') -eq 'False') `
            "Container edit action '$buttonId' must start disabled."
    }
    $containerStateHandlers = @{
        ProductWorkspaceContainerLockButton = `
            'ProductWorkspaceContainerLockButton_Click'
        ProductWorkspaceContainerCollapseButton = `
            'ProductWorkspaceContainerCollapseButton_Click'
        ProductWorkspaceContainerAppearanceButton = `
            'ProductWorkspaceContainerAppearanceButton_Click'
        ProductWorkspaceContainerPlacementButton = `
            'ProductWorkspaceContainerPlacementButton_Click'
    }
    foreach ($selectorId in @(
            'ProductWorkspaceContainerColorSelector',
            'ProductWorkspaceContainerOpacitySelector'
        )) {
        $selectorNode = Get-XamlNodeByAutomationId $document $selectorId
        Assert-Condition (
            $selectorNode.GetAttribute('IsEnabled') -eq 'False' -and
            $selectorNode.GetAttribute('SelectionChanged') -eq `
                'ProductWorkspaceContainerAppearanceSelector_SelectionChanged'
        ) "Container appearance selector '$selectorId' must start disabled and use the audited handler."
    }
    foreach ($selectorId in @(
            'ProductWorkspaceContainerPositionSelector',
            'ProductWorkspaceContainerSizeSelector'
        )) {
        $selectorNode = Get-XamlNodeByAutomationId $document $selectorId
        Assert-Condition (
            $selectorNode.GetAttribute('IsEnabled') -eq 'False' -and
            $selectorNode.GetAttribute('SelectionChanged') -eq `
                'ProductWorkspaceContainerPlacementSelector_SelectionChanged'
        ) "Container placement selector '$selectorId' must start disabled and use the audited handler."
    }
    foreach ($entry in $containerStateHandlers.GetEnumerator()) {
        $buttonNode = Get-XamlNodeByAutomationId $document $entry.Key
        Assert-Condition ($buttonNode.GetAttribute('Click') -eq $entry.Value) `
            "Container state action '$($entry.Key)' must use its audited handler."
    }
    $containerEditStatusNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceContainerEditStatus'
    Assert-Condition (
        $containerEditStatusNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite' -and
        $containerEditStatusNode.GetAttribute('AutomationProperties.ItemStatus') -eq `
            'WorkspaceContainerEditUnavailable:Changed=False:DesktopFilesChanged=False'
    ) 'Container editing must start finite, unchanged, and explicit about desktop files.'
    $resolvedReferenceSelectorNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceResolvedReferenceSelector'
    Assert-Condition (
        $resolvedReferenceSelectorNode.GetAttribute('IsEnabled') -eq 'False' -and
        $resolvedReferenceSelectorNode.GetAttribute('SelectionChanged') -eq `
            'ProductWorkspaceResolvedReferenceSelector_SelectionChanged'
    ) 'Resolved-reference selection must start disabled and use the audited handler.'
    $resolvedReferenceAddButtonNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceResolvedReferenceAddButton'
    Assert-Condition (
        $resolvedReferenceAddButtonNode.GetAttribute('IsEnabled') -eq 'False' -and
        $resolvedReferenceAddButtonNode.GetAttribute('Click') -eq `
            'ProductWorkspaceResolvedReferenceAddButton_Click'
    ) 'Resolved-reference addition must require an explicit valid selection.'
    $resolvedReferenceAddStatusNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceResolvedReferenceAddStatus'
    Assert-Condition (
        $resolvedReferenceAddStatusNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite' -and
        $resolvedReferenceAddStatusNode.GetAttribute('AutomationProperties.ItemStatus') -eq `
            'ResolvedReferenceAddUnavailable:Changed=False:DesktopFilesChanged=False'
    ) 'Resolved-reference addition must start finite and non-mutating.'
    $resolvedReferenceRemovalSelectorNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceResolvedReferenceRemovalSelector'
    Assert-Condition (
        $resolvedReferenceRemovalSelectorNode.GetAttribute('IsEnabled') -eq 'False' -and
        $resolvedReferenceRemovalSelectorNode.GetAttribute('SelectionChanged') -eq `
            'ProductWorkspaceResolvedReferenceRemovalSelector_SelectionChanged'
    ) 'Resolved-reference removal selection must start disabled and use the audited handler.'
    foreach ($entry in @{
            ProductWorkspaceResolvedReferenceRemovalButton =
                'ProductWorkspaceResolvedReferenceRemovalButton_Click'
            ProductWorkspaceResolvedReferenceRemovalUndoButton =
                'ProductWorkspaceResolvedReferenceRemovalUndoButton_Click'
        }.GetEnumerator()) {
        $node = Get-XamlNodeByAutomationId $document $entry.Key
        Assert-Condition (
            $node.GetAttribute('IsEnabled') -eq 'False' -and
            $node.GetAttribute('Click') -eq $entry.Value
        ) "Resolved-reference removal action '$($entry.Key)' must start disabled and use its audited handler."
    }
    $resolvedReferenceRemovalStatusNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceResolvedReferenceRemovalStatus'
    Assert-Condition (
        $resolvedReferenceRemovalStatusNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite' -and
        $resolvedReferenceRemovalStatusNode.GetAttribute('AutomationProperties.ItemStatus') -eq `
            'ResolvedReferenceRemovalUnavailable:Changed=False:DesktopFilesChanged=False'
    ) 'Resolved-reference removal must start finite and non-mutating.'
    $reassignmentTargetNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceResolvedReferenceReassignmentTargetSelector'
    Assert-Condition (
        $reassignmentTargetNode.GetAttribute('IsEnabled') -eq 'False' -and
        $reassignmentTargetNode.GetAttribute('SelectionChanged') -eq `
            'ProductWorkspaceResolvedReferenceReassignmentTargetSelector_SelectionChanged'
    ) 'Resolved-reference reassignment target must start disabled and use the audited handler.'
    $reassignmentButtonNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceResolvedReferenceReassignmentButton'
    Assert-Condition (
        $reassignmentButtonNode.GetAttribute('IsEnabled') -eq 'False' -and
        $reassignmentButtonNode.GetAttribute('Click') -eq `
            'ProductWorkspaceResolvedReferenceReassignmentButton_Click'
    ) 'Resolved-reference reassignment must require explicit source and target selections.'

    $referenceReviewStatusNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceReferenceReviewStatus'
    Assert-Condition (
        $referenceReviewStatusNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite' -and
        $referenceReviewStatusNode.GetAttribute('AutomationProperties.ItemStatus') -eq `
            'ReferenceReviewUnavailable:Changed=False'
    ) 'Reference review must start finite, unavailable, and unchanged.'
    foreach ($buttonId in @(
            'ProductWorkspaceReferenceKeepButton',
            'ProductWorkspaceReferenceReselectButton',
            'ProductWorkspaceReferenceRemoveButton'
        )) {
        $buttonNode = Get-XamlNodeByAutomationId $document $buttonId
        Assert-Condition ($buttonNode.GetAttribute('IsEnabled') -eq 'False') `
            "Reference review action '$buttonId' must start disabled."
    }
    $referenceReviewCardNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceReferenceReviewCard'
    Assert-Condition (-not ($referenceReviewCardNode.OuterXml -match 'Storyboard|Transition')) `
        'Reference review must keep a Reduced Motion-safe static baseline.'

    $productSaveDetailNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductSaveStatusDetail'
    Assert-Condition (
        $productSaveDetailNode.GetAttribute('AutomationProperties.LiveSetting') -eq 'Polite' -and
        $productSaveDetailNode.GetAttribute('AutomationProperties.ItemStatus') -eq `
            'WorkspaceSaveClean:Revision=0:Motion=Static'
    ) 'Product save state must start honest and use polite finite announcements.'
    $productSaveRetryNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductSaveRetryButton'
    Assert-Condition (
        $productSaveRetryNode.GetAttribute('Visibility') -eq 'Collapsed' -and
        $productSaveRetryNode.GetAttribute('IsEnabled') -eq 'False' -and
        $productSaveRetryNode.GetAttribute('Click') -eq 'ProductSaveRetryButton_Click'
    ) 'Product save retry must remain unavailable until a retryable finite failure.'
    $productSaveCardNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductSaveStatusCard'
    Assert-Condition (-not ($productSaveCardNode.OuterXml -match 'Storyboard|Transition')) `
        'Product save status must keep a Reduced Motion-safe static transition baseline.'

    $scrollViewer = $document.SelectSingleNode("//*[local-name()='ScrollViewer']")
    Assert-Condition ($null -ne $scrollViewer) 'The content ScrollViewer is missing.'
    Assert-Condition (
        $scrollViewer.GetAttribute('HorizontalScrollMode') -eq 'Disabled'
    ) 'Horizontal scrolling must stay disabled; compact content must reflow.'

    $expectedAccessKeys = @{
        NavOverview = '1'
        NavFirstRun = '2'
        NavAppearance = '3'
        NavSafety = '4'
        NavRecovery = '5'
    }
    foreach ($entry in $expectedAccessKeys.GetEnumerator()) {
        $node = Get-XamlNodeByAutomationId $document $entry.Key
        Assert-Condition ($node.GetAttribute('AccessKey') -eq $entry.Value) `
            "Navigation item '$($entry.Key)' must keep AccessKey '$($entry.Value)'."
    }

    foreach ($themeId in @('ThemeSystem', 'ThemeLight', 'ThemeDark')) {
        $node = Get-XamlNodeByAutomationId $document $themeId
        Assert-Condition ($node.GetAttribute('Checked') -eq 'ThemeOption_Checked') `
            "Theme option '$themeId' must use the audited memory-only handler."
    }

    Assert-Condition ($codeBehind -match 'RootLayout\.RequestedTheme') `
        'The theme handler must apply RequestedTheme to the app root.'
    Assert-Condition ($codeBehind -match 'ElementTheme\.Default') `
        'The theme handler must preserve a follow-system mode.'
    Assert-Condition ($codeBehind -match 'ElementTheme\.Light') `
        'The theme handler must expose a light mode.'
    Assert-Condition ($codeBehind -match 'ElementTheme\.Dark') `
        'The theme handler must expose a dark mode.'
    Assert-Condition ($codeBehind -match 'CompactBreakpoint\s*=\s*760') `
        'The audited compact/wide breakpoint must remain 760 effective pixels.'
    Assert-Condition ($codeBehind -match 'RootLayout\.SizeChanged') `
        'Responsive layout must follow the effective root size.'
    Assert-Condition ($codeBehind -match 'NavigationViewPaneDisplayMode\.LeftMinimal') `
        'Compact layout must use the minimal navigation pane.'
    Assert-Condition ($codeBehind -match 'AutomationProperties\.SetItemStatus') `
        'Responsive layout must expose its actual state to UI Automation.'
    Assert-Condition ($codeBehind -match 'XamlRoot\?\.RasterizationScale') `
        'Initial window sizing must convert effective pixels using the XAML scale.'
    Assert-Condition ($codeBehind -match 'DisplayArea\.GetFromWindowId') `
        'Initial window sizing must use the active display work area.'
    Assert-Condition ($codeBehind -match 'MaximumWorkAreaFraction\s*=\s*0\.9') `
        'The initial window must remain bounded to 90 percent of the work area.'
    Assert-Condition ($codeBehind -match 'RuntimeStatusSnapshot\.CreateDevelopmentReadOnly') `
        'The UI must obtain its capability state from the audited Core snapshot.'
    Assert-Condition ($codeBehind -match 'FileOrganizationMode\.SafeReference') `
        'The onboarding prototype must default to the Core safe-reference semantic.'
    Assert-Condition ($codeBehind -match 'FileOrganizationMode\.ManagedMove') `
        'The onboarding prototype must explicitly distinguish managed move.'
    Assert-Condition ($codeBehind -match 'ManagedMovePreviewBlocked') `
        'The development prototype must expose managed move as blocked.'
    Assert-Condition ($codeBehind -match 'SafeReferencePreview') `
        'The development prototype must expose a safe-reference preview state.'
    Assert-Condition ($codeBehind -match 'SuggestedStartSelected') `
        'The first-run prototype must expose the suggested-preview start path.'
    Assert-Condition ($codeBehind -match 'BlankStartSelected') `
        'The first-run prototype must expose the blank-layout start path.'
    Assert-Condition ($codeBehind -match 'PracticeContainerCreated') `
        'The practice container must expose its created state.'
    Assert-Condition ($codeBehind -match 'PracticeContainerUndone') `
        'The practice container must expose its undone state.'
    Assert-Condition ($codeBehind -match 'PracticeContainerNameRequired') `
        'The practice container must expose an empty-name validation state.'
    Assert-Condition ($codeBehind -match 'PracticeItemsAdded') `
        'The practice container must expose its three-reference state.'
    Assert-Condition ($codeBehind -match 'PracticeItemsUndone') `
        'The practice container must expose most-recent-action undo.'
    Assert-Condition ($codeBehind -match 'AddReferenceDropPreview') `
        'The drop practice must expose the safe-reference action badge.'
    Assert-Condition ($codeBehind -match 'ReassignDropPreview') `
        'The drop practice must expose relationship-only reassignment.'
    Assert-Condition ($codeBehind -match 'ManagedMoveDropBlocked') `
        'The drop practice must block unapproved managed moves.'
    Assert-Condition ($codeBehind -match 'LayoutRecoveryStatus\.Automatic') `
        'The recovery prototype must expose the Core automatic status.'
    Assert-Condition ($codeBehind -match 'LayoutRecoveryStatus\.ReviewRequired') `
        'The recovery prototype must expose the Core review-required status.'
    Assert-Condition ($codeBehind -match 'LayoutRecoveryStatus\.Blocked') `
        'The recovery prototype must expose the Core blocked status.'
    Assert-Condition ($codeBehind -match 'RecoveryPreviewExpired') `
        'A newer display change must invalidate the anonymous recovery preview.'
    Assert-Condition ($codeBehind -match 'RecoveryPreviewCancelled') `
        'The anonymous recovery preview must expose a no-change cancellation state.'
    Assert-Condition ($codeBehind -match 'ProductConfigurationStartupMode\.RecoveredBackupReadOnly') `
        'The UI must distinguish read-only backup recovery from normal loading.'
    Assert-Condition ($codeBehind -match 'ProductConfigurationStartupMode\.SafeMode') `
        'The UI must expose configuration safe mode as a separate state.'
    Assert-Condition (-not ($codeBehind -match 'PrimaryContractError|BackupContractError')) `
        'The UI must not expose raw configuration contract diagnostics.'
    Assert-Condition ($codeBehind -match 'ContentDialogResult\.Primary') `
        'Backup acceptance must require the destructive primary confirmation result.'
    Assert-Condition ($codeBehind -match 'DefaultButton\s*=\s*ContentDialogButton\.Close') `
        'The backup confirmation dialog must default keyboard focus to cancellation.'
    Assert-Condition ($codeBehind -match 'BackupAccepted:DamagedPrimaryArchived') `
        'Successful backup acceptance must expose evidence archival to UI Automation.'
    Assert-Condition ($codeBehind -match 'ProductConfigurationRecoveryAction\.ResetSafeMode') `
        'Safe mode must route to the finite reset action only after confirmation.'
    Assert-Condition ($codeBehind -match 'SafeModeReset:DamagedEvidenceArchived') `
        'Successful safe-mode reset must expose evidence archival to UI Automation.'
    Assert-Condition ($codeBehind -match 'ProductConfigurationImportPlan') `
        'Configuration import UI must receive only the opaque validated plan.'
    Assert-Condition ($codeBehind -match 'ImportPreviewValidated') `
        'Configuration import must expose a validated preview before confirmation.'
    Assert-Condition ($codeBehind -match 'ImportCommitted:EvidencePreserved') `
        'Successful configuration import must expose evidence preservation.'
    Assert-Condition ($codeBehind -match 'ProductConfigurationImportError\.StoreChanged') `
        'Configuration import must expose preview conflicts without overwriting state.'
    Assert-Condition ($codeBehind -match 'ProductConfigurationExportPlan') `
        'Configuration export UI must receive only the opaque validated plan.'
    Assert-Condition ($codeBehind -match 'ExportPreviewValidated') `
        'Configuration export must expose a validated preview before folder selection.'
    Assert-Condition ($codeBehind -match 'ExportFolderPickerOpen') `
        'Configuration export must request a destination only after confirmation.'
    Assert-Condition ($codeBehind -match 'ProductConfigurationExportError\.StoreChanged') `
        'Configuration export must expose preview conflicts without publishing stale state.'
    Assert-Condition ($codeBehind -match 'ConfigurationEvidenceList\.ItemsSource') `
        'Configuration evidence must expose only the finite inventory contract.'
    Assert-Condition ($codeBehind -match 'EvidenceExportFolderPickerOpen') `
        'Raw evidence export must request a destination only after confirmation.'
    Assert-Condition ($codeBehind -match 'EvidenceExportCommitted:SourcePreserved') `
        'Raw evidence export must disclose source preservation.'
    Assert-Condition ($codeBehind -match 'EvidenceRemovalCommitted:SingleItem') `
        'Evidence removal must disclose the single-item destructive boundary.'
    Assert-Condition (
        $codeBehind -match 'EvidenceRemovalCancelled' -and
        $codeBehind -match 'EvidenceRemovalInProgress'
    ) `
        'Evidence removal must expose cancel and in-progress states before deletion.'
    Assert-Condition ($codeBehind -match 'ObservedSizeBytes') `
        'Evidence inventory must expose bounded observed capacity metadata.'
    Assert-Condition ($codeBehind -match 'ProductConfigurationExportError\.EvidenceChanged') `
        'Raw evidence export must expose stale inventory without publishing it.'
    Assert-Condition (-not ($codeBehind -match 'Evidence.*(Path|FileName)')) `
        'Configuration evidence UI must not receive archive paths or file names.'
    Assert-Condition ($appCode -match 'FolderPicker') `
        'Configuration export folder authorization must remain in the app boundary.'
    Assert-Condition ($codeBehind -match 'ProductConfigurationRecoveryError\.WriteLeaseUnavailable') `
        'The UI must map finite recovery contention without exposing storage details.'
    Assert-Condition ($codeBehind -match 'AutomationProperties\.SetItemStatus\(\s*CurrentModeValue') `
        'The current runtime mode must expose a machine-readable UIA status.'
    Assert-Condition ($codeBehind -match 'AutomationProperties\.SetItemStatus\(\s*FileOperationValue') `
        'The file-operation boundary must expose a machine-readable UIA status.'
    Assert-Condition ($codeBehind -match 'AutomationProperties\.SetItemStatus\(\s*DesktopHostValue') `
        'The DesktopHost boundary must expose a machine-readable UIA status.'

    $forbiddenPatterns = @(
        'System\.IO\.',
        '\bFile\.',
        '\bDirectory\.',
        'Environment\.GetFolderPath',
        'FileOrganizationPlanner',
        'LongGrid\.Core\.DesktopItems',
        '\bDesktopCatalog\s*\.',
        'ShellChange',
        'DesktopHostCompositeTransactionCoordinator',
        'DesktopHostWindowPlanner',
        'LayoutRecoveryPlanner',
        'LayoutRecoveryTransactionCoordinator',
        'DisplayTopologyStabilizer',
        'DisplayTopologyFingerprint',
        'DragEventArgs',
        'DataPackage',
        'StorageItem'
    )
    foreach ($pattern in $forbiddenPatterns) {
        Assert-Condition (-not ($codeBehind -match $pattern)) `
            "UI code-behind crossed the read-only slice boundary: '$pattern'."
    }

    Assert-Condition ($appCode -match 'ProductConfigurationSaveCoordinator') `
        'The App must own the formal product configuration save coordinator.'
    Assert-Condition ($appCode -match 'configurationStore\.LoadAsync') `
        'The App must read the formal configuration result before presenting recovery state.'
    Assert-Condition ($appCode -match 'ProductConfigurationStartupState\.FromLoadResult') `
        'The App must reduce storage results to the finite recovery presentation contract.'
    Assert-Condition ($appCode -match 'configurationStore\.RecoverAsync') `
        'The App must route confirmed backup acceptance through the formal store.'
    Assert-Condition ($appCode -match 'FileOpenPicker') `
        'The App must obtain configuration import authorization from the system picker.'
    Assert-Condition ($appCode -match 'FileTypeFilter\.Add\("\.json"\)') `
        'The configuration picker must expose only the JSON import type.'
    Assert-Condition ($appCode -match 'FileAttributes\.ReparsePoint') `
        'The App must classify reparse-point sources before opening configuration content.'
    Assert-Condition ($appCode -match 'configurationStore\.PrepareImportAsync') `
        'The App must route selected sources through bounded validation and preview.'
    Assert-Condition ($appCode -match 'configurationStore\.ImportAsync') `
        'The App must route confirmed imports through the formal atomic store transaction.'
    Assert-Condition ($appCode -match 'UserConfirmed:\s*true') `
        'The App recovery request must carry the explicit confirmation contract.'
    Assert-Condition ($appCode -match 'AppWindow\.Closing\s*\+=') `
        'The App must intercept window closing before configuration drain.'
    Assert-Condition ($appCode -match 'ShutdownDrainTimeout\s*=\s*TimeSpan\.FromSeconds\(5\)') `
        'The configuration shutdown drain must keep its audited five-second bound.'
    Assert-Condition ($appCode -match 'args\.Cancel\s*=\s*true') `
        'Window closing must be cancelled until the accepted save queue drains.'
    Assert-Condition ($appCode -match 'ProductWorkspaceSaveController') `
        'The App must own the product workspace save controller.'
    Assert-Condition (
        $appCode -match 'ProductWorkspaceSessionSnapshot' -and
        $appCode -match 'ProductWorkspaceSessionLoader\.Load' -and
        $appCode -match 'ProductWorkspaceCatalogSnapshot\.Unavailable' -and
        $appCode -match 'snapshot\.IsAuthoritative'
    ) 'The App must resolve sessions only from an authoritative desktop catalog.'
    Assert-Condition (
        $appCode -match 'ProductDesktopCatalogController' -and
        $appCode -match 'ProductDesktopCatalogReader\.CreateForCurrentUser' -and
        $appCode -match 'RefreshProductDesktopCatalogAsync'
    ) 'The App must own the audited read-only physical desktop catalog controller.'
    Assert-Condition ($appCode -match 'productWorkspaceSaves\.CompleteAsync\(timeout\.Token\)') `
        'Window closing must complete through the product workspace controller.'
    Assert-Condition ($appCode -match 'BlockedByFailure') `
        'A latest save failure must keep the window open for correction or retry.'
    Assert-Condition (
        $appCode -match 'CA1001:Types that own disposable fields should be disposable' -and
        $appCode -match 'WinUI owns the Application lifetime' -and
        $appCode -match 'await\s+productDesktopCatalog\.DisposeAsync' -and
        $appCode -match 'await\s+productWorkspaceSaves\.DisposeAsync'
    ) 'The WinUI-owned App lifetime must document and await both controller disposals.'
    Assert-Condition ($appCode -match 'closingDrainInProgress') `
        'Concurrent close requests must share one configuration drain attempt.'
    Assert-Condition (-not ($appCode -match '\.(SaveAsync|EnqueueAsync)\(')) `
        'The development shell must not directly bypass the controller with ordinary writes.'
    Assert-Condition (-not ($codeBehind -match '\.(SaveAsync|EnqueueAsync)\(')) `
        'MainWindow must not directly call configuration save or queue APIs.'
    Assert-Condition (-not ($appCode -match 'productWorkspaceSaves\.Submit\(')) `
        'The App must route reference edits through the audited commit coordinator.'
    Assert-Condition (
        $appCode -match 'ProductWorkspaceReferenceReview\.Create' -and
        $appCode -match 'ProductWorkspaceCommitCoordinator' -and
        $appCode -match 'workspaceCommits\.Commit' -and
        $appCode -match 'workspaceCommits\.CurrentEditRevision' -and
        $appCode -match 'workspaceCommits\.AdvanceExternalRevision' -and
        $appCode -match 'catalog\.Generation'
    ) 'The App must own the catalog-generation and edit-revision review boundary.'
    Assert-Condition (
        $appCode -match 'currentConfigurationLoadResult\s*=\s*new\(' -and
        $appCode -match 'ProductConfigurationLoadStatus\.LoadedPrimary' -and
        $appCode -match 'result\.Document' -and
        $appCode -match 'ProductWorkspaceSessionLoader\.Load\(' -and
        $appCode -match 'ApplyProductWorkspaceReferenceReview\(\)'
    ) 'An accepted reference edit must replace the in-memory baseline and rebuild the session/review.'
    Assert-Condition (
        $appCode -match 'ProductWorkspaceReadModel\.Create' -and
        $appCode -match 'ProductWorkspaceReadPresentation\.Create' -and
        $appCode -match 'ApplyProductWorkspaceSessionViews\(\)' -and
        $appCode -match 'ApplyProductWorkspaceReadModel'
    ) 'Every rebuilt session must also refresh the validated formal read-only view.'
    Assert-Condition (
        $workspaceReadModelCode -match 'ProductWorkspaceConfigurationProjector\.Project' -and
        $workspaceReadModelCode -match 'CatalogEntry!\.DisplayName' -and
        $workspaceReadModelCode -match 'isResolved\s*\?[^\r\n]*CatalogEntry' -and
        -not ($workspaceReadModelCode -match 'PersistedTarget') -and
        -not ($workspaceReadModelCode -match '\.Id|ProfileId|CanonicalTarget|SourceId|ParsingName|VolumeId|FileId')
    ) 'Core read model must validate first, expose resolved visible names, and omit persistence identity.'
    Assert-Condition ($workspaceReadPresentationCode.Contains('WorkspaceViewReady:Containers=')) `
        'Workspace presentation must expose a finite ready status.'
    Assert-Condition (
        $workspaceReadPresentationCode -match `
            'string displayName\s*=\s*resolved\s*\?\s*item\.UserVisibleName!\s*:\s*\$"'
    ) 'Workspace presentation must use a generated ordinal label for unresolved items.'
    Assert-Condition ($workspaceReadPresentationCode.Contains('AccessibilityName')) `
        'Workspace presentation must carry an explicit accessibility name.'
    Assert-Condition (-not ($workspaceReadPresentationCode -match `
            'PersistedTarget|CanonicalTarget|ProfileId|SourceId')) `
        'Workspace presentation must omit persistence identity.'
    Assert-Condition (
        $codeBehind -match 'ProductWorkspaceContainerList\.ItemsSource\s*=\s*presentation\.Containers' -and
        -not ($codeBehind -match 'ProductWorkspaceRead.*(State|CatalogEntry|PersistedTarget|CanonicalTarget)')
    ) 'MainWindow must render only the presentation contract, never workspace identity state.'
    Assert-Condition (
        ([regex]::Matches($referenceCommitCode, 'saves\.Submit\(').Count -eq 9) -and
        $referenceCommitCode -match 'editRevision\s*=\s*checked\(editRevision\s*\+\s*1\)' -and
        $referenceCommitCode -match 'ProductWorkspaceReferenceGate\.Evaluate' -and
        $referenceCommitCode -match 'ProductWorkspaceConfigurationProjector\.Project' -and
        $referenceCommitCode -match 'CommitContainer' -and
        $referenceCommitCode -match 'CommitResolvedReference' -and
        $referenceCommitCode -match 'ExpectedCatalogGeneration' -and
        $referenceCommitCode -match 'AlreadyReferenced' -and
        $referenceCommitCode -match 'ProductWorkspaceReducer\.AddResolvedReference' -and
        $referenceCommitCode -match 'CommitResolvedReferenceRemoval' -and
        $referenceCommitCode -match 'CommitReferenceRemovalUndo' -and
        $referenceCommitCode -match 'ProductWorkspaceReducer\.RemoveReference' -and
        $referenceCommitCode -match 'CommitResolvedReferenceReassignment' -and
        $referenceCommitCode -match 'CommitReferenceReassignmentUndo' -and
        $referenceCommitCode -match 'ProductWorkspaceReducer\.ReassignResolvedReference' -and
        $referenceCommitCode -match 'ProductWorkspaceReducer\.(CreateContainer|RenameContainer)' -and
        $referenceCommitCode -match 'CommitLayoutRecovery' -and
        $referenceCommitCode -match 'CommitLayoutRecoveryUndo'
    ) 'Reference review, resolved-reference addition, container, layout recovery, and undo edits must share one coordinator with one submission per accepted path.'
    Assert-Condition (
        $resolvedReferenceAddPresentationCode -match 'DisplayName' -and
        $resolvedReferenceAddPresentationCode -match 'assignedTargets' -and
        $resolvedReferenceAddPresentationCode -match 'CatalogGeneration' -and
        -not ($resolvedReferenceAddPresentationCode -match 'PersistedTarget|ProfileId|SourceId|ParsingName|VolumeId|FileId')
    ) 'Resolved-reference presentation may expose the visible name but must omit persistence identity.'
    Assert-Condition (
        $resolvedReferenceRemovalPresentationCode -match 'UserVisibleName' -and
        $resolvedReferenceRemovalPresentationCode -match 'ContainerOrdinal' -and
        $resolvedReferenceRemovalPresentationCode -match 'ItemOrdinal' -and
        -not ($resolvedReferenceRemovalPresentationCode -match 'PersistedTarget|ProfileId|SourceId|ParsingName|VolumeId|FileId|ContainerId|ItemId')
    ) 'Resolved-reference removal presentation may expose visible names and ordinals but must omit persistence identity.'
    Assert-Condition (
        $resolvedReferenceReassignmentPresentationCode -match 'UserVisibleName' -and
        $resolvedReferenceReassignmentPresentationCode -match 'ContainerOrdinal' -and
        -not ($resolvedReferenceReassignmentPresentationCode -match 'PersistedTarget|ProfileId|SourceId|ParsingName|VolumeId|FileId|ContainerId|ItemId')
    ) 'Resolved-reference reassignment presentation may expose target names and ordinals but must omit persistence identity.'
    Assert-Condition (
        $appCode -match 'CommitProductWorkspaceResolvedReference' -and
        $appCode -match 'workspaceCommits\.CommitResolvedReference' -and
        $appCode -match 'candidate\.CatalogGeneration\s*!=\s*catalog\.Generation' -and
        $appCode -match 'ApplyProductWorkspaceResolvedReferenceAdd'
    ) 'The App must bind visible selection to the current catalog generation and shared coordinator.'
    Assert-Condition (
        $codeBehind -match 'configurationTransactionsEnabled' -and
        $codeBehind -match 'ImportConfigurationButton\.IsEnabled' -and
        $codeBehind -match 'ExportConfigurationButton\.IsEnabled' -and
        $codeBehind -match 'ProductWorkspaceSaveStatus\.Clean\s+or\s+ProductWorkspaceSaveStatus\.Saved'
    ) 'Import and export must be disabled while a product edit is pending or failed.'
    foreach ($gateState in @(
            'StaleCatalogGeneration',
            'StaleEditRevision',
            'ItemChanged',
            'ContainerLocked',
            'ConfirmationRequired',
            'ReplacementRequired',
            'ReplacementNotFound',
            'ReplacementAmbiguous'
        )) {
        Assert-Condition ($referenceReviewCode.Contains($gateState)) `
            "Reference review gate is missing finite state '$gateState'."
    }
    Assert-Condition (
        $codeBehind -match '\{item\.Ordinal\}' -and
        $codeBehind -match '\{candidate\.Ordinal\}' -and
        $codeBehind -match 'ReferenceCommit:' -and
        $codeBehind -match 'DesktopFilesChanged=False' -and
        $codeBehind -match 'ConfigurationChanged=' -and
        -not ($codeBehind -match 'PersistedTarget|CanonicalTarget')
    ) 'Reference commit presentation must remain anonymous and explicit about configuration/file effects.'
    Assert-Condition (
        $appCode -match 'CommitProductWorkspaceContainerAction' -and
        $appCode -match 'workspaceCommits\.CommitContainer' -and
        $appCode -match 'ProductConfigurationDefaults\.CreateEmpty' -and
        $appCode -match 'CreateDefaultContainer' -and
        $appCode -match 'DisplayKey\s*=\s*"display-unassigned"'
    ) 'App must support first-container creation through the shared audited coordinator.'
    foreach ($stateAction in @(
            'SetLocked',
            'SetCollapsed',
            'SetAppearancePreset',
            'SetPlacementPreset'
        )) {
        Assert-Condition ($referenceCommitCode.Contains($stateAction)) `
            "Shared coordinator must expose finite container action '$stateAction'."
    }
    Assert-Condition (
        $codeBehind -match 'WorkspaceContainerEdit:' -and
        $codeBehind -match 'DesktopFilesChanged=False' -and
        $codeBehind -match 'ProductWorkspaceContainerCommitStatus\.StaleEditRevision' -and
        $codeBehind -match 'ProductWorkspaceEditError\.ContainerLocked'
    ) 'Container edit UI must expose finite conflict, lock, and file-safety outcomes.'
    Assert-Condition (
        $containerEditPresentationCode -match 'EditRevision' -and
        $containerEditPresentationCode -match 'Ordinal' -and
        $containerEditPresentationCode -match 'IsLocked' -and
        $containerEditPresentationCode -match 'IsCollapsed' -and
        $containerEditPresentationCode -match 'ColorChoices' -and
        $containerEditPresentationCode -match 'OpacityChoices' -and
        $containerEditPresentationCode -match 'PositionChoices' -and
        $containerEditPresentationCode -match 'SizeChoices' -and
        $containerEditPresentationCode -match 'CanUpdateState' -and
        $containerEditPresentationCode -match 'CanUpdateAppearance' -and
        $containerEditPresentationCode -match 'CanUpdatePlacement' -and
        -not ($containerEditPresentationCode -match `
            'ContainerId|PersistedTarget|CanonicalTarget|DisplayKey|ProfileId')
    ) 'Container editor presentation must use revision plus ordinal without persistence identity.'
    foreach ($previewStatus in @(
            'UnavailableSession',
            'AwaitingAuthoritativeTopology',
            'SavedTopologyMissing',
            'Automatic',
            'ReviewRequired',
            'Blocked',
            'InvalidState'
        )) {
        Assert-Condition ($layoutRecoveryPreviewCode.Contains($previewStatus)) `
            "Layout recovery preview is missing finite status '$previewStatus'."
    }
    Assert-Condition (
        $layoutRecoveryPreviewCode -match 'LayoutRecoveryPlanner\.Create' -and
        $layoutRecoveryPreviewCode -match 'DesktopWindowsChanged:\s*false' -and
        $layoutRecoveryPresentationCode -match 'DesktopWindowsChanged=False' -and
        $appCode -match 'currentTopologyAuthoritative:\s*topology\.IsAuthoritative' -and
        $appCode -match 'currentTopology:\s*topology\.IsAuthoritative' -and
        -not ($layoutRecoveryPresentationCode -match `
            'ContainerId|DisplayKey|StableId|RequestedBounds|ProposedBounds')
    ) 'Product layout recovery preview must be finite, count-only, and non-mutating.'
    Assert-Condition (
        $layoutRecoveryReviewCode -match 'SavedTopologyFingerprint' -and
        $layoutRecoveryReviewCode -match 'CurrentTopologyFingerprint' -and
        $layoutRecoveryReviewCode -match 'ConfigurationFingerprint' -and
        $layoutRecoveryReviewCode -match 'TopologyGeneration' -and
        $layoutRecoveryReviewCode -match 'EditRevision' -and
        $layoutRecoveryReviewCode -match 'ConfirmationRequired' -and
        $layoutRecoveryReviewCode -match 'ContainerLocked' -and
        $layoutRecoveryReviewCode -match 'ProductWorkspaceConfigurationProjector\.Project'
    ) 'Product layout recovery confirmation must bind finite topology, configuration, and revision evidence.'
    Assert-Condition ($appCode -match 'CommitProductWorkspaceLayoutRecovery') `
        'App must connect the product layout recovery confirmation delegate.'
    Assert-Condition (
        $codeBehind -match 'ProductWorkspaceLayoutRecoveryConfirmButton_Click' -and
        $codeBehind -match 'ProductWorkspaceLayoutRecoveryCommitResult'
    ) 'Layout recovery confirmation must use the finite product commit result.'
    Assert-Condition (
        $layoutRecoveryUndoCode -match 'OperationId' -and
        $layoutRecoveryUndoCode -match 'RecoveryEditRevision' -and
        $layoutRecoveryUndoCode -match 'RecoveredConfigurationFingerprint' -and
        $layoutRecoveryUndoCode -match 'RestoreConfigurationFingerprint' -and
        $layoutRecoveryUndoCode -match 'CurrentConfigurationChanged' -and
        $referenceCommitCode -match 'CurrentLayoutRecoveryUndoToken' -and
        $referenceCommitCode -match 'pendingLayoutRecoveryUndo\s*=\s*null' -and
        $appCode -match 'CommitProductWorkspaceLayoutRecoveryUndo' -and
        $codeBehind -match 'ProductWorkspaceLayoutRecoveryUndoButton_Click' -and
        $codeBehind -match 'DefaultButton\s*=\s*ContentDialogButton\.Close'
    ) 'Layout recovery undo must be one-time, revision/fingerprint bound, explicitly confirmed, and shared-save coordinated.'
    Assert-Condition (-not ($appCode -match `
            'LayoutRecoveryTransactionCoordinator|ILayoutRecoveryWindowBatchAdapter')) `
        'Product App must not connect the real-window recovery transaction adapter.'
    Assert-Condition (
        $realWindowRecoveryAdmissionCode -match 'BoundPlanMissing' -and
        $realWindowRecoveryAdmissionCode -match 'ConfigurationUndoMismatch' -and
        $realWindowRecoveryAdmissionCode -match 'WindowOwnershipUnverified' -and
        $realWindowRecoveryAdmissionCode -match 'CompositeTransactionUnavailable' -and
        $realWindowRecoveryAdmissionCode -match 'RollbackFaultMatrixPending' -and
        $realWindowRecoveryAdmissionCode -match 'InputSurfaceMatrixPending' -and
        $realWindowRecoveryAdmissionCode -match 'DynamicDisplayMatrixPending' -and
        $realWindowRecoveryAdmissionCode -match 'CleanUiAutomationPending' -and
        -not ($appCode -match `
            'ProductWorkspaceRealWindowRecoveryAdmission|ProductWorkspaceRealWindowRecoveryPlanToken')
    ) 'Real-window recovery must remain blocked until bound transaction, ownership, rollback, and manual evidence all pass.'
    Assert-Condition (
        $windowCompositeTransactionCode -match 'TopologyGeneration' -and
        $windowCompositeTransactionCode -match 'EditRevision' -and
        $windowCompositeTransactionCode -match 'WindowRegistryGeneration' -and
        $windowCompositeTransactionCode -match 'DesktopHostInstanceId' -and
        $windowCompositeTransactionCode -match 'DesktopHostGeneration' -and
        $windowCompositeTransactionCode -match 'ConfigurationFingerprint' -and
        $windowCompositeTransactionCode -match 'PlanFingerprint' -and
        $windowCompositeTransactionCode -match 'WindowOwnershipAttested' -and
        $windowCompositeTransactionCode -match 'CurrentUndoToken' -and
        $windowCompositeTransactionCode -match 'RolledForward' -and
        $windowCompositeTransactionCode -match 'HideAffectedHosts' -and
        -not ($windowCompositeTransactionCode -match `
            '\bnint\b|HWND|SetWindowPos|DeferWindowPos|MoveWindow|SetForegroundWindow') -and
        -not ($appCode -match `
            'ProductWorkspaceWindowCompositeTransactionCoordinator|ProductWorkspaceWindowCompositeToken')
    ) 'Configuration and verified product windows must share a generation-bound, compensating, one-time-undo transaction without App or HWND exposure.'
    Assert-Condition (
        $desktopHostWindowBridgeCode -match 'ProductDesktopHostWindowStatus' -and
        $desktopHostWindowBridgeCode -match 'RegisteredWindowCount' -and
        $desktopHostWindowBridgeCode -match 'VerifiedWindowCount' -and
        $desktopHostWindowBridgeCode -match 'RejectedOperationCount' -and
        $desktopHostWindowBridgeCode -match 'InstanceMarker' -and
        $desktopHostWindowBridgeCode -match 'LastObservedBounds' -and
        $desktopHostWindowBridgeCode -match 'WindowGeneration' -and
        $desktopHostWindowBridgeCode -match 'HostGeneration' -and
        $desktopHostWindowBridgeCode -match 'DuplicateContainer' -and
        $desktopHostWindowBridgeCode -match 'DuplicateHandle' -and
        -not ($appCode -match 'ProductDesktopHostWindowBridge|ProductDesktopHostWindowClaim')
    ) 'The product-owned window registry must bind finite ownership and generation evidence without App wiring.'
    Assert-Condition (
        $windowsDesktopHostWindowInspectorCode -match 'IsWindow' -and
        $windowsDesktopHostWindowInspectorCode -match 'GetWindowThreadProcessId' -and
        $windowsDesktopHostWindowInspectorCode -match 'GetWindowRect' -and
        $windowsDesktopHostWindowInspectorCode -match 'GetPropW' -and
        -not ($windowsDesktopHostWindowInspectorCode -match `
            'SetWindowPos|DeferWindowPos|SetWindowRgn|SetPropW|MoveWindow|ShowWindow|SetForegroundWindow') -and
        -not ($desktopHostWindowBridgeCode -match `
            'SetWindowPos|DeferWindowPos|SetWindowRgn|SetPropW|MoveWindow|ShowWindow|SetForegroundWindow')
    ) 'The DesktopHost bridge may inspect owned windows but must not move, activate, reshape, or mark them.'
    Assert-Condition (
        $verifiedWindowBatchAdapterCode -match `
            'IProductWorkspaceCompositeWindowLayer' -and
        $verifiedWindowBatchAdapterCode -match 'TryPrepareExactVerifiedWindows' -and
        $verifiedWindowBatchAdapterCode -match 'TryUsePreparedVerifiedWindows' -and
        $verifiedWindowBatchAdapterCode -match 'TargetThreadId' -and
        $verifiedWindowBatchAdapterCode -match 'dispatcher\.Invoke' -and
        $verifiedWindowBatchAdapterCode -match 'WindowSnapshot' -and
        $verifiedWindowBatchAdapterCode -match 'RegistryGeneration' -and
        $verifiedWindowBatchAdapterCode -match 'BeginDeferWindowPos' -and
        $verifiedWindowBatchAdapterCode -match 'DeferWindowPos' -and
        $verifiedWindowBatchAdapterCode -match 'EndDeferWindowPos' -and
        $verifiedWindowBatchAdapterCode -match 'NoActivate' -and
        $verifiedWindowBatchAdapterCode -match 'NoZOrder' -and
        $verifiedWindowBatchAdapterCode -match 'NoOwnerZOrder' -and
        $verifiedWindowBatchAdapterCode -match 'NoSendChanging' -and
        -not ($verifiedWindowBatchAdapterCode -match `
            'SetWindowPos|MoveWindow|ShowWindow|SetForegroundWindow|SetWindowRgn') -and
        -not ($appCode -match `
            'ProductDesktopHostVerifiedWindowBatchAdapter|WindowsProductDesktopHostWindowBatchMutator')
    ) 'Verified product windows must prepare and reread one generation-bound native batch on the exact host thread without App wiring, activation, Z-order, or region changes.'
    Assert-Condition (
        $desktopHostThreadDispatcherCode -match 'SynchronizationContext' -and
        $desktopHostThreadDispatcherCode -match 'GetCurrentThreadId' -and
        $desktopHostThreadDispatcherCode -match 'QueueTimedOut' -and
        $desktopHostThreadDispatcherCode -match 'Pending' -and
        $desktopHostThreadDispatcherCode -match 'Cancelled' -and
        $desktopHostThreadDispatcherCode -match 'Running' -and
        $desktopHostThreadDispatcherCode -match 'CompareExchange' -and
        -not ($appCode -match `
            'SynchronizationContextProductDesktopHostThreadDispatcher|IProductDesktopHostThreadDispatcher')
    ) 'DesktopHost dispatch must cancel only work that has not started, await running native work, and remain App-blocked.'
    Assert-Condition (
        $desktopHostInputControllerCode -match 'EnableWindow' -and
        $desktopHostInputControllerCode -match 'IsWindowEnabled' -and
        $desktopHostInputControllerCode -match 'ShowWindow' -and
        $desktopHostInputControllerCode -match 'IsWindowVisible' -and
        $desktopHostInputControllerCode -match 'HideUnchecked' -and
        $compositeInputGateCode -match `
            'IProductWorkspaceCompositeInputGate' -and
        $compositeInputGateCode -match 'TryPrepareExactVerifiedWindows' -and
        $compositeInputGateCode -match 'TryUsePreparedVerifiedWindows' -and
        $compositeInputGateCode -match 'TargetThreadId' -and
        $compositeInputGateCode -match 'lifecycle\.BeginShutdown' -and
        $compositeInputGateCode -match 'DrainTimedOut' -and
        $compositeInputGateCode -match 'idle\.Wait' -and
        -not ($appCode -match `
            'ProductWorkspaceCompositeDesktopHostInputGate|WindowsProductDesktopHostInputController')
    ) 'Production DesktopHost input must revalidate the exact owned registry on its UI thread, fail safe by hiding hosts, and expose a retryable bounded shutdown drain without App wiring.'
    Assert-Condition (
        $configurationCompareExchangeCode -match `
            'CompareExchangePrimaryAsync' -and
        $configurationCompareExchangeCode -match 'AcquireWriteLeaseAsync' -and
        $configurationCompareExchangeCode -match 'LoadedPrimary' -and
        $configurationCompareExchangeCode -match `
            'ProductWorkspaceConfigurationFingerprint\.Compute' -and
        $configurationCompareExchangeCode -match 'File\.Replace' -and
        $compositeConfigurationAdapterCode -match `
            'IProductWorkspaceCompositeConfigurationLayer' -and
        $compositeConfigurationAdapterCode -match 'ConfigurationSnapshot' -and
        $compositeConfigurationAdapterCode -match 'lastPublishedFingerprint' -and
        $compositeConfigurationAdapterCode -match `
            'ProductWorkspaceCompositeBindingState' -and
        $compositeConfigurationAdapterCode -match 'lastPublishedBinding' -and
        $compositeConfigurationAdapterCode -match 'TryExchange' -and
        $compositeConfigurationAdapterCode -match 'CompareExchangePrimaryAsync' -and
        $compositeConfigurationAdapterCode -match 'VerifyRestored' -and
        -not ($appCode -match `
            'ProductWorkspaceCompositeConfigurationAdapter|CompareExchangePrimaryAsync')
    ) 'Composite configuration writes must use short-lease fingerprint compare-and-exchange, refuse damaged or foreign state, and remain App-blocked.'
    Assert-Condition (
        $compositeLifecycleGuardCode -match `
            'IProductWorkspaceCompositeBindingExchange' -and
        $compositeLifecycleGuardCode -match 'TopologyChanged' -and
        $compositeLifecycleGuardCode -match 'DesktopHostChanged' -and
        $compositeLifecycleGuardCode -match 'ShuttingDown' -and
        $compositeLifecycleGuardCode -match 'SnapshotChanged \+=' -and
        $compositeLifecycleGuardCode -match 'BeginShutdown' -and
        $compositeLifecycleGuardCode -match 'HasSameLifecycleIdentity' -and
        $windowCompositeTransactionCode -match `
            'FinishWithoutMutation[\s\S]*HideAffectedHosts' -and
        -not ($appCode -match 'ProductWorkspaceCompositeLifecycleGuard')
    ) 'Composite lifecycle evidence must invalidate stale topology, DesktopHost, shutdown, and undo bindings while hiding hosts when closed input cannot reopen; App wiring remains blocked.'
    Assert-Condition (
        $displayTopologyReaderCode -match 'HasStableTargetIdentity' -and
        $displayTopologyReaderCode -match 'MappedToActivePath' -and
        $displayTopologyReaderCode -match 'SourceBoundsMatch' -and
        $displayTopologyReaderCode -match 'TargetAvailable' -and
        $displayTopologyReaderCode -match 'WorkAreaIsInsideBounds' -and
        $windowsDisplayTopologySourceCode -match 'EnumDisplayMonitors' -and
        $windowsDisplayTopologySourceCode -match 'QueryDisplayConfig' -and
        $windowsDisplayTopologySourceCode -match 'MaxBufferAttempts\s*=\s*8' -and
        $displayTopologyControllerCode -match 'refreshGeneration\s*!=\s*generation' -and
        $displayTopologyControllerCode -match 'refreshesDrained' -and
        $appCode -match 'ProductDisplayTopologyReader\.CreateForCurrentSession' -and
        $appCode -match 'await productDisplayTopology\.DisposeAsync'
    ) 'Product display topology must require complete strong native evidence, latest-wins publication, and shutdown drain.'
    Assert-Condition (
        $referencePresentationCode -match 'CatalogGeneration' -and
        $referencePresentationCode -match 'CatalogIndex' -and
        -not ($referencePresentationCode -match `
            'DesktopCatalogEntry|DisplayName|CanonicalTarget|PersistedTarget')
    ) 'Reference candidate presentation must carry only an opaque generation/index handle.'
    foreach ($finiteStatus in @(
            'WorkspaceSaveClean',
            'WorkspaceSaveWaiting',
            'WorkspaceSaveSaving',
            'WorkspaceSaveRetrying',
            'WorkspaceSaveSaved',
            'WorkspaceSaveFailed'
        )) {
        Assert-Condition ($codeBehind.Contains($finiteStatus)) `
            "Product save presentation is missing finite status '$finiteStatus'."
    }
    foreach ($finiteFailure in @(
            'InvalidConfiguration',
            'DamagedEvidence',
            'WriteLeaseUnavailable',
            'IoFailure',
            'RetryUnavailable'
        )) {
        Assert-Condition ($codeBehind.Contains($finiteFailure)) `
            "Product save presentation is missing finite failure '$finiteFailure'."
    }
    foreach ($finiteSession in @(
            'WorkspaceSession',
            'NoSavedConfiguration',
            'AwaitingCatalog',
            'Ready',
            'RecoveredBackupReadOnly',
            'SafeMode',
            'InconsistentLoadResult',
            'InvalidConfiguration',
            'InvalidCatalog'
        )) {
        Assert-Condition ($codeBehind.Contains($finiteSession)) `
            "Product session presentation is missing finite state '$finiteSession'."
    }
    foreach ($finiteCatalog in @(
            'DesktopCatalog',
            'Refreshing',
            'Ready',
            'Partial',
            'Failed',
            'Cancelled',
            'Authoritative'
        )) {
        Assert-Condition ($codeBehind.Contains($finiteCatalog)) `
            "Desktop catalog presentation is missing finite state '$finiteCatalog'."
    }

    return [ordered]@{
        requiredAutomationIds = $requiredIds.Count
        accessKeys = $expectedAccessKeys.Count
        themeModes = 3
        responsiveBreakpoints = 1
        compactWidth = 720
        dpiAwareInitialSize = 'pass'
        coreRuntimeStatus = 'desktop-read-only-explicit-config-edit-enabled'
        firstOrganizationPrototype = 'safe-reference-items-drop-semantics-undo'
        layoutRecoveryPrototype = 'automatic-review-blocked-expire-cancel'
        configurationRecovery = 'loaded-missing-backup-read-only-safe-mode'
        configurationRepair = 'confirmed-recovery-bounded-import-export-evidence-inventory-export-and-single-removal'
        configurationShutdownDrain = 'controller-owned-bounded-explicit-edit-retry'
        productDesktopCatalog = 'physical-read-only-generation-latest-authoritative-only'
        productWorkspaceSession = 'formal-load-authoritative-catalog-revisioned-edit-baseline'
        productLayoutRecovery = 'verified-input-hide-bounded-shutdown-drain-app-blocked'
        productDisplayTopology = 'readonly-ccd-monitor-strong-identity-authoritative-adapter'
        productWorkspaceView = 'formal-session-readonly-visible-names-anonymous-unresolved'
        productResolvedReferenceAdd = 'visible-name-generation-revision-gated-config-only'
        productResolvedReferenceRemoval = 'visible-name-revision-gated-config-only-single-undo'
        productResolvedReferenceReassignment = 'atomic-source-target-revision-gated-config-only-single-undo'
        productContainerEdits = 'shared-revision-create-rename-lock-collapse-finite-appearance-placement-config-only'
        productReferenceReview = 'anonymous-generation-revision-gated-explicit-save-submission'
        productSavePresentation = 'privacy-safe-static-reduced-motion'
        readOnlyBoundary = 'explicit-reference-config-writes-no-desktop-file-mutations'
    }
}

function Find-UiaElement {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$AutomationId,
        [int]$TimeoutSeconds = 5
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId
    )
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $element = $Root.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition
        )
        if ($null -ne $element) {
            return $element
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "UI Automation element '$AutomationId' was not found within $TimeoutSeconds seconds."
}

function Select-UiaElement {
    param([System.Windows.Automation.AutomationElement]$Element)

    $pattern = $null
    if ($Element.TryGetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern,
            [ref]$pattern)) {
        ([System.Windows.Automation.SelectionItemPattern]$pattern).Select()
        return
    }

    if ($Element.TryGetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern,
            [ref]$pattern)) {
        ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
        return
    }

    throw "Element '$($Element.Current.AutomationId)' exposes neither SelectionItem nor Invoke."
}

function Wait-UiaName {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [string]$ExpectedText,
        [int]$TimeoutSeconds = 5
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if ($Element.Current.Name -like "*$ExpectedText*") {
            return
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Element '$($Element.Current.AutomationId)' did not expose expected text '$ExpectedText'."
}

function Assert-VerticallyStacked {
    param(
        [System.Windows.Automation.AutomationElement[]]$Elements,
        [System.Windows.Rect]$ContainerBounds
    )

    $previousBottom = [double]::NegativeInfinity
    foreach ($element in $Elements) {
        $bounds = $element.Current.BoundingRectangle
        Assert-Condition ($bounds.Width -gt 0 -and $bounds.Height -gt 0) `
            "Element '$($element.Current.AutomationId)' has no compact bounds; offscreen=$($element.Current.IsOffscreen), bounds=$bounds."
        Assert-Condition ($bounds.Left -ge $ContainerBounds.Left - 1) `
            "Element '$($element.Current.AutomationId)' overflows the compact left edge."
        Assert-Condition ($bounds.Right -le $ContainerBounds.Right + 1) `
            "Element '$($element.Current.AutomationId)' overflows the compact right edge."
        Assert-Condition ($bounds.Top -ge $previousBottom - 1) `
            "Element '$($element.Current.AutomationId)' overlaps the previous compact card."
        $previousBottom = $bounds.Bottom
    }
}

function Scroll-UiaToMetrics {
    param([System.Windows.Automation.AutomationElement]$ScrollViewer)

    $pattern = $null
    if (-not $ScrollViewer.TryGetCurrentPattern(
            [System.Windows.Automation.ScrollPattern]::Pattern,
            [ref]$pattern)) {
        throw 'ContentScrollViewer does not expose the Scroll pattern.'
    }

    $scrollPattern = [System.Windows.Automation.ScrollPattern]$pattern
    if ($scrollPattern.Current.VerticallyScrollable) {
        $scrollPattern.SetScrollPercent(
            [System.Windows.Automation.ScrollPattern]::NoScroll,
            25)
        Start-Sleep -Milliseconds 250
    }
}

function Scroll-UiaElementIntoView {
    param([System.Windows.Automation.AutomationElement]$Element)

    $pattern = $null
    if ($Element.TryGetCurrentPattern(
            [System.Windows.Automation.ScrollItemPattern]::Pattern,
            [ref]$pattern)) {
        ([System.Windows.Automation.ScrollItemPattern]$pattern).ScrollIntoView()
        Start-Sleep -Milliseconds 250
    }
}

function Wait-UiaElementOnscreen {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [string]$FailureMessage,
        [int]$TimeoutSeconds = 5
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        Scroll-UiaElementIntoView $Element
        if (-not $Element.Current.IsOffscreen) {
            return
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw $FailureMessage
}

function Test-LiveUi {
    if ($env:OS -ne 'Windows_NT') {
        throw 'The live Long Grid UI smoke requires Windows.'
    }

    if (-not $NoBuild) {
        & dotnet restore $projectPath --locked-mode --runtime $runtimeIdentifier
        if ($LASTEXITCODE -ne 0) {
            throw "LongGrid.App restore failed with exit code $LASTEXITCODE."
        }

        & dotnet build $projectPath `
            --configuration $Configuration `
            --runtime $runtimeIdentifier `
            --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw "LongGrid.App build failed with exit code $LASTEXITCODE."
        }
    }

    $targetFramework = 'net8.0-windows10.0.19041.0'
    $appPath = Join-Path $projectRoot `
        "src\LongGrid.App\bin\$Configuration\$targetFramework\$runtimeIdentifier\LongGrid.App.exe"
    Assert-Condition (Test-Path -LiteralPath $appPath) `
        "LongGrid.App executable was not found: $appPath"

    $existingProcesses = @(
        Get-Process -Name 'LongGrid.App' -ErrorAction SilentlyContinue
    )
    Assert-Condition ($existingProcesses.Count -eq 0) `
        "Clean-session UIA requires zero existing LongGrid.App processes; found PID(s): $($existingProcesses.Id -join ', '). The test will not terminate processes it did not start."

    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class LongGridWindowNative
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool MoveWindow(
        IntPtr window,
        int x,
        int y,
        int width,
        int height,
        bool repaint);
}
'@

    $process = Start-Process -FilePath $appPath -PassThru
    $liveResult = $null
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            Start-Sleep -Milliseconds 100
            $process.Refresh()
        } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and
            -not $process.HasExited -and
            [DateTime]::UtcNow -lt $deadline)

        Assert-Condition (-not $process.HasExited) `
            'LongGrid.App exited before the UI Automation smoke could attach.'
        Assert-Condition ($process.MainWindowHandle -ne [IntPtr]::Zero) `
            'LongGrid.App did not expose a main window within 15 seconds.'

        $root = [System.Windows.Automation.AutomationElement]::FromHandle(
            $process.MainWindowHandle
        )
        $expectedTitle = 'Long' + [char]0x65B9 + [char]0x683C
        Assert-Condition ($root.Current.Name -eq $expectedTitle) `
            "Unexpected window title '$($root.Current.Name)'."

        $responsiveStatus = Find-UiaElement $root 'ResponsiveStatusText'
        Wait-UiaName $responsiveStatus 'UI Shell'
        $root = [System.Windows.Automation.AutomationElement]::FromHandle(
            $process.MainWindowHandle
        )
        $navigation = Find-UiaElement $root 'ShellNavigation'
        $layoutRoot = Find-UiaElement $root 'LongGridRoot'
        $overview = Find-UiaElement $root 'NavOverview'
        $firstRun = Find-UiaElement $root 'NavFirstRun'
        $appearance = Find-UiaElement $root 'NavAppearance'
        $safety = Find-UiaElement $root 'NavSafety'
        $recovery = Find-UiaElement $root 'NavRecovery'
        $productSaveStatus = Find-UiaElement $root 'ProductSaveStatusDetail'
        Assert-Condition (
            $productSaveStatus.Current.ItemStatus -eq `
                'WorkspaceSaveClean:Revision=0:Motion=Static'
        ) 'The initial product save state did not remain honest and static.'
        $productSaveMotion = Find-UiaElement $root 'ProductSaveMotionPolicy'
        Assert-Condition ($productSaveMotion.Current.Name.Length -gt 0) `
            'The Reduced Motion-safe product save policy was not exposed.'
        $productSessionStatus = Find-UiaElement $root 'ProductWorkspaceSessionDetail'
        Assert-Condition (
            $productSessionStatus.Current.ItemStatus.StartsWith('WorkspaceSession')
        ) 'The product session did not expose a finite UIA state.'
        $productCatalogStatus = Find-UiaElement $root 'ProductDesktopCatalogDetail'
        Assert-Condition (
            $productCatalogStatus.Current.ItemStatus.StartsWith('DesktopCatalog')
        ) 'The read-only desktop catalog did not expose a finite UIA state.'
        $referenceReviewStatus = Find-UiaElement `
            $root `
            'ProductWorkspaceReferenceReviewStatus'
        Assert-Condition (
            $referenceReviewStatus.Current.ItemStatus.StartsWith('ReferenceReview')
        ) 'The reference review did not expose a finite UIA state.'
        foreach ($item in @($overview, $firstRun, $appearance, $safety, $recovery)) {
            Assert-Condition $item.Current.IsKeyboardFocusable `
                "Navigation item '$($item.Current.AutomationId)' is not keyboard focusable."
        }
        Select-UiaElement $overview

        $windowBounds = $root.Current.BoundingRectangle
        Assert-Condition (
            [LongGridWindowNative]::MoveWindow(
                $process.MainWindowHandle,
                [int]$windowBounds.Left,
                [int]$windowBounds.Top,
                720,
                [int]$windowBounds.Height,
                $true)
        ) 'LongGrid.App could not be resized for the compact layout smoke.'
        Start-Sleep -Milliseconds 500
        $root = [System.Windows.Automation.AutomationElement]::FromHandle(
            $process.MainWindowHandle
        )
        $layoutRoot = Find-UiaElement $root 'LongGridRoot'
        $responsiveStatus = Find-UiaElement $root 'ResponsiveStatusText'
        $compactText = [string]([char]0x7D27) + [char]0x51D1
        Wait-UiaName $responsiveStatus $compactText
        $contentScrollViewer = Find-UiaElement $root 'ContentScrollViewer'
        Scroll-UiaToMetrics $contentScrollViewer
        $currentModeCard = Find-UiaElement $root 'CurrentModeValue'
        $fileOperationCard = Find-UiaElement $root 'FileOperationValue'
        $desktopHostCard = Find-UiaElement $root 'DesktopHostValue'
        Assert-Condition ($currentModeCard.Current.ItemStatus -eq 'DevelopmentReadOnly') `
            'The UI did not expose the Core development read-only mode.'
        Assert-Condition ($fileOperationCard.Current.ItemStatus -eq 'DisabledBySafetyPolicy') `
            'The UI did not expose the file-operation safety policy.'
        Assert-Condition ($desktopHostCard.Current.ItemStatus -eq 'Disconnected') `
            'The UI did not expose the disconnected DesktopHost boundary.'
        Assert-VerticallyStacked `
            @($currentModeCard, $fileOperationCard, $desktopHostCard) `
            $layoutRoot.Current.BoundingRectangle

        Select-UiaElement $firstRun
        $safeReferenceCompact = Find-UiaElement $root 'SafeReferenceMode'
        $managedMoveCompact = Find-UiaElement $root 'ManagedMoveMode'
        Scroll-UiaElementIntoView $safeReferenceCompact
        $safeReferenceBounds = $safeReferenceCompact.Current.BoundingRectangle
        Scroll-UiaElementIntoView $managedMoveCompact
        $managedMoveBounds = $managedMoveCompact.Current.BoundingRectangle
        Assert-Condition (
            $safeReferenceBounds.Width -gt 0 -and
            $managedMoveBounds.Width -gt 0 -and
            [Math]::Abs($safeReferenceBounds.Left - $managedMoveBounds.Left) -le 2
        ) 'Compact organization modes did not reflow into one column.'
        $suggestedStartCompact = Find-UiaElement $root 'SuggestedStartChoice'
        $blankStartCompact = Find-UiaElement $root 'BlankStartChoice'
        Scroll-UiaElementIntoView $suggestedStartCompact
        $suggestedStartBounds = $suggestedStartCompact.Current.BoundingRectangle
        Scroll-UiaElementIntoView $blankStartCompact
        $blankStartBounds = $blankStartCompact.Current.BoundingRectangle
        Assert-Condition (
            $suggestedStartBounds.Width -gt 0 -and
            $blankStartBounds.Width -gt 0 -and
            [Math]::Abs($suggestedStartBounds.Left - $blankStartBounds.Left) -le 2 -and
            [Math]::Abs($suggestedStartBounds.Width - $blankStartBounds.Width) -le 2
        ) 'Compact start choices did not reflow into one equal-width column.'
        Select-UiaElement $overview

        Assert-Condition (
            [LongGridWindowNative]::MoveWindow(
                $process.MainWindowHandle,
                [int]$windowBounds.Left,
                [int]$windowBounds.Top,
                [int]$windowBounds.Width,
                [int]$windowBounds.Height,
                $true)
        ) 'LongGrid.App could not restore the wide layout.'
        Start-Sleep -Milliseconds 500
        $root = [System.Windows.Automation.AutomationElement]::FromHandle(
            $process.MainWindowHandle
        )
        $responsiveStatus = Find-UiaElement $root 'ResponsiveStatusText'
        Wait-UiaName $responsiveStatus 'UI Shell'
        $navigation = Find-UiaElement $root 'ShellNavigation'
        $firstRun = Find-UiaElement $root 'NavFirstRun'
        $appearance = Find-UiaElement $root 'NavAppearance'
        $safety = Find-UiaElement $root 'NavSafety'

        Select-UiaElement $firstRun
        $firstRunPanel = Find-UiaElement $root 'FirstRunPanel'
        Assert-Condition (-not $firstRunPanel.Current.IsOffscreen) `
            'FirstRunPanel stayed offscreen after selecting its navigation item.'
        $blankStart = Find-UiaElement $root 'BlankStartChoice'
        Scroll-UiaElementIntoView $blankStart
        Select-UiaElement $blankStart
        $startChoiceStatus = Find-UiaElement $root 'StartChoiceStatus'
        Assert-Condition ($startChoiceStatus.Current.ItemStatus -eq 'BlankStartSelected') `
            'Blank-layout start did not expose its audited UIA state.'
        $suggestedStart = Find-UiaElement $root 'SuggestedStartChoice'
        Scroll-UiaElementIntoView $suggestedStart
        Select-UiaElement $suggestedStart
        Assert-Condition ($startChoiceStatus.Current.ItemStatus -eq 'SuggestedStartSelected') `
            'Suggested-preview start did not expose its audited UIA state.'
        $managedMove = Find-UiaElement $root 'ManagedMoveMode'
        Scroll-UiaElementIntoView $managedMove
        Select-UiaElement $managedMove
        $previewStatus = Find-UiaElement $root 'OrganizationPreviewStatus'
        Assert-Condition ($previewStatus.Current.ItemStatus -eq 'ManagedMoveSelected') `
            'Managed move selection did not expose its audited UIA state.'
        $previewButton = Find-UiaElement $root 'OrganizationPreviewButton'
        Scroll-UiaElementIntoView $previewButton
        Select-UiaElement $previewButton
        Assert-Condition ($previewStatus.Current.ItemStatus -eq 'ManagedMovePreviewBlocked') `
            'Managed move preview was not blocked in the development shell.'

        $safeReference = Find-UiaElement $root 'SafeReferenceMode'
        Scroll-UiaElementIntoView $safeReference
        Select-UiaElement $safeReference
        Assert-Condition ($previewStatus.Current.ItemStatus -eq 'SafeReferenceSelected') `
            'Safe-reference selection did not expose its audited UIA state.'
        Scroll-UiaElementIntoView $previewButton
        Select-UiaElement $previewButton
        Assert-Condition ($previewStatus.Current.ItemStatus -eq 'SafeReferencePreview') `
            'Safe-reference preview did not expose its audited UIA state.'

        $createPracticeContainer = Find-UiaElement $root 'CreatePracticeContainerButton'
        Scroll-UiaElementIntoView $createPracticeContainer
        Select-UiaElement $createPracticeContainer
        $practiceActivity = Find-UiaElement $root 'PracticeActivityStatus'
        Assert-Condition ($practiceActivity.Current.ItemStatus -eq 'PracticeContainerCreated') `
            'Anonymous practice-container creation did not expose its audited UIA state.'
        $practicePreview = Find-UiaElement $root 'PracticeContainerPreview'
        Scroll-UiaElementIntoView $practicePreview
        Assert-Condition (-not $practicePreview.Current.IsOffscreen) `
            'The created anonymous practice container did not become visible.'
        $undoPracticeContainer = Find-UiaElement $root 'UndoPracticeContainerButton'
        Assert-Condition $undoPracticeContainer.Current.IsEnabled `
            'Undo did not become available after anonymous container creation.'
        $addPracticeItems = Find-UiaElement $root 'AddPracticeItemsButton'
        Assert-Condition $addPracticeItems.Current.IsEnabled `
            'Adding anonymous references did not become available after container creation.'
        Scroll-UiaElementIntoView $addPracticeItems
        Select-UiaElement $addPracticeItems
        Assert-Condition ($practiceActivity.Current.ItemStatus -eq 'PracticeItemsAdded') `
            'Adding three anonymous references did not expose its audited UIA state.'
        $practiceItems = Find-UiaElement $root 'PracticeItemsList'
        Scroll-UiaElementIntoView $practiceItems
        Assert-Condition (-not $practiceItems.Current.IsOffscreen) `
            'The three anonymous references did not become visible.'
        foreach ($itemId in @('PracticeItemOne', 'PracticeItemTwo', 'PracticeItemThree')) {
            $null = Find-UiaElement $root $itemId
        }

        $dropSafeReference = Find-UiaElement $root 'DropSafeReferenceButton'
        $dropActionStatus = Find-UiaElement $root 'DropActionStatus'
        Scroll-UiaElementIntoView $dropSafeReference
        Select-UiaElement $dropSafeReference
        Assert-Condition ($dropActionStatus.Current.ItemStatus -eq 'AddReferenceDropPreview') `
            'Explorer-to-safe-reference semantics were not exposed as add-reference.'
        $dropReassign = Find-UiaElement $root 'DropReassignButton'
        Scroll-UiaElementIntoView $dropReassign
        Select-UiaElement $dropReassign
        Assert-Condition ($dropActionStatus.Current.ItemStatus -eq 'ReassignDropPreview') `
            'Container-to-container semantics were not exposed as relationship reassignment.'
        $dropManagedMove = Find-UiaElement $root 'DropManagedMoveButton'
        Scroll-UiaElementIntoView $dropManagedMove
        Select-UiaElement $dropManagedMove
        Assert-Condition ($dropActionStatus.Current.ItemStatus -eq 'ManagedMoveDropBlocked') `
            'Unapproved managed-move drop semantics were not blocked.'

        Scroll-UiaElementIntoView $undoPracticeContainer
        Select-UiaElement $undoPracticeContainer
        Assert-Condition ($practiceActivity.Current.ItemStatus -eq 'PracticeItemsUndone') `
            'Undo did not remove the most recently added anonymous references first.'
        Assert-Condition $undoPracticeContainer.Current.IsEnabled `
            'Container undo disappeared after only the item-add action was undone.'
        Select-UiaElement $undoPracticeContainer
        Assert-Condition ($practiceActivity.Current.ItemStatus -eq 'PracticeContainerUndone') `
            'Anonymous practice-container undo did not expose its audited UIA state.'
        Assert-Condition (-not $undoPracticeContainer.Current.IsEnabled) `
            'Undo remained enabled after the anonymous container relationship was removed.'

        Select-UiaElement $recovery
        $recoveryPanel = Find-UiaElement $root 'RecoveryPanel'
        Scroll-UiaElementIntoView $recoveryPanel
        Assert-Condition (-not $recoveryPanel.Current.IsOffscreen) `
            'RecoveryPanel stayed offscreen after selecting its navigation item.'
        $recoveryStatus = Find-UiaElement $root 'RecoveryPlanStatus'
        $reviewScenario = Find-UiaElement $root 'RecoveryReviewScenarioButton'
        Scroll-UiaElementIntoView $reviewScenario
        Select-UiaElement $reviewScenario
        Assert-Condition ($recoveryStatus.Current.ItemStatus -eq 'ReviewRequiredRecoveryPreview') `
            'ReviewRequired recovery semantics were not exposed to UI Automation.'
        $recoveryDiff = Find-UiaElement $root 'RecoveryDiffPanel'
        Wait-UiaElementOnscreen `
            $recoveryDiff `
            'The anonymous recovery difference did not become visible.'
        $reviewRecovery = Find-UiaElement $root 'ReviewRecoveryButton'
        Assert-Condition $reviewRecovery.Current.IsEnabled `
            'ReviewRequired did not enable the audited acknowledgement action.'
        $expireRecovery = Find-UiaElement $root 'ExpireRecoveryPreviewButton'
        Scroll-UiaElementIntoView $expireRecovery
        Select-UiaElement $expireRecovery
        Assert-Condition ($recoveryStatus.Current.ItemStatus -eq 'RecoveryPreviewExpired') `
            'A newer anonymous display change did not expire the old preview.'
        Assert-Condition (-not $reviewRecovery.Current.IsEnabled) `
            'An expired recovery preview remained confirmable.'

        Scroll-UiaElementIntoView $reviewScenario
        Select-UiaElement $reviewScenario
        Scroll-UiaElementIntoView $reviewRecovery
        Select-UiaElement $reviewRecovery
        Assert-Condition ($recoveryStatus.Current.ItemStatus -eq 'RecoveryPreviewAcknowledged') `
            'Review acknowledgement did not preserve its no-execution status.'
        $blockedScenario = Find-UiaElement $root 'RecoveryBlockedScenarioButton'
        Scroll-UiaElementIntoView $blockedScenario
        Select-UiaElement $blockedScenario
        Assert-Condition ($recoveryStatus.Current.ItemStatus -eq 'BlockedRecoveryPreview') `
            'Blocked recovery semantics were not exposed to UI Automation.'
        Assert-Condition (-not $reviewRecovery.Current.IsEnabled) `
            'Blocked recovery incorrectly enabled partial acknowledgement.'
        $automaticScenario = Find-UiaElement $root 'RecoveryAutomaticScenarioButton'
        Scroll-UiaElementIntoView $automaticScenario
        Select-UiaElement $automaticScenario
        Assert-Condition ($recoveryStatus.Current.ItemStatus -eq 'AutomaticRecoveryPreview') `
            'Automatic recovery semantics were not exposed to UI Automation.'
        $cancelRecovery = Find-UiaElement $root 'CancelRecoveryPreviewButton'
        Scroll-UiaElementIntoView $cancelRecovery
        Select-UiaElement $cancelRecovery
        Assert-Condition ($recoveryStatus.Current.ItemStatus -eq 'RecoveryPreviewCancelled') `
            'Cancelling the anonymous recovery preview did not preserve the current layout.'

        $appearance.SetFocus()
        Start-Sleep -Milliseconds 150
        Assert-Condition (
            [System.Windows.Automation.AutomationElement]::FocusedElement.Current.AutomationId -eq
                'NavAppearance'
        ) 'Navigation focus could not be moved to NavAppearance.'
        Select-UiaElement $appearance

        $themeDark = Find-UiaElement $root 'ThemeDark'
        Assert-Condition $themeDark.Current.IsKeyboardFocusable `
            'The dark theme option is not keyboard focusable.'
        Select-UiaElement $themeDark
        $themeStatus = Find-UiaElement $root 'ThemeStatusText'
        $darkText = [string]([char]0x6DF1) + [char]0x8272
        Wait-UiaName $themeStatus $darkText

        $themeSystem = Find-UiaElement $root 'ThemeSystem'
        Select-UiaElement $themeSystem
        $systemText = [string]([char]0x8DDF) +
            [char]0x968F + [char]0x7CFB + [char]0x7EDF
        Wait-UiaName $themeStatus $systemText

        Select-UiaElement $safety
        $safetyPanel = Find-UiaElement $root 'SafetyPanel'
        Assert-Condition (-not $safetyPanel.Current.IsOffscreen) `
            'SafetyPanel stayed offscreen after selecting its navigation item.'

        $liveResult = [ordered]@{
            windowTitle = $root.Current.Name
            processId = $process.Id
            navigationAutomationId = $navigation.Current.AutomationId
            navigationItems = 5
            keyboardFocus = 'pass'
            responsiveLayout = 'wide-compact-wide-720'
            responsiveItemStatus = $layoutRoot.Current.ItemStatus
            compactCards = 3
            compactOrganizationModes = 2
            coreRuntimeStatus = 'development-read-only'
            firstOrganizationPrototype = 'blank-suggested-safe-preview-items-drop-semantics-two-step-undo'
            layoutRecoveryPrototype = 'review-expired-review-acknowledged-blocked-automatic-cancelled'
            productDesktopCatalog = $productCatalogStatus.Current.ItemStatus
            productWorkspaceSession = $productSessionStatus.Current.ItemStatus
            productSavePresentation = $productSaveStatus.Current.ItemStatus
            themeRoundTrip = 'system-dark-system'
            safetyNavigation = 'pass'
            cleanSessionStart = 'zero-existing-processes'
        }
    }
    finally {
        if ($null -ne $process -and -not $process.HasExited) {
            $null = $process.CloseMainWindow()
            if (-not $process.WaitForExit(5000)) {
                Stop-Process -Id $process.Id -Force
                $process.WaitForExit()
            }
        }
    }

    $remainingProcesses = @(
        Get-Process -Name 'LongGrid.App' -ErrorAction SilentlyContinue
    )
    Assert-Condition ($remainingProcesses.Count -eq 0) `
        "Clean-session UIA left LongGrid.App process PID(s): $($remainingProcesses.Id -join ', ')."
    $liveResult.cleanSessionEnd = 'zero-remaining-processes'
    return $liveResult
}

Push-Location $projectRoot
try {
    $contractResult = Test-SourceContract
    $liveResult = if ($ContractOnly) { $null } else { Test-LiveUi }

    [ordered]@{
        contract = $contractResult
        live = $liveResult
        mode = if ($ContractOnly) { 'contract-only' } else { 'contract-and-live' }
        outcome = 'Pass'
    } | ConvertTo-Json -Depth 4
}
finally {
    Pop-Location
}
