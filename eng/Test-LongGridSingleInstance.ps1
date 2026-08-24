[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('x64')]
    [string] $Architecture = 'x64',

    [switch] $NoBuild,
    [switch] $ContractOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'src\LongGrid.App\LongGrid.App.csproj'
$projectCode = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
$programPath = Join-Path $projectRoot 'src\LongGrid.App\Program.cs'
$programCode = Get-Content -LiteralPath $programPath -Raw -Encoding UTF8
$appPath = Join-Path $projectRoot 'src\LongGrid.App\App.xaml.cs'
$appCode = Get-Content -LiteralPath $appPath -Raw -Encoding UTF8
$desktopFirstScript = Join-Path $PSScriptRoot `
    'Test-LongGridDesktopFirstStartup.ps1'

function Assert-Condition {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) {
        throw $Message
    }
}

Assert-Condition ($projectCode.Contains('DISABLE_XAML_GENERATED_MAIN')) `
    'LongGrid.App must disable the generated XAML entry point.'
Assert-Condition (
    $programCode.Contains('ProductDesktopFirstStartupEvidenceSession') -and
    $programCode.Contains('.ResolveInstanceKey(')) `
    'The custom entry point must isolate the desktop-first evidence key.'
Assert-Condition ($programCode.Contains('FindOrRegisterForKey(instanceKey)')) `
    'The custom entry point must register the resolved app-instance key.'
Assert-Condition ($programCode.Contains('RedirectActivationToAsync(activation)')) `
    'A secondary process must forward its complete activation arguments.'
Assert-Condition ($programCode.Contains('mainInstance.Activated += MainInstance_Activated')) `
    'The primary process must subscribe before starting XAML.'
Assert-Condition ($programCode.Contains('PendingActivations.Enqueue(activation)')) `
    'Activations arriving before App construction must be retained in memory.'
Assert-Condition ($appCode.Contains('Program.ReleaseMainInstance()')) `
    'The primary key must be released immediately before the drained window closes.'
Assert-Condition ($appCode.Contains('window.DispatcherQueue.TryEnqueue(ActivateMainWindow)')) `
    'Redirected activation must cross onto the existing window UI queue.'
Assert-Condition ($appCode.Contains('OverlappedPresenterState.Minimized')) `
    'Redirected activation must restore a minimized main window.'
Assert-Condition ($appCode.Contains('InitializeDesktopFirstStartupAsync')) `
    'Normal startup must use the desktop-first decision path.'
Assert-Condition (Test-Path -LiteralPath $desktopFirstScript -PathType Leaf) `
    'The real desktop-first single-instance evidence script is missing.'

if ($ContractOnly) {
    Write-Output 'Long Grid single-instance source contract passed.'
    exit 0
}

Assert-Condition ($env:OS -eq 'Windows_NT') `
    'Long Grid single-instance live validation requires Windows.'
Assert-Condition ($Architecture -eq 'x64') `
    'The desktop-first evidence runner currently supports x64 only.'

$arguments = @{
    Configuration = $Configuration
}
if ($NoBuild) {
    $arguments.NoBuild = $true
}

$result = & $desktopFirstScript @arguments
$evidence = $result | ConvertFrom-Json
Assert-Condition (
    $evidence.Outcome -eq 'Pass' -and
    $evidence.Difference -eq 'None' -and
    $evidence.Actual.SecondaryExitCode -eq 0 -and
    $evidence.Actual.RedirectedControlCenterCount -eq 1 -and
    $evidence.Actual.RemainingLiveProcessCount -eq 0
) 'The desktop-first single-instance Expected/Actual evidence did not pass.'

Write-Output `
    'Long Grid live single-instance validation passed: desktop-first redirect, exit, activate.'
