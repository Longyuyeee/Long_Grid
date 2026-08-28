[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$gateScript = Join-Path $PSScriptRoot 'Test-LongGridDependencyLicenses.ps1'
$contractPath = Join-Path $projectRoot 'packaging\release\dependency-license-contract.json'
$positiveReportPath = Join-Path $projectRoot 'artifacts\LongGrid-dependency-license-gate-test.json'
$negativeContractPath = Join-Path $projectRoot 'artifacts\.dependency-license-negative-contract.json'
$negativeReportPath = Join-Path $projectRoot 'artifacts\.dependency-license-negative-report.json'

if ($env:OS -ne 'Windows_NT') {
    throw 'The dependency license gate test currently supports the Windows release environment only.'
}

try {
    foreach ($stalePath in @($positiveReportPath, $negativeContractPath, $negativeReportPath)) {
        if (Test-Path -LiteralPath $stalePath -PathType Leaf) {
            Remove-Item -LiteralPath $stalePath -Force
        }
    }
    $positiveText = & $gateScript -OutputPath $positiveReportPath | Out-String
    if (-not $?) {
        throw 'The real dependency license inventory did not pass.'
    }
    $positive = $positiveText | ConvertFrom-Json
    if ($positive.outcome -ne 'Pass' -or
        $positive.projectCount -ne 20 -or
        $positive.packageCount -ne 30 -or
        -not $positive.metadataComplete -or
        $positive.clearanceStatus -ne 'PendingOwnerReviewAndNotice' -or
        $positive.distributionApproved) {
        throw 'The real dependency license inventory returned an unsafe or incomplete result.'
    }
    $firstReportHash = $positive.reportSha256
    $secondPositiveText = & $gateScript -OutputPath $positiveReportPath | Out-String
    if (-not $?) {
        throw 'The repeated dependency license inventory did not pass.'
    }
    $secondPositive = $secondPositiveText | ConvertFrom-Json
    if ($secondPositive.reportSha256 -ne $firstReportHash) {
        throw 'The dependency license report is not deterministic across repeated real scans.'
    }

    $negativeContract = Get-Content -LiteralPath $contractPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $negativeContract.expectedPackageCount = 31
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $negativeContractPath)) | Out-Null
    $negativeJson = ($negativeContract | ConvertTo-Json -Depth 5) -replace "`r`n", "`n"
    [System.IO.File]::WriteAllText(
        $negativeContractPath,
        $negativeJson + "`n",
        [System.Text.UTF8Encoding]::new($false))

    $negativeOutput = & powershell -NoProfile -ExecutionPolicy Bypass -File $gateScript `
        -ContractPath $negativeContractPath `
        -OutputPath $negativeReportPath 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) {
        throw 'The dependency license gate accepted an intentionally drifted package count.'
    }
    if ($negativeOutput -notmatch 'packageCount expected=31 actual=30') {
        throw 'The dependency license gate did not report the expected package-count difference.'
    }
    if (Test-Path -LiteralPath $negativeReportPath -PathType Leaf) {
        throw 'The dependency license gate wrote a success report after negative drift.'
    }

    [ordered]@{
        outcome = 'Pass'
        realProjectCount = $positive.projectCount
        realPackageCount = $positive.packageCount
        deterministicReportSha256 = $firstReportHash
        negativeDifference = 'packageCount expected=31 actual=30'
        negativeReportWritten = $false
        clearanceStatus = $positive.clearanceStatus
        distributionApproved = $false
    } | ConvertTo-Json
}
finally {
    foreach ($temporaryPath in @($positiveReportPath, $negativeContractPath, $negativeReportPath)) {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}
