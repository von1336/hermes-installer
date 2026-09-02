using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using HermesLauncher.Models;

namespace HermesLauncher.Services;

public class HermesConfigService
{
    public static string DefaultHermesHome =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hermes");

    public static string DefaultWorkspaceDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "hermes-workspace");

    public static string GetHermesEnvPath(string? customHome = null)
    {
        var home = string.IsNullOrWhiteSpace(customHome) ? DefaultHermesHome : customHome;
        return Path.Combine(home, ".env");
    }

    public static string GetWorkspaceEnvPath(string? customWorkspace = null)
    {
        var ws = string.IsNullOrWhiteSpace(customWorkspace) ? DefaultWorkspaceDir : customWorkspace;
        return Path.Combine(ws, ".env");
    }

    public static string GetCompletionMarkerPath(string? customHome = null) =>
        Path.Combine(string.IsNullOrWhiteSpace(customHome) ? DefaultHermesHome : customHome, "install-complete.json");

    public static Dictionary<string, string> ReadDotEnv(string filePath)
    {
        return TryReadDotEnv(filePath, out var dict, out _) ? dict : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryReadDotEnv(string filePath, out Dictionary<string, string> values, out string? error)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        error = null;
        if (!File.Exists(filePath)) return true;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, detectEncodingFromByteOrderMarks: true);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#") || !trimmed.Contains('='))
                    continue;

                var parts = trimmed.Split('=', 2);
                var key = parts[0].Trim();
                if (key.Length == 0) continue; // tolerate corrupted lines instead of crashing
                var val = parts[1].Trim();
                if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
                    val = val.Substring(1, val.Length - 2);
                else if (val.StartsWith("'") && val.EndsWith("'") && val.Length >= 2)
                    val = val.Substring(1, val.Length - 2);

                values[key] = val;
            }
            return true;
        }
        catch (IOException ex) { error = $".env is locked or unreadable: {ex.Message}"; return false; }
        catch (UnauthorizedAccessException ex) { error = $".env access denied: {ex.Message}"; return false; }
        catch (Exception ex) { error = $".env parse failed: {ex.Message}"; return false; }
    }

    public static void WriteDotEnv(string filePath, Dictionary<string, string> values)
    {
        var existing = ReadDotEnv(filePath);
        foreach (var kvp in values)
        {
            existing[kvp.Key] = kvp.Value;
        }

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var lines = existing.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}").ToArray();
        // Atomic-ish write: temp file + replace, UTF-8 no BOM.
        var tmp = filePath + ".tmp";
        try
        {
            File.WriteAllLines(tmp, lines, new UTF8Encoding(false));
            if (File.Exists(filePath))
                File.Replace(tmp, filePath, null);
            else
                File.Move(tmp, filePath);
        }
        catch (IOException ex)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw new InvalidOperationException($".env is locked by another process: {filePath}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw new InvalidOperationException($"No write permission for .env: {filePath}", ex);
        }
    }

    public static string MaskSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "<empty>";
        if (value.Length <= 4) return "****";
        return "****" + value.Substring(value.Length - 4);
    }

    public static string? DetectTailscaleIp()
    {
        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                var name = iface.Name.ToLowerInvariant();
                var desc = iface.Description.ToLowerInvariant();
                if (name.Contains("tailscale") || desc.Contains("tailscale"))
                {
                    var props = iface.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            var ipStr = addr.Address.ToString();
                            if (ipStr.StartsWith("100.")) return ipStr;
                        }
                    }
                }
            }

            // Fallback: any 100.64.0.0/10 CGNAT address
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                var props = iface.GetIPProperties();
                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var ipStr = addr.Address.ToString();
                        if (ipStr.StartsWith("100.")) return ipStr;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    public static string DetectLanIp()
    {
        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up ||
                    iface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var props = iface.GetIPProperties();
                if (props.GatewayAddresses.Count == 0) continue;

                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var ipStr = addr.Address.ToString();
                        if (!ipStr.StartsWith("169.254.") && !ipStr.StartsWith("127."))
                            return ipStr;
                    }
                }
            }
        }
        catch { }

        return "127.0.0.1";
    }

    public static bool IsHermesInstalled(string? customHome = null, string? customWorkspace = null)
    {
        var home = string.IsNullOrWhiteSpace(customHome) ? DefaultHermesHome : customHome!;
        var ws = string.IsNullOrWhiteSpace(customWorkspace) ? DefaultWorkspaceDir : customWorkspace!;

        // 1. Completion marker written only after a fully successful install.
        if (!TryReadCompletionMarker(home, out _)) return false;

        // 2. Both .env files.
        if (!File.Exists(GetHermesEnvPath(home)) || !File.Exists(GetWorkspaceEnvPath(ws))) return false;

        // 3. Valid install manifest.
        var manifest = Path.Combine(home, "install-meta.json");
        if (!IsValidJsonFile(manifest)) return false;

        // 4. Workspace artifacts.
        if (!Directory.Exists(ws)) return false;
        if (!File.Exists(Path.Combine(ws, "package.json"))) return false;
        if (!Directory.Exists(Path.Combine(ws, "node_modules")) &&
            !Directory.Exists(Path.Combine(ws, ".git"))) return false;

        // 5. Gateway (hermes CLI) resolvable.
        if (!IsGatewayInstalled(home)) return false;

        return true;
    }

    public static bool TryReadCompletionMarker(string? customHome, out Dictionary<string, System.Text.Json.JsonElement> marker)
    {
        marker = new Dictionary<string, System.Text.Json.JsonElement>();
        var path = GetCompletionMarkerPath(customHome);
        if (!File.Exists(path)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty("completedAt", out var completedAt)) return false;
            if (completedAt.ValueKind != System.Text.Json.JsonValueKind.String) return false;
            if (!doc.RootElement.TryGetProperty("version", out _)) return false;
            foreach (var prop in doc.RootElement.EnumerateObject())
                marker[prop.Name] = prop.Value.Clone();
            return true;
        }
        catch { return false; }
    }

    public static void InvalidateCompletionMarker(string? customHome = null)
    {
        try { File.Delete(GetCompletionMarkerPath(customHome)); } catch { }
    }

    private static bool IsValidJsonFile(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object;
        }
        catch { return false; }
    }

    private static bool IsGatewayInstalled(string hermesHome)
    {
        // hermes CLI in install bin
        var binDir = Path.Combine(hermesHome, "bin");
        if (Directory.Exists(binDir))
        {
            foreach (var ext in new[] { ".exe", ".cmd", ".ps1", ".bat" })
                if (File.Exists(Path.Combine(binDir, "hermes" + ext))) return true;
        }
        // hermes CLI on PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                foreach (var ext in new[] { ".exe", ".cmd", ".ps1", ".bat" })
                    if (File.Exists(Path.Combine(dir.Trim(), "hermes" + ext))) return true;
            }
            catch { }
        }
        // npm global shim
        var npmGlobal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
        if (File.Exists(Path.Combine(npmGlobal, "hermes.cmd")) || File.Exists(Path.Combine(npmGlobal, "hermes.ps1")))
            return true;
        return false;
    }
}
