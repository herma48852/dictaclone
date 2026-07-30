[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ResultsDirectory,

    [Parameter(Mandatory)]
    [string] $AssemblyName,

    [ValidateRange(0, 100)]
    [double] $MinimumLinePercent
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$reports = @(
    Get-ChildItem `
        -LiteralPath $ResultsDirectory `
        -Recurse `
        -Filter 'coverage.cobertura.xml'
)

if ($reports.Count -eq 0) {
    throw "No Cobertura coverage reports were found under $ResultsDirectory."
}

$lineHits = @{}

foreach ($report in $reports) {
    [xml] $coverage = Get-Content -LiteralPath $report.FullName -Raw
    $packages = @($coverage.coverage.packages.package)

    foreach ($package in $packages) {
        if ($package.name -ne $AssemblyName) {
            continue
        }

        foreach ($class in @($package.classes.class)) {
            $fileName = $class.filename.Replace('\', '/')
            $assemblyPathMarker = "$AssemblyName/"
            $markerIndex = $fileName.IndexOf(
                $assemblyPathMarker,
                [StringComparison]::OrdinalIgnoreCase)
            if ($markerIndex -ge 0) {
                $fileName = $fileName.Substring(
                    $markerIndex + $assemblyPathMarker.Length)
            }

            foreach ($line in @($class.lines.line)) {
                $key = "$fileName`:$($line.number)"
                $hits = [int] $line.hits

                if (!$lineHits.ContainsKey($key) -or $hits -gt $lineHits[$key]) {
                    $lineHits[$key] = $hits
                }
            }
        }
    }
}

if ($lineHits.Count -eq 0) {
    throw "Assembly '$AssemblyName' was not found in the coverage reports."
}

$coveredLineCount = @($lineHits.Values | Where-Object { $_ -gt 0 }).Count
$linePercent = 100 * $coveredLineCount / $lineHits.Count

Write-Output (
    '{0} line coverage: {1:N2}% ({2}/{3}); required: {4:N2}%' -f `
        $AssemblyName,
        $linePercent,
        $coveredLineCount,
        $lineHits.Count,
        $MinimumLinePercent)

if ($linePercent -lt $MinimumLinePercent) {
    throw (
        '{0} line coverage {1:N2}% is below the required {2:N2}%.' -f `
            $AssemblyName,
            $linePercent,
            $MinimumLinePercent)
}
