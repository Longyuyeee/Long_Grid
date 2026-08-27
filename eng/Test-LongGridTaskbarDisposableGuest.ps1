[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$isSandboxIdentity = [string]::Equals(
    [System.Environment]::UserName,
    'WDAGUtilityAccount',
    [System.StringComparison]::OrdinalIgnoreCase)
$evidenceDirectory = 'C:\LongGridEvidence'
$sourceDirectory = 'C:\LongGridSource'
$difference = @()
if (-not $isSandboxIdentity) { $difference += 'NotWindowsSandboxIdentity' }
if (-not (Test-Path -LiteralPath $sourceDirectory -PathType Container)) {
    $difference += 'ReadOnlySourceMappingMissing'
}
if (-not (Test-Path -LiteralPath $evidenceDirectory -PathType Container)) {
    $difference += 'EvidenceMappingMissing'
}

$result = [ordered]@{
    schemaVersion = 1
    purpose = 'TaskbarR2B1DisposableGuestAdmission'
    expected = [ordered]@{
        sandboxIdentity = 'WDAGUtilityAccount'
        sourceMappingPresent = $true
        evidenceMappingPresent = $true
        modifiedSystemState = $false
    }
    actual = [ordered]@{
        sandboxIdentityAttested = $isSandboxIdentity
        sourceMappingPresent = Test-Path -LiteralPath $sourceDirectory -PathType Container
        evidenceMappingPresent = Test-Path -LiteralPath $evidenceDirectory -PathType Container
        modifiedSystemState = $false
        mutationAllowed = $false
    }
    difference = if ($difference.Count -eq 0) { 'None' } else { $difference }
    outcome = if ($difference.Count -eq 0) { 'GuestReady' } else { 'Blocked' }
}

$payload = $result | ConvertTo-Json -Depth 6
$payload
if ($difference.Count -eq 0) {
    $evidencePath = Join-Path $evidenceDirectory 'guest-admission.json'
    [System.IO.File]::WriteAllText(
        $evidencePath,
        $payload,
        [System.Text.UTF8Encoding]::new($false))
}
