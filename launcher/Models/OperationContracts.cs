using System;

namespace HermesLauncher.Models;

public enum InstallationState
{
    Ready,
    Running,
    Cancelling,
    Cancelled,
    Failed,
    Completed
}

public enum ServiceState
{
    Online,
    Offline,
    Checking,
    Starting,
    Failed
}

public enum DataState
{
    Empty,
    Loading,
    Success,
    Cancelled,
    Failure
}

public enum LogSeverity
{
    Info,
    Warning,
    Error
}

public sealed class OperationResult
{
    public Guid OperationId { get; init; }
    public InstallationState FinalState { get; init; }
    public int ExitCode { get; init; }
    public bool WasCancelled => FinalState == InstallationState.Cancelled;
    public bool IsSuccess => FinalState == InstallationState.Completed;
    public string RedactedMessage { get; init; } = "";
    public string? ErrorReportPath { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset FinishedAtUtc { get; init; }
}

public sealed class ProgressEventDto
{
    public Guid OperationId { get; init; }
    public string StepId { get; init; } = "";
    public double Progress { get; init; }
    public LogSeverity Severity { get; init; } = LogSeverity.Info;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Message { get; init; } = "";
    public string CopySafeText => $"[{TimestampUtc:HH:mm:ss}] [{Severity}] {Message}";
}

public sealed class ServiceStatusDto
{
    public string Name { get; init; } = "";
    public ServiceState State { get; init; }
    public int LatencyMs { get; init; }
    public DateTimeOffset? LastCheckUtc { get; init; }
    public string SafeDiagnostic { get; init; } = "";
}

public sealed class SecretFieldDto
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string MaskedValue { get; init; } = "";
    public bool IsRevealed { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public TimeSpan? RemainingLifetime { get; init; }
    public string? Warning { get; init; }
    // [UI: raw secret value intentionally NOT exposed here; use ISecretCommands.Reveal/Copy on the VM]
}

// [UI: implement/consumed by coder-ui; VM exposes these as methods, UI binds buttons to them]
public interface ISecretCommands
{
    void Reveal(string secretId);
    void Hide(string secretId);
    void Copy(string secretId);
    void Regenerate(string secretId);
    void Expire(string secretId);
}
