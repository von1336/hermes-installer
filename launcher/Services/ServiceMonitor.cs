using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HermesLauncher.Models;

namespace HermesLauncher.Services;

public class ServiceMonitor
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2.0)
    };

    public ObservableCollection<ServiceItem> Services { get; } = new();

    public ServiceItem GatewayService { get; }
    public ServiceItem WorkspaceService { get; }
    public ServiceItem AgentDashboardService { get; }
    public ServiceItem OllamaService { get; }

    public ServiceMonitor()
    {
        GatewayService = new ServiceItem
        {
            Name = "Hermes Gateway",
            Description = "Nous inference engine & OpenAI API server",
            Port = 8642,
            ProbeUrl = "http://127.0.0.1:8642/health",
            EndpointLabel = "http://127.0.0.1:8642",
            Protocol = "HTTP/1.1",
            DetectedModel = "Nous Hermes Engine"
        };

        WorkspaceService = new ServiceItem
        {
            Name = "Hermes Workspace",
            Description = "Full stack app, chat sessions, conductor & files",
            Port = 3000,
            ProbeUrl = "http://127.0.0.1:3000/api/healthcheck",
            EndpointLabel = "http://127.0.0.1:3000",
            Protocol = "HTTP/1.1",
            DetectedModel = "TanStack / Node"
        };

        AgentDashboardService = new ServiceItem
        {
            Name = "Agent Dashboard",
            Description = "NousResearch node management & channels status",
            Port = 9119,
            ProbeUrl = "http://127.0.0.1:9119/api/status",
            EndpointLabel = "http://127.0.0.1:9119",
            Protocol = "HTTP/1.1",
            DetectedModel = "Vite / Node"
        };

        OllamaService = new ServiceItem
        {
            Name = "Ollama Local LLM",
            Description = "Host GPU/CPU local model runner",
            Port = 11434,
            ProbeUrl = "http://127.0.0.1:11434/api/tags",
            EndpointLabel = "http://127.0.0.1:11434",
            Protocol = "HTTP/1.1",
            DetectedModel = "Local Model Runner"
        };

        Services.Add(GatewayService);
        Services.Add(WorkspaceService);
        Services.Add(AgentDashboardService);
        Services.Add(OllamaService);
    }

    public async Task ProbeAllAsync()
    {
        var tasks = new[]
        {
            ProbeSingleAsync(GatewayService),
            ProbeSingleAsync(WorkspaceService),
            ProbeSingleAsync(AgentDashboardService),
            ProbeSingleAsync(OllamaService)
        };

        await Task.WhenAll(tasks);
    }

    public async Task ProbeSingleAsync(ServiceItem item)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2.0));
            var resp = await HttpClient.GetAsync(item.ProbeUrl, cts.Token);
            sw.Stop();

            item.LatencyMs = (int)sw.ElapsedMilliseconds;

            if (resp.IsSuccessStatusCode)
            {
                item.IsOnline = true;
                item.StatusText = $"ONLINE • {item.LatencyMs}ms";

                if (item == GatewayService)
                {
                    _ = ProbeGatewayModelsAsync(item);
                }
                else if (item == OllamaService)
                {
                    _ = ProbeOllamaModelsAsync(item);
                }
            }
            else
            {
                item.IsOnline = false;
                item.StatusText = $"ERROR {(int)resp.StatusCode}";
            }
        }
        catch (HttpRequestException)
        {
            item.IsOnline = false;
            item.LatencyMs = 0;
            item.StatusText = "OFFLINE";
        }
        catch (TaskCanceledException)
        {
            item.IsOnline = false;
            item.LatencyMs = 0;
            item.StatusText = "TIMEOUT";
        }
        catch (Exception ex)
        {
            item.IsOnline = false;
            item.LatencyMs = 0;
            item.StatusText = ex.Message.Length > 20 ? ex.Message.Substring(0, 20) + "..." : ex.Message;
        }
        finally
        {
            item.LastChecked = DateTime.Now.ToString("HH:mm:ss");
        }
    }

    private async Task ProbeGatewayModelsAsync(ServiceItem item)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            var json = await HttpClient.GetStringAsync("http://127.0.0.1:8642/v1/models", cts.Token);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
            {
                var firstModel = data[0].GetProperty("id").GetString();
                if (!string.IsNullOrEmpty(firstModel))
                {
                    item.DetectedModel = firstModel;
                }
            }
        }
        catch { }
    }

    private async Task ProbeOllamaModelsAsync(ServiceItem item)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            var json = await HttpClient.GetStringAsync("http://127.0.0.1:11434/api/tags", cts.Token);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("models", out var models) && models.GetArrayLength() > 0)
            {
                var count = models.GetArrayLength();
                var first = models[0].GetProperty("name").GetString();
                item.DetectedModel = count == 1 ? first ?? "Ollama" : $"{first} (+{count - 1} more)";
            }
        }
        catch { }
    }

    public async Task StartServiceAsync(ServiceItem item, string workspaceDir)
    {
        item.IsBusy = true;
        item.StatusText = "STARTING...";
        item.LastDiagnostic = "";

        try
        {
            await Task.Run(() =>
            {
                if (item == GatewayService)
                {
                    var r = RunProcessHidden("hermes", "gateway start");
                    if (!r.Success) throw new InvalidOperationException(r.Diagnostic);
                }
                else if (item == AgentDashboardService)
                {
                    var r = RunProcessHidden("hermes", "dashboard start");
                    if (!r.Success) throw new InvalidOperationException(r.Diagnostic);
                }
                else if (item == WorkspaceService)
                {
                    var ws = string.IsNullOrWhiteSpace(workspaceDir) ? HermesConfigService.DefaultWorkspaceDir : workspaceDir;
                    if (Directory.Exists(ws))
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = "/c pnpm dev",
                            WorkingDirectory = ws,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        var p = Process.Start(psi);
                        if (p != null)
                        {
                            ProcessOwnershipRegistry.Register(p.Id, null, "cmd.exe /c pnpm dev", ws,
                                HermesConfigService.DefaultHermesHome, ws, "launcher-start-workspace");
                        }
                    }
                    else
                    {
                        throw new DirectoryNotFoundException($"Workspace directory not found: {ws}");
                    }
                }
                else if (item == OllamaService)
                {
                    var r = RunProcessHidden("ollama", "serve", waitExitMs: 2000);
                    // ollama serve is a long-running daemon; a timeout while running is success.
                    if (!r.Success && !r.StillRunning) throw new InvalidOperationException(r.Diagnostic);
                }
            });

            await Task.Delay(2500);
            await ProbeSingleAsync(item);
        }
        catch (Exception ex)
        {
            item.IsOnline = false;
            item.LastDiagnostic = ex.Message;
            item.StatusText = $"START FAILED: {(ex.Message.Length > 40 ? ex.Message.Substring(0, 40) + "..." : ex.Message)}";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    public async Task StopServiceAsync(ServiceItem item, string workspaceDir = "")
    {
        item.IsBusy = true;
        item.StatusText = "STOPPING...";
        item.LastDiagnostic = "";

        try
        {
            await Task.Run(() =>
            {
                if (item == GatewayService)
                {
                    var r = RunProcessHidden("hermes", "gateway stop");
                    if (!r.Success) item.LastDiagnostic = r.Diagnostic;
                }
                else if (item == AgentDashboardService)
                {
                    var r = RunProcessHidden("hermes", "dashboard stop");
                    if (!r.Success) item.LastDiagnostic = r.Diagnostic;
                }
                else
                {
                    var diag = StopOwnedProcessOnPort(item.Port, workspaceDir);
                    item.LastDiagnostic = diag;
                }
            });

            await Task.Delay(1800);
            await ProbeSingleAsync(item);
        }
        catch (Exception ex)
        {
            item.LastDiagnostic = ex.Message;
            item.StatusText = "STOP FAILED";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    public async Task RestartServiceAsync(ServiceItem item, string workspaceDir)
    {
        await StopServiceAsync(item, workspaceDir);
        await Task.Delay(1000);
        await StartServiceAsync(item, workspaceDir);
    }

    private sealed class ProcessRunResult
    {
        public bool Success;
        public bool StillRunning;
        public int ExitCode = -1;
        public string Diagnostic = "";
    }

    private static ProcessRunResult RunProcessHidden(string fileName, string args, int waitExitMs = 10000)
    {
        var result = new ProcessRunResult();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p == null)
            {
                result.Diagnostic = $"failed to start {fileName}";
                return result;
            }
            if (!p.WaitForExit(waitExitMs))
            {
                result.StillRunning = !p.HasExited;
                result.Success = result.StillRunning; // long-running daemon: alive is OK
                result.Diagnostic = result.StillRunning ? "process running (daemon)" : "timed out";
                return result;
            }
            result.ExitCode = p.ExitCode;
            result.Success = p.ExitCode == 0;
            if (!result.Success)
            {
                var err = p.StandardError.ReadToEnd();
                result.Diagnostic = $"exit={p.ExitCode}" +
                    (string.IsNullOrWhiteSpace(err) ? "" : $": {err.Trim().Substring(0, Math.Min(err.Trim().Length, 120))}");
            }
            return result;
        }
        catch (Exception ex)
        {
            result.Diagnostic = ex.Message;
            return result;
        }
    }

    // Stops only processes confirmed to belong to Hermes (path/cmdline/registry evidence).
    private static string StopOwnedProcessOnPort(int port, string workspaceDir)
    {
        try
        {
            var installDir = HermesConfigService.DefaultHermesHome;
            var ws = string.IsNullOrWhiteSpace(workspaceDir) ? HermesConfigService.DefaultWorkspaceDir : workspaceDir;
            return ProcessOwnershipRegistry.StopOwnedProcessesOnPort(port, installDir, ws);
        }
        catch (Exception ex)
        {
            return $"ownership check failed: {ex.Message}";
        }
    }
}
