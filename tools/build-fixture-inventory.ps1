param(
    [string] $RealFixturesDir = "C:\upxsharp-artifacts\real-fixtures",
    [string] $UpxPackedFixturesDir = "C:\upxsharp-artifacts\upx-packed-fixtures",
    [string] $OutputPath = "C:\upxsharp-artifacts\fixture-inventory.json",
    [string] $MissingReportPath,
    [string] $CliProjectPath = (Join-Path $PSScriptRoot "..\src\UpxSharp.Cli\UpxSharp.Cli.csproj"),
    [string] $RepoRoot = (Join-Path $PSScriptRoot ".."),
    [switch] $DryRun
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($MissingReportPath)) {
    $outputDirectory = Split-Path -Parent $OutputPath
    if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
        $outputDirectory = "."
    }

    $MissingReportPath = Join-Path $outputDirectory "fixture-missing-report.json"
}

$resolvedRepoRoot = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

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

function Get-AllowedFiles([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return @()
    }

    $allowed = @("", ".exe", ".elf", ".bin", ".com", ".upx")
    return @(Get-ChildItem -LiteralPath $Path -File -Recurse |
        Where-Object { $_.Length -gt 0 -and $_.Length -le 32MB -and $allowed -contains $_.Extension.ToLowerInvariant() } |
        Sort-Object FullName)
}

function Get-RelativePath([string] $Root, [string] $Path) {
    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $pathFull = [System.IO.Path]::GetFullPath($Path)
    if ($pathFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $pathFull.Substring($rootFull.Length)
    }

    return $pathFull
}

function Test-IsRepoLocalPath([string] $Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    return $fullPath.StartsWith($resolvedRepoRoot, [System.StringComparison]::OrdinalIgnoreCase)
}

function Join-ProcessArguments([string[]] $Arguments) {
    return ($Arguments | ForEach-Object {
        if ($_ -notmatch '[\s"]') {
            $_
        }
        else {
            '"' + ($_.Replace('\', '\\').Replace('"', '\"')) + '"'
        }
    }) -join " "
}

function Invoke-Detector([string] $Mode, [System.IO.FileInfo[]] $Files, [string] $ProjectPath) {
    if ($Files.Count -eq 0) {
        return @()
    }

    $arguments = @("run", "--project", $ProjectPath, "--no-restore", "--", "--detect-fixture", $Mode)
    $arguments += @($Files | ForEach-Object { $_.FullName })
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new("dotnet")
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Arguments = Join-ProcessArguments $arguments

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    if ($process.ExitCode -ne 0) {
        if (-not [string]::IsNullOrWhiteSpace($stdout)) { Write-Error $stdout }
        if (-not [string]::IsNullOrWhiteSpace($stderr)) { Write-Error $stderr }
        throw "Fixture detector failed for mode '$Mode'. Run dotnet build UpxSharp.sln first if assets are missing."
    }

    return @(($stdout -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1) | ConvertFrom-Json)
}

function Write-MissingReportSummary([System.Collections.IEnumerable] $Report) {
    $items = @($Report)
    $summary = $items | ForEach-Object {
        if ($_ -is [System.Collections.IDictionary]) {
            $_["status"]
        }
        else {
            $_.status
        }
    } | Group-Object | Sort-Object Name
    Write-Host "Fixture status summary:"
    foreach ($group in $summary) {
        Write-Host " - $($group.Name): $($group.Count)"
    }

    if ($items | Where-Object { if ($_ -is [System.Collections.IDictionary]) { $_["needsOriginal"] -eq $true } else { $_.needsOriginal -eq $true } } | Select-Object -First 1) {
        Write-Host "Next: add original binaries to $RealFixturesDir"
    }
    elseif ($items | Where-Object { if ($_ -is [System.Collections.IDictionary]) { $_["needsPacked"] -eq $true } else { $_.needsPacked -eq $true } } | Select-Object -First 1) {
        Write-Host "Next: add official UPX-packed binaries to $UpxPackedFixturesDir"
    }
    else {
        Write-Host "Next: run tools\verify-external-artifacts.ps1"
    }
}

if (-not (Test-Path -LiteralPath $RealFixturesDir -PathType Container)) {
    throw "RealFixturesDir not found: $RealFixturesDir"
}

if (-not (Test-Path -LiteralPath $UpxPackedFixturesDir -PathType Container)) {
    throw "UpxPackedFixturesDir not found: $UpxPackedFixturesDir"
}

$cliProject = [System.IO.Path]::GetFullPath($CliProjectPath)
$realFiles = Get-AllowedFiles $RealFixturesDir
$packedFiles = Get-AllowedFiles $UpxPackedFixturesDir
$realResults = Invoke-Detector -Mode "pack" -Files $realFiles -ProjectPath $cliProject
$packedResults = Invoke-Detector -Mode "unpack" -Files $packedFiles -ProjectPath $cliProject

$repoLocalReal = @($realResults | Where-Object { $_.Recognized -eq $true -and (Test-IsRepoLocalPath $_.File) })
$repoLocalPacked = @($packedResults | Where-Object { $_.Recognized -eq $true -and (Test-IsRepoLocalPath $_.File) })
foreach ($item in ($repoLocalReal + $repoLocalPacked)) {
    Write-Warning "Repo-local fixture skipped; external fixture required: $($item.File)"
}

$realResults = @($realResults | Where-Object { -not (Test-IsRepoLocalPath $_.File) })
$packedResults = @($packedResults | Where-Object { -not (Test-IsRepoLocalPath $_.File) })

$recognizedReal = @($realResults | Where-Object { $_.Recognized -eq $true })
$recognizedPacked = @($packedResults | Where-Object { $_.Recognized -eq $true })
$unrecognized = @($realResults + $packedResults | Where-Object { $_.Recognized -ne $true })

foreach ($item in $unrecognized) {
    Write-Warning "Unrecognized fixture skipped: $($item.File) - $($item.Error)"
}

$duplicates = @($recognizedReal | Group-Object Format | Where-Object { $_.Count -gt 1 })
if ($duplicates.Count -gt 0) {
    $names = ($duplicates | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ", "
    throw "Duplicate original fixtures by format. Keep one original per format: $names"
}

$fixtures = New-Object System.Collections.Generic.List[object]
$rows = New-Object System.Collections.Generic.List[object]
$missingReport = New-Object System.Collections.Generic.List[object]

foreach ($format in $expectedFormats) {
    $real = $recognizedReal | Where-Object { $_.Format -eq $format } | Select-Object -First 1
    $packed = $null
    if ($null -ne $real) {
        $expectedPackedName = [System.IO.Path]::GetFileName($real.File) + ".upx"
        $packed = $recognizedPacked |
            Where-Object {
                $_.Format -eq $format -and
                [string]::Equals(
                    [System.IO.Path]::GetFileName($_.File),
                    $expectedPackedName,
                    [System.StringComparison]::OrdinalIgnoreCase)
            } |
            Select-Object -First 1
    }
    else {
        $packed = $recognizedPacked | Where-Object { $_.Format -eq $format } | Select-Object -First 1
    }
    $status = if ($null -eq $real -and $null -ne $packed) { "PackedOrphan" } elseif ($null -eq $real) { "MissingOriginal" } elseif ($null -eq $packed) { "MissingPacked" } else { "Ready" }
    $originalPath = if ($null -eq $real) { $null } else { Get-RelativePath $RealFixturesDir $real.File }
    $packedPath = if ($null -eq $packed) { $null } else { Get-RelativePath $UpxPackedFixturesDir $packed.File }

    $rows.Add([pscustomobject]@{
        Format = $format
        Original = if ($null -eq $originalPath) { "" } else { $originalPath }
        Packed = if ($null -eq $packedPath) { "" } else { $packedPath }
        Status = $status
    }) | Out-Null

    $missingReport.Add([ordered]@{
        format = $format
        needsOriginal = $null -eq $real
        needsPacked = $null -eq $packed
        detectedOriginal = $originalPath
        detectedPacked = $packedPath
        status = $status
    }) | Out-Null

    if ($null -ne $real) {
        $fixtures.Add([ordered]@{
            format = $format
            path = $originalPath
            packedPath = $packedPath
        }) | Out-Null
    }
}

$orphanPacked = @($recognizedPacked | Where-Object {
    $packedFormat = $_.Format
    $matchingReal = $recognizedReal | Where-Object { $_.Format -eq $packedFormat } | Select-Object -First 1
    if ($null -eq $matchingReal) {
        return $true
    }

    $expectedPackedName = [System.IO.Path]::GetFileName($matchingReal.File) + ".upx"
    return -not [string]::Equals(
        [System.IO.Path]::GetFileName($_.File),
        $expectedPackedName,
        [System.StringComparison]::OrdinalIgnoreCase)
})

foreach ($item in $orphanPacked) {
    Write-Warning "Packed fixture has no matching original and will not be added to inventory: $($item.Format) $($item.File)"
}

$rows | Format-Table -AutoSize | Out-String | Write-Host

if ($DryRun) {
    Write-Host "Dry run: inventory not written."
    exit 0
}

$parent = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
}

$inventory = [ordered]@{ fixtures = $fixtures }
$json = $inventory | ConvertTo-Json -Depth 4
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($OutputPath, $json + [Environment]::NewLine, $utf8NoBom)

Write-Host "Wrote fixture inventory: $OutputPath"

$reportParent = Split-Path -Parent $MissingReportPath
if (-not [string]::IsNullOrWhiteSpace($reportParent)) {
    New-Item -ItemType Directory -Path $reportParent -Force | Out-Null
}

$reportJson = $missingReport | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText($MissingReportPath, $reportJson + [Environment]::NewLine, $utf8NoBom)
Write-Host "Wrote missing fixture report: $MissingReportPath"
Write-MissingReportSummary $missingReport
