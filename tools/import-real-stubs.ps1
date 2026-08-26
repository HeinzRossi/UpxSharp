param(
    [Parameter(Mandatory = $true)]
    [string] $SourceDir,

    [string] $DestinationDir,

    [string] $ManifestPath
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($DestinationDir)) {
    $DestinationDir = Join-Path $PSScriptRoot "..\src\UpxSharp\Stubs\Resources"
}

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $PSScriptRoot "..\src\UpxSharp\Stubs\Resources\stubs.manifest.json"
}

$expected = @(
    @{ format = "LinuxElfi386"; resourceName = "i386-linux.elf.stub" },
    @{ format = "LinuxElfAmd64"; resourceName = "amd64-linux.elf.stub" },
    @{ format = "LinuxElfArmLE"; resourceName = "arm-linux.elf.stub" },
    @{ format = "LinuxElfArm64LE"; resourceName = "arm64-linux.elf.stub" },
    @{ format = "LinuxElfMipsel"; resourceName = "mipsel-linux.elf.stub" },
    @{ format = "LinuxElfMips"; resourceName = "mips-linux.elf.stub" },
    @{ format = "LinuxElf32Ppc"; resourceName = "ppc32-linux.elf.stub" },
    @{ format = "LinuxElf64Ppc"; resourceName = "ppc64-linux.elf.stub" },
    @{ format = "LinuxElfRiscv64"; resourceName = "riscv64-linux.elf.stub" },
    @{ format = "MachI386"; resourceName = "i386-mach.macho.stub" },
    @{ format = "MachAMD64"; resourceName = "amd64-mach.macho.stub" },
    @{ format = "Vmlinuzi386"; resourceName = "i386-vmlinuz.stub" },
    @{ format = "DosCom"; resourceName = "dos-com.stub" },
    @{ format = "DosExe"; resourceName = "dos-exe.stub" },
    @{ format = "AtariTos"; resourceName = "atari-tos.stub" },
    @{ format = "Ps1Exe"; resourceName = "ps1-exe.stub" },
    @{ format = "Win32Pe"; resourceName = "i386-win32.pe.stub" },
    @{ format = "Win64Pe"; resourceName = "amd64-win64.pe.stub" }
)

if (-not (Test-Path -LiteralPath $SourceDir -PathType Container)) {
    throw "SourceDir not found: $SourceDir"
}

New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null

$manifestStubs = @()
foreach ($stub in $expected) {
    $source = Join-Path $SourceDir $stub.resourceName
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Missing required real stub: $($stub.resourceName)"
    }

    $destination = Join-Path $DestinationDir $stub.resourceName
    Copy-Item -LiteralPath $source -Destination $destination -Force

    $item = Get-Item -LiteralPath $destination
    $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash

    $manifestStubs += [ordered]@{
        format = $stub.format
        resourceName = $stub.resourceName
        kind = "real"
        size = $item.Length
        sha256 = $hash
    }
}

$manifest = [ordered]@{ stubs = $manifestStubs }
$json = $manifest | ConvertTo-Json -Depth 4
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($ManifestPath, $json + [Environment]::NewLine, $utf8NoBom)

Write-Host "Imported $($manifestStubs.Count) real stubs into $DestinationDir"
