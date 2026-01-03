$ErrorActionPreference = "Stop"

Set-Location (Split-Path -Parent $PSScriptRoot)

if (Test-Path .\TestResults) { Remove-Item -Recurse -Force .\TestResults }
if (Test-Path .\coverage-report) { Remove-Item -Recurse -Force .\coverage-report }

Write-Host "Restoring dotnet tools..."
dotnet tool restore

Write-Host "Running tests with XPlat Code Coverage..."
# Stop common long-running processes that lock build outputs.
Get-Process ClipboardSync.WindowsAgent -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process RelayServer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# IMPORTANT: If you enabled RelayServer auth via environment variables in your shell,
# that can leak into `dotnet test` and cause non-auth integration tests to fail.
# Force auth OFF for the duration of this script.
$prevAuthEnabled = $env:Auth__Enabled
$prevAuthJwtSigningKey = $env:Auth__JwtSigningKey
$prevAuthGoogleClientId0 = $env:Auth__GoogleClientIds__0

try {
  $env:Auth__Enabled = "false"
  Remove-Item Env:Auth__JwtSigningKey -ErrorAction SilentlyContinue
  Remove-Item Env:Auth__GoogleClientIds__0 -ErrorAction SilentlyContinue

  # Redirect build outputs away from bin/ (helps avoid file-lock flakiness on Windows/Sandbox).
  dotnet test .\ClipboardSync.sln -c Release `
    -p:BaseOutputPath=".\artifacts\coverage-build\" `
    --collect:"XPlat Code Coverage" `
    --settings .\coverlet.runsettings `
    --results-directory .\TestResults
}
finally {
  if ($null -ne $prevAuthEnabled) { $env:Auth__Enabled = $prevAuthEnabled } else { Remove-Item Env:Auth__Enabled -ErrorAction SilentlyContinue }
  if ($null -ne $prevAuthJwtSigningKey) { $env:Auth__JwtSigningKey = $prevAuthJwtSigningKey } else { Remove-Item Env:Auth__JwtSigningKey -ErrorAction SilentlyContinue }
  if ($null -ne $prevAuthGoogleClientId0) { $env:Auth__GoogleClientIds__0 = $prevAuthGoogleClientId0 } else { Remove-Item Env:Auth__GoogleClientIds__0 -ErrorAction SilentlyContinue }
}

Write-Host "Generating HTML coverage report..."
$reports = ".\TestResults\**\coverage.cobertura.xml"
$target = ".\coverage-report"

dotnet tool run reportgenerator `
  "-reports:$reports" `
  "-targetdir:$target" `
  "-reporttypes:Html;TextSummary"

Write-Host ""
Write-Host "Done."
Write-Host "Open: $target\index.html"


