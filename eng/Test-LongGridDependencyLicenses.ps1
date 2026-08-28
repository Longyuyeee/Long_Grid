[CmdletBinding()]
param(
    [string]$Solution = 'LongGrid.sln',
    [string]$ContractPath = 'packaging/release/dependency-license-contract.json',
    [string]$OutputPath = 'artifacts/LongGrid-dependency-license-report.json',
    [string[]]$ProjectAssetsPaths,
    [string]$NuGetPackageRoot,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetResolverPath = Join-Path $PSScriptRoot 'LongGrid.DotNetHost.ps1'

if (-not (Test-Path -LiteralPath $dotnetResolverPath -PathType Leaf)) {
    throw "The shared .NET host resolver was not found: $dotnetResolverPath"
}
. $dotnetResolverPath
$dotnetHostPath = Resolve-LongGridDotNetHost $projectRoot

function Resolve-ProjectPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $projectRoot $Path))
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

function Get-Sha256Text {
    param(
        [Parameter(Mandatory)]
        [string[]]$Lines
    )

    $canonical = ($Lines -join "`n") + "`n"
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($canonical)
    $stream = [System.IO.MemoryStream]::new($bytes, $false)
    try {
        return (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    finally {
        $stream.Dispose()
    }
}

function Read-SafeXml {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($Path, $settings)
    try {
        $document = [System.Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
        return $document
    }
    finally {
        $reader.Dispose()
    }
}

$resolvedContractPath = Resolve-ProjectPath -Path $ContractPath
if (-not (Test-Path -LiteralPath $resolvedContractPath -PathType Leaf)) {
    throw "Dependency license contract is missing: $resolvedContractPath"
}
$contract = Get-Content -LiteralPath $resolvedContractPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($contract.schemaVersion -ne 1 -or
    $contract.scope -ne 'all-solution-projects-restored-assets' -or
    $contract.clearanceStatus -ne 'PendingOwnerReviewAndNotice' -or
    $contract.distributionApproved -or
    @($contract.acceptedLicenseKinds).Count -ne 3 -or
    @($contract.acceptedLicenseKinds) -notcontains 'expression' -or
    @($contract.acceptedLicenseKinds) -notcontains 'file' -or
    @($contract.acceptedLicenseKinds) -notcontains 'url') {
    throw 'Dependency license contract does not preserve the fail-closed release boundary.'
}

if ($ValidateOnly) {
    [ordered]@{
        outcome = 'Pass'
        mode = 'ValidateOnly'
        dotnetHost = $dotnetHostPath
        scope = $contract.scope
        expectedProjectCount = $contract.expectedProjectCount
        expectedPackageCount = $contract.expectedPackageCount
        metadataDriftFails = $true
        copiesLicenseFiles = $false
        decidesCompatibility = $false
        clearanceStatus = $contract.clearanceStatus
        distributionApproved = $false
    } | ConvertTo-Json
    exit 0
}

Push-Location $projectRoot
try {
    if ($null -eq $ProjectAssetsPaths -or $ProjectAssetsPaths.Count -eq 0) {
        $projectPaths = @(& $dotnetHostPath sln $Solution list | Where-Object { $_ -match '\.csproj$' })
        if ($LASTEXITCODE -ne 0 -or $projectPaths.Count -eq 0) {
            throw 'Unable to enumerate solution projects.'
        }
        $ProjectAssetsPaths = @($projectPaths | ForEach-Object {
                Join-Path (Split-Path -Parent $_) 'obj/project.assets.json'
            })
    }

    $resolvedAssetsPaths = @($ProjectAssetsPaths | ForEach-Object { Resolve-ProjectPath -Path $_ } | Sort-Object -Unique)
    $packages = @{}
    $candidatePackageRoots = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    if (-not [string]::IsNullOrWhiteSpace($NuGetPackageRoot)) {
        [void]$candidatePackageRoots.Add((Resolve-ProjectPath -Path $NuGetPackageRoot))
    }

    foreach ($assetsPath in $resolvedAssetsPaths) {
        if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
            throw "Restored project assets are missing: $assetsPath"
        }
        $assets = Get-Content -LiteralPath $assetsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([string]::IsNullOrWhiteSpace($NuGetPackageRoot)) {
            foreach ($property in $assets.packageFolders.PSObject.Properties) {
                [void]$candidatePackageRoots.Add([System.IO.Path]::GetFullPath($property.Name))
            }
        }
        foreach ($property in $assets.libraries.PSObject.Properties) {
            if ($property.Value.type -ne 'package') {
                continue
            }
            $identityParts = $property.Name -split '/', 2
            if ($identityParts.Count -ne 2 -or
                [string]::IsNullOrWhiteSpace($identityParts[0]) -or
                [string]::IsNullOrWhiteSpace($identityParts[1]) -or
                [string]::IsNullOrWhiteSpace($property.Value.path)) {
                throw "Malformed package identity in restored assets: $($property.Name)"
            }
            $key = "$($identityParts[0].ToLowerInvariant())/$($identityParts[1])"
            $entry = [ordered]@{
                id = $identityParts[0]
                version = $identityParts[1]
                packagePath = ([string]$property.Value.path).Replace('\', '/')
                files = @($property.Value.files | ForEach-Object { ([string]$_).Replace('\', '/') } | Sort-Object -Unique)
            }
            if ($packages.ContainsKey($key)) {
                $existing = $packages[$key]
                if ($existing.packagePath -ne $entry.packagePath -or
                    (@($existing.files) -join "`n") -ne (@($entry.files) -join "`n")) {
                    throw "Conflicting restored package records for $key."
                }
            }
            else {
                $packages[$key] = $entry
            }
        }
    }

    $records = @()
    foreach ($key in @($packages.Keys | Sort-Object)) {
        $package = $packages[$key]
        $packageDirectory = $null
        foreach ($packageRoot in @($candidatePackageRoots | Sort-Object)) {
            $candidate = Join-Path $packageRoot $package.packagePath
            if (Test-Path -LiteralPath $candidate -PathType Container) {
                $packageDirectory = [System.IO.Path]::GetFullPath($candidate)
                break
            }
        }
        if ($null -eq $packageDirectory) {
            throw "Restored package directory was not found for $key."
        }

        $nuspecFiles = @(Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nuspec' -File)
        if ($nuspecFiles.Count -ne 1) {
            throw "Expected exactly one nuspec for $key; found $($nuspecFiles.Count)."
        }
        $nuspec = Read-SafeXml -Path $nuspecFiles[0].FullName
        $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
        if ($null -eq $metadata) {
            throw "Nuspec metadata is missing for $key."
        }
        $licenseNode = $metadata.SelectSingleNode("*[local-name()='license']")
        $licenseUrlNode = $metadata.SelectSingleNode("*[local-name()='licenseUrl']")
        $acceptanceNode = $metadata.SelectSingleNode("*[local-name()='requireLicenseAcceptance']")
        $licenseKind = $null
        $licenseValue = $null
        $licenseFileSha256 = $null
        if ($null -ne $licenseNode -and -not [string]::IsNullOrWhiteSpace($licenseNode.InnerText)) {
            $licenseKind = ([string]$licenseNode.GetAttribute('type')).Trim().ToLowerInvariant()
            $licenseValue = $licenseNode.InnerText.Trim()
            if ($licenseKind -notin @('expression', 'file')) {
                throw "Unsupported license type '$licenseKind' for $key."
            }
            if ($licenseKind -eq 'file') {
                if ([System.IO.Path]::IsPathRooted($licenseValue)) {
                    throw "License file path must be package-relative for $key."
                }
                $licensePath = [System.IO.Path]::GetFullPath((Join-Path $packageDirectory $licenseValue))
                Assert-ChildPath -Parent $packageDirectory -Child $licensePath
                if (-not (Test-Path -LiteralPath $licensePath -PathType Leaf)) {
                    throw "Declared license file is missing for ${key}: $licenseValue"
                }
                $licenseValue = $licenseValue.Replace('\', '/')
                $licenseFileSha256 = (Get-FileHash -LiteralPath $licensePath -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
        elseif ($null -ne $licenseUrlNode -and -not [string]::IsNullOrWhiteSpace($licenseUrlNode.InnerText)) {
            $licenseKind = 'url'
            $licenseValue = $licenseUrlNode.InnerText.Trim()
            $uri = $null
            if (-not [System.Uri]::TryCreate($licenseValue, [System.UriKind]::Absolute, [ref]$uri) -or
                $uri.Scheme -notin @('https', 'http')) {
                throw "License URL is not an absolute HTTP(S) URI for $key."
            }
        }
        else {
            throw "License metadata is missing for $key."
        }

        $supplementalLicenseUrl = if ($null -ne $licenseUrlNode) { $licenseUrlNode.InnerText.Trim() } else { $null }
        $noticeRecords = @()
        foreach ($relativeFile in @($package.files | Where-Object {
                    ([System.IO.Path]::GetFileName([string]$_)) -match '^(NOTICE|THIRD[-_. ]PARTY|THIRDPARTY)(\.|$)'
                } | Sort-Object -Unique)) {
            $normalizedRelativeFile = ([string]$relativeFile).Replace('\', '/')
            $noticePath = [System.IO.Path]::GetFullPath((Join-Path $packageDirectory $normalizedRelativeFile))
            Assert-ChildPath -Parent $packageDirectory -Child $noticePath
            if (-not (Test-Path -LiteralPath $noticePath -PathType Leaf)) {
                throw "Restored NOTICE/third-party file is missing for ${key}: $normalizedRelativeFile"
            }
            $noticeRecords += [ordered]@{
                path = $normalizedRelativeFile
                sha256 = (Get-FileHash -LiteralPath $noticePath -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }

        $requiresAcceptance = $false
        if ($null -ne $acceptanceNode -and
            -not [bool]::TryParse($acceptanceNode.InnerText.Trim(), [ref]$requiresAcceptance)) {
            throw "Invalid requireLicenseAcceptance value for $key."
        }
        $records += [ordered]@{
            id = $package.id
            version = $package.version
            licenseKind = $licenseKind
            licenseValue = $licenseValue
            licenseFileSha256 = $licenseFileSha256
            supplementalLicenseUrl = $supplementalLicenseUrl
            requireLicenseAcceptance = $requiresAcceptance
            noticeFiles = $noticeRecords
        }
    }

    $identityLines = @($records | ForEach-Object { "$($_.id.ToLowerInvariant())@$($_.version)" })
    $metadataLines = @($records | ForEach-Object {
            $notice = @($_.noticeFiles | ForEach-Object { "$($_.path):$($_.sha256)" }) -join ','
            "$($_.id.ToLowerInvariant())@$($_.version)|$($_.licenseKind)|$($_.licenseValue)|$($_.licenseFileSha256)|$($_.supplementalLicenseUrl)|$($_.requireLicenseAcceptance.ToString().ToLowerInvariant())|$notice"
        })
    $identitySha256 = Get-Sha256Text -Lines $identityLines
    $metadataSha256 = Get-Sha256Text -Lines $metadataLines
    $actual = [ordered]@{
        projectCount = $resolvedAssetsPaths.Count
        packageCount = $records.Count
        packageIdentitySha256 = $identitySha256
        licenseMetadataSha256 = $metadataSha256
    }
    $differences = @()
    if ($actual.projectCount -ne $contract.expectedProjectCount) {
        $differences += "projectCount expected=$($contract.expectedProjectCount) actual=$($actual.projectCount)"
    }
    if ($actual.packageCount -ne $contract.expectedPackageCount) {
        $differences += "packageCount expected=$($contract.expectedPackageCount) actual=$($actual.packageCount)"
    }
    if ($actual.packageIdentitySha256 -ne $contract.expectedPackageIdentitySha256) {
        $differences += "packageIdentitySha256 expected=$($contract.expectedPackageIdentitySha256) actual=$($actual.packageIdentitySha256)"
    }
    if ($actual.licenseMetadataSha256 -ne $contract.expectedLicenseMetadataSha256) {
        $differences += "licenseMetadataSha256 expected=$($contract.expectedLicenseMetadataSha256) actual=$($actual.licenseMetadataSha256)"
    }
    if ($differences.Count -gt 0) {
        throw "Dependency license metadata drifted:`n$($differences -join "`n")"
    }

    $report = [ordered]@{
        schemaVersion = 1
        scope = $contract.scope
        projectCount = $actual.projectCount
        packageCount = $actual.packageCount
        packageIdentitySha256 = $identitySha256
        licenseMetadataSha256 = $metadataSha256
        packages = $records
        metadataComplete = $true
        copiesLicenseFiles = $false
        decidesCompatibility = $false
        clearanceStatus = $contract.clearanceStatus
        distributionApproved = $false
    }
    $resolvedOutputPath = Resolve-ProjectPath -Path $OutputPath
    $artifactRoot = Join-Path $projectRoot 'artifacts'
    Assert-ChildPath -Parent $artifactRoot -Child $resolvedOutputPath
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedOutputPath)) | Out-Null
    $json = $report | ConvertTo-Json -Depth 8 -Compress
    [System.IO.File]::WriteAllText($resolvedOutputPath, $json + "`n", [System.Text.UTF8Encoding]::new($false))
    $reportSha256 = (Get-FileHash -LiteralPath $resolvedOutputPath -Algorithm SHA256).Hash.ToLowerInvariant()

    [ordered]@{
        outcome = 'Pass'
        report = $resolvedOutputPath
        reportSha256 = $reportSha256
        projectCount = $actual.projectCount
        packageCount = $actual.packageCount
        packageIdentitySha256 = $identitySha256
        licenseMetadataSha256 = $metadataSha256
        metadataComplete = $true
        clearanceStatus = $contract.clearanceStatus
        distributionApproved = $false
    } | ConvertTo-Json
}
finally {
    Pop-Location
}
