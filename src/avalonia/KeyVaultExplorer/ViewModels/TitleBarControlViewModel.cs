using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeyVaultExplorer.Services;
using System.Threading;
using System.Threading.Tasks;

namespace KeyVaultExplorer.ViewModels;

public partial class TitleBarViewModel : ViewModelBase
{
    private readonly AuthService _authService;

    public TitleBarViewModel(AuthService authService, VaultService vaultService)
    {
        _authService = authService;
    }

    [ObservableProperty]
    private string title = "Key Vault Explorer for Azure";

    public TitleBarViewModel()
    {
        _authService = new AuthService();
    }

    [RelayCommand]
    private async void SignIn()
    {
        var initialized = await _authService.InitializeAsync();
        if (!initialized)
            await _authService.LaunchAzLoginAsync();
    }

    [RelayCommand]
    private async Task SignOut()
    {
        _authService.ClearState();
    }
}
