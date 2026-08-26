param(
    [string] $PackagePath,
    [string] $CertificatePath,
    [string] $CertificateThumbprint,
    [securestring] $Password,
    [string] $SignToolPath
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

    throw "$ToolName não encontrado. Instale o Windows SDK ou informe -SignToolPath."
}

if (-not $PackagePath) {
    $repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')
    $PackagePath = Join-Path $repoRoot 'artifacts\windows-shell\UpxSharp.Sparse.msix'
}

$resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$signTool = Resolve-Tool -ExplicitPath $SignToolPath -ToolName 'SignTool.exe'

$plainPassword = $null
if ($Password) {
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
    try {
        $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

if ($CertificatePath) {
    $resolvedCertificatePath = (Resolve-Path -LiteralPath $CertificatePath).Path
    $arguments = @('sign', '/fd', 'SHA256', '/f', $resolvedCertificatePath)
    if ($plainPassword) {
        $arguments += @('/p', $plainPassword)
    }
} elseif ($CertificateThumbprint) {
    $arguments = @('sign', '/fd', 'SHA256', '/s', 'My', '/sha1', $CertificateThumbprint)
} else {
    throw "Informe -CertificatePath ou -CertificateThumbprint."
}
$arguments += $resolvedPackagePath

& $signTool @arguments
if ($LASTEXITCODE -ne 0) {
    throw "SignTool.exe falhou com código $LASTEXITCODE."
}

Write-Host "Sparse package assinado: '$resolvedPackagePath'."
