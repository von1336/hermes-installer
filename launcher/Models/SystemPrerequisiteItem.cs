using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HermesLauncher.Models;

public class SystemPrerequisiteItem : INotifyPropertyChanged
{
    private string _name = "";
    private string _category = "";
    private bool _isInstalled;
    private bool _isOptional;
    private string _versionOrStatus = "Checking...";
    private string _recommendation = "";

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string Category
    {
        get => _category;
        set { _category = value; OnPropertyChanged(); }
    }

    public bool IsInstalled
    {
        get => _isInstalled;
        set { _isInstalled = value; OnPropertyChanged(); }
    }

    public bool IsOptional
    {
        get => _isOptional;
        set { _isOptional = value; OnPropertyChanged(); }
    }

    public string VersionOrStatus
    {
        get => _versionOrStatus;
        set { _versionOrStatus = value; OnPropertyChanged(); }
    }

    public string Recommendation
    {
        get => _recommendation;
        set { _recommendation = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
