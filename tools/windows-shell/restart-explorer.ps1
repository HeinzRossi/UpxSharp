$ErrorActionPreference = 'Stop'

Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Process explorer.exe

Write-Host "Explorer reiniciado."
