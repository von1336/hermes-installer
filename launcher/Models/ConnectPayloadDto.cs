using System;
using System.Text.Json.Serialization;

namespace HermesLauncher.Models;

public class ConnectPayloadDto
{
    [JsonPropertyName("v")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("gateway")]
    public string Gateway { get; set; } = "";

    [JsonPropertyName("dashboard")]
    public string Dashboard { get; set; } = "";

    [JsonPropertyName("agentDashboard")]
    public string AgentDashboard { get; set; } = "";

    [JsonPropertyName("workspace")]
    public string Workspace { get; set; } = "";

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = "";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    [JsonPropertyName("tailscaleIp")]
    public string TailscaleIp { get; set; } = "";

    [JsonPropertyName("exp")]
    public long? ExpiryEpoch { get; set; }
}

public class InstallSettings
{
    public string InstallDir { get; set; } = "";
    public string WorkspaceDir { get; set; } = "";
    public bool InstallTailscale { get; set; } = true;
    public bool InstallOllama { get; set; } = true;
    public bool InstallMemOS { get; set; } = false;
    public string MemOSMode { get; set; } = "skip"; // "local", "provider", "skip"
    public string MemOSProviderUrl { get; set; } = "https://api.openai.com/v1";
    public string MemOSProviderKey { get; set; } = "";
    public string MemOSProviderModel { get; set; } = "gpt-4o-mini";
    public bool InstallObsidian { get; set; } = false;
    public bool InstallObsidianSkills { get; set; } = false;
    public bool ConfigureFirewall { get; set; } = true;
    public bool StartServices { get; set; } = true;
    // Independent of StartServices: register Scheduled Tasks / Startup entries at logon.
    public bool EnableAutoStart { get; set; } = true;
}
