param(
  [string]$Runtime = "win-x64",
  [string]$Configuration = "Release",
  [switch]$SingleFile = $true,
  [switch]$SelfContained = $true
)

$ErrorActionPreference = "Stop"

$proj = Join-Path $PSScriptRoot "..\clients\ClipboardSync.WindowsAgent\ClipboardSync.WindowsAgent.csproj"
$outDir = Join-Path $PSScriptRoot "..\artifacts\windows-agent\$Runtime"
$exeName = "ClipboardSync.WindowsAgent.exe"
$exePath = Join-Path $outDir $exeName

Write-Host "Stopping running ClipboardSync.WindowsAgent (if any)..." -ForegroundColor Cyan
Get-Process ClipboardSync.WindowsAgent -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# Some environments (tray apps, corporate endpoints) can keep the exe locked even after the name-based stop.
# Try to stop any process whose executable path matches the publish target.
Get-Process -ErrorAction SilentlyContinue |
  Where-Object { $_.Path -and ($_.Path -ieq $exePath) } |
  Stop-Process -Force -ErrorAction SilentlyContinue

# Give Windows a moment to release file handles (tray shutdown, etc.)
Start-Sleep -Milliseconds 300

Write-Host "Cleaning publish output: $outDir" -ForegroundColor Cyan
if (Test-Path $outDir) {
  Remove-Item $outDir -Recurse -Force -ErrorAction SilentlyContinue
}
if (Test-Path $outDir) {
  # If the directory still exists, something is locking it. Fall back to a timestamped output folder.
  $suffix = Get-Date -Format "yyyyMMdd-HHmmss"
  $outDir = Join-Path $PSScriptRoot "..\artifacts\windows-agent\$Runtime-$suffix"
  $exePath = Join-Path $outDir $exeName
  Write-Host "Publish output is locked; using fallback output: $outDir" -ForegroundColor Yellow
}
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

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
$exit = $LASTEXITCODE
if ($exit -ne 0) {
  throw "dotnet publish failed with exit code $exit"
}

Write-Host ""
Write-Host "Publish complete." -ForegroundColor Green
Write-Host "Output: $outDir" -ForegroundColor Green


