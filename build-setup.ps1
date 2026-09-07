#Requires -Version 5.1
param(
    # Regenerate wizard artwork even when assets already exist (off by default for
    # deterministic builds).
    [switch]$ForceBranding
)
$ErrorActionPreference = 'Stop'

$InstallerDir = $PSScriptRoot
$Iss = Join-Path $InstallerDir 'hermes-setup.iss'
$BrandScript = Join-Path $InstallerDir 'build-brand-assets.ps1'
$VersionFile = Join-Path $InstallerDir 'version.txt'
$AppVersion = if (Test-Path $VersionFile) { (Get-Content $VersionFile -Raw).Trim() } else { '' }
if (-not $AppVersion) { throw "version.txt missing or empty: $VersionFile" }
$parts = @($AppVersion -split '[.-]' | Where-Object { $_ -match '^\d+$' })
while ($parts.Count -lt 4) { $parts += '0' }
$AppVersionInfo = ($parts[0..3]) -join '.'
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

$brandOutputs = @('wizard-side.bmp', 'wizard-small.bmp', 'hermes-setup.ico') |
    ForEach-Object { Join-Path $InstallerDir "assets\$_" }
$needBranding = $ForceBranding -or ($brandOutputs | Where-Object { -not (Test-Path $_) })
if ($needBranding) {
    Write-Host "Generating Hermes wizard artwork..." -ForegroundColor Cyan
    & $BrandScript
} else {
    Write-Host "Wizard artwork already present - skipping regeneration (use -ForceBranding to rebuild)." -ForegroundColor DarkGray
}

Write-Host "Compiling wizard (version $AppVersion) with: $Iscc" -ForegroundColor Cyan
& $Iscc $Iss "/DMyAppVersion=$AppVersion" "/DMyAppVersionInfo=$AppVersionInfo"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed: $LASTEXITCODE" }

$setup = Join-Path $OutDir 'HermesWorkspaceSetup.exe'
if (-not (Test-Path $setup)) { throw "Setup EXE missing: $setup" }
$sizeMb = [math]::Round((Get-Item $setup).Length / 1MB, 2)
Write-Host ""
Write-Host "OK: $setup ($sizeMb MB)" -ForegroundColor Green
Write-Host "Double-click for the full install wizard (path + components)."
