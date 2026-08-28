[CmdletBinding()]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$PortableVersion = '0.1.0-rcdev',

    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$PackageVersion = '0.1.0.0',

    [switch]$SkipQualityGates,
    [switch]$NoRestore,
    [switch]$NoToolRestore,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $projectRoot 'artifacts'
$portableBaseName = "LongGrid-$PortableVersion-win-x64"
$packageBaseName = "LongGrid-$PackageVersion-win-x64-unsigned"
$portablePath = Join-Path $artifactRoot "$portableBaseName.zip"
$portableHashPath = "$portablePath.sha256"
$msixPath = Join-Path $artifactRoot "$packageBaseName.msix"
$msixHashPath = "$msixPath.sha256"
$msixManifestPath = Join-Path $artifactRoot "$packageBaseName.manifest.json"
$sbomPath = Join-Path $artifactRoot "$packageBaseName.spdx.json"
$sbomHashPath = "$sbomPath.sha256"
$sbomEvidencePath = Join-Path $artifactRoot "$packageBaseName.sbom-evidence.json"
$licenseReportPath = Join-Path $artifactRoot "LongGrid-$PackageVersion-dependency-licenses.json"
$candidateManifestPath = Join-Path $artifactRoot "LongGrid-$PackageVersion-win-x64-internal-rc-evidence.json"
$candidateHashPath = "$candidateManifestPath.sha256"

function Invoke-JsonContract {
    param(
        [Parameter(Mandatory)]
        [string]$Description,

        [Parameter(Mandatory)]
        [string]$Path
    )

    $text = & $Path -ValidateOnly | Out-String
    if (-not $?) {
        throw "$Description failed."
    }
    try {
        return $text | ConvertFrom-Json
    }
    catch {
        throw "$Description did not return a single JSON contract."
    }
}

function Invoke-PackagingScript {
    param(
        [Parameter(Mandatory)]
        [string]$Description,

        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [hashtable]$Arguments
    )

    & $Path @Arguments
    if (-not $?) {
        throw "$Description failed."
    }
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

function Assert-HashSidecar {
    param(
        [Parameter(Mandatory)]
        [string]$ArtifactPath,

        [Parameter(Mandatory)]
        [string]$SidecarPath
    )

    if (-not (Test-Path -LiteralPath $ArtifactPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $SidecarPath -PathType Leaf)) {
        throw "Artifact or SHA-256 sidecar is missing: $ArtifactPath"
    }
    $hash = (Get-FileHash -LiteralPath $ArtifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedLine = "$hash  $([System.IO.Path]::GetFileName($ArtifactPath))"
    $actualLine = (Get-Content -LiteralPath $SidecarPath -Raw -Encoding ASCII).Trim()
    if ($actualLine -ne $expectedLine) {
        throw "SHA-256 sidecar does not match its artifact: $SidecarPath"
    }
    return $hash
}

if ($env:OS -ne 'Windows_NT') {
    throw 'Long Grid release-candidate delivery only supports Windows.'
}

$portableScript = Join-Path $PSScriptRoot 'Pack-LongGrid.ps1'
$msixScript = Join-Path $PSScriptRoot 'Pack-LongGridMsix.ps1'
$sbomScript = Join-Path $PSScriptRoot 'New-LongGridSbom.ps1'
$lifecycleScript = Join-Path $PSScriptRoot 'Test-LongGridMsixLifecycle.ps1'
$signingScript = Join-Path $PSScriptRoot 'Test-LongGridReleaseSigning.ps1'
$licenseScript = Join-Path $PSScriptRoot 'Test-LongGridDependencyLicenses.ps1'
foreach ($requiredScript in @($portableScript, $msixScript, $sbomScript, $lifecycleScript, $signingScript, $licenseScript)) {
    if (-not (Test-Path -LiteralPath $requiredScript -PathType Leaf)) {
        throw "Required release-candidate input is missing: $requiredScript"
    }
}

$lifecycleContract = Invoke-JsonContract -Description 'MSIX lifecycle contract' -Path $lifecycleScript
$signingContract = Invoke-JsonContract -Description 'protected signing contract' -Path $signingScript
$licenseContract = Invoke-JsonContract -Description 'dependency license metadata contract' -Path $licenseScript
if ($lifecycleContract.outcome -ne 'Pass' -or
    $lifecycleContract.liveEvidence -ne 'PendingSignedPackageAndDisposableWindowsProfile' -or
    $lifecycleContract.startsProcess -or
    $lifecycleContract.modifiesPackageState -or
    $lifecycleContract.trustsUnsignedPackage) {
    throw 'The MSIX lifecycle boundary is not safely pending.'
}
if ($signingContract.outcome -ne 'Pass' -or
    $signingContract.signingState -ne 'BlockedPendingApprovedPublisherCertificateAndManagedSigningProvider' -or
    $signingContract.prAndMainSigningAccess -or
    $signingContract.liveSigningImplemented -or
    $signingContract.installOrDistributionApproved) {
    throw 'The protected signing boundary is not safely blocked.'
}
if ($licenseContract.outcome -ne 'Pass' -or
    -not $licenseContract.metadataDriftFails -or
    $licenseContract.copiesLicenseFiles -or
    $licenseContract.decidesCompatibility -or
    $licenseContract.clearanceStatus -ne 'PendingOwnerReviewAndNotice' -or
    $licenseContract.distributionApproved) {
    throw 'The dependency license boundary is not safely pending owner review and NOTICE clearance.'
}

if ($ValidateOnly) {
    [ordered]@{
        outcome = 'Pass'
        mode = 'ValidateOnly'
        candidateType = 'internal-unsigned-developer-preview'
        portableVersion = $PortableVersion
        packageVersion = $PackageVersion
        aggregates = @('portable-zip', 'unsigned-msix', 'spdx-2.2', 'dependency-license-metadata', 'sha256', 'source-commit')
        lifecycleEvidence = $lifecycleContract.liveEvidence
        signingState = $signingContract.signingState
        licenseClearance = $licenseContract.clearanceStatus
        startsProcess = $false
        modifiesPackageState = $false
        signed = $false
        installable = $false
        distributionApproved = $false
    } | ConvertTo-Json -Depth 4
    exit 0
}

Push-Location $projectRoot
try {
    $gitStatus = & git status --porcelain
    if ($LASTEXITCODE -ne 0 -or $gitStatus) {
        throw 'The git worktree must be clean before release-candidate delivery.'
    }
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') {
        throw 'Unable to resolve the source commit.'
    }

    [System.IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
    foreach ($staleSuccessMarker in @($candidateManifestPath, $candidateHashPath, $licenseReportPath)) {
        if (Test-Path -LiteralPath $staleSuccessMarker -PathType Leaf) {
            [System.IO.File]::Delete($staleSuccessMarker)
        }
    }

    $portableArguments = @{ Version = $PortableVersion }
    if ($SkipQualityGates) {
        $portableArguments.SkipQualityGates = $true
    }
    if ($NoRestore) {
        $portableArguments.NoRestore = $true
    }
    Invoke-PackagingScript -Description 'portable Developer Preview build' -Path $portableScript -Arguments $portableArguments

    $downstreamArguments = @{
        PackageVersion = $PackageVersion
        PortableVersion = $PortableVersion
        SkipQualityGates = $true
    }
    if ($NoRestore) {
        $downstreamArguments.NoRestore = $true
    }
    Invoke-PackagingScript -Description 'unsigned MSIX build' -Path $msixScript -Arguments $downstreamArguments

    $sbomArguments = $downstreamArguments.Clone()
    if ($NoToolRestore) {
        $sbomArguments.NoToolRestore = $true
    }
    Invoke-PackagingScript -Description 'SPDX SBOM generation and validation' -Path $sbomScript -Arguments $sbomArguments

    # Downstream publish/package operations can perform RID-specific restores. Inventory the final
    # restored solution state rather than an earlier project.assets.json snapshot.
    $licenseText = & $licenseScript -OutputPath $licenseReportPath | Out-String
    if (-not $?) {
        throw 'Dependency license metadata inventory failed.'
    }
    try {
        $licenseEvidence = $licenseText | ConvertFrom-Json
    }
    catch {
        throw 'Dependency license metadata inventory did not return a single JSON contract.'
    }
    if ($licenseEvidence.outcome -ne 'Pass' -or
        $licenseEvidence.projectCount -ne $licenseContract.expectedProjectCount -or
        $licenseEvidence.packageCount -ne $licenseContract.expectedPackageCount -or
        -not $licenseEvidence.metadataComplete -or
        $licenseEvidence.clearanceStatus -ne 'PendingOwnerReviewAndNotice' -or
        $licenseEvidence.distributionApproved -or
        -not (Test-Path -LiteralPath $licenseReportPath -PathType Leaf)) {
        throw 'Dependency license metadata evidence is incomplete or unsafe.'
    }
    $licenseReportHash = (Get-FileHash -LiteralPath $licenseReportPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($licenseEvidence.reportSha256 -ne $licenseReportHash) {
        throw 'Dependency license report hash does not match its inventory result.'
    }

    $portableHash = Assert-HashSidecar -ArtifactPath $portablePath -SidecarPath $portableHashPath
    $msixHash = Assert-HashSidecar -ArtifactPath $msixPath -SidecarPath $msixHashPath
    $sbomHash = Assert-HashSidecar -ArtifactPath $sbomPath -SidecarPath $sbomHashPath
    foreach ($requiredEvidence in @($msixManifestPath, $sbomEvidencePath)) {
        if (-not (Test-Path -LiteralPath $requiredEvidence -PathType Leaf)) {
            throw "Required release-candidate evidence is missing: $requiredEvidence"
        }
    }

    $portableManifest = Get-PortableArtifactManifest -Path $portablePath
    $msixManifest = Get-Content -LiteralPath $msixManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $sbomEvidence = Get-Content -LiteralPath $sbomEvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($portableManifest.sourceCommit -ne $commit -or
        $portableManifest.version -ne $PortableVersion -or
        $portableManifest.packageType -ne 'portable-unpacked-zip' -or
        $portableManifest.signed -or
        $portableManifest.installer -or
        $portableManifest.distributionApproved) {
        throw 'The portable package is not bound to the internal Developer Preview contract.'
    }
    if ($msixManifest.sourceCommit -ne $commit -or
        $msixManifest.packageVersion -ne $PackageVersion -or
        $msixManifest.sha256 -ne $msixHash -or
        -not $msixManifest.deterministicLayout -or
        $msixManifest.signed -or
        $msixManifest.installable -or
        $msixManifest.distributionApproved) {
        throw 'The MSIX is not bound to the verified unsigned layout contract.'
    }
    if ($sbomEvidence.sourceCommit -ne $commit -or
        $sbomEvidence.packageVersion -ne $PackageVersion -or
        $sbomEvidence.subject.sha256 -ne $msixHash -or
        $sbomEvidence.subject.signed -or
        $sbomEvidence.sbom.sha256 -ne $sbomHash -or
        $sbomEvidence.sbom.format -ne 'SPDX-2.2' -or
        -not $sbomEvidence.sbom.validated -or
        $sbomEvidence.installable -or
        $sbomEvidence.distributionApproved) {
        throw 'The SBOM evidence is not bound to the current unsigned MSIX and source commit.'
    }

    $qualityGateMode = if ($SkipQualityGates) { 'prevalidated-by-caller' } else { 'packaging-default-executed' }
    $executionEnvironment = if ($env:GITHUB_ACTIONS -eq 'true') { 'github-actions' } else { 'local' }
    $candidateManifest = [ordered]@{
        schemaVersion = 1
        product = 'Long Grid'
        candidateType = 'internal-unsigned-developer-preview'
        sourceCommit = $commit
        portableVersion = $PortableVersion
        packageVersion = $PackageVersion
        execution = [ordered]@{
            environment = $executionEnvironment
            qualityGateMode = $qualityGateMode
            githubRunId = if ($executionEnvironment -eq 'github-actions') { $env:GITHUB_RUN_ID } else { $null }
            githubRunAttempt = if ($executionEnvironment -eq 'github-actions') { $env:GITHUB_RUN_ATTEMPT } else { $null }
        }
        artifacts = [ordered]@{
            portable = [ordered]@{
                file = [System.IO.Path]::GetFileName($portablePath)
                sha256 = $portableHash
                deterministicArchive = $true
            }
            msix = [ordered]@{
                file = [System.IO.Path]::GetFileName($msixPath)
                sha256 = $msixHash
                identityName = $msixManifest.identityName
                publisher = $msixManifest.publisher
                deterministicLayout = $true
                byteReproducible = [bool]$msixManifest.byteReproducible
            }
            sbom = [ordered]@{
                file = [System.IO.Path]::GetFileName($sbomPath)
                sha256 = $sbomHash
                format = 'SPDX-2.2'
                tool = $sbomEvidence.sbom.tool
                toolVersion = $sbomEvidence.sbom.toolVersion
                inventoriedFileCount = $sbomEvidence.sbom.inventoriedFileCount
                validated = $true
            }
            dependencyLicenses = [ordered]@{
                file = [System.IO.Path]::GetFileName($licenseReportPath)
                sha256 = $licenseReportHash
                projectCount = $licenseEvidence.projectCount
                packageCount = $licenseEvidence.packageCount
                metadataComplete = $true
                clearanceStatus = $licenseEvidence.clearanceStatus
            }
        }
        gates = [ordered]@{
            lifecycleEvidence = $lifecycleContract.liveEvidence
            signingState = $signingContract.signingState
            sameSourceCommit = $true
            artifactHashesVerified = $true
            sbomSubjectHashVerified = $true
            dependencyLicenseMetadataVerified = $true
            licenseClearance = $licenseEvidence.clearanceStatus
        }
        signed = $false
        installable = $false
        distributionApproved = $false
        licenseStatus = 'Deferred'
        desktopHostExecutionEnabled = $false
    }
    $candidateManifest | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $candidateManifestPath -Encoding utf8
    $candidateHash = (Get-FileHash -LiteralPath $candidateManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$candidateHash  $([System.IO.Path]::GetFileName($candidateManifestPath))" |
        Set-Content -LiteralPath $candidateHashPath -Encoding ascii

    [ordered]@{
        outcome = 'Pass'
        candidateType = 'internal-unsigned-developer-preview'
        sourceCommit = $commit
        portable = $portablePath
        portableSha256 = $portableHash
        msix = $msixPath
        msixSha256 = $msixHash
        sbom = $sbomPath
        sbomSha256 = $sbomHash
        dependencyLicenseReport = $licenseReportPath
        dependencyLicenseReportSha256 = $licenseReportHash
        licenseClearance = $licenseEvidence.clearanceStatus
        candidateEvidence = $candidateManifestPath
        candidateEvidenceSha256 = $candidateHash
        qualityGateMode = $qualityGateMode
        lifecycleEvidence = $lifecycleContract.liveEvidence
        signingState = $signingContract.signingState
        signed = $false
        installable = $false
        distributionApproved = $false
    } | ConvertTo-Json -Depth 4
}
finally {
    Pop-Location
}
