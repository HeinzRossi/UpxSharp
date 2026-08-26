param(
    [string] $PackageName = 'UpxSharp.ContextMenu'
)

$ErrorActionPreference = 'Stop'

$packages = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue

foreach ($package in $packages) {
    Remove-AppxPackage -Package $package.PackageFullName
}

if (-not $packages) {
    Write-Host "Sparse package '$PackageName' não estava instalado."
} else {
    Write-Host "Sparse package '$PackageName' removido. Reinicie o Explorer para recarregar o menu moderno."
}
