$ErrorActionPreference = "Stop"

Set-Location (Split-Path -Parent $PSScriptRoot)

if (Test-Path .\TestResults) { Remove-Item -Recurse -Force .\TestResults }
if (Test-Path .\coverage-report) { Remove-Item -Recurse -Force .\coverage-report }

Write-Host "Restoring dotnet tools..."
dotnet tool restore

Write-Host "Running tests with XPlat Code Coverage..."
dotnet test .\ClipboardSync.sln -c Release --collect:"XPlat Code Coverage" --settings .\coverlet.runsettings --results-directory .\TestResults

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


