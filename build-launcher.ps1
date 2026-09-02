#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$InstallerDir = $PSScriptRoot
if (-not $InstallerDir) { $InstallerDir = Split-Path -Parent $MyInvocation.MyCommand.Path }

$Proj = Join-Path $InstallerDir 'launcher\HermesLauncher.csproj'
$OutDir = Join-Path $InstallerDir 'dist'

if (-not (Test-Path $Proj)) { throw "Project file not found: $Proj" }

# Stop running launcher if open so binary can be overwritten
Get-Process -Name 'HermesLauncher' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

Write-Host "Building Hermes Modern Launcher (WPF Single-File EXE)..." -ForegroundColor Cyan

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

Push-Location (Join-Path $InstallerDir 'launcher')
try {
    dotnet publish HermesLauncher.csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $OutDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
} finally {
    Pop-Location
}

$exe = Join-Path $OutDir 'HermesLauncher.exe'
if (-not (Test-Path $exe)) { throw "Target EXE missing: $exe" }

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 2)
Write-Host ""
Write-Host "==================================================" -ForegroundColor Green
Write-Host " SUCCESS: $exe ($sizeMb MB)" -ForegroundColor Green
Write-Host " Modern Hermes Launcher & Service Manager is ready!" -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Green
