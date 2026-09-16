# Build Release from command line
# Usage: powershell -ExecutionPolicy Bypass -File .\build_release.ps1

$ErrorActionPreference = 'Stop'

Write-Host "Restoring..."
dotnet restore .\MiniTrainerScheduler.sln

Write-Host "Cleaning..."
dotnet clean .\MiniTrainerScheduler.sln -c Release

Write-Host "Building (Release)..."
dotnet build .\MiniTrainerScheduler.sln -c Release

Write-Host "OK - Release build succeeded."
