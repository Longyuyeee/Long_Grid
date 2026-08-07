[CmdletBinding()]
param(
    [ValidateSet('x64')]
    [string]$Architecture = 'x64',

    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0-dev',

    [switch]$SkipQualityGates,
    [switch]$NoRestore,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $projectRoot 'LongGrid.sln'
$projectPath = Join-Path $projectRoot 'src\LongGrid.App\LongGrid.App.csproj'
$artifactRoot = Join-Path $projectRoot 'artifacts'
$runtimeIdentifier = "win-$Architecture"
$packageBaseName = "LongGrid-$Version-$runtimeIdentifier"
$zipPath = Join-Path $artifactRoot "$packageBaseName.zip"
$zipHashPath = "$zipPath.sha256"
$fixedArchiveTime = [System.DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [System.TimeSpan]::Zero)

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)]
        [string]$Description,

        [Parameter(Mandatory)]
        [scriptblock]$Command
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory)]
        [string]$Parent,

        [Parameter(Mandatory)]
        [string]$Child
    )

    $parentPath = [System.IO.Path]::GetFullPath($Parent).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $childPath = [System.IO.Path]::GetFullPath($Child)
    $prefix = $parentPath + [System.IO.Path]::DirectorySeparatorChar

    if (-not $childPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path must remain under ${parentPath}: $childPath"
    }
}

function Get-RelativePackagePath {
    param(
        [Parameter(Mandatory)]
        [string]$Root,

        [Parameter(Mandatory)]
        [string]$Path
    )

    return [System.IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
}

function New-DeterministicZip {
    param(
        [Parameter(Mandatory)]
        [string]$SourceDirectory,

        [Parameter(Mandatory)]
        [string]$DestinationPath
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $destinationDirectory = Split-Path -Parent $DestinationPath
    [System.IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null
    $stream = [System.IO.File]::Open($DestinationPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $stream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $false,
            [System.Text.Encoding]::UTF8)
        try {
            $files = Get-ChildItem -LiteralPath $SourceDirectory -File -Recurse |
                Sort-Object { Get-RelativePackagePath -Root $SourceDirectory -Path $_.FullName }
            foreach ($file in $files) {
                $relativePath = Get-RelativePackagePath -Root $SourceDirectory -Path $file.FullName
                $entry = $archive.CreateEntry("LongGrid/$relativePath", [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedArchiveTime
                $entryStream = $entry.Open()
                try {
                    $fileStream = $file.OpenRead()
                    try {
                        $fileStream.CopyTo($entryStream)
                    }
                    finally {
                        $fileStream.Dispose()
                    }
                }
                finally {
                    $entryStream.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Test-ZipPackage {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entryNames = @($archive.Entries | ForEach-Object FullName)
        if ($entryNames.Count -eq 0) {
            throw 'The portable archive is empty.'
        }

        foreach ($entryName in $entryNames) {
            if (-not $entryName.StartsWith('LongGrid/', [System.StringComparison]::Ordinal) -or
                $entryName.StartsWith('/', [System.StringComparison]::Ordinal) -or
                $entryName -match '(^|/)\.\.(/|$)' -or
                $entryName.Contains('\')) {
                throw "Unsafe archive entry: $entryName"
            }
        }

        foreach ($requiredEntry in @(
            'LongGrid/LongGrid.App.exe',
            'LongGrid/Install-Preflight.ps1',
            'LongGrid/PORTABLE-README.txt',
            'LongGrid/artifact-manifest.json',
            'LongGrid/SHA256SUMS.txt'
        )) {
            if ($entryNames -notcontains $requiredEntry) {
                throw "Required archive entry is missing: $requiredEntry"
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

if ($env:OS -ne 'Windows_NT') {
    throw 'Long Grid packaging only supports Windows.'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet was not found. Install the .NET SDK selected by global.json.'
}

foreach ($requiredPath in @(
    $solutionPath,
    $projectPath,
    (Join-Path $projectRoot 'packaging\Install-Preflight.ps1'),
    (Join-Path $projectRoot 'packaging\PORTABLE-README.txt')
)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required packaging input is missing: $requiredPath"
    }
}

$projectXml = Get-Content -LiteralPath $projectPath -Raw
foreach ($requiredContract in @(
    '<WindowsPackageType>None</WindowsPackageType>',
    '<RuntimeIdentifier>win-x64</RuntimeIdentifier>',
    '<PlatformTarget>x64</PlatformTarget>'
)) {
    if ($projectXml.IndexOf($requiredContract, [System.StringComparison]::Ordinal) -lt 0) {
        throw "LongGrid.App publish contract is missing: $requiredContract"
    }
}

if ($ValidateOnly) {
    [ordered]@{
        outcome = 'Pass'
        mode = 'ValidateOnly'
        packageType = 'portable-unpacked-zip'
        runtimeIdentifier = $runtimeIdentifier
        signed = $false
        installer = $false
        distributionApproved = $false
    } | ConvertTo-Json
    exit 0
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'git was not found. A committed source revision is required for traceable packaging.'
}

Push-Location $projectRoot
$stagingRoot = $null
try {
    $gitStatus = & git status --porcelain
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect the git worktree.'
    }
    if ($gitStatus) {
        throw 'The git worktree must be clean before packaging so the artifact maps to one committed revision.'
    }

    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') {
        throw 'Unable to resolve the source commit.'
    }

    if (-not $SkipQualityGates) {
        if (-not $NoRestore) {
            Invoke-CheckedCommand 'Locked restore' { & dotnet restore $solutionPath --locked-mode }
        }
        Invoke-CheckedCommand 'Format verification' { & dotnet format $solutionPath --verify-no-changes --no-restore }
        Invoke-CheckedCommand 'Release build' { & dotnet build $solutionPath --configuration Release --no-restore }
        Invoke-CheckedCommand 'Release tests' {
            & dotnet test $solutionPath --configuration Release --no-build --logger 'trx;LogFileName=pack-tests.trx' --collect 'XPlat Code Coverage' --results-directory TestResults
        }
        & (Join-Path $PSScriptRoot 'Verify-Coverage.ps1') -MinimumLineRate 0.90 -MinimumBranchRate 0.75
        & (Join-Path $PSScriptRoot 'Verify-VulnerablePackages.ps1')
    }
    elseif (-not $NoRestore) {
        Invoke-CheckedCommand 'Locked restore' { & dotnet restore $solutionPath --locked-mode }
    }

    [System.IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
    $stagingRoot = Join-Path $artifactRoot ('.pack-' + [System.Guid]::NewGuid().ToString('N'))
    $publishRoot = Join-Path $stagingRoot 'LongGrid'
    Assert-ChildPath -Parent $artifactRoot -Child $stagingRoot
    [System.IO.Directory]::CreateDirectory($publishRoot) | Out-Null

    $publishArguments = @(
        'publish', $projectPath,
        '--configuration', 'Release',
        '--runtime', $runtimeIdentifier,
        '--self-contained', 'true',
        '--output', $publishRoot,
        '-p:WindowsAppSDKSelfContained=true',
        ("-p:Version=$Version"),
        '--no-restore'
    )
    Invoke-CheckedCommand 'Self-contained publish' { & dotnet @publishArguments }

    Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging\Install-Preflight.ps1') -Destination $publishRoot
    Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging\PORTABLE-README.txt') -Destination $publishRoot

    $artifactManifest = [ordered]@{
        schemaVersion = 1
        product = 'Long Grid'
        displayName = 'Long方格'
        version = $Version
        sourceCommit = $commit
        configuration = 'Release'
        runtimeIdentifier = $runtimeIdentifier
        packageType = 'portable-unpacked-zip'
        channel = 'DeveloperPreview'
        dotNetSelfContained = $true
        windowsAppSdkSelfContained = $true
        signed = $false
        installer = $false
        distributionApproved = $false
        licenseStatus = 'Deferred'
        desktopHostExecutionEnabled = $false
    }
    $artifactManifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $publishRoot 'artifact-manifest.json') -Encoding utf8

    $hashLines = Get-ChildItem -LiteralPath $publishRoot -File -Recurse |
        Where-Object Name -ne 'SHA256SUMS.txt' |
        Sort-Object { Get-RelativePackagePath -Root $publishRoot -Path $_.FullName } |
        ForEach-Object {
            $relativePath = Get-RelativePackagePath -Root $publishRoot -Path $_.FullName
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $relativePath"
        }
    $hashLines | Set-Content -LiteralPath (Join-Path $publishRoot 'SHA256SUMS.txt') -Encoding ascii

    foreach ($requiredFile in @('LongGrid.App.exe', 'Assets\LongFangGe.png')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishRoot $requiredFile) -PathType Leaf)) {
            throw "Published payload is missing: $requiredFile"
        }
    }

    New-DeterministicZip -SourceDirectory $publishRoot -DestinationPath $zipPath
    $comparisonZipPath = Join-Path $stagingRoot "$packageBaseName.comparison.zip"
    New-DeterministicZip -SourceDirectory $publishRoot -DestinationPath $comparisonZipPath
    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $comparisonHash = (Get-FileHash -LiteralPath $comparisonZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($zipHash -ne $comparisonHash) {
        throw 'Deterministic archive verification failed: identical payloads produced different ZIP hashes.'
    }

    Test-ZipPackage -Path $zipPath
    "$zipHash  $([System.IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath $zipHashPath -Encoding ascii

    [ordered]@{
        outcome = 'Pass'
        version = $Version
        sourceCommit = $commit
        runtimeIdentifier = $runtimeIdentifier
        packageType = 'portable-unpacked-zip'
        archive = $zipPath
        sha256 = $zipHash
        sha256File = $zipHashPath
        payloadFiles = $hashLines.Count + 1
        deterministicArchive = $true
        dotNetSelfContained = $true
        windowsAppSdkSelfContained = $true
        signed = $false
        installer = $false
        distributionApproved = $false
    } | ConvertTo-Json -Depth 4
}
finally {
    if ($null -ne $stagingRoot -and (Test-Path -LiteralPath $stagingRoot)) {
        Assert-ChildPath -Parent $artifactRoot -Child $stagingRoot
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
    Pop-Location
}
