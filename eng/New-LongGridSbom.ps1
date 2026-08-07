[CmdletBinding()]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$PackageVersion = '0.1.0.0',

    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$PortableVersion = '0.1.0-msixdev',

    [switch]$SkipQualityGates,
    [switch]$NoRestore,
    [switch]$NoToolRestore,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $projectRoot 'artifacts'
$packageBaseName = "LongGrid-$PackageVersion-win-x64-unsigned"
$msixPath = Join-Path $artifactRoot "$packageBaseName.msix"
$buildManifestPath = Join-Path $artifactRoot "$packageBaseName.manifest.json"
$sbomPath = Join-Path $artifactRoot "$packageBaseName.spdx.json"
$sbomHashPath = "$sbomPath.sha256"
$sbomEvidencePath = Join-Path $artifactRoot "$packageBaseName.sbom-evidence.json"
$toolManifestPath = Join-Path $projectRoot '.config\dotnet-tools.json'
$buildComponentsRoot = Join-Path $projectRoot 'src'
$expectedToolVersion = '4.1.5'
$namespaceBase = 'https://github.com/Longyuyeee/Long_Grid'

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

function Find-MakeAppx {
    $command = Get-Command MakeAppx.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $programFilesX86 = [System.Environment]::GetFolderPath(
        [System.Environment+SpecialFolder]::ProgramFilesX86)
    $sdkBinRoot = Join-Path $programFilesX86 'Windows Kits\10\bin'
    if (-not (Test-Path -LiteralPath $sdkBinRoot -PathType Container)) {
        return $null
    }

    return Get-ChildItem -LiteralPath $sdkBinRoot -Directory |
        ForEach-Object { Join-Path $_.FullName 'x64\MakeAppx.exe' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Sort-Object -Descending |
        Select-Object -First 1
}

if ($env:OS -ne 'Windows_NT') {
    throw 'Long Grid SBOM generation for MSIX only supports Windows.'
}

foreach ($requiredPath in @($toolManifestPath, $buildComponentsRoot)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required SBOM input is missing: $requiredPath"
    }
}
$toolManifest = Get-Content -LiteralPath $toolManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$sbomTool = $toolManifest.tools.'microsoft.sbom.dotnettool'
if ($null -eq $sbomTool -or
    $sbomTool.version -ne $expectedToolVersion -or
    @($sbomTool.commands) -notcontains 'sbom-tool') {
    throw "Microsoft.Sbom.DotNetTool must be pinned to $expectedToolVersion."
}

$makeAppxPath = Find-MakeAppx
if ($ValidateOnly) {
    [ordered]@{
        outcome = 'Pass'
        mode = 'ValidateOnly'
        manifestFormat = 'SPDX:2.2'
        packageName = 'Long Grid'
        packageVersion = $PackageVersion
        supplier = 'Organization: Longyuyeee'
        buildComponentsScope = 'src'
        namespaceBase = $namespaceBase
        sbomToolPackage = 'Microsoft.Sbom.DotNetTool'
        sbomToolVersion = $expectedToolVersion
        makeAppxAvailable = $null -ne $makeAppxPath
        describesUnsignedMsixLayout = $true
        signed = $false
        distributionApproved = $false
    } | ConvertTo-Json
    exit 0
}

if ($null -eq $makeAppxPath) {
    throw 'MakeAppx.exe was not found. Install the Windows 11 SDK packaging tools.'
}

Push-Location $projectRoot
$stagingRoot = $null
try {
    $gitStatus = & git status --porcelain
    if ($LASTEXITCODE -ne 0 -or $gitStatus) {
        throw 'The git worktree must be clean before SBOM generation.'
    }
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') {
        throw 'Unable to resolve the source commit.'
    }

    $msixIsCurrent = $false
    if ((Test-Path -LiteralPath $msixPath -PathType Leaf) -and
        (Test-Path -LiteralPath $buildManifestPath -PathType Leaf)) {
        $buildManifest = Get-Content -LiteralPath $buildManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $actualMsixHash = (Get-FileHash -LiteralPath $msixPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $msixIsCurrent = $buildManifest.sourceCommit -eq $commit -and
            $buildManifest.packageVersion -eq $PackageVersion -and
            $buildManifest.packageType -eq 'unsigned-msix' -and
            -not $buildManifest.signed -and
            -not $buildManifest.distributionApproved -and
            $buildManifest.sha256 -eq $actualMsixHash
    }

    if (-not $msixIsCurrent) {
        $packArguments = @{
            PackageVersion = $PackageVersion
            PortableVersion = $PortableVersion
        }
        if ($SkipQualityGates) {
            $packArguments.SkipQualityGates = $true
        }
        if ($NoRestore) {
            $packArguments.NoRestore = $true
        }
        & (Join-Path $PSScriptRoot 'Pack-LongGridMsix.ps1') @packArguments
        if ($LASTEXITCODE -ne 0) {
            throw 'Unsigned MSIX prerequisite failed.'
        }
    }

    $buildManifest = Get-Content -LiteralPath $buildManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $msixHash = (Get-FileHash -LiteralPath $msixPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($buildManifest.sourceCommit -ne $commit -or
        $buildManifest.sha256 -ne $msixHash -or
        $buildManifest.signed -or
        $buildManifest.distributionApproved) {
        throw 'The unsigned MSIX and its build manifest are not bound to the current source commit.'
    }

    if (-not $NoToolRestore) {
        Invoke-CheckedCommand 'Repository-local SBOM tool restore' {
            & dotnet tool restore
        }
    }

    [System.IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
    $stagingRoot = Join-Path $artifactRoot ('.sbom-' + [System.Guid]::NewGuid().ToString('N'))
    Assert-ChildPath -Parent $artifactRoot -Child $stagingRoot
    $dropRoot = Join-Path $stagingRoot 'msix-layout'
    $validationPath = Join-Path $stagingRoot 'validation.json'
    [System.IO.Directory]::CreateDirectory($dropRoot) | Out-Null

    Invoke-CheckedCommand 'MakeAppx unpack for SBOM input' {
        & $makeAppxPath unpack /o /p $msixPath /d $dropRoot | Out-Null
    }
    if (Test-Path -LiteralPath (Join-Path $dropRoot 'AppxSignature.p7x') -PathType Leaf) {
        throw 'SBOM input unexpectedly contains a package signature.'
    }

    Invoke-CheckedCommand 'SPDX 2.2 SBOM generation' {
        & dotnet tool run sbom-tool generate `
            -b $dropRoot `
            -bc $buildComponentsRoot `
            -pn 'Long Grid' `
            -pv $PackageVersion `
            -ps 'Longyuyeee' `
            -nsb $namespaceBase `
            -mi 'SPDX:2.2'
    }

    $generatedSbomPath = Join-Path $dropRoot '_manifest\spdx_2.2\manifest.spdx.json'
    if (-not (Test-Path -LiteralPath $generatedSbomPath -PathType Leaf)) {
        throw 'The SBOM tool did not generate the expected SPDX 2.2 manifest.'
    }
    Invoke-CheckedCommand 'SPDX 2.2 SBOM validation' {
        & dotnet tool run sbom-tool validate `
            -b $dropRoot `
            -o $validationPath `
            -mi 'SPDX:2.2'
    }

    $validation = Get-Content -LiteralPath $validationPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($validation.Result -ne 'Success') {
        throw 'The Microsoft SBOM tool did not report successful validation.'
    }

    $sbom = Get-Content -LiteralPath $generatedSbomPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($sbom.spdxVersion -ne 'SPDX-2.2' -or
        $sbom.dataLicense -ne 'CC0-1.0' -or
        -not $sbom.documentNamespace.StartsWith("$namespaceBase/", [System.StringComparison]::Ordinal)) {
        throw 'The generated SBOM metadata does not match the SPDX 2.2 contract.'
    }
    $describedPackage = @($sbom.packages) | Where-Object {
        $_.name -eq 'Long Grid' -and
        $_.versionInfo -eq $PackageVersion -and
        $_.supplier -eq 'Organization: Longyuyeee'
    } | Select-Object -First 1
    if ($null -eq $describedPackage) {
        throw 'The generated SBOM does not describe the expected Long Grid package.'
    }
    $fileNames = @($sbom.files | ForEach-Object fileName)
    if ($fileNames.Count -lt 1 -or
        -not ($fileNames | Where-Object { $_ -match '(^|/)AppxManifest\.xml$' }) -or
        -not ($fileNames | Where-Object { $_ -match '(^|/)LongGrid\.App\.exe$' })) {
        throw 'The generated SBOM does not inventory the required MSIX layout files.'
    }

    Copy-Item -LiteralPath $generatedSbomPath -Destination $sbomPath -Force
    $sbomHash = (Get-FileHash -LiteralPath $sbomPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$sbomHash  $([System.IO.Path]::GetFileName($sbomPath))" |
        Set-Content -LiteralPath $sbomHashPath -Encoding ascii
    [ordered]@{
        schemaVersion = 1
        product = 'Long Grid'
        packageVersion = $PackageVersion
        sourceCommit = $commit
        subject = [ordered]@{
            file = [System.IO.Path]::GetFileName($msixPath)
            sha256 = $msixHash
            signed = $false
        }
        sbom = [ordered]@{
            file = [System.IO.Path]::GetFileName($sbomPath)
            sha256 = $sbomHash
            format = 'SPDX-2.2'
            tool = 'Microsoft.Sbom.DotNetTool'
            toolVersion = $expectedToolVersion
            validated = $true
            inventoriedFileCount = $fileNames.Count
        }
        installable = $false
        distributionApproved = $false
    } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $sbomEvidencePath -Encoding utf8

    [ordered]@{
        outcome = 'Pass'
        packageVersion = $PackageVersion
        sourceCommit = $commit
        subjectPackage = $msixPath
        subjectSha256 = $msixHash
        sbom = $sbomPath
        sbomSha256 = $sbomHash
        sbomSha256File = $sbomHashPath
        evidence = $sbomEvidencePath
        manifestFormat = 'SPDX-2.2'
        toolVersion = $expectedToolVersion
        inventoriedFileCount = $fileNames.Count
        validated = $true
        signed = $false
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
