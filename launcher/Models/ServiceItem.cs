using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HermesLauncher.Models;

public class ServiceItem : INotifyPropertyChanged
{
    private string _name = "";
    private string _description = "";
    private int _port;
    private string _probeUrl = "";
    private string _endpointLabel = "";
    private string _protocol = "HTTP/1.1";
    private bool _isOnline;
    private string _statusText = "Checking...";
    private int _latencyMs;
    private string _detectedModel = "";
    private bool _isBusy;
    private string _lastChecked = "";
    private string _lastDiagnostic = "";

    // Typed state for UI contracts (derived from probe/operation results).
    public ServiceState State
    {
        get
        {
            if (_isBusy) return ServiceState.Checking;
            if (_statusText.StartsWith("STARTING", StringComparison.OrdinalIgnoreCase)) return ServiceState.Starting;
            if (_statusText.Contains("FAIL", StringComparison.OrdinalIgnoreCase)) return ServiceState.Failed;
            return _isOnline ? ServiceState.Online : ServiceState.Offline;
        }
    }

    public string LastDiagnostic
    {
        get => _lastDiagnostic;
        set { _lastDiagnostic = value; OnPropertyChanged(); }
    }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    public int Port
    {
        get => _port;
        set { _port = value; OnPropertyChanged(); }
    }

    public string ProbeUrl
    {
        get => _probeUrl;
        set { _probeUrl = value; OnPropertyChanged(); }
    }

    public string EndpointLabel
    {
        get => _endpointLabel;
        set { _endpointLabel = value; OnPropertyChanged(); }
    }

    public string Protocol
    {
        get => _protocol;
        set { _protocol = value; OnPropertyChanged(); }
    }

    public bool IsOnline
    {
        get => _isOnline;
        set { _isOnline = value; OnPropertyChanged(); OnPropertyChanged(nameof(State)); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); OnPropertyChanged(nameof(State)); }
    }

    public int LatencyMs
    {
        get => _latencyMs;
        set { _latencyMs = value; OnPropertyChanged(); }
    }

    public string DetectedModel
    {
        get => _detectedModel;
        set { _detectedModel = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(State)); }
    }

    public string LastChecked
    {
        get => _lastChecked;
        set { _lastChecked = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
