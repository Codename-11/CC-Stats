$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $repoRoot "windows\CCStats.Desktop\CCStats.Desktop.csproj"
$dotnet = "C:\Program Files\dotnet\dotnet.exe"

if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
if (-not (Test-Path $projectPath)) { throw "Project not found at '$projectPath'" }

# Kill stale instances
Get-Process -Name "CCStats" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "Starting CC-Stats... (Ctrl+C to stop)" -ForegroundColor Cyan
Write-Host "Logs will appear below." -ForegroundColor DarkGray
Write-Host "---"

try {
    & $dotnet run --project $projectPath @args 2>&1
}
finally {
    Write-Host ""
    Write-Host "Stopping CC-Stats..." -ForegroundColor Yellow
    Get-Process -Name "CCStats" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}
