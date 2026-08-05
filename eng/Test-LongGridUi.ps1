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
    Assert-Condition ($appCode -match 'configurationSaves\.CompleteAsync') `
        'Window closing must complete and drain the configuration save coordinator.'
    Assert-Condition ($appCode -match 'closingDrainInProgress') `
        'Concurrent close requests must share one configuration drain attempt.'
    Assert-Condition (-not ($appCode -match 'configurationSaves\.EnqueueAsync')) `
        'The development read-only shell must not enqueue product configuration writes.'

    return [ordered]@{
        requiredAutomationIds = $requiredIds.Count
        accessKeys = $expectedAccessKeys.Count
        themeModes = 3
        responsiveBreakpoints = 1
        compactWidth = 720
        dpiAwareInitialSize = 'pass'
        coreRuntimeStatus = 'development-read-only'
        firstOrganizationPrototype = 'safe-reference-items-drop-semantics-undo'
        layoutRecoveryPrototype = 'automatic-review-blocked-expire-cancel'
        configurationRecovery = 'loaded-missing-backup-read-only-safe-mode'
        configurationRepair = 'confirmed-recovery-bounded-import-export-evidence-inventory-and-evidence-export'
        configurationShutdownDrain = 'bounded-zero-write-retry'
        readOnlyBoundary = 'no-automatic-product-writes-explicit-config-transactions-only'
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

        return [ordered]@{
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
            themeRoundTrip = 'system-dark-system'
            safetyNavigation = 'pass'
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
