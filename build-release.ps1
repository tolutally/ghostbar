# GhostBar Release Build Script
# Usage: .\build-release.ps1 [-Version "0.1.0"]

param(
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"

Write-Host "Building GhostBar v$Version..." -ForegroundColor Cyan

# Clean previous builds
Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
dotnet clean -c Release

# Publish framework-dependent (small, requires .NET runtime on target)
Write-Host "Publishing framework-dependent executable..." -ForegroundColor Yellow
dotnet publish -c Release -r win-x64 --self-contained false

# Create release ZIP (exclude locale folders)
$publishDir = "bin\Release\net10.0-windows\win-x64\publish"
$zipName = "GhostBar-$Version-win-x64.zip"

Write-Host "Creating $zipName..." -ForegroundColor Yellow

# Remove old ZIP if exists
if (Test-Path $zipName) {
    Remove-Item $zipName -Force
}

# Get only required files (exclude locale folders)
$files = Get-ChildItem $publishDir -File
Compress-Archive -Path $files.FullName -DestinationPath $zipName -Force

# Show result
$zipSize = (Get-Item $zipName).Length / 1MB
$zipSizeRounded = [math]::Round($zipSize, 2)
Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
Write-Host "Output: $zipName - $zipSizeRounded MB" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "1. Go to https://github.com/tolutally/GhostBar/releases/new"
Write-Host "2. Tag: v$Version"
Write-Host "3. Title: GhostBar $Version"
Write-Host "4. Upload: $zipName"
Write-Host "5. Publish the release"
