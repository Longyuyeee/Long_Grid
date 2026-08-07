[CmdletBinding()]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$PackageVersion = '0.1.0.0',

    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$PortableVersion = '0.1.0-msixdev',

    [switch]$SkipQualityGates,
    [switch]$NoRestore,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $projectRoot 'artifacts'
$portableZipPath = Join-Path $artifactRoot "LongGrid-$PortableVersion-win-x64.zip"
$manifestTemplatePath = Join-Path $projectRoot 'packaging\msix\AppxManifest.template.xml'
$msixReadmePath = Join-Path $projectRoot 'packaging\msix\MSIX-README.txt'
$logoSourcePath = Join-Path $projectRoot 'assets\brand\rc1\sizes\png\longfangge-256.png'
$packageBaseName = "LongGrid-$PackageVersion-win-x64-unsigned"
$msixPath = Join-Path $artifactRoot "$packageBaseName.msix"
$msixHashPath = "$msixPath.sha256"
$buildManifestPath = Join-Path $artifactRoot "$packageBaseName.manifest.json"

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

    $candidates = Get-ChildItem -LiteralPath $sdkBinRoot -Directory |
        ForEach-Object { Join-Path $_.FullName 'x64\MakeAppx.exe' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Sort-Object -Descending
    return $candidates | Select-Object -First 1
}

function Get-PortableArtifactManifest {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry('LongGrid/artifact-manifest.json')
        if ($null -eq $entry) {
            throw 'Portable package does not contain artifact-manifest.json.'
        }

        $stream = $entry.Open()
        try {
            $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8)
            try {
                return $reader.ReadToEnd() | ConvertFrom-Json
            }
            finally {
                $reader.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function New-MsixLogo {
    param(
        [Parameter(Mandatory)]
        [string]$SourcePath,

        [Parameter(Mandatory)]
        [string]$DestinationPath,

        [Parameter(Mandatory)]
        [int]$Size
    )

    Add-Type -AssemblyName System.Drawing
    $source = [System.Drawing.Image]::FromFile($SourcePath)
    try {
        $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $bitmap.SetResolution(96, 96)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage($source, 0, 0, $Size, $Size)
            }
            finally {
                $graphics.Dispose()
            }

            $bitmap.Save($DestinationPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $bitmap.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }
}

function Get-LayoutFingerprint {
    param(
        [Parameter(Mandatory)]
        [string]$LayoutRoot
    )

    $rootPath = [System.IO.Path]::GetFullPath($LayoutRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $lines = Get-ChildItem -LiteralPath $LayoutRoot -File -Recurse |
        ForEach-Object {
            $filePath = [System.IO.Path]::GetFullPath($_.FullName)
            if (-not $filePath.StartsWith($rootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Verified layout file escaped ${rootPath}: $filePath"
            }

            $relativePath = $filePath.Substring($rootPath.Length).Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $relativePath"
        } |
        Sort-Object
    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ($algorithm.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join ''
    }
    finally {
        $algorithm.Dispose()
    }
}

function Test-MsixLayout {
    param(
        [Parameter(Mandatory)]
        [string]$LayoutRoot,

        [Parameter(Mandatory)]
        [string]$ExpectedVersion
    )

    foreach ($requiredFile in @(
        'AppxManifest.xml',
        'AppxBlockMap.xml',
        'LongGrid.App.exe',
        'Assets\Square44x44Logo.png',
        'Assets\Square150x150Logo.png',
        'Assets\StoreLogo.png',
        'MSIX-README.txt'
    )) {
        if (-not (Test-Path -LiteralPath (Join-Path $LayoutRoot $requiredFile) -PathType Leaf)) {
            throw "Verified MSIX layout is missing: $requiredFile"
        }
    }

    if (Test-Path -LiteralPath (Join-Path $LayoutRoot 'AppxSignature.p7x') -PathType Leaf) {
        throw 'Unsigned Developer Preview unexpectedly contains AppxSignature.p7x.'
    }

    [xml]$manifest = Get-Content -LiteralPath (Join-Path $LayoutRoot 'AppxManifest.xml') -Raw
    $namespace = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
    $namespace.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $namespace.AddNamespace('rescap', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities')
    $identity = $manifest.SelectSingleNode('/f:Package/f:Identity', $namespace)
    if ($null -eq $identity -or
        $identity.Name -ne 'Longyuyeee.LongGrid.DeveloperPreview' -or
        $identity.Publisher -ne 'CN=LongGrid Development' -or
        $identity.Version -ne $ExpectedVersion -or
        $identity.ProcessorArchitecture -ne 'x64') {
        throw 'MSIX identity does not match the Developer Preview contract.'
    }

    $capabilities = @($manifest.SelectNodes('/f:Package/f:Capabilities/*', $namespace))
    if ($capabilities.Count -ne 1 -or $capabilities[0].Name -ne 'runFullTrust') {
        throw 'MSIX must declare exactly the runFullTrust capability.'
    }
}

function Test-MsixContainerEntries {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entryNames = @($archive.Entries | ForEach-Object FullName)
        foreach ($requiredEntry in @(
            '[Content_Types].xml',
            'AppxManifest.xml',
            'AppxBlockMap.xml'
        )) {
            if ($entryNames -notcontains $requiredEntry) {
                throw "MSIX container entry is missing: $requiredEntry"
            }
        }

        if ($entryNames -contains 'AppxSignature.p7x') {
            throw 'Unsigned Developer Preview unexpectedly contains AppxSignature.p7x.'
        }
    }
    finally {
        $archive.Dispose()
    }
}

if ($env:OS -ne 'Windows_NT') {
    throw 'Long Grid MSIX packaging only supports Windows.'
}

foreach ($requiredPath in @($manifestTemplatePath, $msixReadmePath, $logoSourcePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required MSIX input is missing: $requiredPath"
    }
}

$manifestTemplate = Get-Content -LiteralPath $manifestTemplatePath -Raw -Encoding UTF8
[xml]$manifestContract = $manifestTemplate.Replace('{{PACKAGE_VERSION}}', $PackageVersion)
if ($manifestTemplate.IndexOf('CN=LongGrid Development', [System.StringComparison]::Ordinal) -lt 0 -or
    $manifestTemplate.IndexOf('Longyuyeee.LongGrid.DeveloperPreview', [System.StringComparison]::Ordinal) -lt 0 -or
    $manifestTemplate.IndexOf('runFullTrust', [System.StringComparison]::Ordinal) -lt 0) {
    throw 'MSIX manifest template is missing the approved Developer Preview identity contract.'
}

$makeAppxPath = Find-MakeAppx
if ($ValidateOnly) {
    [ordered]@{
        outcome = 'Pass'
        mode = 'ValidateOnly'
        packageType = 'unsigned-msix'
        identityName = 'Longyuyeee.LongGrid.DeveloperPreview'
        publisher = 'CN=LongGrid Development'
        packageVersion = $PackageVersion
        makeAppxAvailable = $null -ne $makeAppxPath
        signed = $false
        installable = $false
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
        throw 'The git worktree must be clean before MSIX packaging.'
    }
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') {
        throw 'Unable to resolve the source commit.'
    }

    $portableIsCurrent = $false
    if (Test-Path -LiteralPath $portableZipPath -PathType Leaf) {
        $portableManifest = Get-PortableArtifactManifest -Path $portableZipPath
        $portableIsCurrent = $portableManifest.sourceCommit -eq $commit -and
            $portableManifest.version -eq $PortableVersion -and
            $portableManifest.runtimeIdentifier -eq 'win-x64' -and
            $portableManifest.dotNetSelfContained -and
            $portableManifest.windowsAppSdkSelfContained
    }

    if (-not $portableIsCurrent) {
        $packArguments = @{
            Version = $PortableVersion
        }
        if ($SkipQualityGates) {
            $packArguments.SkipQualityGates = $true
        }
        if ($NoRestore) {
            $packArguments.NoRestore = $true
        }
        & (Join-Path $PSScriptRoot 'Pack-LongGrid.ps1') @packArguments
        if ($LASTEXITCODE -ne 0) {
            throw 'Portable package prerequisite failed.'
        }
    }

    $portableManifest = Get-PortableArtifactManifest -Path $portableZipPath
    if ($portableManifest.sourceCommit -ne $commit) {
        throw 'Portable package does not match the current source commit.'
    }

    [System.IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
    $stagingRoot = Join-Path $artifactRoot ('.msix-' + [System.Guid]::NewGuid().ToString('N'))
    Assert-ChildPath -Parent $artifactRoot -Child $stagingRoot
    $expandedRoot = Join-Path $stagingRoot 'expanded'
    $layoutRoot = Join-Path $expandedRoot 'LongGrid'
    $verificationRoot = Join-Path $stagingRoot 'verified'
    $comparisonVerificationRoot = Join-Path $stagingRoot 'comparison-verified'
    [System.IO.Directory]::CreateDirectory($expandedRoot) | Out-Null
    Expand-Archive -LiteralPath $portableZipPath -DestinationPath $expandedRoot

    foreach ($portableOnlyFile in @(
        'Install-Preflight.ps1',
        'PORTABLE-README.txt',
        'artifact-manifest.json',
        'SHA256SUMS.txt'
    )) {
        $portableOnlyPath = Join-Path $layoutRoot $portableOnlyFile
        if (Test-Path -LiteralPath $portableOnlyPath -PathType Leaf) {
            [System.IO.File]::Delete($portableOnlyPath)
        }
    }

    $assetsRoot = Join-Path $layoutRoot 'Assets'
    [System.IO.Directory]::CreateDirectory($assetsRoot) | Out-Null
    New-MsixLogo -SourcePath $logoSourcePath -DestinationPath (Join-Path $assetsRoot 'Square44x44Logo.png') -Size 44
    New-MsixLogo -SourcePath $logoSourcePath -DestinationPath (Join-Path $assetsRoot 'Square150x150Logo.png') -Size 150
    New-MsixLogo -SourcePath $logoSourcePath -DestinationPath (Join-Path $assetsRoot 'StoreLogo.png') -Size 50

    $manifestText = $manifestTemplate.Replace('{{PACKAGE_VERSION}}', $PackageVersion)
    [System.IO.File]::WriteAllText(
        (Join-Path $layoutRoot 'AppxManifest.xml'),
        $manifestText,
        [System.Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath $msixReadmePath -Destination $layoutRoot

    Invoke-CheckedCommand 'MakeAppx pack' {
        & $makeAppxPath pack /o /d $layoutRoot /p $msixPath | Out-Null
    }
    $comparisonPath = Join-Path $stagingRoot "$packageBaseName.comparison.msix"
    Invoke-CheckedCommand 'MakeAppx deterministic comparison pack' {
        & $makeAppxPath pack /o /d $layoutRoot /p $comparisonPath | Out-Null
    }

    $msixHash = (Get-FileHash -LiteralPath $msixPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $comparisonHash = (Get-FileHash -LiteralPath $comparisonPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $byteReproducible = $msixHash -eq $comparisonHash
    Test-MsixContainerEntries -Path $msixPath
    Test-MsixContainerEntries -Path $comparisonPath

    Invoke-CheckedCommand 'MakeAppx unpack verification' {
        & $makeAppxPath unpack /o /p $msixPath /d $verificationRoot | Out-Null
    }
    Invoke-CheckedCommand 'MakeAppx comparison unpack verification' {
        & $makeAppxPath unpack /o /p $comparisonPath /d $comparisonVerificationRoot | Out-Null
    }
    Test-MsixLayout -LayoutRoot $verificationRoot -ExpectedVersion $PackageVersion
    Test-MsixLayout -LayoutRoot $comparisonVerificationRoot -ExpectedVersion $PackageVersion
    $layoutFingerprint = Get-LayoutFingerprint -LayoutRoot $verificationRoot
    $comparisonLayoutFingerprint = Get-LayoutFingerprint -LayoutRoot $comparisonVerificationRoot
    if ($layoutFingerprint -ne $comparisonLayoutFingerprint) {
        throw 'Identical MSIX source layouts produced different unpacked content fingerprints.'
    }

    "$msixHash  $([System.IO.Path]::GetFileName($msixPath))" |
        Set-Content -LiteralPath $msixHashPath -Encoding ascii
    $buildManifest = [ordered]@{
        schemaVersion = 1
        product = 'Long Grid'
        displayName = $manifestContract.Package.Properties.DisplayName
        packageType = 'unsigned-msix'
        packageVersion = $PackageVersion
        portableVersion = $PortableVersion
        sourceCommit = $commit
        identityName = 'Longyuyeee.LongGrid.DeveloperPreview'
        publisher = 'CN=LongGrid Development'
        processorArchitecture = 'x64'
        minimumWindowsBuild = 22000
        capabilities = @('runFullTrust')
        signed = $false
        installable = $false
        distributionApproved = $false
        licenseStatus = 'Deferred'
        desktopHostExecutionEnabled = $false
        deterministicLayout = $true
        byteReproducible = $byteReproducible
        layoutFingerprint = $layoutFingerprint
        sha256 = $msixHash
    }
    $buildManifest | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $buildManifestPath -Encoding utf8

    [ordered]@{
        outcome = 'Pass'
        packageType = 'unsigned-msix'
        packageVersion = $PackageVersion
        sourceCommit = $commit
        identityName = 'Longyuyeee.LongGrid.DeveloperPreview'
        publisher = 'CN=LongGrid Development'
        package = $msixPath
        sha256 = $msixHash
        sha256File = $msixHashPath
        buildManifest = $buildManifestPath
        deterministicLayout = $true
        byteReproducible = $byteReproducible
        layoutFingerprint = $layoutFingerprint
        signed = $false
        installable = $false
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
