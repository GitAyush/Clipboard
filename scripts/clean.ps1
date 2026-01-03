$ErrorActionPreference = "Stop"

Set-Location (Split-Path -Parent $PSScriptRoot)

Write-Host "Stopping running processes (best effort)..." -ForegroundColor Cyan
Get-Process ClipboardSync.WindowsAgent -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process RelayServer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Start-Sleep -Milliseconds 200

$paths = @(
  ".\artifacts",
  ".\TestResults",
  ".\coverage-report"
)

foreach ($p in $paths) {
  if (Test-Path $p) {
    Write-Host "Removing $p" -ForegroundColor Cyan
    Remove-Item -Recurse -Force $p -ErrorAction SilentlyContinue
  }
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green


