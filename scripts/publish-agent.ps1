param(
  [string]$Runtime = "win-x64",
  [string]$Configuration = "Release",
  [switch]$SingleFile = $true,
  [switch]$SelfContained = $true
)

$ErrorActionPreference = "Stop"

$proj = Join-Path $PSScriptRoot "..\clients\ClipboardSync.WindowsAgent\ClipboardSync.WindowsAgent.csproj"
$outDir = Join-Path $PSScriptRoot "..\artifacts\windows-agent\$Runtime"

Write-Host "Stopping running ClipboardSync.WindowsAgent (if any)..." -ForegroundColor Cyan
Get-Process ClipboardSync.WindowsAgent -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# Give Windows a moment to release file handles (tray shutdown, etc.)
Start-Sleep -Milliseconds 300

Write-Host "Cleaning publish output: $outDir" -ForegroundColor Cyan
if (Test-Path $outDir) {
  Remove-Item $outDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $outDir | Out-Null

Write-Host "Publishing..." -ForegroundColor Cyan

$args = @(
  "publish", $proj,
  "-c", $Configuration,
  "-r", $Runtime,
  "-o", $outDir
)

if ($SelfContained) { $args += @("--self-contained", "true") } else { $args += @("--self-contained", "false") }

if ($SingleFile) {
  $args += @(
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true"
  )
}

dotnet @args

Write-Host ""
Write-Host "Publish complete." -ForegroundColor Green
Write-Host "Output: $outDir" -ForegroundColor Green


