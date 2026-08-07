[CmdletBinding()]
param(
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$packScriptPath = Join-Path $PSScriptRoot 'Pack-LongGridMsix.ps1'
$manifestTemplatePath = Join-Path $projectRoot 'packaging\msix\AppxManifest.template.xml'

foreach ($requiredPath in @($packScriptPath, $manifestTemplatePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "MSIX lifecycle contract input is missing: $requiredPath"
    }
}

$packScript = Get-Content -LiteralPath $packScriptPath -Raw
$manifestTemplate = Get-Content -LiteralPath $manifestTemplatePath -Raw -Encoding UTF8
foreach ($requiredContract in @(
    'Longyuyeee.LongGrid.DeveloperPreview',
    'CN=LongGrid Development',
    'AppxSignature.p7x',
    "signed = `$false",
    "installable = `$false",
    "distributionApproved = `$false"
)) {
    if ($packScript.IndexOf($requiredContract, [System.StringComparison]::Ordinal) -lt 0 -and
        $manifestTemplate.IndexOf($requiredContract, [System.StringComparison]::Ordinal) -lt 0) {
        throw "MSIX lifecycle source contract is missing: $requiredContract"
    }
}

if (-not $ValidateOnly) {
    throw 'Live MSIX lifecycle mutation is disabled until a protected signing identity and disposable Windows profile are approved.'
}

[ordered]@{
    schemaVersion = 1
    purpose = 'LongGridMsixInstallUpgradeUninstallRollback'
    mode = 'validate-only'
    startsProcess = $false
    modifiesPackageState = $false
    trustsUnsignedPackage = $false
    requiredIdentity = 'Longyuyeee.LongGrid.DeveloperPreview'
    requiredPublisher = 'CN=LongGrid Development'
    liveEvidence = 'PendingSignedPackageAndDisposableWindowsProfile'
    outcome = 'Pass'
} | ConvertTo-Json
