using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HermesLauncher.Models;
using HermesLauncher.Services;

namespace HermesLauncher.ViewModels;

public class EnvEntry : INotifyPropertyChanged
{
    private string _key = "";
    private string _value = "";

    public string Key
    {
        get => _key;
        set { _key = value; OnPropertyChanged(); }
    }

    public string Value
    {
        get => _value;
        set { _value = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class MainViewModel : INotifyPropertyChanged, ISecretCommands
{
    private readonly ServiceMonitor _monitor;
    private readonly InstallerRunnerService _installerRunner;
    private readonly SystemDiagnosticsService _diagnosticsService;
    private readonly UpdateService _updateService = new();
    private readonly DispatcherTimer _autoRefreshTimer;
    private readonly DispatcherTimer _secondTimer;
    private readonly DateTime _appStartTime = DateTime.UtcNow;

    // Launcher self-update
    private bool _updateCheckRunning;
    private bool _isUpdateReady;
    private string _updateStatusText = "";
    private string _pendingUpdatePath = "";

    // Navigation & State
    private int _selectedTabIndex;
    private bool _isInstalled;
    private string _systemStatusSummary = "Probing Services...";
    private bool _isSystemHealthy;
    private string _uptimeText = "00:00:00";
    private string _avgLatencyText = "-- ms";

    // Installer properties
    private string _installDir = HermesConfigService.DefaultHermesHome;
    private string _workspaceDir = HermesConfigService.DefaultWorkspaceDir;
    private bool _installTailscale = true;
    private bool _installOllama = true;
    private bool _installMemOS;
    private string _memOSMode = "skip";
    private string _memOSProviderUrl = "https://api.openai.com/v1";
    private string _memOSProviderKey = "";
    private string _memOSProviderModel = "gpt-4o-mini";
    private bool _installObsidian;
    private bool _installObsidianSkills;
    private bool _configureFirewall = true;
    private bool _startServices = true;
    private bool _enableAutoStart = true;
    private bool _isInstalling;
    private bool _isCancelling;
    private InstallationState _installationState = InstallationState.Ready;
    private double _installProgress;
    private string _currentStepTitle = "Ready to Install Hermes Stack";
    private string _installerLogText = "";

    // Provider Configurator properties
    private string _selectedProviderPreset = "OpenRouter";
    private string _providerBaseUrl = "https://openrouter.ai/api/v1";
    private string _providerApiKey = "";
    private string _providerModelName = "nousresearch/hermes-3-llama-3.1-405b:free";

    // Connect & QR properties
    private BitmapSource? _qrImage;
    private string _connectCode = "";
    private string _deepLink = "";
    private string _expiryLabel = "Valid for 24 Hours";
    private string _countdownText = "23h 59m 59s";
    private DateTime? _qrExpiryTime;
    private string _copyNotification = "";
    private string _tailscaleIp = "";
    private string _lanIp = "";
    private string _preferredHost = "";
    private string _apiKey = "";
    private string _password = "";

    // Logs viewer
    private string _selectedLogName = "install.log";
    private string _logViewerContent = "";
    private string _logSearchQuery = "";
    private int _setupWizardStep;
    private string _installErrorReport = "";
    private bool _showInstallErrorPanel;
    private string _tailscaleStatusText = "Checking network...";
    private string _setupWizardStepTitle = "Welcome";

    public ObservableCollection<ServiceItem> Services => _monitor.Services;
    public ObservableCollection<SystemPrerequisiteItem> Prerequisites => _diagnosticsService.Prerequisites;
    public ObservableCollection<string> AvailableLogs { get; } = new();
    public ObservableCollection<EnvEntry> EnvEntries { get; } = new();
    public ObservableCollection<string> ProviderPresets { get; } = new()
    {
        "OpenRouter",
        "OpenAI",
        "Groq",
        "Ollama (Local)",
        "Custom Provider"
    };

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set { _selectedTabIndex = value; OnPropertyChanged(); }
    }

    public bool IsInstalled
    {
        get => _isInstalled;
        set { _isInstalled = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowSetupWizardChrome)); OnPropertyChanged(nameof(ShowSetupConfigFields)); OnPropertyChanged(nameof(ShowSetupComponents)); }
    }

    public string SystemStatusSummary
    {
        get => _systemStatusSummary;
        set { _systemStatusSummary = value; OnPropertyChanged(); }
    }

    public string LauncherVersionText => "Launcher v" + UpdateService.CurrentVersionText;

    public string UpdateStatusText
    {
        get => _updateStatusText;
        set { _updateStatusText = value; OnPropertyChanged(); }
    }

    public bool IsUpdateReady
    {
        get => _isUpdateReady;
        set { _isUpdateReady = value; OnPropertyChanged(); }
    }

    public bool IsSystemHealthy
    {
        get => _isSystemHealthy;
        set { _isSystemHealthy = value; OnPropertyChanged(); }
    }

    public string UptimeText
    {
        get => _uptimeText;
        set { _uptimeText = value; OnPropertyChanged(); }
    }

    public string AvgLatencyText
    {
        get => _avgLatencyText;
        set { _avgLatencyText = value; OnPropertyChanged(); }
    }

    public string InstallDir
    {
        get => _installDir;
        set { _installDir = value; OnPropertyChanged(); }
    }

    public string WorkspaceDir
    {
        get => _workspaceDir;
        set { _workspaceDir = value; OnPropertyChanged(); }
    }

    public bool InstallTailscale
    {
        get => _installTailscale;
        set { _installTailscale = value; OnPropertyChanged(); }
    }

    public bool InstallOllama
    {
        get => _installOllama;
        set { _installOllama = value; OnPropertyChanged(); }
    }

    public bool InstallMemOS
    {
        get => _installMemOS;
        set
        {
            _installMemOS = value;
            _memOSMode = value ? "local" : "skip";
            OnPropertyChanged();
        }
    }

    public string MemOSMode
    {
        get => _memOSMode;
        set { _memOSMode = value; OnPropertyChanged(); }
    }

    public string MemOSProviderUrl
    {
        get => _memOSProviderUrl;
        set { _memOSProviderUrl = value; OnPropertyChanged(); }
    }

    public string MemOSProviderKey
    {
        get => _memOSProviderKey;
        set { _memOSProviderKey = value; OnPropertyChanged(); }
    }

    public string MemOSProviderModel
    {
        get => _memOSProviderModel;
        set { _memOSProviderModel = value; OnPropertyChanged(); }
    }

    public bool InstallObsidian
    {
        get => _installObsidian;
        set { _installObsidian = value; OnPropertyChanged(); }
    }

    public bool InstallObsidianSkills
    {
        get => _installObsidianSkills;
        set { _installObsidianSkills = value; OnPropertyChanged(); }
    }

    public bool ConfigureFirewall
    {
        get => _configureFirewall;
        set { _configureFirewall = value; OnPropertyChanged(); }
    }

    public bool StartServices
    {
        get => _startServices;
        set { _startServices = value; OnPropertyChanged(); }
    }

    // [UI: bind checkbox "Start Hermes automatically at Windows logon"; independent from StartServices]
    public bool EnableAutoStart
    {
        get => _enableAutoStart;
        set { _enableAutoStart = value; OnPropertyChanged(); }
    }

    public InstallationState InstallationState
    {
        get => _installationState;
        private set { _installationState = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartInstall)); }
    }

    public bool IsCancelling
    {
        get => _isCancelling;
        private set { _isCancelling = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartInstall)); }
    }

    // [UI: bind Install/Retry button IsEnabled; retry allowed only after previous op fully finished]
    public bool CanStartInstall => !IsInstalling && !IsCancelling && _installerRunner.CanStartNewOperation;

    public bool IsInstalling
    {
        get => _isInstalling;
        set { _isInstalling = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartInstall)); }
    }

    public double InstallProgress
    {
        get => _installProgress;
        set { _installProgress = value; OnPropertyChanged(); }
    }

    public string CurrentStepTitle
    {
        get => _currentStepTitle;
        set { _currentStepTitle = value; OnPropertyChanged(); }
    }

    public string InstallerLogText
    {
        get => _installerLogText;
        set { _installerLogText = value; OnPropertyChanged(); }
    }

    public string SelectedProviderPreset
    {
        get => _selectedProviderPreset;
        set
        {
            _selectedProviderPreset = value;
            OnPropertyChanged();
            ApplyPresetDefaults(value);
        }
    }

    public string ProviderBaseUrl
    {
        get => _providerBaseUrl;
        set { _providerBaseUrl = value; OnPropertyChanged(); }
    }

    public string ProviderApiKey
    {
        get => _providerApiKey;
        set { _providerApiKey = value; OnPropertyChanged(); }
    }

    public string ProviderModelName
    {
        get => _providerModelName;
        set { _providerModelName = value; OnPropertyChanged(); }
    }

    public BitmapSource? QrImage
    {
        get => _qrImage;
        set { _qrImage = value; OnPropertyChanged(); }
    }

    public string ConnectCode
    {
        get => _connectCode;
        set { _connectCode = value; OnPropertyChanged(); }
    }

    public string DeepLink
    {
        get => _deepLink;
        set { _deepLink = value; OnPropertyChanged(); }
    }

    public string ExpiryLabel
    {
        get => _expiryLabel;
        set { _expiryLabel = value; OnPropertyChanged(); }
    }

    public string CountdownText
    {
        get => _countdownText;
        set { _countdownText = value; OnPropertyChanged(); }
    }

    public string CopyNotification
    {
        get => _copyNotification;
        set { _copyNotification = value; OnPropertyChanged(); }
    }

    public string TailscaleIp
    {
        get => _tailscaleIp;
        set { _tailscaleIp = value; OnPropertyChanged(); }
    }

    public string LanIp
    {
        get => _lanIp;
        set { _lanIp = value; OnPropertyChanged(); }
    }

    public string PreferredHost
    {
        get => _preferredHost;
        set { _preferredHost = value; OnPropertyChanged(); }
    }

    public string ApiKey
    {
        get => _apiKey;
        set { _apiKey = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApiKeyDisplay)); }
    }

    public string Password
    {
        get => _password;
        set { _password = value; OnPropertyChanged(); OnPropertyChanged(nameof(PasswordDisplay)); }
    }

    // --- Secret masking contract ([UI: bind PasswordBox/masked controls to Display props; raw props stay internal]) ---
    private bool _isApiKeyRevealed;
    private bool _isPasswordRevealed;

    public bool IsApiKeyRevealed
    {
        get => _isApiKeyRevealed;
        private set { _isApiKeyRevealed = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApiKeyDisplay)); }
    }

    public bool IsPasswordRevealed
    {
        get => _isPasswordRevealed;
        private set { _isPasswordRevealed = value; OnPropertyChanged(); OnPropertyChanged(nameof(PasswordDisplay)); }
    }

    // [UI: default display is masked; raw value only when user explicitly revealed]
    public string ApiKeyDisplay => IsApiKeyRevealed ? ApiKey : HermesConfigService.MaskSecret(ApiKey);
    public string PasswordDisplay => IsPasswordRevealed ? Password : HermesConfigService.MaskSecret(Password);

    public string PairingWarning =>
        "Pairing code contains API credentials. Share only with your own devices. QR expires in 24h.";

    public void Reveal(string secretId) => RevealSecret(secretId);
    public void Hide(string secretId) => HideSecret(secretId);
    public void Copy(string secretId) => CopySecret(secretId);
    public void Regenerate(string secretId) => RegenerateSecret(secretId);
    public void Expire(string secretId) => ExpireSecret(secretId);

    public void RevealSecret(string secretId)
    {
        if (secretId == "apiKey") IsApiKeyRevealed = true;
        else if (secretId == "password") IsPasswordRevealed = true;
        ShowToast("Secret revealed — keep it private.");
    }

    public void HideSecret(string secretId)
    {
        if (secretId == "apiKey") IsApiKeyRevealed = false;
        else if (secretId == "password") IsPasswordRevealed = false;
    }

    public void CopySecret(string secretId)
    {
        var value = secretId == "apiKey" ? ApiKey : secretId == "password" ? Password : null;
        if (string.IsNullOrEmpty(value)) return;
        CopyToClipboard(value, secretId == "apiKey" ? "API key" : "password");
    }

    public void RegenerateSecret(string secretId)
    {
        if (secretId is "apiKey" or "password")
        {
            RegenerateConnectQr(24); // regenerates + persists both credentials
        }
    }

    public void ExpireSecret(string secretId)
    {
        _qrExpiryTime = DateTime.UtcNow;
        UpdateSecondTimer();
        ShowToast("Pairing QR expired. Regenerate to pair again.");
    }

    public string SelectedLogName
    {
        get => _selectedLogName;
        set
        {
            _selectedLogName = value;
            OnPropertyChanged();
            LoadSelectedLogContent();
        }
    }

    public string LogViewerContent
    {
        get => _logViewerContent;
        set { _logViewerContent = value; OnPropertyChanged(); }
    }

    public string LogSearchQuery
    {
        get => _logSearchQuery;
        set
        {
            _logSearchQuery = value;
            OnPropertyChanged();
            LoadSelectedLogContent();
        }
    }

    public int SetupWizardStep
    {
        get => _setupWizardStep;
        set
        {
            _setupWizardStep = value;
            SetupWizardStepTitle = value switch
            {
                0 => "Diagnostics",
                1 => "Paths & Modules",
                2 => "Model & Provider",
                _ => "Terminal & Deploy"
            };
            OnPropertyChanged();
        }
    }

    public string SetupWizardStepTitle
    {
        get => _setupWizardStepTitle;
        set { _setupWizardStepTitle = value; OnPropertyChanged(); }
    }

    public bool ShowSetupWizardChrome => !IsInstalled && SetupWizardStep < 5;

    public bool ShowSetupConfigFields => IsInstalled || SetupWizardStep >= 1;

    public bool ShowSetupComponents => IsInstalled || SetupWizardStep >= 2;

    public string InstallErrorReport
    {
        get => _installErrorReport;
        set { _installErrorReport = value; OnPropertyChanged(); }
    }

    public bool ShowInstallErrorPanel
    {
        get => _showInstallErrorPanel;
        set { _showInstallErrorPanel = value; OnPropertyChanged(); }
    }

    public string TailscaleStatusText
    {
        get => _tailscaleStatusText;
        set { _tailscaleStatusText = value; OnPropertyChanged(); }
    }

    public MainViewModel()
    {
        _monitor = new ServiceMonitor();
        _installerRunner = new InstallerRunnerService();
        _diagnosticsService = new SystemDiagnosticsService();

        _installerRunner.LogReceived += OnInstallerLogReceived;
        _installerRunner.StepChanged += OnInstallerStepChanged;
        _installerRunner.OperationCompleted += OnOperationCompleted;

        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        _autoRefreshTimer.Tick += async (s, e) => await CheckServicesAsync();

        _secondTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _secondTimer.Tick += (s, e) => UpdateSecondTimer();
        _secondTimer.Start();

        RefreshNetworkInfo();
        LoadExistingConfig();

        if (IsInstalled)
        {
            SelectedTabIndex = 0; // Dashboard
            _autoRefreshTimer.Start();
            _ = CheckServicesAsync();
            RegenerateConnectQr(24);
        }
        else
        {
            SelectedTabIndex = 2;
            SetupWizardStep = 0;
        }

        RefreshAvailableLogs();
        LoadEnvEditor();
        _ = RefreshDiagnosticsAsync();
        _ = CheckForUpdatesAsync(false);
    }

    public async Task RefreshDiagnosticsAsync()
    {
        await _diagnosticsService.RunDiagnosticsAsync(InstallDir, WorkspaceDir);
    }

    public async Task CheckForUpdatesAsync(bool userInitiated)
    {
        if (_updateCheckRunning || IsInstalling) { return; }
        _updateCheckRunning = true;
        try
        {
            IsUpdateReady = false;
            UpdateStatusText = "Checking for launcher updates...";
            var info = await _updateService.CheckAsync();
            if (info.Error != null)
            {
                UpdateStatusText = "";
                if (userInitiated) { ShowToast(info.Error); }
                return;
            }
            if (!info.IsNewer)
            {
                UpdateStatusText = "";
                if (userInitiated) { ShowToast($"Launcher is up to date (v{UpdateService.CurrentVersionText})"); }
                return;
            }

            UpdateStatusText = $"Downloading update v{info.LatestVersion}...";
            var progress = new Progress<int>(pct => UpdateStatusText = $"Downloading update v{info.LatestVersion}... {pct}%");
            var target = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "hermes", "updates", $"HermesLauncher-v{info.LatestVersion}.exe");
            await _updateService.DownloadAsync(info, target, progress);

            _pendingUpdatePath = target;
            IsUpdateReady = true;
            UpdateStatusText = $"Update v{info.LatestVersion} ready - click \"Apply update\" to restart";
            ShowToast($"Launcher update v{info.LatestVersion} downloaded!");
        }
        catch (Exception ex)
        {
            UpdateStatusText = "";
            if (userInitiated) { ShowToast("Update failed: " + ex.Message); }
        }
        finally
        {
            _updateCheckRunning = false;
        }
    }

    public bool TryApplyPendingUpdate()
    {
        if (!IsUpdateReady || string.IsNullOrEmpty(_pendingUpdatePath) || !File.Exists(_pendingUpdatePath))
        {
            return false;
        }
        if (IsInstalling)
        {
            ShowToast("Wait for the current installation to finish before updating.");
            return false;
        }
        _updateService.ApplyAndRestart(_pendingUpdatePath);
        return true;
    }

    private void ApplyPresetDefaults(string preset)
    {
        switch (preset)
        {
            case "OpenRouter":
                ProviderBaseUrl = "https://openrouter.ai/api/v1";
                ProviderModelName = "nousresearch/hermes-3-llama-3.1-405b:free";
                break;
            case "OpenAI":
                ProviderBaseUrl = "https://api.openai.com/v1";
                ProviderModelName = "gpt-4o-mini";
                break;
            case "Groq":
                ProviderBaseUrl = "https://api.groq.com/openai/v1";
                ProviderModelName = "llama-3.3-70b-versatile";
                break;
            case "Ollama (Local)":
                ProviderBaseUrl = "http://127.0.0.1:11434/v1";
                ProviderModelName = "hermes3:8b";
                break;
        }
    }

    public async Task ApplyProviderConfigAsync()
    {
        var envPath = HermesConfigService.GetHermesEnvPath(InstallDir);
        var dict = HermesConfigService.ReadDotEnv(envPath);

        dict["OPENAI_BASE_URL"] = ProviderBaseUrl.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(ProviderApiKey))
        {
            dict["OPENAI_API_KEY"] = ProviderApiKey;
        }
        dict["OPENAI_MODEL"] = ProviderModelName;

        HermesConfigService.WriteDotEnv(envPath, dict);
        LoadEnvEditor();

        ShowToast($"Applied {SelectedProviderPreset} provider configuration! Restarting Gateway...");
        await _monitor.RestartServiceAsync(_monitor.GatewayService, WorkspaceDir);
    }

    private void UpdateSecondTimer()
    {
        var uptime = DateTime.UtcNow - _appStartTime;
        UptimeText = $"UPTIME: {uptime:hh\\:mm\\:ss}";

        if (_qrExpiryTime.HasValue)
        {
            var rem = _qrExpiryTime.Value - DateTime.UtcNow;
            if (rem.TotalSeconds > 0)
            {
                var totalHours = (int)Math.Floor(rem.TotalHours);
                CountdownText = $"{totalHours:D2}h {rem.Minutes:D2}m {rem.Seconds:D2}s remaining";
            }
            else
            {
                CountdownText = "Expired • Click to regenerate";
            }
        }
        else
        {
            CountdownText = "Permanent (Local Mode)";
        }
    }

    public void RefreshNetworkInfo()
    {
        TailscaleIp = HermesConfigService.DetectTailscaleIp() ?? "";
        LanIp = HermesConfigService.DetectLanIp();
        PreferredHost = !string.IsNullOrWhiteSpace(TailscaleIp) ? TailscaleIp : LanIp;
        TailscaleStatusText = !string.IsNullOrWhiteSpace(TailscaleIp)
            ? $"Connected {TailscaleIp}"
            : (!string.IsNullOrWhiteSpace(LanIp) && LanIp != "127.0.0.1")
                ? $"LAN only {LanIp}"
                : "Tailscale not logged in";
    }

    public void LoadExistingConfig()
    {
        IsInstalled = HermesConfigService.IsHermesInstalled(InstallDir, WorkspaceDir);
        var env = HermesConfigService.ReadDotEnv(HermesConfigService.GetHermesEnvPath(InstallDir));

        if (env.TryGetValue("API_SERVER_KEY", out var key)) ApiKey = key;
        if (env.TryGetValue("HERMES_PASSWORD", out var pass)) Password = pass;
        if (env.TryGetValue("OPENAI_BASE_URL", out var pUrl)) ProviderBaseUrl = pUrl;
        else if (env.TryGetValue("CUSTOM_PROVIDER_URL", out pUrl)) ProviderBaseUrl = pUrl;
        if (env.TryGetValue("OPENAI_API_KEY", out var pKey)) ProviderApiKey = pKey;
        else if (env.TryGetValue("CUSTOM_PROVIDER_KEY", out pKey)) ProviderApiKey = pKey;
        if (env.TryGetValue("OPENAI_MODEL", out var pModel)) ProviderModelName = pModel;
        else if (env.TryGetValue("CUSTOM_PROVIDER_MODEL", out pModel)) ProviderModelName = pModel;

        if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(Password))
        {
            var wsEnv = HermesConfigService.ReadDotEnv(HermesConfigService.GetWorkspaceEnvPath(WorkspaceDir));
            if (string.IsNullOrWhiteSpace(ApiKey) && wsEnv.TryGetValue("HERMES_API_TOKEN", out var wsKey)) ApiKey = wsKey;
            if (string.IsNullOrWhiteSpace(Password) && wsEnv.TryGetValue("HERMES_PASSWORD", out var wsPass)) Password = wsPass;
        }
    }

    public void LoadEnvEditor()
    {
        EnvEntries.Clear();
        var envPath = HermesConfigService.GetHermesEnvPath(InstallDir);
        var dict = HermesConfigService.ReadDotEnv(envPath);

        if (!dict.ContainsKey("API_SERVER_KEY")) dict["API_SERVER_KEY"] = ApiKey;
        if (!dict.ContainsKey("HERMES_PASSWORD")) dict["HERMES_PASSWORD"] = Password;
        if (!dict.ContainsKey("API_SERVER_PORT")) dict["API_SERVER_PORT"] = "8642";
        if (!dict.ContainsKey("API_SERVER_HOST")) dict["API_SERVER_HOST"] = "0.0.0.0";
        if (!dict.ContainsKey("OLLAMA_HOST")) dict["OLLAMA_HOST"] = "http://127.0.0.1:11434";

        foreach (var kvp in dict.OrderBy(k => k.Key))
        {
            EnvEntries.Add(new EnvEntry { Key = kvp.Key, Value = kvp.Value });
        }
    }

    public void SaveEnvEditor()
    {
        var envPath = HermesConfigService.GetHermesEnvPath(InstallDir);
        var dict = new Dictionary<string, string>();
        foreach (var entry in EnvEntries)
        {
            if (!string.IsNullOrWhiteSpace(entry.Key))
            {
                dict[entry.Key.Trim()] = entry.Value.Trim();
            }
        }
        HermesConfigService.WriteDotEnv(envPath, dict);
        LoadExistingConfig();
        ShowToast("Environment configuration saved successfully!");
    }

    public async Task CheckServicesAsync()
    {
        await _monitor.ProbeAllAsync();

        var gw = _monitor.GatewayService.IsOnline;
        var ws = _monitor.WorkspaceService.IsOnline;
        var agent = _monitor.AgentDashboardService.IsOnline;

        var onlineItems = _monitor.Services.Where(s => s.IsOnline && s.LatencyMs > 0).ToList();
        if (onlineItems.Count > 0)
        {
            var avg = (int)onlineItems.Average(s => s.LatencyMs);
            AvgLatencyText = $"AVG LATENCY: {avg}ms";
        }
        else
        {
            AvgLatencyText = "LATENCY: --";
        }

        if (gw && ws && agent)
        {
            IsSystemHealthy = true;
            SystemStatusSummary = "ALL SYSTEMS OPERATIONAL";
        }
        else if (gw || ws)
        {
            IsSystemHealthy = false;
            SystemStatusSummary = "DEGRADED • PARTIALLY ONLINE";
        }
        else
        {
            IsSystemHealthy = false;
            SystemStatusSummary = "SERVICES OFFLINE";
        }
    }

    public void RegenerateConnectQr(int expiryHours = 24)
    {
        RefreshNetworkInfo();
        LoadExistingConfig();

        var generated = false;
        if (string.IsNullOrWhiteSpace(ApiKey)) { ApiKey = GenerateRandomHex(24); generated = true; }
        if (string.IsNullOrWhiteSpace(Password)) { Password = GenerateRandomHex(24); generated = true; }
        if (generated) PersistConnectSecrets(ApiKey, Password);

        if (expiryHours > 0)
        {
            _qrExpiryTime = DateTime.UtcNow.AddHours(expiryHours);
            ExpiryLabel = $"Valid for {expiryHours} Hours (until {DateTime.Now.AddHours(expiryHours):dd.MM HH:mm})";
        }
        else
        {
            _qrExpiryTime = null;
            ExpiryLabel = "Permanent Token (Local Network)";
        }

        var (deepLink, code, _) = QrGeneratorService.BuildConnectPayload(
            connectHost: PreferredHost,
            apiKey: ApiKey,
            password: Password,
            tailscaleIp: TailscaleIp,
            gatewayPort: 8642,
            workspacePort: 3000,
            dashboardPort: 9119,
            expireHours: expiryHours > 0 ? expiryHours : null
        );

        DeepLink = deepLink;
        ConnectCode = code;
        QrImage = QrGeneratorService.GenerateQrImage(deepLink, 10);
        UpdateSecondTimer();
        ShowToast("Generated fresh pairing QR code!");
    }

    public void NextSetupWizardStep()
    {
        if (IsInstalling) return;
        SetupWizardStep = Math.Min(3, SetupWizardStep + 1);
    }

    public void PrevSetupWizardStep()
    {
        if (IsInstalling || SetupWizardStep <= 0) return;
        SetupWizardStep--;
    }

    public void LoadInstallErrorReport()
    {
        var path = Path.Combine(InstallDir, "install-error.txt");
        if (!File.Exists(path))
        {
            path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "hermes", "install-error.txt");
        }
        InstallErrorReport = File.Exists(path)
            ? File.ReadAllText(path)
            : "No install-error.txt found.";
        ShowInstallErrorPanel = true;
    }

    public void OpenInstallErrorReport()
    {
        LoadInstallErrorReport();
        var path = Path.Combine(InstallDir, "install-error.txt");
        if (!File.Exists(path))
        {
            path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "hermes", "install-error.txt");
        }
        if (File.Exists(path))
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }
    }

    private void PersistConnectSecrets(string apiKey, string password)
    {
        var hermesEnv = HermesConfigService.GetHermesEnvPath(InstallDir);
        var hermesDict = HermesConfigService.ReadDotEnv(hermesEnv);
        hermesDict["API_SERVER_ENABLED"] = "true";
        hermesDict["API_SERVER_KEY"] = apiKey;
        hermesDict["HERMES_PASSWORD"] = password;
        hermesDict["API_SERVER_HOST"] = "0.0.0.0";
        hermesDict["API_SERVER_PORT"] = "8642";
        HermesConfigService.WriteDotEnv(hermesEnv, hermesDict);

        var wsEnv = HermesConfigService.GetWorkspaceEnvPath(WorkspaceDir);
        var wsDict = HermesConfigService.ReadDotEnv(wsEnv);
        wsDict["HERMES_API_TOKEN"] = apiKey;
        wsDict["HERMES_PASSWORD"] = password;
        HermesConfigService.WriteDotEnv(wsEnv, wsDict);
    }

    private bool _lastOperationWasUninstall;
    private int _lastFailedExitCode;
    private string _lastFailedMessage = "";

    // TEMPORARY debug feature: sends a sanitized failure report to GitHub Issues.
    public void SendErrorReportToGitHub(bool auto = false)
    {
        var label = _lastOperationWasUninstall ? "Uninstall" : "Installation";
        var consoleTail = InstallerLogText ?? "";
        if (consoleTail.Length > 2500)
            consoleTail = consoleTail.Substring(consoleTail.Length - 2500);

        ErrorReportService.SendFailureReport(
            label,
            _lastFailedExitCode,
            _lastFailedMessage,
            InstallDir,
            consoleTail,
            new[] { ProviderApiKey });

        ShowToast(auto
            ? "Error report opened in browser — press Submit to send it."
            : "Report copied to clipboard and opened in browser.");
    }

    public async Task StartInstallAsync(bool isCleanReinstall = false)
    {
        if (IsInstalling || !_installerRunner.CanStartNewOperation)
        {
            ShowToast("Previous installation is still finishing — wait for it to complete.");
            return;
        }

        _lastOperationWasUninstall = false;
        IsInstalling = true;
        IsCancelling = false;
        InstallationState = InstallationState.Running;
        ShowInstallErrorPanel = false;
        InstallErrorReport = "";
        InstallProgress = 0.05;
        SetupWizardStep = 3;
        CurrentStepTitle = isCleanReinstall ? "Starting Clean Reinstallation..." : "Initializing installation pipeline...";
        InstallerLogText = $"HERMES STACK {(isCleanReinstall ? "CLEAN REINSTALL" : "INSTALLER")}{Environment.NewLine}========================================={Environment.NewLine}";

        var settings = new InstallSettings
        {
            InstallDir = InstallDir,
            WorkspaceDir = WorkspaceDir,
            InstallTailscale = InstallTailscale,
            InstallOllama = InstallOllama,
            InstallMemOS = InstallMemOS,
            MemOSMode = MemOSMode,
            MemOSProviderUrl = MemOSProviderUrl,
            MemOSProviderKey = MemOSProviderKey,
            MemOSProviderModel = MemOSProviderModel,
            InstallObsidian = InstallObsidian,
            InstallObsidianSkills = InstallObsidianSkills,
            ConfigureFirewall = ConfigureFirewall,
            StartServices = StartServices,
            EnableAutoStart = EnableAutoStart
        };

        try
        {
            await _installerRunner.RunInstallAsync(settings, isCleanReinstall);
        }
        catch (InvalidOperationException ex)
        {
            // Overlap guard: a previous operation is still finalizing.
            ShowToast(ex.Message);
        }
        catch (Exception ex)
        {
            HandleOperationFinished(new OperationResult
            {
                OperationId = Guid.Empty,
                FinalState = InstallationState.Failed,
                ExitCode = -1,
                RedactedMessage = ex.Message,
                StartedAtUtc = DateTimeOffset.UtcNow,
                FinishedAtUtc = DateTimeOffset.UtcNow
            });
        }
    }

    public async Task StartUninstallAsync()
    {
        if (IsInstalling || !_installerRunner.CanStartNewOperation)
        {
            ShowToast("Another operation is still finishing — wait for it to complete.");
            return;
        }

        _lastOperationWasUninstall = true;
        IsInstalling = true;
        IsCancelling = false;
        InstallationState = InstallationState.Running;
        ShowInstallErrorPanel = false;
        InstallErrorReport = "";
        InstallProgress = 0.05;
        SetupWizardStep = 3;
        CurrentStepTitle = "Removing Hermes from this PC...";
        InstallerLogText = $"HERMES FULL UNINSTALL{Environment.NewLine}========================================={Environment.NewLine}";

        var settings = new InstallSettings
        {
            InstallDir = InstallDir,
            WorkspaceDir = WorkspaceDir
        };

        try
        {
            await _installerRunner.RunUninstallAsync(settings);
        }
        catch (InvalidOperationException ex)
        {
            ShowToast(ex.Message);
        }
        catch (Exception ex)
        {
            HandleOperationFinished(new OperationResult
            {
                OperationId = Guid.Empty,
                FinalState = InstallationState.Failed,
                ExitCode = -1,
                RedactedMessage = ex.Message,
                StartedAtUtc = DateTimeOffset.UtcNow,
                FinishedAtUtc = DateTimeOffset.UtcNow
            });
        }
    }

    public void CancelInstall()
    {
        if (!IsInstalling || IsCancelling) return;
        IsCancelling = true;
        InstallationState = InstallationState.Cancelling;
        CurrentStepTitle = "Cancelling installation — waiting for cleanup...";
        _installerRunner.Cancel();
        // IsInstalling stays true until the runner publishes the final OperationResult.
    }

    private void OnInstallerLogReceived(string log)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            InstallerLogText += log + "\r\n";
        });
    }

    private void OnInstallerStepChanged(string title, double progress)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            CurrentStepTitle = title;
            InstallProgress = progress;
        });
    }

    private void OnOperationCompleted(OperationResult result)
    {
        Application.Current?.Dispatcher.Invoke(() => HandleOperationFinished(result));
    }

    private void HandleOperationFinished(OperationResult result)
    {
        IsInstalling = false;
        IsCancelling = false;
        InstallationState = result.FinalState;

        if (result.FinalState == InstallationState.Completed)
        {
            InstallProgress = 1.0;
            if (_lastOperationWasUninstall)
            {
                CurrentStepTitle = "Hermes removed from this PC.";
                IsInstalled = false;
                _autoRefreshTimer.Stop();
                _ = CheckServicesAsync();
                SelectedTabIndex = 0; // Go to Dashboard
                ShowToast("Hermes uninstalled.");
            }
            else
            {
                CurrentStepTitle = "Hermes Stack Deployed Successfully!";
                IsInstalled = true;
                _autoRefreshTimer.Start();
                _ = CheckServicesAsync();
                RegenerateConnectQr(24);
                SelectedTabIndex = 0; // Go to Dashboard
                ShowToast("Deployment Complete!");
            }
        }
        else if (result.FinalState == InstallationState.Cancelled)
        {
            // Cancellation is distinct from error: no error panel, partial install is not "installed".
            InstallProgress = 0;
            CurrentStepTitle = _lastOperationWasUninstall
                ? "Uninstall Cancelled — system left in previous state."
                : "Installation Cancelled — system left in previous state.";
            IsInstalled = HermesConfigService.IsHermesInstalled(InstallDir, WorkspaceDir);
            ShowToast("Operation cancelled.");
        }
        else
        {
            CurrentStepTitle = $"{(_lastOperationWasUninstall ? "Uninstall" : "Installation")} Failed (Exit Code: {result.ExitCode})";
            IsInstalled = HermesConfigService.IsHermesInstalled(InstallDir, WorkspaceDir);
            if (!_lastOperationWasUninstall) LoadInstallErrorReport();

            // TEMPORARY debug feature: auto-report failures to GitHub Issues.
            _lastFailedExitCode = result.ExitCode;
            _lastFailedMessage = result.RedactedMessage;
            SendErrorReportToGitHub(auto: true);
        }
        RefreshAvailableLogs();
        _ = RefreshDiagnosticsAsync();
    }

    public async Task StartServiceAsync(ServiceItem item)
    {
        await _monitor.StartServiceAsync(item, WorkspaceDir);
        await CheckServicesAsync();
        ShowToast($"Started {item.Name}");
    }

    public async Task StopServiceAsync(ServiceItem item)
    {
        await _monitor.StopServiceAsync(item, WorkspaceDir);
        await CheckServicesAsync();
        ShowToast($"Stopped {item.Name}");
    }

    public async Task RestartServiceAsync(ServiceItem item)
    {
        await _monitor.RestartServiceAsync(item, WorkspaceDir);
        await CheckServicesAsync();
        ShowToast($"Restarted {item.Name}");
    }

    public async Task StartAllServicesAsync()
    {
        ShowToast("Starting all services...");
        foreach (var svc in Services)
        {
            await _monitor.StartServiceAsync(svc, WorkspaceDir);
        }
        await CheckServicesAsync();
    }

    public async Task StopAllServicesAsync()
    {
        ShowToast("Stopping all services...");
        foreach (var svc in Services)
        {
            await _monitor.StopServiceAsync(svc, WorkspaceDir);
        }
        await CheckServicesAsync();
    }

    public async Task RestartAllServicesAsync()
    {
        ShowToast("Restarting entire Hermes stack...");
        await StopAllServicesAsync();
        await Task.Delay(1500);
        await StartAllServicesAsync();
    }

    public void RefreshAvailableLogs()
    {
        AvailableLogs.Clear();
        var home = InstallDir;
        if (Directory.Exists(home))
        {
            foreach (var file in Directory.GetFiles(home, "*.log"))
            {
                AvailableLogs.Add(Path.GetFileName(file));
            }
        }

        if (AvailableLogs.Count == 0)
        {
            AvailableLogs.Add("install.log");
        }

        if (!AvailableLogs.Contains(SelectedLogName))
        {
            SelectedLogName = AvailableLogs.FirstOrDefault() ?? "install.log";
        }
        else
        {
            LoadSelectedLogContent();
        }
    }

    public void LoadSelectedLogContent()
    {
        try
        {
            var path = Path.Combine(InstallDir, SelectedLogName);
            if (File.Exists(path))
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                var full = reader.ReadToEnd();

                if (!string.IsNullOrWhiteSpace(LogSearchQuery))
                {
                    var lines = full.Split('\n');
                    var matched = lines.Where(l => l.Contains(LogSearchQuery, StringComparison.OrdinalIgnoreCase));
                    LogViewerContent = string.Join("\n", matched);
                }
                else
                {
                    LogViewerContent = full;
                }
            }
            else
            {
                LogViewerContent = $"Log file [{SelectedLogName}] is empty or not yet generated.";
            }
        }
        catch (Exception ex)
        {
            LogViewerContent = $"Error reading log: {ex.Message}";
        }
    }

    public void OpenLogsFolder()
    {
        try
        {
            if (Directory.Exists(InstallDir))
            {
                Process.Start("explorer.exe", InstallDir);
            }
        }
        catch { }
    }

    public void CopyToClipboard(string text, string label)
    {
        try
        {
            Clipboard.SetText(text);
            ShowToast($"Copied {label} to clipboard!");
        }
        catch { }
    }

    public void ShowToast(string message)
    {
        CopyNotification = message;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (s, e) =>
        {
            CopyNotification = "";
            timer.Stop();
        };
        timer.Start();
    }

    private static string GenerateRandomHex(int bytesCount)
    {
        var buffer = new byte[bytesCount];
        System.Security.Cryptography.RandomNumberGenerator.Fill(buffer);
        return Convert.ToHexString(buffer).ToLowerInvariant();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
