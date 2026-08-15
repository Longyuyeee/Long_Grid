[CmdletBinding()]
param(
    [string] $EvidenceDirectory,

    [switch] $DedicatedTestAccountConfirmed,
    [switch] $PreparedAnonymousWorkspaceConfirmed,
    [switch] $RecoveryPlanConfirmed,
    [switch] $ContinuousPowerConfirmed,
    [switch] $NoAutomaticRestartConfirmed,
    [switch] $ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$minimumEvidenceFreeBytes = 1GB

function Assert-Condition {
    param(
        [bool] $Condition,
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Test-PathWithin {
    param(
        [string] $Candidate,
        [string] $Parent
    )

    $candidatePath = [System.IO.Path]::GetFullPath($Candidate).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $parentPath = [System.IO.Path]::GetFullPath($Parent).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    return $candidatePath.Equals(
        $parentPath,
        [System.StringComparison]::OrdinalIgnoreCase) -or
        $candidatePath.StartsWith(
            "$parentPath$([System.IO.Path]::DirectorySeparatorChar)",
            [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-PowerSleepPolicy {
    $output = @(& powercfg /query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE 2>$null)
    if ($LASTEXITCODE -ne 0) {
        return [pscustomobject]@{
            available = $false
            acSeconds = $null
            dcSeconds = $null
        }
    }

    $matches = [regex]::Matches(
        ($output -join "`n"),
        '0x[0-9a-fA-F]{8}')
    if ($matches.Count -lt 2) {
        return [pscustomobject]@{
            available = $false
            acSeconds = $null
            dcSeconds = $null
        }
    }

    return [pscustomobject]@{
        available = $true
        acSeconds = [Convert]::ToUInt32(
            $matches[$matches.Count - 2].Value.Substring(2),
            16)
        dcSeconds = [Convert]::ToUInt32(
            $matches[$matches.Count - 1].Value.Substring(2),
            16)
    }
}

function Test-PendingReboot {
    if (Test-Path -LiteralPath `
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') {
        return $true
    }

    if (Test-Path -LiteralPath `
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') {
        return $true
    }

    try {
        $pendingRename = Get-ItemProperty -LiteralPath `
            'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' `
            -Name PendingFileRenameOperations `
            -ErrorAction SilentlyContinue
        return $null -ne $pendingRename.PendingFileRenameOperations
    }
    catch {
        return $true
    }
}

function Get-FailureCodes {
    param([object] $Snapshot)

    $failures = [System.Collections.Generic.List[string]]::new()
    foreach ($check in @(
        @('WindowsRequired', $Snapshot.windows),
        @('InteractiveSessionRequired', $Snapshot.interactiveSession),
        @('DedicatedTestAccountConfirmationRequired',
            $Snapshot.dedicatedTestAccountConfirmed),
        @('AnonymousWorkspaceConfirmationRequired',
            $Snapshot.preparedAnonymousWorkspaceConfirmed),
        @('RecoveryPlanConfirmationRequired',
            $Snapshot.recoveryPlanConfirmed),
        @('ContinuousPowerConfirmationRequired',
            $Snapshot.continuousPowerConfirmed),
        @('NoAutomaticRestartConfirmationRequired',
            $Snapshot.noAutomaticRestartConfirmed),
        @('UserDesktopMustBeEmpty', $Snapshot.userDesktopEmpty),
        @('PublicDesktopMustBeEmpty', $Snapshot.publicDesktopEmpty),
        @('PowerPolicyMustBeReadable', $Snapshot.powerPolicyAvailable),
        @('AcSleepMustBeDisabled', $Snapshot.acSleepDisabled),
        @('DcSleepMustBeDisabled', $Snapshot.dcSleepDisabled),
        @('PendingRebootMustBeClear', $Snapshot.pendingRebootClear),
        @('EvidenceDirectoryMustExist', $Snapshot.evidenceDirectoryExists),
        @('EvidenceDirectoryMustBeEmpty', $Snapshot.evidenceDirectoryEmpty),
        @('EvidenceDirectoryMustNotBeReparsePoint',
            $Snapshot.evidenceDirectoryNotReparsePoint),
        @('EvidenceDirectoryMustBeOutsideRepository',
            $Snapshot.evidenceDirectoryOutsideRepository),
        @('EvidenceDirectoryRequiresFreeSpace',
            $Snapshot.evidenceDirectoryHasFreeSpace),
        @('LongGridProcessesMustBeZero', $Snapshot.longGridProcessesClear),
        @('RepositoryMustBeClean', $Snapshot.repositoryClean))) {
        if (-not [bool]$check[1]) {
            $failures.Add([string]$check[0])
        }
    }

    return @($failures)
}

if ($ValidateOnly) {
    $validSnapshot = [pscustomobject]@{
        windows = $true
        interactiveSession = $true
        dedicatedTestAccountConfirmed = $true
        preparedAnonymousWorkspaceConfirmed = $true
        recoveryPlanConfirmed = $true
        continuousPowerConfirmed = $true
        noAutomaticRestartConfirmed = $true
        userDesktopEmpty = $true
        publicDesktopEmpty = $true
        powerPolicyAvailable = $true
        acSleepDisabled = $true
        dcSleepDisabled = $true
        pendingRebootClear = $true
        evidenceDirectoryExists = $true
        evidenceDirectoryEmpty = $true
        evidenceDirectoryNotReparsePoint = $true
        evidenceDirectoryOutsideRepository = $true
        evidenceDirectoryHasFreeSpace = $true
        longGridProcessesClear = $true
        repositoryClean = $true
    }

    $failureCodeByProperty = [ordered]@{
        windows = 'WindowsRequired'
        interactiveSession = 'InteractiveSessionRequired'
        dedicatedTestAccountConfirmed =
            'DedicatedTestAccountConfirmationRequired'
        preparedAnonymousWorkspaceConfirmed =
            'AnonymousWorkspaceConfirmationRequired'
        recoveryPlanConfirmed = 'RecoveryPlanConfirmationRequired'
        continuousPowerConfirmed = 'ContinuousPowerConfirmationRequired'
        noAutomaticRestartConfirmed =
            'NoAutomaticRestartConfirmationRequired'
        userDesktopEmpty = 'UserDesktopMustBeEmpty'
        publicDesktopEmpty = 'PublicDesktopMustBeEmpty'
        powerPolicyAvailable = 'PowerPolicyMustBeReadable'
        acSleepDisabled = 'AcSleepMustBeDisabled'
        dcSleepDisabled = 'DcSleepMustBeDisabled'
        pendingRebootClear = 'PendingRebootMustBeClear'
        evidenceDirectoryExists = 'EvidenceDirectoryMustExist'
        evidenceDirectoryEmpty = 'EvidenceDirectoryMustBeEmpty'
        evidenceDirectoryNotReparsePoint =
            'EvidenceDirectoryMustNotBeReparsePoint'
        evidenceDirectoryOutsideRepository =
            'EvidenceDirectoryMustBeOutsideRepository'
        evidenceDirectoryHasFreeSpace =
            'EvidenceDirectoryRequiresFreeSpace'
        longGridProcessesClear = 'LongGridProcessesMustBeZero'
        repositoryClean = 'RepositoryMustBeClean'
    }
    foreach ($property in $failureCodeByProperty.Keys) {
        $caseSnapshot = $validSnapshot.PSObject.Copy()
        $caseSnapshot.PSObject.Properties[$property].Value = $false
        $caseFailures = @(Get-FailureCodes $caseSnapshot)
        Assert-Condition (
            $caseFailures.Count -eq 1 -and
            $caseFailures[0] -eq $failureCodeByProperty[$property]
        ) "The $property rejection did not map to its deterministic failure code."
    }

    $invalidSnapshot = $validSnapshot.PSObject.Copy()
    $invalidSnapshot.userDesktopEmpty = $false
    $invalidSnapshot.acSleepDisabled = $false
    $invalidFailures = @(Get-FailureCodes $invalidSnapshot)

    Assert-Condition (@(Get-FailureCodes $validSnapshot).Count -eq 0) `
        'A valid synthetic environment was rejected.'
    Assert-Condition (
        $invalidFailures.Count -eq 2 -and
        $invalidFailures -contains 'UserDesktopMustBeEmpty' -and
        $invalidFailures -contains 'AcSleepMustBeDisabled'
    ) 'An invalid synthetic environment was not rejected deterministically.'

    [ordered]@{
        schemaVersion = 1
        purpose = 'M4c2cResourceStabilityEnvironmentPreflight'
        mode = 'ValidateOnly'
        resultStatus = 'PreflightContractValidated'
        failureCodeCount = $failureCodeByProperty.Count
        evaluatesLiveEnvironment = $false
        changesAccounts = $false
        changesPowerPolicy = $false
        changesUpdatePolicy = $false
        changesDesktopFiles = $false
        startsProductOrVirtualMachine = $false
        recordsPathsNamesContentOrIdentity = $false
        canProduceM4cPass = $false
        liveEnvironmentStillPending = $true
    } | ConvertTo-Json -Depth 4
    exit 0
}

$powerPolicy = Get-PowerSleepPolicy
$userDesktop = [Environment]::GetFolderPath('Desktop')
$publicDesktop = [Environment]::GetFolderPath('CommonDesktopDirectory')
$userDesktopEntries = if (Test-Path -LiteralPath $userDesktop -PathType Container) {
    @(Get-ChildItem -LiteralPath $userDesktop -Force).Count
} else {
    -1
}
$publicDesktopEntries = if (Test-Path -LiteralPath $publicDesktop -PathType Container) {
    @(Get-ChildItem -LiteralPath $publicDesktop -Force).Count
} else {
    -1
}

$evidenceDirectoryExists = -not [string]::IsNullOrWhiteSpace(
    $EvidenceDirectory) -and
    (Test-Path -LiteralPath $EvidenceDirectory -PathType Container)
$evidenceDirectoryEmpty = $false
$evidenceDirectoryNotReparsePoint = $false
$evidenceDirectoryOutsideRepository = $false
$evidenceDirectoryHasFreeSpace = $false
if ($evidenceDirectoryExists) {
    $evidenceItem = Get-Item -LiteralPath $EvidenceDirectory
    $evidenceDirectoryEmpty = @(
        Get-ChildItem -LiteralPath $EvidenceDirectory -Force).Count -eq 0
    $evidenceDirectoryNotReparsePoint =
        -not [bool]($evidenceItem.Attributes -band
            [System.IO.FileAttributes]::ReparsePoint)
    $evidenceDirectoryOutsideRepository = -not (
        Test-PathWithin $evidenceItem.FullName $projectRoot)
    $evidenceDirectoryHasFreeSpace =
        $null -ne $evidenceItem.PSDrive -and
        $evidenceItem.PSDrive.Free -ge $minimumEvidenceFreeBytes
}

$repositoryClean = $false
$commit = 'unavailable'
if (Get-Command git -ErrorAction SilentlyContinue) {
    $candidateCommit = & git -C $projectRoot rev-parse --short=12 HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and
        -not [string]::IsNullOrWhiteSpace($candidateCommit)) {
        $commit = $candidateCommit.Trim()
    }

    $repositoryState = @(& git -C $projectRoot status --porcelain 2>$null)
    $repositoryClean = $LASTEXITCODE -eq 0 -and $repositoryState.Count -eq 0
}

$processes = @(
    Get-Process -Name @(
        'LongGrid.App',
        'LongGrid.ThumbnailWorker'
    ) -ErrorAction SilentlyContinue)
try {
    $snapshot = [pscustomobject]@{
        windows = $env:OS -eq 'Windows_NT'
        interactiveSession = [Environment]::UserInteractive
        dedicatedTestAccountConfirmed = $DedicatedTestAccountConfirmed.IsPresent
        preparedAnonymousWorkspaceConfirmed =
            $PreparedAnonymousWorkspaceConfirmed.IsPresent
        recoveryPlanConfirmed = $RecoveryPlanConfirmed.IsPresent
        continuousPowerConfirmed = $ContinuousPowerConfirmed.IsPresent
        noAutomaticRestartConfirmed = $NoAutomaticRestartConfirmed.IsPresent
        userDesktopEmpty = $userDesktopEntries -eq 0
        publicDesktopEmpty = $publicDesktopEntries -eq 0
        powerPolicyAvailable = $powerPolicy.available
        acSleepDisabled = $powerPolicy.available -and
            $powerPolicy.acSeconds -eq 0
        dcSleepDisabled = $powerPolicy.available -and
            $powerPolicy.dcSeconds -eq 0
        pendingRebootClear = -not (Test-PendingReboot)
        evidenceDirectoryExists = $evidenceDirectoryExists
        evidenceDirectoryEmpty = $evidenceDirectoryEmpty
        evidenceDirectoryNotReparsePoint =
            $evidenceDirectoryNotReparsePoint
        evidenceDirectoryOutsideRepository =
            $evidenceDirectoryOutsideRepository
        evidenceDirectoryHasFreeSpace = $evidenceDirectoryHasFreeSpace
        longGridProcessesClear = $processes.Count -eq 0
        repositoryClean = $repositoryClean
    }
    $failureCodes = @(Get-FailureCodes $snapshot)
    $ready = $failureCodes.Count -eq 0

    [ordered]@{
        schemaVersion = 1
        purpose = 'M4c2cResourceStabilityEnvironmentPreflight'
        mode = 'LiveEnvironmentReadOnly'
        commit = $commit
        resultStatus = if ($ready) {
            'ReadyForM4c2cSession'
        } else {
            'RejectedEnvironment'
        }
        canStartLiveSession = $ready
        checks = [ordered]@{
            interactiveSession = $snapshot.interactiveSession
            dedicatedTestAccountConfirmed =
                $snapshot.dedicatedTestAccountConfirmed
            preparedAnonymousWorkspaceConfirmed =
                $snapshot.preparedAnonymousWorkspaceConfirmed
            recoveryPlanConfirmed = $snapshot.recoveryPlanConfirmed
            continuousPowerConfirmed = $snapshot.continuousPowerConfirmed
            noAutomaticRestartConfirmed =
                $snapshot.noAutomaticRestartConfirmed
            userDesktopEntryCount = $userDesktopEntries
            publicDesktopEntryCount = $publicDesktopEntries
            acSleepSeconds = $powerPolicy.acSeconds
            dcSleepSeconds = $powerPolicy.dcSeconds
            pendingRebootClear = $snapshot.pendingRebootClear
            evidenceDirectoryExists = $snapshot.evidenceDirectoryExists
            evidenceDirectoryEmpty = $snapshot.evidenceDirectoryEmpty
            evidenceDirectoryNotReparsePoint =
                $snapshot.evidenceDirectoryNotReparsePoint
            evidenceDirectoryOutsideRepository =
                $snapshot.evidenceDirectoryOutsideRepository
            evidenceDirectoryHasAtLeastOneGiBFree =
                $snapshot.evidenceDirectoryHasFreeSpace
            longGridProcessCount = $processes.Count
            repositoryClean = $snapshot.repositoryClean
        }
        failureCodes = $failureCodes
        changesAccounts = $false
        changesPowerPolicy = $false
        changesUpdatePolicy = $false
        changesDesktopFiles = $false
        startsProductOrVirtualMachine = $false
        recordsPathsNamesContentProcessIdsOrIdentity = $false
        canProduceM4cPass = $false
    } | ConvertTo-Json -Depth 6

    if (-not $ready) {
        exit 2
    }
}
finally {
    foreach ($process in $processes) {
        $process.Dispose()
    }
}
