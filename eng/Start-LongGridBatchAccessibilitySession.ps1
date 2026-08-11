[CmdletBinding()]
param(
    [ValidateSet(
        'BSA-01',
        'BSA-02',
        'BSA-03',
        'BSA-04',
        'BSA-05')]
    [string]$Scenario,

    [ValidateSet('O1', 'O2', 'O3', 'O4', 'O5', 'O6', 'O7', 'O8', 'O9')]
    [string]$OperatorId,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$DedicatedTestAccountConfirmed,
    [switch]$RecoveryPlanConfirmed,
    [switch]$NoRestore,
    [switch]$NoBuild,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'src\LongGrid.App\LongGrid.App.csproj'
$runtimeIdentifier = 'win-x64'
$targetFramework = 'net8.0-windows10.0.19041.0'
$xamlPath = Join-Path $projectRoot 'src\LongGrid.App\MainWindow.xaml'
$codeBehindPath = Join-Path $projectRoot 'src\LongGrid.App\MainWindow.xaml.cs'
$startScript = Join-Path $PSScriptRoot 'Start-LongGrid.ps1'
$uiScript = Join-Path $PSScriptRoot 'Test-LongGridUi.ps1'
$runbook = Join-Path $projectRoot `
    'docs\manual-testing\batch-selection-accessibility-runbook.md'

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-LongGridProcesses {
    @(
        Get-Process -Name 'LongGrid.App' -ErrorAction SilentlyContinue
    )
}

function Assert-CleanSession {
    param([string]$Checkpoint)

    $processes = @(Get-LongGridProcesses)
    Assert-Condition ($processes.Count -eq 0) `
        "$Checkpoint requires zero LongGrid.App processes; found PID(s): $($processes.Id -join ', '). This launcher never terminates processes it did not start."
}

if ($env:OS -ne 'Windows_NT') {
    throw 'Long Grid batch accessibility sessions can only run on Windows.'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet was not found. Install the .NET SDK selected by global.json.'
}

foreach ($requiredPath in @(
    $projectPath,
    $xamlPath,
    $codeBehindPath,
    $startScript,
    $uiScript,
    $runbook)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Batch accessibility session dependency was not found: $requiredPath"
    }
}

if (-not $ValidateOnly) {
    if ([string]::IsNullOrWhiteSpace($Scenario)) {
        throw 'Scenario is required and must be one ID from BSA-01 through BSA-05.'
    }

    if ([string]::IsNullOrWhiteSpace($OperatorId)) {
        throw 'OperatorId is required and must be one anonymous label from O1 through O9.'
    }

    if (-not $DedicatedTestAccountConfirmed) {
        throw 'Confirm a dedicated test account with no personal desktop content by passing -DedicatedTestAccountConfirmed.'
    }

    if (-not $RecoveryPlanConfirmed) {
        throw 'Confirm the scenario recovery plan by passing -RecoveryPlanConfirmed.'
    }
}

$scenarioCatalog = [ordered]@{
    'BSA-01' = 'KeyboardOnlyBatchSelection'
    'BSA-02' = 'NarratorLiveRegionAndControlSemantics'
    'BSA-03' = 'HighContrastFocusSelectionAndDisabledStates'
    'BSA-04' = 'TextScale200PercentNoClipping'
    'BSA-05' = 'CompactWidthReflowAndFocusOrder'
}

$requiredAutomationIds = @(
    'ProductWorkspaceResolvedReferenceSelector',
    'ProductWorkspaceResolvedReferenceSelectFirstBatchButton',
    'ProductWorkspaceResolvedReferenceClearSelectionButton',
    'ProductWorkspaceResolvedReferenceAddStatus',
    'ProductWorkspaceResolvedReferenceRemovalSelector',
    'ProductWorkspaceResolvedReferenceSelectContainerBatchButton',
    'ProductWorkspaceResolvedReferenceRemovalClearSelectionButton',
    'ProductWorkspaceResolvedReferenceRemovalStatus'
)

$xaml = Get-Content -LiteralPath $xamlPath -Raw -Encoding UTF8
$codeBehind = Get-Content -LiteralPath $codeBehindPath -Raw -Encoding UTF8
foreach ($automationId in $requiredAutomationIds) {
    Assert-Condition (
        $xaml.Contains(
            "AutomationProperties.AutomationId=`"$automationId`"")) `
        "The batch accessibility contract is missing AutomationId '$automationId'."
}

Assert-Condition (
    $xaml.Contains('x:Name="ProductWorkspaceResolvedReferenceAddStatus"') -and
    $xaml.Contains('x:Name="ProductWorkspaceResolvedReferenceRemovalStatus"') -and
    ([regex]::Matches(
        $xaml,
        'AutomationProperties.LiveSetting="Polite"').Count -ge 2)
) 'The batch selection status regions must retain polite live-region semantics.'

Assert-Condition (
    $codeBehind.Contains('private const double CompactBreakpoint = 760;') -and
    $codeBehind.Contains('_suppressBatchSelectionAnnouncements') -and
    $codeBehind.Contains('AutomationEvents.LiveRegionChanged') -and
    $codeBehind.Contains('ApplyTwoActionResponsiveLayout(')
) 'The compact reflow and single live-region announcement source contract is incomplete.'

$uiJson = & powershell -NoProfile -ExecutionPolicy Bypass `
    -File $uiScript `
    -Configuration $Configuration `
    -ContractOnly
Assert-Condition ($LASTEXITCODE -eq 0) `
    'The authoritative Long Grid UI source contract validation failed.'
$uiResult = $uiJson | ConvertFrom-Json
Assert-Condition (
    $uiResult.outcome -eq 'Pass' -and
    $uiResult.contract.requiredAutomationIds -eq 139 -and
    $uiResult.contract.productBatchSelectionControls -eq
        'focusable-bounded-single-live-announcement-empty-reset-compact-reflow'
) 'The batch accessibility matrix requires the complete 139-ID UI contract.'

$commit = 'unavailable'
if (Get-Command git -ErrorAction SilentlyContinue) {
    $candidate = & git -C $projectRoot rev-parse --short=12 HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and
        -not [string]::IsNullOrWhiteSpace($candidate)) {
        $commit = $candidate.Trim()
    }
}

$sessionContract = [ordered]@{
    schemaVersion = 1
    purpose = 'LongGridBatchSelectionAccessibilityManualMatrix'
    scenario = if ($ValidateOnly) {
        'BSA-01-through-BSA-05-required-at-runtime'
    } else {
        $Scenario
    }
    scenarioName = if ($ValidateOnly) {
        'RecordedAtRuntime'
    } else {
        $scenarioCatalog[$Scenario]
    }
    operatorId = if ($ValidateOnly) {
        'O1-O9-required-at-runtime'
    } else {
        $OperatorId
    }
    operatorIdentifierPolicy = 'AnonymousLabelsOnly'
    commit = $commit
    requiredAutomationIds = 139
    focusedAutomationIds = $requiredAutomationIds.Count
    resultStatus = 'PendingManualEvidence'
    requiresManualJudgment = $true
    requiresDedicatedTestAccount = $true
    requiresPreparedAnonymousWorkspace = $true
    requiresRecoveryConfirmation = $true
    startsFreshProductProcess = -not $ValidateOnly
    ownsStartedProductProcess = -not $ValidateOnly
    launcherReadsDesktopMetadata = -not $ValidateOnly
    launcherReadsDesktopFileContent = $false
    launcherChangesLongGridConfiguration = $false
    launcherChangesDesktopFiles = $false
    launcherChangesSystemSettings = $false
    launcherCapturesEvidence = $false
    launcherWritesResultFile = $false
    terminatesForeignProcess = $false
}

Assert-Condition (
    $sessionContract.operatorIdentifierPolicy -eq 'AnonymousLabelsOnly' -and
    $sessionContract.resultStatus -eq 'PendingManualEvidence' -and
    $sessionContract.requiresManualJudgment -and
    $sessionContract.requiresDedicatedTestAccount -and
    $sessionContract.requiresPreparedAnonymousWorkspace -and
    $sessionContract.requiresRecoveryConfirmation -and
    -not $sessionContract.launcherReadsDesktopFileContent -and
    -not $sessionContract.launcherChangesLongGridConfiguration -and
    -not $sessionContract.launcherChangesDesktopFiles -and
    -not $sessionContract.launcherChangesSystemSettings -and
    -not $sessionContract.launcherCapturesEvidence -and
    -not $sessionContract.launcherWritesResultFile -and
    -not $sessionContract.terminatesForeignProcess
) 'The batch accessibility manual-session safety contract is invalid.'

$sessionContract | ConvertTo-Json -Depth 3

if ($ValidateOnly) {
    Write-Output 'Long Grid batch accessibility session chain validation passed; all BSA scenario results remain pending manual evidence.'
    exit 0
}

Assert-CleanSession 'Batch accessibility session preflight'
Write-Warning (
    'Run only the selected BSA scenario from the facilitator runbook. The launcher does not change system settings, ' +
    'capture evidence, modify desktop files, or decide Pass/Fail. Restore all operator-changed state before closing the session.'
)

$validationArguments = @(
    '-NoProfile',
    '-ExecutionPolicy', 'Bypass',
    '-File', $startScript,
    '-Configuration', $Configuration,
    '-ValidateOnly'
)
if ($NoRestore) {
    $validationArguments += '-NoRestore'
}
if ($NoBuild) {
    $validationArguments += '-NoBuild'
}

& powershell @validationArguments
if ($LASTEXITCODE -ne 0) {
    throw "Long Grid batch accessibility build validation failed with exit code $LASTEXITCODE."
}

$appPath = Join-Path $projectRoot `
    "src\LongGrid.App\bin\$Configuration\$targetFramework\$runtimeIdentifier\LongGrid.App.exe"
Assert-Condition (Test-Path -LiteralPath $appPath -PathType Leaf) `
    "LongGrid.App executable was not found: $appPath"
Assert-CleanSession 'Batch accessibility session launch checkpoint'

$productProcess = $null
try {
    $productProcess = Start-Process -FilePath $appPath -PassThru
    $windowDeadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 100
        $productProcess.Refresh()
    } while (-not $productProcess.HasExited -and
        $productProcess.MainWindowHandle -eq [IntPtr]::Zero -and
        [DateTime]::UtcNow -lt $windowDeadline)

    Assert-Condition (-not $productProcess.HasExited) `
        'LongGrid.App exited before the batch accessibility session could begin.'
    Assert-Condition ($productProcess.MainWindowHandle -ne [IntPtr]::Zero) `
        'LongGrid.App did not expose a main window within 15 seconds.'

    $productProcess.WaitForExit()
    Assert-Condition ($productProcess.ExitCode -eq 0) `
        "Long Grid batch accessibility session failed with exit code $($productProcess.ExitCode)."
}
finally {
    if ($null -ne $productProcess) {
        $productProcess.Refresh()
        if (-not $productProcess.HasExited) {
            Stop-Process -Id $productProcess.Id -Force -ErrorAction SilentlyContinue
            $productProcess.WaitForExit(5000) | Out-Null
        }
        $productProcess.Dispose()
    }
}

Assert-CleanSession 'Batch accessibility session postflight'
