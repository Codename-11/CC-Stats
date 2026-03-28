$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $repoRoot "windows\CCStats.Desktop\CCStats.Desktop.csproj"
$dotnet = "C:\Program Files\dotnet\dotnet.exe"

if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
if (-not (Test-Path $projectPath)) { throw "Project not found at '$projectPath'" }

Write-Host "Starting CC Stats... (Ctrl+C to stop)" -ForegroundColor Cyan

& $dotnet run --project $projectPath @args
