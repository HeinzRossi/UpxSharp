param(
    [Parameter(Mandatory = $true)]
    [string] $SourceDir,
    [string] $ArtifactsRoot = "C:\upxsharp-artifacts",
    [string] $RepoRoot,
    [string] $CliProjectPath,
    [switch] $Strict,
    [switch] $DryRun
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Join-Path $PSScriptRoot ".."
}

if ([string]::IsNullOrWhiteSpace($CliProjectPath)) {
    $CliProjectPath = Join-Path $PSScriptRoot "..\src\UpxSharp.Cli\UpxSharp.Cli.csproj"
}

$realFixturesDir = Join-Path $ArtifactsRoot "real-fixtures"
$packedFixturesDir = Join-Path $ArtifactsRoot "upx-packed-fixtures"
$inventoryPath = Join-Path $ArtifactsRoot "fixture-inventory.json"
$missingReportPath = Join-Path $ArtifactsRoot "fixture-missing-report.json"
$upxExePath = Join-Path $ArtifactsRoot "upx-bin\upx.exe"
$resolvedRepoRoot = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

function Test-IsRepoLocalPath([string] $Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    return $fullPath.StartsWith($resolvedRepoRoot, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-AllowedFiles([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "SourceDir not found: $Path"
    }

    $allowed = @("", ".exe", ".elf", ".bin", ".com")
    return @(Get-ChildItem -LiteralPath $Path -File -Recurse |
        Where-Object { $_.Length -gt 0 -and $_.Length -le 32MB -and $allowed -contains $_.Extension.ToLowerInvariant() } |
        Sort-Object FullName)
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

function Invoke-Detector([System.IO.FileInfo[]] $Files) {
    if ($Files.Count -eq 0) {
        return @()
    }

    $arguments = @("run", "--project", ([System.IO.Path]::GetFullPath($CliProjectPath)), "--no-restore", "--", "--detect-fixture", "pack")
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
        throw "Fixture detector failed. Run dotnet build UpxSharp.sln first if assets are missing."
    }

    return @(($stdout -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1) | ConvertFrom-Json)
}

function Invoke-UpxPack([string] $InputPath, [string] $OutputPath) {
    if (-not (Test-Path -LiteralPath $upxExePath -PathType Leaf)) {
        throw "Official UPX not found: $upxExePath. Run .\tools\fetch-upx-official-artifacts.ps1 first, or pre-populate packed fixture: $OutputPath"
    }

    $tempOutput = $OutputPath + ".tmp"
    if (Test-Path -LiteralPath $tempOutput -PathType Leaf) {
        Remove-Item -LiteralPath $tempOutput -Force
    }

    Copy-Item -LiteralPath $InputPath -Destination $tempOutput -Force
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($upxExePath)
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.ArgumentList.Add("-f")
    $startInfo.ArgumentList.Add($tempOutput)

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    if ($process.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $tempOutput -PathType Leaf) {
            Remove-Item -LiteralPath $tempOutput -Force
        }
        throw "Official UPX failed for $InputPath. $stdout $stderr"
    }

    Move-Item -LiteralPath $tempOutput -Destination $OutputPath -Force
}

function Get-SafeFileName([string] $Format, [string] $SourcePath) {
    $name = [System.IO.Path]::GetFileName($SourcePath)
    foreach ($invalid in [System.IO.Path]::GetInvalidFileNameChars()) {
        $name = $name.Replace($invalid, '_')
    }

    return "$Format-$name"
}

$files = Get-AllowedFiles $SourceDir
$repoLocalFiles = @($files | Where-Object { Test-IsRepoLocalPath $_.FullName })
foreach ($file in $repoLocalFiles) {
    Write-Warning "Repo-local fixture skipped; external fixture required: $($file.FullName)"
}

$externalFiles = @($files | Where-Object { -not (Test-IsRepoLocalPath $_.FullName) })
$results = Invoke-Detector $externalFiles
$recognized = @($results | Where-Object { $_.Recognized -eq $true })
$unrecognized = @($results | Where-Object { $_.Recognized -ne $true })
$failures = New-Object System.Collections.Generic.List[string]

foreach ($item in $unrecognized) {
    $message = "Unrecognized fixture skipped: $($item.File) - $($item.Error)"
    Write-Warning $message
    if ($Strict) {
        $failures.Add($message) | Out-Null
    }
}

$duplicates = @($recognized | Group-Object Format | Where-Object { $_.Count -gt 1 })
foreach ($duplicate in $duplicates) {
    $message = "Duplicate original fixtures by format: $($duplicate.Name)=$($duplicate.Count)"
    Write-Warning $message
    if ($Strict) {
        $failures.Add($message) | Out-Null
    }
}

if ($failures.Count -gt 0) {
    throw ("External fixture import failed:`n - " + ($failures -join "`n - "))
}

$selected = @($recognized | Group-Object Format | ForEach-Object { $_.Group | Select-Object -First 1 } | Sort-Object Format)
$rows = New-Object System.Collections.Generic.List[object]

foreach ($item in $selected) {
    $targetName = Get-SafeFileName $item.Format $item.File
    $realTarget = Join-Path $realFixturesDir $targetName
    $packedTarget = Join-Path $packedFixturesDir ($targetName + ".upx")

    $rows.Add([pscustomobject]@{
        Format = $item.Format
        Source = $item.File
        Original = $realTarget
        Packed = $packedTarget
        Status = if (Test-Path -LiteralPath $packedTarget -PathType Leaf) { "PackedExists" } else { "WillPack" }
    }) | Out-Null
}

$rows | Format-Table -AutoSize | Out-String | Write-Host

if ($DryRun) {
    Write-Host "Dry run: no fixtures copied, no packed files created, inventory not rebuilt."
    exit 0
}

New-Item -ItemType Directory -Path $realFixturesDir -Force | Out-Null
New-Item -ItemType Directory -Path $packedFixturesDir -Force | Out-Null

foreach ($item in $selected) {
    $targetName = Get-SafeFileName $item.Format $item.File
    $realTarget = Join-Path $realFixturesDir $targetName
    $packedTarget = Join-Path $packedFixturesDir ($targetName + ".upx")

    Copy-Item -LiteralPath $item.File -Destination $realTarget -Force
    if (Test-Path -LiteralPath $packedTarget -PathType Leaf) {
        Write-Host "Packed fixture already exists: $packedTarget"
    }
    else {
        try {
            Invoke-UpxPack -InputPath $realTarget -OutputPath $packedTarget
            Write-Host "Created official UPX-packed fixture: $packedTarget"
        }
        catch {
            $message = $_.Exception.Message
            Write-Warning $message
            if ($Strict) {
                throw
            }
        }
    }
}

& (Join-Path $PSScriptRoot "build-fixture-inventory.ps1") `
    -RealFixturesDir $realFixturesDir `
    -UpxPackedFixturesDir $packedFixturesDir `
    -OutputPath $inventoryPath `
    -MissingReportPath $missingReportPath `
    -RepoRoot $RepoRoot

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "External fixture import complete."
