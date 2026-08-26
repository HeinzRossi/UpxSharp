param(
    [string] $ArtifactsRoot = "C:\upxsharp-artifacts",
    [string] $MissingReportPath,
    [string] $RealFixturesDir,
    [string] $UpxPackedFixturesDir,
    [string] $UpxExePath,
    [string] $CliProjectPath,
    [string] $OutputReportPath,
    [string] $DiagnosticRestoredDir,
    [switch] $DryRun,
    [switch] $Strict
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($MissingReportPath)) {
    $MissingReportPath = Join-Path $ArtifactsRoot "fixture-missing-report.json"
}
if ([string]::IsNullOrWhiteSpace($RealFixturesDir)) {
    $RealFixturesDir = Join-Path $ArtifactsRoot "real-fixtures"
}
if ([string]::IsNullOrWhiteSpace($UpxPackedFixturesDir)) {
    $UpxPackedFixturesDir = Join-Path $ArtifactsRoot "upx-packed-fixtures"
}
if ([string]::IsNullOrWhiteSpace($UpxExePath)) {
    $UpxExePath = Join-Path $ArtifactsRoot "upx-bin\upx.exe"
}
if ([string]::IsNullOrWhiteSpace($CliProjectPath)) {
    $CliProjectPath = Join-Path $PSScriptRoot "..\src\UpxSharp.Cli\UpxSharp.Cli.csproj"
}
if ([string]::IsNullOrWhiteSpace($OutputReportPath)) {
    $OutputReportPath = Join-Path $ArtifactsRoot "materialized-official-orphans-report.json"
}
if ([string]::IsNullOrWhiteSpace($DiagnosticRestoredDir)) {
    $DiagnosticRestoredDir = Join-Path $ArtifactsRoot "diagnostic-restored-fixtures"
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

function Get-SafeFileName([string] $Format, [string] $PackedPath) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($PackedPath)
    foreach ($invalid in [System.IO.Path]::GetInvalidFileNameChars()) {
        $name = $name.Replace($invalid, "_")
    }

    return "$Format-official-restored-$name"
}

function Invoke-Detector([string] $Path) {
    $arguments = @("run", "--project", ([System.IO.Path]::GetFullPath($CliProjectPath)), "--no-restore", "--", "--detect-fixture", "pack", $Path)
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
        throw "Fixture detector failed. $stdout $stderr"
    }

    return @(($stdout -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1) | ConvertFrom-Json)[0]
}

function Invoke-UpxDecompress([string] $PackedPath, [string] $OutputPath) {
    $tempPath = $OutputPath + ".tmp"
    if (Test-Path -LiteralPath $tempPath -PathType Leaf) {
        Remove-Item -LiteralPath $tempPath -Force
    }

    Copy-Item -LiteralPath $PackedPath -Destination $tempPath -Force

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($UpxExePath)
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Arguments = Join-ProcessArguments @("-d", $tempPath)

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    if ($process.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $tempPath -PathType Leaf) {
            Remove-Item -LiteralPath $tempPath -Force
        }
        throw "Official UPX decompression failed. $stdout $stderr"
    }

    Move-Item -LiteralPath $tempPath -Destination $OutputPath -Force
}

function Get-HeaderMagic([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $length = [Math]::Min(16, [int]$stream.Length)
        $buffer = New-Object byte[] $length
        [void]$stream.Read($buffer, 0, $length)
        return ($buffer | ForEach-Object { $_.ToString("X2") }) -join " "
    }
    finally {
        $stream.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $MissingReportPath -PathType Leaf)) {
    throw "Missing report not found: $MissingReportPath"
}
if (-not (Test-Path -LiteralPath $UpxPackedFixturesDir -PathType Container)) {
    throw "UPX-packed fixtures directory not found: $UpxPackedFixturesDir"
}
if (-not (Test-Path -LiteralPath $UpxExePath -PathType Leaf)) {
    throw "Official UPX executable not found: $UpxExePath"
}

$missingItems = New-Object System.Collections.Generic.List[object]
foreach ($item in (Get-Content -LiteralPath $MissingReportPath -Raw | ConvertFrom-Json)) {
    $missingItems.Add($item) | Out-Null
}

$orphans = @($missingItems | Where-Object { [string]$_.status -eq "PackedOrphan" -and -not [string]::IsNullOrWhiteSpace($_.detectedPacked) })
$report = New-Object System.Collections.Generic.List[object]
$failures = New-Object System.Collections.Generic.List[string]

if (-not $DryRun) {
    New-Item -ItemType Directory -Path $RealFixturesDir -Force | Out-Null
    New-Item -ItemType Directory -Path $DiagnosticRestoredDir -Force | Out-Null
}

foreach ($orphan in $orphans) {
    $format = [string]$orphan.format
    $packedPath = if ([System.IO.Path]::IsPathRooted([string]$orphan.detectedPacked)) {
        [string]$orphan.detectedPacked
    }
    else {
        Join-Path $UpxPackedFixturesDir ([string]$orphan.detectedPacked)
    }
    $outputName = Get-SafeFileName -Format $format -PackedPath $packedPath
    $outputPath = Join-Path $RealFixturesDir $outputName
    $status = "DryRun"
    $errorMessage = $null
    $detectedFormat = $null
    $diagnosticPath = $null

    try {
        if (-not (Test-Path -LiteralPath $packedPath -PathType Leaf)) {
            throw "Packed orphan file not found: $packedPath"
        }

        if (-not $DryRun) {
            Invoke-UpxDecompress -PackedPath $packedPath -OutputPath $outputPath
            $status = "RestoredButNotRecognized"
            $detection = Invoke-Detector $outputPath
            $detectedFormat = if ($null -eq $detection.Format) { $null } else { [string]$detection.Format }
            if ($detection.Recognized -ne $true) {
                throw "Restored original was not recognized: $($detection.Error)"
            }
            if ($detectedFormat -ne $format) {
                $status = "DetectedWrongFormat"
                throw "Expected $format, detected $detectedFormat"
            }
            $status = "Materialized"
        }
    }
    catch {
        if ($status -eq "DryRun") {
            $status = "UpxDecompressFailed"
        }
        $errorMessage = $_.Exception.Message
        Write-Warning "$format failed: $errorMessage"
        if (Test-Path -LiteralPath $outputPath -PathType Leaf) {
            $diagnosticPath = Join-Path $DiagnosticRestoredDir $outputName
            Move-Item -LiteralPath $outputPath -Destination $diagnosticPath -Force
        }
        if ($Strict) {
            $failures.Add("${format}: $errorMessage") | Out-Null
        }
    }

    $report.Add([ordered]@{
        format = $format
        packedPath = $packedPath
        originalPath = if ($DryRun) { $outputPath } elseif (Test-Path -LiteralPath $outputPath -PathType Leaf) { $outputPath } else { $null }
        diagnosticPath = $diagnosticPath
        tool = $UpxExePath
        detectedFormat = $detectedFormat
        size = if (Test-Path -LiteralPath $outputPath -PathType Leaf) { (Get-Item -LiteralPath $outputPath).Length } elseif ($diagnosticPath -and (Test-Path -LiteralPath $diagnosticPath -PathType Leaf)) { (Get-Item -LiteralPath $diagnosticPath).Length } else { $null }
        headerMagic = if (Test-Path -LiteralPath $outputPath -PathType Leaf) { Get-HeaderMagic $outputPath } elseif ($diagnosticPath -and (Test-Path -LiteralPath $diagnosticPath -PathType Leaf)) { Get-HeaderMagic $diagnosticPath } else { $null }
        status = $status
        error = $errorMessage
    }) | Out-Null
}

$report | Format-Table format, status, detectedFormat, originalPath -AutoSize | Out-String | Write-Host

if (-not $DryRun) {
    $parent = Split-Path -Parent $OutputReportPath
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    $reportArray = @($report | ForEach-Object { $_ })
    $json = if ($reportArray.Count -eq 1) {
        "[" + ($reportArray[0] | ConvertTo-Json -Depth 5) + "]"
    }
    else {
        $reportArray | ConvertTo-Json -Depth 5
    }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($OutputReportPath, $json + [Environment]::NewLine, $utf8NoBom)
    Write-Host "Wrote materialized orphan report: $OutputReportPath"
}

foreach ($group in ($report | Group-Object { if ($_ -is [System.Collections.IDictionary]) { $_["status"] } else { $_.status } } | Sort-Object Name)) {
    Write-Host " - $($group.Name): $($group.Count)"
}

if ($failures.Count -gt 0) {
    throw ("Could not materialize all official orphan fixtures:`n - " + ($failures -join "`n - "))
}
