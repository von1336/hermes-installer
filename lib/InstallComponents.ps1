#Requires -Version 5.1
# Detect/Ensure framework for Hermes professional installer.
# Dot-source after $Script:HermesHome, $Script:LogFile, and helper functions are defined.

Set-StrictMode -Version Latest

$Script:OutcomeInstalled      = 'installed'
$Script:OutcomeAlreadyPresent = 'already_present'
$Script:OutcomeSkipped        = 'skipped'
$Script:OutcomeFailedSoft     = 'failed_soft'
$Script:OutcomeFailedHard     = 'failed_hard'

if (-not $Script:ComponentResults) {
    $Script:ComponentResults = [ordered]@{}
}

function Get-NativePath {
    param($CommandOrItem)
    if ($null -eq $CommandOrItem) { return $null }
    if ($CommandOrItem -is [System.Management.Automation.ApplicationInfo]) {
        return [string]$CommandOrItem.Source
    }
    if ($CommandOrItem.PSObject.Properties['Source'] -and $CommandOrItem.Source) {
        return [string]$CommandOrItem.Source
    }
    if ($CommandOrItem -is [System.IO.FileInfo]) {
        return [string]$CommandOrItem.FullName
    }
    if ($CommandOrItem -is [string] -and (Test-Path -LiteralPath $CommandOrItem)) {
        return [string]$CommandOrItem
    }
    return $null
}

function New-ComponentResult {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Outcome,
        [string]$Message = '',
        [hashtable]$Details = @{}
    )
    $result = [ordered]@{
        name    = $Name
        outcome = $Outcome
        message = $Message
        at      = (Get-Date -Format o)
        details = $Details
    }
    $Script:ComponentResults[$Name] = $result
    return $result
}

function Redact-Secret([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return '<empty>' }
    if ($Value.Length -le 4) { return '****' }
    return ('****' + $Value.Substring($Value.Length - 4))
}

function Write-InstallLog([string]$Message, [switch]$Secret) {
    $line = if ($Secret) { Redact-Secret $Message } else { $Message }
    if ($Script:LogFile) {
        Add-Content -Path $Script:LogFile -Value ("[{0}] {1}" -f (Get-Date -Format o), $line) -ErrorAction SilentlyContinue
    }
}

function Get-HermesAgentHome {
    if ($Script:HermesHome -and $Script:HermesHome.Trim()) {
        return [IO.Path]::GetFullPath($Script:HermesHome.Trim())
    }
    if ($env:HERMES_HOME -and $env:HERMES_HOME.Trim()) {
        return [IO.Path]::GetFullPath($env:HERMES_HOME.Trim())
    }
    return Join-Path $env:LOCALAPPDATA 'hermes'
}

function Get-HermesSkillsDir {
    $candidates = @(
        (Join-Path $env:USERPROFILE '.hermes\skills'),
        (Join-Path (Get-HermesAgentHome) 'skills')
    )
    foreach ($dir in $candidates) {
        $parent = Split-Path $dir -Parent
        if (Test-Path $parent) {
            if (-not (Test-Path $dir)) {
                New-Item -ItemType Directory -Path $dir -Force | Out-Null
            }
            return $dir
        }
    }
    $fallback = Join-Path $env:USERPROFILE '.hermes\skills'
    New-Item -ItemType Directory -Path $fallback -Force | Out-Null
    return $fallback
}

function Get-MemOSHomeDir {
    $prefix = Join-Path $env:LOCALAPPDATA 'hermes\memos-plugin'
    $runtimeMarker = Join-Path $prefix '.memos-runtime-home'
    if (Test-Path $runtimeMarker) {
        $raw = Get-Content $runtimeMarker -Raw -ErrorAction SilentlyContinue
        $runtimeHome = if ($null -ne $raw) { $raw.ToString().Trim() } else { '' }
        if ($runtimeHome -and (Test-Path $runtimeHome)) { return $runtimeHome }
    }
    $legacy = Join-Path $env:USERPROFILE '.hermes\memos-plugin'
    if (Test-Path (Join-Path $legacy 'config.yaml')) { return $legacy }
    if (Test-Path (Join-Path $prefix 'config.yaml')) { return $prefix }
    return $prefix
}

function Get-MemOSConfigPath {
    return Join-Path (Get-MemOSHomeDir) 'config.yaml'
}

function Detect-Tailscale {
    $cmd = Get-Command tailscale -ErrorAction SilentlyContinue
    if (-not $cmd) {
        foreach ($c in @(
            (Join-Path $env:ProgramFiles 'Tailscale\tailscale.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Tailscale\tailscale.exe')
        )) {
            if (Test-Path $c) { $cmd = Get-Item $c; break }
        }
    }
    $ip = $null
    $running = $false
    $exePath = Get-NativePath $cmd
    if ($exePath) {
        try {
            $ip = (& $exePath ip -4 2>$null | Select-Object -First 1).ToString().Trim()
            $status = & $exePath status --json 2>$null | ConvertFrom-Json
            $running = ($status.BackendState -eq 'Running')
        } catch { }
    }
    return [pscustomobject]@{
        Installed = [bool]$exePath
        Running   = $running
        Ip        = if ($ip -match '^100\.') { $ip } else { $null }
        Path      = $exePath
    }
}

function Ensure-TailscaleComponent {
    param([switch]$SkipInstall)

    if ($SkipInstall) {
        New-ComponentResult -Name 'tailscale' -Outcome $Script:OutcomeSkipped -Message 'Deselected in wizard'
        return $null
    }

    $detect = Detect-Tailscale
    if ($detect.Installed -and $detect.Ip) {
        Write-Host ("  Tailscale already present: {0}" -f $detect.Ip) -ForegroundColor Green
        New-ComponentResult -Name 'tailscale' -Outcome $Script:OutcomeAlreadyPresent -Message $detect.Ip -Details @{ ip = $detect.Ip }
        return $detect.Ip
    }

    $tailscaleCmd = Get-Command tailscale -ErrorAction SilentlyContinue
    if (-not $tailscaleCmd) {
        Write-Host '  Installing Tailscale via winget...'
        try {
            winget install --id Tailscale.Tailscale -e --accept-package-agreements --accept-source-agreements | Out-Null
        } catch {
            Write-Host "  winget Tailscale failed: $_" -ForegroundColor Yellow
        }
        Refresh-Path
        foreach ($c in @(
            (Join-Path $env:ProgramFiles 'Tailscale\tailscale.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Tailscale\tailscale.exe')
        )) {
            if (Test-Path $c) {
                $dir = Split-Path $c -Parent
                if ($env:Path -notlike "*$dir*") { $env:Path = "$dir;$env:Path" }
                break
            }
        }
        $tailscaleCmd = Get-Command tailscale -ErrorAction SilentlyContinue
    }

    if (-not $tailscaleCmd) {
        New-ComponentResult -Name 'tailscale' -Outcome $Script:OutcomeFailedSoft -Message 'CLI not found after install'
        return $null
    }

    $tsExe = Get-NativePath $tailscaleCmd
    if (-not $tsExe) {
        foreach ($c in @(
            (Join-Path $env:ProgramFiles 'Tailscale\tailscale.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Tailscale\tailscale.exe')
        )) {
            if (Test-Path $c) { $tsExe = $c; break }
        }
    }
    try {
        if ($tsExe) {
            $status = & $tsExe status --json 2>$null | ConvertFrom-Json
            if (-not $status.Self -or $status.BackendState -ne 'Running') {
                Write-Host '  Tailscale login may open in browser...' -ForegroundColor Yellow
                Start-Process -FilePath $tsExe -ArgumentList 'up' -Wait -NoNewWindow
            }
        } else {
            & tailscale up
        }
    } catch {
        try {
            if ($tsExe) { Start-Process -FilePath $tsExe -ArgumentList 'up' -Wait -NoNewWindow }
            else { & tailscale up }
        } catch { }
    }

    $ip = $null
    try { $ip = (& tailscale ip -4 2>$null | Select-Object -First 1).ToString().Trim() } catch { }
    if ($ip -and $ip -match '^100\.') {
        Write-Host ("  Tailscale IP: {0}" -f $ip) -ForegroundColor Green
        $outcome = if ($detect.Installed) { $Script:OutcomeAlreadyPresent } else { $Script:OutcomeInstalled }
        New-ComponentResult -Name 'tailscale' -Outcome $outcome -Message $ip -Details @{ ip = $ip }
        return $ip
    }

    New-ComponentResult -Name 'tailscale' -Outcome $Script:OutcomeFailedSoft -Message 'No 100.x IP yet'
    return $null
}

function Detect-Ollama {
    $cmd = Get-Command ollama -ErrorAction SilentlyContinue
    if (-not $cmd) {
        $exe = Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'
        if (Test-Path $exe) { $cmd = Get-Item $exe }
    }
    $healthy = $false
    if ($cmd) {
        try {
            $r = Invoke-WebRequest -Uri "http://127.0.0.1:$Script:OllamaPort/api/tags" -UseBasicParsing -TimeoutSec 3
            $healthy = ($r.StatusCode -ge 200 -and $r.StatusCode -lt 300)
        } catch { }
    }
    return [pscustomobject]@{
        Installed = [bool](Get-NativePath $cmd)
        Healthy   = $healthy
        Path      = Get-NativePath $cmd
    }
}

function Ensure-OllamaComponent {
    param([switch]$SkipInstall)

    if ($SkipInstall) {
        New-ComponentResult -Name 'ollama' -Outcome $Script:OutcomeSkipped -Message 'Deselected in wizard'
        return $false
    }

    $detect = Detect-Ollama
    if ($detect.Installed -and $detect.Healthy) {
        Write-Host '  Ollama already running' -ForegroundColor Green
        New-ComponentResult -Name 'ollama' -Outcome $Script:OutcomeAlreadyPresent -Message 'API healthy'
        return $true
    }

    $ollamaCmd = Get-Command ollama -ErrorAction SilentlyContinue
    if (-not $ollamaCmd) {
        $ollamaPath = Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'
        if (Test-Path $ollamaPath) { $ollamaCmd = Get-Item $ollamaPath }
    }
    if (-not $ollamaCmd) {
        Write-Host '  Installing Ollama via winget...'
        try {
            winget install --id Ollama.Ollama -e --accept-package-agreements --accept-source-agreements | Out-Null
        } catch {
            Write-Host "  winget Ollama failed: $_" -ForegroundColor Yellow
        }
        Refresh-Path
        $ollamaPath = Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'
        if (Test-Path $ollamaPath) {
            $dir = Split-Path $ollamaPath -Parent
            if ($env:Path -notlike "*$dir*") { $env:Path = "$dir;$env:Path" }
        }
        $ollamaCmd = Get-Command ollama -ErrorAction SilentlyContinue
    }

    if (-not $ollamaCmd) {
        New-ComponentResult -Name 'ollama' -Outcome $Script:OutcomeFailedSoft -Message 'Not found after install attempt'
        return $false
    }

    try {
        [Environment]::SetEnvironmentVariable('OLLAMA_HOST', '127.0.0.1:11434', 'User')
        $env:OLLAMA_HOST = '127.0.0.1:11434'
    } catch { }

    $up = $false
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:$Script:OllamaPort/api/tags" -UseBasicParsing -TimeoutSec 3
        $up = ($r.StatusCode -ge 200 -and $r.StatusCode -lt 300)
    } catch { }

    if (-not $up) {
        $ollamaExe = Get-NativePath $ollamaCmd
        if (-not $ollamaExe) {
            $ollamaExe = Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'
        }
        if ($ollamaExe -and (Test-Path $ollamaExe)) {
            Write-Host '  Starting ollama serve...'
            Start-Process -FilePath $ollamaExe -ArgumentList 'serve' -WindowStyle Hidden
            Start-Sleep -Seconds 3
        }
    }

    $ok = Wait-HttpHealthy "http://127.0.0.1:$Script:OllamaPort/api/tags" 60
    $outcome = if ($detect.Installed) { $Script:OutcomeAlreadyPresent } elseif ($ok) { $Script:OutcomeInstalled } else { $Script:OutcomeFailedSoft }
    New-ComponentResult -Name 'ollama' -Outcome $outcome -Message $(if ($ok) { 'API healthy' } else { 'Not responding' })
    return $ok
}

function Detect-Obsidian {
    $paths = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\obsidian\Obsidian.exe'),
        (Join-Path $env:LOCALAPPDATA 'Obsidian\Obsidian.exe'),
        (Join-Path $env:ProgramFiles 'Obsidian\Obsidian.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Obsidian\Obsidian.exe')
    )
    foreach ($p in $paths) {
        if ($p -and (Test-Path $p)) {
            return [pscustomobject]@{ Installed = $true; Path = $p }
        }
    }

    $cmd = Get-Command Obsidian -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source -and (Test-Path $cmd.Source)) {
        return [pscustomobject]@{ Installed = $true; Path = $cmd.Source }
    }

    foreach ($regPath in @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\Obsidian.exe',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\App Paths\Obsidian.exe'
    )) {
        try {
            $appPath = (Get-ItemProperty -Path $regPath -ErrorAction Stop).'(default)'
            if ($appPath -and (Test-Path $appPath)) {
                return [pscustomobject]@{ Installed = $true; Path = $appPath }
            }
        } catch { }
    }

    return [pscustomobject]@{ Installed = $false; Path = $null }
}

function Ensure-ObsidianComponent {
    param([switch]$SkipInstall)

    if ($SkipInstall) {
        New-ComponentResult -Name 'obsidian' -Outcome $Script:OutcomeSkipped -Message 'Deselected in wizard'
        return $false
    }

    $detect = Detect-Obsidian
    if ($detect.Installed) {
        Write-Host ("  Obsidian already installed: {0}" -f $detect.Path) -ForegroundColor Green
        New-ComponentResult -Name 'obsidian' -Outcome $Script:OutcomeAlreadyPresent -Message $detect.Path
        return $true
    }

    Write-Host '  Installing Obsidian via winget...'
    try {
        winget install --id Obsidian.Obsidian -e --accept-package-agreements --accept-source-agreements | Out-Null
        Refresh-Path
    } catch {
        New-ComponentResult -Name 'obsidian' -Outcome $Script:OutcomeFailedSoft -Message $_.Exception.Message
        return $false
    }

    $detect = Detect-Obsidian
    if ($detect.Installed) {
        New-ComponentResult -Name 'obsidian' -Outcome $Script:OutcomeInstalled -Message $detect.Path
        return $true
    }
    New-ComponentResult -Name 'obsidian' -Outcome $Script:OutcomeFailedSoft -Message 'Install finished but exe not found'
    return $false
}

function Detect-ObsidianSkills {
    $skillsDir = Get-HermesSkillsDir
    $target = Join-Path $skillsDir 'obsidian-skills'
    $skillFiles = @()
    if (Test-Path $target) {
        $skillFiles = Get-ChildItem -Path $target -Recurse -Filter 'SKILL.md' -ErrorAction SilentlyContinue
    }
    return [pscustomobject]@{
        Installed = ($skillFiles.Count -gt 0)
        Path      = $target
        Count     = $skillFiles.Count
    }
}

function Ensure-ObsidianSkillsComponent {
    param([switch]$SkipInstall)

    if ($SkipInstall) {
        New-ComponentResult -Name 'obsidian_skills' -Outcome $Script:OutcomeSkipped -Message 'Deselected in wizard'
        return $false
    }

    $detect = Detect-ObsidianSkills
    if ($detect.Installed) {
        Write-Host ("  obsidian-skills already present ({0} skills)" -f $detect.Count) -ForegroundColor Green
        New-ComponentResult -Name 'obsidian_skills' -Outcome $Script:OutcomeAlreadyPresent -Message "$($detect.Count) skills" -Details @{ path = $detect.Path }
        return $true
    }

    $skillsDir = Get-HermesSkillsDir
    $target = Join-Path $skillsDir 'obsidian-skills'
    $repo = 'https://github.com/kepano/obsidian-skills.git'

    Write-Host '  Installing obsidian-skills into Hermes skills directory...'
    $tempClone = Join-Path $env:TEMP ("obsidian-skills-clone-{0}" -f [guid]::NewGuid().ToString('N'))
    try {
        git clone --depth 1 $repo $tempClone 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "git clone failed with exit $LASTEXITCODE" }
        if (Test-Path $target) { Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue }
        Move-Item -LiteralPath $tempClone -Destination $target -Force
    } catch {
        if (Test-Path $tempClone) { Remove-Item $tempClone -Recurse -Force -ErrorAction SilentlyContinue }
        New-ComponentResult -Name 'obsidian_skills' -Outcome $Script:OutcomeFailedSoft -Message $_.Exception.Message
        return $false
    }

    $detect = Detect-ObsidianSkills
    if ($detect.Installed) {
        New-ComponentResult -Name 'obsidian_skills' -Outcome $Script:OutcomeInstalled -Message "$($detect.Count) skills" -Details @{ path = $detect.Path }
        return $true
    }
    New-ComponentResult -Name 'obsidian_skills' -Outcome $Script:OutcomeFailedSoft -Message 'Clone completed but no SKILL.md found'
    return $false
}

function Detect-MemOS {
    $configPath = Get-MemOSConfigPath
    $prefix = Join-Path $env:LOCALAPPDATA 'hermes\memos-plugin'
    $bridge = Join-Path $prefix 'dist\bridge.cjs'
    if (-not (Test-Path $bridge)) { $bridge = Join-Path $prefix 'bridge.cts' }
    return [pscustomobject]@{
        Installed  = (Test-Path $configPath)
        ConfigPath = $configPath
        Prefix     = $prefix
        Bridge     = $bridge
    }
}

function Set-YamlBlockField {
    param(
        [string]$Content,
        [string]$Block,
        [string]$Field,
        [string]$Value
    )
    $blockPattern = "(?m)^${Block}:\s*\n(?:\s+.+\n)*"
    if ($Content -notmatch "(?m)^${Block}:") {
        return ($Content.TrimEnd() + "`n${Block}:`n  ${Field}: ${Value}`n")
    }
    $fieldPattern = "(?m)(^${Block}:[\s\S]*?\s*${Field}:\s*).*$"
    if ($Content -match $fieldPattern) {
        return [regex]::Replace($Content, $fieldPattern, "`${1}$Value", 1)
    }
    return [regex]::Replace($Content, "(?m)^(${Block}:\s*\n)", "`${1}  ${Field}: $Value`n", 1)
}

function Set-MemOSConfigProfile {
    param(
        [ValidateSet('local', 'provider')]
        [string]$Mode,
        [string]$ProviderBaseUrl = '',
        [string]$ProviderApiKey = '',
        [string]$ProviderModel = 'gpt-4o-mini',
        [switch]$OllamaAvailable
    )

    $configPath = Get-MemOSConfigPath
    if (-not (Test-Path $configPath)) {
        Write-Host '  MemOS config.yaml not found --" skipping profile patch' -ForegroundColor Yellow
        return $false
    }

    $content = Get-Content $configPath -Raw -Encoding UTF8
    if ($null -eq $content) { $content = '' }

    if ($Mode -eq 'local') {
        $content = Set-YamlBlockField -Content $content -Block 'embedding' -Field 'provider' -Value 'local'
        if ($OllamaAvailable) {
            $cloudDefaults = @('gpt-4o-mini', 'gpt-4o', 'gpt-4', 'gpt-3.5-turbo')
            $model = if ($ProviderModel -and ($ProviderModel -notin $cloudDefaults)) { $ProviderModel } else { 'llama3.2' }
            $content = Set-YamlBlockField -Content $content -Block 'llm' -Field 'provider' -Value 'openai_compatible'
            $content = Set-YamlBlockField -Content $content -Block 'llm' -Field 'baseUrl' -Value 'http://127.0.0.1:11434/v1'
            $content = Set-YamlBlockField -Content $content -Block 'llm' -Field 'apiKey' -Value 'ollama'
            $content = Set-YamlBlockField -Content $content -Block 'llm' -Field 'model' -Value $model
        }
    } else {
        $baseUrl = if ($ProviderBaseUrl) { $ProviderBaseUrl } else { 'https://api.openai.com/v1' }
        $model = if ($ProviderModel) { $ProviderModel } else { 'gpt-4o-mini' }
        $content = Set-YamlBlockField -Content $content -Block 'embedding' -Field 'provider' -Value 'openai_compatible'
        $content = Set-YamlBlockField -Content $content -Block 'llm' -Field 'provider' -Value 'openai_compatible'
        $content = Set-YamlBlockField -Content $content -Block 'llm' -Field 'baseUrl' -Value $baseUrl
        $content = Set-YamlBlockField -Content $content -Block 'llm' -Field 'model' -Value $model
        if ($ProviderApiKey) {
            $content = Set-YamlBlockField -Content $content -Block 'llm' -Field 'apiKey' -Value $ProviderApiKey
            Write-InstallLog -Message "MemOS provider key set: $(Redact-Secret $ProviderApiKey)" -Secret
        } else {
            $content = Set-YamlBlockField -Content $content -Block 'llm' -Field 'apiKey' -Value '""'
        }
    }

    Write-Utf8NoBom -Path $configPath -Content $content
    Write-Host ("  Patched MemOS config for mode: {0}" -f $Mode) -ForegroundColor Green
    return $true
}

function Test-MemOSProviderProbe {
    param(
        [string]$BaseUrl,
        [string]$ApiKey,
        [string]$Model
    )
    if ([string]::IsNullOrWhiteSpace($BaseUrl) -or [string]::IsNullOrWhiteSpace($ApiKey)) {
        return $false
    }
    $url = $BaseUrl.TrimEnd('/') + '/models'
    try {
        $headers = @{ Authorization = "Bearer $ApiKey" }
        $r = Invoke-WebRequest -Uri $url -Headers $headers -UseBasicParsing -TimeoutSec 15
        return ($r.StatusCode -ge 200 -and $r.StatusCode -lt 300)
    } catch {
        return $false
    }
}

function Test-MemOSHealth {
    param([int]$Port = 18800)
    return (Wait-HttpHealthy "http://127.0.0.1:$Port/" 15)
}

function Ensure-MemOSComponent {
    param(
        [switch]$SkipInstall,
        [ValidateSet('local', 'provider', 'skip')]
        [string]$Mode = 'skip',
        [string]$ProviderBaseUrl = '',
        [string]$ProviderApiKey = '',
        [string]$ProviderModel = 'gpt-4o-mini',
        [switch]$OllamaAvailable
    )

    if ($SkipInstall -or $Mode -eq 'skip') {
        New-ComponentResult -Name 'memos' -Outcome $Script:OutcomeSkipped -Message 'Deselected in wizard'
        return $false
    }

    $detect = Detect-MemOS
    $wasPresent = $detect.Installed

    if ($Mode -eq 'provider' -and [string]::IsNullOrWhiteSpace($ProviderApiKey)) {
        Write-Host '  MemOS provider mode: API key missing --" continuing with degraded config' -ForegroundColor Yellow
    }

    $installScriptUrl = 'https://raw.githubusercontent.com/MemTensor/MemOS/main/apps/memos-local-plugin/install.ps1'
    $installScriptPath = Join-Path $Script:HermesHome 'memos-local-plugin-install.ps1'
    $memosLogOut = Join-Path $Script:HermesHome 'memos-install-out.log'
    $memosLogErr = Join-Path $Script:HermesHome 'memos-install-err.log'
    $providerProbeOk = $null
    $installExit = 0
    $degraded = $false

    if (-not $wasPresent) {
        Write-Host '  Downloading MemOS local plugin installer...'
        try {
            if (Get-Command Get-CachedOrDownloadScript -ErrorAction SilentlyContinue) {
                $null = Get-CachedOrDownloadScript -Url $installScriptUrl -DestPath $installScriptPath -SanityPattern 'memos|MemOS|install'
            } else {
                Invoke-WebRequest -Uri $installScriptUrl -UseBasicParsing -OutFile $installScriptPath
                if (-not (Test-Path $installScriptPath) -or ((Get-Item $installScriptPath).Length -lt 512)) {
                    throw 'Downloaded MemOS install script looks truncated or missing'
                }
            }

            # Isolated child process so upstream exit cannot kill the Hermes installer.
            $wrapperPath = Join-Path $Script:HermesHome 'memos-install-wrapper.ps1'
            $safeInstallScript = $installScriptPath.Replace("'", "''")
            $wrapper = @(
                "`$ErrorActionPreference = 'Continue'"
                "'2' | & '$safeInstallScript'"
                "exit `$LASTEXITCODE"
            ) -join [Environment]::NewLine
            Set-Content -Path $wrapperPath -Value $wrapper -Encoding UTF8
            Write-Host '  Running MemOS install in isolated process (Hermes agent)...'
            $proc = Start-Process -FilePath 'powershell.exe' -ArgumentList @(
                '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $wrapperPath
            ) -Wait -PassThru -NoNewWindow `
                -RedirectStandardOutput $memosLogOut `
                -RedirectStandardError $memosLogErr
            $installExit = $proc.ExitCode
            if ($installExit -ne 0) {
                Write-Host ("  MemOS installer exited with code {0} (soft fail; core continues)" -f $installExit) -ForegroundColor Yellow
                Write-Host ("  See: {0}" -f $memosLogErr) -ForegroundColor DarkGray
                $degraded = $true
            }
        } catch {
            New-ComponentResult -Name 'memos' -Outcome $Script:OutcomeFailedSoft -Message $_.Exception.Message `
                -Details @{ mode = $Mode; degraded = $true; log = $memosLogErr }
            return $false
        }
    } else {
        Write-Host '  MemOS already installed --" applying profile only' -ForegroundColor Green
    }

    $detectAfter = Detect-MemOS
    if (-not $detectAfter.Installed -and $installExit -ne 0) {
        New-ComponentResult -Name 'memos' -Outcome $Script:OutcomeFailedSoft `
            -Message "installer exit=$installExit; config missing" `
            -Details @{ mode = $Mode; degraded = $true; exitCode = $installExit; log = $memosLogErr }
        return $false
    }

    try {
        Set-MemOSConfigProfile -Mode $Mode `
            -ProviderBaseUrl $ProviderBaseUrl `
            -ProviderApiKey $ProviderApiKey `
            -ProviderModel $ProviderModel `
            -OllamaAvailable:$OllamaAvailable | Out-Null
    } catch {
        Write-Host "  MemOS config patch failed: $_" -ForegroundColor Yellow
        $degraded = $true
    }

    if ($Mode -eq 'provider' -and $ProviderApiKey) {
        $providerProbeOk = Test-MemOSProviderProbe -BaseUrl $ProviderBaseUrl -ApiKey $ProviderApiKey -Model $ProviderModel
        if (-not $providerProbeOk) {
            Write-Host '  MemOS provider probe failed --" credentials may be invalid (soft fail)' -ForegroundColor Yellow
            $degraded = $true
        }
    } elseif ($Mode -eq 'provider' -and [string]::IsNullOrWhiteSpace($ProviderApiKey)) {
        $providerProbeOk = $false
        $degraded = $true
    }

    $healthy = Test-MemOSHealth
    if (-not $healthy) { $degraded = $true }

    $outcome = if ($wasPresent -and -not $degraded) {
        $Script:OutcomeAlreadyPresent
    } elseif ($degraded -or $installExit -ne 0) {
        $Script:OutcomeFailedSoft
    } else {
        $Script:OutcomeInstalled
    }

    New-ComponentResult -Name 'memos' -Outcome $outcome `
        -Message $(if ($healthy -and -not $degraded) { "mode=$Mode healthy" } else { "mode=$Mode degraded" }) `
        -Details @{
            mode           = $Mode
            healthy        = $healthy
            degraded       = $degraded
            providerProbe  = $providerProbeOk
            exitCode       = $installExit
            config         = (Get-MemOSConfigPath)
            logOut         = $memosLogOut
            logErr         = $memosLogErr
        }
    # Fail-open: never abort core Hermes.
    return ($healthy -or $wasPresent -or $detectAfter.Installed)
}

function Write-InstallManifest {
    param(
        [hashtable]$Extra = @{}
    )
    $manifest = [ordered]@{
        version     = $Script:InstallerVersion
        installDir  = $Script:HermesHome
        workspaceDir = $Script:WorkspaceDir
        installedAt = (Get-Date -Format o)
        choices     = [ordered]@{
            tailscale       = [bool]$Script:InstallTailscale
            ollama          = [bool]$Script:InstallOllama
            memos           = ($Script:MemOSMode -ne 'skip')
            memosMode       = $Script:MemOSMode
            obsidian        = [bool]$Script:InstallObsidian
            obsidianSkills  = [bool]$Script:InstallObsidianSkills
            firewall        = [bool]$Script:ConfigureFirewall
            startServices   = [bool]$Script:StartServices
        }
        components  = $Script:ComponentResults
        health      = $Extra
    }
    $path = Join-Path $Script:HermesHome 'install-meta.json'
    Write-Utf8NoBom -Path $path -Content ($manifest | ConvertTo-Json -Depth 8)
    return $path
}

function Export-DiagnosticBundle {
    param([string]$OutputDir = '')
    if ([string]::IsNullOrWhiteSpace($OutputDir)) {
        $OutputDir = Join-Path $Script:HermesHome 'diagnostics'
    }
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $bundleDir = Join-Path $OutputDir ("bundle-$stamp")
    New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null

    foreach ($file in @('install.log', 'install-meta.json', 'workspace-err.log', 'workspace-out.log', 'memos-install-out.log', 'memos-install-err.log','gateway-install.log','gateway-install-err.log','gateway-start.log','gateway-start-err.log')) {
        $src = Join-Path $Script:HermesHome $file
        if (Test-Path $src) {
            Copy-Item $src (Join-Path $bundleDir $file) -Force
        }
    }

    $snapshot = [ordered]@{
        capturedAt = (Get-Date -Format o)
        tailscale  = Detect-Tailscale
        ollama     = Detect-Ollama
        obsidian   = Detect-Obsidian
        memos      = Detect-MemOS
        skills     = Detect-ObsidianSkills
    } | ConvertTo-Json -Depth 6
    Write-Utf8NoBom -Path (Join-Path $bundleDir 'health-snapshot.json') -Content $snapshot
    Write-Host ("  Diagnostic bundle: {0}" -f $bundleDir) -ForegroundColor DarkGray
    return $bundleDir
}

function Invoke-PostInstallHealthChecks {
    param(
        [string]$TailscaleIp = $null,
        [bool]$OllamaSelected = $false,
        [bool]$MemOSSelected = $false,
        [bool]$SkillsSelected = $false
    )
    $health = [ordered]@{}
    $health.gateway = Wait-HttpHealthy "http://127.0.0.1:$Script:GatewayPort/health" 5
    $health.workspace = Wait-HttpHealthy "http://127.0.0.1:$Script:WorkspacePort/api/healthcheck" 5
    $health.tailscale = [bool]($TailscaleIp -and $TailscaleIp -match '^100\.')
    if ($OllamaSelected) { $health.ollama = (Detect-Ollama).Healthy }
    if ($MemOSSelected) { $health.memos = Test-MemOSHealth }
    if ($SkillsSelected) {
        $s = Detect-ObsidianSkills
        $health.obsidian_skills = ($s.Count -gt 0)
    }
    return $health
}

