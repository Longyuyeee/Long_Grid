[CmdletBinding()]
param(
    [string]$ReadmePath = 'README.md',
    [string]$ExecutionPlanPath = 'docs/PRODUCT_EXECUTION_PLAN.md',
    [string]$FeatureBacklogPath = 'docs/153-product-feature-parity-development-plan.md',
    [string]$RoadmapPath = 'docs/04-roadmap.md'
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

function Read-RequiredText {
    param([Parameter(Mandatory)][string]$Path)

    $resolvedPath = Resolve-ProjectPath -Path $Path
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "Execution source document is missing: $resolvedPath"
    }
    return Get-Content -LiteralPath $resolvedPath -Raw -Encoding UTF8
}

function Get-LevelTwoSections {
    param([Parameter(Mandatory)][string]$Text)

    return @([regex]::Matches(
            $Text,
            '(?ms)^## [^\r\n]+\r?\n(?<body>.*?)(?=^## |\z)') |
        ForEach-Object { $_.Groups['body'].Value })
}

function Get-ExecutionSource {
    param([Parameter(Mandatory)][string]$PlanText)

    $planLead = if ($PlanText.Length -gt 2000) { $PlanText.Substring(0, 2000) } else { $PlanText }
    $pattern = '(?s)origin/main@(?<baseline>[0-9a-f]{7,40}).*?\[Stage (?<stage>[0-9]+)\]\((?<document>[0-9]+-[^)]+\.md)\)'
    $match = [regex]::Match($planLead, $pattern)
    if (-not $match.Success) {
        return $null
    }
    return [ordered]@{
        baseline = $match.Groups['baseline'].Value
        stage = [int]$match.Groups['stage'].Value
        document = $match.Groups['document'].Value
    }
}

function Get-FreshnessDifferences {
    param(
        [Parameter(Mandatory)][string]$ReadmeText,
        [Parameter(Mandatory)][string]$PlanText,
        [Parameter(Mandatory)][string]$BacklogText,
        [Parameter(Mandatory)][string]$RoadmapText
    )

    $differences = @()
    $source = Get-ExecutionSource -PlanText $PlanText
    if ($null -eq $source) {
        return @('execution-plan:canonical-source-missing')
    }

    $stageLabel = "Stage $($source.stage)"
    $relativeLink = "[$stageLabel]($($source.document))"
    $readmeLinkPattern = '\[[^\]]*' + [regex]::Escape($stageLabel) + '[^\]]*\]\(docs/' +
        [regex]::Escape($source.document) + '\)'
    $backlogLead = if ($BacklogText.Length -gt 3000) { $BacklogText.Substring(0, 3000) } else { $BacklogText }

    $planQueueText = Get-LevelTwoSections -Text $PlanText | Where-Object {
            $_.Contains('PF-008') -and
            $_.Contains('BOX-R1-C/D') -and
            $_.Contains('TASKBAR-R2B1-B') -and
            $_.Contains('FunctionFirst')
        } | Select-Object -First 1
    if ($null -eq $planQueueText) {
        $differences += 'execution-plan-current-queue:section-missing'
    }
    elseif (-not $planQueueText.Contains($relativeLink)) {
        $linkedStages = @([regex]::Matches(
                $planQueueText,
                '\[Stage (?<stage>[0-9]+)\]') |
            ForEach-Object { [int]$_.Groups['stage'].Value })
        $actualStage = if ($linkedStages.Count -gt 0) {
            ($linkedStages | Measure-Object -Maximum).Maximum
        }
        else {
            'missing'
        }
        $differences += "execution-plan-current-queue:audit-expected=$($source.stage)-actual=$actualStage"
    }

    $backlogMatch = [regex]::Match($backlogLead, 'origin/main@(?<baseline>[0-9a-f]{7,40})')
    if (-not $backlogMatch.Success) {
        $differences += 'feature-backlog:continuation-baseline-missing'
    }
    elseif ($backlogMatch.Groups['baseline'].Value -ne $source.baseline) {
        $differences += "feature-backlog:baseline-expected=$($source.baseline)-actual=$($backlogMatch.Groups['baseline'].Value)"
    }
    if (-not $BacklogText.Contains($relativeLink)) {
        $differences += "feature-backlog:audit-expected=$stageLabel"
    }

    $readmeSections = @(Get-LevelTwoSections -Text $ReadmeText)
    $statusBoundary = $ReadmeText.IndexOf('## ')
    $statusText = if ($statusBoundary -ge 0) { $ReadmeText.Substring(0, $statusBoundary) } else { '' }
    if ($statusText -notmatch $readmeLinkPattern) {
        $differences += "readme-status:audit-expected=$stageLabel"
    }

    $navigationText = $readmeSections | Where-Object {
            $_.Contains('<details>') -and $_.Contains('docs/PRODUCT_EXECUTION_PLAN.md')
        } | Select-Object -First 1
    if ($null -eq $navigationText) {
        $differences += 'readme-navigation:section-missing'
    }
    elseif ($navigationText -notmatch $readmeLinkPattern) {
        $differences += "readme-navigation:audit-expected=$stageLabel"
    }

    $continuationText = $readmeSections | Where-Object {
            $_.Contains('PF-008') -and
            $_.Contains('BOX-R1-C/D') -and
            $_.Contains('TASKBAR-R2B1-B')
        } | Select-Object -First 1
    if ($null -eq $continuationText) {
        $differences += 'readme-continuation:section-missing'
    }
    else {
        if ($continuationText -notmatch $readmeLinkPattern) {
            $actualStage = [regex]::Match($continuationText, '\[Stage (?<stage>[0-9]+)[^\]]*\]')
            $actualValue = if ($actualStage.Success) { $actualStage.Groups['stage'].Value } else { 'missing' }
            $differences += "readme-continuation:audit-expected=$($source.stage)-actual=$actualValue"
        }
        foreach ($requiredTerm in @(
                'PF-008',
                'FunctionFirst',
                'BOX-R1-C/D',
                'TASKBAR-R2B1-B',
                'Runtime',
                'Stage 216',
                '#23/#274')) {
            if (-not $continuationText.Contains($requiredTerm)) {
                $differences += "readme-continuation:missing=$requiredTerm"
            }
        }
    }

    $roadmapLead = if ($RoadmapText.Length -gt 3000) { $RoadmapText.Substring(0, 3000) } else { $RoadmapText }
    if (-not $roadmapLead.Contains($relativeLink)) {
        $differences += "roadmap-current-source:audit-expected=$stageLabel"
    }
    return $differences
}

$readme = Read-RequiredText -Path $ReadmePath
$plan = Read-RequiredText -Path $ExecutionPlanPath
$backlog = Read-RequiredText -Path $FeatureBacklogPath
$roadmap = Read-RequiredText -Path $RoadmapPath
$source = Get-ExecutionSource -PlanText $plan
$actualDifferences = @(Get-FreshnessDifferences `
        -ReadmeText $readme `
        -PlanText $plan `
        -BacklogText $backlog `
        -RoadmapText $roadmap)
if ($actualDifferences.Count -gt 0) {
    throw "Execution source freshness contract failed:`n$($actualDifferences -join "`n")"
}

$continuation = Get-LevelTwoSections -Text $readme | Where-Object {
        $_.Contains('PF-008') -and
        $_.Contains('BOX-R1-C/D') -and
        $_.Contains('TASKBAR-R2B1-B')
    } | Select-Object -First 1
$readmeLinkPattern = '\[[^\]]*Stage ' + $source.stage + '[^\]]*\]\(docs/' +
    [regex]::Escape($source.document) + '\)'
$staleContinuation = ([regex]::new($readmeLinkPattern)).Replace(
    $continuation,
    '[Stage 226](docs/226-current-development-and-requirement-alignment-audit.md)',
    1)
$negativeReadme = $readme.Replace($continuation, $staleContinuation)
$negativeDifferences = @(Get-FreshnessDifferences `
        -ReadmeText $negativeReadme `
        -PlanText $plan `
        -BacklogText $backlog `
        -RoadmapText $roadmap)
$expectedNegativeDifference = "readme-continuation:audit-expected=$($source.stage)-actual=226"
if ($negativeDifferences -notcontains $expectedNegativeDifference) {
    throw 'Execution source freshness contract accepted an intentionally stale README continuation audit.'
}

$planQueue = Get-LevelTwoSections -Text $plan | Where-Object {
        $_.Contains('PF-008') -and
        $_.Contains('BOX-R1-C/D') -and
        $_.Contains('TASKBAR-R2B1-B') -and
        $_.Contains('FunctionFirst')
    } | Select-Object -First 1
$stalePlanStage = [Math]::Max(0, $source.stage - 1)
$stalePlanQueue = $planQueue.Replace(
    "[Stage $($source.stage)]($($source.document))",
    "[Stage $stalePlanStage](stale-current-audit.md)")
$negativePlan = $plan.Replace($planQueue, $stalePlanQueue)
$negativePlanDifferences = @(Get-FreshnessDifferences `
        -ReadmeText $readme `
        -PlanText $negativePlan `
        -BacklogText $backlog `
        -RoadmapText $roadmap)
$expectedPlanDifference =
    "execution-plan-current-queue:audit-expected=$($source.stage)-actual=$stalePlanStage"
if ($negativePlanDifferences -notcontains $expectedPlanDifference) {
    throw 'Execution source freshness contract accepted an intentionally stale execution-plan current queue.'
}

[ordered]@{
    outcome = 'Pass'
    baseline = $source.baseline
    currentStage = $source.stage
    currentAudit = $source.document
    checkedDocuments = @(
        $ReadmePath,
        $ExecutionPlanPath,
        $FeatureBacklogPath,
        $RoadmapPath)
    negativeDifference = $expectedNegativeDifference
    negativePlanDifference = $expectedPlanDifference
    modifiesSystemState = $false
} | ConvertTo-Json -Depth 3
