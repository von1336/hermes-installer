#Requires -Version 5.1
# VERSION: 2026-08-30-pro-v9
# Prefer native Windows for workspace (Node already on PC). WSL is optional.
# Always prefer Tailscale IP for phone connect. Optional Ollama, MemOS, Obsidian.
# Supports GUI wizard flags from HermesWorkspaceSetup.exe (Inno Setup).
param(
    [string]$InstallDir = '',
    [string]$WorkspaceDir = '',
    [bool]$InstallTailscale = $true,
    [bool]$InstallOllama = $true,
    [bool]$InstallMemOS = $false,
    [ValidateSet('local', 'provider', 'skip')]
    [string]$MemOSMode = 'skip',
    [string]$MemOSProviderUrl = 'https://api.openai.com/v1',
    [string]$MemOSProviderKey = '',
    [string]$MemOSProviderModel = 'gpt-4o-mini',
    [bool]$InstallObsidian = $false,
    [bool]$InstallObsidianSkills = $false,
    [bool]$ConfigureFirewall = $true,
    [bool]$StartServices = $true,
    [bool]$EnableAutoStart = $true,
    [bool]$OpenConnect = $true,
    [bool]$CreateShortcuts = $true,
    [bool]$PrintConnectSecrets = $false,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$Script:InstallerVersion = '2026-08-30-pro-v9'

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $Script:HermesHome = Join-Path $env:LOCALAPPDATA 'hermes'
} else {
    $Script:HermesHome = $InstallDir.TrimEnd('\', '/')
}
$Script:HermesBin = Join-Path $Script:HermesHome 'bin'
$Script:HermesEnv = Join-Path $Script:HermesHome '.env'
if ([string]::IsNullOrWhiteSpace($WorkspaceDir)) {
    $Script:WorkspaceDir = Join-Path $env:USERPROFILE 'hermes-workspace'
} else {
    $Script:WorkspaceDir = $WorkspaceDir.TrimEnd('\', '/')
}
$Script:GatewayPort = 8642
$Script:DashboardPort = 9119
$Script:WorkspacePort = 3000
$Script:OllamaPort = 11434
$Script:Root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$Script:LogFile = Join-Path $Script:HermesHome 'install.log'
$Script:ErrorReportPath = Join-Path $Script:HermesHome 'install-error.txt'
$Script:CompletionMarker = Join-Path $Script:HermesHome 'install-complete.json'
$Script:InstallTailscale = $InstallTailscale
$Script:InstallOllama = $InstallOllama
$Script:InstallMemOS = $InstallMemOS
$Script:MemOSMode = if ($InstallMemOS -and $MemOSMode -ne 'skip') { $MemOSMode } else { 'skip' }
$Script:MemOSProviderUrl = $MemOSProviderUrl
$Script:MemOSProviderKey = $MemOSProviderKey
$Script:MemOSProviderModel = $MemOSProviderModel
$Script:InstallObsidian = $InstallObsidian
$Script:InstallObsidianSkills = $InstallObsidianSkills
$Script:ConfigureFirewall = $ConfigureFirewall
$Script:StartServices = $StartServices
$Script:EnableAutoStart = $EnableAutoStart
$Script:OpenConnect = $OpenConnect
$Script:CreateShortcuts = $CreateShortcuts
$Script:PrintConnectSecrets = $PrintConnectSecrets
$Script:NoPause = [bool]$NoPause
$Script:ComponentResults = [ordered]@{}
$Script:FailureReported = $false

if (-not (Test-Path $Script:HermesHome)) {
    New-Item -ItemType Directory -Path $Script:HermesHome -Force | Out-Null
}
Remove-Item -LiteralPath $Script:CompletionMarker -Force -ErrorAction SilentlyContinue

try {
    [Environment]::SetEnvironmentVariable('HERMES_HOME', $Script:HermesHome, 'User')
    $env:HERMES_HOME = $Script:HermesHome
} catch { }

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
    Add-Content -Path $Script:LogFile -Value ("[{0}] {1}" -f (Get-Date -Format o), $Message) -ErrorAction SilentlyContinue
}

function Get-SafeErrorText($ErrorRecord) {
    if ($null -eq $ErrorRecord) { return 'Unknown error (no error record).' }
    try {
        if ($ErrorRecord.PSObject.Properties['Exception'] -and $ErrorRecord.Exception) {
            return [string]$ErrorRecord.Exception.Message
        }
        return [string]$ErrorRecord
    } catch {
        return [string]$ErrorRecord
    }
}

function Protect-SecretText([string]$Text) {
    if ([string]::IsNullOrEmpty($Text)) { return $Text }
    $secretValues = @($Script:MemOSProviderKey, $apiKey, $hermesPassword) | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and $_.Length -ge 6
    }
    foreach ($sv in $secretValues) {
        $redacted = '****' + $sv.Substring($sv.Length - 4)
        $Text = $Text.Replace($sv, $redacted)
    }
    return $Text
}

function Write-InstallFailureReport {
    param($ErrorRecord)

    if ($Script:FailureReported) { return }
    $Script:FailureReported = $true

    $homeDir = $Script:HermesHome
    if ([string]::IsNullOrWhiteSpace($homeDir)) {
        $homeDir = Join-Path $env:LOCALAPPDATA 'hermes'
    }
    if (-not (Test-Path $homeDir)) {
        New-Item -ItemType Directory -Path $homeDir -Force | Out-Null
    }
    $reportPath = Join-Path $homeDir 'install-error.txt'
    $Script:ErrorReportPath = $reportPath
    $logPath = Join-Path $homeDir 'install.log'

    $message = Get-SafeErrorText $ErrorRecord
    $category = ''
    $scriptStack = ''
    $position = ''
    try { $category = [string]$ErrorRecord.CategoryInfo } catch { }
    try { $scriptStack = [string]$ErrorRecord.ScriptStackTrace } catch { }
    try { $position = [string]$ErrorRecord.InvocationInfo.PositionMessage } catch { }

    $tail = ''
    if (Test-Path $logPath) {
        try {
            $tail = (Get-Content $logPath -Tail 80 -ErrorAction SilentlyContinue) -join [Environment]::NewLine
        } catch { }
    }

    $components = ''
    try {
        if ($Script:ComponentResults -and $Script:ComponentResults.Count -gt 0) {
            $components = ($Script:ComponentResults.GetEnumerator() | ForEach-Object {
                $v = $_.Value
                ('  {0}: {1} - {2}' -f $_.Key, $v.outcome, $v.message)
            }) -join [Environment]::NewLine
        }
    } catch { }

    $report = @"
Hermes installer FAILED
=======================
Time:     $(Get-Date -Format o)
Version:  $Script:InstallerVersion
Install:  $homeDir
Workspace:$Script:WorkspaceDir
Machine:  $env:COMPUTERNAME
User:     $env:USERNAME

ERROR
-----
$message

Category:
$category

Position:
$position

Script stack:
$scriptStack

Component outcomes
------------------
$(if ($components) { $components } else { '  (none recorded yet)' })

Last install.log lines
----------------------
$(if ($tail) { $tail } else { '  (log empty or missing)' })

Related files
-------------
  $logPath
  $reportPath
  $(Join-Path $homeDir 'workspace-err.log')
  $(Join-Path $homeDir 'memos-install-err.log')
  $(Join-Path $homeDir 'gateway-install.log')
  $(Join-Path $homeDir 'gateway-install-err.log')
  $(Join-Path $homeDir 'gateway-start.log')
  $(Join-Path $homeDir 'gateway-start-err.log')

Re-run: installer\dist\HermesLauncher.exe
"@

    try {
        $utf8 = New-Object System.Text.UTF8Encoding $false
        [IO.File]::WriteAllText($reportPath, (Protect-SecretText $report), $utf8)
    } catch {
        Set-Content -Path $reportPath -Value (Protect-SecretText $report) -Encoding UTF8
    }

    $desktop = [Environment]::GetFolderPath('Desktop')
    if ($desktop) {
        try { Copy-Item -LiteralPath $reportPath -Destination (Join-Path $desktop 'Hermes-install-error.txt') -Force } catch { }
    }

    Write-Host ''
    Write-Host 'INSTALLATION FAILED' -ForegroundColor Red
    Write-Host $message -ForegroundColor Red
    Write-Host ''
    Write-Host ("Error report: {0}" -f $reportPath) -ForegroundColor Yellow
    Write-Host 'A copy was also saved to the Desktop as Hermes-install-error.txt' -ForegroundColor Yellow
    try { Start-Process notepad.exe $reportPath } catch { }
}

function Wait-OnInstallFailure {
    Write-Host ''
    Write-Host 'Press Enter to close this window...' -ForegroundColor Cyan
    try { [void][Console]::ReadLine() } catch { Start-Sleep -Seconds 30 }
}

trap {
    try { Write-InstallFailureReport -ErrorRecord $_ } catch {
        Write-Host ("INSTALLATION FAILED: {0}" -f $_) -ForegroundColor Red
    }
    # Invalidate completion marker on any failure.
    try { if ($Script:CompletionMarker) { Remove-Item -LiteralPath $Script:CompletionMarker -Force -ErrorAction SilentlyContinue } } catch { }
    if (-not $Script:NoPause) { Wait-OnInstallFailure }
    exit 1
}

$libPath = Join-Path $Script:Root 'lib\InstallComponents.ps1'
if (-not (Test-Path $libPath)) {
    throw "Missing installer library: $libPath"
}
. $libPath

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-Elevated([scriptblock]$Block) {
    if (Test-IsAdmin) { & $Block; return }
    Write-Host "Administrator privileges required for firewall rules." -ForegroundColor Yellow
    $scriptPath = Join-Path $env:TEMP ("hermes-elevated-{0}.ps1" -f (Get-Random))
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Block.ToString()))
    $launcher = @(
        "`$ErrorActionPreference = 'Stop'"
        "Invoke-Expression ([Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('$encoded')))"
    ) -join [Environment]::NewLine
    Set-Content -Path $scriptPath -Value $launcher -Encoding UTF8
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $scriptPath
    )
    Remove-Item $scriptPath -Force -ErrorAction SilentlyContinue
}

function Refresh-Path {
    $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = @($machinePath, $userPath) -join ';'
    if ((Test-Path $Script:HermesBin) -and ($env:Path -notlike "*$Script:HermesBin*")) {
        $env:Path = "$Script:HermesBin;$env:Path"
    }
    $npmGlobal = Join-Path $env:APPDATA 'npm'
    if ((Test-Path $npmGlobal) -and ($env:Path -notlike "*$npmGlobal*")) {
        $env:Path = "$npmGlobal;$env:Path"
    }
}

function Test-CommandExists([string]$Name) {
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Resolve-HermesCommand {
    # PATH may be stale in this process even after Refresh-Path; also probe the
    # managed bin dir where upstream install.ps1 stages the launchers.
    $cmd = Get-Command hermes -ErrorAction SilentlyContinue
    if ($cmd) { return [string]$cmd.Source }
    foreach ($candidate in @(
        (Join-Path $Script:HermesBin 'hermes.exe'),
        (Join-Path $Script:HermesBin 'hermes.cmd')
    )) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    return $null
}

function Test-WingetAvailable {
    return [bool](Get-Command winget -ErrorAction SilentlyContinue)
}

function Ensure-WingetPackage([string]$Id, [string]$Label) {
    if (Test-CommandExists $Label) {
        Write-Host "  $Label already installed"
        return
    }
    if (-not (Test-WingetAvailable)) {
        Write-Host "  winget not found. Install $Label manually:" -ForegroundColor Yellow
        Write-Host "    winget install --id $Id -e --accept-package-agreements --accept-source-agreements" -ForegroundColor Yellow
        throw "Failed to install $Label (winget missing). Install manually and re-run."
    }
    Write-Host "  Installing $Label via winget..."
    winget install --id $Id -e --accept-package-agreements --accept-source-agreements
    Refresh-Path
    if (-not (Test-CommandExists $Label)) {
        throw "Failed to install $Label. Install manually and re-run."
    }
}

function Ensure-Python {
    if (Test-CommandExists 'python') {
        try {
            Write-Host ("  python: {0}" -f (python --version 2>&1))
        } catch {
            Write-Host '  python already installed'
        }
        return
    }
    Write-Host '  Installing Python via winget...'
    if (Test-WingetAvailable) {
        try {
            winget install --id Python.Python.3.12 -e --accept-package-agreements --accept-source-agreements | Out-Null
            Refresh-Path
        } catch {
            Write-Host "  winget Python failed: $_" -ForegroundColor Yellow
        }
    } else {
        Write-Host '  winget not found. Install: winget install Python.Python.3.12' -ForegroundColor Yellow
    }
    if (-not (Test-CommandExists 'python')) {
        Write-Host '  Python not detected yet; hermes-agent installer may install it.' -ForegroundColor Yellow
    } else {
        Write-Host ("  python: {0}" -f (python --version 2>&1))
    }
}

function Ensure-Pnpm {
    if (Test-CommandExists 'pnpm') {
        Write-Host ("  pnpm already installed: {0}" -f (pnpm -v))
        return
    }
    Write-Host '  Installing pnpm...'
    try {
        corepack enable 2>$null
        corepack prepare pnpm@latest --activate
    } catch {
        npm install -g pnpm
    }
    Refresh-Path
    if (-not (Test-CommandExists 'pnpm')) {
        throw 'pnpm install failed'
    }
}

function New-HexSecret([int]$Bytes = 24) {
    $buffer = New-Object byte[] $Bytes
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buffer)
    return ([BitConverter]::ToString($buffer) -replace '-', '').ToLower()
}

function Merge-DotEnv([string]$Path, [hashtable]$Values, [string[]]$PreserveKeys = @()) {
    $lines = @()
    try {
        if (Test-Path $Path) { $lines = Get-Content $Path -ErrorAction Stop }
    } catch {
        throw "Cannot read .env (locked or no permission): $Path - $($_.Exception.Message)"
    }
    $map = @{}
    foreach ($line in $lines) {
        if ($line -match '^\s*#' -or $line -notmatch '=') { continue }
        $parts = $line -split '=', 2
        if ([string]::IsNullOrWhiteSpace($parts[0])) { continue }
        $map[$parts[0].Trim()] = $parts[1]
    }
    foreach ($key in $Values.Keys) {
        if ($PreserveKeys -contains $key -and $map.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace($map[$key])) {
            continue
        }
        $map[$key] = [string]$Values[$key]
    }
    $out = foreach ($key in ($map.Keys | Sort-Object)) { "$key=$($map[$key])" }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    $tmpPath = "$Path.tmp"
    $backupPath = "$Path.bak"
    try {
        # NB: do NOT pass $null as the backup arg of [IO.File]::Replace here -
        # PowerShell coerces $null to an empty string and .NET then throws
        # "path has invalid form". Use a real backup path and clean it up.
        if (Test-Path $backupPath) { Remove-Item $backupPath -Force -ErrorAction Stop }
        [IO.File]::WriteAllLines($tmpPath, @($out), $utf8)
        if (Test-Path $Path) {
            [IO.File]::Replace($tmpPath, $Path, $backupPath)
            if (Test-Path $backupPath) { Remove-Item $backupPath -Force -ErrorAction SilentlyContinue }
        } else {
            [IO.File]::Move($tmpPath, $Path)
        }
    } catch {
        try { if (Test-Path $tmpPath) { Remove-Item $tmpPath -Force } } catch { }
        throw "Cannot write .env (locked or no permission): $Path - $($_.Exception.Message)"
    }
    return $map
}

function Read-DotEnvMap([string]$Path) {
    $map = @{}
    if (-not (Test-Path $Path)) { return $map }
    foreach ($line in (Get-Content $Path -ErrorAction SilentlyContinue)) {
        if ($line -match '^\s*#' -or $line -notmatch '=') { continue }
        $parts = $line -split '=', 2
        $map[$parts[0].Trim()] = $parts[1].Trim()
    }
    return $map
}

function Write-Utf8NoBom([string]$Path, [string]$Content) {
    $unix = ($Content -replace "`r`n", "`n" -replace "`r", "`n")
    if (-not $unix.EndsWith("`n")) { $unix += "`n" }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $unix, $utf8NoBom)
}

function Get-CachedOrDownloadScript {
    param(
        [string]$Url,
        [string]$DestPath,
        [int]$MinBytes = 512,
        [string]$SanityPattern = 'hermes',
        [string]$ExpectedSha256 = ''
    )
    # Optional integrity pin, also honored via env var HERMES_PIN_SHA256_<TAG>
    # where TAG = uppercase alphanumeric file base name.
    $tag = ([IO.Path]::GetFileNameWithoutExtension($DestPath) -replace '[^A-Za-z0-9]', '').ToUpperInvariant()
    $envPin = [Environment]::GetEnvironmentVariable("HERMES_PIN_SHA256_$tag")
    if (-not $ExpectedSha256 -and $envPin) { $ExpectedSha256 = $envPin }

    function Test-ScriptIntegrity([string]$Path) {
        $fi = Get-Item $Path
        if ($fi.Length -lt $MinBytes) { throw "Downloaded script looks truncated ($($fi.Length) bytes)" }
        if (-not (Select-String -Path $Path -Pattern $SanityPattern -Quiet)) {
            throw 'Downloaded script failed content sanity check'
        }
        if ($ExpectedSha256) {
            $hash = (Get-FileHash -Path $Path -Algorithm SHA256).Hash
            if ($hash -ne $ExpectedSha256.ToUpperInvariant()) {
                throw "Downloaded script SHA256 mismatch (got $hash)"
            }
        }
    }

    if (Test-Path $DestPath) {
        try {
            Test-ScriptIntegrity $DestPath
            Write-Host ("  Using cached script: {0}" -f $DestPath)
            return $DestPath
        } catch {
            Write-Host '  Cached script failed integrity - re-downloading' -ForegroundColor Yellow
            Remove-Item $DestPath -Force -ErrorAction SilentlyContinue
        }
    }
    if (-not $ExpectedSha256) {
        Write-Host '  Note: no SHA256 pin configured; content sanity check only (TOFU).' -ForegroundColor DarkGray
    }
    $lastErr = $null
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        try {
            if ($attempt -gt 1) {
                Write-Host '  Retrying download...' -ForegroundColor Yellow
                Start-Sleep -Seconds 2
            }
            Invoke-WebRequest -Uri $Url -UseBasicParsing -OutFile $DestPath
            Test-ScriptIntegrity $DestPath
            return $DestPath
        } catch {
            $lastErr = $_
        }
    }
    throw $lastErr
}

function ConvertTo-Base64Url([byte[]]$Bytes) {
    $b64 = [Convert]::ToBase64String($Bytes)
    return $b64.TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Get-LanIp {
    $cfg = Get-NetIPConfiguration |
        Where-Object {
            $_.IPv4DefaultGateway -and
            $_.NetAdapter.Status -eq 'Up' -and
            $_.IPv4Address.IPAddress -notlike '169.254.*'
        } |
        Select-Object -First 1
    if ($cfg) { return $cfg.IPv4Address.IPAddress }
    return '127.0.0.1'
}

function Wait-HttpHealthy([string]$Url, [int]$TimeoutSec = 120) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        try {
            $resp = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
            if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 300) { return $true }
        } catch { Start-Sleep -Seconds 2 }
    }
    return $false
}

function Invoke-HermesGatewayCommand {
    param(
        [ValidateSet('install', 'start')]
        [string]$Action,
        [int]$TimeoutSec = 120
    )

    $hermesPath = Resolve-HermesCommand
    if (-not $hermesPath) {
        return [pscustomobject]@{
            Succeeded = $false
            TimedOut  = $false
            ExitCode  = -1
            OutLog    = ''
            ErrLog    = ''
            Message   = 'hermes CLI not found'
        }
    }

    $outLog = Join-Path $Script:HermesHome ("gateway-{0}.log" -f $Action)
    $errLog = Join-Path $Script:HermesHome ("gateway-{0}-err.log" -f $Action)
    Write-Host ("  hermes gateway {0}..." -f $Action)

    # The hermes CLI can prompt interactively (e.g. "Messaging platform token detected!").
    # With a hidden process there is no console input, so it would hang forever.
    # Fixes applied here:
    #  1) stdin closed via `echo. |` (one empty line = default answer, then EOF)
    #  2) gateway install choices pinned via flags + env so no prompt path is reached
    #  3) PYTHONIOENCODING/PYTHONUTF8: gateway_windows.py prints unicode glyphs;
    #     on a localized (e.g. Russian) codepage redirected stdout would crash with
    #     UnicodeEncodeError -> silent exit 1
    $prevCI = $env:CI
    $prevNonInteractive = $env:HERMES_NONINTERACTIVE
    $prevNoInput = $env:NO_INPUT
    $prevPyEnc = $env:PYTHONIOENCODING
    $prevPyUtf8 = $env:PYTHONUTF8
    $prevGwStartNow = $env:HERMES_GATEWAY_INSTALL_START_NOW
    $prevGwOnLogin = $env:HERMES_GATEWAY_INSTALL_START_ON_LOGIN
    $env:CI = 'true'
    $env:HERMES_NONINTERACTIVE = '1'
    $env:NO_INPUT = '1'
    $env:PYTHONIOENCODING = 'utf-8'
    $env:PYTHONUTF8 = '1'
    $env:HERMES_GATEWAY_INSTALL_START_NOW = '1'
    $env:HERMES_GATEWAY_INSTALL_START_ON_LOGIN = '1'

    try {
        # Use cmd.exe wrapper to properly close stdin (echo. | closes the pipe).
        # Exit code is ALSO written to a file by cmd itself: Start-Process with
        # redirected streams can lose ExitCode (returns $null) when the child
        # spawns a detached gateway process and exits before .NET finalizes it.
        $extraArgs = if ($Action -eq 'install') { ' --start-now --start-on-login' } else { '' }
        $codeFile = Join-Path $env:TEMP ("hermes-gw-{0}-{1}.code" -f $Action, [guid]::NewGuid().ToString('N'))
        $cmdWrapper = "echo. | `"$(Get-NativePath $hermesPath)`" gateway $Action$extraArgs & echo !ERRORLEVEL! > `"$codeFile`""
        $proc = Start-Process -FilePath 'cmd.exe' `
            -ArgumentList @('/v:on', '/c', $cmdWrapper) `
            -PassThru -NoNewWindow -Wait:$false `
            -RedirectStandardOutput $outLog `
            -RedirectStandardError $errLog

        $finished = $proc.WaitForExit($TimeoutSec * 1000)
        if (-not $finished) {
            try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { }
            return [pscustomobject]@{
                Succeeded = $false
                TimedOut  = $true
                ExitCode  = 124
                OutLog    = $outLog
                ErrLog    = $errLog
                Message   = ("Timed out after {0}s" -f $TimeoutSec)
            }
        }

        # Flush async stream readers so logs are complete and process info is stable.
        try { $proc.WaitForExit() } catch { }

        $code = $null
        $codeSource = 'process'
        try {
            if (Test-Path $codeFile) {
                $codeText = (Get-Content $codeFile -Raw -ErrorAction Stop).Trim()
                if ($codeText -match '^\d+$') { $code = [int]$codeText; $codeSource = 'codefile' }
            }
        } catch { }
        try { Remove-Item $codeFile -Force -ErrorAction SilentlyContinue } catch { }
        if ($null -eq $code) {
            try { $proc.Refresh(); $code = $proc.ExitCode } catch { $code = $null }
        }
        if ($null -eq $code) {
            # Last resort: judge by the logs. Upstream prints a traceback on failure
            # and 'Next steps' / 'Gateway started' on success (incl. Startup-folder fallback).
            $outText = ''
            $errText = ''
            try { $outText = Get-Content $outLog -Raw -Encoding UTF8 -ErrorAction Stop } catch { }
            try { $errText = Get-Content $errLog -Raw -Encoding UTF8 -ErrorAction Stop } catch { }
            $codeSource = 'logs'
            if ($errText -match 'Traceback' -or $errText -match 'RuntimeError') {
                $code = 1
            } elseif ($outText -match 'Next steps' -or $outText -match 'Gateway started') {
                $code = 0
            } else {
                $code = 1
            }
        }
        $codeText = if ($codeSource -eq 'logs') { "$code (from logs)" } else { [string]$code }
        return [pscustomobject]@{
            Succeeded = ($code -eq 0)
            TimedOut  = $false
            ExitCode  = $codeText
            OutLog    = $outLog
            ErrLog    = $errLog
            Message   = ("Exit code {0}" -f $codeText)
        }
    } finally {
        if ($null -ne $prevCI) { $env:CI = $prevCI } else { Remove-Item Env:CI -ErrorAction SilentlyContinue }
        if ($null -ne $prevNonInteractive) { $env:HERMES_NONINTERACTIVE = $prevNonInteractive } else { Remove-Item Env:HERMES_NONINTERACTIVE -ErrorAction SilentlyContinue }
        if ($null -ne $prevNoInput) { $env:NO_INPUT = $prevNoInput } else { Remove-Item Env:NO_INPUT -ErrorAction SilentlyContinue }
        if ($null -ne $prevPyEnc) { $env:PYTHONIOENCODING = $prevPyEnc } else { Remove-Item Env:PYTHONIOENCODING -ErrorAction SilentlyContinue }
        if ($null -ne $prevPyUtf8) { $env:PYTHONUTF8 = $prevPyUtf8 } else { Remove-Item Env:PYTHONUTF8 -ErrorAction SilentlyContinue }
        if ($null -ne $prevGwStartNow) { $env:HERMES_GATEWAY_INSTALL_START_NOW = $prevGwStartNow } else { Remove-Item Env:HERMES_GATEWAY_INSTALL_START_NOW -ErrorAction SilentlyContinue }
        if ($null -ne $prevGwOnLogin) { $env:HERMES_GATEWAY_INSTALL_START_ON_LOGIN = $prevGwOnLogin } else { Remove-Item Env:HERMES_GATEWAY_INSTALL_START_ON_LOGIN -ErrorAction SilentlyContinue }
    }
}

function Ensure-FirewallRules {
    $added = 0
    $existing = 0
    $resultFile = Join-Path $env:TEMP ("hermes-fw-result-{0}.txt" -f [guid]::NewGuid().ToString('N'))
    $elevScript = Join-Path $env:TEMP ("hermes-fw-{0}.ps1" -f [guid]::NewGuid().ToString('N'))
    $safeResult = $resultFile.Replace("'", "''")
    $body = @"
`$ErrorActionPreference = 'Stop'
`$rules = @(
    @{ Name = 'Hermes Workspace (TCP 3000)'; Port = 3000 },
    @{ Name = 'Hermes Gateway (TCP 8642)'; Port = 8642 },
    @{ Name = 'Hermes Agent Dashboard (TCP 9119)'; Port = 9119 }
)
`$added = 0; `$existing = 0
foreach (`$rule in `$rules) {
    if (Get-NetFirewallRule -DisplayName `$rule.Name -ErrorAction SilentlyContinue) {
        Write-Host ("  Firewall rule exists: {0}" -f `$rule.Name)
        `$existing++
        continue
    }
    New-NetFirewallRule -DisplayName `$rule.Name -Direction Inbound -Action Allow ``
        -Protocol TCP -LocalPort `$rule.Port -Profile Private,Domain | Out-Null
    Write-Host ("  Added firewall rule: {0}" -f `$rule.Name)
    `$added++
}
Set-Content -Path '$safeResult' -Value ("added=`$added;existing=`$existing") -Encoding ASCII
"@
    Set-Content -Path $elevScript -Value $body -Encoding UTF8
    try {
        if (Test-IsAdmin) {
            & $elevScript
        } else {
            Write-Host 'Administrator privileges required for firewall rules.' -ForegroundColor Yellow
            Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList @(
                '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $elevScript
            ) -ErrorAction Stop
        }
    } catch {
        # UAC declined or elevation failed: firewall stays unconfigured (soft fail, user-visible).
        Write-Host ("  Firewall elevation cancelled or failed: {0}" -f $_.Exception.Message) -ForegroundColor Yellow
        Remove-Item $elevScript -Force -ErrorAction SilentlyContinue
        return [pscustomobject]@{ Added = 0; Existing = 0; ElevationDenied = $true }
    }
    Remove-Item $elevScript -Force -ErrorAction SilentlyContinue

    if (Test-Path $resultFile) {
        $raw = (Get-Content $resultFile -Raw).Trim()
        Remove-Item $resultFile -Force -ErrorAction SilentlyContinue
        if ($raw -match 'added=(\d+);existing=(\d+)') {
            $added = [int]$Matches[1]
            $existing = [int]$Matches[2]
        }
    }
    return [pscustomobject]@{ Added = $added; Existing = $existing; ElevationDenied = $false }
}

function Test-WorkspacePortHealthy {
    return (Wait-HttpHealthy "http://127.0.0.1:$Script:WorkspacePort/api/healthcheck" 3)
}

function Install-WorkspaceNative {
    Write-Host ("  Workspace dir: {0}" -f $Script:WorkspaceDir)
    $gitDir = Join-Path $Script:WorkspaceDir '.git'
    $repoUrl = 'https://github.com/outsourc-e/hermes-workspace.git'

    if (-not (Test-Path $gitDir)) {
        if (Test-Path $Script:WorkspaceDir) {
            $children = @(Get-ChildItem -LiteralPath $Script:WorkspaceDir -Force -ErrorAction SilentlyContinue)
            if ($children.Count -gt 0) {
                $msg = @"
Workspace folder exists and is not empty, but is not a git clone:
  $($Script:WorkspaceDir)

Choose an empty folder or an existing hermes-workspace clone, then re-run.
Refusing to delete user data.
"@
                Write-Host $msg -ForegroundColor Red
                Add-Content -Path $Script:LogFile -Value ("[{0}] WORKSPACE_REFUSED: non-empty non-git folder {1}" -f (Get-Date -Format o), $Script:WorkspaceDir) -ErrorAction SilentlyContinue
                throw "Workspace folder is non-empty and not a git repository: $($Script:WorkspaceDir)"
            }
        } else {
            New-Item -ItemType Directory -Path $Script:WorkspaceDir -Force | Out-Null
        }
        Write-Host ("  Cloning {0} ..." -f $repoUrl)
        $cloneOk = $false
        for ($attempt = 1; $attempt -le 2; $attempt++) {
            if ($attempt -gt 1) {
                Write-Host '  Retrying git clone...' -ForegroundColor Yellow
                Start-Sleep -Seconds 3
            }
            git clone $repoUrl $Script:WorkspaceDir
            if ($LASTEXITCODE -eq 0) { $cloneOk = $true; break }
        }
        if (-not $cloneOk) { throw "git clone failed after 2 attempts (exit $LASTEXITCODE)" }
    } else {
        Push-Location $Script:WorkspaceDir
        try {
            $remote = (git remote get-url origin 2>$null)
            Write-Host ("  Existing clone (origin={0})" -f $(if ($remote) { $remote } else { 'unknown' }))
            if ($remote -and $remote -notmatch 'hermes-workspace') {
                Write-Host '  Warning: origin is not hermes-workspace; skipping git pull' -ForegroundColor Yellow
            } else {
                try { git pull --ff-only } catch { Write-Host '  git pull skipped' }
            }
        } finally {
            Pop-Location
        }
    }
    Push-Location $Script:WorkspaceDir
    try {
        Write-Host '  pnpm install (this can take a few minutes)...'
        $pnpmOk = $false
        for ($attempt = 1; $attempt -le 2; $attempt++) {
            if ($attempt -gt 1) {
                Write-Host '  Retrying pnpm install...' -ForegroundColor Yellow
                Start-Sleep -Seconds 3
            }
            pnpm install
            if ($LASTEXITCODE -eq 0) { $pnpmOk = $true; break }
        }
        if (-not $pnpmOk) { throw "pnpm install failed after 2 attempts (exit $LASTEXITCODE)" }
    } finally {
        Pop-Location
    }
}

function Start-WorkspaceDevIfNeeded {
    if (Test-WorkspacePortHealthy) {
        Write-Host '  Workspace already healthy on :3000 - skipping start' -ForegroundColor Green
        return $false
    }

    $task = Get-ScheduledTask -TaskName 'HermesWorkspace' -ErrorAction SilentlyContinue
    if ($task) {
        try {
            Start-ScheduledTask -TaskName 'HermesWorkspace' -ErrorAction Stop
            Write-Host '  Started scheduled task HermesWorkspace'
            Start-Sleep -Seconds 5
            if (Test-WorkspacePortHealthy) { return $true }
            Write-Host '  Task started but port not ready yet - trying direct start' -ForegroundColor Yellow
        } catch {
            Write-Host "  Scheduled task start failed: $_" -ForegroundColor Yellow
        }
    }

    Start-WorkspaceDev
    return $true
}

function New-StartupCmd([string]$Name, [string]$Body) {
    $startup = [Environment]::GetFolderPath('Startup')
    if (-not $startup) {
        $startup = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'
    }
    if (-not (Test-Path $startup)) {
        New-Item -ItemType Directory -Path $startup -Force | Out-Null
    }
    $path = Join-Path $startup $Name
    Set-Content -Path $path -Value $Body -Encoding ASCII
    Write-Host ("  Startup shortcut: {0}" -f $path)
}

function Try-RegisterScheduledTask([string]$TaskName, [object]$Action, [object]$Trigger, [object]$Settings) {
    try {
        Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger -Settings $Settings `
            -User $env:USERNAME -RunLevel Limited -Force | Out-Null
        Write-Host ("  Registered scheduled task: {0}" -f $TaskName)
        return $true
    } catch {
        Write-Host ("  Scheduled task '{0}' skipped (Access denied or policy). Using Startup folder." -f $TaskName) -ForegroundColor Yellow
        return $false
    }
}

function Register-NativeTasks([string]$ApiKey, [string]$Password) {
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero)

    $hermesExe = Get-Command hermes -ErrorAction SilentlyContinue
    $hermesPath = Get-NativePath $hermesExe
    if ($hermesPath) {
        $dashboardAction = New-ScheduledTaskAction -Execute $hermesPath -Argument 'dashboard start'
        $ok = Try-RegisterScheduledTask -TaskName 'HermesDashboard' -Action $dashboardAction -Trigger $trigger -Settings $settings
        if (-not $ok) {
            New-StartupCmd 'HermesDashboard.cmd' ("@echo off`r`n""{0}"" dashboard start`r`n" -f $hermesPath)
        }
    }

    $pnpmCmd = (Get-Command pnpm.cmd -ErrorAction SilentlyContinue)
    if (-not $pnpmCmd) { $pnpmCmd = Get-Command pnpm -ErrorAction SilentlyContinue }
    $pnpmPath = Get-NativePath $pnpmCmd
    if ($pnpmPath) {
        $arg = '/c cd /d "{0}" && "{1}" dev' -f $Script:WorkspaceDir, $pnpmPath
        $action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument $arg -WorkingDirectory $Script:WorkspaceDir
        $ok = Try-RegisterScheduledTask -TaskName 'HermesWorkspace' -Action $action -Trigger $trigger -Settings $settings
        if (-not $ok) {
            New-StartupCmd 'HermesWorkspace.cmd' (
                "@echo off`r`ncd /d ""{0}""`r`n""{1}"" dev`r`n" -f $Script:WorkspaceDir, $pnpmPath
            )
        }
    }
}

function Start-WorkspaceDev {
    $pnpmCmd = (Get-Command pnpm.cmd -ErrorAction SilentlyContinue)
    if (-not $pnpmCmd) { $pnpmCmd = Get-Command pnpm -ErrorAction SilentlyContinue }
    $pnpmPath = Get-NativePath $pnpmCmd
    if (-not $pnpmPath) { throw 'pnpm not found' }

    $logOut = Join-Path $Script:HermesHome 'workspace-out.log'
    $logErr = Join-Path $Script:HermesHome 'workspace-err.log'
    Write-Host ("  Starting workspace: {0} dev" -f $pnpmPath)
    Start-Process -FilePath $pnpmPath -ArgumentList 'dev' `
        -WorkingDirectory $Script:WorkspaceDir `
        -WindowStyle Hidden `
        -RedirectStandardOutput $logOut `
        -RedirectStandardError $logErr
}

function New-ConnectHtml(
    [string]$Path,
    [string]$DeepLink,
    [string]$Code,
    [string]$ConnectIp,
    [string]$LanIp,
    [string]$TailscaleIp,
    [bool]$OllamaOk
) {
    $safeLink = $DeepLink.Replace("'", "&#39;")
    $safeCode = $Code.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
    $logoHtml = if (Test-Path (Join-Path $Script:HermesHome 'hermes-logo.png')) {
        '<img class="logo" src="hermes-logo.png" alt="Hermes Workspace logo" />'
    } else {
        ''
    }
    $tsLine = if ($TailscaleIp) {
        "<p>Tailscale IP: <strong>$TailscaleIp</strong> - use this from the phone (same tailnet).</p>"
    } else {
        '<p>Tailscale IP: <em>not available - using LAN (same Wi-Fi only)</em></p>'
    }
    $ollamaLine = if ($OllamaOk) {
        '<p>Ollama is installed locally for Hermes on this PC (<code>http://127.0.0.1:11434</code>). The phone does not talk to Ollama directly.</p>'
    } else {
        '<p>Ollama was not detected - install it later if you want local models for Hermes.</p>'
    }
    $html = @"
<!DOCTYPE html>
<html lang="en"><head>
<meta charset="utf-8" /><meta name="viewport" content="width=device-width, initial-scale=1" />
<title>Hermes Connect</title>
<style>
body{font-family:system-ui,sans-serif;background:#060914;color:#e6eaf2;margin:0;padding:2rem}
main{max-width:560px;margin:0 auto;text-align:center;background:#11182a;border:1px solid #24304a;border-radius:14px;padding:24px}
p{color:#9aa7b5;line-height:1.5}
h1{margin-top:0}
.logo{max-width:260px;width:100%;height:auto;display:block;margin:0 auto 14px auto;border-radius:8px}
#qrcode{margin:1.5rem auto;display:inline-block;background:#fff;padding:12px;border-radius:12px}
textarea{width:100%;box-sizing:border-box;background:#151b24;color:#d7e2ee;border:1px solid #2a3544;border-radius:8px;padding:12px;font-size:12px;word-break:break-all;min-height:88px;margin-top:1rem}
a.button{display:inline-block;margin-top:1rem;padding:12px 18px;background:#6366f1;color:#fff;text-decoration:none;border-radius:8px}
.box{text-align:left;background:#151b24;border:1px solid #2a3544;border-radius:8px;padding:12px;margin-top:1.25rem}
</style>
<script src="https://cdn.jsdelivr.net/npm/qrcodejs@1.0.0/qrcode.min.js"></script>
</head><body><main>
$logoHtml
<h1>Hermes Workspace - Quick Connect</h1>
<p>Install <strong>Tailscale</strong> on the phone, join the same tailnet as this PC, then scan the QR in the Hermes Android app.</p>
<div id="qrcode"></div>
<p><a class="button" href="$safeLink">Open in app</a></p>
<textarea readonly>$safeCode</textarea>
$tsLine
<p>LAN IP (Wi-Fi fallback): <strong>$LanIp</strong></p>
<p>Connect host in QR: <strong>$ConnectIp</strong></p>
<p>Gateway :8642 / Workspace :3000 / Agent dashboard :9119</p>
<div class="box">
  <p><strong>Ollama + Hermes</strong></p>
  $ollamaLine
</div>
</main>
<script>
(function(){var link='$safeLink';
if(typeof QRCode!=='undefined'){new QRCode(document.getElementById('qrcode'),{text:link,width:220,height:220});}
else{document.getElementById('qrcode').innerHTML='<p style="color:#666;font-size:12px">QR library failed to load. Use the connect code below.</p>';}
})();
</script>
</body></html>
"@
    Write-Utf8NoBom -Path $Path -Content $html
}

# ---- main ----
try {
if (Test-Path $Script:ErrorReportPath) {
    Remove-Item -LiteralPath $Script:ErrorReportPath -Force -ErrorAction SilentlyContinue
}
'' | Set-Content $Script:LogFile -Encoding UTF8

Write-Host 'Hermes Workspace Windows Installer' -ForegroundColor Green
Write-Host ("VERSION: {0}" -f $Script:InstallerVersion) -ForegroundColor Green
Write-Host ("Install: {0}" -f $Script:HermesHome) -ForegroundColor DarkGray
Write-Host ("Workspace: {0}" -f $Script:WorkspaceDir) -ForegroundColor DarkGray
Write-Host ("Tailscale={0} Ollama={1} MemOS={2} Obsidian={3} Skills={4}" -f `
    $Script:InstallTailscale, $Script:InstallOllama, $Script:MemOSMode, $Script:InstallObsidian, $Script:InstallObsidianSkills) -ForegroundColor DarkGray
Write-Host ("Firewall={0} Start={1}" -f $Script:ConfigureFirewall, $Script:StartServices) -ForegroundColor DarkGray
Write-Host 'Mode:    Native Windows (no sudo / no WSL required)' -ForegroundColor DarkGray
Write-Host '==================================' -ForegroundColor Green

Write-Step 'Checking prerequisites'
Ensure-WingetPackage 'OpenJS.NodeJS.LTS' 'node'
Ensure-WingetPackage 'Git.Git' 'git'
Ensure-Python
Refresh-Path
Write-Host ("  node: {0}" -f (node -v))
Ensure-Pnpm

Write-Step 'Tailscale (phone connect via 100.x IP)'
$tailscaleIp = Ensure-TailscaleComponent -SkipInstall:(-not $Script:InstallTailscale)

Write-Step 'Ollama (local models for Hermes on this PC)'
$ollamaOk = Ensure-OllamaComponent -SkipInstall:(-not $Script:InstallOllama)

Write-Step 'Resolving preferred network host (Tailscale first)'
$tailscaleState = Detect-Tailscale
if (-not $tailscaleIp -and $tailscaleState.Ip) {
    $tailscaleIp = $tailscaleState.Ip
    Write-Host ("  Reusing existing Tailscale IP: {0}" -f $tailscaleIp) -ForegroundColor Green
}
$lanIp = Get-LanIp
$Script:ConnectHost = if ($tailscaleIp) { $tailscaleIp } else { $lanIp }
Write-Host ("  Preferred connect host: {0}" -f $Script:ConnectHost) -ForegroundColor Green
if ($tailscaleIp) {
    Write-Host '  Tailscale is active: gateway/workspace will be reachable over tailnet.' -ForegroundColor Green
} else {
    Write-Host '  Tailscale not active: using LAN fallback host.' -ForegroundColor Yellow
}

Write-Step 'Installing hermes-agent'
$HermesAgentInstallUrl = 'https://raw.githubusercontent.com/NousResearch/hermes-agent/main/scripts/install.ps1'
$HermesAgentScriptPath = Join-Path $Script:HermesHome 'hermes-agent-install.ps1'
try {
    $null = Get-CachedOrDownloadScript -Url $HermesAgentInstallUrl -DestPath $HermesAgentScriptPath
    $scriptInfo = Get-Item $HermesAgentScriptPath
    Write-Host "  Saved install script: $HermesAgentScriptPath ($($scriptInfo.Length) bytes)"
    # Upstream install.ps1 swallows stage errors in interactive mode and exits 0,
    # so exit code alone is NOT proof of success. Capture output, then verify the
    # hermes launcher was actually staged under $HermesHome\bin.
    $agentLog = Join-Path $Script:HermesHome 'hermes-agent-install.log'
    & $HermesAgentScriptPath -SkipSetup 2>&1 | Tee-Object -FilePath $agentLog
    $agentExit = $LASTEXITCODE
    Refresh-Path
    $launcherStaged = [bool](Resolve-HermesCommand)
    if ($agentExit -ne 0 -or -not $launcherStaged) {
        $tail = ''
        if (Test-Path $agentLog) {
            try { $tail = (Get-Content $agentLog -Tail 30 -ErrorAction SilentlyContinue) -join [Environment]::NewLine } catch { }
        }
        New-ComponentResult -Name 'hermes_agent' -Outcome $Script:OutcomeFailedHard `
            -Message ("exit={0}; launcherStaged={1}" -f $agentExit, $launcherStaged) `
            -Details @{ agentLog = $agentLog }
        throw "hermes-agent install failed (exit $agentExit, hermes command staged: $launcherStaged). See $agentLog`nLast output:`n$tail"
    }
    New-ComponentResult -Name 'hermes_agent' -Outcome $Script:OutcomeInstalled -Message 'CLI installed'
} catch {
    if (-not ($Script:ComponentResults.Keys -contains 'hermes_agent')) {
        New-ComponentResult -Name 'hermes_agent' -Outcome $Script:OutcomeFailedHard -Message $_.Exception.Message
    }
    throw "hermes-agent install failed: $_"
}
Refresh-Path

if ($Script:MemOSMode -ne 'skip') {
    Write-Step ("MemOS memory plugin (mode: {0})" -f $Script:MemOSMode)
    $null = Ensure-MemOSComponent -SkipInstall:$false -Mode $Script:MemOSMode `
        -ProviderBaseUrl $Script:MemOSProviderUrl `
        -ProviderApiKey $Script:MemOSProviderKey `
        -ProviderModel $Script:MemOSProviderModel `
        -OllamaAvailable:([bool]$ollamaOk)
} else {
    Write-Step 'Skipping MemOS (deselected in wizard)'
    New-ComponentResult -Name 'memos' -Outcome $Script:OutcomeSkipped -Message 'Not selected'
}

if ($Script:InstallObsidian) {
    Write-Step 'Obsidian (optional vault editor)'
    $null = Ensure-ObsidianComponent
} else {
    New-ComponentResult -Name 'obsidian' -Outcome $Script:OutcomeSkipped -Message 'Not selected'
}

if ($Script:InstallObsidianSkills) {
    Write-Step 'Obsidian Skills for Hermes Agent'
    $null = Ensure-ObsidianSkillsComponent
} else {
    New-ComponentResult -Name 'obsidian_skills' -Outcome $Script:OutcomeSkipped -Message 'Not selected'
}

Write-Step 'Generating secrets'
$existingHermesEnv = Read-DotEnvMap $Script:HermesEnv
$apiKey = if ($existingHermesEnv['API_SERVER_KEY']) { $existingHermesEnv['API_SERVER_KEY'] } else { New-HexSecret }
$hermesPassword = if ($existingHermesEnv['HERMES_PASSWORD']) { $existingHermesEnv['HERMES_PASSWORD'] } else { New-HexSecret }
if ($existingHermesEnv['API_SERVER_KEY']) {
    Write-Host '  Reusing existing API_SERVER_KEY (re-run safe)' -ForegroundColor Green
}
if ($existingHermesEnv['HERMES_PASSWORD']) {
    Write-Host '  Reusing existing HERMES_PASSWORD (re-run safe)' -ForegroundColor Green
}
$envMap = Merge-DotEnv $Script:HermesEnv @{
    API_SERVER_ENABLED = 'true'
    API_SERVER_KEY     = $apiKey
    API_SERVER_HOST    = '0.0.0.0'
    API_SERVER_PORT    = [string]$Script:GatewayPort
    HERMES_PASSWORD    = $hermesPassword
    OLLAMA_HOST        = 'http://127.0.0.1:11434'
} -PreserveKeys @('API_SERVER_KEY', 'HERMES_PASSWORD')
$apiKey = if ($envMap['API_SERVER_KEY']) { $envMap['API_SERVER_KEY'] } else { $apiKey }
$hermesPassword = if ($envMap['HERMES_PASSWORD']) { $envMap['HERMES_PASSWORD'] } else { $hermesPassword }
Write-Host "  Wrote $Script:HermesEnv"

Write-Step 'Installing hermes-workspace (native Windows)'
Install-WorkspaceNative

$publicHost = if ($Script:ConnectHost) { $Script:ConnectHost } elseif ($tailscaleIp) { $tailscaleIp } else { $lanIp }
$publicHost = [string]$publicHost

$wsEnv = @"
HERMES_API_URL=http://127.0.0.1:$Script:GatewayPort
HERMES_DASHBOARD_URL=http://127.0.0.1:$Script:DashboardPort
HERMES_PUBLIC_API_URL=http://${publicHost}:$Script:GatewayPort
HERMES_PUBLIC_WORKSPACE_URL=http://${publicHost}:$Script:WorkspacePort
HERMES_PUBLIC_AGENT_DASHBOARD_URL=http://${publicHost}:$Script:DashboardPort
HERMES_TAILSCALE_IP=$(if ($tailscaleIp) { $tailscaleIp } else { '' })
HERMES_API_TOKEN=$apiKey
HERMES_PASSWORD=$hermesPassword
PORT=$Script:WorkspacePort
COOKIE_SECURE=0
HOST=0.0.0.0
"@
Write-Utf8NoBom (Join-Path $Script:WorkspaceDir '.env') $wsEnv
Write-Host ("  Wrote {0}\.env" -f $Script:WorkspaceDir)

if ($Script:ConfigureFirewall) {
    Write-Step 'Configuring Windows Firewall (private profile)'
    $fw = Ensure-FirewallRules
    $fwOutcome = if ($fw.Added -gt 0) { $Script:OutcomeInstalled } elseif ($fw.Existing -gt 0) { $Script:OutcomeAlreadyPresent } else { $Script:OutcomeFailedSoft }
    New-ComponentResult -Name 'firewall' -Outcome $fwOutcome -Message ("added={0} existing={1}" -f $fw.Added, $fw.Existing) `
        -Details @{ added = $fw.Added; existing = $fw.Existing }
} else {
    Write-Step 'Skipping firewall rules (deselected in wizard)'
    New-ComponentResult -Name 'firewall' -Outcome $Script:OutcomeSkipped -Message 'Not selected'
}

# Autostart registration is independent from immediate service start.
if ($Script:EnableAutoStart) {
    Write-Step 'Registering autostart (logon tasks / startup entries)'
    Register-NativeTasks -ApiKey $apiKey -Password $hermesPassword
} else {
    Write-Host '  Autostart registration disabled by setting.' -ForegroundColor DarkGray
}

if ($Script:StartServices) {
    Write-Step 'Starting hermes gateway + workspace'
    if (Resolve-HermesCommand) {
        $env:API_SERVER_ENABLED = 'true'
        $env:API_SERVER_HOST = '0.0.0.0'
        $env:API_SERVER_PORT = [string]$Script:GatewayPort

        $gatewayAlreadyUp = Wait-HttpHealthy "http://127.0.0.1:$Script:GatewayPort/health" 3
        $gwInstall = [pscustomobject]@{ Succeeded = $false; TimedOut = $false; ExitCode = 0; OutLog = ''; ErrLog = ''; Message = 'skipped' }
        if (-not $gatewayAlreadyUp) {
            $gwInstall = Invoke-HermesGatewayCommand -Action install -TimeoutSec 150
            # Gateway is a REQUIRED component: install failure aborts the install with nonzero exit.
            if ($gwInstall.TimedOut) {
                New-ComponentResult -Name 'gateway' -Outcome $Script:OutcomeFailedHard `
                    -Message 'install timed out' -Details @{ installTimedOut = $true }
                throw "Required component 'gateway' install timed out after 150s. See gateway install logs in $Script:HermesHome"
            } elseif (-not $gwInstall.Succeeded) {
                New-ComponentResult -Name 'gateway' -Outcome $Script:OutcomeFailedHard `
                    -Message ("install exit {0}" -f $gwInstall.ExitCode) `
                    -Details @{ installExit = $gwInstall.ExitCode; installErrLog = $gwInstall.ErrLog }
                $gwOutTail = ''
                $gwErrTail = ''
                try { if ($gwInstall.OutLog -and (Test-Path $gwInstall.OutLog)) { $gwOutTail = (Get-Content $gwInstall.OutLog -Tail 25 -Encoding UTF8 -ErrorAction Stop) -join "`n" } } catch { }
                try { if ($gwInstall.ErrLog -and (Test-Path $gwInstall.ErrLog)) { $gwErrTail = (Get-Content $gwInstall.ErrLog -Tail 25 -Encoding UTF8 -ErrorAction Stop) -join "`n" } } catch { }
                $gwDetail = "Required component 'gateway' install failed (exit $($gwInstall.ExitCode)). See: $($gwInstall.ErrLog)"
                if ($gwOutTail) { $gwDetail += "`n--- gateway install output (tail) ---`n$gwOutTail" }
                if ($gwErrTail) { $gwDetail += "`n--- gateway install stderr (tail) ---`n$gwErrTail" }
                throw $gwDetail
            }
        } else {
            Write-Host '  Gateway already healthy; skipping install.' -ForegroundColor Green
        }

        $gwStart = Invoke-HermesGatewayCommand -Action start -TimeoutSec 90
        $gatewayHealthy = Wait-HttpHealthy "http://127.0.0.1:$Script:GatewayPort/health" 8
        $gatewayOutcome = if ($gatewayHealthy) {
            if ($gatewayAlreadyUp) { $Script:OutcomeAlreadyPresent } else { $Script:OutcomeInstalled }
        } else {
            $Script:OutcomeFailedHard
        }
        New-ComponentResult -Name 'gateway' -Outcome $gatewayOutcome -Message ("install={0};start={1}" -f $gwInstall.Message, $gwStart.Message) `
            -Details @{
                tailscaleIp = $tailscaleIp
                connectHost = $Script:ConnectHost
                installExit = $gwInstall.ExitCode
                installTimedOut = $gwInstall.TimedOut
                installOutLog = $gwInstall.OutLog
                installErrLog = $gwInstall.ErrLog
                startExit = $gwStart.ExitCode
                startTimedOut = $gwStart.TimedOut
                startOutLog = $gwStart.OutLog
                startErrLog = $gwStart.ErrLog
            }
        if (-not $gatewayHealthy) {
            throw "Required component 'gateway' failed to start or pass health check (start exit $($gwStart.ExitCode)). See: $($gwStart.ErrLog)"
        }
    } else {
        New-ComponentResult -Name 'gateway' -Outcome $Script:OutcomeFailedHard -Message 'hermes CLI not on PATH'
        throw "Required component 'gateway': hermes CLI not found on PATH after install."
    }
    try { Start-ScheduledTask -TaskName 'HermesDashboard' -ErrorAction Stop } catch { }
    # Single start path for workspace: task if healthy, else direct Start-WorkspaceDev (never both).
    Start-WorkspaceDevIfNeeded | Out-Null

    Write-Step 'Waiting for services'
    $gatewayOk = Wait-HttpHealthy "http://127.0.0.1:$Script:GatewayPort/health" 90
    $workspaceOk = Wait-HttpHealthy "http://127.0.0.1:$Script:WorkspacePort/api/healthcheck" 180
    # Gateway and workspace are REQUIRED: health timeout aborts with nonzero exit, no INSTALLATION COMPLETE.
    if (-not $gatewayOk) {
        New-ComponentResult -Name 'gateway' -Outcome $Script:OutcomeFailedHard -Message 'health check timed out'
        throw "Required component 'gateway' health check timed out. See gateway logs in $Script:HermesHome"
    }
    if (-not $workspaceOk) {
        New-ComponentResult -Name 'workspace' -Outcome $Script:OutcomeFailedHard -Message 'health check timed out' `
            -Details @{ errLog = (Join-Path $Script:HermesHome 'workspace-err.log') }
        throw "Required component 'workspace' health check timed out - check: $(Join-Path $Script:HermesHome 'workspace-err.log')"
    }
} else {
    Write-Step 'Skipping service start (deselected in wizard)'
    Write-Host '  Services were not started. Autostart registration: '$(if ($Script:EnableAutoStart) { 'enabled' } else { 'disabled' }) -ForegroundColor DarkGray
}

Write-Step 'Post-install health checks'
$health = Invoke-PostInstallHealthChecks -TailscaleIp $tailscaleIp `
    -OllamaSelected:$Script:InstallOllama `
    -MemOSSelected:($Script:MemOSMode -ne 'skip') `
    -SkillsSelected:$Script:InstallObsidianSkills
if ($tailscaleIp) {
    $health.gateway_tailscale = Wait-HttpHealthy "http://${tailscaleIp}:$Script:GatewayPort/health" 8
    $health.workspace_tailscale = Wait-HttpHealthy "http://${tailscaleIp}:$Script:WorkspacePort/api/healthcheck" 8
}
foreach ($key in $health.Keys) {
    $flag = if ($health[$key]) { 'OK' } else { 'WARN' }
    $color = if ($health[$key]) { 'Green' } else { 'Yellow' }
    Write-Host ("  {0}: {1}" -f $key, $flag) -ForegroundColor $color
}
$criticalHealthKeys = @('gateway', 'workspace')
$failedCritical = @($criticalHealthKeys | Where-Object { -not [bool]$health[$_] })
if ($Script:StartServices -and $failedCritical.Count -gt 0) {
    $failed = $failedCritical -join ', '
    throw "Critical post-install health check failed: $failed"
}
Write-InstallManifest -Extra $health | Out-Null
Export-DiagnosticBundle | Out-Null

Write-Step 'Building connect payload (Tailscale preferred for phone)'
$lanIp = if ($lanIp) { $lanIp } else { Get-LanIp }
$connectIp = if ($Script:ConnectHost) { $Script:ConnectHost } elseif ($tailscaleIp) { $tailscaleIp } else { $lanIp }
$expiryUtc = (Get-Date).ToUniversalTime().AddHours(24)
$connectExpiryEpoch = [int64][Math]::Floor(($expiryUtc - [datetime]'1970-01-01T00:00:00Z').TotalSeconds)
$payload = [ordered]@{
    v = 1
    gateway = "http://${connectIp}:$Script:GatewayPort"
    dashboard = "http://${connectIp}:$Script:WorkspacePort"
    agentDashboard = "http://${connectIp}:$Script:DashboardPort"
    workspace = "http://${connectIp}:$Script:WorkspacePort"
    apiKey = $apiKey
    password = $hermesPassword
    tailscaleIp = if ($tailscaleIp) { $tailscaleIp } else { '' }
    exp = $connectExpiryEpoch
} | ConvertTo-Json -Compress
$connectCode = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payload))
$deepLink = "hermes://connect?data=$connectCode"
$connectHtml = Join-Path $Script:HermesHome 'connect.html'
New-ConnectHtml -Path $connectHtml -DeepLink $deepLink -Code $connectCode `
    -ConnectIp $connectIp -LanIp $lanIp `
    -TailscaleIp $(if ($tailscaleIp) { $tailscaleIp } else { '' }) `
    -OllamaOk ([bool]$ollamaOk)

$requiredArtifacts = @($Script:HermesEnv, (Join-Path $Script:WorkspaceDir '.env'), (Join-Path $Script:HermesHome 'install-meta.json'), $connectHtml)
$missingArtifacts = @($requiredArtifacts | Where-Object { -not (Test-Path -LiteralPath $_) })
if ($missingArtifacts.Count -gt 0) {
    throw ("Required installation artifacts are missing: " + ($missingArtifacts -join ', '))
}
$completion = [ordered]@{
    version = $Script:InstallerVersion
    completedAt = (Get-Date -Format o)
    installMeta = (Join-Path $Script:HermesHome 'install-meta.json')
    connectPage = $connectHtml
    health = $health
} | ConvertTo-Json -Depth 8
Write-Utf8NoBom -Path $Script:CompletionMarker -Content $completion

Write-Host ''
Write-Host 'INSTALLATION COMPLETE' -ForegroundColor Green
Write-Host ("VERSION: {0}" -f $Script:InstallerVersion)
Write-Host ''
if ($Script:PrintConnectSecrets) {
    Write-Host 'Connect code (contains secrets - do not share):' -ForegroundColor Cyan
    Write-Host $connectCode
    Write-Host ''
} else {
    Write-Host 'Connect code written to QR page only (not printed to console).' -ForegroundColor DarkGray
    Write-Host ''
}
Write-Host ("QR page: {0}" -f $connectHtml)
Write-Host ("Connect host (Tailscale preferred): {0}" -f $connectIp)
Write-Host ("Gateway: http://{0}:{1}" -f $connectIp, $Script:GatewayPort)
Write-Host ("Workspace: http://{0}:{1}" -f $connectIp, $Script:WorkspacePort)
Write-Host ("Agent dashboard: http://{0}:{1}" -f $connectIp, $Script:DashboardPort)
Write-Host 'Ollama: local for Hermes at http://127.0.0.1:11434 (not exposed to phone)'
if (-not $tailscaleIp -and $Script:InstallTailscale) {
    Write-Host 'WARNING: No Tailscale IP - install/login Tailscale on PC and phone, then re-run.' -ForegroundColor Yellow
}
Write-Host ''
Write-Host 'Phone: Tailscale same tailnet + scan QR in Hermes app.'

if ($Script:CreateShortcuts) {
    try {
        $wsh = New-Object -ComObject WScript.Shell
        $desktop = [Environment]::GetFolderPath('Desktop')
        $startMenu = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\Hermes'
        New-Item -ItemType Directory -Path $startMenu -Force | Out-Null
        $lnk1 = $wsh.CreateShortcut((Join-Path $desktop 'Hermes Connect QR.lnk'))
        $lnk1.TargetPath = $connectHtml
        $lnk1.Description = 'Hermes phone connect QR'
        $lnk1.Save()
        $lnk2 = $wsh.CreateShortcut((Join-Path $startMenu 'Hermes Connect QR.lnk'))
        $lnk2.TargetPath = $connectHtml
        $lnk2.Save()
        Write-Host '  Desktop / Start Menu shortcuts created'
    } catch {
        Write-Host "  Shortcut create skipped: $_" -ForegroundColor Yellow
    }
}

if ($Script:OpenConnect) {
    Start-Process $connectHtml
}

if (-not $Script:NoPause) {
    Write-Host ''
    Write-Host 'Press Enter to close...'
    try { [void][Console]::ReadLine() } catch { }
}

} catch {
    Write-InstallFailureReport -ErrorRecord $_
    # Invalidate completion marker on any failure.
    try { Remove-Item -LiteralPath $Script:CompletionMarker -Force -ErrorAction SilentlyContinue } catch { }
    if (-not $Script:NoPause) { Wait-OnInstallFailure }
    exit 1
}

