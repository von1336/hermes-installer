using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace HermesLauncher.Services;

/// <summary>
/// TEMPORARY debug feature: builds a sanitized failure report and opens a
/// pre-filled GitHub issue (full report also copied to clipboard).
/// GitHub Issues cannot be created without user auth, so the final "Submit"
/// click stays with the user. Remove this feature after stabilization.
/// </summary>
public static class ErrorReportService
{
    private const string IssuesNewUrl = "https://github.com/von1336/hermes-installer/issues/new";
    private const int MaxUrlLength = 7000;

    private static readonly Regex SecretAssignment = new(
        @"([A-Z0-9_]*(?:KEY|TOKEN|SECRET|PASSWORD|PASS)[A-Z0-9_]*\s*[=:]\s*)\S+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static void SendFailureReport(
        string operationLabel,
        int exitCode,
        string redactedMessage,
        string installDir,
        string consoleTail,
        IEnumerable<string?> extraSecrets)
    {
        try
        {
            var body = BuildBody(operationLabel, exitCode, redactedMessage, installDir, consoleTail, extraSecrets);
            var title = $"[auto] {operationLabel} failed (exit {exitCode})";
            TryCopyToClipboard(body);
            OpenIssue(title, body);
        }
        catch
        {
            // Reporting must never break the launcher itself.
        }
    }

    private static string BuildBody(
        string operationLabel,
        int exitCode,
        string redactedMessage,
        string installDir,
        string consoleTail,
        IEnumerable<string?> extraSecrets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("> Auto-generated error report from Hermes Launcher (temporary debug feature).");
        sb.AppendLine("> Full report is in the clipboard — paste it here if sections below are truncated.");
        sb.AppendLine();
        sb.AppendLine($"**Launcher:** v{UpdateService.CurrentVersionText}");
        sb.AppendLine($"**OS:** {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "x64" : "x86")})");
        sb.AppendLine($"**Time (UTC):** {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"**Operation:** {operationLabel}");
        sb.AppendLine($"**Exit code:** {exitCode}");
        sb.AppendLine();

        // Most diagnostic value first: the URL-safe body shrink drops trailing
        // sections, so gateway logs and the installer error report go first.
        AppendSection(sb, "install-error.txt", Sanitize(ReadTail(FindErrorReportPath(installDir), 3000), extraSecrets), 3000);

        try
        {
            // Invoke-HermesGatewayCommand writes gateway-<action>.log / -err.log
            // into the hermes home ROOT (not the logs\ subfolder).
            var gatewayLogs = Directory.GetFiles(HermesHome(), "gateway-*")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(4);
            foreach (var log in gatewayLogs)
            {
                AppendSection(sb, Path.GetFileName(log), Sanitize(ReadTail(log, 2000), extraSecrets), 2000);
            }
        }
        catch { }

        AppendSection(sb, "Error", Sanitize(redactedMessage, extraSecrets), 800);
        AppendSection(sb, "hermes-agent-install.log",
            Sanitize(ReadTail(Path.Combine(HermesHome(), "hermes-agent-install.log"), 1500), extraSecrets), 1500);
        AppendSection(sb, "Console tail", Sanitize(consoleTail, extraSecrets), 1500);

        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string title, string content, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        if (content.Length > maxChars)
            content = "...(truncated)\n" + content.Substring(content.Length - maxChars);
        sb.AppendLine($"### {title}");
        sb.AppendLine("```");
        sb.AppendLine(content.Trim());
        sb.AppendLine("```");
        sb.AppendLine();
    }

    private static void OpenIssue(string title, string body)
    {
        var url = IssuesNewUrl
            + "?labels=installer-error"
            + "&title=" + Uri.EscapeDataString(title)
            + "&body=" + Uri.EscapeDataString(body);

        // Shrink the body until the URL fits into a safe length.
        while (url.Length > MaxUrlLength && body.Length > 600)
        {
            body = body.Substring(0, body.Length / 2)
                + "\n... (truncated — full report is in the clipboard, paste it here)";
            url = IssuesNewUrl
                + "?labels=installer-error"
                + "&title=" + Uri.EscapeDataString(title)
                + "&body=" + Uri.EscapeDataString(body);
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static void TryCopyToClipboard(string text)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch
            {
                System.Threading.Thread.Sleep(150);
            }
        }
    }

    private static string Sanitize(string? text, IEnumerable<string?> extraSecrets)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = text;

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
            s = s.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);

        foreach (var secret in extraSecrets ?? Enumerable.Empty<string?>())
        {
            if (!string.IsNullOrEmpty(secret))
                s = s.Replace(secret, "****", StringComparison.Ordinal);
        }

        return SecretAssignment.Replace(s, "$1****");
    }

    private static string ReadTail(string? path, int maxChars)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
            var text = File.ReadAllText(path);
            return text.Length > maxChars ? text.Substring(text.Length - maxChars) : text;
        }
        catch { return ""; }
    }

    private static string FindErrorReportPath(string installDir)
    {
        var inInstallDir = Path.Combine(installDir, "install-error.txt");
        if (File.Exists(inInstallDir)) return inInstallDir;
        return Path.Combine(HermesHome(), "install-error.txt");
    }

    private static string HermesHome() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hermes");
}
