[CmdletBinding()]
param(
    [string]$PackageRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

function Test-IsChildPath {
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

    return $childPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
}

if ($env:OS -ne 'Windows_NT') {
    throw 'Long Grid Developer Preview only supports Windows 11 x64.'
}

$resolvedPackageRoot = [System.IO.Path]::GetFullPath($PackageRoot)
$executablePath = Join-Path $resolvedPackageRoot 'LongGrid.App.exe'
$hashManifestPath = Join-Path $resolvedPackageRoot 'SHA256SUMS.txt'
$artifactManifestPath = Join-Path $resolvedPackageRoot 'artifact-manifest.json'

foreach ($requiredPath in @($executablePath, $hashManifestPath, $artifactManifestPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required package file is missing: $requiredPath"
    }
}

$osVersion = [System.Environment]::OSVersion.Version
if ($osVersion.Major -lt 10 -or $osVersion.Build -lt 22000) {
    throw "Unsupported Windows build $($osVersion.Build). Windows 11 build 22000 or later is required."
}

if (-not [System.Environment]::Is64BitOperatingSystem) {
    throw 'Long Grid Developer Preview requires a 64-bit Windows installation.'
}

$manifest = Get-Content -LiteralPath $artifactManifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or
    $manifest.runtimeIdentifier -ne 'win-x64' -or
    -not $manifest.dotNetSelfContained -or
    -not $manifest.windowsAppSdkSelfContained -or
    $manifest.packageType -ne 'portable-unpacked-zip') {
    throw 'The artifact manifest does not describe a supported self-contained win-x64 portable package.'
}

$verifiedFiles = 0
foreach ($line in Get-Content -LiteralPath $hashManifestPath) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    if ($line -notmatch '^(?<hash>[0-9a-fA-F]{64})  (?<path>.+)$') {
        throw "Invalid SHA256SUMS entry: $line"
    }

    $relativePath = $Matches.path.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    if ([System.IO.Path]::IsPathRooted($relativePath)) {
        throw "Rooted package path is forbidden: $relativePath"
    }

    $candidatePath = [System.IO.Path]::GetFullPath((Join-Path $resolvedPackageRoot $relativePath))
    if (-not (Test-IsChildPath -Parent $resolvedPackageRoot -Child $candidatePath)) {
        throw "Package path escapes the package root: $relativePath"
    }

    if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) {
        throw "Hashed package file is missing: $relativePath"
    }

    $actualHash = (Get-FileHash -LiteralPath $candidatePath -Algorithm SHA256).Hash
    if (-not $actualHash.Equals($Matches.hash, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "SHA-256 mismatch: $relativePath"
    }

    $verifiedFiles++
}

if ($verifiedFiles -eq 0) {
    throw 'SHA256SUMS.txt did not contain any payload entries.'
}

[ordered]@{
    outcome = 'Pass'
    product = 'Long Grid'
    version = $manifest.version
    windowsBuild = $osVersion.Build
    runtimeIdentifier = $manifest.runtimeIdentifier
    verifiedFiles = $verifiedFiles
    signed = [bool]$manifest.signed
    installer = [bool]$manifest.installer
    launchCommand = $executablePath
} | ConvertTo-Json -Depth 4

