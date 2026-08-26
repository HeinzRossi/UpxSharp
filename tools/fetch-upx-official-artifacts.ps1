param(
    [string] $Version = "5.2.0",
    [string] $ArtifactsRoot = "C:\upxsharp-artifacts",
    [switch] $Force,
    [switch] $SkipDownloads,
    [switch] $SkipFixturePacking,
    [switch] $SkipOfficialBinaryClassification
)

$ErrorActionPreference = "Stop"

$downloadDir = Join-Path $ArtifactsRoot "downloads"
$sourceDir = Join-Path $ArtifactsRoot "upx-src"
$upxBinDir = Join-Path $ArtifactsRoot "upx-bin"
$realStubsDir = Join-Path $ArtifactsRoot "real-stubs"
$realFixturesDir = Join-Path $ArtifactsRoot "real-fixtures"
$upxPackedFixturesDir = Join-Path $ArtifactsRoot "upx-packed-fixtures"
$incomingRealBinariesDir = Join-Path $ArtifactsRoot "incoming-real-binaries"
$officialBinariesDir = Join-Path $ArtifactsRoot "official-binaries"
$officialBinaryReportPath = Join-Path $ArtifactsRoot "official-binaries-report.json"
$fixtureInventoryPath = Join-Path $ArtifactsRoot "fixture-inventory.json"

$assetBaseUrl = "https://github.com/upx/upx/releases/download/v$Version"
$releaseApiUrl = "https://api.github.com/repos/upx/upx/releases/tags/v$Version"
$sourceAssetName = "upx-$Version-src.tar.xz"
$win64AssetName = "upx-$Version-win64.zip"
$officialBinaryAssetNames = @(
    "upx-$Version-amd64_linux.tar.xz",
    "upx-$Version-arm64_linux.tar.xz",
    "upx-$Version-arm_linux.tar.xz",
    "upx-$Version-dos.zip",
    "upx-$Version-i386_linux.tar.xz",
    "upx-$Version-mipsel_linux.tar.xz",
    "upx-$Version-mips_linux.tar.xz",
    "upx-$Version-powerpc64le_linux.tar.xz",
    "upx-$Version-powerpc_linux.tar.xz",
    "upx-$Version-riscv64_linux.tar.xz",
    "upx-$Version-win32.zip",
    "upx-$Version-win64.zip"
)

$stubMappings = @(
    @{ output = "i386-linux.elf.stub"; candidates = @("i386-linux.elf-entry.h", "i386-linux.elf-fold.h") },
    @{ output = "amd64-linux.elf.stub"; candidates = @("amd64-linux.elf-entry.h", "amd64-linux.elf-fold.h") },
    @{ output = "arm-linux.elf.stub"; candidates = @("arm.v5a-linux.elf-entry.h", "arm.v4a-linux.elf-entry.h", "arm-linux.elf-entry.h") },
    @{ output = "arm64-linux.elf.stub"; candidates = @("arm64-linux.elf-entry.h", "arm64-linux.elf-fold.h") },
    @{ output = "mipsel-linux.elf.stub"; candidates = @("mipsel.r3000-linux.elf-entry.h", "mipsel-linux.elf-entry.h") },
    @{ output = "mips-linux.elf.stub"; candidates = @("mips.r3000-linux.elf-entry.h", "mips-linux.elf-entry.h") },
    @{ output = "ppc32-linux.elf.stub"; candidates = @("powerpc-linux.elf-entry.h", "ppc32-linux.elf-entry.h") },
    @{ output = "ppc64-linux.elf.stub"; candidates = @("powerpc64-linux.elf-entry.h", "ppc64-linux.elf-entry.h") },
    @{ output = "riscv64-linux.elf.stub"; candidates = @("riscv64-linux.elf-entry.h", "riscv64-linux.elf-fold.h") },
    @{ output = "i386-mach.macho.stub"; candidates = @("i386-darwin.macho-entry.h", "i386-mach.macho-entry.h") },
    @{ output = "amd64-mach.macho.stub"; candidates = @("amd64-darwin.macho-entry.h", "amd64-mach.macho-entry.h") },
    @{ output = "i386-vmlinuz.stub"; candidates = @("i386-linux.kernel.vmlinuz.h", "i386-vmlinuz.h") },
    @{ output = "dos-com.stub"; candidates = @("i086-dos16.com.h", "i386-dos16.com.h", "dos-com.h") },
    @{ output = "dos-exe.stub"; candidates = @("i086-dos16.exe.h", "i386-dos16.exe.h", "dos-exe.h") },
    @{ output = "atari-tos.stub"; candidates = @("m68k-atari.tos.h", "atari-tos.h") },
    @{ output = "ps1-exe.stub"; candidates = @("mipsel.r3000-ps1.h", "ps1-exe.h") },
    @{ output = "i386-win32.pe.stub"; candidates = @("i386-win32.pe.h") },
    @{ output = "amd64-win64.pe.stub"; candidates = @("amd64-win64.pe.h") }
)

function New-CleanDirectory([string] $Path, [switch] $Clean) {
    if ($Clean -and (Test-Path -LiteralPath $Path)) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Get-ReleaseAsset([object] $Release, [string] $Name) {
    $asset = $Release.assets | Where-Object { $_.name -eq $Name } | Select-Object -First 1
    if ($null -eq $asset) {
        throw "Release asset not found: $Name"
    }

    if ([string]::IsNullOrWhiteSpace($asset.digest) -or -not $asset.digest.StartsWith("sha256:", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Release asset does not expose published SHA-256 digest: $Name"
    }

    return $asset
}

function Save-ReleaseAsset([object] $Asset, [string] $Destination) {
    if ((Test-Path -LiteralPath $Destination -PathType Leaf) -and -not $Force) {
        Write-Host "Using existing download: $Destination"
    }
    else {
        Write-Host "Downloading $($Asset.name)"
        Invoke-WebRequest -Uri $Asset.browser_download_url -OutFile $Destination
    }

    $expected = $Asset.digest.Substring("sha256:".Length).ToUpperInvariant()
    $actual = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actual -ne $expected) {
        throw "SHA-256 mismatch for $($Asset.name). Expected $expected, got $actual"
    }

    Write-Host "Verified SHA-256: $($Asset.name)"
}

function Find-UpxSourceRoot([string] $Root) {
    $matches = Get-ChildItem -LiteralPath $Root -Directory -Recurse |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "src\stub") -PathType Container } |
        Select-Object -First 1

    if ($null -ne $matches) {
        return $matches.FullName
    }

    if (Test-Path -LiteralPath (Join-Path $Root "src\stub") -PathType Container) {
        return $Root
    }

    throw "UPX source tree was extracted, but src\stub was not found under $Root"
}

function Find-UpxStubHeaderDir([string] $UpxSourceRoot) {
    $generatedHeaderDir = Join-Path $UpxSourceRoot "src\stub"
    if ((Test-Path -LiteralPath $generatedHeaderDir -PathType Container) -and
        $null -ne (Get-ChildItem -LiteralPath $generatedHeaderDir -Filter "*.h" -File | Select-Object -First 1)) {
        return $generatedHeaderDir
    }

    $legacyHeaderDir = Join-Path $UpxSourceRoot "src\stub\src"
    if ((Test-Path -LiteralPath $legacyHeaderDir -PathType Container) -and
        $null -ne (Get-ChildItem -LiteralPath $legacyHeaderDir -Filter "*.h" -File | Select-Object -First 1)) {
        return $legacyHeaderDir
    }

    throw "UPX stub headers were not found under $UpxSourceRoot"
}

function Convert-CHeaderToBytes([string] $Path) {
    $text = Get-Content -LiteralPath $Path -Raw
    $text = [regex]::Replace($text, "(?s)/\*.*?\*/", "")
    $text = [regex]::Replace($text, "(?m)//.*$", "")

    $arrayMatches = [regex]::Matches(
        $text,
        "(?is)(?:unsigned\s+char|signed\s+char|char|uint8_t|byte)\s+[A-Za-z_][A-Za-z0-9_]*\s*(?:\[[^\]]*\])?\s*=\s*\{(?<body>.*?)\};")

    if ($arrayMatches.Count -eq 0) {
        throw "No C byte array found in $Path"
    }

    $bytes = New-Object System.Collections.Generic.List[byte]
    foreach ($array in $arrayMatches) {
        $body = $array.Groups["body"].Value
        $numberMatches = [regex]::Matches($body, "0x[0-9A-Fa-f]{1,2}|\b\d{1,3}\b")
        foreach ($number in $numberMatches) {
            $valueText = $number.Value
            if ($valueText.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
                $value = [Convert]::ToInt32($valueText.Substring(2), 16)
            }
            else {
                $value = [int] $valueText
            }

            if ($value -lt 0 -or $value -gt 255) {
                throw "Byte value out of range in ${Path}: $valueText"
            }

            $bytes.Add([byte] $value) | Out-Null
        }
    }

    if ($bytes.Count -eq 0) {
        throw "C byte arrays contained no bytes: $Path"
    }

    return $bytes.ToArray()
}

function Convert-UpxStubs([string] $StubHeaderDir, [string] $DestinationDir) {
    if (-not (Test-Path -LiteralPath $StubHeaderDir -PathType Container)) {
        throw "UPX stub header directory not found: $StubHeaderDir"
    }

    New-CleanDirectory -Path $DestinationDir -Clean:$Force

    foreach ($mapping in $stubMappings) {
        $source = $null
        foreach ($candidate in $mapping.candidates) {
            $candidatePath = Join-Path $StubHeaderDir $candidate
            if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
                $source = $candidatePath
                break
            }
        }

        if ($null -eq $source) {
            throw "No official stub header found for $($mapping.output). Tried: $($mapping.candidates -join ', ')"
        }

        $bytes = Convert-CHeaderToBytes -Path $source
        $destination = Join-Path $DestinationDir $mapping.output
        [System.IO.File]::WriteAllBytes($destination, $bytes)
        Write-Host "Converted $([System.IO.Path]::GetFileName($source)) -> $($mapping.output) ($($bytes.Length) bytes)"
    }
}

function Copy-UpxBinary([string] $ZipPath, [string] $DestinationDir) {
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ("upxsharp-upx-bin-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    try {
        Expand-Archive -LiteralPath $ZipPath -DestinationPath $temp -Force
        $upxExe = Get-ChildItem -LiteralPath $temp -Filter "upx.exe" -File -Recurse | Select-Object -First 1
        if ($null -eq $upxExe) {
            throw "upx.exe was not found inside $ZipPath"
        }

        New-CleanDirectory -Path $DestinationDir -Clean:$Force
        Copy-Item -LiteralPath $upxExe.FullName -Destination (Join-Path $DestinationDir "upx.exe") -Force
        Write-Host "Extracted upx.exe to $DestinationDir"
    }
    finally {
        if (Test-Path -LiteralPath $temp) {
            Remove-Item -LiteralPath $temp -Recurse -Force
        }
    }
}

function Expand-OfficialBinaryAsset([string] $AssetPath, [string] $DestinationDir) {
    New-CleanDirectory -Path $DestinationDir -Clean:$Force
    $name = [System.IO.Path]::GetFileName($AssetPath)
    if ($name.EndsWith(".zip", [System.StringComparison]::OrdinalIgnoreCase)) {
        Expand-Archive -LiteralPath $AssetPath -DestinationPath $DestinationDir -Force
        return
    }

    tar -xf $AssetPath -C $DestinationDir
    if ($LASTEXITCODE -ne 0) {
        throw "tar failed while extracting $AssetPath"
    }
}

function Get-OfficialBinaryCandidates([string] $Root) {
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        return @()
    }

    $allowedExtensions = @("", ".exe", ".com", ".bin")
    return @(Get-ChildItem -LiteralPath $Root -File -Recurse |
        Where-Object {
            $_.Length -gt 0 -and
            $_.Length -le 32MB -and
            $allowedExtensions -contains $_.Extension.ToLowerInvariant() -and
            $_.Name -match '^upx(\.exe|\.com)?$'
        } |
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

function Invoke-FixtureDetector([string] $Mode, [System.IO.FileInfo[]] $Files) {
    if ($Files.Count -eq 0) {
        return @()
    }

    $cliProject = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\src\UpxSharp.Cli\UpxSharp.Cli.csproj"))
    $arguments = @("run", "--project", $cliProject, "--no-restore", "--", "--detect-fixture", $Mode)
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

function Get-SafeFixtureName([string] $Format, [string] $SourcePath, [string] $Suffix) {
    $name = [System.IO.Path]::GetFileName($SourcePath)
    foreach ($invalid in [System.IO.Path]::GetInvalidFileNameChars()) {
        $name = $name.Replace($invalid, '_')
    }

    return "$Format-official-$name$Suffix"
}

function Get-AssetDirectoryName([string] $AssetName) {
    if ($AssetName.EndsWith(".tar.xz", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $AssetName.Substring(0, $AssetName.Length - ".tar.xz".Length)
    }

    if ($AssetName.EndsWith(".zip", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $AssetName.Substring(0, $AssetName.Length - ".zip".Length)
    }

    return [System.IO.Path]::GetFileNameWithoutExtension($AssetName)
}

function Classify-OfficialBinaries([string] $ExtractedRoot, [string] $IncomingDir, [string] $PackedDir, [string] $ReportPath) {
    New-CleanDirectory -Path $IncomingDir
    New-CleanDirectory -Path $PackedDir

    $files = Get-OfficialBinaryCandidates -Root $ExtractedRoot
    if ($files.Count -eq 0) {
        Write-Warning "No official binary candidates found under $ExtractedRoot"
        return
    }

    $packResults = Invoke-FixtureDetector -Mode "pack" -Files $files
    $unpackResults = Invoke-FixtureDetector -Mode "unpack" -Files $files
    $report = New-Object System.Collections.Generic.List[object]

    foreach ($file in $files) {
        $pack = $packResults | Where-Object { $_.File -eq $file.FullName } | Select-Object -First 1
        $unpack = $unpackResults | Where-Object { $_.File -eq $file.FullName } | Select-Object -First 1
        $status = "NotRecognized"
        $format = $null
        $destination = $null

        if ($null -ne $unpack -and $unpack.Recognized -eq $true) {
            $format = $unpack.Format
            $destinationName = Get-SafeFixtureName -Format $format -SourcePath $file.FullName -Suffix ".upx"
            $destination = Join-Path $PackedDir $destinationName
            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
            $status = "CopiedPacked"
        }
        elseif ($null -ne $pack -and $pack.Recognized -eq $true) {
            $format = $pack.Format
            $destinationName = Get-SafeFixtureName -Format $format -SourcePath $file.FullName -Suffix ""
            $destination = Join-Path $IncomingDir $destinationName
            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
            $status = "CopiedOriginal"
        }

        $report.Add([ordered]@{
            file = $file.FullName
            format = $format
            status = $status
            destination = $destination
            packError = if ($null -eq $pack) { $null } else { $pack.Error }
            unpackError = if ($null -eq $unpack) { $null } else { $unpack.Error }
        }) | Out-Null
    }

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $json = $report | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($ReportPath, $json + [Environment]::NewLine, $utf8NoBom)
    Write-Host "Wrote official binary classification report: $ReportPath"
    foreach ($group in ($report | Group-Object { if ($_ -is [System.Collections.IDictionary]) { $_["status"] } else { $_.status } } | Sort-Object Name)) {
        Write-Host " - $($group.Name): $($group.Count)"
    }
}

function Add-InventoryEntry([string] $InventoryPath, [string] $Format, [string] $Path, [string] $PackedPath) {
    $fixtures = @()
    if (Test-Path -LiteralPath $InventoryPath -PathType Leaf) {
        $existing = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json
        if ($null -ne $existing.fixtures) {
            $fixtures = @($existing.fixtures | Where-Object { $_.format -ne $Format })
        }
    }

    $fixtures += [ordered]@{
        format = $Format
        path = $Path
        packedPath = $PackedPath
    }

    $inventory = [ordered]@{ fixtures = $fixtures }
    $json = $inventory | ConvertTo-Json -Depth 4
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($InventoryPath, $json + [Environment]::NewLine, $utf8NoBom)
}

function Remove-InventoryEntry([string] $InventoryPath, [string] $Format) {
    if (-not (Test-Path -LiteralPath $InventoryPath -PathType Leaf)) {
        return
    }

    $existing = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json
    $fixtures = @()
    if ($null -ne $existing.fixtures) {
        $fixtures = @($existing.fixtures | Where-Object { $_.format -ne $Format })
    }

    $inventory = [ordered]@{ fixtures = $fixtures }
    $json = $inventory | ConvertTo-Json -Depth 4
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($InventoryPath, $json + [Environment]::NewLine, $utf8NoBom)
}

function Add-OfficialUpxFixture([string] $UpxExe, [string] $FixtureDir, [string] $PackedFixtureDir, [string] $InventoryPath) {
    if (-not (Test-Path -LiteralPath $UpxExe -PathType Leaf)) {
        throw "Cannot add official UPX fixture because upx.exe was not found: $UpxExe"
    }

    New-CleanDirectory -Path $FixtureDir
    New-CleanDirectory -Path $PackedFixtureDir
    $fixtureName = "official-upx-win64.exe"
    $realDestination = Join-Path $FixtureDir $fixtureName
    $packedDestination = Join-Path $PackedFixtureDir "$fixtureName.upx"
    Copy-Item -LiteralPath $UpxExe -Destination $realDestination -Force

    & $UpxExe -q -t $realDestination
    if ($LASTEXITCODE -eq 0) {
        Copy-Item -LiteralPath $UpxExe -Destination $packedDestination -Force
        Remove-Item -LiteralPath $realDestination -Force
        Remove-InventoryEntry -InventoryPath $InventoryPath -Format "Win64Pe"
        Write-Host "Official UPX Win64 binary is already packed; added it only as UPX-packed fixture: $($fixtureName).upx"
        return
    }

    Add-InventoryEntry -InventoryPath $InventoryPath -Format "Win64Pe" -Path $fixtureName -PackedPath "$fixtureName.upx"
    Write-Host "Added official UPX Win64 binary to real fixtures and inventory: $fixtureName"
}

function Pack-OfficialFixtures([string] $UpxExe, [string] $InputDir, [string] $OutputDir) {
    if (-not (Test-Path -LiteralPath $InputDir -PathType Container)) {
        Write-Host "Skipping UPX-packed fixtures: real fixtures directory not found: $InputDir"
        return
    }

    $fixtures = @(Get-ChildItem -LiteralPath $InputDir -File -Recurse | Where-Object { $_.Length -gt 0 -and $_.Length -le 32MB })
    if ($fixtures.Count -eq 0) {
        Write-Host "Skipping UPX-packed fixtures: no non-empty files found in $InputDir"
        return
    }

    New-CleanDirectory -Path $OutputDir -Clean:$Force

    $packed = 0
    foreach ($fixture in $fixtures) {
        $relative = $fixture.FullName
        $rootWithSeparator = $InputDir.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        if ($fixture.FullName.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
            $relative = $fixture.FullName.Substring($rootWithSeparator.Length)
        }
        $safeName = ($relative -replace "[:\\\/]", "_")
        $output = Join-Path $OutputDir ($safeName + ".upx")

        Write-Host "Packing fixture with official UPX: $relative"
        & $UpxExe -q -f "-o$output" $fixture.FullName
        if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $output -PathType Leaf)) {
            $packed++
        }
        else {
            Write-Warning "UPX could not pack fixture: $relative"
        }
    }

    if ($packed -eq 0) {
        Write-Warning "Real fixtures were present, but official UPX did not produce any packed fixtures."
        return
    }

    Write-Host "Generated $packed UPX-packed fixture(s) in $OutputDir"
}

New-CleanDirectory -Path $downloadDir
New-CleanDirectory -Path $sourceDir
New-CleanDirectory -Path $upxBinDir
New-CleanDirectory -Path $realStubsDir
New-CleanDirectory -Path $realFixturesDir
New-CleanDirectory -Path $upxPackedFixturesDir
New-CleanDirectory -Path $incomingRealBinariesDir
New-CleanDirectory -Path $officialBinariesDir

$sourceArchive = Join-Path $downloadDir $sourceAssetName
$win64Archive = Join-Path $downloadDir $win64AssetName

if (-not $SkipDownloads) {
    Write-Host "Loading UPX release metadata: $releaseApiUrl"
    $release = Invoke-RestMethod -Uri $releaseApiUrl
    $sourceAsset = Get-ReleaseAsset -Release $release -Name $sourceAssetName
    $win64Asset = Get-ReleaseAsset -Release $release -Name $win64AssetName
    $officialBinaryAssets = @($officialBinaryAssetNames | ForEach-Object { Get-ReleaseAsset -Release $release -Name $_ })

    Save-ReleaseAsset -Asset $sourceAsset -Destination $sourceArchive
    Save-ReleaseAsset -Asset $win64Asset -Destination $win64Archive
    foreach ($asset in $officialBinaryAssets) {
        Save-ReleaseAsset -Asset $asset -Destination (Join-Path $downloadDir $asset.name)
    }
}
elseif (-not (Test-Path -LiteralPath $sourceArchive -PathType Leaf) -or -not (Test-Path -LiteralPath $win64Archive -PathType Leaf)) {
    throw "SkipDownloads was set, but expected archives were not found in $downloadDir"
}
elseif ($officialBinaryAssetNames | Where-Object { -not (Test-Path -LiteralPath (Join-Path $downloadDir $_) -PathType Leaf) } | Select-Object -First 1) {
    throw "SkipDownloads was set, but one or more official binary archives were not found in $downloadDir"
}

if ($Force -and (Test-Path -LiteralPath $sourceDir)) {
    Remove-Item -LiteralPath $sourceDir -Recurse -Force
}
New-Item -ItemType Directory -Path $sourceDir -Force | Out-Null

Write-Host "Extracting source archive to $sourceDir"
tar -xf $sourceArchive -C $sourceDir
if ($LASTEXITCODE -ne 0) {
    throw "tar failed while extracting $sourceArchive"
}

$upxSourceRoot = Find-UpxSourceRoot -Root $sourceDir
$stubHeaderDir = Find-UpxStubHeaderDir -UpxSourceRoot $upxSourceRoot
Convert-UpxStubs -StubHeaderDir $stubHeaderDir -DestinationDir $realStubsDir

Copy-UpxBinary -ZipPath $win64Archive -DestinationDir $upxBinDir
Add-OfficialUpxFixture -UpxExe (Join-Path $upxBinDir "upx.exe") -FixtureDir $realFixturesDir -PackedFixtureDir $upxPackedFixturesDir -InventoryPath $fixtureInventoryPath

if (-not $SkipOfficialBinaryClassification) {
    foreach ($assetName in $officialBinaryAssetNames) {
        $assetArchive = Join-Path $downloadDir $assetName
        if (-not (Test-Path -LiteralPath $assetArchive -PathType Leaf)) {
            throw "Official binary asset archive not found: $assetArchive"
        }

        $assetExtractDir = Join-Path $officialBinariesDir (Get-AssetDirectoryName $assetName)
        Write-Host "Extracting official binary asset: $assetName"
        Expand-OfficialBinaryAsset -AssetPath $assetArchive -DestinationDir $assetExtractDir
    }

    Classify-OfficialBinaries -ExtractedRoot $officialBinariesDir -IncomingDir $incomingRealBinariesDir -PackedDir $upxPackedFixturesDir -ReportPath $officialBinaryReportPath
}

if (-not $SkipFixturePacking) {
    Pack-OfficialFixtures -UpxExe (Join-Path $upxBinDir "upx.exe") -InputDir $realFixturesDir -OutputDir $upxPackedFixturesDir
}

Write-Host "UPX official artifacts prepared."
Write-Host "Real stubs: $realStubsDir"
Write-Host "UPX binary: $upxBinDir"
Write-Host "Official binaries: $officialBinariesDir"
Write-Host "Incoming real binaries: $incomingRealBinariesDir"
Write-Host "Official binary report: $officialBinaryReportPath"
Write-Host "Fixture inventory: $fixtureInventoryPath"
Write-Host "Next:"
Write-Host "  .\tools\import-real-stubs.ps1 -SourceDir $realStubsDir"
Write-Host "  .\tools\import-external-fixtures.ps1 -SourceDir $incomingRealBinariesDir -Strict"
Write-Host "  `$env:UPXSHARP_REAL_FIXTURES_DIR=""$realFixturesDir"""
Write-Host "  `$env:UPXSHARP_UPX_PACKED_FIXTURES_DIR=""$upxPackedFixturesDir"""
Write-Host "  `$env:UPXSHARP_REAL_FIXTURES_INVENTORY=""$fixtureInventoryPath"""
Write-Host "  .\tools\verify-external-artifacts.ps1"
