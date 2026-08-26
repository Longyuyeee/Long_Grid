[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64')]
    [string]$Architecture = 'x64',

    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$commandProject = Join-Path $projectRoot `
    'src\LongGrid.ExplorerCommand\LongGrid.ExplorerCommand.vcxproj'
$probeProject = Join-Path $projectRoot `
    'tests\LongGrid.ExplorerCommand.Probe\LongGrid.ExplorerCommand.Probe.vcxproj'
$contractHeader = Join-Path $projectRoot `
    'src\LongGrid.ExplorerCommand\ExplorerCommandContract.h'
$commandSource = Join-Path $projectRoot `
    'src\LongGrid.ExplorerCommand\ExplorerCommand.cpp'
$iconSource = Join-Path $projectRoot `
    'assets\brand\rc1\sizes\png\longfangge-32.png'
$buildRoot = Join-Path $projectRoot 'artifacts\native-explorer-command'
$outputRoot = Join-Path $buildRoot 'bin'
$commandObjectRoot = Join-Path $buildRoot 'obj\command'
$probeObjectRoot = Join-Path $buildRoot 'obj\probe'
$commandDll = Join-Path $outputRoot 'LongGrid.ExplorerCommand.dll'
$probeExecutable = Join-Path $outputRoot 'LongGrid.ExplorerCommand.Probe.exe'

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Find-MSBuild {
    $command = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }

    $programFiles = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::ProgramFiles)
    $programFilesX86 = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::ProgramFilesX86)
    $vswhere = Join-Path $programFilesX86 `
        'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
        $installationPath = (& $vswhere `
            -latest `
            -products '*' `
            -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
            -property installationPath | Select-Object -First 1)
        if ($installationPath) {
            $candidate = Join-Path $installationPath `
                'MSBuild\Current\Bin\MSBuild.exe'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return $candidate
            }
        }
    }

    $fallbacks = Get-ChildItem `
        -LiteralPath (Join-Path $programFiles 'Microsoft Visual Studio\2022') `
        -Directory `
        -ErrorAction SilentlyContinue |
        ForEach-Object {
            Join-Path $_.FullName 'MSBuild\Current\Bin\MSBuild.exe'
        } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
    return $fallbacks | Select-Object -First 1
}

function New-PngBackedIcon {
    param([string]$PngPath, [string]$IconPath)
    $png = [IO.File]::ReadAllBytes($PngPath)
    $stream = [IO.File]::Open(
        $IconPath,
        [IO.FileMode]::Create,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None)
    try {
        $writer = [IO.BinaryWriter]::new($stream)
        try {
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]1)
            $writer.Write([byte]32)
            $writer.Write([byte]32)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$png.Length)
            $writer.Write([uint32]22)
            $writer.Write($png)
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

foreach ($requiredPath in @(
    $commandProject,
    $probeProject,
    $contractHeader,
    $commandSource,
    $iconSource
)) {
    Assert-Condition (Test-Path -LiteralPath $requiredPath -PathType Leaf) `
        "Native Explorer command input is missing: $requiredPath"
}

$contractCode = Get-Content -LiteralPath $contractHeader -Raw -Encoding UTF8
$commandCode = Get-Content -LiteralPath $commandSource -Raw -Encoding UTF8
foreach ($requiredContract in @(
    '78a940c1',
    'Longyuyeee.LongGrid.DeveloperPreview!LongGrid.App',
    'LongGridExplorerCommandTitle',
    '--long-grid-create-box=v1,',
    'IApplicationActivationManager',
    'GetCursorPos',
    'DllGetClassObject',
    'DllCanUnloadNow'
)) {
    Assert-Condition (
        $contractCode.Contains($requiredContract) -or
        $commandCode.Contains($requiredContract)) `
        "Native Explorer command contract is missing: $requiredContract"
}

$msbuild = Find-MSBuild
if ($ValidateOnly) {
    [ordered]@{
        SchemaVersion = 1
        Purpose = 'BoxR1ExplorerCommandBuildContract'
        Architecture = $Architecture
        Clsid = '78A940C1-2E65-4A03-9D09-3AC62CEF30BB'
        ApplicationUserModelId = `
            'Longyuyeee.LongGrid.DeveloperPreview!LongGrid.App'
        MSBuildAvailable = $null -ne $msbuild
        BuildsOrStartsProcess = $false
        ChangesPackageState = $false
        Outcome = 'Pass'
    } | ConvertTo-Json
    exit 0
}

Assert-Condition ($env:OS -eq 'Windows_NT') `
    'Native Explorer command build requires Windows.'
Assert-Condition ($null -ne $msbuild) `
    'MSBuild with the Visual C++ x64 toolset was not found.'

[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
[IO.Directory]::CreateDirectory($commandObjectRoot) | Out-Null
[IO.Directory]::CreateDirectory($probeObjectRoot) | Out-Null
New-PngBackedIcon `
    -PngPath $iconSource `
    -IconPath (Join-Path $commandObjectRoot 'LongGridExplorerCommand.ico')

& $msbuild `
    $commandProject `
    /nologo `
    /m `
    /t:Build `
    /p:Configuration=$Configuration `
    /p:Platform=$Architecture `
    "/p:OutDir=$outputRoot\" `
    "/p:IntDir=$commandObjectRoot\" |
    Out-Host
Assert-Condition ($LASTEXITCODE -eq 0) `
    'Native Explorer command DLL build failed.'

& $msbuild `
    $probeProject `
    /nologo `
    /m `
    /t:Build `
    /p:Configuration=$Configuration `
    /p:Platform=$Architecture `
    "/p:OutDir=$outputRoot\" `
    "/p:IntDir=$probeObjectRoot\" |
    Out-Host
Assert-Condition ($LASTEXITCODE -eq 0) `
    'Native Explorer command probe build failed.'
Assert-Condition (Test-Path -LiteralPath $commandDll -PathType Leaf) `
    'Native Explorer command DLL output is missing.'
Assert-Condition (Test-Path -LiteralPath $probeExecutable -PathType Leaf) `
    'Native Explorer command probe output is missing.'

[ordered]@{
    SchemaVersion = 1
    Purpose = 'BoxR1ExplorerCommandBuild'
    Architecture = $Architecture
    CommandDll = $commandDll
    ProbeExecutable = $probeExecutable
    CommandDllSha256 = (Get-FileHash `
        -LiteralPath $commandDll `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    Outcome = 'Pass'
} | ConvertTo-Json
