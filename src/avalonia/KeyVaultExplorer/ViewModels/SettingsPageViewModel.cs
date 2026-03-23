using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using KeyVaultExplorer.Database;
using KeyVaultExplorer.Models;
using KeyVaultExplorer.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KeyVaultExplorer.ViewModels;

public partial class SettingsPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private string version;

    private const string BackgroundTranparency = "BackgroundTransparency";
    private readonly AuthService _authService;
    private readonly KvExplorerDb _dbContext;
    private FluentAvaloniaTheme _faTheme;

    [ObservableProperty]
    private string[] appThemes = ["System", "Light", "Dark"];

    [ObservableProperty]
    private AuthenticatedUserClaims? authenticatedUserClaims;

    [ObservableProperty]
    private int clearClipboardTimeout;

    [ObservableProperty]
    private string currentAppTheme;

    [ObservableProperty]
    private bool isBackgroundTransparencyEnabled;

    [ObservableProperty]
    private ObservableCollection<Settings> settings;

    [ObservableProperty]
    private ObservableCollection<TenantInfo> availableTenants = [];

    [ObservableProperty]
    private TenantInfo selectedTenant;

    private bool _isInitializing = true;

    public SettingsPageViewModel()
    {
        _authService = Defaults.Locator.GetRequiredService<AuthService>();
        _dbContext = Defaults.Locator.GetRequiredService<KvExplorerDb>();
        _faTheme = App.Current.Styles[0] as FluentAvaloniaTheme;
        Dispatcher.UIThread.Invoke(async () =>
        {
            Version = GetAppVersion();
            var jsonSettings = await GetAppSettings();
            ClearClipboardTimeout = jsonSettings.ClipboardTimeout;
            IsBackgroundTransparencyEnabled = jsonSettings.BackgroundTransparency;
            CurrentAppTheme = jsonSettings.AppTheme ?? "System";

            // Sync tenant list from MainViewModel
            var mainVm = Defaults.Locator.GetRequiredService<MainViewModel>();
            AvailableTenants = mainVm.AvailableTenants;
            SelectedTenant = mainVm.SelectedTenant;

            // Keep in sync when main changes
            mainVm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.AvailableTenants))
                    AvailableTenants = mainVm.AvailableTenants;
                else if (e.PropertyName == nameof(MainViewModel.SelectedTenant))
                {
                    _isInitializing = true;
                    SelectedTenant = mainVm.SelectedTenant;
                    _isInitializing = false;
                }
            };

            _isInitializing = false;
        }, DispatcherPriority.MaxValue);
    }

    public static string GetAppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version == null ? "(Unknown)" : $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }

    public async Task AddOrUpdateAppSettings<T>(string key, T value)
    {
        var path = Path.Combine(Constants.LocalAppDataFolder, "settings.json");
        var records = await GetAppSettings();
        // Assuming records is a class with a property that matches the key
        var property = records.GetType().GetProperty(key);
        if (property != null && property.PropertyType == typeof(T))
        {
            property.SetValue(records, value);
            var newJson = JsonSerializer.Serialize(records);

            FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(fs);
            writer.WriteLine(newJson);
        }
    }

    public async Task<AppSettings> GetAppSettings()
    {
        var path = Path.Combine(Constants.LocalAppDataFolder, "settings.json");
        using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream);
    }

    [RelayCommand]
    private async Task SetBackgroundColorSetting()
    {
        await AddOrUpdateAppSettings(BackgroundTranparency, IsBackgroundTransparencyEnabled);
    }

    partial void OnCurrentAppThemeChanging(string? oldValue, string newValue)
    {
        if (oldValue is not null && oldValue != newValue)
            Dispatcher.UIThread.InvokeAsync(async () => await AddOrUpdateAppSettings(nameof(AppSettings.AppTheme), CurrentAppTheme), DispatcherPriority.Background);
    }

    partial void OnSelectedTenantChanged(TenantInfo value)
    {
        if (_isInitializing) return;
        if (value is not null && value.TenantId != _authService.TenantId)
        {
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var mainVm = Defaults.Locator.GetRequiredService<MainViewModel>();
                await mainVm.SwitchTenantCommand.ExecuteAsync(value);
                AuthenticatedUserClaims = _authService.AuthenticatedUserClaims;
            }, DispatcherPriority.Background);
        }
    }

    partial void OnClearClipboardTimeoutChanging(int oldValue, int newValue)
    {
        if (oldValue != 0 && oldValue != newValue)
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await Task.Delay(50); // TOOD: figure out a way to get the value without having to wait for it to propagate.
                await AddOrUpdateAppSettings(nameof(AppSettings.ClipboardTimeout), ClearClipboardTimeout);
            }, DispatcherPriority.Background);
    }

    [RelayCommand]
    private async Task SetSplitViewDisplayMode(string splitViewDisplayMode)
    {
        await AddOrUpdateAppSettings(nameof(AppSettings.SplitViewDisplayMode), splitViewDisplayMode);
    }

    [RelayCommand]
    private async Task RefreshCliStatus()
    {
        var available = await _authService.CheckAzureCliAvailableAsync();
        if (!available)
            return;

        var initialized = await _authService.InitializeAsync();
        if (!initialized)
            return;
        AuthenticatedUserClaims = _authService.AuthenticatedUserClaims;

        // Refresh tenant list
        var tenants = await _authService.DiscoverTenantsAsync();
        AvailableTenants = new ObservableCollection<TenantInfo>(tenants);
        if (!string.IsNullOrEmpty(_authService.TenantId))
        {
            SelectedTenant = AvailableTenants.FirstOrDefault(t => t.TenantId == _authService.TenantId);
        }
    }

    [RelayCommand]
    private async Task SignOut()
    {
        _authService.ClearState();
        AuthenticatedUserClaims = _authService.AuthenticatedUserClaims;
    }

    [RelayCommand]
    private void OpenIssueGithub()
    {
        Process.Start(new ProcessStartInfo("https://github.com/cricketthomas/KeyVaultExplorer/issues/new") { UseShellExecute = true, Verb = "open" });
    }


    [RelayCommand]
    private Task DeleteDatabase()
    {
        _ = _dbContext.DropTablesAndRecreate();
        return Task.CompletedTask;
    }

}
