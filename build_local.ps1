<#
.SYNOPSIS
    Build CC-Stats locally as a self-contained single-file exe, matching CI output.
    Use this to test version changes, self-update, and release builds.

.PARAMETER Version
    Version to bake into the exe (default: reads from csproj).
    Examples: "0.3.0", "1.0.0-beta"

.PARAMETER Run
    Launch the built exe after publishing.

.PARAMETER Clean
    Clean build artifacts before building.

.EXAMPLE
    .\build_local.ps1                      # Build with csproj version
    .\build_local.ps1 -Version 0.3.0       # Build as v0.3.0
    .\build_local.ps1 -Version 0.3.0 -Run  # Build and launch
    .\build_local.ps1 -Clean               # Clean + build
#>
param(
    [string]$Version,
    [switch]$Run,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$repoRoot  = Split-Path -Parent $MyInvocation.MyCommand.Path
$slnPath   = Join-Path $repoRoot "windows\CCStats.Windows.sln"
$csprojPath = Join-Path $repoRoot "windows\CCStats.Desktop\CCStats.Desktop.csproj"
$outDir    = Join-Path $repoRoot "publish"
$exePath   = Join-Path $outDir "CCStats.exe"

# Read version from csproj if not specified
if (-not $Version) {
    $csproj = [xml](Get-Content $csprojPath)
    $Version = $csproj.Project.PropertyGroup.Version
    Write-Host "Using csproj version: $Version" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "  CC-Stats Local Build" -ForegroundColor Cyan
Write-Host "  Version:  v$Version" -ForegroundColor White
Write-Host "  Output:   $outDir" -ForegroundColor White
Write-Host ""

# Kill stale instances
Get-Process -Name "CCStats" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if ($Clean) {
    Write-Host "[1/3] Cleaning..." -ForegroundColor Yellow
    & dotnet clean $slnPath --configuration Release --verbosity quiet 2>&1 | Out-Null
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
}

# Restore
Write-Host "[1/3] Restoring..." -ForegroundColor Yellow
& dotnet restore $slnPath --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "Restore failed" }

# Publish (mirrors CI exactly)
Write-Host "[2/3] Publishing v$Version (self-contained, single-file)..." -ForegroundColor Yellow
& dotnet publish $csprojPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" `
    -p:FileVersion="$Version.0" `
    --output $outDir `
    --verbosity quiet

if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

# Verify
$fileInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
$sizeKB = [math]::Round((Get-Item $exePath).Length / 1024)
$sizeMB = [math]::Round((Get-Item $exePath).Length / 1MB, 1)

Write-Host ""
Write-Host "[3/3] Build complete!" -ForegroundColor Green
Write-Host "  Exe:      $exePath" -ForegroundColor White
Write-Host "  Size:     $sizeMB MB ($sizeKB KB)" -ForegroundColor White
Write-Host "  Product:  $($fileInfo.ProductVersion)" -ForegroundColor White
Write-Host "  File Ver: $($fileInfo.FileVersion)" -ForegroundColor White
Write-Host ""

if ($Run) {
    Write-Host "Launching..." -ForegroundColor Cyan
    Start-Process $exePath
}
