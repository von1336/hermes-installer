using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HermesLauncher.Models;

namespace HermesLauncher.Services;

public class InstallerRunnerService
{
    // Legacy events kept for existing UI bindings; raised only for the current operation.
    public event Action<string>? LogReceived;
    public event Action<string, double>? StepChanged;
    public event Action<bool, int, string>? Finished;

    // New typed API for UI contracts.
    public event Action<ProgressEventDto>? ProgressReported;
    public event Action<OperationResult>? OperationCompleted;

    private readonly object _sync = new();
    private InstallOperation? _current;

    public InstallationState State { get; private set; } = InstallationState.Ready;
    public OperationResult? LastResult { get; private set; }

    public Guid? ActiveOperationId
    {
        get { lock (_sync) return _current?.Id; }
    }

    public bool IsRunning
    {
        get { lock (_sync) return _current != null; }
    }

    // Retry is allowed only after the previous operation fully finished
    // (process stopped, stdout/stderr drained, resources disposed, final event queued).
    public bool CanStartNewOperation
    {
        get { lock (_sync) return _current == null; }
    }

    public Task<OperationResult> RunInstallAsync(InstallSettings settings, bool isCleanReinstall = false, CancellationToken cancellationToken = default)
        => RunOperationAsync(new InstallOperation(settings, isCleanReinstall, isUninstall: false, cancellationToken));

    public Task<OperationResult> RunUninstallAsync(InstallSettings settings, CancellationToken cancellationToken = default)
        => RunOperationAsync(new InstallOperation(settings, isCleanReinstall: false, isUninstall: true, cancellationToken));

    private async Task<OperationResult> RunOperationAsync(InstallOperation op)
    {
        lock (_sync)
        {
            if (_current != null)
                throw new InvalidOperationException("Another installation is still running or finalizing. Wait for its final event before retrying.");
            _current = op;
            State = InstallationState.Running;
        }

        OperationResult result;
        string? launchScript = null;
        try
        {
            launchScript = await ExecuteOperationAsync(op);
        }
        catch (Exception ex)
        {
            result = BuildResult(op, InstallationState.Failed, -1, Redact(op, ex.Message), null);
            goto Finalize;
        }
        result = op.Result ?? BuildResult(op, InstallationState.Failed, -1, "Operation ended without a result.", null);

    Finalize:
        // Full teardown before the final event: dispose CTS/process, delete temp script,
        // clear current slot, then publish the result so retry is safe afterwards.
        try { op.Dispose(); } catch { }
        if (launchScript != null) TryDeleteFile(launchScript);
        if (!string.IsNullOrEmpty(op.TempDirectoryToClean)) TryDeleteDirectory(op.TempDirectoryToClean);
        CleanupLegacyTempScripts();
        lock (_sync)
        {
            if (ReferenceEquals(_current, op)) _current = null;
            State = result.FinalState;
            LastResult = result;
        }
        Finished?.Invoke(result.IsSuccess, result.ExitCode, result.RedactedMessage);
        OperationCompleted?.Invoke(result);
        return result;
    }

    public void Cancel()
    {
        InstallOperation? op;
        lock (_sync)
        {
            op = _current;
            if (op == null) return;
            op.CancellingRequested = true;
            State = InstallationState.Cancelling;
        }
        try { op.Cts.Cancel(); } catch { }
        var p = op.Process;
        if (p != null)
        {
            try { if (!p.HasExited) p.Kill(true); } catch { }
        }
    }

    private async Task<string?> ExecuteOperationAsync(InstallOperation op)
    {
        if (op.IsCleanReinstall || op.IsUninstall)
        {
            RaiseStep(op, "cleanup", op.IsUninstall ? "Stopping Hermes services..." : "Terminating running services for clean install...", 0.02);
            RaiseLog(op, $"[{(op.IsUninstall ? "UNINSTALL" : "CLEAN REINSTALL")}] Stopping Hermes-owned processes (registry verified)...");
            var diag = new StringBuilder();
            var stopped = ProcessOwnershipRegistry.StopAllOwned(op.Settings.InstallDir, op.Settings.WorkspaceDir, diag);
            RaiseLog(op, $"[{(op.IsUninstall ? "UNINSTALL" : "CLEAN REINSTALL")}] stopped={stopped} {diag}");
            await Task.Delay(1000);
        }

        if (op.IsUninstall)
        {
            return await ExecuteUninstallAsync(op);
        }

        return await ExecuteInstallAsync(op);
    }

    private async Task<string?> ExecuteInstallAsync(InstallOperation op)
    {
        var installScript = EnsureInstallerFilesExtracted();
        var scriptDir = Path.GetDirectoryName(installScript)!;
        var launchScript = WriteLaunchScript(op.Settings, installScript);
        op.LaunchScriptPath = launchScript;

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = scriptDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(launchScript);
        // Provider secrets travel via process environment only, never inside the launch script.
        if (!string.IsNullOrWhiteSpace(op.Settings.MemOSProviderKey))
            psi.Environment["HERMES_INSTALL_MEMOS_PROVIDER_KEY"] = op.Settings.MemOSProviderKey;

        await Task.Run(() => RunChildProcess(op, psi), CancellationToken.None);
        return launchScript;
    }

    private async Task<string?> ExecuteUninstallAsync(InstallOperation op)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hermes-uninstall-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        op.TempDirectoryToClean = tempDir;

        var uninstallScript = ExtractUninstallScript(tempDir);
        var launchScript = WriteUninstallLaunchScript(tempDir, uninstallScript, op.Settings.InstallDir);
        op.LaunchScriptPath = launchScript;

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = Path.GetTempPath(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(launchScript);

        await Task.Run(() => RunChildProcess(op, psi), CancellationToken.None);

        if (op.Result?.FinalState == InstallationState.Completed)
        {
            await PostUninstallCleanupAsync(op);
        }

        return launchScript;
    }

    private void RunChildProcess(InstallOperation op, ProcessStartInfo psi)
    {
        Process? process = null;
        var errorOutput = new StringBuilder();
        double currentStep = 0;
        const double totalSteps = 11.0;

        try
        {
            process = Process.Start(psi);
            if (process == null)
            {
                op.Result = BuildResult(op, InstallationState.Failed, -1, "Failed to launch powershell.exe", null);
                return;
            }
            op.AttachProcess(process);
            if (!op.IsUninstall)
            {
                ProcessOwnershipRegistry.Register(process.Id, null, "powershell -File " + op.LaunchScriptPath,
                    psi.WorkingDirectory, op.Settings.InstallDir, op.Settings.WorkspaceDir, "installer-launch");
            }

            using var cancelRegistration = op.Cts.Token.Register(() =>
            {
                op.CancellingRequested = true;
                try { if (!process.HasExited) process.Kill(true); } catch { }
            });

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null || !IsCurrent(op)) return;
                var line = Redact(op, e.Data);
                RaiseLog(op, line);
                if (line.StartsWith("==> "))
                {
                    currentStep++;
                    var stepTitle = line.Substring(4).Trim();
                    var progress = Math.Min(currentStep / totalSteps, 0.95);
                    RaiseStep(op, Slug(stepTitle), stepTitle, progress);
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null || !IsCurrent(op)) return;
                var line = Redact(op, e.Data);
                RaiseLog(op, $"[ERROR] {line}", LogSeverity.Error);
                errorOutput.AppendLine(line);
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            process.WaitForExit();
            process.WaitForExit(); // drain async output before evaluating the result
            var exitCode = process.ExitCode;

            if (op.CancellingRequested || op.Cts.IsCancellationRequested)
            {
                op.Result = BuildResult(op, InstallationState.Cancelled, -2, OpLabel(op) + " cancelled by user.", null);
            }
            else if (exitCode == 0)
            {
                RaiseStep(op, "complete", op.IsUninstall ? "Uninstall Complete!" : "Installation Complete!", 1.0);
                op.Result = BuildResult(op, InstallationState.Completed, 0, OpLabel(op) + " completed successfully.", null);
            }
            else
            {
                var reportPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "hermes", "install-error.txt");
                var msg = errorOutput.Length > 0
                    ? errorOutput.ToString()
                    : $"Installer script returned exit code {exitCode}";
                string? report = null;
                if (File.Exists(reportPath))
                {
                    msg += Environment.NewLine + Environment.NewLine + "See: " + reportPath;
                    report = reportPath;
                }
                op.Result = BuildResult(op, InstallationState.Failed, exitCode, msg.Trim(), report);
            }
        }
        catch (Exception ex)
        {
            op.Result = BuildResult(op, InstallationState.Failed, -1, Redact(op, ex.Message), null);
        }
        finally
        {
            try { if (process != null && !op.IsUninstall) ProcessOwnershipRegistry.Unregister(process.Id); } catch { }
            try { process?.Dispose(); } catch { }
        }
    }

    private bool IsCurrent(InstallOperation op)
    {
        lock (_sync) return ReferenceEquals(_current, op);
    }

    private void RaiseLog(InstallOperation op, string message, LogSeverity severity = LogSeverity.Info)
    {
        if (!IsCurrent(op)) return; // stale-event filtering by operation identity
        LogReceived?.Invoke(message);
        ProgressReported?.Invoke(new ProgressEventDto
        {
            OperationId = op.Id,
            StepId = "log",
            Severity = severity,
            Message = message
        });
    }

    private void RaiseStep(InstallOperation op, string stepId, string title, double progress)
    {
        if (!IsCurrent(op)) return;
        StepChanged?.Invoke(title, progress);
        ProgressReported?.Invoke(new ProgressEventDto
        {
            OperationId = op.Id,
            StepId = stepId,
            Progress = progress,
            Severity = LogSeverity.Info,
            Message = title
        });
    }

    private OperationResult BuildResult(InstallOperation op, InstallationState state, int exitCode, string message, string? reportPath)
    {
        return new OperationResult
        {
            OperationId = op.Id,
            FinalState = state,
            ExitCode = exitCode,
            RedactedMessage = Redact(op, message),
            ErrorReportPath = reportPath,
            StartedAtUtc = op.StartedAtUtc,
            FinishedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static string Redact(InstallOperation op, string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var secret in op.Secrets)
        {
            if (!string.IsNullOrEmpty(secret))
                text = text.Replace(secret, "****", StringComparison.Ordinal);
        }
        return text;
    }

    private static string OpLabel(InstallOperation op) => op.IsUninstall ? "Uninstall" : "Installation";

    private static string Slug(string title)
    {
        var sb = new StringBuilder(title.Length);
        foreach (var c in title.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        return sb.ToString().Trim('-');
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                ClearAttributesRecursive(new DirectoryInfo(path));
                Directory.Delete(path, true);
            }
        }
        catch { }
    }

    // Remove leftover launch scripts from older launcher versions (may contain secrets).
    private static void CleanupLegacyTempScripts()
    {
        try
        {
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), "hermes-launcher-install-*.ps1"))
                TryDeleteFile(f);
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), "hermes-launcher-uninstall-*.ps1"))
                TryDeleteFile(f);
            foreach (var d in Directory.GetDirectories(Path.GetTempPath(), "hermes-uninstall-*"))
                TryDeleteDirectory(d);
        }
        catch { }
    }

    private static string WriteLaunchScript(InstallSettings settings, string installScriptPath)
    {
        var launchDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "hermes", "launcher-tmp");
        Directory.CreateDirectory(launchDir);
        var launchPath = Path.Combine(launchDir, $"hermes-launcher-install-{Guid.NewGuid():N}.ps1");
        var memMode = settings.InstallMemOS ? settings.MemOSMode : "skip";

        var sb = new StringBuilder();
        sb.AppendLine("# Auto-generated by HermesLauncher (contains no secrets)");
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("try {");
        sb.AppendLine($"  & '{EscapePsSingleQuoted(installScriptPath)}' `");
        sb.AppendLine($"    -InstallDir '{EscapePsSingleQuoted(settings.InstallDir)}' `");
        sb.AppendLine($"    -WorkspaceDir '{EscapePsSingleQuoted(settings.WorkspaceDir)}' `");
        sb.AppendLine($"    -InstallTailscale:${settings.InstallTailscale.ToString().ToLowerInvariant()} `");
        sb.AppendLine($"    -InstallOllama:${settings.InstallOllama.ToString().ToLowerInvariant()} `");
        sb.AppendLine($"    -InstallMemOS:${settings.InstallMemOS.ToString().ToLowerInvariant()} `");
        sb.AppendLine($"    -MemOSMode '{EscapePsSingleQuoted(memMode)}' `");
        sb.AppendLine($"    -InstallObsidian:${settings.InstallObsidian.ToString().ToLowerInvariant()} `");
        sb.AppendLine($"    -InstallObsidianSkills:${settings.InstallObsidianSkills.ToString().ToLowerInvariant()} `");
        sb.AppendLine($"    -ConfigureFirewall:${settings.ConfigureFirewall.ToString().ToLowerInvariant()} `");
        sb.AppendLine($"    -StartServices:${settings.StartServices.ToString().ToLowerInvariant()} `");
        sb.AppendLine($"    -EnableAutoStart:${settings.EnableAutoStart.ToString().ToLowerInvariant()} `");
        sb.AppendLine("    -CreateShortcuts:$true `");
        sb.AppendLine("    -OpenConnect:$false `");
        // All optional MemOS args MUST stay before the final -NoPause line.
        if (!string.IsNullOrWhiteSpace(settings.MemOSProviderUrl))
            sb.AppendLine($"    -MemOSProviderUrl '{EscapePsSingleQuoted(settings.MemOSProviderUrl)}' `");
        if (!string.IsNullOrWhiteSpace(settings.MemOSProviderKey))
            sb.AppendLine("    -MemOSProviderKey $env:HERMES_INSTALL_MEMOS_PROVIDER_KEY `");
        if (!string.IsNullOrWhiteSpace(settings.MemOSProviderModel))
            sb.AppendLine($"    -MemOSProviderModel '{EscapePsSingleQuoted(settings.MemOSProviderModel)}' `");
        sb.AppendLine("    -NoPause");

        sb.AppendLine("  if ($LASTEXITCODE -ne 0) { throw \"install-hermes.ps1 exited $LASTEXITCODE\" }");
        sb.AppendLine("} catch {");
        sb.AppendLine("  Write-Host $_ -ForegroundColor Red");
        sb.AppendLine("  exit 1");
        sb.AppendLine("}");

        var script = sb.ToString();
        // Static guard: -NoPause must be the final installer argument.
        var noPauseIdx = script.LastIndexOf("-NoPause", StringComparison.Ordinal);
        var tailAfter = script.Substring(noPauseIdx + "-NoPause".Length);
        var firstNewline = tailAfter.IndexOf('\n');
        var between = firstNewline >= 0 ? tailAfter.Substring(0, firstNewline) : tailAfter;
        if (between.Contains("`-") || between.Contains("-M", StringComparison.Ordinal))
            throw new InvalidOperationException("Launch script generation error: arguments found after -NoPause.");
        if (script.Contains("MemOSProviderKey '"))
            throw new InvalidOperationException("Launch script generation error: provider key must not be inlined.");

        File.WriteAllText(launchPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return launchPath;
    }

    private static string ExtractUninstallScript(string tempDir)
    {
        var targetPath = Path.Combine(tempDir, "uninstall-hermes.ps1");
        var utf8NoBom = new UTF8Encoding(false);

        // 1. Try disk files (base dir or dev dir)
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candDisk = Path.Combine(baseDir, "uninstall-hermes.ps1");
        if (File.Exists(candDisk))
        {
            File.WriteAllText(targetPath, File.ReadAllText(candDisk, Encoding.UTF8), utf8NoBom);
            return targetPath;
        }

        var devCand = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "uninstall-hermes.ps1"));
        if (File.Exists(devCand))
        {
            File.WriteAllText(targetPath, File.ReadAllText(devCand, Encoding.UTF8), utf8NoBom);
            return targetPath;
        }

        // 2. Embedded resource in assembly
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (name.EndsWith("uninstall-hermes.ps1", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    File.WriteAllText(targetPath, reader.ReadToEnd(), utf8NoBom);
                    return targetPath;
                }
            }
        }

        throw new FileNotFoundException("uninstall-hermes.ps1 not found in resources or on disk.");
    }

    private static string WriteUninstallLaunchScript(string tempDir, string uninstallScriptPath, string installDir)
    {
        var launchPath = Path.Combine(tempDir, $"hermes-launcher-uninstall-{Guid.NewGuid():N}.ps1");

        // Full removal: services, tasks, firewall rules, shortcuts, marker AND all user data.
        var sb = new StringBuilder();
        sb.AppendLine("# Auto-generated by HermesLauncher (full uninstall, isolated temp)");
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("$global:LASTEXITCODE = 0");
        sb.AppendLine("try {");
        sb.AppendLine($"  & '{EscapePsSingleQuoted(uninstallScriptPath)}' `");
        sb.AppendLine($"    -InstallDir '{EscapePsSingleQuoted(installDir)}' `");
        sb.AppendLine("    -RemoveAllData `");
        sb.AppendLine("    -RemoveMemOS `");
        sb.AppendLine("    -RemoveObsidianSkills");
        sb.AppendLine("  if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) { throw \"uninstall-hermes.ps1 exited $LASTEXITCODE\" }");
        sb.AppendLine("} catch {");
        sb.AppendLine("  Write-Host $_ -ForegroundColor Red");
        sb.AppendLine("  exit 1");
        sb.AppendLine("}");
        sb.AppendLine("exit 0");

        File.WriteAllText(launchPath, sb.ToString(), new UTF8Encoding(false));
        return launchPath;
    }

    private async Task PostUninstallCleanupAsync(InstallOperation op)
    {
        RaiseStep(op, "purge-files", "Verifying complete removal of Hermes files...", 0.98);
        RaiseLog(op, "[UNINSTALL] Finalizing file system cleanup...");

        // Wait briefly for all process handles, file notifications, and child processes to finish closing
        await Task.Delay(600);

        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(op.Settings.InstallDir))
            targets.Add(Path.GetFullPath(op.Settings.InstallDir));

        targets.Add(Path.GetFullPath(HermesConfigService.DefaultHermesHome));

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            targets.Add(Path.GetFullPath(Path.Combine(userProfile, ".hermes")));
        }

        foreach (var dir in targets)
        {
            if (Directory.Exists(dir))
            {
                RaiseLog(op, $"[UNINSTALL] Removing residual directory: {dir}");
                var deleted = ForceDeleteDirectoryWithRetry(dir, 5, 400);
                if (deleted)
                {
                    RaiseLog(op, $"[UNINSTALL] Successfully removed: {dir}");
                }
                else
                {
                    RaiseLog(op, $"[UNINSTALL] WARNING: Could not fully remove {dir} (files locked by another process)", LogSeverity.Warning);
                }
            }
        }

        try
        {
            if (File.Exists(ProcessOwnershipRegistry.RegistryPath))
                File.Delete(ProcessOwnershipRegistry.RegistryPath);
        }
        catch { }
    }

    private static bool ForceDeleteDirectoryWithRetry(string directoryPath, int maxRetries = 5, int delayMs = 400)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            if (!Directory.Exists(directoryPath)) return true;

            try
            {
                ClearAttributesRecursive(new DirectoryInfo(directoryPath));
                Directory.Delete(directoryPath, recursive: true);
            }
            catch
            {
                Thread.Sleep(delayMs);
            }

            if (!Directory.Exists(directoryPath)) return true;
        }

        if (Directory.Exists(directoryPath))
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c rmdir /s /q \"{directoryPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                p?.WaitForExit(3000);
            }
            catch { }
        }

        return !Directory.Exists(directoryPath);
    }

    private static void ClearAttributesRecursive(DirectoryInfo directory)
    {
        try
        {
            directory.Attributes = FileAttributes.Normal;
            foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    file.Attributes = FileAttributes.Normal;
                }
                catch { }
            }
            foreach (var sub in directory.EnumerateDirectories("*", SearchOption.AllDirectories))
            {
                try
                {
                    sub.Attributes = FileAttributes.Normal;
                }
                catch { }
            }
        }
        catch { }
    }

    private static string EscapePsSingleQuoted(string value)
    {
        // PowerShell single-quoted literals only require doubling of the quote char;
        // strip CR/LF to prevent line-continuation breakout.
        var cleaned = value.Replace("\r", "").Replace("\n", "");
        return cleaned.Replace("'", "''");
    }

    private static string EnsureInstallerFilesExtracted()
    {
        var targetDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "hermes",
            "embedded-installer");
        Directory.CreateDirectory(targetDir);
        Directory.CreateDirectory(Path.Combine(targetDir, "lib"));

        var utf8NoBom = new UTF8Encoding(false);

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        CopyScriptTree(baseDir, targetDir, utf8NoBom);

        var asm = Assembly.GetExecutingAssembly();
        foreach (var resourceName in asm.GetManifestResourceNames())
        {
            if (!resourceName.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                continue;

            var relativePath = ResolveEmbeddedRelativePath(resourceName);
            var outPath = Path.Combine(targetDir, relativePath);
            var outDir = Path.GetDirectoryName(outPath)!;
            Directory.CreateDirectory(outDir);

            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null) continue;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd();
            File.WriteAllText(outPath, text, utf8NoBom);
        }

        var devInstaller = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "install-hermes.ps1"));
        if (File.Exists(devInstaller))
        {
            var devDir = Path.GetDirectoryName(devInstaller)!;
            CopyScriptTree(devDir, targetDir, utf8NoBom);
        }

        var entry = Path.Combine(targetDir, "install-hermes.ps1");
        var lib = Path.Combine(targetDir, "lib", "InstallComponents.ps1");
        if (!File.Exists(entry))
            throw new FileNotFoundException("install-hermes.ps1 not found after extract.", entry);
        if (!File.Exists(lib))
            throw new FileNotFoundException("lib\\InstallComponents.ps1 not found after extract.", lib);

        return entry;
    }

    private static string ResolveEmbeddedRelativePath(string resourceName)
    {
        if (resourceName.Contains('\\', StringComparison.Ordinal))
            return resourceName.Replace('\\', Path.DirectorySeparatorChar);

        if (resourceName.Contains("InstallComponents", StringComparison.OrdinalIgnoreCase))
            return Path.Combine("lib", "InstallComponents.ps1");

        if (resourceName.StartsWith("lib.", StringComparison.OrdinalIgnoreCase))
            return Path.Combine("lib", Path.GetFileName(resourceName));

        return Path.GetFileName(resourceName);
    }

    private static void CopyScriptTree(string sourceDir, string targetDir, UTF8Encoding encoding)
    {
        var main = Path.Combine(sourceDir, "install-hermes.ps1");
        if (File.Exists(main))
        {
            File.WriteAllText(
                Path.Combine(targetDir, "install-hermes.ps1"),
                File.ReadAllText(main, Encoding.UTF8),
                encoding);
        }

        var lib = Path.Combine(sourceDir, "lib", "InstallComponents.ps1");
        if (File.Exists(lib))
        {
            var libTarget = Path.Combine(targetDir, "lib", "InstallComponents.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(libTarget)!);
            File.WriteAllText(libTarget, File.ReadAllText(lib, Encoding.UTF8), encoding);
        }
    }

    private sealed class InstallOperation : IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public InstallSettings Settings { get; }
        public bool IsCleanReinstall { get; }
        public bool IsUninstall { get; }
        public CancellationTokenSource Cts { get; }
        public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;
        public string? LaunchScriptPath { get; set; }
        public string? TempDirectoryToClean { get; set; }
        public OperationResult? Result { get; set; }
        public volatile bool CancellingRequested;
        public string[] Secrets { get; }

        private Process? _process;
        public Process? Process => _process;

        public InstallOperation(InstallSettings settings, bool isCleanReinstall, bool isUninstall, CancellationToken linked)
        {
            Settings = settings;
            IsCleanReinstall = isCleanReinstall;
            IsUninstall = isUninstall;
            Cts = CancellationTokenSource.CreateLinkedTokenSource(linked);
            Secrets = string.IsNullOrWhiteSpace(settings.MemOSProviderKey)
                ? Array.Empty<string>()
                : new[] { settings.MemOSProviderKey };
        }

        public void AttachProcess(Process p) => _process = p;

        public void Dispose()
        {
            try { Cts.Dispose(); } catch { }
        }
    }
}
