param(
    [Parameter()]
    [ValidateRange(0.0, 1.0)]
    [double] $MinimumLineRate = 0.90,

    [Parameter()]
    [ValidateRange(0.0, 1.0)]
    [double] $MinimumBranchRate = 0.75,

    [Parameter()]
    [string] $ResultsDirectory = "TestResults"
)

$ErrorActionPreference = "Stop"

$coverageFiles = @(
    Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -File `
        -Filter "coverage.cobertura.xml"
)

if ($coverageFiles.Count -eq 0) {
    throw "No Cobertura coverage report was found under '$ResultsDirectory'."
}

$linesCovered = 0L
$linesValid = 0L
$branchesCovered = 0L
$branchesValid = 0L

foreach ($coverageFile in $coverageFiles) {
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($coverageFile.FullName, $settings)

    try {
        $document = [System.Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
        $coverage = $document.DocumentElement
        if ($null -eq $coverage -or $coverage.Name -ne "coverage") {
            throw "'$($coverageFile.FullName)' is not a Cobertura coverage report."
        }

        $linesCovered += [long] $coverage.GetAttribute("lines-covered")
        $linesValid += [long] $coverage.GetAttribute("lines-valid")
        $branchesCovered += [long] $coverage.GetAttribute("branches-covered")
        $branchesValid += [long] $coverage.GetAttribute("branches-valid")
    }
    finally {
        $reader.Dispose()
    }
}

if ($linesValid -le 0 -or $branchesValid -le 0) {
    throw "Coverage totals must contain positive line and branch counts."
}

$lineRate = $linesCovered / $linesValid
$branchRate = $branchesCovered / $branchesValid
Write-Host ("Coverage: lines {0:P2} ({1}/{2}), branches {3:P2} ({4}/{5})" -f `
    $lineRate, $linesCovered, $linesValid, `
    $branchRate, $branchesCovered, $branchesValid)

if ($lineRate -lt $MinimumLineRate) {
    throw ("Line coverage {0:P2} is below the required {1:P2}." -f `
        $lineRate, $MinimumLineRate)
}

if ($branchRate -lt $MinimumBranchRate) {
    throw ("Branch coverage {0:P2} is below the required {1:P2}." -f `
        $branchRate, $MinimumBranchRate)
}
