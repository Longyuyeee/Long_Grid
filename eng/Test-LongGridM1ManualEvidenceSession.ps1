[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$NoBuild,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$startScript = Join-Path $PSScriptRoot `
    'Start-LongGridM1ManualEvidenceSession.ps1'
$normalConfigurationDirectory = Join-Path $env:LOCALAPPDATA 'LongGrid'

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Get-DirectoryFingerprint {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return 'MISSING'
    }
    $lines = @(
        Get-ChildItem -LiteralPath $Path -File -Recurse -Force |
            Sort-Object FullName |
            ForEach-Object {
                $relative = [IO.Path]::GetRelativePath($Path, $_.FullName)
                $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
                "$relative|$($_.Length)|$hash"
            }
    )
    $payload = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    return [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($payload))
}

if ($ValidateOnly) {
    $validation = & $startScript -ValidateOnly | ConvertFrom-Json
    Assert-Condition ($validation.outcome -eq 'Pass') `
        'M1 manual evidence launch contract validation failed.'
    [ordered]@{
        schemaVersion = 1
        purpose = 'M1ManualProductJourneyHarnessTest'
        mode = 'validate-only'
        startsProcess = $false
        drivesUserInput = $false
        outcome = 'Pass'
    } | ConvertTo-Json
    exit 0
}

if (-not $NoBuild) {
    & dotnet build (Join-Path $projectRoot 'src\LongGrid.App\LongGrid.App.csproj') `
        --configuration $Configuration `
        --runtime win-x64 `
        --no-restore
    Assert-Condition ($LASTEXITCODE -eq 0) 'LongGrid.App build failed.'
}

$normalBefore = Get-DirectoryFingerprint $normalConfigurationDirectory
$launch = & $startScript -Configuration $Configuration -NoBuild |
    ConvertFrom-Json
$processId = [int]$launch.processId
$process = $null
$result = $null

try {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    $launchLog = Join-Path $launch.evidenceDirectory 'launch.log'
    do {
        Start-Sleep -Milliseconds 100
        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        $stages = if (Test-Path -LiteralPath $launchLog -PathType Leaf) {
            @(Get-Content -LiteralPath $launchLog)
        }
        else {
            @()
        }
    } while (
        $process -and
        $stages.Count -lt 4 -and
        [DateTimeOffset]::UtcNow -lt $deadline)

    $fixtureBefore = Get-DirectoryFingerprint $launch.fixtureDirectory
    Start-Sleep -Milliseconds 500
    $fixtureAfter = Get-DirectoryFingerprint $launch.fixtureDirectory
    $normalAfter = Get-DirectoryFingerprint $normalConfigurationDirectory
    $requiredStages = @(
        'InstanceKeyResolved',
        'AppInstanceCurrent',
        'ConfigurationIsolationAccepted',
        'AppConstructed'
    )
    $observedStages = @(
        $stages | ForEach-Object { ($_ -split '\|')[-1] }
    )
    $missingStages = @(
        $requiredStages | Where-Object { $_ -notin $observedStages }
    )
    $configurationPrefix =
        ([IO.Path]::GetFullPath($launch.evidenceDirectory).TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar) +
            [IO.Path]::DirectorySeparatorChar)
    $configurationPath = [IO.Path]::GetFullPath($launch.configurationDirectory)

    $actual = [ordered]@{
        processAliveAfterLaunch = $null -ne $process
        processId = $processId
        observedLaunchStages = $observedStages
        missingLaunchStages = $missingStages
        configurationIsolated = $configurationPath.StartsWith(
            $configurationPrefix,
            [StringComparison]::OrdinalIgnoreCase)
        normalConfigurationFingerprintBefore = $normalBefore
        normalConfigurationFingerprintAfter = $normalAfter
        fixtureFingerprintBefore = $fixtureBefore
        fixtureFingerprintAfter = $fixtureAfter
        physicalInput = 'NotPerformedByHarnessProbe'
    }
    $difference = if (-not $actual.processAliveAfterLaunch) {
        'ProductProcessExitedDuringLaunch'
    }
    elseif ($missingStages.Count -ne 0) {
        'LaunchStageMismatch'
    }
    elseif (-not $actual.configurationIsolated) {
        'ConfigurationIsolationMismatch'
    }
    elseif ($normalBefore -ne $normalAfter) {
        'NormalConfigurationChanged'
    }
    elseif ($fixtureBefore -ne $fixtureAfter) {
        'FixtureChanged'
    }
    else {
        'None'
    }

    $journeyPath = [string]$launch.expectedActualPath
    $journey = Get-Content -LiteralPath $journeyPath -Raw | ConvertFrom-Json
    $journey.actual = $actual
    $journey.difference = $difference
    $journey | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $journeyPath -Encoding utf8

    $result = [ordered]@{
        schemaVersion = 1
        purpose = 'M1ManualProductJourneyHarnessTest'
        sessionId = $launch.sessionId
        expected = [ordered]@{
            processAliveAfterLaunch = $true
            requiredLaunchStages = $requiredStages
            configurationIsolated = $true
            normalConfigurationUnchanged = $true
            fixtureUnchanged = $true
            physicalInput = 'PendingSeparateComputerUseSession'
        }
        actual = $actual
        difference = $difference
        evidencePath = $journeyPath
        outcome = if ($difference -eq 'None') { 'Pass' } else { 'Fail' }
    }
}
finally {
    $ownedProcess = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($ownedProcess -and $ownedProcess.ProcessName -eq 'LongGrid.App') {
        Stop-Process -Id $processId
        Wait-Process -Id $processId -Timeout 5 -ErrorAction SilentlyContinue
    }
}

$result | ConvertTo-Json -Depth 8
Assert-Condition ($result.outcome -eq 'Pass') `
    "M1 manual product journey harness difference: $($result.difference)"
