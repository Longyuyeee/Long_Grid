[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$NoBuild,
    [switch]$ValidateOnly,
    [switch]$EnableDesktopHost,

    [string]$CleanupSessionId
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$targetFramework = 'net8.0-windows10.0.19041.0'
$runtimeIdentifier = 'win-x64'
$outputDirectory = Join-Path $projectRoot `
    "src\LongGrid.App\bin\$Configuration\$targetFramework\$runtimeIdentifier"
$appPath = Join-Path $outputDirectory 'LongGrid.App.exe'
$environmentName = 'LONGGRID_M1_MANUAL_EVIDENCE_SESSION'
$disableHostEnvironmentName = 'LONGGRID_DISABLE_DESKTOP_HOST'
$evidenceRoot = [IO.Path]::GetFullPath((Join-Path `
    ([IO.Path]::GetTempPath()) `
    'LongGridM1ManualEvidence'))

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
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

if (-not [string]::IsNullOrWhiteSpace($CleanupSessionId)) {
    $cleanupDirectory = Resolve-EvidenceDirectory $CleanupSessionId
    if (Test-Path -LiteralPath $cleanupDirectory -PathType Container) {
        $cleanupItem = Get-Item -LiteralPath $cleanupDirectory -Force
        Assert-Condition `
            (($cleanupItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) `
            'Refused to clean a reparse-point M1 manual evidence directory.'
        $markerPath = Join-Path $cleanupDirectory '.longgrid-m1-session'
        Assert-Condition `
            ((Test-Path -LiteralPath $markerPath -PathType Leaf) -and
                (Get-Content -LiteralPath $markerPath -Raw).Trim() -eq
                    $CleanupSessionId) `
            'Refused to clean an M1 directory without its exact marker.'
        Remove-Item -LiteralPath $cleanupDirectory -Recurse -Force
    }
    Assert-Condition (-not (Test-Path -LiteralPath $cleanupDirectory)) `
        'The exact M1 manual evidence directory still exists after cleanup.'
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
    & dotnet build (Join-Path $projectRoot 'src\LongGrid.App\LongGrid.App.csproj') `
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

$sessionId = [Guid]::NewGuid().ToString('N')
$evidenceDirectory = Resolve-EvidenceDirectory $sessionId
$configurationDirectory = Join-Path $evidenceDirectory 'config'
$fixtureDirectory = Join-Path $evidenceDirectory 'fixture'
$previousSession = [Environment]::GetEnvironmentVariable($environmentName, 'Process')
$previousDisableHost = [Environment]::GetEnvironmentVariable(
    $disableHostEnvironmentName,
    'Process')

New-Item -ItemType Directory -Path $configurationDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $fixtureDirectory '子目录') -Force |
    Out-Null
Set-Content `
    -LiteralPath (Join-Path $evidenceDirectory '.longgrid-m1-session') `
    -Value $sessionId `
    -NoNewline
Set-Content `
    -LiteralPath (Join-Path $fixtureDirectory '保留文件.txt') `
    -Value 'Long方格 M1 真实文件夹绑定测试；内容不得被修改。' `
    -NoNewline
Set-Content `
    -LiteralPath (Join-Path $fixtureDirectory '第二个项目.txt') `
    -Value 'Explorer Link 拖入和打开测试；内容不得被修改。' `
    -NoNewline

$expected = [ordered]@{
    schemaVersion = 1
    purpose = 'M1ManualProductJourneyEvidence'
    sessionId = $sessionId
    expected = [ordered]@{
        productWindowVisible = $true
        normalProductNavigation = @('桌面概览', '盒子管理', '个性化', '设置')
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
    desktopHostDisabledForIsolation = -not $EnableDesktopHost
    outcome = 'ReadyForPhysicalInput'
} | ConvertTo-Json
