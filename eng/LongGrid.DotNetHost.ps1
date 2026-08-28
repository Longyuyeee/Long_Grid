function Resolve-LongGridDotNetHost {
    param([string]$WorkingDirectory)

    $candidates = [Collections.Generic.List[string]]::new()
    foreach ($programFilesRoot in @(
        $env:ProgramW6432,
        $env:ProgramFiles,
        [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::ProgramFiles))) {
        if (-not [string]::IsNullOrWhiteSpace($programFilesRoot)) {
            $candidates.Add((Join-Path $programFilesRoot 'dotnet\dotnet.exe'))
        }
    }

    foreach ($command in @(Get-Command dotnet -All -ErrorAction SilentlyContinue)) {
        if (-not [string]::IsNullOrWhiteSpace($command.Source)) {
            $candidates.Add($command.Source)
        }
    }

    $checked = [Collections.Generic.List[string]]::new()
    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            continue
        }

        $checked.Add($candidate)
        Push-Location $WorkingDirectory
        try {
            $version = @(& $candidate --version 2>$null)
            $exitCode = $LASTEXITCODE
        }
        catch {
            $exitCode = 1
            $version = @()
        }
        finally {
            Pop-Location
        }

        if ($exitCode -eq 0 -and
            -not [string]::IsNullOrWhiteSpace(($version -join ''))) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }

    $checkedText = if ($checked.Count -eq 0) {
        'no dotnet hosts were found'
    }
    else {
        $checked -join '; '
    }
    throw "No .NET SDK compatible with global.json was found. Checked: $checkedText"
}
