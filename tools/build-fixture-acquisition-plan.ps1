param(
    [string] $MissingReportPath = "C:\upxsharp-artifacts\fixture-missing-report.json",
    [string] $OutputPath = "C:\upxsharp-artifacts\fixture-acquisition-plan.json",
    [string] $CandidateReportPath = "C:\upxsharp-artifacts\real-fixture-candidates-report.json",
    [string] $PackedFixtureReportPath = "C:\upxsharp-artifacts\reports\upxsharp-upx-packed-fixtures-report.json"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $MissingReportPath -PathType Leaf)) {
    throw "Missing report not found: $MissingReportPath"
}

$items = New-Object System.Collections.Generic.List[object]
foreach ($item in (Get-Content -LiteralPath $MissingReportPath -Raw | ConvertFrom-Json)) {
    $items.Add($item) | Out-Null
}

$candidateItems = New-Object System.Collections.Generic.List[object]
if (Test-Path -LiteralPath $CandidateReportPath -PathType Leaf) {
    foreach ($item in (Get-Content -LiteralPath $CandidateReportPath -Raw | ConvertFrom-Json)) {
        $candidateItems.Add($item) | Out-Null
    }
}

$packedReportItems = New-Object System.Collections.Generic.List[object]
if (Test-Path -LiteralPath $PackedFixtureReportPath -PathType Leaf) {
    foreach ($item in (Get-Content -LiteralPath $PackedFixtureReportPath -Raw | ConvertFrom-Json)) {
        $packedReportItems.Add($item) | Out-Null
    }
}

$plan = New-Object System.Collections.Generic.List[object]
foreach ($item in ($items | Sort-Object format)) {
    $format = [string]$item.format
    $safeName = $format.ToLowerInvariant()
    $packed = if ([string]::IsNullOrWhiteSpace($item.detectedPacked)) { $null } else { [string]$item.detectedPacked }
    $candidate = $candidateItems |
        Where-Object { [string]$_.format -eq $format } |
        Sort-Object @{ Expression = { if ([string]$_.status -eq "CopiedRecognized") { 0 } else { 1 } } } |
        Select-Object -First 1
    $packedReport = $packedReportItems |
        Where-Object { [string]$_.expectedFormat -eq $format } |
        Select-Object -First 1
    $packedFixtureStatus = if ($null -eq $packedReport) { $null } else { [string]$packedReport.status }
    $packedFixtureBlocker = switch ($packedFixtureStatus) {
        "OfficialElfUnsupportedLayout" { "UnsupportedOfficialElfLayout" }
        "OfficialUpxMetadataError" { "CorruptOrIncompleteOfficialMetadata" }
        "OfficialUpxDecompressError" { "OfficialPayloadDecompressionFailed" }
        "OfficialUpxUnsupported" { "NativeOfficialUnpackNotImplemented" }
        "UpxPackedUnpackOk" { $null }
        { [string]::IsNullOrWhiteSpace($_) } { $null }
        default { $packedFixtureStatus }
    }

    $plan.Add([ordered]@{
        format = $format
        needsOriginal = [bool]$item.needsOriginal
        needsPacked = [bool]$item.needsPacked
        suggestedOriginalName = "$safeName-original.bin"
        suggestedPackedName = "$safeName-original.bin.upx"
        currentPackedOrphan = $packed
        candidateStatus = if ($null -eq $candidate) { $null } else { [string]$candidate.status }
        candidateSourceName = if ($null -eq $candidate) { $null } else { [string]$candidate.sourceName }
        packedFixtureStatus = $packedFixtureStatus
        packedFixtureBlocker = $packedFixtureBlocker
        packedFixtureError = if ($null -eq $packedReport) { $null } else { [string]$packedReport.error }
        status = [string]$item.status
    }) | Out-Null
}

$parent = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
}

$planArray = @($plan | ForEach-Object { $_ })
$json = if ($planArray.Count -eq 1) {
    "[" + ($planArray[0] | ConvertTo-Json -Depth 4) + "]"
}
else {
    $planArray | ConvertTo-Json -Depth 4
}
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($OutputPath, $json + [Environment]::NewLine, $utf8NoBom)

Write-Host "Wrote fixture acquisition plan: $OutputPath"
foreach ($group in ($plan | Group-Object { if ($_ -is [System.Collections.IDictionary]) { $_["status"] } else { $_.status } } | Sort-Object Name)) {
    Write-Host " - $($group.Name): $($group.Count)"
}
