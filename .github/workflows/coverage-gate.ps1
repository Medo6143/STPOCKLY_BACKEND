<#
.SYNOPSIS
    Line-coverage gate for Stockly.Domain and Stockly.Application (AGENTS.md section 8: at least 80 %).

.DESCRIPTION
    Reads every Cobertura file that `dotnet test --collect:"XPlat Code Coverage"` wrote under -ResultsDirectory (one per
    test project), merges them per assembly (a line counts as covered when any test project covered it) and fails when
    an assembly in -Assemblies is below -Threshold. No extra tool or package: plain PowerShell and XML.
    Used by .github/workflows/backend.yml; runs the same way locally (Windows PowerShell 5.1 or pwsh 7).
#>
param(
    [Parameter(Mandatory = $true)][string] $ResultsDirectory,
    [string[]] $Assemblies = @('Stockly.Domain', 'Stockly.Application'),
    [double] $Threshold = 80
)

$ErrorActionPreference = 'Stop'

$files = @(Get-ChildItem -Path $ResultsDirectory -Recurse -Filter 'coverage.cobertura.xml')
if ($files.Count -eq 0) {
    Write-Error "No coverage.cobertura.xml under $ResultsDirectory. Run dotnet test --collect:""XPlat Code Coverage"" first."
}

# assembly -> set of "file|line" -> covered (bool)
$lines = @{}
foreach ($assembly in $Assemblies) { $lines[$assembly] = @{} }

foreach ($file in $files) {
    [xml] $report = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($package in $report.coverage.packages.package) {
        $name = [string] $package.name
        if (-not $lines.ContainsKey($name)) { continue }
        $set = $lines[$name]
        foreach ($class in $package.classes.class) {
            $source = [string] $class.filename
            foreach ($line in $class.lines.line) {
                $key = "$source|$($line.number)"
                $hit = [int64] $line.hits -gt 0
                if ($set.ContainsKey($key)) {
                    if ($hit) { $set[$key] = $true }
                } else {
                    $set[$key] = $hit
                }
            }
        }
    }
}

$failed = $false
foreach ($assembly in $Assemblies) {
    $set = $lines[$assembly]
    $total = $set.Count
    if ($total -eq 0) {
        Write-Host "::error::$assembly has no coverage data (was it built and tested?)"
        $failed = $true
        continue
    }

    $covered = @($set.Values | Where-Object { $_ }).Count
    $percent = [math]::Round(100.0 * $covered / $total, 2)
    $message = "$assembly line coverage: $percent % ($covered of $total lines), threshold $Threshold %"
    if ($percent -lt $Threshold) {
        Write-Host "::error::$message"
        $failed = $true
    } else {
        Write-Host $message
    }
}

if ($failed) { exit 1 }
