using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using HermesLauncher.Models;

namespace HermesLauncher.Services;

public class SystemDiagnosticsService
{
    public ObservableCollection<SystemPrerequisiteItem> Prerequisites { get; } = new();

    public SystemDiagnosticsService()
    {
        Prerequisites.Add(new SystemPrerequisiteItem { Name = "Node.js", Category = "JavaScript Runtime", IsOptional = false });
        Prerequisites.Add(new SystemPrerequisiteItem { Name = "pnpm", Category = "Package Manager", IsOptional = false });
        Prerequisites.Add(new SystemPrerequisiteItem { Name = "Git", Category = "Version Control", IsOptional = false });
        Prerequisites.Add(new SystemPrerequisiteItem { Name = "Python 3.10+", Category = "AI & Scripts Runtime", IsOptional = false });
        Prerequisites.Add(new SystemPrerequisiteItem { Name = "Tailscale", Category = "Encrypted Mesh VPN", IsOptional = true });
        Prerequisites.Add(new SystemPrerequisiteItem { Name = "Ollama", Category = "Local LLM Inference", IsOptional = true });
        Prerequisites.Add(new SystemPrerequisiteItem { Name = "Obsidian", Category = "Knowledge Vault", IsOptional = true });
        Prerequisites.Add(new SystemPrerequisiteItem { Name = "Hermes Stack", Category = "Core Workspace", IsOptional = false });
    }

    public async Task RunDiagnosticsAsync(string? installDir = null, string? workspaceDir = null)
    {
        await Task.Run(() =>
        {
            // 1. Node.js
            var nodeOut = RunCommandCapture("node", "-v");
            var nodeItem = Prerequisites[0];
            if (!string.IsNullOrEmpty(nodeOut) && nodeOut.StartsWith("v"))
            {
                nodeItem.IsInstalled = true;
                nodeItem.VersionOrStatus = nodeOut.Trim();
                nodeItem.Recommendation = "Ready";
            }
            else
            {
                nodeItem.IsInstalled = false;
                nodeItem.VersionOrStatus = "Not Detected";
                nodeItem.Recommendation = "Install via: winget install OpenJS.NodeJS";
            }

            // 2. pnpm
            var pnpmOut = RunCommandCapture("pnpm", "-v");
            var pnpmItem = Prerequisites[1];
            if (!string.IsNullOrEmpty(pnpmOut) && char.IsDigit(pnpmOut[0]))
            {
                pnpmItem.IsInstalled = true;
                pnpmItem.VersionOrStatus = $"v{pnpmOut.Trim()}";
                pnpmItem.Recommendation = "Ready";
            }
            else
            {
                pnpmItem.IsInstalled = false;
                pnpmItem.VersionOrStatus = "Not Detected";
                pnpmItem.Recommendation = "Install via: npm install -g pnpm";
            }

            // 3. Git
            var gitOut = RunCommandCapture("git", "--version");
            var gitItem = Prerequisites[2];
            if (!string.IsNullOrEmpty(gitOut) && gitOut.Contains("git version"))
            {
                gitItem.IsInstalled = true;
                gitItem.VersionOrStatus = gitOut.Replace("git version", "").Trim();
                gitItem.Recommendation = "Ready";
            }
            else
            {
                gitItem.IsInstalled = false;
                gitItem.VersionOrStatus = "Not Detected";
                gitItem.Recommendation = "Install via: winget install Git.Git";
            }

            // 4. Python
            var pyOut = RunCommandCapture("python", "--version");
            var pyItem = Prerequisites[3];
            if (!string.IsNullOrEmpty(pyOut) && pyOut.Contains("Python"))
            {
                pyItem.IsInstalled = true;
                pyItem.VersionOrStatus = pyOut.Trim();
                pyItem.Recommendation = "Ready";
            }
            else
            {
                pyItem.IsInstalled = false;
                pyItem.VersionOrStatus = "Not Detected";
                pyItem.Recommendation = "Install via: winget install Python.Python.3.11";
            }

            // 5. Tailscale
            var tsIp = HermesConfigService.DetectTailscaleIp();
            var tsItem = Prerequisites[4];
            if (!string.IsNullOrEmpty(tsIp))
            {
                tsItem.IsInstalled = true;
                tsItem.VersionOrStatus = $"Active ({tsIp})";
                tsItem.Recommendation = "Connected to Tailnet";
            }
            else
            {
                var tsCmd = RunCommandCapture("tailscale", "version");
                if (!string.IsNullOrEmpty(tsCmd))
                {
                    tsItem.IsInstalled = true;
                    tsItem.VersionOrStatus = "Installed (Needs Login)";
                    tsItem.Recommendation = "Run: tailscale login";
                }
                else
                {
                    tsItem.IsInstalled = false;
                    tsItem.VersionOrStatus = "Not Installed";
                    tsItem.Recommendation = "Recommended for mobile connection";
                }
            }

            // 6. Ollama
            var olOut = RunCommandCapture("ollama", "--version");
            var olItem = Prerequisites[5];
            if (!string.IsNullOrEmpty(olOut))
            {
                olItem.IsInstalled = true;
                olItem.VersionOrStatus = olOut.Trim();
                olItem.Recommendation = "Ready for local models";
            }
            else
            {
                olItem.IsInstalled = false;
                olItem.VersionOrStatus = "Not Installed";
                olItem.Recommendation = "Optional for offline GPU models";
            }

            // 7. Obsidian
            var obsItem = Prerequisites[6];
            var localObs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Obsidian", "Obsidian.exe");
            var progObs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Obsidian", "Obsidian.exe");
            if (File.Exists(localObs) || File.Exists(progObs))
            {
                obsItem.IsInstalled = true;
                obsItem.VersionOrStatus = "Installed";
                obsItem.Recommendation = "Ready for memory vault";
            }
            else
            {
                obsItem.IsInstalled = false;
                obsItem.VersionOrStatus = "Not Installed";
                obsItem.Recommendation = "Optional for Obsidian Skills";
            }

            // 8. Hermes Stack
            var hermesItem = Prerequisites[7];
            var isHermes = HermesConfigService.IsHermesInstalled(installDir, workspaceDir);
            if (isHermes)
            {
                hermesItem.IsInstalled = true;
                hermesItem.VersionOrStatus = "Configured";
                hermesItem.Recommendation = "Ready";
            }
            else
            {
                hermesItem.IsInstalled = false;
                hermesItem.VersionOrStatus = "Not Deployed Yet";
                hermesItem.Recommendation = "Use 'Start Full Deployment' below";
            }
        });
    }

    private static string? RunCommandCapture(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            var outStr = p.StandardOutput.ReadToEnd();
            var errStr = p.StandardError.ReadToEnd();
            p.WaitForExit(3000);
            return !string.IsNullOrWhiteSpace(outStr) ? outStr : errStr;
        }
        catch
        {
            return null;
        }
    }
}
