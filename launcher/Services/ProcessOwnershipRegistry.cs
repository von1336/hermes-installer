using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HermesLauncher.Services;

public sealed class HermesProcessRecord
{
    [JsonPropertyName("pid")] public int Pid { get; set; }
    [JsonPropertyName("name")] public string ProcessName { get; set; } = "";
    [JsonPropertyName("exe")] public string? ExecutablePath { get; set; }
    [JsonPropertyName("cmd")] public string? CommandLine { get; set; }
    [JsonPropertyName("cwd")] public string? WorkingDirectory { get; set; }
    [JsonPropertyName("install")] public string ExpectedInstallPath { get; set; } = "";
    [JsonPropertyName("workspace")] public string ExpectedWorkspacePath { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("at")] public DateTimeOffset RegisteredAtUtc { get; set; }
}

public static class ProcessOwnershipRegistry
{
    private static readonly object Sync = new();

    public static string RegistryPath =>
        Path.Combine(HermesConfigService.DefaultHermesHome, "processes.json");

    public static void Register(int pid, string? exePath, string? commandLine, string? workingDirectory,
        string expectedInstallPath, string expectedWorkspacePath, string source)
    {
        lock (Sync)
        {
            var records = LoadLive(expectedInstallPath, expectedWorkspacePath);
            records.RemoveAll(r => r.Pid == pid);
            records.Add(new HermesProcessRecord
            {
                Pid = pid,
                ProcessName = SafeProcessName(pid),
                ExecutablePath = exePath,
                CommandLine = commandLine,
                WorkingDirectory = workingDirectory,
                ExpectedInstallPath = expectedInstallPath,
                ExpectedWorkspacePath = expectedWorkspacePath,
                Source = source,
                RegisteredAtUtc = DateTimeOffset.UtcNow
            });
            Save(records);
        }
    }

    public static void Unregister(int pid)
    {
        lock (Sync)
        {
            var records = LoadRaw();
            if (records.RemoveAll(r => r.Pid == pid) > 0) Save(records);
        }
    }

    public static List<HermesProcessRecord> LoadLive(string installDir, string workspaceDir)
    {
        var records = LoadRaw();
        records.RemoveAll(r => !IsAlive(r.Pid));
        return records;
    }

    public static bool TryConfirmOwnership(int pid, string installDir, string workspaceDir, out string reason)
    {
        reason = "process identity could not be confirmed";
        var info = QueryProcessInfo(pid);
        if (info == null)
        {
            reason = $"pid {pid} not running or not queryable";
            return false;
        }

        var installNorm = NormalizePath(installDir);
        var workspaceNorm = NormalizePath(workspaceDir);
        var name = (info.ProcessName ?? "").ToLowerInvariant();
        var exe = NormalizePath(info.ExecutablePath);
        var cmd = info.CommandLine ?? "";

        if (name == "ollama")
        {
            reason = "ollama is a third-party process and is never force-stopped by Hermes";
            return false;
        }

        if (!string.IsNullOrEmpty(installNorm) && exe.StartsWith(installNorm, StringComparison.OrdinalIgnoreCase))
        {
            reason = "executable path is inside Hermes install dir";
            return true;
        }

        if (!string.IsNullOrEmpty(workspaceNorm) &&
            (cmd.Contains(workspaceNorm, StringComparison.OrdinalIgnoreCase) ||
             exe.StartsWith(workspaceNorm, StringComparison.OrdinalIgnoreCase)))
        {
            reason = "command line / executable references Hermes workspace dir";
            return true;
        }

        if (!string.IsNullOrEmpty(installNorm) &&
            cmd.Contains(installNorm, StringComparison.OrdinalIgnoreCase) &&
            (name is "node" or "pnpm" or "cmd" or "hermes" or "powershell"))
        {
            reason = "command line references Hermes install dir";
            return true;
        }

        var record = LoadRaw().FirstOrDefault(r => r.Pid == pid);
        if (record != null &&
            !string.IsNullOrEmpty(record.ExecutablePath) &&
            string.Equals(NormalizePath(record.ExecutablePath), exe, StringComparison.OrdinalIgnoreCase))
        {
            reason = "pid matches Hermes process registry entry";
            return true;
        }

        reason = $"pid {pid} ({name}) has no Hermes path/ownership evidence";
        return false;
    }

    public static string StopOwnedProcessesOnPort(int port, string installDir, string workspaceDir)
    {
        var diag = new StringBuilder();
        var owners = QueryPortOwners(port);
        if (owners.Count == 0)
        {
            diag.Append($"port {port}: no owners");
            return diag.ToString();
        }

        foreach (var pid in owners)
        {
            if (!TryConfirmOwnership(pid, installDir, workspaceDir, out var reason))
            {
                diag.AppendLine($"port {port}: SKIP pid={pid} ({reason})");
                continue;
            }
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill(true);
                Unregister(pid);
                diag.AppendLine($"port {port}: stopped pid={pid} ({reason})");
            }
            catch (Exception ex)
            {
                diag.AppendLine($"port {port}: stop failed pid={pid}: {ex.Message}");
            }
        }
        return diag.ToString().TrimEnd();
    }

    public static int StopAllOwned(string installDir, string workspaceDir, StringBuilder? diag = null)
    {
        var stopped = 0;
        foreach (var record in LoadLive(installDir, workspaceDir))
        {
            if (!TryConfirmOwnership(record.Pid, installDir, workspaceDir, out var reason))
            {
                diag?.AppendLine($"SKIP pid={record.Pid} ({reason})");
                continue;
            }
            try
            {
                using var p = Process.GetProcessById(record.Pid);
                p.Kill(true);
                Unregister(record.Pid);
                stopped++;
                diag?.AppendLine($"stopped pid={record.Pid} ({record.ProcessName})");
            }
            catch (Exception ex)
            {
                diag?.AppendLine($"stop failed pid={record.Pid}: {ex.Message}");
            }
        }
        return stopped;
    }

    private static List<HermesProcessRecord> LoadRaw()
    {
        try
        {
            if (!File.Exists(RegistryPath)) return new List<HermesProcessRecord>();
            var json = File.ReadAllText(RegistryPath);
            return JsonSerializer.Deserialize<List<HermesProcessRecord>>(json) ?? new List<HermesProcessRecord>();
        }
        catch
        {
            return new List<HermesProcessRecord>();
        }
    }

    private static void Save(List<HermesProcessRecord> records)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath)!);
            File.WriteAllText(RegistryPath, JsonSerializer.Serialize(records), new UTF8Encoding(false));
        }
        catch { }
    }

    private static bool IsAlive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch { return false; }
    }

    private static string SafeProcessName(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.ProcessName; }
        catch { return ""; }
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.GetFullPath(path).TrimEnd('\\', '/'); }
        catch { return path.Trim().TrimEnd('\\', '/'); }
    }

    private sealed class CimProcessInfo
    {
        [JsonPropertyName("ProcessId")] public int ProcessId { get; set; }
        [JsonPropertyName("Name")] public string? ProcessName { get; set; }
        [JsonPropertyName("ExecutablePath")] public string? ExecutablePath { get; set; }
        [JsonPropertyName("CommandLine")] public string? CommandLine { get; set; }
    }

    private static CimProcessInfo? QueryProcessInfo(int pid)
    {
        var json = RunPowerShellJson(
            $"Get-CimInstance Win32_Process -Filter \"ProcessId={pid}\" -ErrorAction SilentlyContinue | " +
            "Select-Object ProcessId,Name,ExecutablePath,CommandLine | ConvertTo-Json -Compress");
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<CimProcessInfo>(json); }
        catch { return null; }
    }

    private static List<int> QueryPortOwners(int port)
    {
        var json = RunPowerShellJson(
            $"@(Get-NetTCPConnection -LocalPort {port} -ErrorAction SilentlyContinue | " +
            "Select-Object -ExpandProperty OwningProcess -Unique) | ConvertTo-Json -Compress");
        if (string.IsNullOrWhiteSpace(json)) return new List<int>();
        try
        {
            if (json.TrimStart().StartsWith("["))
                return JsonSerializer.Deserialize<List<int>>(json) ?? new List<int>();
            if (int.TryParse(json.Trim(), out var single)) return new List<int> { single };
        }
        catch { }
        return new List<int>();
    }

    private static string RunPowerShellJson(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(command);
            using var p = Process.Start(psi);
            if (p == null) return "";
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(8000);
            return p.ExitCode == 0 ? output.Trim() : "";
        }
        catch { return ""; }
    }
}
