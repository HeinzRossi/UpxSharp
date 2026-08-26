param(
    [string] $Subject = 'CN=UpxSharp',
    [string] $OutputPath,
    [securestring] $Password,
    [switch] $TrustForCurrentUser
)

$ErrorActionPreference = 'Stop'

if (-not $OutputPath) {
    $repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')
    $OutputPath = Join-Path $repoRoot 'artifacts\windows-shell\UpxSharp.DevCertificate.pfx'
}

if (-not $Password) {
    $Password = Read-Host -Prompt 'Senha do PFX de desenvolvimento' -AsSecureString
}

$resolvedOutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

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

$certificate = New-SelfSignedCertificate `
    -Type Custom `
    -Subject $Subject `
    -KeyUsage DigitalSignature `
    -KeySpec Signature `
    -KeyExportPolicy Exportable `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -Provider 'Microsoft Enhanced RSA and AES Cryptographic Provider' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3') `
    -NotAfter (Get-Date).AddYears(3)

Export-PfxCertificate -Cert $certificate -FilePath $resolvedOutputPath -Password $Password | Out-Null

if ($TrustForCurrentUser) {
    $certificateFile = [System.IO.Path]::ChangeExtension($resolvedOutputPath, '.cer')
    Export-Certificate -Cert $certificate -FilePath $certificateFile | Out-Null
    Add-DevCertificateToCurrentUserTrustedPeople -CertificateFile $certificateFile
}

Write-Host "Certificado dev exportado em '$resolvedOutputPath'. Publisher esperado: $Subject."
