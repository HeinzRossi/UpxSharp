param(
    [Parameter(Mandatory = $true)]
    [string] $AppDirectory,

    [string] $PackagePath,

    [string] $ManifestPath,

    [switch] $UseLooseManifest
)

$ErrorActionPreference = 'Stop'
$packageName = 'UpxSharp.ContextMenu'
$providerClsid = '7F5D0D52-7266-4B01-96D0-744153A0BB02'

$resolvedAppDirectory = (Resolve-Path -LiteralPath $AppDirectory).Path
$appExe = Join-Path $resolvedAppDirectory 'UpxSharp.Wpf.exe'
$extensionDll = Join-Path $resolvedAppDirectory 'UpxSharp.Shell.comhost.dll'

if (-not (Test-Path -LiteralPath $appExe -PathType Leaf)) {
    throw "UpxSharp.Wpf.exe não encontrado em '$resolvedAppDirectory'."
}

if (-not (Test-Path -LiteralPath $extensionDll -PathType Leaf)) {
    throw "UpxSharp.Shell.comhost.dll não encontrada em '$resolvedAppDirectory'. Compile a shell extension antes de instalar o menu moderno."
}

try {
    $existingPackage = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue
    if ($existingPackage) {
        Write-Host "Removendo sparse package existente: $($existingPackage.PackageFullName)"
        $existingPackage | Remove-AppxPackage
    }

    if ($UseLooseManifest) {
        if (-not $ManifestPath) {
            $outputManifest = Join-Path $resolvedAppDirectory 'UpxSharp.Sparse\AppxManifest.xml'
            if (Test-Path -LiteralPath $outputManifest -PathType Leaf) {
                $ManifestPath = $outputManifest
            } else {
                $repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')
                $ManifestPath = Join-Path $repoRoot 'packaging\UpxSharp.Sparse\AppxManifest.xml'
            }
        }

        $resolvedManifest = (Resolve-Path -LiteralPath $ManifestPath).Path
        Add-AppxPackage -Register $resolvedManifest -ExternalLocation $resolvedAppDirectory
    } else {
        if (-not $PackagePath) {
            $PackagePath = Join-Path $resolvedAppDirectory 'UpxSharp.Sparse.msix'
        }

        if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
            throw "UpxSharp.Sparse.msix não encontrado em '$resolvedAppDirectory'. Execute prepare-dev-signed-package.ps1 para gerar e assinar o pacote."
        }

        $resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path
        Add-AppxPackage -Path $resolvedPackage -ExternalLocation $resolvedAppDirectory
    }
} catch {
    $detail = $_ | Out-String
    if ($detail -match '0x80073CFF' -or $detail -match 'sideload' -or $detail -match 'licen[çc]a de desenvolvedor') {
        throw "O Windows recusou o pacote moderno porque o MSIX não está assinado/confiável ou o sistema bloqueia sideload. O menu clássico continua sendo o fallback seguro."
    }

    if ($detail -match '0x800B0109' -or $detail -match 'certificado' -or $detail -match 'certificate') {
        throw "O Windows recusou o pacote moderno porque a raiz do certificado do MSIX não é confiável para o AppX Deployment Service. Para certificado autoassinado, instale o .cer em LocalMachine\\Root usando PowerShell como Administrador."
    }

    throw
}

$package = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue
if (-not $package) {
    throw "Sparse package '$packageName' não foi detectado após instalação."
}

$packageComPath = "HKLM:\Software\Classes\PackagedCom\Package\$($package.PackageFullName)"
$classIndexPath = "HKLM:\Software\Classes\PackagedCom\ClassIndex\{$providerClsid}"

if (-not (Test-Path -LiteralPath $packageComPath)) {
    throw "Sparse package instalado, mas PackagedCom não foi registrado: '$packageComPath'."
}

if (-not (Test-Path -LiteralPath $classIndexPath)) {
    throw "Sparse package instalado, mas PackagedCom ClassIndex do provider não foi registrado: '$classIndexPath'."
}

Write-Host "Sparse package registrado para '$resolvedAppDirectory'. Reinicie o Explorer para recarregar o menu moderno."
