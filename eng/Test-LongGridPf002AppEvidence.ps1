[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$NoBuild,

    [string]$CleanupSessionId
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'src\LongGrid.App\LongGrid.App.csproj'
$targetFramework = 'net8.0-windows10.0.19041.0'
$runtimeIdentifier = 'win-x64'
$outputDirectory = Join-Path $projectRoot `
    "src\LongGrid.App\bin\$Configuration\$targetFramework\$runtimeIdentifier"
$appPath = Join-Path $outputDirectory 'LongGrid.App.exe'
$sessionId = [Guid]::NewGuid().ToString('N')
$evidenceRoot = [IO.Path]::GetFullPath((Join-Path `
    ([IO.Path]::GetTempPath()) `
    'LongGridEvidence'))
$sessionDirectory = [IO.Path]::GetFullPath((Join-Path $evidenceRoot $sessionId))
$resultPath = Join-Path $sessionDirectory 'result.json'
$progressPath = Join-Path $sessionDirectory 'progress.txt'
$userConfigurationDirectory = Join-Path $env:LOCALAPPDATA 'LongGrid'
$desktopDirectory = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::DesktopDirectory)
$startedProcess = $null
$finalResult = $null
$pendingError = $null
$expectedEvidenceName = 'PF-002 ' +
    [char]0x8BC1 + [char]0x636E + [char]0x65B9 + [char]0x683C

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
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
    $payload = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
            $sha.ComputeHash($payload))).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

if ($env:OS -ne 'Windows_NT') {
    throw 'The PF-002 formal App evidence test can only run on Windows.'
}

if (-not [string]::IsNullOrWhiteSpace($CleanupSessionId)) {
    $cleanupGuid = [Guid]::Empty
    Assert-Condition (
        [Guid]::TryParseExact($CleanupSessionId, 'N', [ref]$cleanupGuid)
    ) 'CleanupSessionId must be one exact 32-character GUID.'
    $cleanupId = $cleanupGuid.ToString('N')
    $cleanupPath = [IO.Path]::GetFullPath((Join-Path $evidenceRoot $cleanupId))
    $cleanupPrefix = $evidenceRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    Assert-Condition (
        $cleanupPath.StartsWith(
            $cleanupPrefix,
            [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $cleanupPath) -eq $cleanupId
    ) 'Refused to clean a path outside the dedicated evidence root.'
    $removed = $false
    if (Test-Path -LiteralPath $cleanupPath -PathType Container) {
        $cleanupItem = Get-Item -LiteralPath $cleanupPath -Force
        Assert-Condition (
            -not (($cleanupItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
        ) 'Refused to clean a reparse-point evidence directory.'
        for ($attempt = 1; $attempt -le 20 -and -not $removed; $attempt++) {
            try {
                Remove-Item -LiteralPath $cleanupPath -Recurse -Force
                $removed = $true
            }
            catch [IO.IOException] {
                Start-Sleep -Milliseconds 250
            }
            catch [UnauthorizedAccessException] {
                Start-Sleep -Milliseconds 250
            }
        }
        Assert-Condition $removed `
            'The exact evidence directory remained locked after cleanup retries.'
    }
    Assert-Condition (-not (Test-Path -LiteralPath $cleanupPath)) `
        'The exact evidence directory still exists after cleanup.'
    [ordered]@{
        SchemaVersion = 1
        Purpose = 'Pf002ExactEvidenceCleanup'
        SessionId = $cleanupId
        Removed = $removed
        Outcome = 'Pass'
    } | ConvertTo-Json -Depth 3
    exit 0
}

$liveProcesses = @(
    Get-Process -Name 'LongGrid.App' -ErrorAction SilentlyContinue |
        Where-Object { $_.HandleCount -gt 0 }
)
Assert-Condition ($liveProcesses.Count -eq 0) `
    "PF-002 App evidence requires a clean session; found PID(s): $($liveProcesses.Id -join ', ')."

if (-not $NoBuild) {
    & dotnet build $projectPath `
        --configuration $Configuration `
        --runtime $runtimeIdentifier
    if ($LASTEXITCODE -ne 0) {
        throw "LongGrid.App build failed with exit code $LASTEXITCODE."
    }
}
Assert-Condition (Test-Path -LiteralPath $appPath -PathType Leaf) `
    "LongGrid.App executable was not found: $appPath"

$expectedPrefix = $evidenceRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
Assert-Condition (
    $sessionDirectory.StartsWith(
        $expectedPrefix,
        [StringComparison]::OrdinalIgnoreCase) -and
    (Split-Path -Leaf $sessionDirectory) -eq $sessionId
) 'The temporary evidence directory escaped its dedicated root.'

$desktopBefore = Get-DirectoryMetadataFingerprint $desktopDirectory
$userConfigurationBefore = Get-DirectoryMetadataFingerprint `
    $userConfigurationDirectory
$sessionVariable = 'LONGGRID_PF002_APP_EVIDENCE_SESSION'
$hostVariable = 'LONGGRID_ENABLE_DESKTOP_HOST'
$previousSession = [Environment]::GetEnvironmentVariable(
    $sessionVariable,
    [EnvironmentVariableTarget]::Process)
$previousHost = [Environment]::GetEnvironmentVariable(
    $hostVariable,
    [EnvironmentVariableTarget]::Process)

try {
    $null = New-Item -ItemType Directory -Path $sessionDirectory -Force
    [Environment]::SetEnvironmentVariable(
        $sessionVariable,
        $sessionId,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $hostVariable,
        '1',
        [EnvironmentVariableTarget]::Process)
    $startedProcess = Start-Process `
        -FilePath $appPath `
        -WorkingDirectory $outputDirectory `
        -PassThru
    [Environment]::SetEnvironmentVariable(
        $sessionVariable,
        $previousSession,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $hostVariable,
        $previousHost,
        [EnvironmentVariableTarget]::Process)

    $deadline = (Get-Date).AddSeconds(70)
    while ((-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) -and
        (-not $startedProcess.HasExited) -and ((Get-Date) -lt $deadline))
    {
        Start-Sleep -Milliseconds 100
        $startedProcess.Refresh()
    }

    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        $lastStage = if (Test-Path -LiteralPath $progressPath -PathType Leaf) {
            Get-Content -LiteralPath $progressPath -Raw
        }
        else {
            'NoProgressPublished'
        }
        throw "PF-002 formal App evidence timed out at finite stage '$lastStage' after 70 seconds."
    }
    Assert-Condition ($startedProcess.WaitForExit(15000)) `
        'PF-002 formal App evidence did not complete its normal shutdown drain.'
    Assert-Condition ($startedProcess.ExitCode -eq 0) `
        "Expected LongGrid.App exit code 0; actual $($startedProcess.ExitCode)."

    $appResult = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    $desktopAfter = Get-DirectoryMetadataFingerprint $desktopDirectory
    $userConfigurationAfter = Get-DirectoryMetadataFingerprint `
        $userConfigurationDirectory
    $desktopUnchanged = $desktopBefore -eq $desktopAfter
    $userConfigurationUnchanged =
        $userConfigurationBefore -eq $userConfigurationAfter
    $appActual = $appResult.Actual
    $appEvidenceChecks = [ordered]@{
        InitialContainerCount = $appActual.InitialContainerCount -eq 0
        InitialDiskStatus = $appActual.InitialDiskStatus -eq 'Missing'
        CancelContainerCount = $appActual.CancelContainerCount -eq 0
        CancelDiskStatus = $appActual.CancelDiskStatus -eq 'Missing'
        ConfirmContainerCount = $appActual.ConfirmContainerCount -eq 1
        ConfirmedName = $appActual.ConfirmedName -eq $expectedEvidenceName
        PersistedContainerCount = $appActual.PersistedContainerCount -eq 1
        PersistedDiskStatus = $appActual.PersistedDiskStatus -eq 'LoadedPrimary'
        CreateSavedRevision = $appActual.CreateSavedRevision -eq 1
        RemovalCommit = $appActual.RemovalCommit -eq 'Accepted'
        RemovedContainerCount = $appActual.RemovedContainerCount -eq 0
        RemovedPersistedContainerCount =
            $appActual.RemovedPersistedContainerCount -eq 0
        RemovedDiskStatus = $appActual.RemovedDiskStatus -eq 'LoadedPrimary'
        RemovalSavedRevision = $appActual.RemovalSavedRevision -eq 2
        LatestUndoSelection = $appActual.LatestUndoSelection -eq 'ContainerRemoval'
        LatestUndoExecuted = $appActual.LatestUndoExecuted -eq 'ContainerRemoval'
        RestoredContainerCount = $appActual.RestoredContainerCount -eq 1
        RestoredPersistedContainerCount =
            $appActual.RestoredPersistedContainerCount -eq 1
        RestoredName = $appActual.RestoredName -eq $expectedEvidenceName
        RestoredDiskStatus = $appActual.RestoredDiskStatus -eq 'LoadedPrimary'
        UndoSavedRevision = $appActual.UndoSavedRevision -eq 3
        LayoutBegin = $appActual.LayoutBegin -eq $true
        LayoutUpdate = $appActual.LayoutUpdate -eq $true
        LayoutComplete = $appActual.LayoutComplete -eq $true
        LayoutDeltaXDip = [Math]::Abs($appActual.LayoutDeltaXDip - 32) -le 1
        LayoutDeltaYDip = [Math]::Abs($appActual.LayoutDeltaYDip - 16) -le 1
        LayoutPersistedDeltaXDip =
            [Math]::Abs($appActual.LayoutPersistedDeltaXDip - 32) -le 1
        LayoutPersistedDeltaYDip =
            [Math]::Abs($appActual.LayoutPersistedDeltaYDip - 16) -le 1
        LayoutSavedRevision = $appActual.LayoutSavedRevision -eq 4
        KeyboardMoveBegin = $appActual.KeyboardMoveBegin -eq $true
        KeyboardMoveUpdate = $appActual.KeyboardMoveUpdate -eq $true
        KeyboardMoveComplete = $appActual.KeyboardMoveComplete -eq $true
        KeyboardFineMoveDeltaXDip =
            [Math]::Abs($appActual.KeyboardFineMoveDeltaXDip - 1) -le 1
        KeyboardMoveSavedRevision =
            $appActual.KeyboardMoveSavedRevision -eq 5
        KeyboardResizeBegin = $appActual.KeyboardResizeBegin -eq $true
        KeyboardResizeUpdate = $appActual.KeyboardResizeUpdate -eq $true
        KeyboardResizeComplete = $appActual.KeyboardResizeComplete -eq $true
        KeyboardLargeResizeDeltaWidthDip =
            [Math]::Abs(
                $appActual.KeyboardLargeResizeDeltaWidthDip - 8) -le 1
        KeyboardPersistedDeltaXDip =
            [Math]::Abs($appActual.KeyboardPersistedDeltaXDip - 1) -le 1
        KeyboardPersistedDeltaWidthDip =
            [Math]::Abs(
                $appActual.KeyboardPersistedDeltaWidthDip - 8) -le 1
        KeyboardLayoutSavedRevision =
            $appActual.KeyboardLayoutSavedRevision -eq 6
        CrossDisplayStatus =
            ($appActual.CrossDisplayHardwareAvailable -and
                $appActual.CrossDisplayStatus -eq 'Passed') -or
            (-not $appActual.CrossDisplayHardwareAvailable -and
                $appActual.CrossDisplayStatus -eq 'Unavailable')
        CrossDisplayBegin =
            -not $appActual.CrossDisplayHardwareAvailable -or
            $appActual.CrossDisplayBegin -eq $true
        CrossDisplayUpdate =
            -not $appActual.CrossDisplayHardwareAvailable -or
            $appActual.CrossDisplayUpdate -eq $true
        CrossDisplayComplete =
            -not $appActual.CrossDisplayHardwareAvailable -or
            $appActual.CrossDisplayComplete -eq $true
        CrossDisplayChangedDisplay =
            -not $appActual.CrossDisplayHardwareAvailable -or
            $appActual.CrossDisplayChangedDisplay -eq $true
        CrossDisplayPersistedSameDisplay =
            -not $appActual.CrossDisplayHardwareAvailable -or
            $appActual.CrossDisplayPersistedSameDisplay -eq $true
        CrossDisplayPersistedDeltaXDip =
            -not $appActual.CrossDisplayHardwareAvailable -or
            [Math]::Abs($appActual.CrossDisplayPersistedDeltaXDip) -le 1
        CrossDisplayPersistedDeltaYDip =
            -not $appActual.CrossDisplayHardwareAvailable -or
            [Math]::Abs($appActual.CrossDisplayPersistedDeltaYDip) -le 1
        CrossDisplaySavedRevision =
            ($appActual.CrossDisplayHardwareAvailable -and
                $appActual.CrossDisplaySavedRevision -eq 7) -or
            (-not $appActual.CrossDisplayHardwareAvailable -and
                $appActual.CrossDisplaySavedRevision -eq 6)
        SaveCompletion = $appActual.SaveCompletion -eq 'Completed'
        PreviewVisualTreeCount = $appActual.PreviewVisualTreeCount -eq 2
        PreviewActivatedCount = $appActual.PreviewActivatedCount -eq 0
        PreviewDrivenCount = $appActual.PreviewDrivenCount -eq 2
        VisibleInteractionStatus =
            $appActual.VisibleInteractionStatus -eq 'BlockedByKnownUpstream'
        VisibleViewPublication =
            $appActual.VisibleViewPublication -eq 'BlockedByKnownUpstream'
        DesktopFilesChanged = $appActual.DesktopFilesChanged -eq $false
        UserConfigurationChanged =
            $appActual.UserConfigurationChanged -eq $false
    }
    $failedAppEvidenceChecks = @(
        $appEvidenceChecks.GetEnumerator() |
            Where-Object { -not $_.Value } |
            ForEach-Object { $_.Key }
    )
    $appEvidenceContractMatched = $failedAppEvidenceChecks.Count -eq 0
    $passed =
        $appResult.Outcome -eq 'Pass' -and
        $appEvidenceContractMatched -and
        $desktopUnchanged -and
        $userConfigurationUnchanged
    $finalResult = [ordered]@{
        SchemaVersion = 1
        Purpose = 'Pf002AndPf003D4FormalAppEvidenceExternalVerification'
        Expected = [ordered]@{
            AppOutcome = 'Pass'
            AppEvidenceContractMatched = $true
            DesktopMetadataUnchanged = $true
            UserConfigurationUnchanged = $true
            ExitCode = 0
            TemporaryEvidenceRemoved = $true
        }
        Actual = [ordered]@{
            AppOutcome = $appResult.Outcome
            AppDifference = $appResult.Difference
            AppActual = $appResult.Actual
            AppEvidenceContractMatched = $appEvidenceContractMatched
            FailedAppEvidenceChecks = $failedAppEvidenceChecks
            DesktopMetadataUnchanged = $desktopUnchanged
            UserConfigurationUnchanged = $userConfigurationUnchanged
            ExitCode = $startedProcess.ExitCode
            TemporaryEvidenceRemoved = $true
        }
        Difference = if ($passed) { 'None' } else { 'ExternalEvidenceMismatch' }
        Outcome = if ($passed) { 'Pass' } else { 'Fail' }
    }
}
catch {
    $pendingError = $_
}
finally {
    [Environment]::SetEnvironmentVariable(
        $sessionVariable,
        $previousSession,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $hostVariable,
        $previousHost,
        [EnvironmentVariableTarget]::Process)
    if ($null -ne $startedProcess -and -not $startedProcess.HasExited)
    {
        $null = $startedProcess.CloseMainWindow()
        if (-not $startedProcess.WaitForExit(5000)) {
            Stop-Process -Id $startedProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }

    if (Test-Path -LiteralPath $sessionDirectory -PathType Container)
    {
        $resolvedCleanup = (Resolve-Path -LiteralPath $sessionDirectory).Path
        Assert-Condition (
            $resolvedCleanup.StartsWith(
                $expectedPrefix,
                [StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path -Leaf $resolvedCleanup) -eq $sessionId
        ) 'Refused to clean an unexpected evidence directory.'
        $cleanupCompleted = $false
        for ($attempt = 1; $attempt -le 20 -and -not $cleanupCompleted; $attempt++)
        {
            try {
                Remove-Item -LiteralPath $resolvedCleanup -Recurse -Force
                $cleanupCompleted = $true
            }
            catch [IO.IOException] {
                Start-Sleep -Milliseconds 250
            }
            catch [UnauthorizedAccessException] {
                Start-Sleep -Milliseconds 250
            }
        }
        Assert-Condition $cleanupCompleted `
            'The dedicated PF-002 evidence directory remained locked after cleanup retries.'
    }
}

if ($null -ne $pendingError) {
    throw $pendingError
}

$finalResult | ConvertTo-Json -Depth 8
if ($finalResult.Outcome -ne 'Pass') {
    exit 1
}
