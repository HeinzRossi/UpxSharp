param(
    [string] $PackageDirectory,
    [string] $OutputPath,
    [string] $MakeAppxPath
)

$ErrorActionPreference = 'Stop'

function Resolve-Tool {
    param(
        [string] $ExplicitPath,
        [string] $ToolName
    )

    if ($ExplicitPath) {
        $resolved = Resolve-Path -LiteralPath $ExplicitPath -ErrorAction Stop
        return $resolved.Path
    }

    $command = Get-Command $ToolName -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $kitsRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path -LiteralPath $kitsRoot) {
        $candidate = Get-ChildItem -LiteralPath $kitsRoot -Filter $ToolName -Recurse -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) {
            return $candidate.FullName
        }
    }

    throw "$ToolName não encontrado. Instale o Windows SDK ou informe -MakeAppxPath."
}

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')

if (-not $PackageDirectory) {
    $PackageDirectory = Join-Path $repoRoot 'packaging\UpxSharp.Sparse'
}

if (-not $OutputPath) {
    $OutputPath = Join-Path $repoRoot 'artifacts\windows-shell\UpxSharp.Sparse.msix'
}

$resolvedPackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$manifestPath = Join-Path $resolvedPackageDirectory 'AppxManifest.xml'

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "AppxManifest.xml não encontrado em '$resolvedPackageDirectory'."
}

$resolvedOutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$makeAppx = Resolve-Tool -ExplicitPath $MakeAppxPath -ToolName 'MakeAppx.exe'

& $makeAppx pack /d $resolvedPackageDirectory /p $resolvedOutputPath /o /nv
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx.exe falhou com código $LASTEXITCODE."
}

Write-Host "Sparse package gerado em '$resolvedOutputPath'."
