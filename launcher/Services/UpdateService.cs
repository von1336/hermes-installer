using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HermesLauncher.Services;

public sealed class UpdateInfo
{
    public bool IsNewer { get; set; }
    public string LatestVersion { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string? Error { get; set; }
}

/// <summary>
/// Checks GitHub Releases for a newer HermesLauncher.exe, downloads it and
/// swaps the running binary via a small cmd updater script (the EXE cannot
/// overwrite itself while running).
/// </summary>
public sealed class UpdateService
{
    private const string ReleasesApi = "https://api.github.com/repos/von1336/hermes-installer/releases/latest";
    private const string AssetName = "HermesLauncher.exe";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HermesLauncher-UpdateCheck");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.Timeout = TimeSpan.FromSeconds(25);
        return client;
    }

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v ?? new Version(0, 0, 0, 0);
        }
    }

    public static string CurrentVersionText
    {
        get
        {
            var v = CurrentVersion;
            return v.Revision > 0 ? v.ToString(4) : v.ToString(3);
        }
    }

    public async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.GetAsync(ReleasesApi, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return new UpdateInfo { Error = $"Update check failed: HTTP {(int)resp.StatusCode}" };
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
            var url = "";
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                        break;
                    }
                }
            }

            var latest = ParseVersion(tag);
            return new UpdateInfo
            {
                IsNewer = latest != null && latest > CurrentVersion && url.Length > 0,
                LatestVersion = tag.TrimStart('v', 'V'),
                DownloadUrl = url
            };
        }
        catch (Exception ex)
        {
            return new UpdateInfo { Error = "Update check failed: " + ex.Message };
        }
    }

    public async Task DownloadAsync(UpdateInfo info, string targetPath, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1;
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        await using var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long received = 0;
        int read;
        var lastPct = -1;
        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
            received += read;
            if (total > 0 && progress != null)
            {
                var pct = (int)(received * 100 / total);
                if (pct != lastPct) { lastPct = pct; progress.Report(pct); }
            }
        }
    }

    /// <summary>
    /// Spawns a detached cmd updater that waits for this process to exit,
    /// replaces the EXE and relaunches it. Call Application.Shutdown() right after.
    /// </summary>
    public void ApplyAndRestart(string downloadedExePath)
    {
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot resolve current exe path");
        var pid = Process.GetCurrentProcess().Id;
        var updaterPath = Path.Combine(Path.GetTempPath(), "hermes-launcher-update.cmd");

        var script = string.Join("\r\n", new[]
        {
            "@echo off",
            ":wait",
            $"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul",
            "if not errorlevel 1 ( timeout /t 1 /nobreak >nul & goto wait )",
            $"copy /y \"{downloadedExePath}\" \"{currentExe}\" >nul",
            "if errorlevel 1 exit /b 1",
            $"start \"\" \"{currentExe}\"",
            "del \"%~f0\""
        });
        File.WriteAllText(updaterPath, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c \"" + updaterPath + "\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static Version? ParseVersion(string tag)
    {
        var s = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? v : null;
    }
}
