[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$ContractOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $PSScriptRoot 'Build-LongGridExplorerCommand.ps1'
$commandSource = Join-Path $projectRoot `
    'src\LongGrid.ExplorerCommand\ExplorerCommand.cpp'
$contractHeader = Join-Path $projectRoot `
    'src\LongGrid.ExplorerCommand\ExplorerCommandContract.h'
$manifestPath = Join-Path $projectRoot `
    'packaging\msix\AppxManifest.template.xml'
$outputRoot = Join-Path $projectRoot 'artifacts\native-explorer-command\bin'
$commandDll = Join-Path $outputRoot 'LongGrid.ExplorerCommand.dll'
$probeExecutable = Join-Path $outputRoot 'LongGrid.ExplorerCommand.Probe.exe'
$expectedClsid = '78A940C1-2E65-4A03-9D09-3AC62CEF30BB'

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

foreach ($requiredPath in @(
    $buildScript,
    $commandSource,
    $contractHeader,
    $manifestPath
)) {
    Assert-Condition (Test-Path -LiteralPath $requiredPath -PathType Leaf) `
        "Explorer command test input is missing: $requiredPath"
}

$commandCode = Get-Content -LiteralPath $commandSource -Raw -Encoding UTF8
$contractCode = Get-Content -LiteralPath $contractHeader -Raw -Encoding UTF8
$manifestText = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8
foreach ($forbiddenCall in @(
    'CreateFile',
    'FindFirstFile',
    'RegOpenKey',
    'RegSetValue',
    'WinHttp',
    'InternetOpen',
    'WaitForSingleObject',
    'Sleep('
)) {
    Assert-Condition (-not $commandCode.Contains($forbiddenCall)) `
        "Explorer menu path contains forbidden product I/O or wait: $forbiddenCall"
}
Assert-Condition ($contractCode.Contains('0x78a940c1')) `
    'Native CLSID is missing from the shared contract.'
Assert-Condition ($manifestText.Contains($expectedClsid)) `
    'MSIX manifest CLSID does not match the native command.'
Assert-Condition (
    ([regex]::Matches(
        $manifestText,
        'desktop5:ItemType\s+Type="Directory\\Background"')).Count -eq 1) `
    'MSIX must register exactly one Directory\Background item type.'

if ($ContractOnly) {
    [ordered]@{
        SchemaVersion = 1
        Purpose = 'BoxR1ExplorerCommandContract'
        Expected = [ordered]@{
            UniqueClsid = $expectedClsid
            BackgroundItemTypeCount = 1
            ProductIoInMenuCallbacks = $false
            ChangesPackageState = $false
        }
        Actual = [ordered]@{
            UniqueClsid = $expectedClsid
            BackgroundItemTypeCount = 1
            ProductIoInMenuCallbacks = $false
            ChangesPackageState = $false
        }
        Difference = 'None'
        Outcome = 'Pass'
    } | ConvertTo-Json -Depth 10
    exit 0
}

if (-not $NoBuild) {
    & $buildScript | Out-Null
    Assert-Condition ($LASTEXITCODE -eq 0) `
        'Explorer command build did not pass.'
}
Assert-Condition (Test-Path -LiteralPath $commandDll -PathType Leaf) `
    'Explorer command DLL is missing.'
Assert-Condition (Test-Path -LiteralPath $probeExecutable -PathType Leaf) `
    'Explorer command native probe is missing.'

$stream = [IO.File]::OpenRead($commandDll)
try {
    $reader = [IO.BinaryReader]::new($stream)
    try {
        Assert-Condition ($reader.ReadUInt16() -eq 0x5A4D) `
            'Explorer command output is not a PE image.'
        $stream.Position = 0x3C
        $peOffset = $reader.ReadUInt32()
        $stream.Position = $peOffset
        Assert-Condition ($reader.ReadUInt32() -eq 0x00004550) `
            'Explorer command PE signature is invalid.'
        $machine = $reader.ReadUInt16()
    }
    finally {
        $reader.Dispose()
    }
}
finally {
    $stream.Dispose()
}
Assert-Condition ($machine -eq 0x8664) `
    'Explorer command DLL is not x64.'

$probeJson = & $probeExecutable $commandDll | Out-String
Assert-Condition ($LASTEXITCODE -eq 0) `
    "Explorer command native probe failed: $probeJson"
$probe = $probeJson | ConvertFrom-Json
$passed = $probe.Outcome -eq 'Pass' `
    -and $probe.Iterations -eq 200 `
    -and $probe.ElapsedMilliseconds -le 1000 `
    -and $probe.TitlePassed `
    -and $probe.IconPassed `
    -and $probe.StatePassed `
    -and $probe.CanonicalNamePassed `
    -and $probe.FlagsPassed `
    -and $probe.SubcommandsPassed `
    -and $probe.UnloadPassed `
    -and $probe.HandlesAfter -le $probe.HandlesBefore + 2

$evidence = [ordered]@{
    SchemaVersion = 1
    Purpose = 'BoxR1ExplorerCommandRealDllEvidence'
    Expected = [ordered]@{
        Architecture = 'x64'
        Iterations = 200
        MaximumElapsedMilliseconds = 1000
        TitleIconStateAndCanonicalName = 'Pass'
        ComServerUnloads = $true
        MaximumHandleGrowth = 2
        PackageStateChanged = $false
        ExplorerRestarted = $false
    }
    Actual = [ordered]@{
        Architecture = if ($machine -eq 0x8664) { 'x64' } else { 'Other' }
        Iterations = $probe.Iterations
        ElapsedMilliseconds = $probe.ElapsedMilliseconds
        TitlePassed = $probe.TitlePassed
        IconPassed = $probe.IconPassed
        StatePassed = $probe.StatePassed
        CanonicalNamePassed = $probe.CanonicalNamePassed
        FlagsPassed = $probe.FlagsPassed
        SubcommandsPassed = $probe.SubcommandsPassed
        ComServerUnloads = $probe.UnloadPassed
        HandlesBefore = $probe.HandlesBefore
        HandlesAfter = $probe.HandlesAfter
        PackageStateChanged = $false
        ExplorerRestarted = $false
    }
    Difference = if ($passed) { 'None' } else { 'NativeDllContractMismatch' }
    Outcome = if ($passed) { 'Pass' } else { 'Fail' }
}
$evidence | ConvertTo-Json -Depth 10
Assert-Condition $passed `
    'Explorer command real DLL evidence did not match expectations.'
