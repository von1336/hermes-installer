#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$InstallerDir = $PSScriptRoot
$Iss = Join-Path $InstallerDir 'hermes-setup.iss'
$BrandScript = Join-Path $InstallerDir 'build-brand-assets.ps1'
$IsccCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)
$Iscc = $IsccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $Iscc) {
    throw 'Inno Setup 6 not found. Install: winget install JRSoftware.InnoSetup'
}
if (-not (Test-Path $Iss)) { throw "Missing $Iss" }
if (-not (Test-Path $BrandScript)) { throw "Missing $BrandScript" }

$OutDir = Join-Path $InstallerDir 'dist'
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

Write-Host "Generating Hermes wizard artwork..." -ForegroundColor Cyan
& $BrandScript

Write-Host "Compiling wizard with: $Iscc" -ForegroundColor Cyan
& $Iscc $Iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed: $LASTEXITCODE" }

$setup = Join-Path $OutDir 'HermesWorkspaceSetup.exe'
if (-not (Test-Path $setup)) { throw "Setup EXE missing: $setup" }
$sizeMb = [math]::Round((Get-Item $setup).Length / 1MB, 2)
Write-Host ""
Write-Host "OK: $setup ($sizeMb MB)" -ForegroundColor Green
Write-Host "Double-click for the full install wizard (path + components)."
