[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('Initial', 'Redirect', 'DuplicateRedirect')]
    [string]$Scenario = 'Redirect',

    [switch]$ContractOnly,

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'src\LongGrid.App\LongGrid.App.csproj'
$targetFramework = 'net8.0-windows10.0.19041.0'
$runtimeIdentifier = 'win-x64'
$outputDirectory = Join-Path $projectRoot `
    "src\LongGrid.App\bin\$Configuration\$targetFramework\$runtimeIdentifier"
$appPath = Join-Path $outputDirectory 'LongGrid.App.exe'
$environmentName = 'LONGGRID_BOX_R1_ACTIVATION_EVIDENCE_SESSION'
$sessionId = [Guid]::NewGuid().ToString('N')
$evidenceRoot = [IO.Path]::GetFullPath((Join-Path `
    ([IO.Path]::GetTempPath()) `
    'LongGridBoxR1Evidence'))
$sessionDirectory = [IO.Path]::GetFullPath((Join-Path $evidenceRoot $sessionId))
$expectedSessionPrefix = $evidenceRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$readyPath = Join-Path $sessionDirectory 'ready.json'
$resultPath = Join-Path $sessionDirectory 'result.json'
$userConfigurationDirectory = Join-Path $env:LOCALAPPDATA 'LongGrid'
$desktopDirectory = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::DesktopDirectory)
$primaryProcess = $null
$secondaryProcess = $null
$duplicateProcess = $null

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Get-DirectoryMetadataFingerprint {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return 'MISSING'
    }

    $lines = @(
        Get-ChildItem -LiteralPath $Path -Force -ErrorAction Stop |
            Sort-Object -Property Name |
            ForEach-Object {
                $length = if ($_.PSIsContainer) { -1 } else { $_.Length }
                '{0}|{1}|{2}|{3}' -f `
                    $_.Name, `
                    [int]$_.Attributes, `
                    $length, `
                    $_.LastWriteTimeUtc.Ticks
            }
    )
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $payload = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
        return [BitConverter]::ToString($sha.ComputeHash($payload)).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function Wait-ForPath {
    param([string]$Path, [int]$TimeoutSeconds)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $Path -PathType Leaf) { return }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for $Path"
}

function Get-LiveLongGridProcess {
    @(
        Get-Process -Name 'LongGrid.App' -ErrorAction SilentlyContinue |
            Where-Object {
                $_.HandleCount -gt 0 -and @($_.Threads).Count -gt 0
            }
    )
}

if ($ContractOnly) {
    $coreCode = Get-Content -LiteralPath (Join-Path $projectRoot `
        'src\LongGrid.Core\DesktopHost\ProductExplorerCreateActivation.cs') -Raw
    $appCode = Get-Content -LiteralPath (Join-Path $projectRoot `
        'src\LongGrid.App\App.xaml.cs') -Raw
    $programCode = Get-Content -LiteralPath (Join-Path $projectRoot `
        'src\LongGrid.App\Program.cs') -Raw
    Assert-Condition ($coreCode.Contains('--long-grid-create-box=')) `
        'BOX-R1 command prefix is missing.'
    Assert-Condition ($coreCode.Contains('MaximumAge')) `
        'BOX-R1 freshness boundary is missing.'
    Assert-Condition ($appCode.Contains('TryDispatchExplorerCreateActivation')) `
        'BOX-R1 App dispatch is missing.'
    Assert-Condition ($programCode.Contains('RedirectActivationToAsync')) `
        'BOX-R1 single-instance redirection is missing.'
    [ordered]@{
        SchemaVersion = 1
        Purpose = 'BoxR1ActivationContract'
        Expected = [ordered]@{
            VersionedFiniteCommand = $true
            FreshnessBoundary = $true
            InitialAndRedirectedDispatch = $true
        }
        Actual = [ordered]@{
            VersionedFiniteCommand = $true
            FreshnessBoundary = $true
            InitialAndRedirectedDispatch = $true
        }
        Difference = 'None'
        Outcome = 'Pass'
    } | ConvertTo-Json -Depth 10
    exit 0
}

try {
    Assert-Condition ($sessionDirectory.StartsWith(
        $expectedSessionPrefix,
        [StringComparison]::OrdinalIgnoreCase)) `
        'BOX-R1 evidence session escaped the dedicated temporary root.'
    if (-not $NoBuild) {
        dotnet build $projectPath `
            --configuration $Configuration `
            --runtime $runtimeIdentifier `
            --no-restore `
            --nologo
        Assert-Condition ($LASTEXITCODE -eq 0) 'BOX-R1 App build failed.'
    }
    Assert-Condition (Test-Path -LiteralPath $appPath -PathType Leaf) `
        'LongGrid.App.exe was not found.'
    $liveProductProcesses = @(Get-LiveLongGridProcess)
    Assert-Condition ($liveProductProcesses.Count -eq 0) `
        'Close existing LongGrid.App processes before BOX-R1 evidence.'

    New-Item -ItemType Directory -Path $sessionDirectory -Force |
        Out-Null
    $userConfigurationBefore = Get-DirectoryMetadataFingerprint `
        $userConfigurationDirectory
    $desktopBefore = Get-DirectoryMetadataFingerprint $desktopDirectory
    [Environment]::SetEnvironmentVariable(
        $environmentName,
        $sessionId,
        [EnvironmentVariableTarget]::Process)

    Add-Type -AssemblyName System.Windows.Forms
    $workArea = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $screenX = $workArea.Left + [Math]::Min(100, [Math]::Max(0, $workArea.Width - 1))
    $screenY = $workArea.Top + [Math]::Min(100, [Math]::Max(0, $workArea.Height - 1))
    $issuedAt = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $nonce = [Guid]::NewGuid().ToString('N')
    $activationArgument = `
        "--long-grid-create-box=v1,$screenX,$screenY,$issuedAt,$nonce"

    if ($Scenario -eq 'Initial') {
        $primaryProcess = Start-Process `
            -FilePath $appPath `
            -ArgumentList $activationArgument `
            -PassThru
        $secondaryExitCode = $null
    }
    else {
        $primaryProcess = Start-Process `
            -FilePath $appPath `
            -ArgumentList '--background' `
            -PassThru
        Wait-ForPath $readyPath 15
        $secondaryProcess = Start-Process `
            -FilePath $appPath `
            -ArgumentList $activationArgument `
            -PassThru
        if ($Scenario -eq 'DuplicateRedirect') {
            $duplicateProcess = Start-Process `
                -FilePath $appPath `
                -ArgumentList $activationArgument `
                -PassThru
        }
        Assert-Condition ($secondaryProcess.WaitForExit(10000)) `
            'The redirected BOX-R1 secondary process did not exit.'
        $secondaryExitCode = $secondaryProcess.ExitCode
        if ($null -ne $duplicateProcess) {
            Assert-Condition ($duplicateProcess.WaitForExit(10000)) `
                'The duplicate BOX-R1 process did not exit.'
            $duplicateExitCode = $duplicateProcess.ExitCode
        }
        else {
            $duplicateExitCode = $null
        }
    }

    Wait-ForPath $resultPath 20
    Assert-Condition ($primaryProcess.WaitForExit(10000)) `
        'The BOX-R1 evidence primary process did not close.'
    $appResult = Get-Content -LiteralPath $resultPath -Raw |
        ConvertFrom-Json
    $userConfigurationAfter = Get-DirectoryMetadataFingerprint `
        $userConfigurationDirectory
    $desktopAfter = Get-DirectoryMetadataFingerprint $desktopDirectory
    $userConfigurationChanged = $userConfigurationBefore -ne $userConfigurationAfter
    $desktopChanged = $desktopBefore -ne $desktopAfter
    $launchPathPassed = $Scenario -eq 'Initial' `
        -or ($secondaryExitCode -eq 0 `
            -and ($Scenario -ne 'DuplicateRedirect' `
                -or $duplicateExitCode -eq 0))
    $passed = $launchPathPassed `
        -and $appResult.Outcome -eq 'Pass' `
        -and -not $userConfigurationChanged `
        -and -not $desktopChanged

    [ordered]@{
        SchemaVersion = 1
        Purpose = 'BoxR1ActivationAndCancelRealEvidence'
        Expected = [ordered]@{
            Scenario = $Scenario
            SecondaryExitCode = if ($Scenario -eq 'Initial') { $null } else { 0 }
            DuplicateExitCode = if ($Scenario -eq 'DuplicateRedirect') { 0 } else { $null }
            PreviewDrivenCount = 1
            PreviewActivatedCount = 1
            ContainerCountDifference = 0
            IsolatedConfigurationChanged = $false
            UserConfigurationChanged = $false
            DesktopMetadataChanged = $false
            RemainingLiveProcessCount = 0
        }
        Actual = [ordered]@{
            Scenario = $Scenario
            SecondaryExitCode = $secondaryExitCode
            DuplicateExitCode = $duplicateExitCode
            PreviewDrivenCount = $appResult.Actual.PreviewDrivenCount
            PreviewVisualTreeCount = $appResult.Actual.PreviewVisualTreeCount
            PreviewActivatedCount = $appResult.Actual.PreviewActivatedCount
            ContainerCountBefore = $appResult.Actual.ContainerCountBefore
            ContainerCountAfter = $appResult.Actual.ContainerCountAfter
            IsolatedConfigurationChanged = `
                $appResult.Actual.ConfigurationFingerprintChanged
            UserConfigurationChanged = $userConfigurationChanged
            DesktopMetadataChanged = $desktopChanged
            RemainingLiveProcessCount = @(Get-LiveLongGridProcess).Count
        }
        Difference = if ($passed) { 'None' } else { 'ActivationOrCancellationMismatch' }
        Outcome = if ($passed) { 'Pass' } else { 'Fail' }
    } | ConvertTo-Json -Depth 20

    Assert-Condition $passed 'BOX-R1 real activation evidence did not match.'
}
finally {
    [Environment]::SetEnvironmentVariable(
        $environmentName,
        $null,
        [EnvironmentVariableTarget]::Process)
    foreach ($process in @($duplicateProcess, $secondaryProcess, $primaryProcess)) {
        if ($null -ne $process -and -not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        if ($null -ne $process) { $process.Dispose() }
    }
    $resolvedCleanupPath = [IO.Path]::GetFullPath($sessionDirectory)
    if ($resolvedCleanupPath.StartsWith(
            $expectedSessionPrefix,
            [StringComparison]::OrdinalIgnoreCase) `
        -and (Split-Path -Leaf $resolvedCleanupPath) -eq $sessionId `
        -and (Test-Path -LiteralPath $resolvedCleanupPath)) {
        Remove-Item -LiteralPath $resolvedCleanupPath -Recurse -Force
    }
}
