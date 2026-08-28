[CmdletBinding()]
param(
    [string]$WorkflowPath = '.github/workflows/codeql.yml'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resolvedWorkflowPath = if ([System.IO.Path]::IsPathRooted($WorkflowPath)) {
    [System.IO.Path]::GetFullPath($WorkflowPath)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $projectRoot $WorkflowPath))
}

function Get-ContractDifferences {
    param(
        [Parameter(Mandatory)]
        [string]$Text
    )

    $differences = @()
    $requiredPatterns = [ordered]@{
        'workflow name' = '(?m)^name: CodeQL$'
        'pull request trigger' = '(?m)^  pull_request:$'
        'main push trigger' = '(?ms)^  push:\s+branches:\s+- main\s*$'
        'read-only contents' = '(?m)^  contents: read$'
        'code scanning upload permission' = '(?m)^  security-events: write$'
        'finite job timeout' = '(?m)^    timeout-minutes: 30$'
        'non-short-circuit matrix' = '(?m)^      fail-fast: false$'
        'exact language matrix' = '(?m)^        language: \[csharp, c-cpp\]$'
        'CodeQL v4 init pinned commit' = '(?m)^        uses: github/codeql-action/init@[0-9a-f]{40} # v4$'
        'matrix language binding' = '(?m)^          languages: \$\{\{ matrix\.language \}\}$'
        'manual compiled build mode' = '(?m)^          build-mode: manual$'
        'locked managed restore' = '(?m)^        run: dotnet restore LongGrid\.sln --locked-mode$'
        'managed Release build' = '(?m)^        run: dotnet build LongGrid\.sln --configuration Release --no-restore$'
        'native audited build entry' = '(?m)^          -File \./eng/Build-LongGridExplorerCommand\.ps1$'
        'CodeQL v4 analyze pinned commit' = '(?m)^        uses: github/codeql-action/analyze@[0-9a-f]{40} # v4$'
        'language result category' = '(?m)^          category: /language:\$\{\{ matrix\.language \}\}$'
    }
    foreach ($entry in $requiredPatterns.GetEnumerator()) {
        if ($Text -notmatch $entry.Value) {
            $differences += "missing:$($entry.Key)"
        }
    }

    foreach ($forbiddenPattern in ([ordered]@{
            'OIDC write permission' = '(?m)^\s*id-token: write\s*$'
            'workflow secret reference' = 'secrets\.'
            'protected environment' = '(?m)^\s*environment:'
            'certificate tooling' = '(?i)SignTool|New-SelfSignedCertificate|\.pfx|\.p12'
            'package state mutation' = '(?i)Add-AppxPackage|Remove-AppxPackage'
        }).GetEnumerator()) {
        if ($Text -match $forbiddenPattern.Value) {
            $differences += "forbidden:$($forbiddenPattern.Key)"
        }
    }

    $permissionLines = @([regex]::Matches(
            $Text,
            '(?m)^  ([a-z-]+): (read|write)$') | ForEach-Object { $_.Value.Trim() })
    if ($permissionLines.Count -ne 2 -or
        $permissionLines -notcontains 'contents: read' -or
        $permissionLines -notcontains 'security-events: write') {
        $differences += 'permissions:not-exactly-contents-read-and-security-events-write'
    }
    if ([regex]::Matches($Text, '(?m)^        uses: github/codeql-action/init@[0-9a-f]{40} # v4$').Count -ne 1) {
        $differences += 'init-action-count:not-one'
    }
    if ([regex]::Matches($Text, '(?m)^        uses: github/codeql-action/analyze@[0-9a-f]{40} # v4$').Count -ne 1) {
        $differences += 'analyze-action-count:not-one'
    }
    return $differences
}

if (-not (Test-Path -LiteralPath $resolvedWorkflowPath -PathType Leaf)) {
    throw "CodeQL workflow is missing: $resolvedWorkflowPath"
}
$workflow = Get-Content -LiteralPath $resolvedWorkflowPath -Raw -Encoding UTF8
$actualDifferences = @(Get-ContractDifferences -Text $workflow)
if ($actualDifferences.Count -gt 0) {
    throw "CodeQL workflow contract failed:`n$($actualDifferences -join "`n")"
}

$negativeWorkflow = $workflow.Replace(
    'language: [csharp, c-cpp]',
    'language: [csharp]')
$negativeDifferences = @(Get-ContractDifferences -Text $negativeWorkflow)
if ($negativeDifferences -notcontains 'missing:exact language matrix') {
    throw 'CodeQL workflow contract accepted an intentionally incomplete C#-only language matrix.'
}

[ordered]@{
    outcome = 'Pass'
    workflow = '.github/workflows/codeql.yml'
    languages = @('csharp', 'c-cpp')
    buildMode = 'manual'
    actionMajor = 'v4'
    permissionContents = 'read'
    permissionSecurityEvents = 'write'
    negativeDifference = 'missing:exact language matrix'
    signingOrDistributionAccess = $false
} | ConvertTo-Json
