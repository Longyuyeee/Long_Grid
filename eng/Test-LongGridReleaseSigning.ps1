[CmdletBinding()]
param(
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$contractPath = Join-Path $projectRoot 'packaging\release\signing-contract.json'
$manifestTemplatePath = Join-Path $projectRoot 'packaging\msix\AppxManifest.template.xml'
$workflowPath = Join-Path $projectRoot '.github\workflows\ci.yml'
$gitIgnorePath = Join-Path $projectRoot '.gitignore'

if (-not $ValidateOnly) {
    throw 'Live signing is intentionally unavailable. Use -ValidateOnly until the Publisher, certificate, protected release environment, license, and lifecycle matrix are approved.'
}

foreach ($requiredPath in @($contractPath, $manifestTemplatePath, $workflowPath, $gitIgnorePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required signing-contract input is missing: $requiredPath"
    }
}

$contract = Get-Content -LiteralPath $contractPath -Raw -Encoding UTF8 | ConvertFrom-Json
$expectedStatus = 'BlockedPendingApprovedPublisherCertificateAndEnvironment'
if ($contract.schemaVersion -ne 1 -or $contract.status -ne $expectedStatus) {
    throw 'The release signing contract is not explicitly blocked on the approved release inputs.'
}

if ($contract.developmentIdentity.name -ne 'Longyuyeee.LongGrid.DeveloperPreview' -or
    $contract.developmentIdentity.publisher -ne 'CN=LongGrid Development' -or
    -not $contract.developmentIdentity.mustNotBeUsedForPublicDistribution) {
    throw 'The Developer Preview identity boundary is incomplete.'
}

$releaseBoundary = $contract.releaseBoundary
if ($releaseBoundary.pullRequestAccess -or
    $releaseBoundary.mainBuildAccess -or
    -not $releaseBoundary.protectedEnvironmentRequired -or
    -not $releaseBoundary.reviewerApprovalRequired -or
    -not $releaseBoundary.oidcOrManagedKeyProviderRequired -or
    $releaseBoundary.privateKeyFileAllowed -or
    $releaseBoundary.selfSignedCertificateAllowed) {
    throw 'The protected release boundary permits an unsafe signing path.'
}

$requirements = $contract.signingRequirements
if (-not $requirements.certificateSubjectMustExactlyMatchManifestPublisher -or
    $requirements.codeSigningEkuRequired -ne '1.3.6.1.5.5.7.3.3' -or
    $requirements.fileDigestAlgorithm -ne 'SHA256' -or
    -not $requirements.rfc3161TimestampRequired -or
    $requirements.timestampDigestAlgorithm -ne 'SHA256' -or
    -not $requirements.unsignedSha256MustBeVerifiedBeforeSigning -or
    -not $requirements.signedSha256MustBeGeneratedAfterSigning -or
    -not $requirements.signatureAndTimestampMustBeVerified -or
    -not $requirements.sbomMustBeValidatedBeforeSigning) {
    throw 'The signing verification requirements are incomplete.'
}

if (-not $contract.distribution.licenseApprovalRequired -or
    -not $contract.distribution.signedLifecycleMatrixRequired -or
    $contract.distribution.currentlyApproved) {
    throw 'Distribution must remain blocked until license and signed lifecycle evidence are approved.'
}

$manifestTemplate = Get-Content -LiteralPath $manifestTemplatePath -Raw -Encoding UTF8
if ($manifestTemplate.IndexOf('Name="Longyuyeee.LongGrid.DeveloperPreview"', [System.StringComparison]::Ordinal) -lt 0 -or
    $manifestTemplate.IndexOf('Publisher="CN=LongGrid Development"', [System.StringComparison]::Ordinal) -lt 0) {
    throw 'The MSIX template no longer matches the audited Developer Preview identity.'
}

$workflow = Get-Content -LiteralPath $workflowPath -Raw -Encoding UTF8
foreach ($forbiddenWorkflowPattern in @(
    'id-token\s*:\s*write',
    'secrets\.',
    'signtool',
    'New-SelfSignedCertificate',
    'Add-AppxPackage',
    'Remove-AppxPackage'
)) {
    if ($workflow -match $forbiddenWorkflowPattern) {
        throw "PR/main CI crosses the signing or installation boundary: $forbiddenWorkflowPattern"
    }
}
if ($workflow -notmatch '(?m)^permissions:\s*\r?\n\s+contents:\s*read\s*$') {
    throw 'PR/main CI must retain the explicit contents: read permission boundary.'
}

$gitIgnore = Get-Content -LiteralPath $gitIgnorePath -Raw -Encoding UTF8
foreach ($secretPattern in @('*.pfx', '*.p12', '*.cer', '*.key')) {
    if ($gitIgnore.IndexOf($secretPattern, [System.StringComparison]::Ordinal) -lt 0) {
        throw "The signing secret ignore boundary is missing: $secretPattern"
    }
}

[ordered]@{
    outcome = 'Pass'
    mode = 'ValidateOnly'
    signingState = $expectedStatus
    developmentIdentity = 'Longyuyeee.LongGrid.DeveloperPreview'
    developmentPublisher = 'CN=LongGrid Development'
    protectedEnvironmentRequired = $true
    prAndMainSigningAccess = $false
    privateKeyFileAllowed = $false
    selfSignedCertificateAllowed = $false
    liveSigningImplemented = $false
    installOrDistributionApproved = $false
} | ConvertTo-Json
