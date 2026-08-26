param(
    [string] $ManifestPath = (Join-Path $PSScriptRoot "..\src\UpxSharp\Stubs\Resources\stubs.manifest.json"),
    [string] $SolutionPath = (Join-Path $PSScriptRoot "..\UpxSharp.sln"),
    [string] $RealFixturesDir = $env:UPXSHARP_REAL_FIXTURES_DIR,
    [string] $UpxPackedFixturesDir = $env:UPXSHARP_UPX_PACKED_FIXTURES_DIR,
    [string] $FixtureInventoryPath = $(if ([string]::IsNullOrWhiteSpace($env:UPXSHARP_REAL_FIXTURES_INVENTORY)) { "C:\upxsharp-artifacts\fixture-inventory.json" } else { $env:UPXSHARP_REAL_FIXTURES_INVENTORY }),
    [string] $MissingReportPath = $(if ([string]::IsNullOrWhiteSpace($env:UPXSHARP_REAL_FIXTURES_MISSING_REPORT)) { "C:\upxsharp-artifacts\fixture-missing-report.json" } else { $env:UPXSHARP_REAL_FIXTURES_MISSING_REPORT }),
    [string] $AcquisitionPlanPath = $(if ([string]::IsNullOrWhiteSpace($env:UPXSHARP_FIXTURE_ACQUISITION_PLAN)) { "C:\upxsharp-artifacts\fixture-acquisition-plan.json" } else { $env:UPXSHARP_FIXTURE_ACQUISITION_PLAN }),
    [string] $ReportDir = $env:UPXSHARP_REAL_FIXTURES_REPORT_DIR,
    [string] $RepoRoot = (Join-Path $PSScriptRoot "..")
)

$ErrorActionPreference = "Stop"
$failures = New-Object System.Collections.Generic.List[string]
$recommendations = New-Object System.Collections.Generic.List[string]

function Add-Failure([string] $message) {
    $failures.Add($message) | Out-Null
}

function Test-FixtureDirectory([string] $name, [string] $path) {
    if ([string]::IsNullOrWhiteSpace($path)) {
        Add-Failure "$name is not set."
        return
    }

    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        Add-Failure "$name directory not found: $path"
        return
    }

    $files = Get-ChildItem -LiteralPath $path -File -Recurse |
        Where-Object { $_.Length -gt 0 } |
        Select-Object -First 1

    if ($null -eq $files) {
        Add-Failure "$name has no non-empty files: $path"
    }
}

function Test-IsRepoLocalPath([string] $path) {
    $repoRootFull = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $fullPath = [System.IO.Path]::GetFullPath($path)
    return $fullPath.StartsWith($repoRootFull, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-InventoryFile([string] $path, [string] $realRoot, [string] $packedRoot) {
    $expectedFormats = @(
        "AtariTos",
        "DosCom",
        "DosExe",
        "LinuxElf32Ppc",
        "LinuxElf64Ppc",
        "LinuxElfAmd64",
        "LinuxElfArm64LE",
        "LinuxElfArmLE",
        "LinuxElfMips",
        "LinuxElfMipsel",
        "LinuxElfRiscv64",
        "LinuxElfi386",
        "MachAMD64",
        "MachI386",
        "Ps1Exe",
        "Vmlinuzi386",
        "Win32Pe",
        "Win64Pe"
    )

    if ([string]::IsNullOrWhiteSpace($path)) {
        Add-Failure "UPXSHARP_REAL_FIXTURES_INVENTORY is not set."
        return
    }

    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Add-Failure "Fixture inventory not found: $path"
        $recommendations.Add("Build fixture inventory: .\tools\build-fixture-inventory.ps1 -RealFixturesDir <real-fixtures-dir> -UpxPackedFixturesDir <upx-packed-fixtures-dir>") | Out-Null
        $recommendations.Add("Fetch real fixture candidates: .\tools\fetch-real-fixture-candidates.ps1 -CandidateManifestPath C:\upxsharp-artifacts\fixture-candidate-sources.json") | Out-Null
        $recommendations.Add("Import external fixtures: .\tools\import-external-fixtures.ps1 -SourceDir <external-binaries-dir>") | Out-Null
        return
    }

    $inventory = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($null -eq $inventory.fixtures) {
        Add-Failure "Fixture inventory has no fixtures: $path"
        $recommendations.Add("Build fixture inventory: .\tools\build-fixture-inventory.ps1 -RealFixturesDir <real-fixtures-dir> -UpxPackedFixturesDir <upx-packed-fixtures-dir>") | Out-Null
    }

    $formats = @($inventory.fixtures | ForEach-Object { $_.format })
    $missingFormats = @($expectedFormats | Where-Object { $formats -notcontains $_ })
    if ($missingFormats.Count -gt 0) {
        Add-Failure ("Fixture inventory is missing implemented formats: " + ($missingFormats -join ", "))
        $recommendations.Add("Fetch real fixture candidates: .\tools\fetch-real-fixture-candidates.ps1 -CandidateManifestPath C:\upxsharp-artifacts\fixture-candidate-sources.json") | Out-Null
        $recommendations.Add("Import external fixtures: .\tools\import-external-fixtures.ps1 -SourceDir <external-binaries-dir>") | Out-Null
    }

    foreach ($entry in $inventory.fixtures) {
        if ([string]::IsNullOrWhiteSpace($entry.format)) {
            Add-Failure "Fixture inventory has an entry with empty format."
        }

        if ([string]::IsNullOrWhiteSpace($entry.path)) {
            Add-Failure "Fixture inventory has empty path for $($entry.format)."
        }
        elseif (-not [string]::IsNullOrWhiteSpace($realRoot)) {
            $realPath = if ([System.IO.Path]::IsPathRooted($entry.path)) { $entry.path } else { Join-Path $realRoot $entry.path }
            if (-not (Test-Path -LiteralPath $realPath -PathType Leaf)) {
                Add-Failure "Real fixture missing for $($entry.format): $realPath"
            }
            elseif (Test-IsRepoLocalPath $realPath) {
                Add-Failure "Real fixture for $($entry.format) is inside repository and cannot close external gate: $realPath"
            }
        }

        if ([string]::IsNullOrWhiteSpace($entry.packedPath)) {
            Add-Failure "Fixture inventory has empty packedPath for $($entry.format)."
        }
        elseif (-not [string]::IsNullOrWhiteSpace($packedRoot)) {
            $packedPath = if ([System.IO.Path]::IsPathRooted($entry.packedPath)) { $entry.packedPath } else { Join-Path $packedRoot $entry.packedPath }
            if (-not (Test-Path -LiteralPath $packedPath -PathType Leaf)) {
                Add-Failure "UPX-packed fixture missing for $($entry.format): $packedPath"
            }
            elseif (Test-IsRepoLocalPath $packedPath) {
                Add-Failure "UPX-packed fixture for $($entry.format) is inside repository and cannot close external gate: $packedPath"
            }
        }
    }
}

function Write-MissingReportSummary([string] $path) {
    try {
        $parsed = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $items = New-Object System.Collections.Generic.List[object]
        foreach ($item in $parsed) {
            $items.Add($item) | Out-Null
        }
        if ($items.Count -eq 0) {
            return
        }

        Write-Host "Missing fixture status summary:"
        foreach ($group in ($items | Group-Object status | Sort-Object Name)) {
            Write-Host " - $($group.Name): $($group.Count)"
        }
    }
    catch {
        Write-Host "Could not read missing fixture report summary: $($_.Exception.Message)"
    }
}

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    Add-Failure "Stub manifest not found: $ManifestPath"
}
else {
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ($null -eq $manifest.stubs -or $manifest.stubs.Count -eq 0) {
        Add-Failure "Stub manifest has no stubs: $ManifestPath"
    }
    else {
        $fakeStubs = @($manifest.stubs | Where-Object { $_.kind -ne "real" })
        if ($fakeStubs.Count -gt 0) {
            $names = ($fakeStubs | ForEach-Object { "$($_.format):$($_.kind)" }) -join ", "
            Add-Failure "Stub manifest still contains non-real stubs: $names"
            $defaultRealStubs = "C:\upxsharp-artifacts\real-stubs"
            if (Test-Path -LiteralPath $defaultRealStubs -PathType Container) {
                $recommendations.Add("Import real stubs: .\tools\import-real-stubs.ps1 -SourceDir $defaultRealStubs") | Out-Null
            }
            else {
                $recommendations.Add("Fetch official artifacts: .\tools\fetch-upx-official-artifacts.ps1") | Out-Null
                $recommendations.Add("Import real stubs: .\tools\import-real-stubs.ps1 -SourceDir <real-stubs-dir>") | Out-Null
            }
        }
    }
}

Test-FixtureDirectory "UPXSHARP_REAL_FIXTURES_DIR" $RealFixturesDir
Test-FixtureDirectory "UPXSHARP_UPX_PACKED_FIXTURES_DIR" $UpxPackedFixturesDir
Test-InventoryFile $FixtureInventoryPath $RealFixturesDir $UpxPackedFixturesDir

if ($failures.Count -gt 0) {
    Write-Host "BLOCKED: external artifacts are missing or incomplete."
    if (Test-Path -LiteralPath $MissingReportPath -PathType Leaf) {
        Write-Host "Missing fixture report: $MissingReportPath"
        Write-MissingReportSummary $MissingReportPath
    }
    if (Test-Path -LiteralPath $AcquisitionPlanPath -PathType Leaf) {
        Write-Host "Fixture acquisition plan: $AcquisitionPlanPath"
    }
    if ($recommendations.Count -gt 0) {
        Write-Host "Recommended next commands:"
        foreach ($recommendation in $recommendations) {
            Write-Host " - $recommendation"
        }
    }
    Write-Error ("External artifact verification failed:`n - " + ($failures -join "`n - "))
    exit 1
}

Write-Host "READY: external artifacts found. Running strict real-fixture test gate..."

if ([string]::IsNullOrWhiteSpace($ReportDir)) {
    $ReportDir = Join-Path ([System.IO.Path]::GetTempPath()) "upxsharp-real-fixture-reports"
}
New-Item -ItemType Directory -Path $ReportDir -Force | Out-Null

$env:UPXSHARP_REAL_FIXTURES_DIR = $RealFixturesDir
$env:UPXSHARP_UPX_PACKED_FIXTURES_DIR = $UpxPackedFixturesDir
$env:UPXSHARP_REAL_FIXTURES_INVENTORY = $FixtureInventoryPath
$env:UPXSHARP_REAL_FIXTURES_REPORT_DIR = $ReportDir
$env:UPXSHARP_REAL_FIXTURES_STRICT = "1"

dotnet test $SolutionPath
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "External artifact verification passed."
Write-Host "Reports: $ReportDir"
