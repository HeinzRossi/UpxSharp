param(
    [string] $CandidateManifestPath,
    [string] $ArtifactsRoot = "C:\upxsharp-artifacts",
    [string] $RepoRoot,
    [string] $CliProjectPath,
    [switch] $Strict,
    [switch] $DryRun,
    [long] $MaxBytes = 33554432
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Join-Path $PSScriptRoot ".."
}

if ([string]::IsNullOrWhiteSpace($CliProjectPath)) {
    $CliProjectPath = Join-Path $PSScriptRoot "..\src\UpxSharp.Cli\UpxSharp.Cli.csproj"
}

if ([string]::IsNullOrWhiteSpace($CandidateManifestPath)) {
    $CandidateManifestPath = Join-Path $ArtifactsRoot "fixture-candidate-sources.json"
}

$candidateDir = Join-Path $ArtifactsRoot "candidate-real-fixtures"
$downloadDir = Join-Path $candidateDir "downloads"
$extractDir = Join-Path $candidateDir "extracted"
$incomingDir = Join-Path $ArtifactsRoot "incoming-real-binaries"
$reportPath = Join-Path $ArtifactsRoot "real-fixture-candidates-report.json"
$acquisitionPlanPath = Join-Path $ArtifactsRoot "fixture-acquisition-plan.json"
$resolvedRepoRoot = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

function Test-IsRepoLocalPath([string] $Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    return $fullPath.StartsWith($resolvedRepoRoot, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-JsonItems($Value) {
    $items = New-Object System.Collections.Generic.List[object]
    if ($null -eq $Value) {
        return @()
    }

    if ($Value -is [System.Collections.IEnumerable] -and
        $Value -isnot [string] -and
        $Value -isnot [System.Collections.IDictionary] -and
        $Value -isnot [pscustomobject]) {
        foreach ($item in $Value) {
            $items.Add($item) | Out-Null
        }
        return $items.ToArray()
    }

    $items.Add($Value) | Out-Null
    return $items.ToArray()
}

function Get-SafeName([string] $Name) {
    if ([string]::IsNullOrWhiteSpace($Name)) {
        $Name = "candidate"
    }

    foreach ($invalid in [System.IO.Path]::GetInvalidFileNameChars()) {
        $Name = $Name.Replace($invalid, "_")
    }

    return $Name
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

function Get-AllowedFiles([string] $Path, $Globs) {
    $allowed = @("", ".exe", ".elf", ".bin", ".com")
    $files = if (Test-Path -LiteralPath $Path -PathType Leaf) {
        @(Get-Item -LiteralPath $Path)
    }
    elseif (Test-Path -LiteralPath $Path -PathType Container) {
        @(Get-ChildItem -LiteralPath $Path -File -Recurse)
    }
    else {
        @()
    }

    $patterns = @(Get-JsonItems $Globs | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    return @($files |
        Where-Object { $_.Length -gt 0 -and $_.Length -le $MaxBytes -and $allowed -contains $_.Extension.ToLowerInvariant() } |
        Where-Object {
            if ($patterns.Count -eq 0) { return $true }
            $file = $_
            foreach ($pattern in $patterns) {
                if ($file.Name -like $pattern -or $file.FullName -like $pattern) {
                    return $true
                }
            }
            return $false
        } |
        Sort-Object FullName)
}

function Expand-CandidateArchive([string] $ArchivePath, [string] $Destination) {
    if (Test-Path -LiteralPath $Destination -PathType Container) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $extension = [System.IO.Path]::GetExtension($ArchivePath).ToLowerInvariant()
    if ($extension -eq ".zip") {
        Expand-Archive -LiteralPath $ArchivePath -DestinationPath $Destination -Force
        return $Destination
    }

    if ($extension -eq ".deb") {
        return Expand-DebianPackage -PackagePath $ArchivePath -Destination $Destination
    }

    if ($ArchivePath.EndsWith(".tar", [System.StringComparison]::OrdinalIgnoreCase) -or
        $ArchivePath.EndsWith(".tar.gz", [System.StringComparison]::OrdinalIgnoreCase) -or
        $ArchivePath.EndsWith(".tgz", [System.StringComparison]::OrdinalIgnoreCase) -or
        $ArchivePath.EndsWith(".tar.xz", [System.StringComparison]::OrdinalIgnoreCase)) {
        tar -xf $ArchivePath -C $Destination
        if ($LASTEXITCODE -ne 0) {
            throw "Could not extract archive: $ArchivePath"
        }
        return $Destination
    }

    return $ArchivePath
}

function Expand-DebianPackage([string] $PackagePath, [string] $Destination) {
    if (Test-Path -LiteralPath $Destination -PathType Container) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $bytes = [System.IO.File]::ReadAllBytes($PackagePath)
    $signature = [System.Text.Encoding]::ASCII.GetString($bytes, 0, [Math]::Min(8, $bytes.Length))
    if ($signature -ne "!<arch>`n") {
        throw "Invalid Debian package archive: $PackagePath"
    }

    $offset = 8
    $dataArchivePath = $null
    while ($offset + 60 -le $bytes.Length) {
        $header = [System.Text.Encoding]::ASCII.GetString($bytes, $offset, 60)
        $name = $header.Substring(0, 16).Trim()
        $sizeText = $header.Substring(48, 10).Trim()
        $parsedSize = 0
        if (-not [int]::TryParse($sizeText, [ref]$parsedSize)) {
            throw "Invalid ar member size in: $PackagePath"
        }

        $size = $parsedSize
        $dataOffset = $offset + 60
        if ($dataOffset + $size -gt $bytes.Length) {
            throw "Truncated ar member in: $PackagePath"
        }

        if ($name.StartsWith("data.tar", [System.StringComparison]::OrdinalIgnoreCase)) {
            $memberName = $name.TrimEnd("/")
            $dataArchivePath = Join-Path $Destination $memberName
            [System.IO.File]::WriteAllBytes($dataArchivePath, $bytes[$dataOffset..($dataOffset + $size - 1)])
            break
        }

        $offset = $dataOffset + $size
        if (($offset % 2) -ne 0) {
            $offset++
        }
    }

    if ([string]::IsNullOrWhiteSpace($dataArchivePath)) {
        throw "Debian package has no data.tar member: $PackagePath"
    }

    tar -xf $dataArchivePath -C $Destination
    if ($LASTEXITCODE -ne 0) {
        throw "Could not extract Debian data archive: $dataArchivePath"
    }

    return $Destination
}

function Test-IsFatMachO([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        if ($stream.Length -lt 4) {
            return $false
        }

        $buffer = New-Object byte[] 4
        [void]$stream.Read($buffer, 0, 4)
        $cafebabe = $buffer[0] -eq 0xCA -and $buffer[1] -eq 0xFE -and $buffer[2] -eq 0xBA -and $buffer[3] -eq 0xBE
        $cafebabf = $buffer[0] -eq 0xCA -and $buffer[1] -eq 0xFE -and $buffer[2] -eq 0xBA -and $buffer[3] -eq 0xBF
        $bebafeca = $buffer[0] -eq 0xBE -and $buffer[1] -eq 0xBA -and $buffer[2] -eq 0xFE -and $buffer[3] -eq 0xCA
        $bfbafeca = $buffer[0] -eq 0xBF -and $buffer[1] -eq 0xBA -and $buffer[2] -eq 0xFE -and $buffer[3] -eq 0xCA
        return $cafebabe -or $cafebabf -or $bebafeca -or $bfbafeca
    }
    finally {
        $stream.Dispose()
    }
}

function Read-UInt32BigEndian([byte[]] $Bytes, [int] $Offset) {
    $b0 = [uint32]$Bytes[$Offset]
    $b1 = [uint32]$Bytes[$Offset + 1]
    $b2 = [uint32]$Bytes[$Offset + 2]
    $b3 = [uint32]$Bytes[$Offset + 3]
    return [uint32](($b0 -shl 24) -bor ($b1 -shl 16) -bor ($b2 -shl 8) -bor $b3)
}

function Try-ExtractFatMachOSlice([string] $Path, [string] $ExpectedFormat, [string] $DestinationDirectory) {
    if ($ExpectedFormat -ne "MachI386" -and $ExpectedFormat -ne "MachAMD64") {
        return $null
    }

    if (-not (Test-IsFatMachO $Path)) {
        return $null
    }

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 8) {
        return $null
    }

    $magic = Read-UInt32BigEndian -Bytes $bytes -Offset 0
    $fatMagic32 = [uint32]3405691582
    $fatMagic64 = [uint32]3405691583
    if ($magic -ne $fatMagic32 -and $magic -ne $fatMagic64) {
        return $null
    }

    $expectedCpu = if ($ExpectedFormat -eq "MachAMD64") { [uint32]0x01000007 } else { [uint32]7 }
    $archSize = if ($magic -eq $fatMagic64) { 32 } else { 20 }
    $count = Read-UInt32BigEndian -Bytes $bytes -Offset 4
    $tableOffset = 8
    $fallbackSliceOffset = -1
    $fallbackSliceSize = -1
    for ($i = 0; $i -lt $count; $i++) {
        $entryOffset = $tableOffset + ($i * $archSize)
        if ($entryOffset + $archSize -gt $bytes.Length) {
            return $null
        }

        $cpu = Read-UInt32BigEndian -Bytes $bytes -Offset $entryOffset
        $sliceOffset = [int64](Read-UInt32BigEndian -Bytes $bytes -Offset ($entryOffset + 8))
        $sliceSize = [int64](Read-UInt32BigEndian -Bytes $bytes -Offset ($entryOffset + 12))
        if ($fallbackSliceOffset -lt 0 -and $sliceOffset -ge 0 -and $sliceSize -gt 0 -and $sliceOffset + $sliceSize -le $bytes.Length) {
            $fallbackSliceOffset = $sliceOffset
            $fallbackSliceSize = $sliceSize
        }

        if ($cpu -ne $expectedCpu) {
            continue
        }

        if ($sliceOffset -lt 0 -or $sliceSize -le 0 -or $sliceOffset + $sliceSize -gt $bytes.Length) {
            return $null
        }

        return Write-FatMachOSlice -Bytes $bytes -SliceOffset $sliceOffset -SliceSize $sliceSize -ExpectedFormat $ExpectedFormat -OriginalPath $Path -DestinationDirectory $DestinationDirectory
    }

    if ($fallbackSliceOffset -ge 0) {
        return Write-FatMachOSlice -Bytes $bytes -SliceOffset $fallbackSliceOffset -SliceSize $fallbackSliceSize -ExpectedFormat $ExpectedFormat -OriginalPath $Path -DestinationDirectory $DestinationDirectory
    }

    return $null
}

function Write-FatMachOSlice([byte[]] $Bytes, [int64] $SliceOffset, [int64] $SliceSize, [string] $ExpectedFormat, [string] $OriginalPath, [string] $DestinationDirectory) {
        New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
        $sliceName = Get-SafeName "$ExpectedFormat-$([System.IO.Path]::GetFileName($OriginalPath)).thin"
        $slicePath = Join-Path $DestinationDirectory $sliceName
        $slice = New-Object byte[] ([int]$SliceSize)
        [System.Array]::Copy($Bytes, $SliceOffset, $slice, 0, $SliceSize)
        [System.IO.File]::WriteAllBytes($slicePath, $slice)
        return $slicePath
}

function Get-UnrecognizedStatus([string] $ExpectedFormat, [string] $File) {
    if (($ExpectedFormat -eq "MachI386" -or $ExpectedFormat -eq "MachAMD64") -and (Test-IsFatMachO $File)) {
        return "FatMachUnsupported"
    }

    return "NotRecognized"
}

function New-CandidateTemplate([string] $Path) {
    $formats = @(
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

    if (Test-Path -LiteralPath $acquisitionPlanPath -PathType Leaf) {
        $plan = Get-JsonItems (Get-Content -LiteralPath $acquisitionPlanPath -Raw | ConvertFrom-Json)
        if ($plan.Count -gt 0) {
            $formats = @($plan | ForEach-Object { [string]$_.format } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        }
    }

    $sources = @($formats | Sort-Object | ForEach-Object {
        [ordered]@{
            format = $_
            sourceName = "TODO-$($_)"
            url = $null
            localPath = $null
            archivePath = $null
            fileGlobs = @("*")
            license = $null
            notes = "External original binary required. Do not use repo-local output."
        }
    })

    $template = [ordered]@{
        sources = $sources
    }

    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    $json = $template | ConvertTo-Json -Depth 6
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, $utf8NoBom)
}

if (-not (Test-Path -LiteralPath $CandidateManifestPath -PathType Leaf)) {
    New-CandidateTemplate $CandidateManifestPath
    Write-Host "Created candidate source template: $CandidateManifestPath"
    Write-Host "Fill url or localPath entries, then rerun this script."
    exit 1
}

$manifest = Get-Content -LiteralPath $CandidateManifestPath -Raw | ConvertFrom-Json
$sources = Get-JsonItems $manifest.sources
if ($sources.Count -eq 0) {
    throw "Candidate manifest has no sources: $CandidateManifestPath"
}

if (-not $DryRun) {
    New-Item -ItemType Directory -Path $candidateDir -Force | Out-Null
    New-Item -ItemType Directory -Path $downloadDir -Force | Out-Null
    New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
}

$report = New-Object System.Collections.Generic.List[object]
$failures = New-Object System.Collections.Generic.List[string]

foreach ($source in $sources) {
    $format = [string]$source.format
    $sourceName = if ([string]::IsNullOrWhiteSpace($source.sourceName)) { $format } else { [string]$source.sourceName }
    $license = [string]$source.license
    $safeSourceName = Get-SafeName "$format-$sourceName"
    $materializedPath = $null
    $sourceLocator = $null

    try {
        if (-not [string]::IsNullOrWhiteSpace($source.localPath)) {
            $sourceLocator = [string]$source.localPath
            $materializedPath = [System.IO.Path]::GetFullPath($sourceLocator)
            if (Test-IsRepoLocalPath $materializedPath) {
                $message = "Repo-local candidate skipped; external fixture required: $materializedPath"
                Write-Warning $message
                $report.Add([ordered]@{
                    format = $format
                    sourceName = $sourceName
                    source = $sourceLocator
                    license = $license
                    file = $materializedPath
                    detectedFormat = $null
                    recognized = $false
                    status = "RepoLocalSkipped"
                    destination = $null
                    error = $message
                }) | Out-Null
                if ($Strict) { $failures.Add($message) | Out-Null }
                continue
            }
        }
        elseif (-not [string]::IsNullOrWhiteSpace($source.url)) {
            $sourceLocator = [string]$source.url
            $downloadName = Get-SafeName ([System.IO.Path]::GetFileName(([System.Uri]$sourceLocator).AbsolutePath))
            if ([string]::IsNullOrWhiteSpace($downloadName)) {
                $downloadName = "$safeSourceName.bin"
            }

            $materializedPath = Join-Path $downloadDir $downloadName
            if ($DryRun) {
                Write-Host "Dry run: would download $sourceLocator -> $materializedPath"
                $report.Add([ordered]@{
                    format = $format
                    sourceName = $sourceName
                    source = $sourceLocator
                    license = $license
                    file = $materializedPath
                    detectedFormat = $null
                    recognized = $false
                    status = "WouldDownload"
                    destination = $null
                    error = $null
                }) | Out-Null
                continue
            }

            Invoke-WebRequest -Uri $sourceLocator -OutFile $materializedPath
        }
        else {
            $message = "Candidate source has neither url nor localPath: $sourceName"
            Write-Warning $message
            $missingStatus = if ($sourceName.StartsWith("UNRESOLVED-", [System.StringComparison]::OrdinalIgnoreCase)) { "NoTrustedSource" } else { "MissingSource" }
            $report.Add([ordered]@{
                format = $format
                sourceName = $sourceName
                source = $null
                license = $license
                file = $null
                detectedFormat = $null
                recognized = $false
                status = $missingStatus
                destination = $null
                error = $message
            }) | Out-Null
            if ($Strict) { $failures.Add($message) | Out-Null }
            continue
        }

        if (-not (Test-Path -LiteralPath $materializedPath)) {
            throw "Candidate path not found: $materializedPath"
        }

        $scanPath = $materializedPath
        if (-not [string]::IsNullOrWhiteSpace($source.archivePath)) {
            $archiveRoot = Expand-CandidateArchive $materializedPath (Join-Path $extractDir $safeSourceName)
            $scanPath = Join-Path $archiveRoot ([string]$source.archivePath)
        }
        elseif ((Test-Path -LiteralPath $materializedPath -PathType Leaf) -and (
            $materializedPath.EndsWith(".zip", [System.StringComparison]::OrdinalIgnoreCase) -or
            $materializedPath.EndsWith(".tar", [System.StringComparison]::OrdinalIgnoreCase) -or
            $materializedPath.EndsWith(".tar.gz", [System.StringComparison]::OrdinalIgnoreCase) -or
            $materializedPath.EndsWith(".tgz", [System.StringComparison]::OrdinalIgnoreCase) -or
            $materializedPath.EndsWith(".tar.xz", [System.StringComparison]::OrdinalIgnoreCase) -or
            $materializedPath.EndsWith(".deb", [System.StringComparison]::OrdinalIgnoreCase))) {
            $scanPath = Expand-CandidateArchive $materializedPath (Join-Path $extractDir $safeSourceName)
        }

        $files = Get-AllowedFiles $scanPath $source.fileGlobs
        $repoLocalFiles = @($files | Where-Object { Test-IsRepoLocalPath $_.FullName })
        foreach ($file in $repoLocalFiles) {
            $message = "Repo-local candidate skipped; external fixture required: $($file.FullName)"
            Write-Warning $message
            $report.Add([ordered]@{
                format = $format
                sourceName = $sourceName
                source = $sourceLocator
                license = $license
                file = $file.FullName
                detectedFormat = $null
                recognized = $false
                status = "RepoLocalSkipped"
                destination = $null
                error = $message
            }) | Out-Null
            if ($Strict) { $failures.Add($message) | Out-Null }
        }

        $files = @($files | Where-Object { -not (Test-IsRepoLocalPath $_.FullName) })
        $fatMachFiles = @($files | Where-Object { ($format -eq "MachI386" -or $format -eq "MachAMD64") -and (Test-IsFatMachO $_.FullName) })
        $fatMachSliceFiles = New-Object System.Collections.Generic.List[System.IO.FileInfo]
        foreach ($file in $fatMachFiles) {
            $slicePath = Try-ExtractFatMachOSlice -Path $file.FullName -ExpectedFormat $format -DestinationDirectory (Join-Path $extractDir "$safeSourceName-fat-slices")
            if ([string]::IsNullOrWhiteSpace($slicePath)) {
                $report.Add([ordered]@{
                    format = $format
                    sourceName = $sourceName
                    source = $sourceLocator
                    license = $license
                    file = $file.FullName
                    detectedFormat = $null
                    recognized = $false
                    status = "FatMachUnsupported"
                    destination = $null
                    error = "Mach-O fat/universal fixture has no supported thin slice for $format"
                }) | Out-Null
            }
            else {
                $fatMachSliceFiles.Add((Get-Item -LiteralPath $slicePath)) | Out-Null
                $report.Add([ordered]@{
                    format = $format
                    sourceName = $sourceName
                    source = $sourceLocator
                    license = $license
                    file = $file.FullName
                    detectedFormat = $null
                    recognized = $false
                    status = "ExtractedFatMachSlice"
                    destination = $slicePath
                    error = $null
                }) | Out-Null
            }
        }

        $files = @($files | Where-Object { -not (($format -eq "MachI386" -or $format -eq "MachAMD64") -and (Test-IsFatMachO $_.FullName)) })
        $files = @($files + @($fatMachSliceFiles))
        $detections = Invoke-Detector $files
        foreach ($detection in $detections) {
            $status = "NotRecognized"
            $destination = $null
            $detectionError = $detection.Error
            $recognized = [bool]$detection.Recognized
            $detectedFormat = if ($null -eq $detection.Format) { $null } else { [string]$detection.Format }

            if ($recognized -and -not [string]::IsNullOrWhiteSpace($format) -and $detectedFormat -ne $format) {
                $status = "WrongFormat"
                $detectionError = "Expected $format, detected $detectedFormat"
                if ($Strict) { $failures.Add("${sourceName}: $detectionError") | Out-Null }
            }
            elseif ($recognized) {
                $targetName = Get-SafeName "$detectedFormat-$([System.IO.Path]::GetFileName([string]$detection.File))"
                $destination = Join-Path $incomingDir $targetName
                $status = if ($DryRun) { "WouldCopyRecognized" } else { "CopiedRecognized" }
                if (-not $DryRun) {
                    New-Item -ItemType Directory -Path $incomingDir -Force | Out-Null
                    Copy-Item -LiteralPath ([string]$detection.File) -Destination $destination -Force
                }
            }
            elseif ($Strict) {
                $failures.Add("${sourceName}: not recognized: $($detection.File)") | Out-Null
            }
            elseif (-not $recognized) {
                $status = Get-UnrecognizedStatus -ExpectedFormat $format -File ([string]$detection.File)
            }

            $report.Add([ordered]@{
                format = $format
                sourceName = $sourceName
                source = $sourceLocator
                license = $license
                file = [string]$detection.File
                detectedFormat = $detectedFormat
                recognized = $recognized
                status = $status
                destination = $destination
                error = $detectionError
            }) | Out-Null
        }

        if ($files.Count -eq 0 -and $fatMachFiles.Count -eq 0) {
            $message = "No candidate files found for source: $sourceName"
            Write-Warning $message
            $report.Add([ordered]@{
                format = $format
                sourceName = $sourceName
                source = $sourceLocator
                license = $license
                file = $scanPath
                detectedFormat = $null
                recognized = $false
                status = "NoFiles"
                destination = $null
                error = $message
            }) | Out-Null
            if ($Strict) { $failures.Add($message) | Out-Null }
        }
    }
    catch {
        $message = $_.Exception.Message
        Write-Warning "$sourceName failed: $message"
        $report.Add([ordered]@{
            format = $format
            sourceName = $sourceName
            source = $sourceLocator
            license = $license
            file = $materializedPath
            detectedFormat = $null
            recognized = $false
            status = "Failed"
            destination = $null
            error = $message
        }) | Out-Null
        if ($Strict) { $failures.Add("${sourceName}: $message") | Out-Null }
    }
}

$report | Sort-Object format, sourceName, file | Format-Table format, sourceName, detectedFormat, status, destination -AutoSize | Out-String | Write-Host

if (-not $DryRun) {
    $parent = Split-Path -Parent $reportPath
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
    [System.IO.File]::WriteAllText($reportPath, $json + [Environment]::NewLine, $utf8NoBom)
    Write-Host "Wrote candidate report: $reportPath"
}
else {
    Write-Host "Dry run: no downloads, copies, or report writes."
}

foreach ($group in ($report | Group-Object { if ($_ -is [System.Collections.IDictionary]) { $_["status"] } else { $_.status } } | Sort-Object Name)) {
    Write-Host " - $($group.Name): $($group.Count)"
}

if ($failures.Count -gt 0) {
    throw ("Real fixture candidate fetch failed:`n - " + ($failures -join "`n - "))
}

Write-Host "Recognized candidates copied to: $incomingDir"
Write-Host "Next: .\tools\import-external-fixtures.ps1 -SourceDir $incomingDir -Strict"
