#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$Root = if ($PSScriptRoot) { Split-Path -Parent $PSScriptRoot } else { Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path) }
# This script lives in installer\exe — parent is installer\
$InstallerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ((Split-Path -Leaf $InstallerDir) -eq 'exe') {
    $InstallerDir = Split-Path -Parent $InstallerDir
}

$Proj = Join-Path $InstallerDir 'exe\HermesInstaller.csproj'
$OutDir = Join-Path $InstallerDir 'dist'
$Ps1 = Join-Path $InstallerDir 'install-hermes.ps1'

if (-not (Test-Path $Ps1)) { throw "Missing $Ps1" }
if (-not (Test-Path $Proj)) { throw "Missing $Proj" }

$marker = Select-String -Path $Ps1 -Pattern 'InstallerVersion\s*=\s*''([^'']+)''' | Select-Object -First 1
$ver = if ($marker) { $marker.Matches.Groups[1].Value } else { 'unknown' }
Write-Host "Building HermesInstaller.exe from script version: $ver" -ForegroundColor Cyan

# Keep EXE version gate in sync with install-hermes.ps1
$programCs = Join-Path $InstallerDir 'exe\Program.cs'
$cs = Get-Content $programCs -Raw
$cs2 = [regex]::Replace(
    $cs,
    'private const string ExpectedVersionMarker = "[^"]+";',
    "private const string ExpectedVersionMarker = `"$ver`";"
)
if ($cs2 -ne $cs) {
    Set-Content -Path $programCs -Value $cs2 -Encoding UTF8 -NoNewline
    Write-Host "  Synced ExpectedVersionMarker -> $ver"
}

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

Push-Location (Join-Path $InstallerDir 'exe')
try {
    dotnet publish HermesInstaller.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $OutDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }
} finally {
    Pop-Location
}

$exe = Join-Path $OutDir 'HermesInstaller.exe'
if (-not (Test-Path $exe)) { throw "EXE not produced: $exe" }

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "OK: $exe ($sizeMb MB)" -ForegroundColor Green
Write-Host "Double-click to install. Uninstall: HermesInstaller.exe --uninstall"
Write-Host "Ship this single file (script is embedded)."
