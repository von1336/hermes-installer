using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using HermesLauncher.Models;
using HermesLauncher.ViewModels;

namespace HermesLauncher.Views;

public partial class MainWindow : Window
{
    private bool _syncingProviderSecret;
    private bool _deepLinkRevealed;
    private bool _connectCodeRevealed;
    private bool _providerKeyRevealed;

    public MainWindow()
    {
        InitializeComponent();
        if (DataContext is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SyncSecretFieldsFromViewModel();
        UpdateToastState();
        AnimateElement(ContentHost, 10);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ProviderApiKey) or nameof(MainViewModel.DeepLink) or nameof(MainViewModel.ConnectCode))
        {
            SyncSecretFieldsFromViewModel();
        }

        if (e.PropertyName == nameof(MainViewModel.CopyNotification))
        {
            UpdateToastState();
        }
    }

    private void SyncSecretFieldsFromViewModel()
    {
        if (ViewModel == null || _syncingProviderSecret) return;

        _syncingProviderSecret = true;
        try
        {
            SetPasswordIfChanged(ProviderApiKeyPasswordBox, ViewModel.ProviderApiKey);
            SetPasswordIfChanged(ProviderApiKeyPasswordBoxEnv, ViewModel.ProviderApiKey);
            SetPasswordIfChanged(DeepLinkPasswordBox, ViewModel.DeepLink);
            SetPasswordIfChanged(ConnectCodePasswordBox, ViewModel.ConnectCode);
        }
        finally
        {
            _syncingProviderSecret = false;
        }
    }

    private static void SetPasswordIfChanged(PasswordBox box, string value)
    {
        if (box.Password != value)
        {
            box.Password = value ?? string.Empty;
        }
    }

    private void UpdateToastState()
    {
        if (ToastHost == null || ViewModel == null) return;
        var hasMessage = !string.IsNullOrWhiteSpace(ViewModel.CopyNotification);
        ToastHost.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
        if (hasMessage)
        {
            AnimateElement(ToastHost, -8);
        }
    }

    private static void AnimateElement(UIElement element, double fromY)
    {
        if (!SystemParameters.ClientAreaAnimation) return;

        var transform = new System.Windows.Media.TranslateTransform(0, fromY);
        element.RenderTransform = transform;
        element.Opacity = 0;

        var duration = TimeSpan.FromMilliseconds(180);
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        transform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void FocusContentHost()
    {
        ContentHost.Focus();
        AnimateElement(ContentHost, 10);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void HostBadge_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.CopyToClipboard(ViewModel.PreferredHost, "Tailscale IP");
        }
    }

    private void NavTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag != null && int.TryParse(elem.Tag.ToString(), out var tabIdx))
        {
            if (ViewModel != null)
            {
                ViewModel.SelectedTabIndex = tabIdx;
                if (tabIdx == 3)
                {
                    ViewModel.RefreshAvailableLogs();
                }
                else if (tabIdx == 4)
                {
                    ViewModel.LoadEnvEditor();
                }
                FocusContentHost();
            }
        }
    }

    private void BtnQuickOpenWeb_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "http://127.0.0.1:3000",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private async void BtnRestartAll_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.RestartAllServicesAsync();
    }

    private void BtnQuickGoLogs_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.SelectedTabIndex = 3;
            ViewModel.RefreshAvailableLogs();
            FocusContentHost();
        }
    }

    private async void BtnStartAll_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.StartAllServicesAsync();
    }

    private async void BtnStopAll_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.StopAllServicesAsync();
    }

    private async void BtnStartService_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ServiceItem item && ViewModel != null)
        {
            await ViewModel.StartServiceAsync(item);
        }
    }

    private async void BtnStopService_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ServiceItem item && ViewModel != null)
        {
            await ViewModel.StopServiceAsync(item);
        }
    }

    private async void BtnRestartService_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ServiceItem item && ViewModel != null)
        {
            await ViewModel.RestartServiceAsync(item);
        }
    }

    private void BtnOpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ServiceItem item)
        {
            try
            {
                var url = item.Port == 3000 ? "http://127.0.0.1:3000" :
                          item.Port == 9119 ? "http://127.0.0.1:9119" :
                          item.Port == 8642 ? "http://127.0.0.1:8642/health" :
                          $"http://127.0.0.1:{item.Port}";
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    private void BtnGoToQr_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.SelectedTabIndex = 1;
            FocusContentHost();
        }
    }

    private void BtnCopyDeepLink_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.CopyToClipboard(ViewModel.DeepLink, "Deep Link");
    }

    private void BtnCopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.CopyToClipboard(ViewModel.ConnectCode, "Connect Code");
    }

    private void BtnRegen24h_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.RegenerateConnectQr(24);
    }

    private void BtnRegen7d_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.RegenerateConnectQr(24 * 7);
    }

    private void BtnRegen30d_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.RegenerateConnectQr(24 * 30);
    }

    private void BtnRegenNever_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.RegenerateConnectQr(0);
    }

    private async void BtnStartInstall_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.StartInstallAsync();
            TxtInstallerLogs.Focus();
        }
    }

    private void BtnCancelInstall_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.CancelInstall();
    }

    private void TxtInstallerLogs_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtInstallerLogs.ScrollToEnd();
    }

    private void BtnCopyInstallerLogs_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.CopyToClipboard(ViewModel.InstallerLogText, "Installer Logs");
        }
    }

    private void BtnClearInstallerLogs_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.InstallerLogText = "";
        }
    }

    private void BtnRefreshLogs_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.RefreshAvailableLogs();
    }

    private void BtnOpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.OpenLogsFolder();
    }

    private void BtnSaveEnv_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.SaveEnvEditor();
    }

    private async void BtnRefreshDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) await ViewModel.RefreshDiagnosticsAsync();
    }

    private async void BtnCleanReinstall_Click(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(
            "Clean Reinstall will terminate all running Hermes processes and re-deploy all modules from scratch.\r\n\r\nDo you wish to proceed?",
            "Confirm Clean Reinstallation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (res == MessageBoxResult.Yes && ViewModel != null)
        {
            await ViewModel.StartInstallAsync(isCleanReinstall: true);
            TxtInstallerLogs.Focus();
        }
    }

    private async void BtnFullUninstall_Click(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(
            "FULL UNINSTALL will permanently remove Hermes from this computer:\r\n\r\n" +
            "• Stop and remove all Hermes services and autostart tasks\r\n" +
            "• Remove firewall rules (ports 3000 / 8642 / 9119)\r\n" +
            "• Delete shortcuts and startup entries\r\n" +
            "• DELETE ALL DATA: %LOCALAPPDATA%\\hermes (config, .env secrets, logs, connect.html), MemOS and Obsidian Skills data\r\n\r\n" +
            "This cannot be undone. Do you wish to proceed?",
            "Confirm Full Uninstall",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (res == MessageBoxResult.Yes && ViewModel != null)
        {
            await ViewModel.StartUninstallAsync();
            TxtInstallerLogs.Focus();
        }
    }

    private void BtnWizardNext_Click(object sender, RoutedEventArgs e) => ViewModel?.NextSetupWizardStep();

    private void BtnWizardBack_Click(object sender, RoutedEventArgs e) => ViewModel?.PrevSetupWizardStep();

    private void BtnWizardPrev_Click(object sender, RoutedEventArgs e) => ViewModel?.PrevSetupWizardStep();

    private void WizardStepTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag != null && int.TryParse(fe.Tag.ToString(), out var step))
        {
            if (ViewModel != null)
            {
                ViewModel.SetupWizardStep = step;
                FocusContentHost();
            }
        }
    }

    private void BtnOpenErrorReport_Click(object sender, RoutedEventArgs e) => ViewModel?.OpenInstallErrorReport();

    private async void BtnRetryInstall_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.StartInstallAsync();
            TxtInstallerLogs.Focus();
        }
    }

    private async void BtnApplyProvider_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.ApplyProviderConfigAsync();
        }
    }

    private void ProviderApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingProviderSecret || ViewModel == null || sender is not PasswordBox box) return;
        ViewModel.ProviderApiKey = box.Password;
        SyncSecretFieldsFromViewModel();
    }

    private void BtnToggleProviderApiKeyReveal_Click(object sender, RoutedEventArgs e)
    {
        _providerKeyRevealed = !_providerKeyRevealed;
        ProviderApiKeyPasswordBox.Visibility = _providerKeyRevealed ? Visibility.Collapsed : Visibility.Visible;
        ProviderApiKeyPasswordBoxEnv.Visibility = _providerKeyRevealed ? Visibility.Collapsed : Visibility.Visible;
        ProviderApiKeyRevealTextBox.Visibility = _providerKeyRevealed ? Visibility.Visible : Visibility.Collapsed;
        ProviderApiKeyRevealTextBoxEnv.Visibility = _providerKeyRevealed ? Visibility.Visible : Visibility.Collapsed;
        if (sender is Button btn) btn.Content = _providerKeyRevealed ? "Hide" : "Show";
        ViewModel?.ShowToast(_providerKeyRevealed ? "Sensitive provider key is visible on screen." : "Provider key hidden.");
    }

    private void BtnToggleDeepLinkReveal_Click(object sender, RoutedEventArgs e)
    {
        _deepLinkRevealed = !_deepLinkRevealed;
        DeepLinkPasswordBox.Visibility = _deepLinkRevealed ? Visibility.Collapsed : Visibility.Visible;
        DeepLinkRevealTextBox.Visibility = _deepLinkRevealed ? Visibility.Visible : Visibility.Collapsed;
        if (sender is Button btn) btn.Content = _deepLinkRevealed ? "Hide" : "Show";
        ViewModel?.ShowToast(_deepLinkRevealed ? "Pairing deep link is visible on screen." : "Pairing deep link hidden.");
    }

    private void BtnToggleConnectCodeReveal_Click(object sender, RoutedEventArgs e)
    {
        _connectCodeRevealed = !_connectCodeRevealed;
        ConnectCodePasswordBox.Visibility = _connectCodeRevealed ? Visibility.Collapsed : Visibility.Visible;
        ConnectCodeRevealTextBox.Visibility = _connectCodeRevealed ? Visibility.Visible : Visibility.Collapsed;
        if (sender is Button btn) btn.Content = _connectCodeRevealed ? "Hide" : "Show";
        ViewModel?.ShowToast(_connectCodeRevealed ? "Pairing code is visible on screen." : "Pairing code hidden.");
    }

    private void BtnCopyVisibleLog_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.CopyToClipboard(ViewModel.LogViewerContent, "Visible Log");
        }
    }

    private void EnvValueTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not EnvEntry entry) return;
        if (!IsSecretLikeKey(entry.Key)) return;

        BindingOperations.ClearBinding(box, TextBox.TextProperty);
        box.Text = string.IsNullOrEmpty(entry.Value) ? "" : "••••••••••••••••";
        box.IsReadOnly = true;
        box.ToolTip = "Secret value is masked. Safe reveal/edit requires a coder-provided secret editing contract.";
        AutomationProperties.SetHelpText(box, "Secret value masked by default");
    }

    private static bool IsSecretLikeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var normalized = key.ToUpperInvariant();
        return normalized.Contains("KEY") || normalized.Contains("TOKEN") || normalized.Contains("SECRET") || normalized.Contains("PASSWORD") || normalized.Contains("PASS");
    }
}
