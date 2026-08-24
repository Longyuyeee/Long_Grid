[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64')]
    [string]$Architecture = 'x64',

    [switch]$NoBuild,
    [switch]$ContractOnly,
    [switch]$DesktopHostDevelopmentOptIn,
    [switch]$AcknowledgeKnownUiaCrashRisk
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$xamlPath = Join-Path $projectRoot 'src\LongGrid.App\MainWindow.xaml'
$codeBehindPath = Join-Path $projectRoot 'src\LongGrid.App\MainWindow.xaml.cs'
$appCodePath = Join-Path $projectRoot 'src\LongGrid.App\App.xaml.cs'
$pf002AppEvidenceCodePath = Join-Path $projectRoot `
    'src\LongGrid.App\ProductPf002AppEvidenceSession.cs'
$winUiRuntimeSafetyCodePath = Join-Path $projectRoot `
    'src\LongGrid.App\ProductWinUiRuntimeSafety.cs'
$referenceReviewCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceReferenceReview.cs'
$referenceCommitCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\Configuration\ProductWorkspaceReferenceCommitCoordinator.cs'
$workspaceReducerCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceReducer.cs'
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
$workspaceReviewShortcutPolicyCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceReviewShortcutPolicy.cs'
$workspaceContainerNavigationPolicyCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceContainerNavigationPolicy.cs'
$workspaceContainerQuickCollapsePolicyCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceContainerQuickCollapsePolicy.cs'
$workspaceContainerQuickLockPolicyCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceContainerQuickLockPolicy.cs'
$workspaceVisibleSearchPolicyCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceVisibleSearchPolicy.cs'
$workspaceContainerSortPolicyCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceContainerSortPolicy.cs'
$workspaceViewResetPolicyCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceViewResetPolicy.cs'
$workspaceEmptyCreateShortcutPolicyCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceEmptyCreateShortcutPolicy.cs'
$workspaceContainerNameIntentPolicyCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceContainerNameIntentPolicy.cs'
$workspaceReadPresentationCodePath = Join-Path $projectRoot `
    'src\LongGrid.App\ProductWorkspaceReadPresentation.cs'
$containerEditPresentationCodePath = Join-Path $projectRoot `
    'src\LongGrid.App\ProductWorkspaceContainerEditPresentation.cs'
$containerRemovalUndoCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceContainerRemovalUndo.cs'
$latestUndoSelectorCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\Configuration\ProductWorkspaceLatestUndoSelector.cs'
$latestUndoPresentationCodePath = Join-Path $projectRoot `
    'src\LongGrid.App\ProductWorkspaceLatestUndoPresentation.cs'
$referenceBatchAdditionUndoCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceReferenceBatchAdditionUndo.cs'
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
$desktopHostFeaturePolicyCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\DesktopHost\ProductDesktopHostFeaturePolicy.cs'
$desktopItemVisualPresentationCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\DesktopHost\ProductDesktopItemVisualPresentation.cs'
$desktopThumbnailRequestControllerCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopThumbnailRequestController.cs'
$desktopItemViewportCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopItemViewport.cs'
$desktopItemOpenCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopItemOpen.cs'
$desktopItemOpenReferenceResolverCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopItemOpenReferenceResolver.cs'
$boxesSettingsCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductBoxesSettings.cs'
$desktopInteractionAdmissionCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\DesktopHost\ProductDesktopInteractionAdmission.cs'
$desktopInteractionCancellationCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\DesktopHost\ProductDesktopInteractionCancellationAdapter.cs'
$desktopInteractionDevelopmentControllerCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\DesktopHost\ProductDesktopInteractionDevelopmentController.cs'
$desktopInteractionSystemSurfaceEventCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\DesktopHost\ProductDesktopInteractionSystemSurfaceEvent.cs'
$desktopInteractionIntentBridgePolicyCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\DesktopHost\ProductDesktopInteractionIntentBridgePolicy.cs'
$desktopInteractionInputForwardingPolicyCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\DesktopHost\ProductDesktopInteractionInputForwardingPolicy.cs'
$desktopInteractionHitTestCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopInteractionHitTestAdapter.cs'
$desktopInteractionSelectionCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\DesktopHost\ProductDesktopInteractionSelection.cs'
$desktopInteractionSelectionAccessibilityCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\DesktopHost\ProductDesktopInteractionSelectionAccessibility.cs'
$desktopInteractionSurfaceModeCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\DesktopHost\ProductDesktopInteractionSurfaceModeTransaction.cs'
$nativeInteractionSurfaceProbeCodePath = Join-Path $projectRoot `
    'probes\LongGrid.Spikes.DesktopHostWindowModels\NativeInteractionSurfaceModeProbe.cs'
$nativeInputForwardingSourceProbeCodePath = Join-Path $projectRoot `
    'probes\LongGrid.Spikes.DesktopHostWindowModels\NativeInputForwardingSourceProbe.cs'
$readOnlyDisplayTopologyObserverCodePath = Join-Path $projectRoot `
    'probes\LongGrid.Spikes.DesktopHostWindowModels\ReadOnlyDisplayTopologyGenerationObserver.cs'
$desktopHostProbeProgramCodePath = Join-Path $projectRoot `
    'probes\LongGrid.Spikes.DesktopHostWindowModels\Program.cs'
$desktopHostPassiveSurfaceAdapterCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopHostPassiveSurfaceModeAdapter.cs'
$desktopHostLifecycleControllerCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopHostLifecycleController.cs'
$desktopSystemSurfaceEventSourceCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\WindowsProductDesktopInteractionSystemSurfaceEventSource.cs'
$desktopIntentPreparationBridgeCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopInteractionIntentPreparationBridge.cs'
$desktopIntentSessionLauncherCodePath = Join-Path $projectRoot `
    'eng\Start-DesktopInteractionIntentSession.ps1'
$desktopInputForwardingAdapterCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopInteractionInputForwardingAdapter.cs'
$desktopIntentConsumptionControllerCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopInteractionIntentConsumptionController.cs'
$desktopInputForwardingSessionLauncherCodePath = Join-Path $projectRoot `
    'eng\Start-DesktopInteractionInputForwardingSession.ps1'
$desktopSystemSurfaceSessionLauncherCodePath = Join-Path $projectRoot `
    'eng\Start-DesktopInteractionSystemSurfaceSession.ps1'
$desktopHostProductSessionLauncherCodePath = Join-Path $projectRoot `
    'eng\Start-DesktopHostProductSessionMatrix.ps1'
$desktopHostProjectionBatchCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopHostProjectionBatch.cs'
$desktopHostProjectionUpdateCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopHostProjectionUpdate.cs'
$desktopHostProjectionBuilderCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopHostProjectionBuilder.cs'
$windowsDesktopHostWindowInspectorCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\WindowsProductDesktopHostWindowInspector.cs'
$windowsDesktopHostReadOnlySurfaceCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\WindowsProductDesktopHostReadOnlySurface.cs'
$desktopContainerHeaderPresentationCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\DesktopHost\ProductDesktopContainerHeaderPresentation.cs'
$desktopContainerHeaderCommandCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\DesktopHost\ProductDesktopContainerHeaderCommand.cs'
$desktopContainerHeaderCommandControllerCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopContainerHeaderCommandController.cs'
$desktopContainerMenuCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\DesktopHost\ProductDesktopContainerMenu.cs'
$desktopContainerMenuNavigationCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopContainerMenuNavigationController.cs'
$desktopContainerDeleteControllerCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopContainerDeleteController.cs'
$desktopInteractionActivationSourceCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\ProductDesktopInteractionActivationSource.cs'
$desktopWorkspaceCreateAdmissionCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\DesktopHost\ProductDesktopWorkspaceCreateAdmission.cs'
$workspaceContainerCreationDefaultsCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductWorkspaceContainerCreationDefaults.cs'
$desktopWorkspaceCreatePreviewCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductDesktopWorkspaceCreatePreview.cs'
$desktopWorkspaceCreatePreviewPlacementCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductDesktopWorkspaceCreatePreviewPlacement.cs'
$desktopWorkspaceCreatePublicationCodePath = Join-Path $projectRoot `
    'src\LongGrid.Core\Configuration\ProductDesktopWorkspaceCreatePublication.cs'
$desktopWorkspaceCreateInlinePreviewCodePath = Join-Path $projectRoot `
    'src\LongGrid.App\DesktopWorkspaceCreatePreviewWindow.cs'
$windowsDesktopHostUiaProviderCodePath = Join-Path $projectRoot `
    'src\LongGrid.Infrastructure\DesktopHost\WindowsProductDesktopHostUiaProvider.cs'
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
    $pf002AppEvidenceCode = Get-Content `
        -LiteralPath $pf002AppEvidenceCodePath `
        -Raw `
        -Encoding UTF8
    $winUiRuntimeSafetyCode = Get-Content `
        -LiteralPath $winUiRuntimeSafetyCodePath `
        -Raw `
        -Encoding UTF8
    $referenceReviewCode = Get-Content `
        -LiteralPath $referenceReviewCodePath `
        -Raw `
        -Encoding UTF8
    $referenceCommitCode = Get-Content `
        -LiteralPath $referenceCommitCodePath `
        -Raw `
        -Encoding UTF8
    $workspaceReducerCode = Get-Content `
        -LiteralPath $workspaceReducerCodePath `
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
    $workspaceReviewShortcutPolicyCode = Get-Content `
        -LiteralPath $workspaceReviewShortcutPolicyCodePath `
        -Raw `
        -Encoding UTF8
    $workspaceContainerNavigationPolicyCode = Get-Content `
        -LiteralPath $workspaceContainerNavigationPolicyCodePath `
        -Raw `
        -Encoding UTF8
    $workspaceContainerQuickCollapsePolicyCode = Get-Content `
        -LiteralPath $workspaceContainerQuickCollapsePolicyCodePath `
        -Raw `
        -Encoding UTF8
    $workspaceContainerQuickLockPolicyCode = Get-Content `
        -LiteralPath $workspaceContainerQuickLockPolicyCodePath `
        -Raw `
        -Encoding UTF8
    $workspaceVisibleSearchPolicyCode = Get-Content `
        -LiteralPath $workspaceVisibleSearchPolicyCodePath `
        -Raw `
        -Encoding UTF8
    $workspaceContainerSortPolicyCode = Get-Content `
        -LiteralPath $workspaceContainerSortPolicyCodePath `
        -Raw `
        -Encoding UTF8
    $workspaceViewResetPolicyCode = Get-Content `
        -LiteralPath $workspaceViewResetPolicyCodePath `
        -Raw `
        -Encoding UTF8
    $workspaceEmptyCreateShortcutPolicyCode = Get-Content `
        -LiteralPath $workspaceEmptyCreateShortcutPolicyCodePath `
        -Raw `
        -Encoding UTF8
    $workspaceContainerNameIntentPolicyCode = Get-Content `
        -LiteralPath $workspaceContainerNameIntentPolicyCodePath `
        -Raw `
        -Encoding UTF8
    $desktopWorkspaceCreatePublicationCode = Get-Content `
        -LiteralPath $desktopWorkspaceCreatePublicationCodePath `
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
    $containerRemovalUndoCode = Get-Content `
        -LiteralPath $containerRemovalUndoCodePath `
        -Raw `
        -Encoding UTF8
    $latestUndoSelectorCode = Get-Content `
        -LiteralPath $latestUndoSelectorCodePath `
        -Raw `
        -Encoding UTF8
    $latestUndoPresentationCode = Get-Content `
        -LiteralPath $latestUndoPresentationCodePath `
        -Raw `
        -Encoding UTF8
    $referenceBatchAdditionUndoCode = Get-Content `
        -LiteralPath $referenceBatchAdditionUndoCodePath `
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
    $desktopHostFeaturePolicyCode = Get-Content `
        -LiteralPath $desktopHostFeaturePolicyCodePath `
        -Raw `
        -Encoding UTF8
    $desktopInteractionAdmissionCode = Get-Content `
        -LiteralPath $desktopInteractionAdmissionCodePath `
        -Raw `
        -Encoding UTF8
    $desktopInteractionCancellationCode = Get-Content `
        -LiteralPath $desktopInteractionCancellationCodePath `
        -Raw `
        -Encoding UTF8
    $desktopInteractionDevelopmentControllerCode = Get-Content `
        -LiteralPath $desktopInteractionDevelopmentControllerCodePath `
        -Raw `
        -Encoding UTF8
    $desktopInteractionSystemSurfaceEventCode = Get-Content `
        -LiteralPath $desktopInteractionSystemSurfaceEventCodePath `
        -Raw `
        -Encoding UTF8
    $desktopInteractionIntentBridgePolicyCode = Get-Content `
        -LiteralPath $desktopInteractionIntentBridgePolicyCodePath `
        -Raw `
        -Encoding UTF8
    $desktopInteractionInputForwardingPolicyCode = Get-Content `
        -LiteralPath $desktopInteractionInputForwardingPolicyCodePath `
        -Raw `
        -Encoding UTF8
    $desktopInteractionHitTestCode = Get-Content `
        -LiteralPath $desktopInteractionHitTestCodePath `
        -Raw `
        -Encoding UTF8
    $desktopInteractionSelectionCode = Get-Content `
        -LiteralPath $desktopInteractionSelectionCodePath `
        -Raw `
        -Encoding UTF8
    $desktopInteractionSelectionAccessibilityCode = Get-Content `
        -LiteralPath $desktopInteractionSelectionAccessibilityCodePath `
        -Raw `
        -Encoding UTF8
    $desktopInteractionSurfaceModeCode = Get-Content `
        -LiteralPath $desktopInteractionSurfaceModeCodePath `
        -Raw `
        -Encoding UTF8
    $nativeInteractionSurfaceProbeCode = Get-Content `
        -LiteralPath $nativeInteractionSurfaceProbeCodePath `
        -Raw `
        -Encoding UTF8
    $nativeInputForwardingSourceProbeCode = Get-Content `
        -LiteralPath $nativeInputForwardingSourceProbeCodePath `
        -Raw `
        -Encoding UTF8
    $readOnlyDisplayTopologyObserverCode = Get-Content `
        -LiteralPath $readOnlyDisplayTopologyObserverCodePath `
        -Raw `
        -Encoding UTF8
    $desktopHostProbeProgramCode = Get-Content `
        -LiteralPath $desktopHostProbeProgramCodePath `
        -Raw `
        -Encoding UTF8
    $desktopHostPassiveSurfaceAdapterCode = Get-Content `
        -LiteralPath $desktopHostPassiveSurfaceAdapterCodePath `
        -Raw `
        -Encoding UTF8
    $desktopHostLifecycleControllerCode = Get-Content `
        -LiteralPath $desktopHostLifecycleControllerCodePath `
        -Raw `
        -Encoding UTF8
    $desktopSystemSurfaceEventSourceCode = Get-Content `
        -LiteralPath $desktopSystemSurfaceEventSourceCodePath `
        -Raw `
        -Encoding UTF8
    $desktopIntentPreparationBridgeCode = Get-Content `
        -LiteralPath $desktopIntentPreparationBridgeCodePath `
        -Raw `
        -Encoding UTF8
    $desktopIntentSessionLauncherCode = Get-Content `
        -LiteralPath $desktopIntentSessionLauncherCodePath `
        -Raw `
        -Encoding UTF8
    $desktopInputForwardingAdapterCode = Get-Content `
        -LiteralPath $desktopInputForwardingAdapterCodePath `
        -Raw `
        -Encoding UTF8
    $desktopIntentConsumptionControllerCode = Get-Content `
        -LiteralPath $desktopIntentConsumptionControllerCodePath `
        -Raw `
        -Encoding UTF8
    $desktopInputForwardingSessionLauncherCode = Get-Content `
        -LiteralPath $desktopInputForwardingSessionLauncherCodePath `
        -Raw `
        -Encoding UTF8
    $desktopSystemSurfaceSessionLauncherCode = Get-Content `
        -LiteralPath $desktopSystemSurfaceSessionLauncherCodePath `
        -Raw `
        -Encoding UTF8
    $desktopHostProductSessionLauncherCode = Get-Content `
        -LiteralPath $desktopHostProductSessionLauncherCodePath `
        -Raw `
        -Encoding UTF8
    $desktopHostProjectionBatchCode = Get-Content `
        -LiteralPath $desktopHostProjectionBatchCodePath `
        -Raw `
        -Encoding UTF8
    $desktopHostProjectionUpdateCode = Get-Content `
        -LiteralPath $desktopHostProjectionUpdateCodePath `
        -Raw `
        -Encoding UTF8
    $desktopHostProjectionBuilderCode = Get-Content `
        -LiteralPath $desktopHostProjectionBuilderCodePath `
        -Raw `
        -Encoding UTF8
    $desktopItemVisualPresentationCode = Get-Content `
        -LiteralPath $desktopItemVisualPresentationCodePath `
        -Raw `
        -Encoding UTF8
    $desktopThumbnailRequestControllerCode = Get-Content `
        -LiteralPath $desktopThumbnailRequestControllerCodePath `
        -Raw `
        -Encoding UTF8
    $desktopItemViewportCode = Get-Content `
        -LiteralPath $desktopItemViewportCodePath `
        -Raw `
        -Encoding UTF8
    $desktopItemOpenCode = Get-Content `
        -LiteralPath $desktopItemOpenCodePath `
        -Raw `
        -Encoding UTF8
    $desktopItemOpenReferenceResolverCode = Get-Content `
        -LiteralPath $desktopItemOpenReferenceResolverCodePath `
        -Raw `
        -Encoding UTF8
    $boxesSettingsCode = Get-Content `
        -LiteralPath $boxesSettingsCodePath `
        -Raw `
        -Encoding UTF8
    $windowsDesktopHostWindowInspectorCode = Get-Content `
        -LiteralPath $windowsDesktopHostWindowInspectorCodePath `
        -Raw `
        -Encoding UTF8
    $windowsDesktopHostReadOnlySurfaceCode = Get-Content `
        -LiteralPath $windowsDesktopHostReadOnlySurfaceCodePath `
        -Raw `
        -Encoding UTF8
    $desktopContainerHeaderPresentationCode = Get-Content `
        -LiteralPath $desktopContainerHeaderPresentationCodePath `
        -Raw `
        -Encoding UTF8
    $desktopContainerHeaderCommandCode = Get-Content `
        -LiteralPath $desktopContainerHeaderCommandCodePath `
        -Raw `
        -Encoding UTF8
    $desktopContainerHeaderCommandControllerCode = Get-Content `
        -LiteralPath $desktopContainerHeaderCommandControllerCodePath `
        -Raw `
        -Encoding UTF8
    $desktopContainerMenuCode = Get-Content `
        -LiteralPath $desktopContainerMenuCodePath `
        -Raw `
        -Encoding UTF8
    $desktopContainerMenuNavigationCode = Get-Content `
        -LiteralPath $desktopContainerMenuNavigationCodePath `
        -Raw `
        -Encoding UTF8
    $desktopContainerDeleteControllerCode = Get-Content `
        -LiteralPath $desktopContainerDeleteControllerCodePath `
        -Raw `
        -Encoding UTF8
    $desktopInteractionActivationSourceCode = Get-Content `
        -LiteralPath $desktopInteractionActivationSourceCodePath `
        -Raw `
        -Encoding UTF8
    $desktopWorkspaceCreateAdmissionCode = Get-Content `
        -LiteralPath $desktopWorkspaceCreateAdmissionCodePath `
        -Raw `
        -Encoding UTF8
    $workspaceContainerCreationDefaultsCode = Get-Content `
        -LiteralPath $workspaceContainerCreationDefaultsCodePath `
        -Raw `
        -Encoding UTF8
    $desktopWorkspaceCreatePreviewCode = Get-Content `
        -LiteralPath $desktopWorkspaceCreatePreviewCodePath `
        -Raw `
        -Encoding UTF8
    $desktopWorkspaceCreatePreviewPlacementCode = Get-Content `
        -LiteralPath $desktopWorkspaceCreatePreviewPlacementCodePath `
        -Raw `
        -Encoding UTF8
    $desktopWorkspaceCreateInlinePreviewCode = Get-Content `
        -LiteralPath $desktopWorkspaceCreateInlinePreviewCodePath `
        -Raw `
        -Encoding UTF8
    $windowsDesktopHostUiaProviderCode = Get-Content `
        -LiteralPath $windowsDesktopHostUiaProviderCodePath `
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
        'CaptureAnonymousInteractionEvidenceButton',
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
        'BoxesEnabledToggle',
        'BoxesEnabledStatus',
        'ThumbnailsEnabledToggle',
        'ThumbnailsEnabledStatus',
        'CurrentModeValue',
        'FileOperationValue',
        'DesktopHostValue',
        'DesktopKeyboardInteractionButton',
        'ProductDesktopCatalogCard',
        'ProductDesktopCatalogTitle',
        'ProductDesktopCatalogDetail',
        'ProductDesktopCatalogGeneration',
        'ProductDesktopCatalogRefreshButton',
        'ProductWorkspaceSessionCard',
        'ProductWorkspaceSessionTitle',
        'ProductWorkspaceSessionDetail',
        'ProductWorkspaceSessionSummary',
        'ProductWorkspaceLatestUndoButton',
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
        'ProductWorkspaceContainerNameGuidance',
        'ProductWorkspaceContainerCreateButton',
        'ProductWorkspaceContainerRenameButton',
        'ProductWorkspaceContainerLockButton',
        'ProductWorkspaceContainerCollapseButton',
        'ProductWorkspaceContainerColorSelector',
        'ProductWorkspaceContainerOpacitySelector',
        'ProductWorkspaceContainerTitleVisibilitySelector',
        'ProductWorkspaceContainerTitleDoubleClickSelector',
        'ProductWorkspaceContainerAppearanceButton',
        'ProductWorkspaceContainerPositionSelector',
        'ProductWorkspaceContainerSizeSelector',
        'ProductWorkspaceContainerPlacementButton',
        'ProductWorkspaceContainerRemoveButton',
        'ProductWorkspaceContainerRemovalUndoButton',
        'ProductWorkspaceResolvedReferenceSelector',
        'ProductWorkspaceResolvedReferenceSelectFirstBatchButton',
        'ProductWorkspaceResolvedReferenceClearSelectionButton',
        'ProductWorkspaceResolvedReferenceAddButton',
        'ProductWorkspaceReferenceBatchAdditionUndoButton',
        'ProductWorkspaceResolvedReferenceAddStatus',
        'ProductWorkspaceResolvedReferenceRemovalSelector',
        'ProductWorkspaceResolvedReferenceSelectContainerBatchButton',
        'ProductWorkspaceResolvedReferenceRemovalClearSelectionButton',
        'ProductWorkspaceSelectedReferenceCreateButton',
        'ProductWorkspaceResolvedReferenceReassignmentTargetSelector',
        'ProductWorkspaceResolvedReferenceRemovalButton',
        'ProductWorkspaceResolvedReferenceReassignmentButton',
        'ProductWorkspaceResolvedReferenceRemovalUndoButton',
        'ProductWorkspaceResolvedReferenceRemovalStatus',
        'ProductWorkspaceContainerEditStatus',
        'ProductWorkspaceSearchBox',
        'ProductWorkspaceHealthFilterSelector',
        'ProductWorkspaceSortSelector',
        'ProductWorkspaceResetViewButton',
        'ProductWorkspaceEmptyCreateButton',
        'ProductWorkspaceOpenReviewButton',
        'ProductWorkspaceContainerList',
        'ProductWorkspaceViewStatus',
        'DesktopWorkspaceCreateSafePreviewOverlay',
        'DesktopWorkspaceCreateSafePreviewNameEditor',
        'DesktopWorkspaceCreateSafePreviewPlacementSummary',
        'DesktopWorkspaceCreateSafePreviewValidation',
        'DesktopWorkspaceCreateSafePreviewCancelButton',
        'DesktopWorkspaceCreateSafePreviewConfirmButton',
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

    Assert-Condition (
        $pf002AppEvidenceCode.Contains(
            'LONGGRID_PF002_APP_EVIDENCE_SESSION') -and
        $pf002AppEvidenceCode.Contains('Path.GetTempPath()') -and
        $pf002AppEvidenceCode.Contains('ReparsePoint') -and
        $pf002AppEvidenceCode.Contains(
            'Directory.EnumerateFileSystemEntries(directoryPath).Any()') -and
        $appCode.Contains('RunPf002AppEvidenceSessionAsync') -and
        $appCode.Contains('HidingFormalWindowFromKnownUnsafeUiaRuntime') -and
        $appCode.Contains('ExecutingFormalLatestUndo') -and
        $codeBehind.Contains(
            'ExecuteProductWorkspaceLatestUndoForEvidence') -and
        $appCode.Contains('VisibleViewPublication = "BlockedByKnownUpstream"')
    ) 'PF-002 App evidence must remain opt-in, temporary, non-reparse, UI-thread driven, and honest about blocked visible publication.'
    Assert-Condition (
        $winUiRuntimeSafetyCode.Contains(
            'Microsoft.WindowsAppRuntime.2_2.4.0.0_') -and
        $winUiRuntimeSafetyCode.Contains('FileMajorPart == 3') -and
        $winUiRuntimeSafetyCode.Contains('FileBuildPart == 3') -and
        $appCode.Contains('RequiresSingleWindowPreview()') -and
        $codeBehind.Contains('ShowDesktopWorkspaceCreateSafePreviewAsync')
    ) 'The attested unsafe WinUI pair must fail closed to the persistent single-window preview surface.'

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
    $captureEvidenceNode = Get-XamlNodeByAutomationId `
        $document `
        'CaptureAnonymousInteractionEvidenceButton'
    Assert-Condition (
        $captureEvidenceNode.GetAttribute('Click') -eq `
            'CaptureAnonymousInteractionEvidenceButton_Click'
    ) 'Anonymous interaction evidence must use the confirmed one-shot capture handler.'
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

    $desktopKeyboardInteractionNode = Get-XamlNodeByAutomationId `
        $document `
        'DesktopKeyboardInteractionButton'
    Assert-Condition (
        $desktopKeyboardInteractionNode.GetAttribute('Click') -eq `
            'DesktopKeyboardInteractionButton_Click' -and
        $desktopKeyboardInteractionNode.GetAttribute('AccessKey') -eq 'I' -and
        $desktopKeyboardInteractionNode.GetAttribute('IsEnabled') -eq 'False'
    ) 'Desktop interaction must use a disabled-by-default standard App command.'

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
    $latestUndoButtonNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceLatestUndoButton'
    Assert-Condition (
        $latestUndoButtonNode.GetAttribute('IsEnabled') -eq 'False' -and
        $latestUndoButtonNode.GetAttribute('Visibility') -eq 'Collapsed' -and
        $latestUndoButtonNode.GetAttribute('Click') -eq `
            'ProductWorkspaceLatestUndoButton_Click' -and
        $latestUndoButtonNode.GetAttribute('AutomationProperties.ItemStatus') -eq `
            'LatestWorkspaceEditUndo:Kind=Unavailable:CanUndo=False:DesktopFilesChanged=False:DesktopWindowsChanged=False'
    ) 'Latest workspace edit undo must start hidden, disabled, finite, and non-mutating.'
    Assert-Condition (
        $latestUndoSelectorCode.Contains('available.Length != 1') -and
        $latestUndoSelectorCode.Contains('ProductWorkspaceLatestUndoKind.Conflict') -and
        $latestUndoPresentationCode.Contains('ProductWorkspaceLatestUndoSelector.Select') -and
        $latestUndoPresentationCode.Contains('DesktopFilesChanged=False') -and
        $codeBehind.Contains('private void ProductWorkspaceLatestUndoButton_Click(') -and
        $codeBehind.Contains('_commitProductWorkspaceLayoutRecoveryUndo(token, true)') -and
        $codeBehind.Contains('_commitProductWorkspaceContainerRemovalUndo(token, true)') -and
        $codeBehind.Contains('_commitProductWorkspaceReferenceBatchAdditionUndo(token, true)') -and
        $codeBehind.Contains('_commitProductWorkspaceReferenceRemovalUndo(token, true)') -and
        $codeBehind.Contains('_commitProductWorkspaceReferenceReassignmentUndo(token, true)') -and
        $appCode.Contains('ApplyProductWorkspaceLatestUndo(')
    ) 'Latest workspace edit undo must fail closed and reuse every audited token/commit path.'

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
    $containerNameGuidanceNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceContainerNameGuidance'
    Assert-Condition (
        $containerNameGuidanceNode.GetAttribute('AutomationProperties.ItemStatus') -eq `
            'WorkspaceContainerNameIntentUnavailable:CanCreate=False:CanRename=False:Changed=False:DesktopFilesChanged=False' -and
        [string]::IsNullOrEmpty(
            $containerNameGuidanceNode.GetAttribute('AutomationProperties.LiveSetting'))
    ) 'Formal container name guidance must start finite and must not announce on every keystroke.'
    Assert-Condition (
        $workspaceContainerNameIntentPolicyCode -match `
            'ProductConfigurationLimits\.MaximumNameLength' -and
        $workspaceContainerNameIntentPolicyCode -match 'name\.Trim\(\)' -and
        $workspaceContainerNameIntentPolicyCode -match 'selectedIsLocked' -and
        $workspaceContainerNameIntentPolicyCode -match `
            'StringComparison\.Ordinal' -and
        $workspaceContainerNameIntentPolicyCode -match `
            'ProductWorkspaceContainerNameIntentStatus\.RenameNoChange' -and
        $codeBehind -match 'ProductWorkspaceContainerNameIntentPolicy\.Evaluate' -and
        $codeBehind -match 'ProductWorkspaceContainerCreateButton\.IsEnabled\s*=\s*nameIntent\.CanCreate' -and
        $codeBehind -match 'ProductWorkspaceContainerRenameButton\.IsEnabled\s*=\s*nameIntent\.CanRename' -and
        $codeBehind -match 'AutomationProperties\.SetHelpText\(\s*ProductWorkspaceContainerNameEditor' -and
        $codeBehind -match 'WorkspaceContainerNameIntent'
    ) 'Formal container name actions must use bounded pre-submit intent guidance and disable locked or unchanged renames.'
    Assert-Condition (
        -not ($workspaceContainerNameIntentPolicyCode -match `
            'Catalog|PersistedTarget|CanonicalTarget|DesktopHost|File\.|Directory\.|Save|Telemetry')
    ) 'Formal container name intent must remain a pure UI admission policy without persistence, catalog, telemetry, or desktop authority.'
    Assert-Condition (
        $desktopWorkspaceCreatePublicationCode.Contains(
            'ProductDesktopWorkspaceCreatePublicationDecision.RollbackRequired') -and
        $desktopWorkspaceCreatePublicationCode.Contains(
            'currentWorkspaceRevision != token.WorkspaceRevision') -and
        $desktopWorkspaceCreatePublicationCode.Contains(
            'save.CurrentRevision != token.SaveRevision') -and
        $appCode.Contains('desktopWorkspaceCreatePublication = new(') -and
        $appCode.Contains('ApplyProductWorkspaceCreateSaveRollbackState(') -and
        $codeBehind.Contains('WorkspaceCreateRolledBack:')
    ) 'Desktop create publication must bind workspace/save revisions, compensate matching failures, and expose a finite rollback state.'
    Assert-Condition (
        $workspaceReducerCode.Contains(
            'CreateContainerFromResolvedReferences(') -and
        $workspaceReducerCode.Contains(
            'selected.Any(item => item.Resolution !=') -and
        $workspaceReducerCode.Contains(
            'Items = selected.Select(Clone).ToArray()') -and
        $referenceCommitCode.Contains(
            'CommitSelectedReferenceContainer(') -and
        $referenceCommitCode.Contains(
            'itemIds.Length > MaximumResolvedReferenceBatchSize') -and
        $referenceCommitCode.Contains(
            'ProductWorkspaceReferenceBatchAdditionUndo.Prepare(') -and
        $referenceCommitCode.Contains('saves.Submit(edit)') -and
        $appCode.Contains('RequestProductWorkspaceSelectedReferenceCreate(') -and
        $appCode.Contains('SelectedReferenceCreateStillCurrent(') -and
        $appCode.Contains('publication.RestoreToken is { } restoreToken') -and
        $appCode.Contains('CommitProductWorkspaceReferenceBatchAdditionUndo(') -and
        $codeBehind.Contains(
            'ProductWorkspaceSelectedReferenceCreateButton_Click(')
    ) 'Selected-reference container creation must capture one bounded Long Grid selection, reuse preview, atomically move references, and restore the whole prior state on matching save failure.'
    foreach ($buttonId in @(
            'ProductWorkspaceContainerCreateButton',
            'ProductWorkspaceContainerRenameButton',
            'ProductWorkspaceContainerLockButton',
            'ProductWorkspaceContainerCollapseButton',
            'ProductWorkspaceContainerAppearanceButton',
            'ProductWorkspaceContainerPlacementButton',
            'ProductWorkspaceContainerRemoveButton',
            'ProductWorkspaceContainerRemovalUndoButton'
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
        ProductWorkspaceContainerRemoveButton = `
            'ProductWorkspaceContainerRemoveButton_Click'
        ProductWorkspaceContainerRemovalUndoButton = `
            'ProductWorkspaceContainerRemovalUndoButton_Click'
    }
    foreach ($selectorId in @(
            'ProductWorkspaceContainerColorSelector',
            'ProductWorkspaceContainerOpacitySelector',
            'ProductWorkspaceContainerTitleVisibilitySelector',
            'ProductWorkspaceContainerTitleDoubleClickSelector'
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
        $resolvedReferenceSelectorNode.GetAttribute('SelectionMode') -eq 'Multiple' -and
        $resolvedReferenceSelectorNode.GetAttribute('SelectionChanged') -eq `
            'ProductWorkspaceResolvedReferenceSelector_SelectionChanged'
    ) 'Resolved-reference selection must start disabled and use the audited handler.'
    foreach ($entry in @{
            ProductWorkspaceResolvedReferenceSelectFirstBatchButton =
                'ProductWorkspaceResolvedReferenceSelectFirstBatchButton_Click'
            ProductWorkspaceResolvedReferenceClearSelectionButton =
                'ProductWorkspaceResolvedReferenceClearSelectionButton_Click'
        }.GetEnumerator()) {
        $node = Get-XamlNodeByAutomationId $document $entry.Key
        Assert-Condition (
            $node.GetAttribute('IsEnabled') -eq 'False' -and
            $node.GetAttribute('Click') -eq $entry.Value -and
            -not [string]::IsNullOrWhiteSpace(
                $node.GetAttribute('AutomationProperties.Name'))
        ) "Batch-add selection control '$($entry.Key)' must be named, keyboard-focusable, disabled by default, and use its audited handler."
    }
    $resolvedReferenceAddButtonNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceResolvedReferenceAddButton'
    Assert-Condition (
        $resolvedReferenceAddButtonNode.GetAttribute('IsEnabled') -eq 'False' -and
        $resolvedReferenceAddButtonNode.GetAttribute('Click') -eq `
            'ProductWorkspaceResolvedReferenceAddButton_Click'
    ) 'Resolved-reference addition must require an explicit valid selection.'
    $resolvedReferenceBatchUndoButtonNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceReferenceBatchAdditionUndoButton'
    Assert-Condition (
        $resolvedReferenceBatchUndoButtonNode.GetAttribute('IsEnabled') -eq 'False' -and
        $resolvedReferenceBatchUndoButtonNode.GetAttribute('Click') -eq `
            'ProductWorkspaceReferenceBatchAdditionUndoButton_Click'
    ) 'Batch-reference addition undo must start disabled and require an explicit click.'
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
        $resolvedReferenceRemovalSelectorNode.GetAttribute('SelectionMode') -eq 'Multiple' -and
        $resolvedReferenceRemovalSelectorNode.GetAttribute('SelectionChanged') -eq `
            'ProductWorkspaceResolvedReferenceRemovalSelector_SelectionChanged'
    ) 'Resolved-reference removal selection must start disabled and use the audited handler.'
    foreach ($entry in @{
            ProductWorkspaceResolvedReferenceSelectContainerBatchButton =
                'ProductWorkspaceResolvedReferenceSelectContainerBatchButton_Click'
            ProductWorkspaceResolvedReferenceRemovalClearSelectionButton =
                'ProductWorkspaceResolvedReferenceRemovalClearSelectionButton_Click'
            ProductWorkspaceSelectedReferenceCreateButton =
                'ProductWorkspaceSelectedReferenceCreateButton_Click'
        }.GetEnumerator()) {
        $node = Get-XamlNodeByAutomationId $document $entry.Key
        Assert-Condition (
            $node.GetAttribute('IsEnabled') -eq 'False' -and
            $node.GetAttribute('Click') -eq $entry.Value -and
            -not [string]::IsNullOrWhiteSpace(
                $node.GetAttribute('AutomationProperties.Name'))
        ) "Batch-removal selection control '$($entry.Key)' must be named, keyboard-focusable, disabled by default, and use its audited handler."
    }
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
    Assert-Condition (
        $desktopHostFeaturePolicyCode -match 'EnabledForProduct' -and
        $desktopHostFeaturePolicyCode -match 'LONGGRID_DISABLE_DESKTOP_HOST' -and
        $desktopHostFeaturePolicyCode -match 'DisabledByEmergencyPolicy' -and
        $desktopHostFeaturePolicyCode -match `
            'string\.Equals\(emergencyDisableValue,\s*"1",\s*StringComparison\.Ordinal\)'
    ) `
        'DesktopHost must default to the product path while preserving exact emergency disable priority.'
    $boxesEnabledToggleNode = Get-XamlNodeByAutomationId `
        $document `
        'BoxesEnabledToggle'
    Assert-Condition (
        $boxesEnabledToggleNode.GetAttribute('AutomationProperties.Name').Length -gt 0 -and
        $boxesEnabledToggleNode.GetAttribute('Toggled') -eq `
            'BoxesEnabledToggle_Toggled'
    ) 'The product boxes switch must remain named and bound to one audited handler.'
    $boxesEnabledStatusNode = Get-XamlNodeByAutomationId `
        $document `
        'BoxesEnabledStatus'
    Assert-Condition (
        $boxesEnabledStatusNode.GetAttribute(
            'AutomationProperties.LiveSetting') -eq 'Polite'
    ) 'Boxes settings changes and failures must be politely announced.'
    Assert-Condition (
        $appCode -match 'ProductBoxesSettingsController' -and
        $appCode -match 'SetUserEnabled' -and
        $codeBehind -match '_suppressBoxesEnabledChange'
    ) 'The product boxes switch must use one persisted controller and suppress programmatic toggles.'
    $thumbnailsEnabledToggleNode = Get-XamlNodeByAutomationId `
        $document `
        'ThumbnailsEnabledToggle'
    Assert-Condition (
        $thumbnailsEnabledToggleNode.GetAttribute(
            'AutomationProperties.Name').Length -gt 0 -and
        $thumbnailsEnabledToggleNode.GetAttribute('Toggled') -eq `
            'ThumbnailsEnabledToggle_Toggled'
    ) 'The thumbnail switch must remain named and bound to one audited handler.'
    $thumbnailsEnabledStatusNode = Get-XamlNodeByAutomationId `
        $document `
        'ThumbnailsEnabledStatus'
    Assert-Condition (
        $thumbnailsEnabledStatusNode.GetAttribute(
            'AutomationProperties.LiveSetting') -eq 'Polite'
    ) 'Thumbnail settings changes and failures must be politely announced.'
    Assert-Condition (
        $desktopInteractionAdmissionCode -match `
            'LONGGRID_ENABLE_DESKTOP_INTERACTION' -and
        $desktopInteractionAdmissionCode -match `
            'LONGGRID_DISABLE_DESKTOP_INTERACTION' -and
        $desktopInteractionAdmissionCode -match `
            'DisabledByDesktopHostSafetyPolicy' -and
        $desktopInteractionAdmissionCode -match `
            'DisabledByInteractionSafetyPolicy' -and
        $desktopInteractionAdmissionCode -match `
            'DisabledByEmergencyPolicy' -and
        $desktopInteractionAdmissionCode -match `
            'MaximumIntentLifetime' -and
        $desktopInteractionAdmissionCode -match 'WorkspaceRevision' -and
        $desktopInteractionAdmissionCode -match 'TopologyGeneration' -and
        $desktopInteractionAdmissionCode -match `
            'WindowRegistryGeneration' -and
        $desktopInteractionAdmissionCode -match `
            'ReadOnlyAccessibilityAttested' -and
        $desktopInteractionAdmissionCode -match `
            'PassiveWindowContractAttested' -and
        $desktopInteractionAdmissionCode -match 'TargetLocked' -and
        -not ($appCode -match `
            'ProductDesktopInteractionAdmissionController|LONGGRID_ENABLE_DESKTOP_INTERACTION') -and
        -not ($desktopHostInputControllerCode -match `
            'ProductDesktopInteractionAdmissionController')
    ) `
        'Desktop interaction must require separate exact opt-ins, honor the emergency override, bind finite intent to current attestations, and remain blocked from native input wiring.'
    Assert-Condition (
        $desktopInteractionHitTestCode -match `
            'ProductDesktopHostSurfaceLayout\s*\r?\n\s*\.GetContainerBounds' -and
        $desktopInteractionHitTestCode -match 'AmbiguousTarget' -and
        $desktopInteractionHitTestCode -match 'OutsideSurface' -and
        $desktopInteractionHitTestCode -match `
            'MaximumIntentLifetime' -and
        $desktopInteractionHitTestCode -match 'DateTimeOffset\.MaxValue' -and
        $desktopInteractionCancellationCode -match 'EscapePressed' -and
        $desktopInteractionCancellationCode -match 'FocusLost' -and
        $desktopInteractionCancellationCode -match `
            'DesktopRevealRequested' -and
        $desktopInteractionCancellationCode -match `
            'SessionLockedOrDisconnected' -and
        $desktopInteractionCancellationCode -match 'ExplorerRestarted' -and
        $desktopInteractionCancellationCode -match `
            'ApplicationShutdown' -and
        $desktopInteractionCancellationCode -match `
            'controller\.Revalidate' -and
        $windowsDesktopHostReadOnlySurfaceCode -match `
            'mode == ProductDesktopInteractionSurfaceMode\.Explicit[\s\S]*NativeMethods\.HtClient[\s\S]*NativeMethods\.HtTransparent' -and
        -not ($appCode -match `
            'ProductDesktopInteractionHitTestAdapter|ProductDesktopInteractionIntentFactory|ProductDesktopInteractionCancellationAdapter') -and
        -not ($windowsDesktopHostReadOnlySurfaceCode -match `
            'ProductDesktopInteractionHitTestAdapter|ProductDesktopInteractionIntentFactory|ProductDesktopInteractionCancellationAdapter')
    ) `
        'B2 hit testing must reuse the shared surface layout, reject overlap, issue finite intentions, unify cancellation semantics, preserve Passive HWND transparency, and remain App-blocked.'
    Assert-Condition (
        $desktopInteractionSelectionCode -match `
            'MaximumVisibleItems\s*=\s*256' -and
        $desktopInteractionSelectionCode -match 'LeaseIntentId' -and
        $desktopInteractionSelectionCode -match 'WorkspaceRevision' -and
        $desktopInteractionSelectionCode -match 'TopologyGeneration' -and
        $desktopInteractionSelectionCode -match `
            'WindowRegistryGeneration' -and
        $desktopInteractionSelectionCode -match 'LeaseExpired' -and
        $desktopInteractionSelectionCode -match 'VisibleItemsChanged' -and
        $desktopInteractionSelectionCode -match 'SelectionRevision' -and
        $desktopInteractionSelectionCode -match 'AnchorItemId' -and
        $desktopInteractionSelectionCode -match `
            'ProductDesktopSelectionModifiers\.Control' -and
        $desktopInteractionSelectionCode -match `
            'ProductDesktopSelectionModifiers\.Shift' -and
        $desktopInteractionSelectionAccessibilityCode -match `
            'PassiveReadOnly' -and
        $desktopInteractionSelectionAccessibilityCode -match `
            'SelectionPatternAvailable:\s*false' -and
        $desktopInteractionSelectionAccessibilityCode -match `
            'IsKeyboardFocusable:\s*false' -and
        $desktopInteractionSelectionAccessibilityCode -match `
            'CanSelectMultiple:\s*true' -and
        $desktopInteractionSelectionAccessibilityCode -match `
            'AddToSelection' -and
        $desktopInteractionSelectionAccessibilityCode -match `
            'RemoveFromSelection' -and
        -not ($appCode -match `
            'ProductDesktopInteractionSelectionController|ProductDesktopInteractionSelectionAccessibilityAdapter') -and
        $windowsDesktopHostUiaProviderCode -match 'ISelectionProvider' -and
        $windowsDesktopHostUiaProviderCode -match `
            'ExplicitSelectionAvailable[\s\S]*SelectionPatternIdentifiers\.Pattern\.Id' -and
        $windowsDesktopHostUiaProviderCode -match `
            'GetSelection\(\)' -and
        $windowsDesktopHostUiaProviderCode -match `
            'ISelectionItemProvider' -and
        $windowsDesktopHostUiaProviderCode -match 'IInvokeProvider' -and
        $windowsDesktopHostUiaProviderCode -match `
            'ProductDesktopInteractionSelectionAccessibilityAdapter' -and
        $windowsDesktopHostUiaProviderCode -match 'IsInteractiveItem' -and
        -not ($windowsDesktopHostUiaProviderCode -match `
            'ProductDesktopInteractionSelectionController')
    ) `
        'B3/M2 selection must remain lease and generation bound, keep Passive UIA nonfocusable and pattern-free, and expose Explicit SelectionItem/Invoke through the shared accessibility snapshot without a second selection controller.'
    Assert-Condition (
        $desktopInteractionSurfaceModeCode -match `
            'IProductDesktopInteractionSurfaceModeAdapter' -and
        $desktopInteractionSurfaceModeCode -match 'IsPassiveContract' -and
        $desktopInteractionSurfaceModeCode -match 'IsExplicitContract' -and
        $desktopInteractionSurfaceModeCode -match 'IsHiddenContract' -and
        $desktopInteractionSurfaceModeCode -match 'HitTestTransparent' -and
        $desktopInteractionSurfaceModeCode -match 'IsKeyboardFocusable' -and
        $desktopInteractionSurfaceModeCode -match `
            'SelectionPatternAvailable' -and
        $desktopInteractionSurfaceModeCode -match 'ToolWindow' -and
        $desktopInteractionSurfaceModeCode -match 'NoActivate' -and
        $desktopInteractionSurfaceModeCode -match 'OwnsForeground' -and
        $desktopInteractionSurfaceModeCode -match `
            'WindowRegistryGeneration' -and
        $desktopInteractionSurfaceModeCode -match 'ApplyExplicit' -and
        $desktopInteractionSurfaceModeCode -match 'ApplyPassive' -and
        $desktopInteractionSurfaceModeCode -match 'Restore' -and
        $desktopInteractionSurfaceModeCode -match 'HideFailClosed' -and
        $desktopInteractionSurfaceModeCode -match `
            'ProductDesktopInteractionSelectionController' -and
        $desktopInteractionSurfaceModeCode -match `
            'ProductDesktopInteractionSelectionAccessibilityAdapter' -and
        -not ($appCode -match `
            'ProductDesktopInteractionSurfaceModeTransaction|IProductDesktopInteractionSurfaceModeAdapter') -and
        -not ($windowsDesktopHostReadOnlySurfaceCode -match `
            'ProductDesktopInteractionSurfaceModeTransaction') -and
        $windowsDesktopHostReadOnlySurfaceCode -match `
            'mode == ProductDesktopInteractionSurfaceMode\.Explicit[\s\S]*NativeMethods\.HtClient[\s\S]*NativeMethods\.HtTransparent'
    ) `
        'B4/M1 surface-mode switching must transact Passive/Explicit/Hidden evidence, preserve window policy and registry generation, fail closed through restore/hide, connect B3 semantics, and keep the transaction isolated from App while the formal HWND implements the adapter contract.'
    Assert-Condition (
        $nativeInteractionSurfaceProbeCode -match `
            'IProductDesktopInteractionSurfaceModeAdapter' -and
        $nativeInteractionSurfaceProbeCode -match 'CreateWindowEx' -and
        $nativeInteractionSurfaceProbeCode -match 'SetWindowRgn' -and
        $nativeInteractionSurfaceProbeCode -match 'WmNcHitTest' -and
        $nativeInteractionSurfaceProbeCode -match 'WmMouseActivate' -and
        $nativeInteractionSurfaceProbeCode -match `
            'AutomationElement\.FromHandle' -and
        $nativeInteractionSurfaceProbeCode -match `
            'SelectionPattern\.Pattern' -and
        $nativeInteractionSurfaceProbeCode -match `
            'FailExplicitAfterMutation' -and
        $nativeInteractionSurfaceProbeCode -match 'FailRestore' -and
        $nativeInteractionSurfaceProbeCode -match 'FailHide' -and
        $nativeInteractionSurfaceProbeCode -match `
            'RepeatedResourcePlateau' -and
        $nativeInteractionSurfaceProbeCode -match `
            'SyntheticInputUsed:\s*false' -and
        $nativeInteractionSurfaceProbeCode -match `
            'DesktopFilesReadOrChanged:\s*false' -and
        $nativeInteractionSurfaceProbeCode -match `
            'ExplorerWindowInspected:\s*false' -and
        -not ($appCode -match `
            'NativeInteractionSurfaceModeProbe|NativeInteractionSurfaceAdapter') -and
        -not ($windowsDesktopHostReadOnlySurfaceCode -match `
            'NativeInteractionSurfaceModeProbe|NativeInteractionSurfaceAdapter') -and
        $windowsDesktopHostReadOnlySurfaceCode -match `
            'mode == ProductDesktopInteractionSurfaceMode\.Explicit[\s\S]*NativeMethods\.HtClient[\s\S]*NativeMethods\.HtTransparent'
    ) `
        'B5 must keep its historical validation probe-owned and isolated while M1 independently carries the verified Region/message/UIA contract into the production-owned HWND.'
    Assert-Condition (
        $desktopInteractionDevelopmentControllerCode -match `
            'ProductDesktopInteractionDevelopmentStatus' -and
        $desktopInteractionDevelopmentControllerCode -match `
            'SuspendedFailClosed' -and
        $desktopInteractionDevelopmentControllerCode -match `
            'EmergencyDisabled' -and
        $desktopInteractionDevelopmentControllerCode -match `
            'SuspendFailClosed' -and
        $desktopInteractionDevelopmentControllerCode -match `
            'TryResumePassive' -and
        $desktopInteractionDevelopmentControllerCode -match `
            'EmergencyDisable' -and
        $desktopInteractionDevelopmentControllerCode -match `
            'Complete' -and
        $desktopInteractionDevelopmentControllerCode -match `
            'AttachPassiveSurface' -and
        $desktopInteractionDevelopmentControllerCode -match `
            'DetachPassiveSurface' -and
        $desktopInteractionDevelopmentControllerCode -match `
            'NativeSurfaceAdapterConnected:\s*surface is not null' -and
        $desktopInteractionDevelopmentControllerCode -match `
            'RealFileOperationsAllowed:\s*false' -and
        -not ($desktopInteractionDevelopmentControllerCode -match `
            'System\.IO|File\.|Directory\.|IFileOperation|MoveFile|DeleteFile') -and
        ([regex]::Matches(
            $appCode,
            'ProductDesktopInteractionDevelopmentController\s+\r?\n?\s*productDesktopInteraction')).Count -eq 1 -and
        $appCode -match `
            'ProductDesktopInteractionFeaturePolicy\.Evaluate' -and
        $appCode -match `
            'EmergencyDisableEnvironmentVariableName' -and
        $appCode -match `
            'productDesktopInteraction\.Complete\(DateTimeOffset\.UtcNow\)' -and
        -not ($appCode -match `
            'ProductDesktopInteractionSurfaceModeTransaction|ProductDesktopInteractionHitTestAdapter|ProductDesktopInteractionIntentFactory') -and
        $appCode -match `
            'productDesktopHostLifecycle\s*=\s*new\(\s*desktopHostFeature,\s*productDesktopInteraction,\s*productDesktopIntentPreparation,\s*productDesktopInputForwarding,\s*productDesktopIntentConsumption,\s*userEnabled:\s*false\)' -and
        $desktopHostLifecycleControllerCode -match `
            'startHidden:\s*controlledSurfaceLifecycle' -and
        $desktopHostLifecycleControllerCode -match `
            'AttachPassiveSurface' -and
        $desktopHostLifecycleControllerCode -match `
            'DetachPassiveSurface' -and
        $desktopHostPassiveSurfaceAdapterCode -match `
            'IProductDesktopInteractionSurfaceModeAdapter' -and
        $desktopHostPassiveSurfaceAdapterCode -match `
            'ApplyExplicit[\s\S]*surface\.ApplyExplicit\(\)' -and
        $desktopHostPassiveSurfaceAdapterCode -match `
            'expectedWindowRegistryGeneration != registryGeneration' -and
        $desktopHostPassiveSurfaceAdapterCode -match `
            'surface\.ApplyPassive\(\)' -and
        $desktopHostPassiveSurfaceAdapterCode -match `
            'surface\.ApplyHidden\(\)' -and
        -not ($desktopHostPassiveSurfaceAdapterCode -match `
            'System\.IO|File\.|Directory\.|IFileOperation|MoveFile|DeleteFile|SetForegroundWindow') -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'startHidden' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'ApplyEmptyWindowRegion' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'GetWindowRgn' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'IsWindowVisible' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'SwHide' -and
        $windowsDesktopHostReadOnlySurfaceCode -match `
            'mode == ProductDesktopInteractionSurfaceMode\.Explicit[\s\S]*NativeMethods\.HtClient[\s\S]*NativeMethods\.HtTransparent'
    ) `
        'B6b/M1 must create product HWNDs behind the persisted user switch and interaction safety policy, attach only after registry evidence, publish verified Passive, admit only generation-bound Explicit surface changes, hide before detach/shutdown, reject stale generations, forbid file operations, and keep Passive WM_NCHITTEST transparent.'
    Assert-Condition (
        $desktopInteractionSystemSurfaceEventCode -match `
            'ProductDesktopInteractionSystemSurfaceEventKind' -and
        $desktopInteractionSystemSurfaceEventCode -match `
            'RecoveryCandidate' -and
        $desktopInteractionSystemSurfaceEventCode -match `
            'ToCancellationSignal' -and
        $desktopSystemSurfaceEventSourceCode -match `
            'GetShellWindow' -and
        $desktopSystemSurfaceEventSourceCode -match `
            'GetForegroundWindow' -and
        $desktopSystemSurfaceEventSourceCode -match `
            'SHQueryUserNotificationState' -and
        $desktopSystemSurfaceEventSourceCode -match `
            'SessionSwitch' -and
        $desktopSystemSurfaceEventSourceCode -match `
            'PowerModeChanged' -and
        $desktopSystemSurfaceEventSourceCode -match `
            'StableSamplesRequired = 2' -and
        $desktopSystemSurfaceEventSourceCode -match `
            'SystemEvents\.SessionSwitch -=' -and
        $desktopHostLifecycleControllerCode -match `
            'ApplySystemSurfaceEvent' -and
        $desktopHostLifecycleControllerCode -match `
            'lastSystemSurfaceSequence' -and
        $desktopHostLifecycleControllerCode -match `
            'SuspendedSystemSurface' -and
        $appCode -match `
            'productDesktopSystemSurfaceEvents\.Start\(\)' -and
        $appCode -match `
            'productDesktopSystemSurfaceEvents\.Dispose\(\)' -and
        -not ($desktopSystemSurfaceEventSourceCode -match `
            'SetWindowsHookEx|SendInput|SetForegroundWindow|WorkerW|Progman|System\.IO') -and
        -not ($appCode -match `
            'ProductDesktopInteractionSurfaceModeTransaction|ProductDesktopInteractionHitTestAdapter|ProductDesktopInteractionIntentFactory')
    ) `
        'B6c1 must convert only finite public system observations into monotonic fail-closed Hidden/Passive lifecycle events, require stable recovery, release subscriptions, and keep Explicit/input/file operations disconnected.'
    Assert-Condition (
        $desktopInteractionIntentBridgePolicyCode -match `
            'LONGGRID_ENABLE_DESKTOP_INTENT_BRIDGE' -and
        $desktopInteractionIntentBridgePolicyCode -match `
            'LONGGRID_ACKNOWLEDGE_DESKTOP_INTENT_SESSION' -and
        $desktopInteractionIntentBridgePolicyCode -match `
            'StringComparison\.Ordinal' -and
        $desktopIntentPreparationBridgeCode -match `
            'MaximumUserActionAge\s*=\s*TimeSpan\.FromSeconds\(1\)' -and
        $desktopIntentPreparationBridgeCode -match `
            'ExplicitUserActionConfirmed' -and
        $desktopIntentPreparationBridgeCode -match `
            'ReplayedUserAction' -and
        $desktopIntentPreparationBridgeCode -match `
            'ProductDesktopInteractionHitTestAdapter\.HitTest' -and
        $desktopIntentPreparationBridgeCode -match `
            'ProductDesktopInteractionIntentFactory\.Create' -and
        $desktopIntentPreparationBridgeCode -match `
            'ExplicitInteractionEntered:\s*false' -and
        $desktopIntentPreparationBridgeCode -match `
            'RealFileOperationsAllowed:\s*false' -and
        $desktopHostLifecycleControllerCode -match `
            'PrepareInteractionIntent' -and
        $desktopHostLifecycleControllerCode -match `
            'intentPreparation\?\.Invalidate' -and
        $desktopHostLifecycleControllerCode -match `
            'intentPreparation\?\.Complete' -and
        $appCode -match `
            'ProductDesktopInteractionIntentBridgePolicy\.Evaluate' -and
        $appCode -match `
            'ProductDesktopInteractionIntentPreparationBridge' -and
        -not ($appCode -match `
            'PrepareInteractionIntent|ProductDesktopInteractionIntentFactory|ProductDesktopInteractionHitTestAdapter|TryEnterExplicitInteraction') -and
        $desktopIntentSessionLauncherCode -match `
            'AcknowledgeNoExplicitInteraction' -and
        $desktopIntentSessionLauncherCode -match `
            'preparesIntentOnly\s*=\s*\$true' -and
        $desktopIntentSessionLauncherCode -match `
            'entersExplicitInteraction\s*=\s*\$false' -and
        -not ($desktopIntentPreparationBridgeCode -match `
            'ProductDesktopInteractionAdmissionController|ProductDesktopInteractionSurfaceModeTransaction|ApplyExplicit|System\.IO|File\.|Directory\.|IFileOperation') -and
        $desktopHostPassiveSurfaceAdapterCode -match `
            'ApplyExplicit[\s\S]*surface\.ApplyExplicit\(\)' -and
        $windowsDesktopHostReadOnlySurfaceCode -match `
            'mode == ProductDesktopInteractionSurfaceMode\.Explicit[\s\S]*NativeMethods\.HtClient[\s\S]*NativeMethods\.HtTransparent'
    ) `
        'B6c2 must require exact third-stage and manual-session gates, accept only fresh confirmed monotonic unique hits, prepare but never consume intents, invalidate on lifecycle changes, and keep its bridge disconnected from the formal Explicit adapter, input consumption and file operations.'
    Assert-Condition (
        $desktopInteractionInputForwardingPolicyCode -match `
            'LONGGRID_ENABLE_DESKTOP_INPUT_FORWARDING' -and
        $desktopInteractionInputForwardingPolicyCode -match `
            'LONGGRID_ACKNOWLEDGE_DESKTOP_INPUT_FORWARDING_SESSION' -and
        $desktopInteractionInputForwardingPolicyCode -match `
            'StringComparison\.Ordinal' -and
        $desktopInputForwardingAdapterCode -match `
            'RememberedActionCapacity\s*=\s*64' -and
        $desktopInputForwardingAdapterCode -match 'SourceAttested' -and
        $desktopInputForwardingAdapterCode -match 'IsInjected' -and
        $desktopInputForwardingAdapterCode -match 'IsAutoRepeat' -and
        $desktopInputForwardingAdapterCode -match `
            'ProductDesktopInteractionIntentPreparationBridge' -and
        $desktopInputForwardingAdapterCode -match `
            'ExplicitUserActionConfirmed:\s*true' -and
        $desktopInputForwardingAdapterCode -match `
            'CapturesGlobalInput:\s*false' -and
        $desktopInputForwardingAdapterCode -match `
            'SendsSyntheticInput:\s*false' -and
        $desktopInputForwardingAdapterCode -match `
            'ExplicitInteractionEntered:\s*false' -and
        $desktopInputForwardingAdapterCode -match `
            'RealFileOperationsAllowed:\s*false' -and
        $desktopHostLifecycleControllerCode -match 'ForwardInteractionInput' -and
        $desktopHostLifecycleControllerCode -match `
            'inputForwarding\.Invalidate' -and
        $desktopHostLifecycleControllerCode -match `
            'inputForwarding\.Complete' -and
        $appCode -match `
            'ProductDesktopInteractionInputForwardingPolicy\.Evaluate' -and
        $appCode -match `
            'ProductDesktopInteractionInputForwardingAdapter' -and
        -not ($appCode -match 'ForwardInteractionInput') -and
        $desktopInputForwardingSessionLauncherCode -match `
            'AcknowledgeIsolatedSource' -and
        $desktopInputForwardingSessionLauncherCode -match `
            'forwardsNormalizedInputOnly\s*=\s*\$true' -and
        $desktopInputForwardingSessionLauncherCode -match `
            'capturesGlobalInput\s*=\s*\$false' -and
        $desktopInputForwardingSessionLauncherCode -match `
            'entersExplicitInteraction\s*=\s*\$false' -and
        -not ($desktopInputForwardingAdapterCode -match `
            'SetWindowsHookEx|SendInput|GetAsyncKeyState|RegisterRawInputDevices|ProductDesktopInteractionAdmissionController|ProductDesktopInteractionSurfaceModeTransaction|ApplyExplicit|System\.IO|File\.|Directory\.|IFileOperation') -and
        $desktopHostPassiveSurfaceAdapterCode -match `
            'ApplyExplicit[\s\S]*surface\.ApplyExplicit\(\)' -and
        $windowsDesktopHostReadOnlySurfaceCode -match `
            'mode == ProductDesktopInteractionSurfaceMode\.Explicit[\s\S]*NativeMethods\.HtClient[\s\S]*NativeMethods\.HtTransparent'
    ) `
        'B6c3 must require exact fourth-stage and manual-session gates, forward only attested non-injected non-repeat normalized actions once into intent preparation, remain bounded, and keep global capture, synthetic input, formal Explicit consumption and file operations disconnected.'
    Assert-Condition (
        $desktopIntentPreparationBridgeCode -match `
            'internal bool TryConsume' -and
        $desktopIntentPreparationBridgeCode -match `
            'ProductDesktopInteractionIntentPreparationStatus\.Consumed' -and
        $desktopIntentConsumptionControllerCode -match `
            'featureDecision\.IsEnabled && inputForwardingDecision\.IsEnabled' -and
        $desktopIntentConsumptionControllerCode -match `
            'bridge\.TryConsume' -and
        $desktopIntentConsumptionControllerCode -match `
            'ProductDesktopInteractionSurfaceModeTransaction' -and
        $desktopIntentConsumptionControllerCode -match 'ApplySelection' -and
        $desktopIntentConsumptionControllerCode -match `
            'RealFileOperationsAllowed:\s*false' -and
        -not ($desktopIntentConsumptionControllerCode -match `
            'SetWindowsHookEx|SendInput|GetAsyncKeyState|RegisterRawInputDevices|System\.IO|File\.|Directory\.|IFileOperation|MoveFile|DeleteFile') -and
        $desktopHostLifecycleControllerCode -match `
            'ConsumePreparedInteractionIntent' -and
        $desktopHostLifecycleControllerCode -match `
            'TryCreatePassiveInteractionEvidenceUnsafe' -and
        $desktopHostLifecycleControllerCode -match `
            'capture\.Evidence\?\.IsPassiveContract != true' -and
        $desktopHostLifecycleControllerCode -match `
            'intentConsumption\?\.Cancel' -and
        $desktopHostLifecycleControllerCode -match `
            'intentConsumption\?\.DetachSurface' -and
        $desktopHostProjectionBuilderCode -match `
            '\$"item:\{item\.Ordinal\}"' -and
        $appCode -match `
            'ProductDesktopInteractionIntentConsumptionController' -and
        -not ($appCode -match `
            'ForwardInteractionInput|ConsumePreparedInteractionIntent|ApplyInteractionSelection')
    ) `
        'E2a/M2 must atomically consume one prepared intent behind all four gates, require a freshly recaptured Passive surface, carry bounded anonymous item identities into the existing Explicit/selection transaction, cancel on lifecycle loss, expose no file operations, and remain disconnected from a formal HWND input source.'
    Assert-Condition (
        $nativeInputForwardingSourceProbeCode -match `
            'NativeInputForwardingProbeWindow\.Create' -and
        $nativeInputForwardingSourceProbeCode -match `
            'WsExToolWindow' -and
        $nativeInputForwardingSourceProbeCode -match `
            'WsExNoActivate' -and
        $nativeInputForwardingSourceProbeCode -match `
            'WmLeftButtonDown' -and
        $nativeInputForwardingSourceProbeCode -match `
            'WmKeyDown' -and
        $nativeInputForwardingSourceProbeCode -match `
            'InvokePattern\.Pattern' -and
        $nativeInputForwardingSourceProbeCode -match `
            'IInvokeProvider' -and
        $nativeInputForwardingSourceProbeCode -match `
            'ProductDesktopInteractionInputForwardingAdapter' -and
        $nativeInputForwardingSourceProbeCode -match `
            'SyntheticWindowMessagesUsed:\s*true' -and
        $nativeInputForwardingSourceProbeCode -match `
            'SendInputUsed:\s*false' -and
        $nativeInputForwardingSourceProbeCode -match `
            'GlobalHooksInstalled:\s*false' -and
        $nativeInputForwardingSourceProbeCode -match `
            'RawInputRegistered:\s*false' -and
        $nativeInputForwardingSourceProbeCode -match `
            'PhysicalDeviceInputVerified:\s*false' -and
        $nativeInputForwardingSourceProbeCode -match `
            'ExplicitInteractionEntered:\s*false' -and
        $nativeInputForwardingSourceProbeCode -match `
            'DesktopFilesReadOrChanged:\s*false' -and
        $nativeInputForwardingSourceProbeCode -match 'ForegroundStable' -and
        $nativeInputForwardingSourceProbeCode -match 'CleanupPassed' -and
        -not ($nativeInputForwardingSourceProbeCode -match `
            'SendInput\(|SetWindowsHookEx|RegisterRawInputDevices|GetAsyncKeyState|IFileOperation|MoveFile|DeleteFile|ApplyExplicit|TryEnterExplicitInteraction') -and
        -not ($appCode -match 'NativeInputForwardingSourceProbe')
    ) `
        'B6c4 must keep the native input source probe-owned, normalize pointer/key/UIA paths into B6c3, report synthetic-message and physical-input limits, preserve foreground and cleanup, and remain disconnected from product Explicit and files.'
    Assert-Condition (
        $desktopHostProbeProgramCode -match `
            '--native-input-forwarding-session' -and
        $nativeInputForwardingSourceProbeCode -match `
            'RunInteractive' -and
        $nativeInputForwardingSourceProbeCode -match `
            'Environment\.GetEnvironmentVariable' -and
        $nativeInputForwardingSourceProbeCode -match `
            'GetMessage' -and
        $nativeInputForwardingSourceProbeCode -match `
            'WmClose' -and
        $nativeInputForwardingSourceProbeCode -match `
            'VkEscape' -and
        $nativeInputForwardingSourceProbeCode -match `
            'PendingManualEvidence' -and
        $nativeInputForwardingSourceProbeCode -match `
            'PhysicalDeviceInputAutomaticallyVerified:\s*false' -and
        $nativeInputForwardingSourceProbeCode -match `
            'NativeInjectionDetection:\s*false' -and
        $nativeInputForwardingSourceProbeCode -match `
            'DesktopFilesReadOrChanged:\s*false' -and
        $nativeInputForwardingSourceProbeCode -match `
            'ExplicitInteractionEntered:\s*false' -and
        $desktopInputForwardingSessionLauncherCode -match `
            'launchesProbeOwnedNativeSource\s*=\s*\$true' -and
        $desktopInputForwardingSessionLauncherCode -match `
            'startsProductApp\s*=\s*\$false' -and
        $desktopInputForwardingSessionLauncherCode -match `
            'deferredSystemSurfaceScenarios' -and
        $desktopInputForwardingSessionLauncherCode -match `
            'adapterRejectsInjectedAttestation\s*=\s*\$true' -and
        $desktopInputForwardingSessionLauncherCode -match `
            'detectsNativeInjection\s*=\s*\$false' -and
        $desktopInputForwardingSessionLauncherCode -match `
            "B6C3-05.*B6C3-06.*B6C3-07" -and
        $desktopInputForwardingSessionLauncherCode -match `
            '--native-input-forwarding-session' -and
        -not ($desktopInputForwardingSessionLauncherCode -match `
            'Start-LongGrid\.ps1') -and
        -not ($nativeInputForwardingSourceProbeCode -match `
            'SendInput\(|SetWindowsHookEx|RegisterRawInputDevices|GetAsyncKeyState|IFileOperation|MoveFile|DeleteFile|ApplyExplicit|TryEnterExplicitInteraction') -and
        -not ($appCode -match 'NativeInputForwardingProbeWindow')
    ) `
        'B6c5 must launch only an acknowledged probe-owned visible source, accept bounded physical/UIA input until Escape or close, retain PendingManualEvidence, and keep the product App, global input, Explicit and files disconnected.'
    Assert-Condition (
        $desktopHostProbeProgramCode -match `
            '--native-input-system-surface-session' -and
        $nativeInputForwardingSourceProbeCode -match `
            'RunSystemSurfaceInteractive' -and
        $nativeInputForwardingSourceProbeCode -match `
            'WindowsProductDesktopInteractionSystemSurfaceEventSource' -and
        $nativeInputForwardingSourceProbeCode -match `
            'SurfaceChanged' -and
        $nativeInputForwardingSourceProbeCode -match `
            'forwarding\.Invalidate\(\)' -and
        $nativeInputForwardingSourceProbeCode -match `
            'forwarding\.AwaitPassiveSurface\(\)' -and
        $nativeInputForwardingSourceProbeCode -match `
            'PreparedIntentInvalidationCount' -and
        $nativeInputForwardingSourceProbeCode -match `
            'DisplayTopologyGenerationObserved' -and
        $nativeInputForwardingSourceProbeCode -match `
            'systemSurfaceSafe\s*&&\s*displayTopologySafe' -and
        $nativeInputForwardingSourceProbeCode -match `
            '!systemSurfaceSafe\s*\|\|\s*!displayTopologySafe' -and
        $readOnlyDisplayTopologyObserverCode -match `
            'ProductDisplayTopologyReader' -and
        $readOnlyDisplayTopologyObserverCode -match `
            'DisplayTopologyFingerprint\.Compute' -and
        $readOnlyDisplayTopologyObserverCode -match `
            'DisplayTopologyStabilizer' -and
        $readOnlyDisplayTopologyObserverCode -match `
            'DisplayTopologyStabilizationState\.Ready' -and
        $nativeInputForwardingSourceProbeCode -match `
            'SwHide' -and
        $nativeInputForwardingSourceProbeCode -match `
            'SwShowNoActivate' -and
        $desktopSystemSurfaceSessionLauncherCode -match `
            'AcknowledgeSystemStateChange' -and
        $desktopSystemSurfaceSessionLauncherCode -match `
            'AcknowledgeReadOnlyDisplayTopologyObservation' -and
        $desktopSystemSurfaceSessionLauncherCode -match `
            "B6C3-07'" -and
        $desktopSystemSurfaceSessionLauncherCode -match `
            'observesDisplayTopologyGeneration\s*=\s*\$true' -and
        $desktopSystemSurfaceSessionLauncherCode -match `
            'requiresAuthoritativeDisplayTopology\s*=\s*\$true' -and
        $desktopSystemSurfaceSessionLauncherCode -match `
            'requiresStabilizedDisplayTopology\s*=\s*\$true' -and
        $desktopSystemSurfaceSessionLauncherCode -match `
            'changesSystemState\s*=\s*\$false' -and
        $desktopSystemSurfaceSessionLauncherCode -match `
            'finalResultStatus\s*=\s*''PendingManualEvidence''' -and
        -not ($desktopSystemSurfaceSessionLauncherCode -match `
            'Start-LongGrid\.ps1|Stop-Process|tsdiscon|logoff|SetDisplayConfig|ChangeDisplaySettings|Restart-Computer') -and
        -not ($readOnlyDisplayTopologyObserverCode -match `
            'SetDisplayConfig|ChangeDisplaySettings|EnumDisplaySettings|DeviceIoControl') -and
        -not ($nativeInputForwardingSourceProbeCode -match `
            'SendInput\(|SetWindowsHookEx|RegisterRawInputDevices|GetAsyncKeyState|IFileOperation|MoveFile|DeleteFile|ApplyExplicit|TryEnterExplicitInteraction') -and
        -not ($appCode -match 'RunSystemSurfaceInteractive')
    ) `
        'B6c7 must combine public system-surface events with authoritative read-only topology fingerprints, invalidate and hide on unsafe generation, require stabilized joint recovery, retain manual evidence, and keep product Explicit and files disconnected.'
    Assert-Condition (
        $desktopHostProductSessionLauncherCode -match `
            "'PF003D5-01'.*'PF003D5-02'.*'PF003D5-03'.*'PF003D5-04'.*'PF003D5-05'" -and
        $desktopHostProductSessionLauncherCode -match `
            'physicalDeviceInputAutomaticallyVerified\s*=\s*\$false' -and
        $desktopHostProductSessionLauncherCode -match `
            'visibleScreenshotAutomaticallyCaptured\s*=\s*\$false' -and
        $desktopHostProductSessionLauncherCode -match `
            'touchOrPenRequiredOnlyWhenAvailable\s*=\s*\$true' -and
        $desktopHostProductSessionLauncherCode -match `
            'sendsSyntheticInput\s*=\s*\$false' -and
        $desktopHostProductSessionLauncherCode -match `
            'finalResultStatus\s*=\s*''PendingManualEvidence''' -and
        -not ($desktopHostProductSessionLauncherCode -match `
            'SendInput\(|SetDisplayConfig|ChangeDisplaySettings|Start-Process|Stop-Process')
    ) `
        'PF-003D5 manual launcher must admit all five real-device scenarios while sending no input, changing no display state, capturing no screenshot, and retaining PendingManualEvidence.'
    Assert-Condition (
        $desktopHostLifecycleControllerCode -match 'DisabledBySafetyPolicy' -and
        $desktopHostLifecycleControllerCode -match 'AwaitingHost' -and
        $desktopHostLifecycleControllerCode -match 'AwaitingWorkspace' -and
        $desktopHostLifecycleControllerCode -match 'SuspendedUnsafeTopology' -and
        $desktopHostLifecycleControllerCode -match 'ReadyReadOnly' -and
        $desktopHostLifecycleControllerCode -match 'Faulted' -and
        $desktopHostLifecycleControllerCode -match 'Completed' -and
        $desktopHostLifecycleControllerCode -match 'NativeHostConnected' -and
        $desktopHostLifecycleControllerCode -match 'OwnedWindowCount' -and
        $desktopHostLifecycleControllerCode -match 'WorkspaceRevision' -and
        $desktopHostLifecycleControllerCode -match 'TopologyGeneration' -and
        $desktopHostLifecycleControllerCode -match 'RenderedContainerCount' -and
        $desktopHostLifecycleControllerCode -match 'ReadOnlyAccessibilityAvailable' -and
        $desktopHostLifecycleControllerCode -match 'PassiveWindowContractAttested'
    ) `
        'DesktopHost lifecycle must expose the finite anonymous state bridge.'
    Assert-Condition (
        $desktopHostLifecycleControllerCode -match 'ProductDesktopHostWindowBridge' -and
        $desktopHostLifecycleControllerCode -match 'WindowsProductDesktopHostWindowInspector' -and
        $desktopHostLifecycleControllerCode -match 'OwnershipAttested' -and
        $desktopHostLifecycleControllerCode -match 'ApplyProjectionBatch' -and
        $desktopHostLifecycleControllerCode -match 'ApplyProjectionUpdate' -and
        $desktopHostLifecycleControllerCode -match 'lastWorkspaceRevision' -and
        $desktopHostLifecycleControllerCode -match 'lastTopologyGeneration' -and
        $desktopHostLifecycleControllerCode -match 'registrations' -and
        $desktopHostLifecycleControllerCode -match 'surfaces' -and
        $desktopHostLifecycleControllerCode -match 'ReleaseSurfaceUnsafe'
    ) `
        'The A3 lifecycle must create an ownership-attested display batch and release every surface on failure or exit.'
    Assert-Condition (
        $desktopHostProjectionBatchCode -match 'MaximumDisplays\s*=\s*16' -and
        $desktopHostProjectionBatchCode -match 'MaximumContainers' -and
        $desktopHostProjectionBatchCode -match 'TopologyFingerprint' -and
        $desktopHostProjectionBatchCode -match 'TopologyGeneration' -and
        $desktopHostProjectionBatchCode -match 'WorkspaceRevision' -and
        $desktopHostProjectionBuilderCode -match 'topology\.IsAuthoritative' -and
        $desktopHostProjectionBuilderCode -match 'DisplayTopologyFingerprint\.Compute' -and
        $desktopHostProjectionBuilderCode -match 'display\.IsPrimary' -and
        $desktopHostProjectionBuilderCode -match 'source\.Placement\.DisplayKey'
    ) `
        'The A3 projection must be bounded, generation-bound, authoritative-topology-only, and use a deterministic primary fallback.'
    Assert-Condition (
        $desktopHostProjectionUpdateCode -match 'TopologyRefreshing' -and
        $desktopHostProjectionUpdateCode -match 'TopologyUnavailable' -and
        $desktopHostProjectionUpdateCode -match 'EmptyWorkspace' -and
        $desktopHostProjectionUpdateCode -match 'Invalid' -and
        $desktopHostProjectionBuilderCode -match 'BuildUpdate' -and
        $desktopHostLifecycleControllerCode -match 'UpdatesEqual' -and
        $desktopHostLifecycleControllerCode -match 'update\.WorkspaceRevision\s*<' -and
        $desktopHostLifecycleControllerCode -match 'update\.TopologyGeneration\s*<'
    ) `
        'The A4 projection boundary must distinguish unsafe states and reject stale generations.'
    Assert-Condition (
        $windowsDesktopHostReadOnlySurfaceCode -match 'WsExToolWindow' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'WsExNoActivate' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'WsExLayered' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'WsExTransparent' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'SwShowNoActivate' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'HtTransparent' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'InstanceMarkerProperty' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'SetWindowRgn' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'projection\.Containers' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'projection\.WorkArea' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'projection\.EffectiveDpi' -and
        -not ($windowsDesktopHostReadOnlySurfaceCode -match 'SystemParametersInfo') -and
        -not ($windowsDesktopHostReadOnlySurfaceCode -match `
            'Progman|WorkerW|SetForegroundWindow|RegisterDragDrop|IFileOperation')
    ) `
        'The A3 per-display surface must remain bounded, DPI-aware, no-activate, click-through, product-owned, and Explorer-independent.'
    Assert-Condition (
        $windowsDesktopHostReadOnlySurfaceCode -match 'WmGetObject' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'ReturnRawElementProvider' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'AttestStableWindowPolicy' -and
        $windowsDesktopHostReadOnlySurfaceCode -match `
            'PassiveWindowContractAttested' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'WsExTopmost' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'GetForegroundWindow' -and
        $windowsDesktopHostUiaProviderCode -match 'IRawElementProviderFragmentRoot' -and
        $windowsDesktopHostUiaProviderCode -match 'ControlType\.Group' -and
        $windowsDesktopHostUiaProviderCode -match 'ControlType\.Text' -and
        $windowsDesktopHostUiaProviderCode -match 'IsKeyboardFocusableProperty' -and
        $windowsDesktopHostUiaProviderCode -match `
            'ExplicitSelectionAvailable[\s\S]*SelectionPatternIdentifiers\.Pattern\.Id' -and
        $windowsDesktopHostUiaProviderCode -match `
            'ISelectionItemProvider' -and
        $windowsDesktopHostUiaProviderCode -match 'IInvokeProvider' -and
        $windowsDesktopHostUiaProviderCode -match 'IsInteractiveItem'
    ) `
        'The A5/M2 product surface must expose bounded UIA Fragments, keep Passive item patterns unavailable, gate root/item selection to Explicit, and attest non-topmost behavior.'
    Assert-Condition (
        $desktopItemOpenCode -match 'KeyboardEnter' -and
        $desktopItemOpenCode -match 'PointerDoubleClick' -and
        $desktopItemOpenCode -match 'AssistiveInvoke' -and
        $desktopItemOpenCode -match 'ShellExecuteEx' -and
        $desktopItemOpenCode -match 'ReparsePointRejected' -and
        $desktopItemOpenCode -match 'File\.GetAttributes' -and
        $desktopHostLifecycleControllerCode -match 'BindItemOpen' -and
        $desktopInteractionActivationSourceCode -match 'KeyboardEnter' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'PointerDoubleClick' -and
        $windowsDesktopHostUiaProviderCode -match 'requestItemOpen' -and
        $windowsDesktopHostUiaProviderCode -match 'InvokeItem'
    ) `
        'PF-006B1 must converge Enter, item double-click, and UIA Invoke on one authority-safe File/Folder ShellExecuteEx boundary while fail-closing reparse targets.'
    Assert-Condition (
        $desktopItemOpenReferenceResolverCode -match 'IShellLinkW' -and
        $desktopItemOpenReferenceResolverCode -match 'IPersistFile' -and
        $desktopItemOpenReferenceResolverCode -match `
            'MaximumShortcutBytes\s*=\s*1024\s*\*\s*1024' -and
        $desktopItemOpenReferenceResolverCode -match `
            'MaximumInternetShortcutBytes\s*=\s*64\s*\*\s*1024' -and
        $desktopItemOpenReferenceResolverCode -match 'UTF8Encoding' -and
        $desktopItemOpenReferenceResolverCode -match 'Encoding\.Unicode' -and
        $desktopItemOpenReferenceResolverCode -match 'UriSchemeHttp' -and
        $desktopItemOpenReferenceResolverCode -match 'UriSchemeHttps' -and
        $desktopItemOpenReferenceResolverCode -match 'ProtocolRejected' -and
        $desktopItemOpenReferenceResolverCode -match 'SHA256\.HashData' -and
        $desktopItemOpenReferenceResolverCode -match `
            'CryptographicOperations\.FixedTimeEquals' -and
        $desktopItemOpenReferenceResolverCode -match 'FileAttributes\.ReparsePoint' -and
        $desktopItemOpenCode -match 'UserMessage' -and
        $desktopHostLifecycleControllerCode -match `
            'PublishItemOpenFeedbackUnsafe' -and
        $windowsDesktopHostReadOnlySurfaceCode -match `
            'ApplyItemOpenFeedback' -and
        $windowsDesktopHostUiaProviderCode -match `
            'PublishItemOpenFeedback'
    ) `
        'PF-006B2A must bound real Shell Link and InternetShortcut parsing, allow only HTTP/HTTPS URLs, recheck references, and publish finite path-free HWND/UIA feedback.'
    Assert-Condition (
        $desktopContainerHeaderPresentationCode -match 'VisualTitle' -and
        $desktopContainerHeaderPresentationCode -match 'VisualStatus' -and
        $desktopContainerHeaderPresentationCode -match 'AccessibilityName' -and
        $desktopContainerHeaderPresentationCode -match 'AccessibilityStatus' -and
        $desktopContainerHeaderPresentationCode -match 'SafeReferences' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'header\.VisualTitle' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'header\.VisualStatus' -and
        $windowsDesktopHostUiaProviderCode -match `
            'projection\.Header\.AccessibilityName' -and
        $windowsDesktopHostUiaProviderCode -match `
            'projection\.Header\.AccessibilityStatus'
    ) `
        'PF-004A must render one bounded title/status contract and expose the same state to UIA.'
    Assert-Condition (
        $desktopHostProjectionBuilderCode -match `
            'ProductDesktopItemVisualPresentation\.Create' -and
        $desktopItemVisualPresentationCode -match `
            'ReadyTypeIcon[\s\S]*LoadingThumbnail[\s\S]*ReadyThumbnail' -and
        $desktopItemVisualPresentationCode -match `
            'Offline[\s\S]*TargetChanged[\s\S]*Ambiguous[\s\S]*Unsupported[\s\S]*AccessDenied[\s\S]*FailedFallback' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'SHGetStockIconInfo' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'DrawIconEx' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'DestroyIcon' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'StockIconWarning' -and
        $windowsDesktopHostUiaProviderCode -match `
            'ItemVisuals\[itemIndex\]\.AccessibilityName\(name\)'
    ) `
        'PF-005A must project finite item visual states, draw released Windows Shell stock icons, and expose only privacy-safe UIA names.'
    Assert-Condition (
        $desktopThumbnailRequestControllerCode -match `
            'MaximumVisibleRequests\s*=\s*12' -and
        $desktopThumbnailRequestControllerCode -match `
            'MaximumCacheEntries\s*=\s*64' -and
        $desktopThumbnailRequestControllerCode -match `
            'TimeSpan\.FromMilliseconds\(1500\)' -and
        $desktopThumbnailRequestControllerCode -match `
            'if \(!enabled\)[\s\S]{0,300}StopRuntime\(\)' -and
        $desktopThumbnailRequestControllerCode -match `
            'file\.LastWriteTimeUtc\.Ticks' -and
        $desktopThumbnailRequestControllerCode -match `
            'SHA256\.HashData' -and
        $desktopThumbnailRequestControllerCode -match `
            'string SafeIdentity' -and
        $desktopThumbnailRequestControllerCode -match `
            'file\.Attributes & FileAttributes\.ReparsePoint' -and
        $desktopThumbnailRequestControllerCode -match `
            'ReadyThumbnail' -and
        $desktopThumbnailRequestControllerCode -match `
            'FailedFallback' -and
        $desktopThumbnailRequestControllerCode -match `
            'IsZeroCapabilityAppContainer' -and
        $desktopThumbnailRequestControllerCode -match `
            'UsesKillOnJobClose'
    ) `
        'PF-005B1/PF-005C must keep thumbnail work lazy, 12-request bounded, version/theme cached, 1500 ms limited, isolation-attested, and finite-fallback safe.'
    Assert-Condition (
        $boxesSettingsCode -match `
            'JsonPropertyName\("thumbnailsEnabled"\)' -and
        $codeBehind -match 'ThumbnailsEnabledChangeRequested' -and
        $appCode -match 'ChangeThumbnailsAsync' -and
        $appCode -match `
            'ProductDesktopThumbnailCandidateBuilder\.Build' -and
        $appCode -match `
            'ProductDesktopThumbnailRefreshAdmission\.CanPublish' -and
        $appCode -match 'desktopThumbnailRefreshGeneration' -and
        $desktopHostProjectionBuilderCode -match 'LoadingThumbnail' -and
        $desktopHostProjectionBuilderCode -match `
            'Status = ProductDesktopItemVisualStatus\.ReadyThumbnail' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'StretchDIBits' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'SourceCopy' -and
        $windowsDesktopHostReadOnlySurfaceCode -match `
            'ProductDesktopThumbnailFrame'
    ) `
        'PF-005B2 must persist its switch, derive authoritative candidates, reject stale facts, project finite states, and draw bounded BGRA pixels on the HWND.'
    Assert-Condition (
        $desktopHostProjectionBatchCode -match 'PresentationGeneration' -and
        $desktopHostLifecycleControllerCode -match `
            'ApplyPresentationUpdateUnsafe' -and
        $desktopHostLifecycleControllerCode -match `
            'PresentationStructuresEqual' -and
        $desktopItemViewportCode -match `
            'ProductDesktopItemViewportPolicy' -and
        $desktopItemViewportCode -match `
            'MaximumVisibleItems' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'WmMouseWheel' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'BindItemViewport' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'ApplyPresentation' -and
        $appCode -match 'desktopItemViewportStarts' -and
        $appCode -match 'RequestDesktopItemViewport' -and
        $appCode -match 'BindItemViewport'
    ) `
        'PF-005C must sequence presentation-only updates in place and page 13-500 items through an authority-stamped 12-item viewport.'
    Assert-Condition (
        $desktopContainerHeaderCommandCode -match 'ToggleCollapsed' -and
        $desktopContainerHeaderCommandCode -match 'ToggleLocked' -and
        $desktopContainerHeaderCommandCode -match 'ExpectedWorkspaceRevision' -and
        $desktopContainerHeaderCommandCode -match 'ExpectedTopologyGeneration' -and
        $desktopInteractionActivationSourceCode -match `
            'ActivationButtonSizeDip\s*=\s*32' -and
        $desktopInteractionActivationSourceCode -match `
            'BindContainerHeaderCommand' -and
        $desktopInteractionActivationSourceCode -match `
            'IsInjected[\s\S]*IsAutoRepeat' -and
        $desktopHostLifecycleControllerCode -match `
            'BindContainerHeaderCommand' -and
        $desktopContainerHeaderCommandControllerCode -match `
            'ProductWorkspaceCommitCoordinator' -and
        $desktopContainerHeaderCommandControllerCode -match `
            'ProductWorkspaceSaveStatus\.Failed' -and
        $desktopContainerHeaderCommandControllerCode -match `
            'Compensated' -and
        $appCode -match 'ProductDesktopContainerHeaderCommandController' -and
        $appCode -match 'ObserveSave'
    ) `
        'PF-004B must expose finite 32 DIP header commands, stamp source/revision/topology facts, use the formal commit/save chain, and compensate failed persistence.'
    Assert-Condition (
        $desktopContainerMenuCode -match 'OpenRename' -and
        $desktopContainerMenuCode -match 'OpenAppearance' -and
        $desktopContainerMenuCode -match 'OpenSort' -and
        $desktopContainerMenuCode -match 'ExpectedWorkspaceRevision' -and
        $desktopContainerMenuCode -match 'ExpectedTopologyGeneration' -and
        $desktopContainerMenuNavigationCode -match `
            'ProductWorkspaceSaveStatus\.Failed' -and
        $desktopContainerMenuNavigationCode -match 'IsInjected' -and
        $desktopContainerMenuNavigationCode -match 'IsAutoRepeat' -and
        $desktopInteractionActivationSourceCode -match 'CreatePopupMenu' -and
        $desktopInteractionActivationSourceCode -match 'TrackPopupMenuEx' -and
        $desktopInteractionActivationSourceCode -match `
            '创建规则（后续功能）' -and
        $desktopHostLifecycleControllerCode -match 'BindContainerMenu' -and
        $appCode -match 'RequestDesktopContainerMenuNavigation' -and
        $codeBehind -match 'OpenProductWorkspaceContainerMenuTarget' -and
        $codeBehind -match 'Changed=False:DesktopFilesChanged=False'
    ) `
        'PF-004C must expose a finite native menu, pre-disable unsafe/future actions, stamp source and generation facts, and navigate the unique control center without writing configuration.'
    Assert-Condition (
        $desktopContainerMenuCode -match `
            'DeleteContainerConfiguration' -and
        $desktopContainerMenuCode -match `
            'CanDeleteContainerConfiguration' -and
        $desktopInteractionActivationSourceCode -match `
            'MenuDeleteCommand' -and
        $desktopInteractionActivationSourceCode -match `
            '删除方格配置…' -and
        $desktopContainerDeleteControllerCode -match `
            'CommitContainerRemovalUndo' -and
        $desktopContainerDeleteControllerCode -match `
            'ProductWorkspaceSaveStatus\.Failed' -and
        $desktopContainerDeleteControllerCode -match 'Compensated' -and
        $codeBehind -match `
            'DesktopContainerDeleteConfirmationDialog' -and
        $codeBehind -match `
            'DefaultButton\s*=\s*ContentDialogButton\.Close' -and
        $codeBehind -match `
            '真实桌面文件不会被删除、移动或重命名' -and
        $appCode -match `
            'HandleDesktopContainerMenuRequestAsync' -and
        $appCode -match `
            'ProductDesktopContainerMenuNavigationController\.Handle' -and
        $appCode -match `
            'desktopContainerDeletes\.CommitConfirmed' -and
        $latestUndoPresentationCode -match `
            'ProductWorkspaceLatestUndoKind\.ContainerRemoval'
    ) `
        'PF-004D must bind delete confirmation to current facts, default to cancel, preserve desktop files, compensate failed persistence, and expose unified undo.'
    Assert-Condition (
        ([regex]::Matches(
            $appCode,
            'ProductDesktopHostLifecycleController\s+productDesktopHostLifecycle')).Count -eq 1
    ) `
        'The App composition root must own exactly one DesktopHost lifecycle controller field.'
    Assert-Condition (
        $appCode -match 'ProductDesktopHostFeaturePolicy\.Evaluate' -and
        $appCode -match 'Environment\.GetEnvironmentVariable' -and
        $appCode -match 'ApplyProductDesktopHostLifecycleState' -and
        $appCode -match 'ApplyProjectionUpdate' -and
        $appCode -match 'ProductDesktopHostProjectionBuilder\.BuildUpdate' -and
        $appCode -match 'productDesktopHostLifecycle\.DisposeAsync'
    ) `
        'The App must evaluate, present, and dispose the DesktopHost lifecycle boundary.'
    Assert-Condition (
        $codeBehind -match 'ApplyProductDesktopHostLifecycleState' -and
        $codeBehind -match 'desktopHostFeatureEnabled:\s*_desktopHostFeatureEnabled' -and
        $codeBehind -match 'DesktopHostValue\.Text\s*=\s*snapshot\.DesktopHost\s+switch' -and
        $codeBehind -match 'RuntimeCapabilityState\.DisabledBySafetyPolicy' -and
        $codeBehind -match 'RuntimeCapabilityState\.Disconnected' -and
        $codeBehind -match 'RuntimeCapabilityState\.ConnectedReadOnly'
    ) `
        'The control center must distinguish default-off from enabled-but-awaiting-host state.'
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
    Assert-Condition (
        $codeBehind -match 'AnonymousEvidenceCaptureCommitted:Anonymous=True:' -and
        $codeBehind -match 'ContinuousLogging=False'
    ) 'Interaction evidence capture must disclose anonymity and reject continuous logging.'
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
    Assert-Condition (
        $codeBehind -match 'DesktopKeyboardInteractionRequested\?\.Invoke' -and
        $appCode -match 'productDesktopHostLifecycle\.RequestKeyboardInteraction\(\)'
    ) 'The App keyboard command must call only the lifecycle-owned no-handle capability.'

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
        $workspaceReadModelCode -match 'ProductWorkspaceContainerHealth' -and
        $workspaceReadModelCode -match 'unresolved\s*>\s*0' -and
        $workspaceReadModelCode -match 'items\.Count\s*==\s*0' -and
        $workspaceReadPresentationCode -match 'EmptyContainers=' -and
        $workspaceReadPresentationCode -match 'NeedsReviewContainers=' -and
        $document.OuterXml.Contains('Text="{Binding Health}"')
    ) 'Formal containers must expose finite empty, ready, and review health states.'
    $workspaceHealthFilterNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceHealthFilterSelector'
    Assert-Condition (
        $workspaceHealthFilterNode.GetAttribute('IsEnabled') -eq 'False' -and
        $workspaceHealthFilterNode.GetAttribute('SelectedIndex') -eq '0' -and
        $workspaceHealthFilterNode.GetAttribute('SelectionChanged') -eq `
            'ProductWorkspaceHealthFilterSelector_SelectionChanged' -and
        $workspaceHealthFilterNode.ChildNodes.Count -eq 4
    ) 'Workspace health filter must start disabled, select All, and expose four finite choices.'
    $workspaceSearchNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceSearchBox'
    Assert-Condition (
        $workspaceSearchNode.GetAttribute('IsEnabled') -eq 'False' -and
        $workspaceSearchNode.GetAttribute('MaxLength') -eq '64' -and
        $workspaceSearchNode.GetAttribute('TextChanged') -eq `
            'ProductWorkspaceSearchBox_TextChanged' -and
        -not [string]::IsNullOrWhiteSpace(
            $workspaceSearchNode.GetAttribute('AutomationProperties.Name'))
    ) 'Workspace visible search must start disabled and expose a bounded accessible text field.'
    Assert-Condition (
        $workspaceVisibleSearchPolicyCode -match 'MaximumQueryLength\s*=\s*64' -and
        $workspaceVisibleSearchPolicyCode -match 'query\.Any\(char\.IsControl\)' -and
        $workspaceVisibleSearchPolicyCode -match 'StringComparison\.OrdinalIgnoreCase' -and
        $workspaceVisibleSearchPolicyCode -match 'ContainerDisplayName' -and
        $workspaceVisibleSearchPolicyCode -match 'HealthLabel' -and
        $workspaceVisibleSearchPolicyCode -match 'VisibleItemDisplayNames' -and
        $workspaceReadPresentationCode -match 'ProductWorkspaceVisibleSearchPolicy\.Resolve' -and
        $workspaceReadPresentationCode -match 'container\.DisplayName' -and
        $workspaceReadPresentationCode -match 'container\.Health' -and
        $workspaceReadPresentationCode -match 'container\.Items\.Select\(item\s*=>\s*item\.DisplayName\)' -and
        $workspaceReadPresentationCode -match 'Search=Invalid' -and
        $workspaceReadPresentationCode -match 'Search=\{search\.Status\}' -and
        $codeBehind -match 'ApplyFilter\(\s*filter,\s*ProductWorkspaceSearchBox\.Text,' -and
        $codeBehind -match 'ProductWorkspaceSearchBox\.IsEnabled\s*=\s*presentation\.CanFilter' -and
        $codeBehind -match 'ProductWorkspaceSearchBox\.Text\s*=\s*string\.Empty'
    ) 'Workspace search must intersect presentation-only visible names, reject unsafe input, and avoid query telemetry.'
    Assert-Condition (
        -not ($workspaceReadPresentationCode -match `
            'VisibleSearchInput\([^\)]*(Detail|Appearance|MachineStatus|PersistedTarget|CanonicalTarget)') -and
        -not ($workspaceReadPresentationCode -match `
            'Search=\{query\}|SearchQuery|Query=\{')
    ) 'Workspace search must not ingest hidden identity/detail fields or echo user queries.'
    $workspaceSortNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceSortSelector'
    Assert-Condition (
        $workspaceSortNode.GetAttribute('IsEnabled') -eq 'False' -and
        $workspaceSortNode.GetAttribute('SelectedIndex') -eq '0' -and
        $workspaceSortNode.GetAttribute('SelectionChanged') -eq `
            'ProductWorkspaceSortSelector_SelectionChanged' -and
        $workspaceSortNode.GetAttribute('HorizontalAlignment') -eq 'Stretch' -and
        $workspaceSortNode.ChildNodes.Count -eq 4
    ) 'Workspace sort must start disabled, preserve configuration order, and expose four finite choices.'
    Assert-Condition (
        $workspaceContainerSortPolicyCode -match 'ConfigurationOrder' -and
        $workspaceContainerSortPolicyCode -match 'NameAscending' -and
        $workspaceContainerSortPolicyCode -match 'NameDescending' -and
        $workspaceContainerSortPolicyCode -match 'NeedsReviewFirst' -and
        $workspaceContainerSortPolicyCode -match 'StringComparer\.OrdinalIgnoreCase' -and
        $workspaceContainerSortPolicyCode -match 'ThenBy\(entry\s*=>\s*entry\.Index\)' -and
        $workspaceContainerSortPolicyCode -match 'Array\.Empty<int>\(\)' -and
        $workspaceReadPresentationCode -match 'ProductWorkspaceContainerSortPolicy\.Resolve' -and
        $workspaceReadPresentationCode -match 'container\.DisplayName' -and
        $workspaceReadPresentationCode -match 'container\.HealthKind' -and
        $workspaceReadPresentationCode -match 'WorkspaceViewSortUnavailable' -and
        $workspaceReadPresentationCode -match 'Sort=\{sort\}' -and
        $codeBehind -match 'ProductWorkspaceSortSelector\.IsEnabled\s*=\s*presentation\.CanFilter' -and
        $codeBehind -match 'ProductWorkspaceSortSelector\.SelectedIndex\s+switch'
    ) 'Workspace sort must be stable, finite, presentation-only, and fail closed.'
    Assert-Condition (
        -not ($workspaceReadPresentationCode -match `
            'ContainerSortInput\([^\)]*(Detail|Appearance|MachineStatus|PersistedTarget|CanonicalTarget)') -and
        -not ($workspaceContainerSortPolicyCode -match `
            'DateTime|LastUsed|Click|Telemetry|Catalog|PersistedTarget|CanonicalTarget')
    ) 'Workspace sort must not infer recency or ingest hidden identity, telemetry, or detail fields.'
    $workspaceResetViewNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceResetViewButton'
    Assert-Condition (
        $workspaceResetViewNode.GetAttribute('IsEnabled') -eq 'False' -and
        $workspaceResetViewNode.GetAttribute('Click') -eq `
            'ProductWorkspaceResetViewButton_Click' -and
        $workspaceResetViewNode.GetAttribute('AutomationProperties.ItemStatus') -eq `
            'WorkspaceViewResetUnavailable:DesktopFilesChanged=False' -and
        $workspaceResetViewNode.ParentNode.ParentNode.GetAttribute('Visibility') -eq `
            'Collapsed'
    ) 'Workspace zero-result recovery must start collapsed, disabled, finite, and explicit-click only.'
    Assert-Condition (
        $workspaceViewResetPolicyCode -match 'totalContainerCount\s*>\s*0' -and
        $workspaceViewResetPolicyCode -match 'visibleContainerCount\s*==\s*0' -and
        $workspaceViewResetPolicyCode -match 'hasNonDefaultCriteria' -and
        $workspaceViewResetPolicyCode -match 'ProductWorkspaceViewResetStatus\.Invalid' -and
        $codeBehind -match 'ProductWorkspaceViewResetPolicy\.Evaluate' -and
        $codeBehind -match 'ProductWorkspaceSearchBox\.Text\.Length\s*>\s*0' -and
        $codeBehind -match '_suppressProductWorkspaceViewChanges\s*=\s*true' -and
        $codeBehind -match '_suppressProductWorkspaceViewChanges\s*=\s*false' -and
        $codeBehind -match 'ProductWorkspaceSearchBox\.Text\s*=\s*string\.Empty' -and
        $codeBehind -match 'ProductWorkspaceHealthFilterSelector\.SelectedIndex\s*=\s*0' -and
        $codeBehind -match 'ProductWorkspaceSortSelector\.SelectedIndex\s*=\s*0' -and
        $codeBehind -match 'ProductWorkspaceSearchBox\.Focus\(FocusState\.Programmatic\)' -and
        $codeBehind -match 'WorkspaceViewResetApplied' -and
        $codeBehind -match 'Changed=False:DesktopFilesChanged=False'
    ) 'Workspace zero-result recovery must require a recoverable non-default view and reset controls once without changing configuration or desktop files.'
    Assert-Condition (
        -not ($workspaceViewResetPolicyCode -match `
            'Catalog|PersistedTarget|CanonicalTarget|DesktopHost|File\.|Directory\.|Save|Telemetry') -and
        -not ($codeBehind -match `
            'ProductWorkspaceResetViewButton_Click[\s\S]{0,1600}(Submit|Commit|Save|DesktopHost)')
    ) 'Workspace zero-result recovery must remain presentation-only and avoid persistence or desktop execution paths.'
    $workspaceEmptyCreateNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceEmptyCreateButton'
    Assert-Condition (
        $workspaceEmptyCreateNode.GetAttribute('IsEnabled') -eq 'False' -and
        $workspaceEmptyCreateNode.GetAttribute('Click') -eq `
            'ProductWorkspaceEmptyCreateButton_Click' -and
        $workspaceEmptyCreateNode.GetAttribute('AutomationProperties.ItemStatus') -eq `
            'WorkspaceEmptyCreateShortcutUnavailable:Focused=False:Changed=False:DesktopFilesChanged=False' -and
        $workspaceEmptyCreateNode.ParentNode.ParentNode.GetAttribute('Visibility') -eq `
            'Collapsed'
    ) 'Workspace empty-create shortcut must start collapsed, disabled, finite, and explicit-click only.'
    Assert-Condition (
        $workspaceEmptyCreateShortcutPolicyCode -match 'isKnownEmptyWorkspace' -and
        $workspaceEmptyCreateShortcutPolicyCode -match 'readContainerCount\s*==\s*0' -and
        $workspaceEmptyCreateShortcutPolicyCode -match 'canCreateContainer' -and
        $workspaceEmptyCreateShortcutPolicyCode -match 'editorCandidateCount\s*==\s*0' -and
        $workspaceEmptyCreateShortcutPolicyCode -match 'ProductWorkspaceEmptyCreateShortcutStatus\.Invalid' -and
        $workspaceReadPresentationCode -match 'IsKnownEmptyWorkspace' -and
        $codeBehind -match 'ProductWorkspaceEmptyCreateShortcutPolicy\.Evaluate' -and
        $codeBehind -match 'ApplyProductWorkspaceReadModel[\s\S]{0,1200}UpdateProductWorkspaceEmptyCreateShortcut' -and
        $codeBehind -match 'ApplyProductWorkspaceContainerEditor[\s\S]{0,4000}UpdateProductWorkspaceEmptyCreateShortcut' -and
        $codeBehind -match 'ProductWorkspaceContainerNameEditor\.Focus\(\s*FocusState\.Programmatic\)' -and
        $codeBehind -match 'WorkspaceEmptyCreateShortcutOpened' -and
        $codeBehind -match 'Changed=False:DesktopFilesChanged=False'
    ) 'Workspace empty-create shortcut must align known-empty read and editor state, then focus the existing name editor without changing configuration.'
    Assert-Condition (
        -not ($workspaceEmptyCreateShortcutPolicyCode -match `
            'Catalog|PersistedTarget|CanonicalTarget|DesktopHost|File\.|Directory\.|Save|Telemetry') -and
        -not ($codeBehind -match `
            'ProductWorkspaceEmptyCreateButton_Click[\s\S]{0,1400}(ProductWorkspaceContainerNameEditor\.Text\s*=|Submit|Commit|Save|DesktopHost|ProductWorkspaceContainerCreateButton_Click)')
    ) 'Workspace empty-create shortcut must only focus the existing editor and must not fill, create, save, or execute desktop operations.'
    Assert-Condition (
        $desktopHostProjectionBuilderCode -match `
            'EmptyWorkspace[\s\S]{0,500}emptyBatch' -and
        $windowsDesktopHostReadOnlySurfaceCode -match `
            'GetEmptyCreateButtonBounds' -and
        $windowsDesktopHostReadOnlySurfaceCode -match `
            'GetContinuedCreateButtonBounds' -and
        $windowsDesktopHostReadOnlySurfaceCode -match `
            'GetCurrentInputMessageSource' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'WmRButtonUp' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'CreatePopupMenu' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'TrackPopupMenuEx' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'DestroyMenu' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'WmHotKey' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'RegisterHotKey' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'UnregisterHotKey' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'HtTransparent' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'WsExNoActivate' -and
        $desktopWorkspaceCreateAdmissionCode -match 'UntrustedSource' -and
        $desktopWorkspaceCreateAdmissionCode -match 'Injected' -and
        $desktopWorkspaceCreateAdmissionCode -match 'AutoRepeat' -and
        $desktopWorkspaceCreateAdmissionCode -match 'StaleWorkspace' -and
        $desktopWorkspaceCreateAdmissionCode -match 'StaleTopology' -and
        $desktopWorkspaceCreateAdmissionCode -match 'PointerDrag' -and
        $desktopWorkspaceCreateAdmissionCode -match 'RequestedBoundsPixels' -and
        $workspaceContainerCreationDefaultsCode -match `
            'MinimumDraggedWidthDip\s*=\s*160' -and
        $workspaceContainerCreationDefaultsCode -match `
            'MinimumDraggedHeightDip\s*=\s*120' -and
        $workspaceContainerCreationDefaultsCode -match `
            'requested\.Intersect\(workArea\)' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'WmMouseMove' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'WmLButtonUp' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'SetCapture' -and
        $windowsDesktopHostReadOnlySurfaceCode -match 'ReleaseCapture' -and
        $windowsDesktopHostReadOnlySurfaceCode -match `
            'SubmitWorkspaceCreateDragInput' -and
        $windowsDesktopHostUiaProviderCode -match `
            'LongGrid\.DesktopHost\.EmptyCreateButton' -and
        $windowsDesktopHostUiaProviderCode -match `
            'LongGrid\.DesktopHost\.WorkspaceCreateButton' -and
        $windowsDesktopHostUiaProviderCode -match `
            'InvokePatternIdentifiers\.Pattern' -and
        $appCode -match `
            'BindWorkspaceCreate\(\s*RequestDesktopWorkspaceCreate\)' -and
        $appCode -match `
            'BindContainerLayout\(\s*RequestDesktopContainerLayout\)' -and
        $appCode -match `
            'private bool RequestDesktopContainerLayout[\s\S]{0,5000}desktopContainerLayoutInteractions\.Handle[\s\S]{0,5000}ApplyContainerLayoutPreview' -and
        $desktopHostLifecycleControllerCode -match `
            'ApplyContainerLayoutPreview' -and
        $windowsDesktopHostReadOnlySurfaceCode -match `
            'containerLayoutPreview' -and
        $windowsDesktopHostReadOnlySurfaceCode -match `
            'ApplyContainerLayoutPreview\(\s*ProductDesktopHostReadOnlyProjection source' -and
        $desktopHostLifecycleControllerCode -match `
            'placement\.DisplayKey[\s\S]{0,1200}ApplyContainerLayoutPreview' -and
        $appCode -match `
            'DrivingFormalHardwareCrossDisplayMove' -and
        $appCode -match `
            'RequestDesktopWorkspaceCreate[\s\S]{0,2400}ProductDesktopHostLifecycleStatus\.ReadyReadOnly' -and
        $appCode -match `
            'RequestDesktopWorkspaceCreate[\s\S]{0,2200}RunDesktopWorkspaceCreatePreviewAsync' -and
        $appCode -match `
            'RunDesktopWorkspaceCreatePreviewAsync[\s\S]{0,12000}DesktopWorkspaceCreatePreviewWindow[\s\S]{0,12000}ShowDesktopWorkspaceCreatePreviewAsync[\s\S]{0,12000}CommitProductWorkspaceContainerActionCore' -and
        $appCode -match `
            'ProductDesktopWorkspaceCreatePreviewPlacement\.ResolveWindowBounds' -and
        $appCode -match `
            'request\.RequestedBoundsPixels' -and
        $appCode -match `
            'createBoundsPixels:\s*request\.RequestedBoundsPixels' -and
        $desktopWorkspaceCreatePreviewPlacementCode -match `
            'workArea\.Left\s*\+\s*relativeLeft' -and
        $desktopWorkspaceCreatePreviewPlacementCode -match `
            'workArea\.Top\s*\+\s*relativeTop' -and
        $desktopWorkspaceCreateInlinePreviewCode -match `
            'DesktopWorkspaceCreateInlinePreviewRoot' -and
        $desktopWorkspaceCreateInlinePreviewCode -match `
            'DesktopWorkspaceCreateInlinePreviewNameEditor' -and
        $desktopWorkspaceCreateInlinePreviewCode -match `
            'DesktopWorkspaceCreateInlinePreviewConfirmButton' -and
        $desktopWorkspaceCreateInlinePreviewCode -match `
            'DesktopWorkspaceCreateInlinePreviewCancelButton' -and
        $desktopWorkspaceCreateInlinePreviewCode -match `
            'IsShownInSwitchers\s*=\s*false' -and
        $desktopWorkspaceCreateInlinePreviewCode -match `
            'SetBorderAndTitleBar\(false,\s*false\)' -and
        $desktopWorkspaceCreateInlinePreviewCode -match `
            'WindowActivationState\.Deactivated' -and
        $codeBehind -match 'DesktopWorkspaceCreatePreviewDialog' -and
        $codeBehind -match 'DesktopWorkspaceCreatePreviewNameEditor' -and
        $codeBehind -match 'DesktopWorkspaceCreatePreviewPlacementSummary' -and
        $codeBehind -match 'DesktopWorkspaceCreatePreviewValidation' -and
        $codeBehind -match 'IsPrimaryButtonEnabled\s*=\s*current\.CanSubmit' -and
        $desktopWorkspaceCreatePreviewCode -match 'Editing' -and
        $desktopWorkspaceCreatePreviewCode -match 'Submitting' -and
        $desktopWorkspaceCreatePreviewCode -match 'Cancelled' -and
        $desktopWorkspaceCreatePreviewCode -match 'StaleWorkspace' -and
        $desktopWorkspaceCreatePreviewCode -match 'StaleTopology' -and
        $desktopWorkspaceCreatePreviewCode -match 'DuplicateName'
    ) 'DesktopHost workspace-create entries must keep bounded non-activating inputs, open one editable preview before the unified commit, expose finite UIA validation, and reject unsafe, invalid, cancelled, or stale sessions.'
    $workspaceOpenReviewNode = Get-XamlNodeByAutomationId `
        $document `
        'ProductWorkspaceOpenReviewButton'
    Assert-Condition (
        $workspaceOpenReviewNode.GetAttribute('IsEnabled') -eq 'False' -and
        $workspaceOpenReviewNode.GetAttribute('Visibility') -eq 'Collapsed' -and
        $workspaceOpenReviewNode.GetAttribute('Click') -eq `
            'ProductWorkspaceOpenReviewButton_Click' -and
        $workspaceOpenReviewNode.GetAttribute('AutomationProperties.ItemStatus') -eq `
            'WorkspaceReviewShortcutUnavailable:Items=0:DesktopFilesChanged=False'
    ) 'Workspace review shortcut must start collapsed, disabled, finite, and explicit-click only.'
    Assert-Condition (
        $workspaceReadModelCode -match 'ProductWorkspaceContainerHealthFilterPolicy' -and
        $workspaceReadModelCode -match 'ProductWorkspaceContainerHealthFilter\.All' -and
        $workspaceReadModelCode -match 'ProductWorkspaceContainerHealthFilter\.NeedsReview' -and
        $workspaceReadModelCode -match 'ProductWorkspaceContainerHealthFilter\.Empty' -and
        $workspaceReadModelCode -match 'ProductWorkspaceContainerHealthFilter\.Ready' -and
        $workspaceReadModelCode -match '_\s*=>\s*false' -and
        $workspaceReadPresentationCode -match 'WorkspaceViewFilterUnavailable' -and
        $workspaceReadPresentationCode -match 'DesktopFilesChanged=False'
    ) 'Workspace health filter must use finite presentation-only modes and fail closed.'
    Assert-Condition (
        $workspaceReviewShortcutPolicyCode -match 'workspaceUnresolvedCount\s*>\s*0' -and
        $workspaceReviewShortcutPolicyCode -match `
            'reviewItemCount\s*==\s*workspaceUnresolvedCount' -and
        $workspaceReviewShortcutPolicyCode -match 'reviewAvailable' -and
        $codeBehind -match 'ProductWorkspaceOpenReviewButton\.IsEnabled\s*=\s*false' -and
        $codeBehind -match 'ProductWorkspaceOpenReviewButton\.Visibility\s*=\s*Visibility\.Collapsed' -and
        $codeBehind -match 'ProductWorkspaceHealthFilterSelector\.SelectedIndex\s*=\s*1' -and
        $codeBehind -match 'ProductWorkspaceReferenceReviewSelector\.Focus\(' -and
        $codeBehind -match 'FocusState\.Programmatic' -and
        $codeBehind -match 'UpdateProductWorkspaceOpenReviewButton\(\)'
    ) 'Workspace review shortcut must require aligned counts and move focus only after an explicit click.'
    Assert-Condition (
        $document.OuterXml.Contains('Click="ProductWorkspaceContainerNavigateButton_Click"') -and
        $document.OuterXml.Contains('Tag="{Binding Ordinal}"') -and
        $document.OuterXml.Contains(
            'AutomationProperties.Name="{Binding NavigationAccessibilityName}"') -and
        -not ($document.OuterXml.Contains(
            'AutomationProperties.AutomationId="ProductWorkspaceContainerNavigateButton"'))
    ) 'Repeated workspace navigation buttons must be explicit, ordinal-bound, named, and avoid duplicate AutomationIds.'
    Assert-Condition (
        $workspaceContainerNavigationPolicyCode -match 'requestedOrdinal\s*<=\s*0' -and
        $workspaceContainerNavigationPolicyCode -match `
            'workspaceOrdinals\.Count\(value\s*=>\s*value\s*==\s*requestedOrdinal\)\s*!=\s*1' -and
        $workspaceContainerNavigationPolicyCode -match 'return\s+-1' -and
        $codeBehind -match 'ProductWorkspaceContainerNavigationPolicy' -and
        $codeBehind -match 'ProductWorkspaceContainerEditSelector\.SelectedIndex\s*=\s*candidateIndex' -and
        $codeBehind -match 'ProductWorkspaceContainerEditSelector\.Focus\(' -and
        $codeBehind -match 'WorkspaceContainerNavigationRejected' -and
        $codeBehind -match 'Changed=False:DesktopFilesChanged=False'
    ) 'Workspace card navigation must require unique read/editor ordinals, fail closed, and only select and focus the existing editor.'
    Assert-Condition (
        $document.OuterXml.Contains(
            'Click="ProductWorkspaceContainerQuickCollapseButton_Click"') -and
        $document.OuterXml.Contains(
            'AutomationProperties.Name="{Binding QuickCollapseAccessibilityName}"') -and
        $document.OuterXml.Contains('Content="{Binding QuickCollapseButtonText}"') -and
        $document.OuterXml.Contains('IsEnabled="{Binding CanQuickToggleCollapsed}"') -and
        -not ($document.OuterXml.Contains(
            'AutomationProperties.AutomationId="ProductWorkspaceContainerQuickCollapseButton"'))
    ) 'Repeated quick-collapse buttons must be explicit, state-bound, named, and avoid duplicate AutomationIds.'
    Assert-Condition (
        $workspaceContainerQuickCollapsePolicyCode -match 'requestedOrdinal\s*<=\s*0' -and
        $workspaceContainerQuickCollapsePolicyCode -match 'workspace\s+is\s+not\s+null' -and
        $workspaceContainerQuickCollapsePolicyCode -match 'candidate\s+is\s+not\s+null' -and
        $workspaceContainerQuickCollapsePolicyCode -match 'workspace\.IsLocked' -and
        $workspaceContainerQuickCollapsePolicyCode -match `
            'workspace\.IsCollapsed\s*!=\s*candidate\.IsCollapsed' -and
        $codeBehind -match 'ProductWorkspaceContainerQuickCollapsePolicy' -and
        $codeBehind -match `
            'ProductWorkspaceContainerEditSelector\.SelectedIndex\s*=\s*decision\.CandidateIndex' -and
        $codeBehind -match 'ProductWorkspaceContainerCommitAction\.SetCollapsed' -and
        $codeBehind -match 'WorkspaceContainerQuickCollapse' -and
        $codeBehind -match 'DesktopFilesChanged=False' -and
        $workspaceReadPresentationCode -match 'CanQuickToggleCollapsed\s*=>\s*!IsLocked' -and
        $workspaceReadPresentationCode -match 'QuickCollapseButtonText'
    ) 'Quick collapse must require aligned unlocked snapshots and reuse the configuration-only collapse commit.'
    Assert-Condition (
        $document.OuterXml.Contains(
            'Click="ProductWorkspaceContainerQuickLockButton_Click"') -and
        $document.OuterXml.Contains(
            'AutomationProperties.Name="{Binding QuickLockAccessibilityName}"') -and
        $document.OuterXml.Contains('Content="{Binding QuickLockButtonText}"') -and
        $document.OuterXml.Contains('IsEnabled="{Binding CanQuickLock}"') -and
        -not ($document.OuterXml.Contains(
            'AutomationProperties.AutomationId="ProductWorkspaceContainerQuickLockButton"'))
    ) 'Repeated quick-lock buttons must be explicit, state-bound, named, and avoid duplicate AutomationIds.'
    Assert-Condition (
        $workspaceContainerQuickLockPolicyCode -match 'requestedOrdinal\s*<=\s*0' -and
        $workspaceContainerQuickLockPolicyCode -match 'workspace\s+is\s+not\s+null' -and
        $workspaceContainerQuickLockPolicyCode -match 'candidate\s+is\s+not\s+null' -and
        $workspaceContainerQuickLockPolicyCode -match 'workspace\.IsLocked' -and
        $workspaceContainerQuickLockPolicyCode -match 'candidate\.IsLocked' -and
        $codeBehind -match 'ProductWorkspaceContainerQuickLockPolicy' -and
        $codeBehind -match `
            'ProductWorkspaceContainerEditSelector\.SelectedIndex\s*=\s*decision\.CandidateIndex' -and
        $codeBehind -match 'ProductWorkspaceContainerCommitAction\.SetLocked' -and
        $codeBehind -match 'stateValue:\s*true' -and
        $codeBehind -match 'WorkspaceContainerQuickLock' -and
        $codeBehind -match 'DesktopFilesChanged=False' -and
        $workspaceReadPresentationCode -match 'CanQuickLock\s*=>\s*!IsLocked' -and
        $workspaceReadPresentationCode -match 'QuickLockAccessibilityName'
    ) 'Quick lock must require aligned unlocked snapshots, lock only, and keep unlock in the management area.'
    $workspaceCardManageButton = $document.SelectSingleNode(
        "//*[local-name()='Button' and @Click='ProductWorkspaceContainerNavigateButton_Click']")
    $workspaceCardCollapseButton = $document.SelectSingleNode(
        "//*[local-name()='Button' and @Click='ProductWorkspaceContainerQuickCollapseButton_Click']")
    $workspaceCardLockButton = $document.SelectSingleNode(
        "//*[local-name()='Button' and @Click='ProductWorkspaceContainerQuickLockButton_Click']")
    $workspaceCardActionGrid = $workspaceCardManageButton.ParentNode
    Assert-Condition (
        $workspaceCardActionGrid.LocalName -eq 'Grid' -and
        $workspaceCardActionGrid.GetAttribute('ColumnSpacing') -eq '8' -and
        $workspaceCardActionGrid.GetAttribute('RowSpacing') -eq '8' -and
        $workspaceCardActionGrid.SelectNodes(
            "./*[local-name()='Grid.RowDefinitions']/*[local-name()='RowDefinition' and @Height='Auto']"
        ).Count -eq 2 -and
        $workspaceCardActionGrid.SelectNodes(
            "./*[local-name()='Grid.ColumnDefinitions']/*[local-name()='ColumnDefinition' and @Width='*']"
        ).Count -eq 2 -and
        $workspaceCardManageButton.GetAttribute('Grid.ColumnSpan') -eq '2' -and
        $workspaceCardManageButton.GetAttribute('HorizontalAlignment') -eq 'Stretch' -and
        $workspaceCardCollapseButton.ParentNode -eq $workspaceCardActionGrid -and
        $workspaceCardCollapseButton.GetAttribute('Grid.Row') -eq '1' -and
        $workspaceCardCollapseButton.GetAttribute('HorizontalAlignment') -eq 'Stretch' -and
        $workspaceCardLockButton.ParentNode -eq $workspaceCardActionGrid -and
        $workspaceCardLockButton.GetAttribute('Grid.Row') -eq '1' -and
        $workspaceCardLockButton.GetAttribute('Grid.Column') -eq '1' -and
        $workspaceCardLockButton.GetAttribute('HorizontalAlignment') -eq 'Stretch'
    ) 'Workspace card actions must use a two-row intrinsic grid with a full-width management action and equal quick actions.'
    $workspaceCardActionOrder = @(
        $workspaceCardActionGrid.SelectNodes("./*[local-name()='Button']") |
            ForEach-Object { $_.GetAttribute('Click') }
    ) -join '>'
    Assert-Condition (
        $workspaceCardActionOrder -eq (
            'ProductWorkspaceContainerNavigateButton_Click>' +
            'ProductWorkspaceContainerQuickCollapseButton_Click>' +
            'ProductWorkspaceContainerQuickLockButton_Click')
    ) 'Workspace card action source order must preserve manage, collapse, then lock keyboard traversal.'
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
        $codeBehind -match 'ProductWorkspaceContainerList\.ItemsSource\s*=\s*filtered\.Containers' -and
        $codeBehind -match `
            'ProductWorkspaceViewStatus\.Text\s*=\s*detailOverride\s*\?\?\s*filtered\.Detail' -and
        $codeBehind -match 'ProductWorkspaceHealthFilterSelector_SelectionChanged' -and
        -not ($codeBehind -match 'ProductWorkspaceRead.*(State|CatalogEntry|PersistedTarget|CanonicalTarget)')
    ) 'MainWindow must filter only the presentation contract, never workspace identity state.'
    $selectedReferenceContainerCommitStart = $referenceCommitCode.IndexOf(
        'public ProductWorkspaceSelectedReferenceContainerCommitResult',
        [System.StringComparison]::Ordinal)
    $selectedReferenceContainerCommitEnd = if ($selectedReferenceContainerCommitStart -ge 0) {
        $referenceCommitCode.IndexOf(
            '    public ',
            $selectedReferenceContainerCommitStart + 1,
            [System.StringComparison]::Ordinal)
    }
    else {
        -1
    }
    $selectedReferenceContainerCommitCode = if (
        $selectedReferenceContainerCommitStart -ge 0 -and
        $selectedReferenceContainerCommitEnd -gt $selectedReferenceContainerCommitStart) {
        $referenceCommitCode.Substring(
            $selectedReferenceContainerCommitStart,
            $selectedReferenceContainerCommitEnd - $selectedReferenceContainerCommitStart)
    }
    else {
        ''
    }
    Assert-Condition (
        ([regex]::Matches($selectedReferenceContainerCommitCode, 'saves\.Submit\(edit\)').Count -eq 1) -and
        $referenceCommitCode -match 'editRevision\s*=\s*checked\(editRevision\s*\+\s*1\)' -and
        $referenceCommitCode -match 'ProductWorkspaceReferenceGate\.Evaluate' -and
        $referenceCommitCode -match 'ProductWorkspaceConfigurationProjector\.Project' -and
        $referenceCommitCode -match 'CommitContainer' -and
        $referenceCommitCode -match 'CommitResolvedReference' -and
        $referenceCommitCode -match 'ExpectedCatalogGeneration' -and
        $referenceCommitCode -match 'AlreadyReferenced' -and
        $referenceCommitCode -match 'ProductWorkspaceReducer\.AddResolvedReference' -and
        $referenceCommitCode -match 'CommitResolvedReferenceBatch' -and
        $referenceCommitCode -match 'MaximumResolvedReferenceBatchSize\s*=\s*256' -and
        $referenceCommitCode -match 'ProductWorkspaceReducer\.AddResolvedReferences' -and
        $referenceCommitCode -match 'CommitReferenceBatchAdditionUndo' -and
        $referenceCommitCode -match 'CommitResolvedReferenceRemoval' -and
        $referenceCommitCode -match 'CommitReferenceRemovalUndo' -and
        $referenceCommitCode -match 'ProductWorkspaceReducer\.RemoveReference' -and
        $referenceCommitCode -match 'CommitResolvedReferenceBatchRemoval' -and
        $referenceCommitCode -match 'MaximumResolvedReferenceRemovalBatchSize\s*=\s*256' -and
        $referenceCommitCode -match 'ProductWorkspaceReducer\.RemoveResolvedReferences' -and
        $codeBehind -match 'ResolvedReferenceBatchRemoval' -and
        $codeBehind -match 'Atomic=True' -and
        $referenceCommitCode -match 'CommitResolvedReferenceReassignment' -and
        $referenceCommitCode -match 'MaximumResolvedReferenceReassignmentBatchSize\s*=\s*256' -and
        $referenceCommitCode -match 'CommitReferenceReassignmentUndo' -and
        $referenceCommitCode -match 'ProductWorkspaceReducer\.ReassignResolvedReferences' -and
        $referenceCommitCode -match 'ProductWorkspaceReducer\.(CreateContainer|RenameContainer)' -and
        $referenceCommitCode -match 'ProductWorkspaceReducer\.RemoveContainer' -and
        $referenceCommitCode -match 'CommitContainerRemovalUndo' -and
        $referenceCommitCode -match 'CommitLayoutRecovery' -and
        $referenceCommitCode -match 'CommitLayoutRecoveryUndo'
    ) 'Reference review, resolved-reference addition, container, layout recovery, and undo edits must share one coordinator with one submission per accepted path.'
    Assert-Condition (
        $codeBehind -match 'ProductWorkspaceResolvedReferenceSelectFirstBatchButton_Click' -and
        $codeBehind -match 'ProductWorkspaceResolvedReferenceSelectContainerBatchButton_Click' -and
        ([regex]::Matches($codeBehind, 'SelectedItems\.Clear\(\)').Count -ge 4) -and
        $codeBehind -match 'Take\(\s*ProductWorkspaceCommitCoordinator\.MaximumResolvedReferenceBatchSize\s*\)' -and
        $codeBehind -match 'MaximumResolvedReferenceRemovalBatchSize' -and
        $codeBehind -match 'MaximumResolvedReferenceReassignmentBatchSize' -and
        $codeBehind -match 'ResolvedReferenceBatchAddSelection:Count=0' -and
        $codeBehind -match 'ResolvedReferenceBatchRemovalSelection:Count=0' -and
        $codeBehind -match 'DesktopFilesChanged=False'
    ) 'Batch selection controls must remain bounded, container-scoped, clearable, status-visible, and config-only.'
    $batchReassignmentChecks = @(
        $codeBehind.Contains(
            'async void ProductWorkspaceResolvedReferenceReassignmentButton_Click')
        $codeBehind.Contains(
            'sources.Select(source => source.ContainerOrdinal)')
        $codeBehind.Contains('PrimaryButtonText =')
        $codeBehind.Contains('await confirmation.ShowAsync()')
        ([regex]::Matches(
                $codeBehind,
                'DefaultButton\s*=\s*ContentDialogButton\.Close').Count -ge 2)
        $codeBehind.Contains('ResolvedReferenceReassignment:{result.Status}')
        $codeBehind.Contains('Count={sources.Length}')
        $codeBehind.Contains('Atomic=True')
    )
    Assert-Condition (-not ($batchReassignmentChecks -contains $false)) `
        'Batch reassignment must stay same-source, bounded, confirmation-gated, atomic, and default-cancel.'
    Assert-Condition (
        $document.OuterXml -match 'x:Name="ProductWorkspaceResolvedReferenceSelectionActionGrid"' -and
        $document.OuterXml -match 'x:Name="ProductWorkspaceResolvedReferenceRemovalSelectionActionGrid"' -and
        $codeBehind -match 'ApplyTwoActionResponsiveLayout' -and
        $codeBehind -match 'ProductWorkspaceResolvedReferenceSelectionActionGrid' -and
        $codeBehind -match 'ProductWorkspaceResolvedReferenceRemovalSelectionActionGrid' -and
        $codeBehind -match 'grid\.RowSpacing\s*=\s*compact\s*\?\s*8\s*:\s*0' -and
        $codeBehind -match 'row:\s*compact\s*\?\s*1\s*:\s*0'
    ) 'Batch selection action grids must reflow vertically in compact mode.'
    Assert-Condition (
        $codeBehind -match '_suppressBatchSelectionAnnouncements' -and
        ([regex]::Matches(
                $codeBehind,
                'if\s*\(!_suppressBatchSelectionAnnouncements\)').Count -eq 2) -and
        $codeBehind -match 'PublishResolvedReferenceAddSelectionStatus' -and
        $codeBehind -match 'PublishResolvedReferenceRemovalSelectionStatus' -and
        $codeBehind -match 'FrameworkElementAutomationPeer\.(FromElement|CreatePeerForElement)' -and
        $codeBehind -match 'AutomationEvents\.LiveRegionChanged' -and
        ([regex]::Matches(
                $codeBehind,
                'RaiseLiveRegionChanged\(ProductWorkspaceResolvedReference(Add|Removal)Status\)').Count -ge 2) -and
        $codeBehind -match 'ResolvedReferenceBatchAddSelection:Count=0' -and
        $codeBehind -match 'ResolvedReferenceBatchRemovalSelection:Count=0'
    ) 'Batch selection changes must publish one explicit live-region event and clear stale empty-selection state.'
    Assert-Condition (
        $containerRemovalUndoCode -match 'RemovalEditRevision' -and
        $containerRemovalUndoCode -match 'OperationId' -and
        $containerRemovalUndoCode -match 'RemovedConfigurationFingerprint' -and
        $containerRemovalUndoCode -match 'RestoreConfigurationFingerprint' -and
        $containerRemovalUndoCode -match 'ConfirmationRequired' -and
        $containerRemovalUndoCode -match 'CurrentConfigurationChanged'
    ) 'Container-removal undo must bind revision, operation, fingerprints, and confirmation.'
    Assert-Condition (
        $referenceBatchAdditionUndoCode -match 'AdditionEditRevision' -and
        $referenceBatchAdditionUndoCode -match 'OperationId' -and
        $referenceBatchAdditionUndoCode -match 'AddedConfigurationFingerprint' -and
        $referenceBatchAdditionUndoCode -match 'RestoreConfigurationFingerprint' -and
        $referenceBatchAdditionUndoCode -match 'ConfirmationRequired' -and
        $referenceBatchAdditionUndoCode -match 'CurrentConfigurationChanged' -and
        $codeBehind -match 'SelectedItems' -and
        $codeBehind -match 'Atomic=True' -and
        $codeBehind -match 'DesktopFilesChanged=False'
    ) 'Batch-reference addition must be bounded, atomic, one-time undoable, and config-only.'
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
        $appCode -match 'CommitProductWorkspaceContainerRemovalUndo' -and
        $appCode -match 'ProductWorkspaceContainerCreationDefaults\.Evaluate' -and
        $appCode -match 'display\?\.StableId\s*\?\?\s*"display-unassigned"'
    ) 'App must support deterministic first/subsequent container creation through the shared audited coordinator and creation-default policy.'
    foreach ($stateAction in @(
            'SetLocked',
            'SetCollapsed',
            'SetAppearancePreset',
            'SetPlacementPreset',
            'Remove'
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
        coreRuntimeStatus = 'desktop-read-only-config-edit-host-user-switch-uia-session-attested'
        firstOrganizationPrototype = 'safe-reference-items-drop-semantics-undo'
        layoutRecoveryPrototype = 'automatic-review-blocked-expire-cancel'
        configurationRecovery = 'loaded-missing-backup-read-only-safe-mode'
        configurationRepair = 'confirmed-recovery-bounded-import-export-anonymous-interaction-evidence-and-single-removal'
        configurationShutdownDrain = 'controller-owned-bounded-explicit-edit-retry'
        productDesktopCatalog = 'physical-read-only-generation-latest-authoritative-only'
        productDesktopItemVisuals = 'windows-shell-stock-icons-finite-resolution-status-privacy-safe-uia-500-first-surface-bounded-20dip-100-to-400-percent'
        productDesktopThumbnailRequests = 'lazy-zero-disabled-12-visible-64-cache-version-size-theme-1500ms-circuit-breaker-appcontainer-job-finite-fallback'
        productDesktopThumbnailPresentation = 'persistent-switch-authoritative-candidates-loading-ready-fallback-stale-rejected-real-hwnd-bgra-1500ms-success-inplace-generation-500-item-viewport'
        productWorkspaceSession = 'formal-load-authoritative-catalog-revisioned-edit-baseline'
        productLayoutRecovery = 'verified-input-hide-bounded-shutdown-drain-app-blocked'
        productDisplayTopology = 'readonly-ccd-monitor-strong-identity-authoritative-adapter'
        productWorkspaceView = 'formal-session-intrinsic-card-actions-direct-navigation-quick-collapse-quick-lock-finite-health-filter-visible-search-finite-sort-zero-results-recovery-empty-create-pointer-context-hotkey-uia-drag-bounds-inline-preview-fallback-review-shortcut-anonymous-unresolved'
        productWorkspaceLatestUndo = 'single-visible-token-immediate-config-only-fail-closed'
        productResolvedReferenceAdd = 'bounded-256-multi-select-atomic-config-only-single-undo'
        productResolvedReferenceRemoval = 'same-container-bounded-256-atomic-config-only-single-undo'
        productBatchSelectionControls = 'focusable-bounded-single-live-announcement-empty-reset-compact-reflow'
        productResolvedReferenceReassignment = 'same-source-bounded-256-confirmed-atomic-config-only-single-undo'
        productContainerEdits = 'shared-revision-bounded-name-intent-guidance-create-rename-lock-collapse-finite-appearance-title-visibility-title-double-click-placement-remove-unified-edit-undo-save-compensation-selected-reference-preview-snapshot-atomic-move-full-restore-config-only-desktop-layout-session-candidate-publish-compensate-keyboard-title-focus-transaction-cross-display-mixed-dpi'
        productReferenceReview = 'anonymous-generation-revision-gated-explicit-save-submission'
                    productSavePresentation = 'privacy-safe-static-reduced-motion'
                    productDesktopActivation = 'finite-region-activation-explicit-pointer-keyboard-selectionitem-title-layout-select-then-authority-safe-file-folder-lnk-http-https-open-finite-path-free-feedback-zero-file-mutation'
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
        $element = $null
        try {
            $element = $Root.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                $condition
            )
        }
        catch [System.Runtime.InteropServices.COMException] {
            # WinUI can briefly rebuild its automation tree during initial layout.
        }
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

    $runtimePreflightPath = Join-Path $PSScriptRoot `
        'Test-LongGridWinUiUiaRuntime.ps1'
    $runtimePreflightJson = & powershell `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $runtimePreflightPath
    Assert-Condition ($LASTEXITCODE -eq 0) `
        'The WinUI cross-process UIA runtime preflight failed to execute.'
    $runtimePreflight = $runtimePreflightJson | ConvertFrom-Json
    if (
        $runtimePreflight.outcome -eq 'BlockedByKnownUpstream' -and
        -not $AcknowledgeKnownUiaCrashRisk)
    {
        throw (
            'Live cross-process UIA was blocked before application launch: ' +
            'WindowsAppRuntime ' +
            $runtimePreflight.actual.runtimePackageVersion +
            ' / Microsoft.UI.Xaml.dll ' +
            $runtimePreflight.actual.xamlFileVersion +
            ' matches the audited RPC_E_WRONG_THREAD fail-fast pair. ' +
            'Use -ContractOnly, install an upstream-fixed stable runtime, or ' +
            'explicitly pass -AcknowledgeKnownUiaCrashRisk in a disposable ' +
            'diagnostic session.')
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

    $desktopHostFlagName = 'LONGGRID_ENABLE_DESKTOP_HOST'
    $previousDesktopHostFlag = [Environment]::GetEnvironmentVariable(
        $desktopHostFlagName,
        [EnvironmentVariableTarget]::Process)
    try {
        [Environment]::SetEnvironmentVariable(
            $desktopHostFlagName,
            $(if ($DesktopHostDevelopmentOptIn) { '1' } else { $null }),
            [EnvironmentVariableTarget]::Process)
        $process = Start-Process -FilePath $appPath -PassThru
    }
    finally {
        [Environment]::SetEnvironmentVariable(
            $desktopHostFlagName,
            $previousDesktopHostFlag,
            [EnvironmentVariableTarget]::Process)
    }
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
        $expectedDesktopHostStatuses = @(
            'Disconnected',
            'ConnectedReadOnly',
            'DisabledBySafetyPolicy'
        )
        Assert-Condition (
            $expectedDesktopHostStatuses -contains $desktopHostCard.Current.ItemStatus
        ) 'The UI did not expose the audited DesktopHost feature boundary.'
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
            coreRuntimeStatus = 'development-read-only-host-user-switch'
            desktopHost = $desktopHostCard.Current.ItemStatus
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
