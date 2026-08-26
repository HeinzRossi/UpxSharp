param(
    [string] $ArtifactsRoot = "C:\upxsharp-artifacts",
    [string] $OutputPath,
    [switch] $Strict,
    [switch] $NoNetwork
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $ArtifactsRoot "fixture-candidate-sources.json"
}

$cacheDir = Join-Path $ArtifactsRoot "candidate-source-index-cache"
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

function Get-SafeName([string] $Name) {
    foreach ($invalid in [System.IO.Path]::GetInvalidFileNameChars()) {
        $Name = $Name.Replace($invalid, "_")
    }
    return $Name
}

function Read-GZipText([string] $Path) {
    $inputStream = [System.IO.File]::OpenRead($Path)
    try {
        $gzip = [System.IO.Compression.GZipStream]::new($inputStream, [System.IO.Compression.CompressionMode]::Decompress)
        try {
            $reader = [System.IO.StreamReader]::new($gzip, [System.Text.Encoding]::UTF8)
            try {
                return $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }
        }
        finally {
            $gzip.Dispose()
        }
    }
    finally {
        $inputStream.Dispose()
    }
}

function Get-PackageParagraphs([string] $Text) {
    return @($Text -split "(`r?`n){2,}" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-PackageField([string] $Paragraph, [string] $Field) {
    $pattern = "(?m)^$([regex]::Escape($Field)):\s*(.+)$"
    $match = [regex]::Match($Paragraph, $pattern)
    if ($match.Success) {
        return $match.Groups[1].Value.Trim()
    }
    return $null
}

function Resolve-DebianPackageUrl([string] $BaseUrl, [string] $Suite, [string] $Component, [string] $Arch, [string] $Package) {
    if ($NoNetwork) {
        return $null
    }

    New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
    $indexUrl = "$BaseUrl/dists/$Suite/$Component/binary-$Arch/Packages.gz"
    $cacheName = Get-SafeName "$Suite-$Component-$Arch-Packages.gz"
    $cachePath = Join-Path $cacheDir $cacheName

    try {
        if (-not (Test-Path -LiteralPath $cachePath -PathType Leaf)) {
            Invoke-WebRequest -Uri $indexUrl -OutFile $cachePath -TimeoutSec 30
        }

        $text = Read-GZipText $cachePath
        foreach ($paragraph in (Get-PackageParagraphs $text)) {
            if ((Get-PackageField $paragraph "Package") -ne $Package) {
                continue
            }

            $filename = Get-PackageField $paragraph "Filename"
            if ([string]::IsNullOrWhiteSpace($filename)) {
                continue
            }

            return "$BaseUrl/$filename"
        }
    }
    catch {
        Write-Warning "Could not resolve Debian package $Package/$Arch from ${indexUrl}: $($_.Exception.Message)"
    }

    return $null
}

function Resolve-DebianPackageUrlByPattern(
    [string] $BaseUrl,
    [string] $Suite,
    [string] $Component,
    [string] $Arch,
    [string] $PackagePattern
) {
    if ($NoNetwork) {
        return $null
    }

    New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
    $indexUrl = "$BaseUrl/dists/$Suite/$Component/binary-$Arch/Packages.gz"
    $cacheName = Get-SafeName "$Suite-$Component-$Arch-Packages.gz"
    $cachePath = Join-Path $cacheDir $cacheName

    try {
        if (-not (Test-Path -LiteralPath $cachePath -PathType Leaf)) {
            Invoke-WebRequest -Uri $indexUrl -OutFile $cachePath -TimeoutSec 30
        }

        $text = Read-GZipText $cachePath
        foreach ($paragraph in (Get-PackageParagraphs $text)) {
            $packageName = Get-PackageField $paragraph "Package"
            if ([string]::IsNullOrWhiteSpace($packageName) -or $packageName -notmatch $PackagePattern) {
                continue
            }

            $filename = Get-PackageField $paragraph "Filename"
            if (-not [string]::IsNullOrWhiteSpace($filename)) {
                return "$BaseUrl/$filename"
            }
        }
    }
    catch {
        Write-Warning "Could not resolve Debian package pattern $PackagePattern/$Arch from ${indexUrl}: $($_.Exception.Message)"
    }

    return $null
}

function New-Source(
    [string] $Format,
    [string] $SourceName,
    [string] $Url,
    [string[]] $FileGlobs,
    [string] $License,
    [string] $Notes
) {
    [ordered]@{
        format = $Format
        sourceName = $SourceName
        url = if ([string]::IsNullOrWhiteSpace($Url)) { $null } else { $Url }
        localPath = $null
        archivePath = $null
        fileGlobs = $FileGlobs
        license = $License
        notes = $Notes
    }
}

$sources = New-Object System.Collections.Generic.List[object]
$resolved = New-Object System.Collections.Generic.HashSet[string]
$failures = New-Object System.Collections.Generic.List[string]

$debianCandidates = @(
    @{ Format = "LinuxElfAmd64"; Arch = "amd64"; Package = "hello"; Globs = @("usr/bin/hello", "*/usr/bin/hello"); Repos = @(
        @{ Base = "https://deb.debian.org/debian"; Suite = "bookworm"; Component = "main" },
        @{ Base = "https://deb.debian.org/debian"; Suite = "trixie"; Component = "main" }
    ) },
    @{ Format = "LinuxElfi386"; Arch = "i386"; Package = "hello"; Globs = @("usr/bin/hello", "*/usr/bin/hello"); Repos = @(
        @{ Base = "https://deb.debian.org/debian"; Suite = "bookworm"; Component = "main" },
        @{ Base = "https://deb.debian.org/debian"; Suite = "trixie"; Component = "main" }
    ) },
    @{ Format = "LinuxElfArmLE"; Arch = "armhf"; Package = "hello"; Globs = @("usr/bin/hello", "*/usr/bin/hello"); Repos = @(
        @{ Base = "https://deb.debian.org/debian"; Suite = "bookworm"; Component = "main" },
        @{ Base = "https://deb.debian.org/debian"; Suite = "trixie"; Component = "main" }
    ) },
    @{ Format = "LinuxElfArm64LE"; Arch = "arm64"; Package = "hello"; Globs = @("usr/bin/hello", "*/usr/bin/hello"); Repos = @(
        @{ Base = "https://deb.debian.org/debian"; Suite = "bookworm"; Component = "main" },
        @{ Base = "https://deb.debian.org/debian"; Suite = "trixie"; Component = "main" }
    ) },
    @{ Format = "LinuxElfMipsel"; Arch = "mipsel"; Package = "hello"; Globs = @("usr/bin/hello", "*/usr/bin/hello"); Repos = @(
        @{ Base = "https://deb.debian.org/debian"; Suite = "bookworm"; Component = "main" },
        @{ Base = "https://archive.debian.org/debian"; Suite = "bullseye"; Component = "main" }
    ) },
    @{ Format = "LinuxElfMips"; Arch = "mips"; Package = "hello"; Globs = @("usr/bin/hello", "*/usr/bin/hello"); Repos = @(
        @{ Base = "https://archive.debian.org/debian"; Suite = "buster"; Component = "main" },
        @{ Base = "https://deb.debian.org/debian-ports"; Suite = "sid"; Component = "main" }
    ) },
    @{ Format = "LinuxElf32Ppc"; Arch = "powerpc"; Package = "hello"; Globs = @("usr/bin/hello", "*/usr/bin/hello"); Repos = @(
        @{ Base = "https://deb.debian.org/debian-ports"; Suite = "sid"; Component = "main" },
        @{ Base = "https://archive.debian.org/debian"; Suite = "jessie"; Component = "main" }
    ) },
    @{ Format = "LinuxElf64Ppc"; Arch = "ppc64el"; Package = "hello"; Globs = @("usr/bin/hello", "*/usr/bin/hello"); Repos = @(
        @{ Base = "https://deb.debian.org/debian"; Suite = "bookworm"; Component = "main" },
        @{ Base = "https://deb.debian.org/debian"; Suite = "trixie"; Component = "main" }
    ) },
    @{ Format = "LinuxElfRiscv64"; Arch = "riscv64"; Package = "hello"; Globs = @("usr/bin/hello", "*/usr/bin/hello"); Repos = @(
        @{ Base = "https://deb.debian.org/debian"; Suite = "trixie"; Component = "main" },
        @{ Base = "https://deb.debian.org/debian-ports"; Suite = "sid"; Component = "main" }
    ) }
)

foreach ($candidate in $debianCandidates) {
    $url = $null
    foreach ($repo in $candidate.Repos) {
        $url = Resolve-DebianPackageUrl -BaseUrl $repo.Base -Suite $repo.Suite -Component $repo.Component -Arch $candidate.Arch -Package $candidate.Package
        if (-not [string]::IsNullOrWhiteSpace($url)) {
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($url)) {
        $failures.Add("Could not resolve Debian candidate for $($candidate.Format)") | Out-Null
        continue
    }

    $sources.Add((New-Source `
        -Format $candidate.Format `
        -SourceName "Debian $($candidate.Package) $($candidate.Arch)" `
        -Url $url `
        -FileGlobs $candidate.Globs `
        -License "GPL-3.0-or-later (Debian package metadata/source package)" `
        -Notes "External original from Debian/Debian Ports package index.")) | Out-Null
    $resolved.Add($candidate.Format) | Out-Null
}

$vmlinuzUrl = $null
foreach ($suite in @("bookworm", "trixie")) {
    $vmlinuzUrl = Resolve-DebianPackageUrlByPattern `
        -BaseUrl "https://deb.debian.org/debian" `
        -Suite $suite `
        -Component "main" `
        -Arch "i386" `
        -PackagePattern '^linux-image-[0-9].*-(?:686|686-pae)$'
    if (-not [string]::IsNullOrWhiteSpace($vmlinuzUrl)) {
        break
    }
}

if (-not [string]::IsNullOrWhiteSpace($vmlinuzUrl)) {
    $sources.Add((New-Source `
        -Format "Vmlinuzi386" `
        -SourceName "Debian signed i386 Linux kernel" `
        -Url $vmlinuzUrl `
        -FileGlobs @("boot/vmlinuz-*", "*/boot/vmlinuz-*", "vmlinuz-*") `
        -License "GPL-2.0-only (Linux kernel; Debian package metadata/source package)" `
        -Notes "External i386 bzImage from the official Debian package index.")) | Out-Null
    $resolved.Add("Vmlinuzi386") | Out-Null
}
else {
    $failures.Add("Could not resolve Debian i386 vmlinuz candidate") | Out-Null
}

$staticCandidates = @(
    @{ Format = "LinuxElfAmd64"; SourceName = "Debian hello amd64"; Url = "https://deb.debian.org/debian/pool/main/h/hello/hello_2.10-5_amd64.deb"; Globs = @("hello"); License = "GPL-3.0-or-later (Debian hello package)"; Notes = "External original from Debian package pool." },
    @{ Format = "LinuxElfi386"; SourceName = "Debian hello i386"; Url = "https://deb.debian.org/debian/pool/main/h/hello/hello_2.10-5_i386.deb"; Globs = @("hello"); License = "GPL-3.0-or-later (Debian hello package)"; Notes = "External original from Debian package pool." },
    @{ Format = "LinuxElfArmLE"; SourceName = "Debian hello armhf"; Url = "https://deb.debian.org/debian/pool/main/h/hello/hello_2.10-5_armhf.deb"; Globs = @("hello"); License = "GPL-3.0-or-later (Debian hello package)"; Notes = "External original from Debian package pool." },
    @{ Format = "LinuxElfArm64LE"; SourceName = "Debian hello arm64"; Url = "https://deb.debian.org/debian/pool/main/h/hello/hello_2.10-5_arm64.deb"; Globs = @("hello"); License = "GPL-3.0-or-later (Debian hello package)"; Notes = "External original from Debian package pool." },
    @{ Format = "LinuxElfMipsel"; SourceName = "Debian hello mipsel"; Url = "https://deb.debian.org/debian/pool/main/h/hello/hello_2.10-3_mipsel.deb"; Globs = @("hello"); License = "GPL-3.0-or-later (Debian hello package)"; Notes = "External original from Debian package pool." },
    @{ Format = "LinuxElf64Ppc"; SourceName = "Debian hello ppc64el"; Url = "https://deb.debian.org/debian/pool/main/h/hello/hello_2.10-5_ppc64el.deb"; Globs = @("hello"); License = "GPL-3.0-or-later (Debian hello package)"; Notes = "External original from Debian package pool." },
    @{ Format = "LinuxElfRiscv64"; SourceName = "Debian hello riscv64"; Url = "https://deb.debian.org/debian/pool/main/h/hello/hello_2.10-5_riscv64.deb"; Globs = @("hello"); License = "GPL-3.0-or-later (Debian hello package)"; Notes = "External original from Debian package pool." },
    @{ Format = "Win32Pe"; SourceName = "Sysinternals Handle handle.exe"; Url = "https://download.sysinternals.com/files/Handle.zip"; Globs = @("handle.exe"); License = "Sysinternals Software License Terms"; Notes = "External public Windows utility archive; detector must confirm PE32." },
    @{ Format = "Win64Pe"; SourceName = "Sysinternals Handle handle64.exe"; Url = "https://download.sysinternals.com/files/Handle.zip"; Globs = @("handle64.exe"); License = "Sysinternals Software License Terms"; Notes = "External public Windows utility archive; detector must confirm PE64." }
)

foreach ($candidate in $staticCandidates) {
    $sources.Add((New-Source `
        -Format $candidate.Format `
        -SourceName $candidate.SourceName `
        -Url $candidate.Url `
        -FileGlobs $candidate.Globs `
        -License $candidate.License `
        -Notes $candidate.Notes)) | Out-Null
    $resolved.Add($candidate.Format) | Out-Null
}

$unresolvedNotes = @{
    AtariTos = "No direct binary URL verified. Preferred manual sources: FreeMiNT/SpareMiNT or EmuTOS releases with a licensed .PRG/.TOS/.TTP executable."
    DosCom = "No direct package URL verified. Preferred manual source: an official FreeDOS package containing a licensed .COM executable."
    MachAMD64 = "No stable direct asset verified. Provide a public macOS x86_64 thin binary or universal binary with an x86_64 slice and record its license."
    MachI386 = "No stable direct asset verified. Provide a public legacy macOS universal/thin binary with an i386 slice and record its license."
    Ps1Exe = "No prebuilt PS-X EXE asset with clear license verified. Prefer a licensed PSn00bSDK homebrew release, not a commercial image."
}

foreach ($format in $expectedFormats) {
    if ($resolved.Contains($format)) {
        continue
    }

    $notes = if ($unresolvedNotes.ContainsKey($format)) {
        $unresolvedNotes[$format]
    }
    else {
        "No trusted public source configured yet. Provide url or localPath manually; do not use repo-local binaries."
    }

    $sources.Add((New-Source `
        -Format $format `
        -SourceName "UNRESOLVED-$format" `
        -Url $null `
        -FileGlobs @("*") `
        -License $null `
        -Notes $notes)) | Out-Null
}

$parent = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
}

$manifest = [ordered]@{ sources = @($sources | Sort-Object { if ($_ -is [System.Collections.IDictionary]) { $_["format"] } else { $_.format } }) }
$json = $manifest | ConvertTo-Json -Depth 6
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($OutputPath, $json + [Environment]::NewLine, $utf8NoBom)

Write-Host "Wrote fixture candidate source manifest: $OutputPath"
foreach ($group in ($sources | Group-Object { if ($_ -is [System.Collections.IDictionary]) { if ([string]::IsNullOrWhiteSpace($_["url"])) { "Unresolved" } else { "Url" } } else { if ([string]::IsNullOrWhiteSpace($_.url)) { "Unresolved" } else { "Url" } } } | Sort-Object Name)) {
    Write-Host " - $($group.Name): $($group.Count)"
}

if ($Strict -and $failures.Count -gt 0) {
    throw ("Could not resolve all automatic fixture sources:`n - " + ($failures -join "`n - "))
}
