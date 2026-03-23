using Azure.Core;
using Azure.Identity;
using KeyVaultExplorer.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KeyVaultExplorer.Services;

public class AuthService
{
    public bool IsAuthenticated { get; private set; } = false;

    public AuthenticatedUserClaims AuthenticatedUserClaims { get; private set; }

    public string TenantName { get; private set; }

    public string TenantId { get; private set; }

    public TokenCredential Credential { get; private set; }

    public AuthService()
    {
    }

    /// <summary>
    /// Checks if the Azure CLI is installed and available on the PATH.
    /// </summary>
    public async Task<bool> CheckAzureCliAvailableAsync()
    {
        try
        {
            var (exitCode, _) = await RunAzCommandAsync("--version");
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Initializes auth state by reading the current az CLI account.
    /// Creates an AzureCliCredential for the current or saved tenant.
    /// </summary>
    public async Task<bool> InitializeAsync()
    {
        try
        {
            var settings = Defaults.Locator.GetRequiredService<AppSettingReader>();
            var savedTenantId = settings.AppSettings.SelectedTenantId;

            if (!string.IsNullOrEmpty(savedTenantId))
            {
                await SwitchTenantAsync(savedTenantId);
                return IsAuthenticated;
            }

            var (exitCode, output) = await RunAzCommandAsync("account show --output json");
            if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
                return false;

            var accountInfo = JsonDocument.Parse(output);
            var root = accountInfo.RootElement;

            var tenantId = root.GetProperty("tenantId").GetString();
            var userName = root.TryGetProperty("user", out var user)
                ? user.GetProperty("name").GetString()
                : "Unknown";
            var tenantDisplayName = root.TryGetProperty("tenantDisplayName", out var tdn)
                ? tdn.GetString()
                : tenantId;

            TenantId = tenantId;
            TenantName = tenantDisplayName;
            Credential = new AzureCliCredential(new AzureCliCredentialOptions
            {
                TenantId = tenantId
            });

            AuthenticatedUserClaims = new AuthenticatedUserClaims
            {
                Username = userName,
                TenantId = tenantId,
                Email = userName,
                Name = userName,
                TenantDisplayName = tenantDisplayName
            };

            IsAuthenticated = true;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AuthService.InitializeAsync failed: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Discovers all tenants the user has access to via az account list.
    /// </summary>
    public async Task<List<TenantInfo>> DiscoverTenantsAsync()
    {
        try
        {
            var (exitCode, output) = await RunAzCommandAsync("account list --query \"[].{tenantId:tenantId, name:tenantDisplayName}\" --output json");
            if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
                return [];

            var tenants = JsonSerializer.Deserialize<List<TenantDiscoveryEntry>>(output, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (tenants is null)
                return [];

            // Deduplicate by tenantId and build TenantInfo list
            return tenants
                .Where(t => !string.IsNullOrEmpty(t.TenantId))
                .GroupBy(t => t.TenantId)
                .Select(g => new TenantInfo(g.Key, g.First().Name ?? g.Key))
                .OrderBy(t => t.DisplayName)
                .ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AuthService.DiscoverTenantsAsync failed: {ex}");
            return [];
        }
    }

    /// <summary>
    /// Switches to a different tenant by creating a new AzureCliCredential scoped to that tenant.
    /// </summary>
    public async Task SwitchTenantAsync(string tenantId)
    {
        TenantId = tenantId;
        Credential = new AzureCliCredential(new AzureCliCredentialOptions
        {
            TenantId = tenantId
        });

        // Try to get account info for this tenant
        try
        {
            var (exitCode, output) = await RunAzCommandAsync($"account show --tenant {tenantId} --output json");
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var accountInfo = JsonDocument.Parse(output);
                var root = accountInfo.RootElement;

                var userName = root.TryGetProperty("user", out var user)
                    ? user.GetProperty("name").GetString()
                    : "Unknown";
                var tenantDisplayName = root.TryGetProperty("tenantDisplayName", out var tdn)
                    ? tdn.GetString()
                    : tenantId;

                TenantName = tenantDisplayName;
                AuthenticatedUserClaims = new AuthenticatedUserClaims
                {
                    Username = userName,
                    TenantId = tenantId,
                    Email = userName,
                    Name = userName,
                    TenantDisplayName = tenantDisplayName
                };
                IsAuthenticated = true;
            }
            else
            {
                // Credential is set but we couldn't get account info -- user may need to az login
                TenantName = tenantId;
                AuthenticatedUserClaims = new AuthenticatedUserClaims
                {
                    Username = "Not logged in",
                    TenantId = tenantId,
                    Email = "Not logged in",
                    Name = "Not logged in",
                    TenantDisplayName = tenantId
                };
                IsAuthenticated = false;
            }
        }
        catch
        {
            TenantName = tenantId;
            IsAuthenticated = false;
        }
    }

    /// <summary>
    /// Launches az login interactively for the specified tenant.
    /// Opens the browser for authentication (similar to kubelogin interactive).
    /// </summary>
    public async Task<bool> LaunchAzLoginAsync(string? tenantId = null)
    {
        try
        {
            var args = "login";
            if (!string.IsNullOrEmpty(tenantId))
                args += $" --tenant {tenantId}";

            var psi = new ProcessStartInfo
            {
                FileName = GetAzCliPath(),
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false
            };

            using var process = Process.Start(psi);
            if (process is null)
                return false;

            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                // Re-initialize after successful login
                if (!string.IsNullOrEmpty(tenantId))
                    await SwitchTenantAsync(tenantId);
                else
                    await InitializeAsync();
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AuthService.LaunchAzLoginAsync failed: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Clears authentication state. Replaces the old RemoveAccount().
    /// </summary>
    public void ClearState()
    {
        IsAuthenticated = false;
        AuthenticatedUserClaims = null;
        TenantId = null;
        TenantName = null;
        Credential = null;
    }

    private static string GetAzCliPath()
    {
        if (OperatingSystem.IsWindows())
            return "az.cmd";

        // On macOS/Linux, try common paths
        var paths = new[] { "/usr/local/bin/az", "/opt/homebrew/bin/az", "az" };
        foreach (var path in paths)
        {
            if (System.IO.File.Exists(path))
                return path;
        }
        return "az"; // fallback to PATH
    }

    public static async Task<(int ExitCode, string Output)> RunAzCommandAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = GetAzCliPath(),
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
            return (-1, string.Empty);

        // Read both stdout and stderr concurrently to avoid deadlocks
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await Task.WhenAll(outputTask, errorTask);
        await process.WaitForExitAsync();

        return (process.ExitCode, outputTask.Result);
    }

    private class TenantDiscoveryEntry
    {
        public string TenantId { get; set; }
        public string Name { get; set; }
    }
}
