param(
    [Parameter(Mandatory = $true)]
    [string] $AppDirectory,

    [switch] $RestartExplorer
)

$ErrorActionPreference = 'Stop'
$packageName = 'UpxSharp.ContextMenu'
$providerClsid = '7F5D0D52-7266-4B01-96D0-744153A0BB02'

function Assert-File {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,
        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description não encontrado: '$Path'."
    }

    Write-Host "[ok] $Description"
}

$resolvedAppDirectory = (Resolve-Path -LiteralPath $AppDirectory).Path
$manifestPath = Join-Path $resolvedAppDirectory 'UpxSharp.Sparse\AppxManifest.xml'
$packagePath = Join-Path $resolvedAppDirectory 'UpxSharp.Sparse.msix'

Assert-File -Path (Join-Path $resolvedAppDirectory 'UpxSharp.Wpf.exe') -Description 'UpxSharp.Wpf.exe'
Assert-File -Path (Join-Path $resolvedAppDirectory 'UpxSharp.Shell.comhost.dll') -Description 'UpxSharp.Shell.comhost.dll'
Assert-File -Path (Join-Path $resolvedAppDirectory 'UpxSharp.Shell.clsidmap') -Description 'UpxSharp.Shell.clsidmap'
Assert-File -Path (Join-Path $resolvedAppDirectory 'UpxSharp.Shell.dll') -Description 'UpxSharp.Shell.dll'
Assert-File -Path (Join-Path $resolvedAppDirectory 'UpxSharp.Cli.exe') -Description 'UpxSharp.Cli.exe'
Assert-File -Path $manifestPath -Description 'Sparse AppxManifest.xml'
Assert-File -Path $packagePath -Description 'UpxSharp.Sparse.msix'
Assert-File -Path (Join-Path $resolvedAppDirectory 'UpxSharp.Sparse\Assets\Square44x44Logo.png') -Description 'Square44x44Logo.png'

$signature = Get-AuthenticodeSignature -LiteralPath $packagePath
if ($signature.Status -eq 'Valid') {
    Write-Host "[ok] UpxSharp.Sparse.msix assinado e confiável: $($signature.SignerCertificate.Subject)"
} else {
    Write-Host "[aviso] UpxSharp.Sparse.msix não está com assinatura válida/confiável: $($signature.Status)"
}

[xml] $manifest = Get-Content -LiteralPath $manifestPath
$namespaceManager = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$namespaceManager.AddNamespace('com', 'http://schemas.microsoft.com/appx/manifest/com/windows10')
$namespaceManager.AddNamespace('desktop5', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10/5')

$comClass = $manifest.SelectSingleNode('//com:Class', $namespaceManager)
if (-not $comClass) {
    throw 'Manifesto não contém com:Class.'
}
if ($comClass.Id -ne $providerClsid) {
    throw "CLSID do com:Class inválido: '$($comClass.Id)'. Esperado '$providerClsid'."
}
if ($comClass.Path -ne 'UpxSharp.Shell.comhost.dll') {
    throw "Path do com:Class inválido: '$($comClass.Path)'."
}
Write-Host '[ok] Manifesto aponta para o provider e comhost corretos'

$itemTypes = $manifest.SelectNodes('//desktop5:ItemType', $namespaceManager)
if ($itemTypes.Count -ne 1 -or $itemTypes[0].Type -ne '*') {
    throw 'Manifesto deve registrar desktop5:ItemType Type="*" e filtrar .exe no GetState.'
}
Write-Host '[ok] Manifesto registra Type="*" para o menu moderno'

$package = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue
if ($package) {
    Write-Host "[ok] Pacote instalado: $($package.PackageFullName)"
    $packageComPath = "HKLM:\Software\Classes\PackagedCom\Package\$($package.PackageFullName)"
    $classIndexPath = "HKLM:\Software\Classes\PackagedCom\ClassIndex\{$providerClsid}"
    if (Test-Path -LiteralPath $packageComPath) {
        Write-Host '[ok] PackagedCom package registrado'
    } else {
        throw "PackagedCom package não encontrado: '$packageComPath'."
    }

    if (Test-Path -LiteralPath $classIndexPath) {
        Write-Host '[ok] PackagedCom ClassIndex do provider registrado'
    } else {
        throw "PackagedCom ClassIndex não encontrado: '$classIndexPath'."
    }
} else {
    Write-Host "[aviso] Pacote '$packageName' não está instalado para o usuário atual."
}

if ($RestartExplorer) {
    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    Start-Process explorer.exe
    Write-Host '[ok] Explorer reiniciado'
}

Write-Host 'Diagnóstico concluído.'
