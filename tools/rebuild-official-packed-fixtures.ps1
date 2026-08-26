param(
    [string] $ArtifactsRoot = "C:\upxsharp-artifacts",
    [string] $InventoryPath,
    [string] $RealFixturesDir,
    [string] $UpxPackedFixturesDir,
    [string] $UpxExePath,
    [string] $ProvenancePath,
    [switch] $DryRun,
    [switch] $Strict
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($InventoryPath)) {
    $InventoryPath = Join-Path $ArtifactsRoot "fixture-inventory.json"
}
if ([string]::IsNullOrWhiteSpace($RealFixturesDir)) {
    $RealFixturesDir = Join-Path $ArtifactsRoot "real-fixtures"
}
if ([string]::IsNullOrWhiteSpace($UpxPackedFixturesDir)) {
    $UpxPackedFixturesDir = Join-Path $ArtifactsRoot "upx-packed-fixtures"
}
if ([string]::IsNullOrWhiteSpace($UpxExePath)) {
    $UpxExePath = Join-Path $ArtifactsRoot "upx-bin\upx.exe"
}
if ([string]::IsNullOrWhiteSpace($ProvenancePath)) {
    $ProvenancePath = Join-Path $ArtifactsRoot "official-packed-provenance.json"
}

if (-not (Test-Path -LiteralPath $InventoryPath -PathType Leaf)) {
    throw "Fixture inventory not found: $InventoryPath"
}
if (-not (Test-Path -LiteralPath $RealFixturesDir -PathType Container)) {
    throw "Real fixture directory not found: $RealFixturesDir"
}
if (-not (Test-Path -LiteralPath $UpxExePath -PathType Leaf)) {
    throw "Official UPX executable not found: $UpxExePath"
}

$inventory = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json
$fixtures = @($inventory.fixtures)
$failures = New-Object System.Collections.Generic.List[string]
$records = New-Object System.Collections.Generic.List[object]

$versionInfo = & $UpxExePath --version 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Could not read official UPX version from: $UpxExePath"
}
$upxVersion = ($versionInfo | Select-Object -First 1).ToString().Trim()

New-Item -ItemType Directory -Path $UpxPackedFixturesDir -Force | Out-Null

foreach ($fixture in $fixtures) {
    $originalPath = if ([System.IO.Path]::IsPathRooted($fixture.path)) {
        $fixture.path
    }
    else {
        Join-Path $RealFixturesDir $fixture.path
    }

    if (-not (Test-Path -LiteralPath $originalPath -PathType Leaf)) {
        $message = "Original fixture missing for $($fixture.format): $originalPath"
        $failures.Add($message) | Out-Null
        Write-Warning $message
        continue
    }

    $packedName = [System.IO.Path]::GetFileName($originalPath) + ".upx"
    $packedPath = Join-Path $UpxPackedFixturesDir $packedName
    $tempPath = $packedPath + ".tmp"

    Write-Host "$($fixture.format): $originalPath -> $packedPath"
    if ($DryRun) {
        continue
    }

    try {
        if (Test-Path -LiteralPath $tempPath -PathType Leaf) {
            Remove-Item -LiteralPath $tempPath -Force
        }

        Copy-Item -LiteralPath $originalPath -Destination $tempPath -Force
        $upxOutput = & $UpxExePath -f $tempPath 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "UPX exited with $LASTEXITCODE. $($upxOutput -join [Environment]::NewLine)"
        }
        if (-not (Test-Path -LiteralPath $tempPath -PathType Leaf)) {
            throw "UPX did not produce output: $tempPath"
        }

        Move-Item -LiteralPath $tempPath -Destination $packedPath -Force
        $fixture.packedPath = $packedName
        $records.Add([ordered]@{
            format = $fixture.format
            original = [System.IO.Path]::GetFileName($originalPath)
            packed = $packedName
            originalSha256 = (Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash.ToLowerInvariant()
            packedSha256 = (Get-FileHash -LiteralPath $packedPath -Algorithm SHA256).Hash.ToLowerInvariant()
            upxVersion = $upxVersion
            generatedAtUtc = [DateTime]::UtcNow.ToString("O")
        }) | Out-Null
    }
    catch {
        if (Test-Path -LiteralPath $tempPath -PathType Leaf) {
            Remove-Item -LiteralPath $tempPath -Force
        }
        $message = "Could not create official packed fixture for $($fixture.format): $($_.Exception.Message)"
        $failures.Add($message) | Out-Null
        Write-Warning $message
    }
}

if (-not $DryRun) {
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $inventoryJson = $inventory | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($InventoryPath, $inventoryJson + [Environment]::NewLine, $utf8NoBom)

    $provenance = [ordered]@{
        upxExecutable = $UpxExePath
        upxVersion = $upxVersion
        fixtures = $records
    }
    $provenanceJson = $provenance | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($ProvenancePath, $provenanceJson + [Environment]::NewLine, $utf8NoBom)
    Write-Host "Wrote provenance: $ProvenancePath"
}

if ($Strict -and $failures.Count -gt 0) {
    throw ("Official packed fixture generation failed:`n - " + ($failures -join "`n - "))
}

Write-Host "Official packed fixture generation complete. Success=$($records.Count) Failed=$($failures.Count)"
