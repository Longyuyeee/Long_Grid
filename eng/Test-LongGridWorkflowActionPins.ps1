[CmdletBinding()]
param(
    [string]$WorkflowDirectory = '.github/workflows',
    [string]$ManifestPath = '.github/actions-pins.json'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

function Resolve-ProjectPath {
    param([Parameter(Mandatory)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $projectRoot $Path))
}

function Get-NormalizedProjectPath {
    param([Parameter(Mandatory)][string]$Path)

    $relative = $Path.Substring($projectRoot.Length).TrimStart('\', '/')
    return ($relative -replace '\\', '/')
}

function Get-PinDifferences {
    param(
        [Parameter(Mandatory)][object[]]$Documents,
        [Parameter(Mandatory)]$Manifest
    )

    $differences = @()
    $manifestByTarget = @{}
    foreach ($entry in @($Manifest.actions)) {
        if ($manifestByTarget.ContainsKey($entry.target)) {
            $differences += "manifest:duplicate-target:$($entry.target)"
            continue
        }
        if ([string]$entry.commit -cnotmatch '^[0-9a-f]{40}$') {
            $differences += "manifest:invalid-commit:$($entry.target)"
        }
        if ([string]$entry.version -notmatch '^v[0-9]+$') {
            $differences += "manifest:invalid-version:$($entry.target)"
        }
        $manifestByTarget[$entry.target] = $entry
    }

    $observedByTarget = @{}
    foreach ($document in $Documents) {
        $matches = [regex]::Matches(
            $document.Text,
            '(?m)^\s*-?\s*uses:\s*(?<reference>[^\s#]+)(?:\s+#.*)?$')
        foreach ($match in $matches) {
            $reference = $match.Groups['reference'].Value
            if ($reference.StartsWith('./') -or $reference.StartsWith('.\')) {
                continue
            }
            if ($reference.StartsWith('docker://')) {
                if ($reference -notmatch '@sha256:[0-9a-f]{64}$') {
                    $differences += "mutable-container:$($document.Path):$reference"
                }
                continue
            }

            $separator = $reference.LastIndexOf('@')
            if ($separator -le 0 -or $separator -eq ($reference.Length - 1)) {
                $differences += "invalid-action-reference:$($document.Path):$reference"
                continue
            }
            $target = $reference.Substring(0, $separator)
            $commit = $reference.Substring($separator + 1)
            if ($commit -cnotmatch '^[0-9a-f]{40}$') {
                $differences += "mutable-ref:$($document.Path):$reference"
                continue
            }
            if (-not $manifestByTarget.ContainsKey($target)) {
                $differences += "unapproved-action:$($document.Path):$target@$commit"
                continue
            }
            if ($commit -cne [string]$manifestByTarget[$target].commit) {
                $differences += "unapproved-pin:$($document.Path):$target@$commit"
            }
            if (-not $observedByTarget.ContainsKey($target)) {
                $observedByTarget[$target] = @()
            }
            $observedByTarget[$target] += $document.Path
        }
    }

    foreach ($entry in @($Manifest.actions)) {
        $expected = @($entry.workflows | Sort-Object)
        $actual = if ($observedByTarget.ContainsKey($entry.target)) {
            @($observedByTarget[$entry.target] | Sort-Object)
        }
        else {
            @()
        }
        if (($expected -join '|') -cne ($actual -join '|')) {
            $differences += "consumer-drift:$($entry.target):expected=$($expected -join ','):actual=$($actual -join ',')"
        }
    }

    $commitsByRepository = @{}
    foreach ($entry in @($Manifest.actions)) {
        $segments = ([string]$entry.target).Split('/')
        $repository = "$($segments[0])/$($segments[1])"
        if (-not $commitsByRepository.ContainsKey($repository)) {
            $commitsByRepository[$repository] = @()
        }
        $commitsByRepository[$repository] += [string]$entry.commit
    }
    foreach ($repository in $commitsByRepository.Keys) {
        if (@($commitsByRepository[$repository] | Select-Object -Unique).Count -ne 1) {
            $differences += "repository-pin-split:$repository"
        }
    }
    return $differences
}

$resolvedWorkflowDirectory = Resolve-ProjectPath -Path $WorkflowDirectory
$resolvedManifestPath = Resolve-ProjectPath -Path $ManifestPath
if (-not (Test-Path -LiteralPath $resolvedWorkflowDirectory -PathType Container)) {
    throw "Workflow directory is missing: $resolvedWorkflowDirectory"
}
if (-not (Test-Path -LiteralPath $resolvedManifestPath -PathType Leaf)) {
    throw "Action pin manifest is missing: $resolvedManifestPath"
}

$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or @($manifest.actions).Count -eq 0) {
    throw 'Action pin manifest schema or action list is invalid.'
}
$documents = @(Get-ChildItem -LiteralPath $resolvedWorkflowDirectory -File |
    Where-Object { $_.Extension -in '.yml', '.yaml' } |
    Sort-Object FullName |
    ForEach-Object {
        [pscustomobject]@{
            Path = Get-NormalizedProjectPath -Path $_.FullName
            Text = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
        }
    })
if ($documents.Count -eq 0) {
    throw 'No GitHub Actions workflow files were found.'
}

$actualDifferences = @(Get-PinDifferences -Documents $documents -Manifest $manifest)
if ($actualDifferences.Count -gt 0) {
    throw "GitHub Actions pin contract failed:`n$($actualDifferences -join "`n")"
}

$mutableDocuments = @($documents | ForEach-Object {
        [pscustomobject]@{ Path = $_.Path; Text = $_.Text }
    })
$mutableDocuments[0].Text = $mutableDocuments[0].Text.Replace(
    '@3d3c42e5aac5ba805825da76410c181273ba90b1',
    '@v7')
$mutableDifferences = @(Get-PinDifferences -Documents $mutableDocuments -Manifest $manifest)
if (@($mutableDifferences | Where-Object { $_ -like 'mutable-ref:*actions/checkout@v7' }).Count -ne 1) {
    throw 'Action pin contract accepted an intentionally mutable checkout major tag.'
}

$driftDocuments = @($documents | ForEach-Object {
        [pscustomobject]@{ Path = $_.Path; Text = $_.Text }
    })
$driftDocuments[0].Text = $driftDocuments[0].Text.Replace(
    '@3d3c42e5aac5ba805825da76410c181273ba90b1',
    '@0000000000000000000000000000000000000000')
$driftDifferences = @(Get-PinDifferences -Documents $driftDocuments -Manifest $manifest)
if (@($driftDifferences | Where-Object { $_ -like 'unapproved-pin:*actions/checkout@0000000000000000000000000000000000000000' }).Count -ne 1) {
    throw 'Action pin contract accepted an intentionally unapproved checkout commit.'
}

$unknownDocuments = @($documents | ForEach-Object {
        [pscustomobject]@{ Path = $_.Path; Text = $_.Text }
    })
$unknownDocuments[0].Text += "`n      - uses: example/unapproved@1111111111111111111111111111111111111111`n"
$unknownDifferences = @(Get-PinDifferences -Documents $unknownDocuments -Manifest $manifest)
if (@($unknownDifferences | Where-Object { $_ -like 'unapproved-action:*example/unapproved@1111111111111111111111111111111111111111' }).Count -ne 1) {
    throw 'Action pin contract accepted an intentionally unknown remote action.'
}

$consumerDocuments = @($documents | ForEach-Object {
        [pscustomobject]@{ Path = $_.Path; Text = $_.Text }
    })
$consumerDocuments[0].Text += "`n      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1`n"
$consumerDifferences = @(Get-PinDifferences -Documents $consumerDocuments -Manifest $manifest)
if (@($consumerDifferences | Where-Object { $_ -like 'consumer-drift:actions/checkout:*' }).Count -ne 1) {
    throw 'Action pin contract accepted an intentionally duplicated checkout consumer.'
}

$usageCount = 0
foreach ($entry in @($manifest.actions)) {
    $usageCount += @($entry.workflows).Count
}

$executionSourceFreshnessContract = Join-Path `
    $PSScriptRoot `
    'Test-LongGridExecutionSourceFreshness.ps1'
if (-not (Test-Path -LiteralPath $executionSourceFreshnessContract -PathType Leaf)) {
    throw "Execution source freshness contract is missing: $executionSourceFreshnessContract"
}
& $executionSourceFreshnessContract | Out-Host

[ordered]@{
    outcome = 'Pass'
    workflowCount = $documents.Count
    approvedActionTargets = @($manifest.actions).Count
    pinnedRemoteUsages = $usageCount
    mutableNegativeDifference = @($mutableDifferences | Where-Object { $_ -like 'mutable-ref:*' })[0]
    driftNegativeDifference = @($driftDifferences | Where-Object { $_ -like 'unapproved-pin:*' })[0]
    unknownNegativeDifference = @($unknownDifferences | Where-Object { $_ -like 'unapproved-action:*' })[0]
    consumerNegativeDifference = @($consumerDifferences | Where-Object { $_ -like 'consumer-drift:*' })[0]
} | ConvertTo-Json
