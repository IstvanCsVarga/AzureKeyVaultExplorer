using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using KeyVaultExplorer.Models;
using KeyVaultExplorer.Services;
using KeyVaultExplorer.Views.Pages;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KeyVaultExplorer.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private AuthenticatedUserClaims authenticatedUserClaims;

    [ObservableProperty]
    private bool isAuthenticated = false;

    [ObservableProperty]
    private ObservableCollection<TenantInfo> availableTenants = [];

    [ObservableProperty]
    private TenantInfo selectedTenant;

    [ObservableProperty]
    private bool isLoggingIn = false;

    private readonly AuthService _authService;
    private readonly NotificationViewModel _notificationViewModel;
    private bool _suppressTenantSwitch = false;

    public NavigationFactory NavigationFactory { get; }

    partial void OnIsAuthenticatedChanged(bool value)
    {
        AuthenticatedUserClaims = _authService.AuthenticatedUserClaims;
    }

    partial void OnSelectedTenantChanged(TenantInfo value)
    {
        if (_suppressTenantSwitch || value is null) return;
        // Trigger switch if different tenant OR if same tenant but not authenticated
        if (value.TenantId != _authService.TenantId || !_authService.IsAuthenticated)
        {
            _ = SwitchTenantCommand.ExecuteAsync(value);
        }
    }

    /// <summary>
    /// Called by the refresh button to retry auth + reload if not authenticated.
    /// </summary>
    public async Task RetryAuthAndRefresh()
    {
        if (!_authService.IsAuthenticated && SelectedTenant is not null)
        {
            await SwitchTenantCommand.ExecuteAsync(SelectedTenant);
        }
        else
        {
            var treeVm = Defaults.Locator.GetRequiredService<KeyVaultTreeListViewModel>();
            await treeVm.GetAvailableKeyVaultsCommand.ExecuteAsync(true);
        }
    }

    public MainViewModel()
    {
        _authService = Defaults.Locator.GetRequiredService<AuthService>();
        _notificationViewModel = Defaults.Locator.GetRequiredService<NotificationViewModel>();
        NavigationFactory = new NavigationFactory();
    }

    public async Task RefreshTokenAndGetAccountInformation()
    {
        var cliAvailable = await _authService.CheckAzureCliAvailableAsync();
        if (!cliAvailable)
        {
            _notificationViewModel.AddMessage(new Notification
            {
                Title = "Azure CLI Not Found",
                Message = "Install Azure CLI from https://aka.ms/installazurecli",
                Type = NotificationType.Error
            });
            return;
        }

        // Always discover tenants if CLI is available
        var tenants = await _authService.DiscoverTenantsAsync();
        AvailableTenants = new ObservableCollection<TenantInfo>(tenants);

        var initialized = await _authService.InitializeAsync();

        AuthenticatedUserClaims = _authService.AuthenticatedUserClaims;
        IsAuthenticated = _authService.IsAuthenticated;

        // Set current tenant as selected (suppress to avoid re-triggering switch)
        _suppressTenantSwitch = true;
        if (!string.IsNullOrEmpty(_authService.TenantId))
        {
            SelectedTenant = AvailableTenants.FirstOrDefault(t => t.TenantId == _authService.TenantId);
        }
        _suppressTenantSwitch = false;

        if (initialized)
        {
            _notificationViewModel.AddMessage(new Notification
            {
                Title = "Signed In",
                Message = $"Authenticated as {_authService.AuthenticatedUserClaims?.Email ?? "unknown"} in {_authService.TenantName}",
                Type = NotificationType.Success
            });
        }
        else
        {
            _notificationViewModel.AddMessage(new Notification
            {
                Title = "Not Signed In",
                Message = "Use Account > Azure CLI Login to sign in",
                Type = NotificationType.Warning
            });
        }
    }

    [RelayCommand]
    private async Task ForceSignIn()
    {
        _notificationViewModel.AddMessage(new Notification
        {
            Title = "Signing In",
            Message = "Opening browser for Azure CLI login...",
            Type = NotificationType.Information
        });

        var tenantId = _authService.TenantId;
        var success = await _authService.LaunchAzLoginAsync(tenantId);
        if (success)
        {
            AuthenticatedUserClaims = _authService.AuthenticatedUserClaims;
            IsAuthenticated = _authService.IsAuthenticated;

            // Refresh tenant list
            var tenants = await _authService.DiscoverTenantsAsync();
            AvailableTenants = new ObservableCollection<TenantInfo>(tenants);

            _suppressTenantSwitch = true;
            if (!string.IsNullOrEmpty(_authService.TenantId))
                SelectedTenant = AvailableTenants.FirstOrDefault(t => t.TenantId == _authService.TenantId);
            _suppressTenantSwitch = false;

            _notificationViewModel.AddMessage(new Notification
            {
                Title = "Login Successful",
                Message = $"Signed in as {_authService.AuthenticatedUserClaims?.Email ?? "unknown"}",
                Type = NotificationType.Success
            });

            // Reload the tree view
            var treeVm = Defaults.Locator.GetRequiredService<KeyVaultTreeListViewModel>();
            await treeVm.GetAvailableKeyVaultsCommand.ExecuteAsync(true);
        }
        else
        {
            _notificationViewModel.AddMessage(new Notification
            {
                Title = "Login Failed",
                Message = "Azure CLI login did not complete successfully",
                Type = NotificationType.Error
            });
        }
    }

    [RelayCommand]
    private async Task SignOut()
    {
        _authService.ClearState();
        AuthenticatedUserClaims = null;
        IsAuthenticated = false;
        AvailableTenants = [];
        SelectedTenant = null;

        _notificationViewModel.AddMessage(new Notification
        {
            Title = "Signed Out",
            Message = "Session cleared",
            Type = NotificationType.Information
        });
    }

    [RelayCommand]
    private async Task SwitchTenant(TenantInfo tenant)
    {
        if (tenant is null)
            return;

        try
        {
            IsLoggingIn = true;
            _notificationViewModel.AddMessage(new Notification
            {
                Title = "Switching Tenant",
                Message = $"Connecting to {tenant.DisplayName}...",
                Type = NotificationType.Information
            });

            await _authService.SwitchTenantAsync(tenant.TenantId);

            AuthenticatedUserClaims = _authService.AuthenticatedUserClaims;
            IsAuthenticated = _authService.IsAuthenticated;
            _suppressTenantSwitch = true;
            SelectedTenant = tenant;
            _suppressTenantSwitch = false;

            // Persist selection
            var settingsVm = Defaults.Locator.GetRequiredService<SettingsPageViewModel>();
            await settingsVm.AddOrUpdateAppSettings(nameof(AppSettings.SelectedTenantId), tenant.TenantId);

            if (!_authService.IsAuthenticated)
            {
                _notificationViewModel.AddMessage(new Notification
                {
                    Title = "Login Required",
                    Message = $"Opening browser to sign in to {tenant.DisplayName}...",
                    Type = NotificationType.Information
                });

                var loginSuccess = await _authService.LaunchAzLoginAsync(tenant.TenantId);
                if (!loginSuccess)
                {
                    _notificationViewModel.AddMessage(new Notification
                    {
                        Title = "Login Failed",
                        Message = $"Could not authenticate to {tenant.DisplayName}",
                        Type = NotificationType.Error
                    });
                    return;
                }

                AuthenticatedUserClaims = _authService.AuthenticatedUserClaims;
                IsAuthenticated = _authService.IsAuthenticated;
            }

            // Reload the tree view for the new tenant
            var treeVm = Defaults.Locator.GetRequiredService<KeyVaultTreeListViewModel>();
            await treeVm.GetAvailableKeyVaultsCommand.ExecuteAsync(true);

            _notificationViewModel.AddMessage(new Notification
            {
                Title = "Tenant Switched",
                Message = $"Now connected to {tenant.DisplayName}",
                Type = NotificationType.Success
            });
        }
        catch (Exception ex)
        {
            _notificationViewModel.AddMessage(new Notification
            {
                Title = "Tenant Switch Error",
                Message = ex.Message,
                Type = NotificationType.Error
            });
        }
        finally
        {
            IsLoggingIn = false;
        }
    }
}
