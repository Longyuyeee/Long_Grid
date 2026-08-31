[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$NoBuild,
    [switch]$ValidateOnly,
    [switch]$EnableDesktopHost,
    [switch]$ExternalAutomation,

    [string]$CleanupSessionId
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetResolverPath = Join-Path $PSScriptRoot 'LongGrid.DotNetHost.ps1'
. $dotnetResolverPath
$targetFramework = 'net8.0-windows10.0.19041.0'
$runtimeIdentifier = 'win-x64'
$outputDirectory = Join-Path $projectRoot `
    "src\LongGrid.App\bin\$Configuration\$targetFramework\$runtimeIdentifier"
$appPath = Join-Path $outputDirectory 'LongGrid.App.exe'
$environmentName = 'LONGGRID_M1_MANUAL_EVIDENCE_SESSION'
$disableHostEnvironmentName = 'LONGGRID_DISABLE_DESKTOP_HOST'
$runtimePreflightPath = Join-Path $PSScriptRoot `
    'Test-LongGridWinUiUiaRuntime.ps1'
$evidenceRoot = [IO.Path]::GetFullPath((Join-Path `
    ([IO.Path]::GetTempPath()) `
    'LongGridM1ManualEvidence'))

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function ConvertFrom-CodePoints {
    param([int[]]$CodePoints)
    return -join @($CodePoints | ForEach-Object { [char]$_ })
}

function Resolve-EvidenceDirectory {
    param([string]$SessionId)

    $sessionGuid = [Guid]::Empty
    Assert-Condition `
        ([Guid]::TryParseExact($SessionId, 'N', [ref]$sessionGuid)) `
        'M1 manual evidence session id must be one exact 32-character GUID.'
    $normalized = $sessionGuid.ToString('N')
    $directory = [IO.Path]::GetFullPath((Join-Path $evidenceRoot $normalized))
    $prefix = $evidenceRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    Assert-Condition `
        ($directory.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path -Leaf $directory) -eq $normalized) `
        'M1 manual evidence path escaped its dedicated temporary root.'
    return $directory
}

function Assert-EvidenceRootSafe {
    if (-not (Test-Path -LiteralPath $evidenceRoot -PathType Container)) {
        return
    }
    $rootItem = Get-Item -LiteralPath $evidenceRoot -Force
    Assert-Condition `
        (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) `
        'Refused to use a reparse-point M1 manual evidence root.'
}

function Remove-EvidenceDirectory {
    param([string]$SessionId)

    Assert-EvidenceRootSafe
    $directory = Resolve-EvidenceDirectory $SessionId
    Assert-Condition `
        (Test-Path -LiteralPath $directory -PathType Container) `
        'Refused to clean a missing M1 manual evidence directory.'
    $directoryItem = Get-Item -LiteralPath $directory -Force
    Assert-Condition `
        (($directoryItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) `
        'Refused to clean a reparse-point M1 manual evidence directory.'
    $markerPath = Join-Path $directory '.longgrid-m1-session'
    Assert-Condition `
        ((Test-Path -LiteralPath $markerPath -PathType Leaf) -and
            (Get-Content -LiteralPath $markerPath -Raw).Trim() -eq
                $SessionId) `
        'Refused to clean an M1 directory without its exact marker.'
    Remove-Item -LiteralPath $directory -Recurse -Force
    Assert-Condition (-not (Test-Path -LiteralPath $directory)) `
        'The exact M1 manual evidence directory still exists after cleanup.'
}

if (-not [string]::IsNullOrWhiteSpace($CleanupSessionId) -and
    ($ValidateOnly -or $ExternalAutomation)) {
    throw $(
        'CleanupSessionId cannot be combined with ValidateOnly or ' +
        'ExternalAutomation. Run cleanup as a separate operation.')
}

if ($ValidateOnly -and $ExternalAutomation) {
    throw $(
        'ValidateOnly and ExternalAutomation are mutually exclusive. ' +
        'Run ValidateOnly for the static launcher contract, then run ' +
        'ExternalAutomation separately for the real runtime preflight.')
}

if ($ValidateOnly) {
    $programCode = Get-Content (Join-Path $projectRoot 'src\LongGrid.App\Program.cs') -Raw
    $appCode = Get-Content (Join-Path $projectRoot 'src\LongGrid.App\App.xaml.cs') -Raw
    $sessionCode = Get-Content `
        (Join-Path $projectRoot 'src\LongGrid.App\ProductM1ManualEvidenceSession.cs') `
        -Raw
    Assert-Condition `
        ($programCode.Contains('ProductM1ManualEvidenceSession.ResolveInstanceKey')) `
        'M1 manual evidence must use a unique AppInstance key.'
    Assert-Condition `
        ($appCode.Contains('m1ManualEvidenceSession?.ConfigurationDirectory')) `
        'M1 manual evidence must isolate the formal product configuration store.'
    Assert-Condition `
        ($sessionCode.Contains('MarkerFileName') -and
            $sessionCode.Contains('ReparsePoint')) `
        'M1 manual evidence must require an exact marker and reject reparse points.'
    Assert-Condition `
        ((Test-Path -LiteralPath $runtimePreflightPath -PathType Leaf) -and
            $MyInvocation.MyCommand.ScriptBlock.ToString().Contains(
                "if (`$ExternalAutomation)")) `
        'External automation must be guarded by the WinUI runtime preflight.'
    $scriptCode = Get-Content -LiteralPath $PSCommandPath -Raw
    Assert-Condition `
        ($scriptCode.Contains("'AppConstructed'") -and
            $scriptCode.Contains("'ProductWindowActivated'") -and
            $scriptCode.Contains('MainWindowTitle') -and
            $scriptCode.Contains('$productWindowReady') -and
            $scriptCode.Contains('managed product window did not become ready')) `
        'M1 manual evidence must require managed product window readiness.'
    Assert-Condition `
        ($scriptCode.Contains('Assert-EvidenceRootSafe') -and
            $scriptCode.Contains(
                'Refused to use a reparse-point M1 manual evidence root.')) `
        'M1 manual evidence must reject a reparse-point evidence root.'
    [ordered]@{
        schemaVersion = 1
        purpose = 'M1ManualProductJourneyEvidence'
        mode = 'validate-only'
        startsProcess = $false
        drivesUserInput = $false
        isolatesConfiguration = $true
        outcome = 'Pass'
    } | ConvertTo-Json
    exit 0
}

if ($ExternalAutomation) {
    $runtimePreflight = & $runtimePreflightPath | ConvertFrom-Json
    $safeForExternalAutomation = $runtimePreflight.outcome -eq 'Pass'
    if (-not $safeForExternalAutomation) {
        [ordered]@{
            schemaVersion = 1
            purpose = 'M1ManualProductJourneyLaunch'
            mode = 'external-automation'
            startsProcess = $false
            createsEvidenceSession = $false
            runtimePreflight = $runtimePreflight
            difference = $runtimePreflight.difference
            outcome = $runtimePreflight.outcome
        } | ConvertTo-Json -Depth 8
        exit 0
    }
}

if (-not [string]::IsNullOrWhiteSpace($CleanupSessionId)) {
    Remove-EvidenceDirectory $CleanupSessionId
    [ordered]@{
        schemaVersion = 1
        purpose = 'M1ManualProductJourneyCleanup'
        sessionId = $CleanupSessionId
        removed = $true
        outcome = 'Pass'
    } | ConvertTo-Json
    exit 0
}

if (-not $NoBuild) {
    $dotnetHostPath = Resolve-LongGridDotNetHost $projectRoot
    & $dotnetHostPath build (Join-Path $projectRoot 'src\LongGrid.App\LongGrid.App.csproj') `
        --configuration $Configuration `
        --runtime $runtimeIdentifier
    Assert-Condition ($LASTEXITCODE -eq 0) 'LongGrid.App build failed.'
}
Assert-Condition (Test-Path -LiteralPath $appPath -PathType Leaf) `
    "LongGrid.App executable was not found: $appPath"

if ($EnableDesktopHost) {
    $existingLongGrid = @(
        Get-Process -Name 'LongGrid.App' -ErrorAction SilentlyContinue
    )
    Assert-Condition ($existingLongGrid.Count -eq 0) `
        'DesktopHost evidence requires a dedicated account with no existing LongGrid.App process.'
}

[IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null
Assert-EvidenceRootSafe
$sessionId = [Guid]::NewGuid().ToString('N')
$evidenceDirectory = Resolve-EvidenceDirectory $sessionId
$configurationDirectory = Join-Path $evidenceDirectory 'config'
$fixtureDirectory = Join-Path $evidenceDirectory 'fixture'
$previousSession = [Environment]::GetEnvironmentVariable($environmentName, 'Process')
$previousDisableHost = [Environment]::GetEnvironmentVariable(
    $disableHostEnvironmentName,
    'Process')
$unicodeSubdirectory = ConvertFrom-CodePoints @(0x5B50, 0x76EE, 0x5F55)
$unicodeKeepFile =
    (ConvertFrom-CodePoints @(0x4FDD, 0x7559, 0x6587, 0x4EF6)) + '.txt'
$unicodeSecondFile =
    (ConvertFrom-CodePoints @(0x7B2C, 0x4E8C, 0x4E2A, 0x9879, 0x76EE)) +
    '.txt'
$navigation = @(
    (ConvertFrom-CodePoints @(0x684C, 0x9762, 0x6982, 0x89C8)),
    (ConvertFrom-CodePoints @(0x76D2, 0x5B50, 0x7BA1, 0x7406)),
    (ConvertFrom-CodePoints @(0x4E2A, 0x6027, 0x5316)),
    (ConvertFrom-CodePoints @(0x8BBE, 0x7F6E))
)

$process = $null
$markerWritten = $false
try {
    New-Item -ItemType Directory -Path $configurationDirectory -Force | Out-Null
    New-Item -ItemType Directory `
        -Path (Join-Path $fixtureDirectory $unicodeSubdirectory) `
        -Force |
        Out-Null
    Set-Content `
        -LiteralPath (Join-Path $evidenceDirectory '.longgrid-m1-session') `
        -Value $sessionId `
        -NoNewline
    $markerWritten = $true
    Set-Content `
        -LiteralPath (Join-Path $fixtureDirectory $unicodeKeepFile) `
        -Value 'Long Grid M1 folder-binding fixture; content must not change.' `
        -NoNewline
    Set-Content `
        -LiteralPath (Join-Path $fixtureDirectory $unicodeSecondFile) `
        -Value 'Explorer Link and open fixture; content must not change.' `
        -NoNewline

    $expected = [ordered]@{
        schemaVersion = 1
        purpose = 'M1ManualProductJourneyEvidence'
        sessionId = $sessionId
        expected = [ordered]@{
            productWindowVisible = $true
            normalProductNavigation = $navigation
            createBox = 'Visible physical input creates one persisted box in the control center.'
            bindFolder = 'FolderPicker binds the fixture without modifying its files.'
            refreshAndOpen = 'The box shows direct children and opens by the system handler.'
            desktopHost = if ($EnableDesktopHost) {
                'Enabled for a dedicated-account physical session.'
            }
            else {
                'Pending a dedicated account without an existing elevated LongGrid owner.'
            }
            explorerDrop = 'Requires physical input in a DesktopHost-enabled session.'
            reassignmentUndo = 'Requires physical input in a DesktopHost-enabled session.'
            fileDifference = 'None'
        }
        actual = [ordered]@{
            status = 'PendingPhysicalInput'
        }
        difference = 'PendingPhysicalInput'
    } | ConvertTo-Json -Depth 6
    Set-Content `
        -LiteralPath (Join-Path $evidenceDirectory 'journey.json') `
        -Value $expected `
        -Encoding utf8

    try {
        [Environment]::SetEnvironmentVariable($environmentName, $sessionId, 'Process')
        [Environment]::SetEnvironmentVariable(
            $disableHostEnvironmentName,
            $(if ($EnableDesktopHost) { $null } else { '1' }),
            'Process')
        $process = Start-Process `
            -FilePath $appPath `
            -WorkingDirectory $outputDirectory `
            -PassThru
        Start-Sleep -Milliseconds 750
        $process.Refresh()
    }
    finally {
        [Environment]::SetEnvironmentVariable(
            $environmentName,
            $previousSession,
            'Process')
        [Environment]::SetEnvironmentVariable(
            $disableHostEnvironmentName,
            $previousDisableHost,
            'Process')
    }

    $launchLogPath = Join-Path $evidenceDirectory 'launch.log'
    $launchDeadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    $managedLaunchReady = $false
    $productWindowActivated = $false
    $productWindowReady = $false
    $hostWindowTitle = $null
    do {
        if ($process.HasExited) { break }
        $process.Refresh()
        $hostWindowTitle = $process.MainWindowTitle
        if (-not [string]::IsNullOrWhiteSpace($hostWindowTitle) -and
            $hostWindowTitle.EndsWith(
                'This application could not be started',
                [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        if (Test-Path -LiteralPath $launchLogPath -PathType Leaf) {
            $observedStages = @(
                Get-Content -LiteralPath $launchLogPath |
                    ForEach-Object { ($_ -split '\|')[-1] }
            )
            $managedLaunchReady = 'AppConstructed' -in $observedStages
            $productWindowActivated = 'ProductWindowActivated' -in $observedStages
        }
        $productWindowReady =
            $managedLaunchReady -and
            $productWindowActivated -and
            -not [string]::IsNullOrWhiteSpace($hostWindowTitle)
        if (-not $productWindowReady) { Start-Sleep -Milliseconds 100 }
    } while (-not $productWindowReady -and
        [DateTimeOffset]::UtcNow -lt $launchDeadline)

    if (-not $productWindowReady) {
        $processExited = $process.HasExited
        $exitCode = if ($processExited) { $process.ExitCode } else { $null }
        throw $(
            'LongGrid.App managed product window did not become ready. ' +
            "ProcessExited=$processExited; ExitCode=$exitCode; " +
            "WindowTitle=$hostWindowTitle")
    }
}
catch {
    if ($null -ne $process) {
        if (-not $process.HasExited) {
            $process.Kill()
            $process.WaitForExit()
        }
        $process.Dispose()
    }
    if ($markerWritten) {
        Remove-EvidenceDirectory $sessionId
    }
    throw
}

[ordered]@{
    schemaVersion = 1
    purpose = 'M1ManualProductJourneyLaunch'
    sessionId = $sessionId
    processId = $process.Id
    processExitedDuringLaunch = $process.HasExited
    processExitCode = if ($process.HasExited) { $process.ExitCode } else { $null }
    evidenceDirectory = $evidenceDirectory
    fixtureDirectory = $fixtureDirectory
    configurationDirectory = $configurationDirectory
    expectedActualPath = Join-Path $evidenceDirectory 'journey.json'
    drivesUserInput = $false
    externalAutomation = [bool]$ExternalAutomation
    runtimePreflight = if ($ExternalAutomation) { $runtimePreflight } else { $null }
    desktopHostDisabledForIsolation = -not $EnableDesktopHost
    managedLaunchReady = $managedLaunchReady
    productWindowActivated = $productWindowActivated
    productWindowReady = $productWindowReady
    hostWindowTitle = $hostWindowTitle
    outcome = 'ReadyForPhysicalInput'
} | ConvertTo-Json
