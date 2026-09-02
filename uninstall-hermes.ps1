#Requires -Version 5.1
param(
    [switch]$KeepUserData,
    [switch]$RemoveMemOS,
    [switch]$RemoveObsidianSkills,
    [switch]$RemoveAllData
)

$ErrorActionPreference = 'Continue'

$hermesHome = Join-Path $env:LOCALAPPDATA 'hermes'
$metaPath = Join-Path $hermesHome 'install-meta.json'
$markerPath = Join-Path $hermesHome 'install-complete.json'

Write-Host 'Uninstalling Hermes scheduled tasks, services and firewall rules...' -ForegroundColor Cyan
Write-Host '  Policy: user data is preserved unless you pass -RemoveMemOS / -RemoveObsidianSkills / -RemoveAllData' -ForegroundColor DarkGray

# Resolve expected workspace dir from the install manifest (fallback: default location).
$workspaceDir = Join-Path $env:USERPROFILE 'hermes-workspace'
try {
    if (Test-Path -LiteralPath $metaPath) {
        $meta = Get-Content -LiteralPath $metaPath -Raw -ErrorAction Stop | ConvertFrom-Json
        if ($meta.workspaceDir) { $workspaceDir = [string]$meta.workspaceDir }
    }
} catch { }

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Ownership evidence: executable inside Hermes home/workspace, or command line referencing them.
# Third-party daemons (ollama) are never force-stopped.
function Test-HermesOwnedProcess {
    param([int]$ProcessId)
    $proc = Get-CimInstance Win32_Process -Filter "ProcessId=$ProcessId" -ErrorAction SilentlyContinue
    if (-not $proc) { return $false }
    $name = ''
    if ($proc.Name) { $name = ([string]$proc.Name).ToLowerInvariant() }
    if ($name -eq 'ollama' -or $name -eq 'ollama.exe') { return $false }
    $exe = ''
    if ($proc.ExecutablePath) { $exe = [string]$proc.ExecutablePath }
    $cmd = ''
    if ($proc.CommandLine) { $cmd = [string]$proc.CommandLine }
    $homeN = $hermesHome.TrimEnd('\')
    $wsN = $workspaceDir.TrimEnd('\')
    if ($exe -ne '' -and $exe.StartsWith($homeN, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    if ($exe -ne '' -and $wsN -ne '' -and $exe.StartsWith($wsN, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    if ($cmd -ne '' -and $cmd.IndexOf($homeN, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
    if ($cmd -ne '' -and $wsN -ne '' -and $cmd.IndexOf($wsN, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
    return $false
}

Write-Host '  Stopping Hermes services...' -ForegroundColor Yellow
foreach ($cmd in @(
    @{ exe = 'hermes'; args = 'gateway stop' },
    @{ exe = 'hermes'; args = 'dashboard stop' }
)) {
    try {
        if (Get-Command $cmd.exe -ErrorAction SilentlyContinue) {
            Start-Process -FilePath $cmd.exe -ArgumentList $cmd.args -Wait -NoNewWindow -ErrorAction SilentlyContinue
        }
    } catch { }
}

# Stop port owners only when Hermes ownership is confirmed (PID/exe/cmdline evidence).
foreach ($port in @(3000, 8642, 9119, 11434)) {
    try {
        $owners = @(Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty OwningProcess -Unique)
        foreach ($ownerPid in $owners) {
            $ownerPid = [int]$ownerPid
            if ($ownerPid -le 4) { continue }
            if (Test-HermesOwnedProcess -ProcessId $ownerPid) {
                Stop-Process -Id $ownerPid -Force -ErrorAction SilentlyContinue
                Write-Host "  Stopped Hermes-owned process pid=$ownerPid (port $port)"
            } else {
                Write-Host "  Skipped pid=$ownerPid on port $port (no Hermes ownership evidence)" -ForegroundColor DarkGray
            }
        }
    } catch { }
}

foreach ($name in @('HermesDashboard', 'HermesWorkspace', 'HermesGateway')) {
    Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "  Removed task: $name"
}

# Firewall rule removal requires admin rights; elevate via a single child process if needed.
$fwRuleNames = @(
    'Hermes Workspace (TCP 3000)',
    'Hermes Gateway (TCP 8642)',
    'Hermes Agent Dashboard (TCP 9119)'
)
$existingFw = @($fwRuleNames | Where-Object { Get-NetFirewallRule -DisplayName $_ -ErrorAction SilentlyContinue })
$fwRemoved = $true
if ($existingFw.Count -gt 0) {
    if (Test-IsAdmin) {
        foreach ($name in $existingFw) {
            try {
                Remove-NetFirewallRule -DisplayName $name -ErrorAction Stop
                Write-Host "  Removed firewall rule: $name"
            } catch {
                $fwRemoved = $false
                Write-Host "  Could not remove firewall rule: $name ($($_.Exception.Message))" -ForegroundColor Yellow
            }
        }
    } else {
        $escaped = ($existingFw | ForEach-Object { $_.Replace("'", "''") }) -join "','"
        $elevatedScript = "@('$escaped') | ForEach-Object { Remove-NetFirewallRule -DisplayName `$_ -ErrorAction SilentlyContinue }"
        try {
            Start-Process powershell.exe -Verb RunAs -Wait -ErrorAction Stop -ArgumentList @(
                '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $elevatedScript
            )
        } catch {
            Write-Host "  Firewall elevation cancelled or failed: $($_.Exception.Message)" -ForegroundColor Yellow
        }
        foreach ($name in $existingFw) {
            if (Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue) {
                $fwRemoved = $false
            } else {
                Write-Host "  Removed firewall rule: $name"
            }
        }
    }
}
if (-not $fwRemoved) {
    Write-Host '  Some firewall rules remain. Re-run this script as Administrator to remove them.' -ForegroundColor Yellow
}

# Remove shortcuts created by install-hermes.ps1 (symmetric cleanup).
$desktopDir = [Environment]::GetFolderPath('Desktop')
$startMenuDir = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\Hermes'
foreach ($lnk in @(
    (Join-Path $desktopDir 'Hermes Connect QR.lnk'),
    (Join-Path $startMenuDir 'Hermes Connect QR.lnk')
)) {
    if (Test-Path -LiteralPath $lnk) {
        Remove-Item -LiteralPath $lnk -Force -ErrorAction SilentlyContinue
        Write-Host "  Removed shortcut: $lnk"
    }
}
if ((Test-Path -LiteralPath $startMenuDir) -and
    -not (Get-ChildItem -LiteralPath $startMenuDir -Force -ErrorAction SilentlyContinue)) {
    Remove-Item -LiteralPath $startMenuDir -Force -ErrorAction SilentlyContinue
    Write-Host '  Removed empty Start Menu folder: Programs\Hermes'
}

$startup = [Environment]::GetFolderPath('Startup')
if (-not $startup) {
    $startup = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'
}
foreach ($cmd in @('HermesDashboard.cmd', 'HermesWorkspace.cmd')) {
    $p = Join-Path $startup $cmd
    if (Test-Path -LiteralPath $p) {
        Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue
        Write-Host "  Removed startup script: $cmd"
    }
}

# Uninstall always invalidates the completion marker, even when user data is kept.
if (Test-Path -LiteralPath $markerPath) {
    Remove-Item -LiteralPath $markerPath -Force -ErrorAction SilentlyContinue
    Write-Host '  Invalidated install completion marker.'
}

if ($RemoveAllData -or $RemoveMemOS) {
    $memosPaths = @(
        (Join-Path $env:LOCALAPPDATA 'hermes\memos-plugin'),
        (Join-Path $env:USERPROFILE '.hermes\memos-plugin')
    )
    foreach ($p in $memosPaths) {
        if (Test-Path $p) {
            Write-Host "  Removing MemOS plugin data: $p" -ForegroundColor Yellow
            Remove-Item $p -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($RemoveAllData -or $RemoveObsidianSkills) {
    $skillTargets = @(
        (Join-Path $env:USERPROFILE '.hermes\skills\obsidian-skills'),
        (Join-Path $hermesHome 'skills\obsidian-skills')
    )
    foreach ($p in $skillTargets) {
        if (Test-Path $p) {
            Write-Host "  Removing obsidian-skills: $p" -ForegroundColor Yellow
            Remove-Item $p -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($RemoveAllData) {
    if (Test-Path $hermesHome) {
        Write-Host "  Removing install home: $hermesHome" -ForegroundColor Yellow
        Remove-Item $hermesHome -Recurse -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host '  Kept %LOCALAPPDATA%\hermes (logs, connect.html, .env, install-meta.json).' -ForegroundColor DarkGray
    if (Test-Path $metaPath) {
        Write-Host ("  Manifest: {0}" -f $metaPath) -ForegroundColor DarkGray
    }
}

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
Write-Host 'Optional cleanup:'
Write-Host '  uninstall-hermes.ps1 -RemoveMemOS'
Write-Host '  uninstall-hermes.ps1 -RemoveObsidianSkills'
Write-Host '  uninstall-hermes.ps1 -RemoveAllData'
