[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$NoBuild,
    [switch]$ValidateOnly,

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
$environmentName = 'LONGGRID_UI_R1E_EVIDENCE_SESSION'
$disableHostName = 'LONGGRID_DISABLE_DESKTOP_HOST'
$evidenceRoot = [IO.Path]::GetFullPath((Join-Path `
    ([IO.Path]::GetTempPath()) `
    'LongGridUiR1eEvidence'))

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
        'UI-R1E evidence session id must be one exact 32-character GUID.'
    $normalized = $sessionGuid.ToString('N')
    $directory = [IO.Path]::GetFullPath((Join-Path $evidenceRoot $normalized))
    $prefix = $evidenceRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    Assert-Condition `
        ($directory.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path -Leaf $directory) -eq $normalized) `
        'UI-R1E evidence path escaped its dedicated temporary root.'
    return $directory
}

function Get-DirectoryMetadataFingerprint {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return 'MISSING'
    }
    $lines = @(
        Get-ChildItem -LiteralPath $Path -Force -ErrorAction Stop |
            Sort-Object Name |
            ForEach-Object {
                $length = if ($_.PSIsContainer) { -1 } else { $_.Length }
                '{0}|{1}|{2}|{3}' -f `
                    $_.Name, [int]$_.Attributes, $length, $_.LastWriteTimeUtc.Ticks
            }
    )
    $payload = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($payload))
}

if ($ValidateOnly) {
    $programCode = Get-Content (Join-Path $projectRoot 'src\LongGrid.App\Program.cs') -Raw
    $appCode = Get-Content (Join-Path $projectRoot 'src\LongGrid.App\App.xaml.cs') -Raw
    $windowCode = Get-Content (Join-Path $projectRoot 'src\LongGrid.App\MainWindow.xaml.cs') -Raw
    Assert-Condition ($programCode.Contains('ProductUiR1eEvidenceSession.ResolveInstanceKey')) `
        'UI-R1E evidence must use a unique AppInstance key.'
    Assert-Condition ($appCode.Contains('RunUiR1eEvidenceSessionAsync')) `
        'UI-R1E evidence must run inside the formal product App.'
    Assert-Condition (
        $windowCode.Contains('RenderTargetBitmap') -and
        $windowCode.Contains('SettingsAdvancedDiagnosticsExpander.IsExpanded = false')
    ) 'UI-R1E evidence must render the real XAML root with diagnostics collapsed.'
    [ordered]@{
        schemaVersion = 1
        purpose = 'UiR1eRealXamlRenderingEvidence'
        mode = 'validate-only'
        startsProcess = $false
        capturesRealProductXaml = $true
        terminatesForeignProcess = $false
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
            'Refused to clean a reparse-point UI-R1E evidence directory.'
        Remove-Item -LiteralPath $cleanupDirectory -Recurse -Force
    }
    Assert-Condition (-not (Test-Path -LiteralPath $cleanupDirectory)) `
        'The exact UI-R1E evidence directory still exists after cleanup.'
    [ordered]@{
        schemaVersion = 1
        purpose = 'UiR1eExactEvidenceCleanup'
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

$sessionId = [Guid]::NewGuid().ToString('N')
$evidenceDirectory = Resolve-EvidenceDirectory $sessionId
$resultPath = Join-Path $evidenceDirectory 'result.json'
$desktopDirectory = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::DesktopDirectory)
$configurationDirectory = Join-Path $env:LOCALAPPDATA 'LongGrid'
$desktopBefore = Get-DirectoryMetadataFingerprint $desktopDirectory
$configurationBefore = Get-DirectoryMetadataFingerprint $configurationDirectory
$previousSession = [Environment]::GetEnvironmentVariable($environmentName, 'Process')
$previousDisableHost = [Environment]::GetEnvironmentVariable($disableHostName, 'Process')
$startedProcess = $null

try {
    New-Item -ItemType Directory -Path $evidenceDirectory | Out-Null
    [Environment]::SetEnvironmentVariable($environmentName, $sessionId, 'Process')
    [Environment]::SetEnvironmentVariable($disableHostName, '1', 'Process')
    $startedProcess = Start-Process `
        -FilePath $appPath `
        -WorkingDirectory $outputDirectory `
        -PassThru
    [Environment]::SetEnvironmentVariable($environmentName, $previousSession, 'Process')
    [Environment]::SetEnvironmentVariable(
        $disableHostName,
        $previousDisableHost,
        'Process')

    $deadline = (Get-Date).AddSeconds(45)
    while (-not (Test-Path -LiteralPath $resultPath -PathType Leaf) -and
        -not $startedProcess.HasExited -and (Get-Date) -lt $deadline)
    {
        Start-Sleep -Milliseconds 100
        $startedProcess.Refresh()
    }
    Assert-Condition (Test-Path -LiteralPath $resultPath -PathType Leaf) `
        'UI-R1E evidence did not publish a result within 45 seconds.'
    Assert-Condition ($startedProcess.WaitForExit(15000)) `
        'UI-R1E evidence process did not complete its normal shutdown drain.'

    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    Assert-Condition ($result.Outcome -eq 'Pass') `
        "UI-R1E in-process evidence failed: $($result.Actual.ErrorDetail)"
    $cases = @($result.Actual.Cases)
    Assert-Condition ($cases.Count -eq 3) 'Expected three real XAML render cases.'
    Assert-Condition (
        $cases[0].LayoutStatus -eq 'wide' -and
        $cases[1].LayoutStatus -eq 'compact' -and
        $cases[2].LayoutStatus -eq 'wide'
    ) 'Real XAML responsive status did not match the requested widths.'
    Assert-Condition (
        @($cases | Where-Object {
            $_.AdvancedDiagnosticsExpanded -or
            $_.FocusedElement -ne 'ImportConfigurationButton'
        }).Count -eq 0
    ) 'Settings diagnostics or keyboard focus differed from the product expectation.'
    foreach ($case in $cases) {
        $imagePath = Join-Path $evidenceDirectory $case.ScreenshotFile
        Assert-Condition (
            (Test-Path -LiteralPath $imagePath -PathType Leaf) -and
            (Get-Item -LiteralPath $imagePath).Length -gt 0
        ) "Real XAML screenshot is missing or empty: $($case.ScreenshotFile)"
    }

    $desktopAfter = Get-DirectoryMetadataFingerprint $desktopDirectory
    $configurationAfter = Get-DirectoryMetadataFingerprint $configurationDirectory
    Assert-Condition ($desktopBefore -eq $desktopAfter) `
        'UI-R1E evidence changed desktop directory metadata.'
    Assert-Condition ($configurationBefore -eq $configurationAfter) `
        'UI-R1E evidence changed the normal LongGrid configuration directory.'

    [ordered]@{
        schemaVersion = 1
        purpose = 'UiR1eRealXamlRenderingEvidence'
        sessionId = $sessionId
        evidenceDirectory = $evidenceDirectory
        expected = $result.Expected
        actual = $result.Actual
        difference = $result.Difference
        desktopMetadataUnchanged = $true
        normalConfigurationMetadataUnchanged = $true
        foreignProcessTerminated = $false
        outcome = 'Pass'
    } | ConvertTo-Json -Depth 8
}
finally {
    [Environment]::SetEnvironmentVariable($environmentName, $previousSession, 'Process')
    [Environment]::SetEnvironmentVariable(
        $disableHostName,
        $previousDisableHost,
        'Process')
    if ($null -ne $startedProcess -and -not $startedProcess.HasExited) {
        Stop-Process -Id $startedProcess.Id -ErrorAction SilentlyContinue
    }
}
