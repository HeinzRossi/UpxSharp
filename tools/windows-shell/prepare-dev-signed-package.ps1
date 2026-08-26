param(
    [string] $Configuration = 'Debug',

    [string] $CertificatePath,

    [string] $CertificateThumbprint,

    [securestring] $Password,

    [string] $MakeAppxPath,

    [string] $SignToolPath,

    [switch] $SkipTrust,

    [switch] $TrustLocalMachine,

    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')
$artifactsDirectory = Join-Path $repoRoot 'artifacts\windows-shell'
$packagePath = Join-Path $artifactsDirectory 'UpxSharp.Sparse.msix'
$wpfOutputDirectory = Join-Path $repoRoot "src\UpxSharp.Wpf\bin\$Configuration\net10.0-windows"

function Add-DevCertificateToCurrentUserTrustedPeople {
    param(
        [Parameter(Mandatory = $true)]
        [string] $CertificateFile
    )

    $process = Start-Process `
        -FilePath 'certutil.exe' `
        -ArgumentList @('-user', '-addstore', 'TrustedPeople', $CertificateFile) `
        -NoNewWindow `
        -PassThru

    if (-not $process.WaitForExit(15000)) {
        $process.Kill()
        throw "O Windows abriu uma confirmação invisível para confiar o certificado. Instale manualmente '$CertificateFile' em CurrentUser\\TrustedPeople ou rode o script em um PowerShell visível."
    }

    if ($process.ExitCode -ne 0) {
        throw "certutil.exe falhou ao confiar o certificado dev com código $($process.ExitCode)."
    }
}

function Add-DevCertificateToLocalMachineRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string] $CertificateFile
    )

    $process = Start-Process `
        -FilePath 'certutil.exe' `
        -ArgumentList @('-f', '-addstore', 'Root', $CertificateFile) `
        -NoNewWindow `
        -PassThru `
        -Wait

    if ($process.ExitCode -ne 0) {
        throw "certutil.exe falhou ao confiar o certificado dev em LocalMachine\\Root com código $($process.ExitCode). Rode o script em PowerShell como Administrador."
    }
}

if (-not $CertificatePath) {
    $CertificatePath = Join-Path $artifactsDirectory 'UpxSharp.DevCertificate.pfx'
}

if (-not $SkipBuild) {
    dotnet build (Join-Path $repoRoot 'UpxSharp.sln') --no-restore -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build falhou com código $LASTEXITCODE."
    }
}

$certificateThumbprint = $CertificateThumbprint
if ($Password) {
    & (Join-Path $PSScriptRoot 'new-dev-certificate.ps1') `
        -OutputPath $CertificatePath `
        -Password $Password `
        -TrustForCurrentUser
} else {
    if ($certificateThumbprint) {
        $certificate = Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' |
            Where-Object { $_.Thumbprint -eq $certificateThumbprint } |
            Select-Object -First 1
        if (-not $certificate) {
            throw "Certificado '$certificateThumbprint' não encontrado em CurrentUser\My."
        }
    } else {
        $certificate = Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' |
            Where-Object { $_.Subject -eq 'CN=UpxSharp' -and $_.HasPrivateKey } |
            Sort-Object NotBefore -Descending |
            Select-Object -First 1
    }

    if (-not $certificate) {
        Write-Host "Criando certificado dev CN=UpxSharp em CurrentUser\My..."
        $certificate = New-SelfSignedCertificate `
            -Type Custom `
            -Subject 'CN=UpxSharp' `
            -KeyUsage DigitalSignature `
            -KeySpec Signature `
            -KeyExportPolicy Exportable `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -Provider 'Microsoft Enhanced RSA and AES Cryptographic Provider' `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3') `
            -NotAfter (Get-Date).AddYears(3)
    } else {
        Write-Host "Reutilizando certificado dev em CurrentUser\My: $($certificate.Thumbprint)."
    }

    $certificateFile = Join-Path $artifactsDirectory 'UpxSharp.DevCertificate.cer'
    New-Item -ItemType Directory -Path $artifactsDirectory -Force | Out-Null
    Export-Certificate -Cert $certificate -FilePath $certificateFile | Out-Null

    if ($SkipTrust) {
        Write-Host "Confiança do certificado pulada. Instale manualmente '$certificateFile' em CurrentUser\TrustedPeople antes de instalar o MSIX."
    } elseif ($TrustLocalMachine) {
        Write-Host "Confiando certificado dev em LocalMachine\Root..."
        Add-DevCertificateToLocalMachineRoot -CertificateFile $certificateFile
    } else {
        Write-Host "Confiando certificado dev em CurrentUser\TrustedPeople..."
        Add-DevCertificateToCurrentUserTrustedPeople -CertificateFile $certificateFile
    }

    $certificateThumbprint = $certificate.Thumbprint
    Write-Host "Certificado dev disponível em CurrentUser\My: $certificateThumbprint."
}

& (Join-Path $PSScriptRoot 'build-sparse-package.ps1') `
    -OutputPath $packagePath `
    -MakeAppxPath $MakeAppxPath

if ($Password) {
    & (Join-Path $PSScriptRoot 'sign-sparse-package.ps1') `
        -PackagePath $packagePath `
        -CertificatePath $CertificatePath `
        -Password $Password `
        -SignToolPath $SignToolPath
} else {
    & (Join-Path $PSScriptRoot 'sign-sparse-package.ps1') `
        -PackagePath $packagePath `
        -CertificateThumbprint $certificateThumbprint `
        -SignToolPath $SignToolPath
}

if (-not (Test-Path -LiteralPath $wpfOutputDirectory -PathType Container)) {
    throw "Output WPF não encontrado: '$wpfOutputDirectory'."
}

Copy-Item -LiteralPath $packagePath -Destination (Join-Path $wpfOutputDirectory 'UpxSharp.Sparse.msix') -Force

Write-Host "MSIX dev assinado copiado para '$wpfOutputDirectory'."
Write-Host "Agora use o botão 'Instalar integração' no WPF ou execute install-modern-context-menu.ps1."
